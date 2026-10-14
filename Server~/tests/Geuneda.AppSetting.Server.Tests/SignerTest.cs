using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Geuneda.AppSetting.Server.Tests
{
	public class SignerTest
	{
		[Fact]
		public void Sign_ThenVerify_RoundTripsPayloadBytes()
		{
			var payload = TestKeys.Payload(4, extra: "\"endpoints\":{\"game\":\"https://game.test/\"}");

			var document = AppSettingSigner.Sign(payload, new[] { TestKeys.First });
			var verified = AppSettingSigner.Verify(document, new[] { TestKeys.First.PublicKey }, true);

			Assert.Equal(4, verified.Revision);
			Assert.Equal(AppPlatformStatus.Open, verified.Versions["1.0.0"].GetStatus("android"));
			Assert.Equal(payload, AppSettingEnvelope.Read(document).Payload);
		}

		[Fact]
		public void Sign_WithTwoKeys_VerifiesWithEitherKey()
		{
			var document = AppSettingSigner.Sign(TestKeys.Payload(1), new[] { TestKeys.First, TestKeys.Second });

			Assert.Equal(1, AppSettingSigner.Verify(document, new[] { TestKeys.First.PublicKey }).Revision);
			Assert.Equal(1, AppSettingSigner.Verify(document, new[] { TestKeys.Second.PublicKey }).Revision);
		}

		[Fact]
		public void Sign_PayloadWithTypo_IsRejectedBeforeSigning()
		{
			var payload = TestKeys.Payload(1, extra: "\"endpoint\":{\"game\":\"https://game.test/\"}");

			var exception = Assert.Throws<AppSettingDocumentException>(() => AppSettingSigner.Sign(payload, new[] { TestKeys.First }));

			Assert.Equal(AppSettingResultCodes.InvalidDocument, exception.Code);
			Assert.Contains("endpoint", exception.Message);
		}

		[Fact]
		public void Sign_InvalidKeyList_Throws()
		{
			var payload = TestKeys.Payload(1);

			Assert.Throws<ArgumentException>(() => AppSettingSigner.Sign(payload, Array.Empty<AppSettingSigningKey>()));
			Assert.Throws<ArgumentException>(() => AppSettingSigner.Sign(payload, new AppSettingSigningKey[] { null }));
			Assert.Throws<ArgumentException>(() => AppSettingSigner.Sign(payload, new[] { TestKeys.First, TestKeys.First }));
		}

		[Fact]
		public void Verify_UntrustedKey_IsInvalidSignature()
		{
			var exception = Assert.Throws<AppSettingDocumentException>(
				() => AppSettingSigner.Verify(TestKeys.Document(1), new[] { TestKeys.Second.PublicKey }));

			Assert.Equal(AppSettingResultCodes.InvalidSignature, exception.Code);
		}

		[Fact]
		public void Verify_StrictRejectsWhatClientsAccept()
		{
			// 서명 도구를 거치지 않은 문서(모르는 속성)는 클라이언트 규칙으로는 통과하고 엄격한 규칙으로는 실패한다
			var payload = Encoding.UTF8.GetBytes("{\"revision\":1,\"versions\":{\"*\":{\"platforms\":{}}},\"future\":true}");
			var signature = new KeyValuePair<string, byte[]>("k1", TestKeys.First.Sign(payload));
			var document = new AppSettingEnvelope(payload, new List<KeyValuePair<string, byte[]>> { signature }).Write();

			Assert.Equal(1, AppSettingSigner.Verify(document, new[] { TestKeys.First.PublicKey }).Revision);
			Assert.Throws<AppSettingDocumentException>(() => AppSettingSigner.Verify(document, new[] { TestKeys.First.PublicKey }, true));
		}

		[Fact]
		public void Verify_WithoutKeys_Throws()
		{
			Assert.Throws<ArgumentException>(() => AppSettingSigner.Verify(TestKeys.Document(1), Array.Empty<AppSettingPublicKey>()));
		}

		[Fact]
		public void PeekRevision_ReadsWithoutSignature_AndIsNullForBrokenDocuments()
		{
			Assert.Equal(3, AppSettingSigner.PeekRevision(TestKeys.Document(3, key: TestKeys.Second)));
			Assert.Null(AppSettingSigner.PeekRevision(Encoding.UTF8.GetBytes("{}")));
		}

		[Fact]
		public void SigningKey_Generate_DefaultsTo3072Bits()
		{
			using var key = AppSettingSigningKey.Generate("big");

			Assert.Equal(3072, key.PublicKey.KeySizeBits);
			Assert.Equal("AQAB", key.PublicKey.Exponent);
			Assert.Throws<ArgumentException>(() => AppSettingSigningKey.Generate("small", 1024));
			Assert.Throws<ArgumentException>(() => AppSettingSigningKey.Generate("", 2048));
		}

		[Fact]
		public void SigningKey_FromPem_ReadsPkcs8AndPkcs1_SamePublicKey()
		{
			var pkcs8 = TestKeys.First.ExportPrivateKeyPem();
			using var fromPkcs8 = AppSettingSigningKey.FromPem("k1", pkcs8);
			using var rsa = RSA.Create();
			rsa.ImportFromPem(pkcs8);
			using var fromPkcs1 = AppSettingSigningKey.FromPem("k1", rsa.ExportRSAPrivateKeyPem());

			Assert.StartsWith("-----BEGIN PRIVATE KEY-----", pkcs8);
			Assert.Equal(TestKeys.First.PublicKey.Modulus, fromPkcs8.PublicKey.Modulus);
			Assert.Equal(TestKeys.First.PublicKey.Modulus, fromPkcs1.PublicKey.Modulus);
		}

		[Fact]
		public void SigningKey_FromPem_RejectsPublicOnlySmallAndGarbage()
		{
			using var small = RSA.Create(1024);
			using var rsa = RSA.Create();
			rsa.ImportFromPem(TestKeys.First.ExportPrivateKeyPem());

			Assert.Throws<ArgumentException>(() => AppSettingSigningKey.FromPem("k", rsa.ExportSubjectPublicKeyInfoPem()));
			Assert.Throws<ArgumentException>(() => AppSettingSigningKey.FromPem("k", small.ExportPkcs8PrivateKeyPem()));
			Assert.Throws<ArgumentException>(() => AppSettingSigningKey.FromPem("k", "not a pem"));
			Assert.Throws<ArgumentException>(() => AppSettingSigningKey.FromPem("", TestKeys.First.ExportPrivateKeyPem()));
		}

		[Fact]
		public void PublicKeys_Json_RoundTrips_AndCSharpLineHasTheSameValues()
		{
			var keys = new[] { TestKeys.First.PublicKey, TestKeys.Second.PublicKey };

			var read = AppSettingPublicKeys.FromJson(AppSettingPublicKeys.ToJson(keys));
			var line = AppSettingPublicKeys.ToCSharp(TestKeys.First.PublicKey);

			Assert.Equal(keys.Select(key => key.KeyId), read.Select(key => key.KeyId));
			Assert.Equal(keys.Select(key => key.Modulus), read.Select(key => key.Modulus));
			Assert.Equal($"options.PublicKeys.Add(new AppSettingPublicKey(\"k1\", \"{TestKeys.First.PublicKey.Modulus}\", \"AQAB\"));", line);
		}

		[Theory]
		[InlineData("{}", typeof(FormatException))]
		[InlineData("[1]", typeof(FormatException))]
		[InlineData("[{\"keyId\":\"k\"}]", typeof(FormatException))]
		[InlineData("[{\"keyId\":\"k\",\"modulus\":\"AQAB\",\"exponent\":\"AQAB\"}]", typeof(ArgumentException))]
		public void PublicKeys_FromJson_RejectsBrokenFiles(string json, Type expected)
		{
			Assert.Throws(expected, () => AppSettingPublicKeys.FromJson(json));
		}

		public sealed class NumberFeatures
		{
			public double Tiny { get; set; }
			public double Huge { get; set; }
			public decimal Precise { get; set; }
		}

		[Fact]
		public void Features_KeepTheSignedTextSoNumbersSurviveIntoTheGameType()
		{
			// decimal 로 먼저 읽으면 1e-30 은 0, 1e30 은 범위 밖이고, double 로 먼저 읽으면 정밀한 decimal 이 깎인다
			const string features = "{ \"tiny\": 1e-30, \"huge\": 1e30, \"precise\": 0.1234567890123456789012345678 }";
			var payload = Encoding.UTF8.GetBytes("{\"revision\":1,\"versions\":{\"*\":{\"platforms\":{},\"features\":" + features + "}}}");

			var document = AppSettingSigner.Verify(AppSettingSigner.Sign(payload, new[] { TestKeys.First }), new[] { TestKeys.First.PublicKey });
			var read = Newtonsoft.Json.JsonConvert.DeserializeObject<NumberFeatures>(document.Versions["*"].FeaturesJson);

			Assert.Equal(features, document.Versions["*"].FeaturesJson);
			Assert.Equal(1e-30, read.Tiny);
			Assert.Equal(1e30, read.Huge);
			Assert.Equal(0.1234567890123456789012345678m, read.Precise);
		}

		[Theory]
		[InlineData("{\"tiny\":1e400}")]
		[InlineData("{\"x\":NaN}")]
		[InlineData("{\"x\":1, }")]
		[InlineData("{'x':1}")]
		[InlineData("{x:1}")]
		[InlineData("{\"x\":01}")]
		[InlineData("{\"x\":1 /* note */}")]
		public void Sign_NonStandardJson_IsRejected(string features)
		{
			var payload = Encoding.UTF8.GetBytes("{\"revision\":1,\"versions\":{\"*\":{\"platforms\":{},\"features\":" + features + "}}}");

			var exception = Assert.Throws<AppSettingDocumentException>(() => AppSettingSigner.Sign(payload, new[] { TestKeys.First }));

			Assert.Equal(AppSettingResultCodes.InvalidDocument, exception.Code);
		}

		[Fact]
		public void PublicKeys_DateShapedKeyId_SurvivesTheFileAndVerifies()
		{
			using var key = AppSettingSigningKey.FromPem("2026-10-14T02:00:00Z", TestKeys.First.ExportPrivateKeyPem());
			var document = AppSettingSigner.Sign(TestKeys.Payload(1), new[] { key });

			var read = AppSettingPublicKeys.FromJson(AppSettingPublicKeys.ToJson(new[] { key.PublicKey }));

			Assert.Equal("2026-10-14T02:00:00Z", read[0].KeyId);
			Assert.Equal(1, AppSettingSigner.Verify(document, read).Revision);
			Assert.Throws<FormatException>(() => AppSettingPublicKeys.FromJson("[{\"keyId\":1,\"modulus\":\"m\",\"exponent\":\"AQAB\"}]"));
		}

		[Fact]
		public void PublicKeys_ToCSharp_EscapesTheKeyId()
		{
			using var key = AppSettingSigningKey.FromPem("release\\new\"\u00e9", TestKeys.First.ExportPrivateKeyPem());

			var line = AppSettingPublicKeys.ToCSharp(key.PublicKey);

			Assert.StartsWith("options.PublicKeys.Add(new AppSettingPublicKey(\"release\\\\new\\\"\\u00e9\", ", line);
		}

		[Fact]
		public void PublicKeys_FromJson_RejectsContentAfterTheArray_AllowsTrailingWhitespace()
		{
			var first = AppSettingPublicKeys.ToJson(new[] { TestKeys.First.PublicKey });
			var second = AppSettingPublicKeys.ToJson(new[] { TestKeys.Second.PublicKey });

			Assert.Throws<FormatException>(() => AppSettingPublicKeys.FromJson(first + "\n" + second));
			Assert.Throws<FormatException>(() => AppSettingPublicKeys.FromJson(first + " oops"));
			Assert.Single(AppSettingPublicKeys.FromJson(first + " \t\r\n"));
		}

		[Theory]
		[InlineData("\u00a0")]
		[InlineData("\u000b")]
		[InlineData("\u2028")]
		public void Sign_NonStandardWhitespaceAfterTheRoot_IsRejected(string trailing)
		{
			var payload = Encoding.UTF8.GetBytes("{\"revision\":1,\"versions\":{\"*\":{\"platforms\":{}}}}" + trailing);

			Assert.Throws<AppSettingDocumentException>(() => AppSettingSigner.Sign(payload, new[] { TestKeys.First }));
		}

		[Fact]
		public void Sign_StandardWhitespaceAroundTheRoot_IsAccepted()
		{
			var payload = Encoding.UTF8.GetBytes(" \t\r\n{\"revision\":1,\"versions\":{\"*\":{\"platforms\":{}}}} \t\r\n");

			Assert.NotEmpty(AppSettingSigner.Sign(payload, new[] { TestKeys.First }));
		}

		[Fact]
		public void PublicKeys_FromJson_RejectsDuplicateKeyIds()
		{
			var json = AppSettingPublicKeys.ToJson(new[] { TestKeys.First.PublicKey, TestKeys.First.PublicKey });

			Assert.Throws<ArgumentException>(() => AppSettingPublicKeys.FromJson(json));
		}
	}
}
