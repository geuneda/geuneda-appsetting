using System;
using System.Text;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting.Tests
{
	[TestFixture]
	public class AppSettingTest
	{
		private static readonly DateTime Before = new DateTime(2026, 10, 14, 1, 0, 0, DateTimeKind.Utc);
		private static readonly DateTime During = new DateTime(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);

		private const string Maintenance =
			"\"maintenance\":{\"startAt\":\"2026-10-14T02:00:00Z\",\"endAt\":\"2026-10-14T04:00:00Z\",\"defaultLanguage\":\"en\",\"messages\":{\"en\":\"m\"}}";

		public sealed class Features
		{
			public bool ShopEnabled;
			public int Limit = 5;
		}

		private static AppSetting Evaluate(string versions, string appVersion, string platform = AppSettingPlatforms.Android)
		{
			var document = AppSettingPayload.Parse(Encoding.UTF8.GetBytes("{\"revision\":1,\"versions\":{" + versions + "}}"), false);
			return new AppSetting(document, appVersion, platform);
		}

		private static string Node(string android, string extra = null)
		{
			return "{\"platforms\":{\"android\":\"" + android + "\"}" + (extra == null ? string.Empty : "," + extra) + "}";
		}

		[Test]
		public void ExactVersion_IsPreferredOverDefaultNode()
		{
			var setting = Evaluate("\"1.0.0\":" + Node("open") + ",\"*\":" + Node("closed"), "1.0.0");

			Assert.AreEqual("1.0.0", setting.MatchedVersion);
			Assert.AreEqual(AppPlatformStatus.Open, setting.Status);
			Assert.AreEqual(AppAccess.Open, setting.GetAccess(Before));
		}

		[Test]
		public void UnregisteredVersion_UsesDefaultNode()
		{
			var setting = Evaluate("\"1.0.0\":" + Node("open") + ",\"*\":" + Node("internal"), "1.0.1");

			Assert.AreEqual("*", setting.MatchedVersion);
			Assert.AreEqual(AppAccess.Internal, setting.GetAccess(Before));
		}

		[Test]
		public void UnregisteredVersion_WithoutDefaultNode_IsUpdateRequired()
		{
			var setting = Evaluate("\"1.0.0\":" + Node("open"), "0.9.0");

			Assert.IsNull(setting.MatchedVersion);
			Assert.IsNull(setting.Version);
			Assert.AreEqual(AppPlatformStatus.Closed, setting.Status);
			Assert.AreEqual(AppAccess.UpdateRequired, setting.GetAccess(Before));
		}

		[Test]
		public void VersionKeys_MatchExactly()
		{
			// "1.0" 과 "1.0.0" 은 숫자로는 같지만 노드는 문자열이 같은 키만 고른다
			var setting = Evaluate("\"1.0.0\":" + Node("open"), "1.0");

			Assert.IsNull(setting.Version);
		}

		[Test]
		public void Open_WithMaintenanceWindow_IsMaintenanceOnlyInsideTheWindow()
		{
			var setting = Evaluate("\"1.0.0\":" + Node("open", Maintenance), "1.0.0");

			Assert.AreEqual(AppAccess.Open, setting.GetAccess(Before));
			Assert.AreEqual(AppAccess.Maintenance, setting.GetAccess(During));
			Assert.AreEqual("m", setting.Maintenance.GetMessage("en"));
		}

		[Test]
		public void Internal_IgnoresMaintenanceWindow()
		{
			var setting = Evaluate("\"1.0.0\":" + Node("internal", Maintenance), "1.0.0");

			Assert.AreEqual(AppAccess.Internal, setting.GetAccess(During));
		}

		[Test]
		public void Closed_WithNewerPublicVersion_IsUpdateRequired_OtherwiseMaintenance()
		{
			var withNewer = Evaluate("\"1.0.0\":" + Node("closed") + ",\"1.1.0\":" + Node("open"), "1.0.0");
			var latest = Evaluate("\"1.1.0\":" + Node("closed") + ",\"1.0.0\":" + Node("open"), "1.1.0");

			Assert.AreEqual(AppAccess.UpdateRequired, withNewer.GetAccess(Before));
			Assert.AreEqual(AppAccess.Maintenance, latest.GetAccess(Before));
		}

		[Test]
		public void PlatformMissingFromNode_IsClosed()
		{
			var setting = Evaluate("\"1.0.0\":" + Node("open"), "1.0.0", AppSettingPlatforms.Ios);

			Assert.AreEqual(AppPlatformStatus.Closed, setting.Status);
			Assert.AreEqual(AppAccess.Maintenance, setting.GetAccess(Before));
		}

		[Test]
		public void NewerVersions_AreNumericallyOrderedPublicVersionsOnThisPlatform()
		{
			var setting = Evaluate(
				"\"1.9.0\":" + Node("open") +
				",\"1.10.0\":" + Node("open") +
				",\"1.9.5\":" + Node("open") +
				",\"2.0.0\":" + Node("open", "\"review\":true") +
				",\"2.1.0\":" + Node("internal") +
				",\"2.2.0\":" + Node("closed") +
				",\"3.0.0-beta\":" + Node("open") +
				",\"1.9\":" + Node("open") +
				",\"*\":" + Node("open") +
				",\"1.8.0\":" + Node("open"),
				"1.9.0");

			CollectionAssert.AreEqual(new[] { "1.9.5", "1.10.0" }, setting.NewerVersions);
			Assert.IsTrue(setting.HasNewerVersion);
		}

		[Test]
		public void NewerVersions_UseThisPlatformsStatus()
		{
			var setting = Evaluate("\"1.0.0\":" + Node("open") + ",\"1.1.0\":{\"platforms\":{\"android\":\"closed\",\"ios\":\"open\"}}", "1.0.0", AppSettingPlatforms.Ios);

			CollectionAssert.AreEqual(new[] { "1.1.0" }, setting.NewerVersions);
		}

		[Test]
		public void UnparsableAppVersion_HasNoNewerVersions()
		{
			var setting = Evaluate("\"1.0.0-rc1\":" + Node("closed") + ",\"2.0.0\":" + Node("open"), "1.0.0-rc1");

			Assert.IsFalse(setting.HasNewerVersion);
			Assert.AreEqual(AppAccess.Maintenance, setting.GetAccess(Before));
		}

		[Test]
		public void GetAccess_RequiresUtc()
		{
			var setting = Evaluate("\"*\":" + Node("open"), "1.0.0");

			Assert.Throws<ArgumentException>(() => setting.GetAccess(DateTime.Now));
		}

		[Test]
		public void TryGetEndpoint_ReadsMatchedNode()
		{
			var setting = Evaluate("\"*\":" + Node("open", "\"endpoints\":{\"game\":\"https://game.test/\"}"), "1.0.0");
			var none = Evaluate("\"2.0.0\":" + Node("open", "\"endpoints\":{\"game\":\"https://game.test/\"}"), "1.0.0");

			Assert.IsTrue(setting.TryGetEndpoint("game", out var game));
			Assert.AreEqual("https://game.test/", game);
			Assert.IsFalse(setting.TryGetEndpoint("Game", out _));
			Assert.IsFalse(setting.TryGetEndpoint(null, out _));
			Assert.IsFalse(none.TryGetEndpoint("game", out _));
		}

		[Test]
		public void GetFeatures_ReadsGameType_AndKeepsDefaultsForMissingFields()
		{
			var setting = Evaluate("\"*\":" + Node("open", "\"features\":{\"shopEnabled\":true}"), "1.0.0");
			var features = setting.GetFeatures<Features>();

			Assert.IsTrue(features.IsSuccess, features.ToString());
			Assert.IsTrue(features.Value.ShopEnabled);
			Assert.AreEqual(5, features.Value.Limit);
		}

		public sealed class NumberFeatures
		{
			public double Tiny;
			public double Huge;
			public decimal Precise;
		}

		[Test]
		public void GetFeatures_KeepsNumbersOutsideTheOtherTypesRange()
		{
			// decimal 로 먼저 읽으면 1e-30 은 0, 1e30 은 범위 밖이고, double 로 먼저 읽으면 정밀한 decimal 이 깎인다
			var setting = Evaluate("\"*\":" + Node("open", "\"features\":{\"tiny\":1e-30,\"huge\":1e30,\"precise\":0.1234567890123456789012345678}"), "1.0.0");

			var features = setting.GetFeatures<NumberFeatures>();

			Assert.IsTrue(features.IsSuccess, features.ToString());
			Assert.AreEqual(1e-30, features.Value.Tiny);
			Assert.AreEqual(1e30, features.Value.Huge);
			Assert.AreEqual(0.1234567890123456789012345678m, features.Value.Precise);
		}

		[Test]
		public void GetFeatures_WithoutNode_ReturnsDefaults()
		{
			var features = Evaluate("\"2.0.0\":" + Node("open"), "1.0.0").GetFeatures<Features>();

			Assert.IsTrue(features.IsSuccess);
			Assert.IsFalse(features.Value.ShopEnabled);
		}

		[Test]
		public void GetFeatures_WrongShape_IsInvalidDocument()
		{
			var features = Evaluate("\"*\":" + Node("open", "\"features\":{\"limit\":\"many\"}"), "1.0.0").GetFeatures<Features>();

			Assert.IsFalse(features.IsSuccess);
			Assert.AreEqual(AppSettingResultCodes.InvalidDocument, features.Code);
		}
	}
}
