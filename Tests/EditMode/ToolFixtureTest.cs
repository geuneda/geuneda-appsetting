using System;
using System.Text;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting.Tests
{
	/// <summary>.NET 서명 CLI 가 만든 문서(RSA-3072)를 Unity(Mono)의 검증·해석 코드가 그대로 받아들이는지 확인합니다.</summary>
	[TestFixture]
	public class ToolFixtureTest
	{
		private static AppSettingPublicKey Key()
		{
			return new AppSettingPublicKey(ToolFixture.KeyId, ToolFixture.Modulus, ToolFixture.Exponent);
		}

		[Test]
		public void CliSignedDocument_Verifies_AndParsesInBothModes()
		{
			var bytes = Encoding.UTF8.GetBytes(ToolFixture.Document);

			var lenient = AppSettingEnvelope.ReadDocument(bytes, new[] { Key() }, false);
			var strict = AppSettingEnvelope.ReadDocument(bytes, new[] { Key() }, true);

			Assert.AreEqual(3072, Key().KeySizeBits);
			Assert.AreEqual(1, lenient.Revision);
			Assert.AreEqual(lenient.PayloadHash, strict.PayloadHash);
			CollectionAssert.AreEquivalent(new[] { "1.0.0", "1.1.0", "*" }, lenient.Versions.Keys);
		}

		[Test]
		public void CliSignedDocument_EvaluatesTheSamplePayload()
		{
			var document = AppSettingEnvelope.ReadDocument(Encoding.UTF8.GetBytes(ToolFixture.Document), new[] { Key() }, false);
			var current = new AppSetting(document, "1.0.0", AppSettingPlatforms.Android);
			var unknown = new AppSetting(document, "0.9.0", AppSettingPlatforms.Ios);
			var inMaintenance = new DateTime(2030, 1, 1, 3, 0, 0, DateTimeKind.Utc);

			Assert.AreEqual(AppAccess.Open, current.GetAccess(new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc)));
			Assert.AreEqual(AppAccess.Maintenance, current.GetAccess(inMaintenance));
			Assert.AreEqual("정기 점검", current.Maintenance.GetMessage("ko-KR"));
			Assert.IsTrue(current.TryGetEndpoint("realtime", out var realtime));
			Assert.AreEqual("wss://rt.example.com/rpc", realtime);
			Assert.IsTrue(current.GetFeatures<AppSettingTest.Features>().Value.ShopEnabled);
			Assert.IsFalse(current.HasNewerVersion, "1.1.0 is in review and internal");
			Assert.AreEqual("*", unknown.MatchedVersion);
			CollectionAssert.AreEqual(new[] { "1.0.0" }, unknown.NewerVersions);
			Assert.AreEqual(AppAccess.UpdateRequired, unknown.GetAccess(inMaintenance));
		}

		[Test]
		public void CliSignedDocument_OtherKey_IsInvalidSignature()
		{
			var other = TestSigner.Get("k1").PublicKey;
			var renamed = new AppSettingPublicKey(ToolFixture.KeyId, other.Modulus, other.Exponent);

			var exception = Assert.Throws<AppSettingDocumentException>(
				() => AppSettingEnvelope.ReadDocument(Encoding.UTF8.GetBytes(ToolFixture.Document), new[] { renamed }, false));

			Assert.AreEqual(AppSettingResultCodes.InvalidSignature, exception.Code);
		}
	}
}
