using System;
using System.Text;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting.Tests
{
	[TestFixture]
	public class AppSettingPayloadTest
	{
		private const string Full = @"{
  ""revision"": 7,
  ""versions"": {
    ""1.2.0"": {
      ""platforms"": { ""Android"": ""open"", ""ios"": ""INTERNAL"" },
      ""review"": true,
      ""endpoints"": { ""game"": ""https://game.example.com/"", ""realtime"": ""wss://rt.example.com/rpc"" },
      ""maintenance"": {
        ""startAt"": ""2026-10-14T11:00:00+09:00"",
        ""endAt"": ""2026-10-14T04:30:00.5Z"",
        ""defaultLanguage"": ""en"",
        ""messages"": { ""en"": ""Maintenance"", ""ko"": ""점검 중"", ""zh-Hant"": ""維護"" }
      },
      ""features"": { ""shopEnabled"": true, ""banner"": { ""id"": 3 } }
    },
    ""*"": { ""platforms"": {} }
  }
}";

		private static AppSettingDocument Parse(string json, bool strict = false)
		{
			return AppSettingPayload.Parse(Encoding.UTF8.GetBytes(json), strict);
		}

		private static void AssertInvalid(string json, bool strict = false)
		{
			var exception = Assert.Throws<AppSettingDocumentException>(() => Parse(json, strict));
			Assert.AreEqual(AppSettingResultCodes.InvalidDocument, exception.Code, exception.Message);
		}

		private static string WithNode(string node)
		{
			return "{\"revision\":1,\"versions\":{\"1.0\":" + node + "}}";
		}

		[TestCase(false)]
		[TestCase(true)]
		public void Parse_FullDocument_ReadsEveryField(bool strict)
		{
			var document = Parse(Full, strict);
			var node = document.Versions["1.2.0"];

			Assert.AreEqual(7, document.Revision);
			Assert.AreEqual(AppPlatformStatus.Open, node.GetStatus("android"));
			Assert.AreEqual(AppPlatformStatus.Internal, node.GetStatus("IOS"));
			Assert.AreEqual(AppPlatformStatus.Closed, node.GetStatus("webgl"));
			Assert.AreEqual(AppPlatformStatus.Closed, node.GetStatus(null));
			Assert.IsTrue(node.IsReview);
			Assert.AreEqual("wss://rt.example.com/rpc", node.Endpoints["realtime"]);
			Assert.AreEqual(new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc), node.Maintenance.StartAt);
			Assert.AreEqual(DateTimeKind.Utc, node.Maintenance.StartAt.Kind);
			Assert.AreEqual(new DateTime(2026, 10, 14, 4, 30, 0, 500, DateTimeKind.Utc), node.Maintenance.EndAt);
			Assert.AreEqual("{ \"shopEnabled\": true, \"banner\": { \"id\": 3 } }", node.FeaturesJson);
			Assert.AreEqual("{}", document.Versions["*"].FeaturesJson);
			Assert.IsNull(document.Versions["*"].Maintenance);
			Assert.IsFalse(document.Versions["*"].IsReview);
		}

		[Test]
		public void Maintenance_GetMessage_ExactThenPrimaryTagThenDefault()
		{
			var notice = Parse(Full).Versions["1.2.0"].Maintenance;

			Assert.AreEqual("점검 중", notice.GetMessage("KO"));
			Assert.AreEqual("점검 중", notice.GetMessage("ko-KR"));
			Assert.AreEqual("維護", notice.GetMessage("zh-Hant"));
			Assert.AreEqual("Maintenance", notice.GetMessage("ja"));
			Assert.AreEqual("Maintenance", notice.GetMessage(null));
		}

		[Test]
		public void Maintenance_IsActiveAt_StartInclusiveEndExclusive_AndRequiresUtc()
		{
			var notice = Parse(Full).Versions["1.2.0"].Maintenance;

			Assert.IsFalse(notice.IsActiveAt(notice.StartAt.AddTicks(-1)));
			Assert.IsTrue(notice.IsActiveAt(notice.StartAt));
			Assert.IsTrue(notice.IsActiveAt(notice.EndAt.AddTicks(-1)));
			Assert.IsFalse(notice.IsActiveAt(notice.EndAt));
			Assert.Throws<ArgumentException>(() => notice.IsActiveAt(DateTime.SpecifyKind(notice.StartAt, DateTimeKind.Local)));
			Assert.Throws<ArgumentException>(() => notice.IsActiveAt(DateTime.SpecifyKind(notice.StartAt, DateTimeKind.Unspecified)));
		}

		[TestCase("{\"versions\":{\"*\":{}}}")]
		[TestCase("{\"revision\":0,\"versions\":{\"*\":{}}}")]
		[TestCase("{\"revision\":-1,\"versions\":{\"*\":{}}}")]
		[TestCase("{\"revision\":\"1\",\"versions\":{\"*\":{}}}")]
		[TestCase("{\"revision\":1.5,\"versions\":{\"*\":{}}}")]
		[TestCase("{\"revision\":99999999999999999999,\"versions\":{\"*\":{}}}")]
		[TestCase("{\"revision\":1}")]
		[TestCase("{\"revision\":1,\"versions\":{}}")]
		[TestCase("{\"revision\":1,\"versions\":[]}")]
		[TestCase("{\"revision\":1,\"versions\":{\"\":{}}}")]
		[TestCase("{\"revision\":1,\"versions\":{\"1.0\":\"open\"}}")]
		[TestCase("{\"revision\":1,\"revision\":2,\"versions\":{\"*\":{}}}")]
		[TestCase("[]")]
		public void Parse_BrokenRoot_IsInvalid(string json)
		{
			AssertInvalid(json);
		}

		[TestCase("{\"platforms\":[]}")]
		[TestCase("{\"platforms\":{\"android\":1}}")]
		[TestCase("{\"platforms\":{\"\":\"open\"}}")]
		[TestCase("{\"platforms\":{\"android\":\"open\",\"ANDROID\":\"closed\"}}")]
		[TestCase("{\"review\":\"yes\"}")]
		[TestCase("{\"endpoints\":{\"game\":\"\"}}")]
		[TestCase("{\"endpoints\":{\"game\":3}}")]
		[TestCase("{\"features\":[1]}")]
		[TestCase("{\"maintenance\":{\"startAt\":\"2026-10-14T02:00:00\",\"endAt\":\"2026-10-14T04:00:00Z\",\"defaultLanguage\":\"en\",\"messages\":{\"en\":\"m\"}}}")]
		[TestCase("{\"maintenance\":{\"startAt\":\"2026-10-14T04:00:00Z\",\"endAt\":\"2026-10-14T04:00:00Z\",\"defaultLanguage\":\"en\",\"messages\":{\"en\":\"m\"}}}")]
		[TestCase("{\"maintenance\":{\"startAt\":\"2026-10-14T02:00:00Z\",\"endAt\":\"2026-10-14T04:00:00Z\",\"defaultLanguage\":\"ko\",\"messages\":{\"en\":\"m\"}}}")]
		[TestCase("{\"maintenance\":{\"startAt\":\"2026-10-14T02:00:00Z\",\"endAt\":\"2026-10-14T04:00:00Z\",\"messages\":{\"en\":\"m\"}}}")]
		[TestCase("{\"maintenance\":{\"startAt\":\"2026-10-14T02:00:00Z\",\"endAt\":\"2026-10-14T04:00:00Z\",\"defaultLanguage\":\"en\",\"messages\":{\"en\":\"m\",\"EN\":\"n\"}}}")]
		[TestCase("{\"maintenance\":{\"startAt\":\"tomorrow\",\"endAt\":\"2026-10-14T04:00:00Z\",\"defaultLanguage\":\"en\",\"messages\":{\"en\":\"m\"}}}")]
		public void Parse_BrokenNode_IsInvalidInBothModes(string node)
		{
			AssertInvalid(WithNode(node));
			AssertInvalid(WithNode(node), true);
		}

		[Test]
		public void Parse_UnknownStatus_IsClosedWhenLenient_AndRejectedWhenStrict()
		{
			var json = WithNode("{\"platforms\":{\"android\":\"paused\"}}");

			Assert.AreEqual(AppPlatformStatus.Closed, Parse(json).Versions["1.0"].GetStatus("android"));
			AssertInvalid(json, true);
		}

		[TestCase("{\"revision\":1,\"versions\":{\"*\":{\"platforms\":{}}},\"note\":\"x\"}")]
		[TestCase("{\"revision\":1,\"versions\":{\"*\":{\"platforms\":{},\"endpoint\":{}}}}")]
		[TestCase("{\"revision\":1,\"versions\":{\"*\":{\"platforms\":{},\"maintenance\":{\"startAt\":\"2026-10-14T02:00:00Z\",\"endAt\":\"2026-10-14T04:00:00Z\",\"defaultLanguage\":\"en\",\"messages\":{\"en\":\"m\"},\"url\":\"x\"}}}}")]
		[TestCase("{\"revision\":1,\"versions\":{\"*\":{}}}")]
		[TestCase("{\"revision\":1,\"versions\":{\" 1.0\":{\"platforms\":{}}}}")]
		[TestCase("{\"revision\":1,\"versions\":{\"*\":{\"platforms\":{},\"endpoints\":{\"game\":\"/relative\"}}}}")]
		[TestCase("{\"revision\":1,\"versions\":{\"*\":{\"platforms\":{},\"endpoints\":{\"game\":\"file:///etc/hosts\"}}}}")]
		[TestCase("{\"revision\":1,\"versions\":{\"*\":{\"platforms\":{},\"endpoints\":{\"game\":\"game.example.com\"}}}}")]
		public void Parse_TyposAndLooseValues_PassLenient_AndFailStrict(string json)
		{
			Assert.DoesNotThrow(() => Parse(json));
			AssertInvalid(json, true);
		}

		[Test]
		public void Parse_JsonNullValues_AreTreatedAsAbsent()
		{
			var node = Parse(WithNode("{\"platforms\":null,\"review\":null,\"endpoints\":null,\"maintenance\":null,\"features\":null}")).Versions["1.0"];

			Assert.AreEqual(0, node.Platforms.Count);
			Assert.IsFalse(node.IsReview);
			Assert.IsNull(node.Maintenance);
			Assert.AreEqual("{}", node.FeaturesJson);
		}

		[Test]
		public void Parse_Utf8Bom_IsAccepted_AndInvalidUtf8_IsRejected()
		{
			var json = Encoding.UTF8.GetBytes(WithNode("{\"platforms\":{}}"));
			var withBom = new byte[json.Length + 3];
			withBom[0] = 0xEF;
			withBom[1] = 0xBB;
			withBom[2] = 0xBF;
			Buffer.BlockCopy(json, 0, withBom, 3, json.Length);

			Assert.AreEqual(1, AppSettingPayload.Parse(withBom, true).Revision);

			var broken = (byte[]) json.Clone();
			broken[broken.Length - 3] = 0xFF;
			var exception = Assert.Throws<AppSettingDocumentException>(() => AppSettingPayload.Parse(broken, false));
			StringAssert.Contains("UTF-8", exception.Message);
		}

		[Test]
		public void Parse_DateLikeFeatureStrings_StayStrings()
		{
			// Newtonsoft 기본값은 날짜처럼 보이는 문자열을 지역 시간 DateTime 으로 바꿔 다시 쓴다
			var node = Parse(WithNode("{\"features\":{\"eventEnd\":\"2026-10-14T02:00:00Z\"}}")).Versions["1.0"];

			Assert.AreEqual("{\"eventEnd\":\"2026-10-14T02:00:00Z\"}", node.FeaturesJson);
		}

		[Test]
		public void Features_AreTheSignedText_EvenWithEscapedNamesAndUnusualNumbers()
		{
			const string features = "{\"tiny\": 1e-30, \"huge\": 1E+30, \"precise\": 0.1234567890123456789012345678, \"text\": \"a\\\"}b\"}";
			var json = "{\"revision\":1,\"versions\":{\"a\":{\"platforms\":{}},\"b\":{\"platforms\":{},\"feat\\u0075res\":" + features + "}}}";

			var document = Parse(json, true);

			Assert.AreEqual("{}", document.Versions["a"].FeaturesJson);
			Assert.AreEqual(features, document.Versions["b"].FeaturesJson);
		}

		[TestCase("{\"x\":1e400}")]
		[TestCase("{\"x\":-1e400}")]
		[TestCase("{\"x\":NaN}")]
		[TestCase("{\"x\":Infinity}")]
		[TestCase("{\"x\":1,}")]
		[TestCase("{\"x\":[1,]}")]
		[TestCase("{'x':1}")]
		[TestCase("{x:1}")]
		[TestCase("{\"x\":01}")]
		[TestCase("{\"x\":1.}")]
		[TestCase("{\"x\":.5}")]
		[TestCase("{\"x\":+1}")]
		[TestCase("{\"x\":1 /* note */}")]
		[TestCase("{\"x\":1} // note")]
		[TestCase("{\"x\":\"a\\qb\"}")]
		[TestCase("{\"x\":\"a\tb\"}")]
		public void Parse_NonStandardJson_IsRejectedInBothModes(string features)
		{
			var json = WithNode("{\"platforms\":{},\"features\":" + features + "}");

			AssertInvalid(json);
			AssertInvalid(json, true);
		}

		[TestCase("\u00a0")]
		[TestCase("\u000b")]
		[TestCase("\u2028")]
		[TestCase("\u3000")]
		public void Parse_NonStandardWhitespaceAroundTheRoot_IsRejectedInBothModes(string space)
		{
			var json = WithNode("{\"platforms\":{}}");

			AssertInvalid(json + space);
			AssertInvalid(json + space, true);
			AssertInvalid(space + json);
			AssertInvalid(space + json, true);
		}

		[Test]
		public void Parse_StandardWhitespaceAroundTheRoot_IsAccepted()
		{
			Assert.AreEqual(1, Parse(" \t\r\n" + WithNode("{\"platforms\":{}}") + " \t\r\n", true).Revision);
		}

		[Test]
		public void Parse_StandardJsonEdgeCases_AreAccepted()
		{
			var features = "{\"a\":[],\"b\":{},\"c\":[1,-0,0.5,-2.5E-3,1e+2,true,false,null,\"\\u00e9\\n\\/\"],\"d\":\"\"}";

			Assert.AreEqual(features, Parse(WithNode("{\"platforms\":{},\"features\":" + features + "}"), true).Versions["1.0"].FeaturesJson);
		}

		private const string Sha = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08";

		private static string WithResource(string category, string entry)
		{
			return WithNode("{\"platforms\":{},\"resources\":{\"" + category + "\":" + entry + "}}");
		}

		private static string Entry(string version = "20261014-1", string size = "4", string sha = Sha, string url = "https://cdn.example.com/tables/20261014-1.json")
		{
			return "{\"version\":\"" + version + "\",\"size\":" + size + ",\"sha256\":\"" + sha + "\",\"url\":\"" + url + "\"}";
		}

		[TestCase(false)]
		[TestCase(true)]
		public void Resources_AreReadWithLowercaseHash(bool strict)
		{
			var resource = Parse(WithResource("tables", Entry()), strict).Versions["1.0"].Resources["tables"];

			Assert.AreEqual("20261014-1", resource.Version);
			Assert.AreEqual(4, resource.Size);
			Assert.AreEqual(Sha.ToLowerInvariant(), resource.Sha256);
			Assert.AreEqual("https://cdn.example.com/tables/20261014-1.json", resource.Url);
			Assert.AreEqual(0, Parse(WithNode("{\"platforms\":{}}"), strict).Versions["1.0"].Resources.Count);
		}

		[TestCase("con", null, null, null, null)]
		[TestCase("tables", "nul.1", null, null, null)]
		[TestCase("tables", "v1.", null, null, null)]
		[TestCase("Tables", null, null, null, null)]
		[TestCase("tables", "V1", null, null, null)]
		[TestCase("..", null, null, null, null)]
		[TestCase("a/b", null, null, null, null)]
		[TestCase("a b", null, null, null, null)]
		[TestCase("0123456789012345678901234567890123456789012345678901234567890123x", null, null, null, null)]
		[TestCase("tables", "../up", null, null, null)]
		[TestCase("tables", ".", null, null, null)]
		[TestCase("tables", "v\\\\1", null, null, null)]
		[TestCase("tables", null, "-1", null, null)]
		[TestCase("tables", null, "1.5", null, null)]
		[TestCase("tables", null, null, "abc", null)]
		[TestCase("tables", null, null, "zz86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", null)]
		[TestCase("tables", null, null, null, "/tables.json")]
		[TestCase("tables", null, null, null, "ftp://cdn.example.com/t.json")]
		[TestCase("tables", null, null, null, "file:///etc/hosts")]
		public void Resources_InvalidEntry_IsInvalidInBothModes(string category, string version, string size, string sha, string url)
		{
			var json = WithResource(category, Entry(version ?? "20261014-1", size ?? "4", sha ?? Sha, url ?? "https://cdn.example.com/t.json"));

			AssertInvalid(json);
			AssertInvalid(json, true);
		}

		[Test]
		public void Resources_UnknownProperty_PassesLenient_AndFailsStrict()
		{
			var json = WithResource("tables", Entry().TrimEnd('}') + ",\"note\":\"x\"}");

			Assert.DoesNotThrow(() => Parse(json));
			AssertInvalid(json, true);
		}

		[Test]
		public void PayloadHash_SameBytesSame_DifferentBytesDifferent()
		{
			var first = Parse(WithNode("{}"));
			var same = Parse(WithNode("{}"));
			var spaced = Parse(WithNode("{ }"));

			Assert.AreEqual(first.PayloadHash, same.PayloadHash);
			Assert.AreNotEqual(first.PayloadHash, spaced.PayloadHash);
		}
	}
}
