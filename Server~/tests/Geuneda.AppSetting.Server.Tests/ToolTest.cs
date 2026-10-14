using System;
using System.IO;
using System.Linq;
using Geuneda.AppSetting.Tool;
using Xunit;

namespace Geuneda.AppSetting.Server.Tests
{
	public sealed class ToolTest : IDisposable
	{
		private readonly string _folder = TestKeys.NewFolder();

		public void Dispose()
		{
			TestKeys.DeleteFolder(_folder);
		}

		private string PathOf(string name)
		{
			return Path.Combine(_folder, name);
		}

		private static (int Code, string Output, string Error) Run(params string[] args)
		{
			var output = new StringWriter();
			var error = new StringWriter();
			var code = ToolCommands.Run(args, output, error);
			return (code, output.ToString(), error.ToString());
		}

		private void WriteKey(string name, AppSettingSigningKey key)
		{
			File.WriteAllText(PathOf(name), key.ExportPrivateKeyPem());
		}

		private void WritePublicKeys(string name, params AppSettingSigningKey[] keys)
		{
			File.WriteAllText(PathOf(name), AppSettingPublicKeys.ToJson(keys.Select(key => key.PublicKey)));
		}

		[Fact]
		public void KeyGen_WritesPrivateKeyOnlyForTheOwner_PrintsPublicKey_AndNeverOverwrites()
		{
			var keyPath = PathOf("key.pem");

			var result = Run("keygen", "--key-id", "2026-10", "--out", keyPath);

			Assert.Equal(ToolCommands.Ok, result.Code);
			using var key = AppSettingSigningKey.FromPem("2026-10", File.ReadAllText(keyPath));
			Assert.Equal(3072, key.PublicKey.KeySizeBits);
			Assert.Contains(AppSettingPublicKeys.ToCSharp(key.PublicKey), result.Output);
			Assert.Contains(key.PublicKey.Modulus, result.Output);

			if (!OperatingSystem.IsWindows())
			{
				Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(keyPath));
			}

			var before = File.ReadAllText(keyPath);
			var again = Run("keygen", "--key-id", "2026-10", "--out", keyPath);

			Assert.Equal(ToolCommands.Failed, again.Code);
			Assert.Equal(before, File.ReadAllText(keyPath));

			var printed = Run("public-key", "--key", keyPath, "--key-id", "2026-10");
			Assert.Equal(ToolCommands.Ok, printed.Code);
			Assert.Contains(AppSettingPublicKeys.ToCSharp(key.PublicKey), printed.Output);
		}

		[Fact]
		public void Sign_ThenVerify_WithBothKeys()
		{
			WriteKey("k1.pem", TestKeys.First);
			WriteKey("k2.pem", TestKeys.Second);
			WritePublicKeys("old.json", TestKeys.First);
			WritePublicKeys("new.json", TestKeys.Second);
			File.WriteAllBytes(PathOf("payload.json"), TestKeys.Payload(3, extra: "\"endpoints\":{\"game\":\"https://game.test/\"}"));

			var signed = Run("sign", "--payload", PathOf("payload.json"), "--key", PathOf("k1.pem"), "--key-id", "k1",
				"--key", PathOf("k2.pem"), "--key-id", "k2", "--out", PathOf("appsetting.json"));
			var withOld = Run("verify", "--document", PathOf("appsetting.json"), "--public-keys", PathOf("old.json"), "--strict");
			var withNew = Run("verify", "--document", PathOf("appsetting.json"), "--public-keys", PathOf("new.json"));

			Assert.Equal(ToolCommands.Ok, signed.Code);
			Assert.Contains("Signed revision 3 (1.0.0) with k1, k2", signed.Output);
			Assert.Equal(ToolCommands.Ok, withOld.Code);
			Assert.Contains("revision 3", withOld.Output);
			Assert.Contains("1.0.0: android=open; endpoints game", withOld.Output);
			Assert.Equal(ToolCommands.Ok, withNew.Code);
			Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
		}

		[Fact]
		public void Sign_InvalidPayload_FailsAndWritesNothing()
		{
			WriteKey("k1.pem", TestKeys.First);
			File.WriteAllText(PathOf("payload.json"), "{\"revision\":1,\"versions\":{\"*\":{\"platforms\":{\"android\":\"opne\"}}}}");

			var result = Run("sign", "--payload", PathOf("payload.json"), "--key", PathOf("k1.pem"), "--key-id", "k1", "--out", PathOf("appsetting.json"));

			Assert.Equal(ToolCommands.Failed, result.Code);
			Assert.Contains("opne", result.Error);
			Assert.False(File.Exists(PathOf("appsetting.json")));
		}

