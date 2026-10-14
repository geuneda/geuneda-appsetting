using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Geuneda.AppSetting.Server;

namespace Geuneda.AppSetting.Tool
{
	/// <summary>서명 CLI 명령입니다. 종료 코드: 0 성공, 1 실패(문서·키·파일 오류), 2 사용법 오류.</summary>
	internal static class ToolCommands
	{
		public const int Ok = 0;
		public const int Failed = 1;
		public const int UsageError = 2;

		/// <summary>다른 서명의 잠금을 기다리는 최대 시간입니다.</summary>
		internal static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

		public const string Usage = @"Usage: geuneda-appsetting <command> [options]

Commands:
  keygen      --key-id <id> --out <private-key.pem>
              Create an RSA-3072 signing key (file mode 600, never overwritten) and print its public key.
  public-key  --key <private-key.pem> --key-id <id>
              Print the public key of an existing signing key.
  sign        --payload <payload.json> --key <private-key.pem> --key-id <id> [--key <pem> --key-id <id> ...] --out <document.json>
              Check the payload strictly, sign it with every key and write the document atomically.
              Refuses a revision lower than the one already in --out. Concurrent signs of the same --out
              wait for each other through a .<file>.lock file next to it (left in place; do not deploy it).
  resource    --category <name> --version <version> --url <url> --file <file>
              Print the payload resources entry for a file to upload (its size and SHA-256).
  verify      --document <document.json> --public-keys <public-keys.json> [--strict]
              Verify the signature and payload as clients do (or strictly) and print a summary.
";

		public static int Run(string[] args, TextWriter output, TextWriter error)
		{
			if (args.Length == 0 || args[0] == "help" || args[0] == "--help" || args[0] == "-h")
			{
				output.Write(Usage);
				return args.Length == 0 ? UsageError : Ok;
			}

			try
			{
				var options = ToolArguments.Parse(args.Skip(1).ToArray());

				switch (args[0])
				{
					case "keygen":
						return KeyGen(options, output);
					case "public-key":
						return PublicKey(options, output);
					case "sign":
						return Sign(options, output, error);
					case "resource":
						return Resource(options, output);
					case "verify":
						return Verify(options, output);
					default:
						throw new ToolUsageException($"Unknown command '{args[0]}'");
				}
			}
			catch (ToolUsageException exception)
			{
				error.WriteLine(exception.Message);
				error.Write(Usage);
				return UsageError;
			}
			catch (AppSettingDocumentException exception)
			{
				error.WriteLine($"Invalid document ({exception.Code}): {exception.Message}");
				return Failed;
			}
			catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
				|| exception is ArgumentException || exception is FormatException)
			{
				error.WriteLine(exception.Message);
				return Failed;
			}
		}

		private static int KeyGen(ToolArguments options, TextWriter output)
		{
			options.RejectUnknown("--key-id", "--out");
			var keyId = options.Single("--key-id");
			var path = options.Single("--out");

			using (var key = AppSettingSigningKey.Generate(keyId))
			{
				// 이미 있는 키를 덮어쓰면 그 키로 서명한 문서를 믿는 앱이 새 문서를 거절한다
				var file = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };

				if (!OperatingSystem.IsWindows())
				{
					file.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
				}

				using (var writer = new StreamWriter(path, file))
				{
					writer.Write(key.ExportPrivateKeyPem());
				}

				output.WriteLine($"Private key '{keyId}' written to {path}. Keep it in a secret store, never in the repository.");
				PrintPublicKey(key.PublicKey, output);
			}

