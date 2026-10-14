using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting.Tests
{
	[TestFixture]
	public class AppSettingEnvelopeTest
	{
		private const string Payload = "{\"revision\":3,\"versions\":{\"*\":{\"platforms\":{\"android\":\"open\"}}}}";

		private static readonly TestSigner Signer = TestSigner.Get("k1");
		private static readonly TestSigner Other = TestSigner.Get("k2");

		private static int CodeOf(TestDelegate action)
		{
			return Assert.Throws<AppSettingDocumentException>(action).Code;
		}

		private static AppSettingDocument Read(byte[] document, params AppSettingPublicKey[] keys)
		{
			return AppSettingEnvelope.ReadDocument(document, keys, false);
		}

		private static byte[] Envelope(Action<JObject> edit)
		{
			var root = JObject.Parse(Encoding.UTF8.GetString(TestSigner.Document(Payload, Signer)));
			edit(root);
			return Encoding.UTF8.GetBytes(root.ToString());
		}

		[Test]
		public void ReadDocument_TrustedSignature_ReturnsPayload()
		{
			var document = Read(TestSigner.Document(Payload, Signer), Signer.PublicKey);

			Assert.AreEqual(3, document.Revision);
			Assert.AreEqual(AppPlatformStatus.Open, document.Versions["*"].GetStatus("android"));
		}

		[Test]
		public void Write_Read_RoundTripsPayloadBytesAndSignatures()
		{
			var payload = Encoding.UTF8.GetBytes(Payload);
			var written = TestSigner.Document(payload, Signer, Other);
			var read = AppSettingEnvelope.Read(written);

			CollectionAssert.AreEqual(payload, read.Payload);
			CollectionAssert.AreEqual(new[] { "k1", "k2" }, new[] { read.Signatures[0].Key, read.Signatures[1].Key });
		}

		[Test]
		public void ReadDocument_PayloadChangedAfterSigning_IsInvalidSignature()
		{
			var forged = Convert.ToBase64String(Encoding.UTF8.GetBytes(Payload.Replace("open", "internal")));
			var document = Envelope(root => root["payload"] = forged);

			Assert.AreEqual(AppSettingResultCodes.InvalidSignature, CodeOf(() => Read(document, Signer.PublicKey)));
		}

		[Test]
		public void ReadDocument_SignatureByteFlipped_IsInvalidSignature()
		{
			var document = Envelope(root =>
			{
				var signature = Convert.FromBase64String((string) root["signatures"][0]["signature"]);
				signature[10] ^= 0x01;
				root["signatures"][0]["signature"] = Convert.ToBase64String(signature);
			});

			Assert.AreEqual(AppSettingResultCodes.InvalidSignature, CodeOf(() => Read(document, Signer.PublicKey)));
		}

		[Test]
		public void ReadDocument_TruncatedSignature_IsInvalidSignatureWithoutCryptoException()
		{
			var document = Envelope(root => root["signatures"][0]["signature"] = Convert.ToBase64String(new byte[] { 1, 2, 3 }));

			Assert.AreEqual(AppSettingResultCodes.InvalidSignature, CodeOf(() => Read(document, Signer.PublicKey)));
		}

		[Test]
		public void ReadDocument_UnknownKeyId_IsInvalidSignature_AndNamesTheKeys()
		{
			var exception = Assert.Throws<AppSettingDocumentException>(() => Read(TestSigner.Document(Payload, Other), Signer.PublicKey));

			Assert.AreEqual(AppSettingResultCodes.InvalidSignature, exception.Code);
			StringAssert.Contains("signed by [k2]", exception.Message);
			StringAssert.Contains("trusted [k1]", exception.Message);
		}

		[Test]
		public void ReadDocument_OtherKeySignatureUnderTrustedKeyId_IsInvalidSignature()
		{
			var payload = Encoding.UTF8.GetBytes(Payload);
			var signatures = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("k1", Other.Sign(payload)) };
			var document = new AppSettingEnvelope(payload, signatures).Write();

			Assert.AreEqual(AppSettingResultCodes.InvalidSignature, CodeOf(() => Read(document, Signer.PublicKey)));
		}

		[Test]
		public void ReadDocument_SignedByOldAndNewKey_PassesWithEitherKey()
		{
			var document = TestSigner.Document(Payload, Other, Signer);

			Assert.AreEqual(3, Read(document, Signer.PublicKey).Revision);
			Assert.AreEqual(3, Read(document, Other.PublicKey).Revision);
		}

		[Test]
		public void ReadDocument_NoSignatures_IsInvalidSignature()
		{
			Assert.AreEqual(AppSettingResultCodes.InvalidSignature, CodeOf(() => Read(TestSigner.Document(Payload), Signer.PublicKey)));
		}

		[Test]
		public void ReadDocument_SignatureWithoutFormatPrefix_IsInvalidSignature()
		{
			// 같은 키로 다른 용도(접두사 없는 본문)에 만든 서명은 설정 문서 서명으로 쓰지 못한다
			var payload = Encoding.UTF8.GetBytes(Payload);
			byte[] rawSignature;

			using (var rsa = new RSACryptoServiceProvider(2048))
			{
				var publicKey = new AppSettingPublicKey("raw", Base64Url.Encode(rsa.ExportParameters(false).Modulus), "AQAB");
				rawSignature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
				var document = new AppSettingEnvelope(payload, new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("raw", rawSignature) }).Write();

				Assert.AreEqual(AppSettingResultCodes.InvalidSignature, CodeOf(() => Read(document, publicKey)));
			}
		}

		[Test]
		public void Read_OtherFormat_IsUnsupportedFormat()
		{
			var document = Envelope(root => root["format"] = "geuneda.appsetting.v2");

			Assert.AreEqual(AppSettingResultCodes.UnsupportedFormat, CodeOf(() => AppSettingEnvelope.Read(document)));
		}

		[TestCase("format")]
		[TestCase("payload")]
		[TestCase("signatures")]
		public void Read_MissingField_IsInvalidDocument(string field)
		{
			var document = Envelope(root => root.Remove(field));

			Assert.AreEqual(AppSettingResultCodes.InvalidDocument, CodeOf(() => AppSettingEnvelope.Read(document)));
		}

		[Test]
		public void Read_PayloadNotBase64_IsInvalidDocument()
		{
			var document = Envelope(root => root["payload"] = "%%%");

			Assert.AreEqual(AppSettingResultCodes.InvalidDocument, CodeOf(() => AppSettingEnvelope.Read(document)));
		}

		[Test]
		public void Read_SignatureEntryNotObject_IsInvalidDocument()
		{
			var document = Envelope(root => root["signatures"] = new JArray("k1"));

			Assert.AreEqual(AppSettingResultCodes.InvalidDocument, CodeOf(() => AppSettingEnvelope.Read(document)));
		}

		[Test]
		public void Read_UnknownEnvelopeProperty_IsIgnored()
		{
			var document = Envelope(root => root["comment"] = "uploaded by ci");

			Assert.AreEqual(3, Read(document, Signer.PublicKey).Revision);
		}

		[TestCase("not json")]
		[TestCase("[1, 2]")]
		[TestCase("{\"format\":\"geuneda.appsetting.v1\"} {}")]
		[TestCase("{\"format\":\"a\",\"format\":\"geuneda.appsetting.v1\"}")]
		public void Read_BrokenJson_IsInvalidDocument(string json)
		{
			Assert.AreEqual(AppSettingResultCodes.InvalidDocument, CodeOf(() => AppSettingEnvelope.Read(Encoding.UTF8.GetBytes(json))));
		}

		[Test]
		public void PublicKey_RoundTripsModulusAndExponent_AndTrimsLeadingZero()
		{
			var original = Signer.PublicKey;
			var modulus = Base64Url.TryDecode(original.Modulus);
			var withZero = new byte[modulus.Length + 1];
			Buffer.BlockCopy(modulus, 0, withZero, 1, modulus.Length);

			var key = new AppSettingPublicKey("k1", Convert.ToBase64String(withZero), original.Exponent);

			Assert.AreEqual(original.Modulus, key.Modulus);
			Assert.AreEqual("AQAB", key.Exponent);
			Assert.AreEqual(2048, key.KeySizeBits);
			Assert.AreEqual(3, Read(TestSigner.Document(Payload, Signer), key).Revision);
		}

		[Test]
		public void PublicKey_SmallerThan2048Bits_Throws()
		{
			using (var rsa = new RSACryptoServiceProvider(1024))
			{
				var modulus = Base64Url.Encode(rsa.ExportParameters(false).Modulus);

				Assert.Throws<ArgumentException>(() => new AppSettingPublicKey("small", modulus, "AQAB"));
			}
		}

		[TestCase("", "AQAB")]
		[TestCase("k", "%%%")]
		[TestCase("k", "Ag")]
		[TestCase("k", "AQ")]
		public void PublicKey_InvalidKeyIdOrExponent_Throws(string keyId, string exponent)
		{
			Assert.Throws<ArgumentException>(() => new AppSettingPublicKey(keyId, Signer.PublicKey.Modulus, exponent));
		}

		[Test]
		public void PublicKey_EvenModulus_Throws()
		{
			var modulus = Base64Url.TryDecode(Signer.PublicKey.Modulus);
			modulus[modulus.Length - 1] &= 0xFE;

			Assert.Throws<ArgumentException>(() => new AppSettingPublicKey("even", Base64Url.Encode(modulus), "AQAB"));
		}
	}
}
