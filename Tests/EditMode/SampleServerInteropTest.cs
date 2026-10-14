using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting.Tests
{
	/// <summary>
	/// 실제 .NET 샘플 배포 서버(<c>Server~/samples/Geuneda.AppSetting.SampleServer</c>)와의 상호 운용을 확인합니다.
	/// 서버를 띄우고 <c>GEUNEDA_APPSETTING_INTEROP_URL</c>(예: <c>http://127.0.0.1:5399</c>)을 설정한 뒤 이름으로 골라 실행합니다.
	/// </summary>
	[TestFixture]
	[Explicit("샘플 배포 서버가 필요합니다")]
	[Category("Interop")]
	public class SampleServerInteropTest
	{
		private const string UrlVariable = "GEUNEDA_APPSETTING_INTEROP_URL";

		private static string ServerUrl()
		{
			var url = Environment.GetEnvironmentVariable(UrlVariable);

			if (string.IsNullOrEmpty(url))
			{
				Assert.Ignore($"{UrlVariable} is not set");
			}

			return url.TrimEnd('/') + "/";
		}

		[UnityTest]
		public IEnumerator SampleServer_VerifiesConditionalRequestAndPublishedRevision() => UniTask.ToCoroutine(async () =>
		{
			var http = new HttpClientBuilder().WithBaseAddress(ServerUrl()).Build();
			var keys = await http.GetJsonAsync<List<JObject>>("sample/public-keys");
			Assert.IsTrue(keys.IsSuccess, keys.ToString());

			var options = new AppSettingServiceOptions
			{
				HttpClient = http,
				DocumentUri = "appsetting",
				AppVersion = "1.0.0",
				Platform = AppSettingPlatforms.Android
			};

			foreach (var key in keys.Value)
			{
				options.PublicKeys.Add(new AppSettingPublicKey((string) key["keyId"], (string) key["modulus"], (string) key["exponent"]));
			}

			var service = new AppSettingService(options);
			var changed = new List<AppSetting>();
			service.Changed += changed.Add;

			// 처음 받기: UnityWebRequest 로 받은 바이트를 서버 서명 그대로 검증한다
			var first = await service.RefreshAsync();
			Assert.IsTrue(first.IsSuccess, first.ToString());
			var revision = first.Value.Revision;
			Assert.AreEqual(AppAccess.Open, first.Value.GetAccess(DateTime.UtcNow));
			Assert.IsTrue(first.Value.TryGetEndpoint("game", out _));

			// 같은 문서: If-None-Match 에 304 가 오고 같은 설정을 그대로 쓴다
			var second = await service.RefreshAsync();
			Assert.IsTrue(second.IsSuccess, second.ToString());
			Assert.AreSame(first.Value, second.Value);
			Assert.AreEqual(1, changed.Count);

			// 새 판을 배포하면(서버는 1초마다 파일을 다시 읽음) 새 설정이 적용된다
			var payload = "{\"revision\":" + (revision + 1) + ",\"versions\":{\"1.0.0\":{\"platforms\":{\"android\":\"closed\"}}}}";
			var published = await http.SendAsync(HttpRequest.Post("sample/publish").WithBody(Encoding.UTF8.GetBytes(payload), "application/json"));
			Assert.IsTrue(published.IsSuccess && published.Value.StatusCode == 200, published.ToString());
			await UniTask.Delay(TimeSpan.FromSeconds(1.5), DelayType.Realtime);

			var third = await service.RefreshAsync();
			Assert.IsTrue(third.IsSuccess, third.ToString());
			Assert.AreEqual(revision + 1, third.Value.Revision);
			Assert.AreEqual(AppAccess.Maintenance, third.Value.GetAccess(DateTime.UtcNow));
			Assert.AreEqual(2, changed.Count);

			// 서명 도구 규칙에 어긋난 본문은 서버가 거절한다
			var rejected = await http.SendAsync(HttpRequest.Post("sample/publish").WithBody(Encoding.UTF8.GetBytes("{\"revision\":1}"), "application/json"));
			Assert.AreEqual(400, rejected.Value.StatusCode);
		});
	}
}