			return Ok;
		}

		private static int PublicKey(ToolArguments options, TextWriter output)
		{
			options.RejectUnknown("--key", "--key-id");

			using (var key = AppSettingSigningKey.FromPem(options.Single("--key-id"), File.ReadAllText(options.Single("--key"))))
			{
				PrintPublicKey(key.PublicKey, output);
			}

			return Ok;
		}

		private static int Sign(ToolArguments options, TextWriter output, TextWriter error)
		{
			options.RejectUnknown("--payload", "--key", "--key-id", "--out");
			var keyPaths = options.All("--key");
			var keyIds = options.All("--key-id");

			if (keyPaths.Count == 0 || keyPaths.Count != keyIds.Count)
			{
				throw new ToolUsageException("Give --key and --key-id in pairs (at least one)");
			}

			var payload = File.ReadAllBytes(options.Single("--payload"));
			var outPath = options.Single("--out");
			var keys = new List<AppSettingSigningKey>();

			try
			{
				for (var i = 0; i < keyPaths.Count; i++)
				{
					keys.Add(AppSettingSigningKey.FromPem(keyIds[i], File.ReadAllText(keyPaths[i])));
				}

				var document = AppSettingSigner.Sign(payload, keys);
				var signed = AppSettingSigner.Verify(document, keys.Select(key => key.PublicKey), true);

				// 판 비교와 교체를 한 잠금 안에서 한다. 둘이 동시에 서명하면 낮은 판이 나중에 덮어쓸 수 있다
				using (AcquireLock(outPath, error))
				{
					if (File.Exists(outPath))
					{
						var previous = AppSettingSigner.PeekRevision(File.ReadAllBytes(outPath));

						if (previous > signed.Revision)
						{
							error.WriteLine($"Revision {signed.Revision} is lower than {previous} in {outPath}. Clients ignore lower revisions; raise the revision.");
							return Failed;
						}

						if (previous == signed.Revision)
						{
							error.WriteLine($"Warning: {outPath} already has revision {previous}. Raise the revision for every published change.");
						}
					}

					WriteAtomically(outPath, document);
				}

				output.WriteLine($"Signed revision {signed.Revision} ({string.Join(", ", signed.Versions.Keys)}) with {string.Join(", ", keyIds)} -> {outPath}");
				return Ok;
			}
			finally
			{
				foreach (var key in keys)
				{
					key.Dispose();
				}
			}
		}

		private static int Resource(ToolArguments options, TextWriter output)
		{
			options.RejectUnknown("--category", "--version", "--url", "--file");
			output.WriteLine(AppSettingResources.Describe(options.Single("--category"), options.Single("--version"), options.Single("--url"), options.Single("--file")));
			return Ok;
		}

		private static int Verify(ToolArguments options, TextWriter output)
		{
			options.RejectUnknown("--document", "--public-keys", "--strict");
			var keys = AppSettingPublicKeys.FromJson(File.ReadAllText(options.Single("--public-keys")));
			var document = AppSettingSigner.Verify(File.ReadAllBytes(options.Single("--document")), keys, options.Flag("--strict"));

			output.WriteLine($"Valid document: revision {document.Revision}");

			foreach (var entry in document.Versions)
			{
				var node = entry.Value;
				var platforms = string.Join(" ", node.Platforms.Select(platform => $"{platform.Key}={platform.Value.ToString().ToLowerInvariant()}"));
				var line = $"  {entry.Key}: {(platforms.Length == 0 ? "(no platforms)" : platforms)}";

				if (node.IsReview)
				{
					line += " review";
				}

				if (node.Endpoints.Count > 0)
				{
					line += $"; endpoints {string.Join(", ", node.Endpoints.Keys)}";
				}

				if (node.Resources.Count > 0)
				{
					line += $"; resources {string.Join(", ", node.Resources.Select(resource => $"{resource.Key}@{resource.Value.Version}"))}";
				}

				if (node.Maintenance != null)
				{
					line += $"; maintenance {node.Maintenance.StartAt:yyyy-MM-ddTHH:mm:ssZ} .. {node.Maintenance.EndAt:yyyy-MM-ddTHH:mm:ssZ}";
				}

				output.WriteLine(line);
			}

			return Ok;
		}

		private static void PrintPublicKey(AppSettingPublicKey key, TextWriter output)
		{
			output.WriteLine($"Public key '{key.KeyId}' (RSA {key.KeySizeBits})");
			output.WriteLine("Server public-keys.json:");
			output.WriteLine(AppSettingPublicKeys.ToJson(new[] { key }));
			output.WriteLine("Unity AppSettingServiceOptions:");
			output.WriteLine(AppSettingPublicKeys.ToCSharp(key));
		}

		/// <summary>
		/// 같은 폴더의 <c>.&lt;파일&gt;.lock</c> 을 배타적으로 엽니다(Unix 는 flock). 다른 서명이 잡고 있으면 <see cref="LockTimeout"/> 까지 기다립니다.
		/// 잠금 파일은 지우지 않습니다(지우면 기다리던 쪽과 새로 만든 쪽이 서로 다른 파일을 잠글 수 있음).
		/// </summary>
		internal static FileStream AcquireLock(string path, TextWriter error)
		{
			var full = Path.GetFullPath(path);
			var lockPath = Path.Combine(Path.GetDirectoryName(full) ?? ".", $".{Path.GetFileName(full)}.lock");
			var deadline = DateTime.UtcNow + LockTimeout;
			var waiting = false;

			while (true)
			{
				try
				{
					return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
				}
				catch (IOException exception) when (!(exception is DirectoryNotFoundException))
				{
					if (DateTime.UtcNow >= deadline)
					{
						throw new IOException($"Another sign of {path} is still running (lock {lockPath})", exception);
					}

					if (!waiting)
					{
						waiting = true;
						error.WriteLine($"Waiting for another sign of {path} (lock {lockPath})");
					}

					Thread.Sleep(100);
				}
			}
		}

		private static void WriteAtomically(string path, byte[] bytes)
		{
			// 같은 폴더의 임시 파일에 다 쓴 뒤 이름을 바꾼다. 배포 서버가 쓰다 만 파일을 읽지 않는다
			var full = Path.GetFullPath(path);
			var temporary = Path.Combine(Path.GetDirectoryName(full) ?? ".", $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");

			try
			{
				File.WriteAllBytes(temporary, bytes);
				File.Move(temporary, full, true);
			}
			finally
			{
				if (File.Exists(temporary))
				{
					File.Delete(temporary);
				}
			}
		}
	}
}