		[Fact]
		public void Sign_LowerRevisionThanTheExistingDocument_FailsAndKeepsIt_EqualRevisionWarns()
		{
			WriteKey("k1.pem", TestKeys.First);
			File.WriteAllBytes(PathOf("appsetting.json"), TestKeys.Document(5));
			var existing = File.ReadAllBytes(PathOf("appsetting.json"));

			File.WriteAllBytes(PathOf("payload.json"), TestKeys.Payload(4));
			var lower = Run("sign", "--payload", PathOf("payload.json"), "--key", PathOf("k1.pem"), "--key-id", "k1", "--out", PathOf("appsetting.json"));

			Assert.Equal(ToolCommands.Failed, lower.Code);
			Assert.Contains("lower than 5", lower.Error);
			Assert.Equal(existing, File.ReadAllBytes(PathOf("appsetting.json")));

			File.WriteAllBytes(PathOf("payload.json"), TestKeys.Payload(5, "closed"));
			var equal = Run("sign", "--payload", PathOf("payload.json"), "--key", PathOf("k1.pem"), "--key-id", "k1", "--out", PathOf("appsetting.json"));

			Assert.Equal(ToolCommands.Ok, equal.Code);
			Assert.Contains("Warning", equal.Error);
		}

		[Fact]
		public async System.Threading.Tasks.Task Sign_WaitsForAnotherSign_ThenComparesWithTheRevisionItLeft()
		{
			// 다른 서명이 잠금을 잡은 채 r12 를 써 두면, 그 전에 r10 을 보고 출발한 r11 서명은 잠금 뒤에 다시 읽어 거절한다
			WriteKey("k1.pem", TestKeys.First);
			File.WriteAllBytes(PathOf("appsetting.json"), TestKeys.Document(10));
			File.WriteAllBytes(PathOf("payload.json"), TestKeys.Payload(11));
			var output = new StringWriter();
			var error = new StringWriter();
			System.Threading.Tasks.Task<int> signing;

			using (ToolCommands.AcquireLock(PathOf("appsetting.json"), TextWriter.Null))
			{
				signing = System.Threading.Tasks.Task.Run(() => ToolCommands.Run(new[]
				{
					"sign", "--payload", PathOf("payload.json"), "--key", PathOf("k1.pem"), "--key-id", "k1", "--out", PathOf("appsetting.json")
				}, output, error));

				await System.Threading.Tasks.Task.Delay(500);
				Assert.False(signing.IsCompleted);
				File.WriteAllBytes(PathOf("appsetting.json"), TestKeys.Document(12));
			}

			Assert.Equal(ToolCommands.Failed, await signing);
			Assert.Contains("Waiting for another sign", error.ToString());
			Assert.Contains("lower than 12", error.ToString());
			Assert.Equal(12, AppSettingSigner.PeekRevision(File.ReadAllBytes(PathOf("appsetting.json"))));

			// 잠금이 풀린 뒤의 서명은 기다리지 않는다
			File.WriteAllBytes(PathOf("payload.json"), TestKeys.Payload(13));
			var next = Run("sign", "--payload", PathOf("payload.json"), "--key", PathOf("k1.pem"), "--key-id", "k1", "--out", PathOf("appsetting.json"));
			Assert.Equal(ToolCommands.Ok, next.Code);
			Assert.DoesNotContain("Waiting", next.Error);
		}

		[Fact]
		public void Verify_UntrustedOrStrictFailures_ExitWithOne()
		{
			File.WriteAllBytes(PathOf("appsetting.json"), TestKeys.Document(1, key: TestKeys.Second));
			WritePublicKeys("keys.json", TestKeys.First);

			var result = Run("verify", "--document", PathOf("appsetting.json"), "--public-keys", PathOf("keys.json"));

			Assert.Equal(ToolCommands.Failed, result.Code);
			Assert.Contains($"({AppSettingResultCodes.InvalidSignature})", result.Error);
		}

		[Theory]
		[InlineData("")]
		[InlineData("publish")]
		[InlineData("keygen --key-id")]
		[InlineData("keygen --key-id a --out x.pem --bits 4096")]
		[InlineData("keygen --key-id a --key-id b --out x.pem")]
		[InlineData("keygen stray")]
		[InlineData("sign --payload p.json --key k.pem --out o.json")]
		[InlineData("verify --document d.json")]
		public void UsageErrors_ExitWithTwo_AndPrintUsage(string args)
		{
			var result = Run(args.Split(' ', StringSplitOptions.RemoveEmptyEntries));

			Assert.Equal(ToolCommands.UsageError, result.Code);
			Assert.Contains("Usage:", result.Output + result.Error);
		}

		[Fact]
		public void Help_ExitsWithZero()
		{
			Assert.Equal(ToolCommands.Ok, Run("--help").Code);
		}
	}
}
