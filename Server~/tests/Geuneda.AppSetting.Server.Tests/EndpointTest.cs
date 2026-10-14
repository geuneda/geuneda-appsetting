using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Geuneda.AppSetting.Server.Tests
{
	public class EndpointTest
	{
		private static async Task<HttpResponseMessage> GetAsync(AppSettingTestServer server, string ifNoneMatch = null)
		{
			var request = new HttpRequestMessage(HttpMethod.Get, "appsetting");

			if (ifNoneMatch != null)
			{
				request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
			}

			return await server.Http.SendAsync(request);
		}

		private static async Task<long> ServedRevisionAsync(AppSettingTestServer server)
		{
			var response = await GetAsync(server);
			return AppSettingSigner.Verify(await response.Content.ReadAsByteArrayAsync(), new[] { TestKeys.First.PublicKey }).Revision;
		}

		[Fact]
		public async Task Get_ReturnsTheFileBytes_WithETagAndCacheControl()
		{
			var document = TestKeys.Document(1);
			await using var server = await AppSettingTestServer.StartAsync(document);

			var response = await GetAsync(server);

			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			Assert.Equal(document, await response.Content.ReadAsByteArrayAsync());
			Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
			Assert.True(response.Headers.ETag.IsWeak == false && response.Headers.ETag.Tag.Length > 10);
			Assert.True(response.Headers.CacheControl.NoCache);
		}

		[Fact]
		public async Task Get_MatchingIfNoneMatch_Is304WithETag()
		{
			await using var server = await AppSettingTestServer.StartAsync(TestKeys.Document(1));
			var etag = (await GetAsync(server)).Headers.ETag.Tag;

			foreach (var header in new[] { etag, "W/" + etag, "\"other\", " + etag, "*" })
			{
				var response = await GetAsync(server, header);

				Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
				Assert.Equal(etag, response.Headers.ETag.Tag);
				Assert.Empty(await response.Content.ReadAsByteArrayAsync());
			}

			Assert.Equal(HttpStatusCode.OK, (await GetAsync(server, "\"other\"")).StatusCode);
		}

		[Fact]
		public async Task ReplacedFile_IsServedAfterTheCheckInterval()
		{
			await using var server = await AppSettingTestServer.StartAsync(TestKeys.Document(1));
			var etag = (await GetAsync(server)).Headers.ETag.Tag;

			server.Replace(TestKeys.Document(2, "closed"));

			Assert.Equal(1, await ServedRevisionAsync(server));

			server.Time.Advance(AppSettingFileSource.CheckInterval);
			var response = await GetAsync(server, etag);

			Assert.Equal(HttpStatusCode.OK, response.StatusCode);
			Assert.NotEqual(etag, response.Headers.ETag.Tag);
			Assert.Equal(2, await ServedRevisionAsync(server));
			Assert.Equal(1, server.Logs.Count(LogLevel.Information, "revision 2"));
		}

		[Fact]
		public async Task ReplacedFile_SameSizeWithinTheSameSecond_IsStillDetected()
		{
			// 크기·수정 시각이 아니라 내용으로 비교한다
			await using var server = await AppSettingTestServer.StartAsync(TestKeys.Document(1, "open"));
			await GetAsync(server);
			var original = File.GetLastWriteTimeUtc(server.DocumentPath);

			server.Replace(TestKeys.Document(2, "open"));
			File.SetLastWriteTimeUtc(server.DocumentPath, original);
			server.Time.Advance(AppSettingFileSource.CheckInterval);

			Assert.Equal(2, await ServedRevisionAsync(server));
		}

		[Theory]
		[InlineData("signature")]
		[InlineData("broken")]
		[InlineData("lower")]
		public async Task InvalidReplacement_KeepsServingTheCurrentDocument_AndLogsOnce(string kind)
		{
			await using var server = await AppSettingTestServer.StartAsync(TestKeys.Document(5));

			server.Replace(kind switch
			{
				"signature" => TestKeys.Document(6, key: TestKeys.Second),
				"broken" => new byte[] { 1, 2, 3 },
				_ => TestKeys.Document(4)
			});

			for (var i = 0; i < 3; i++)
			{
				server.Time.Advance(AppSettingFileSource.CheckInterval);
				Assert.Equal(5, await ServedRevisionAsync(server));
			}

			Assert.Equal(1, server.Logs.Entries.Count(entry => entry.Level == LogLevel.Error && entry.Message.Contains("still serving revision 5")
				|| entry.Level == LogLevel.Error && entry.Message.Contains("lower than the served 5")));

			// 고친 문서는 다시 받아들인다
			server.Replace(TestKeys.Document(7));
			server.Time.Advance(AppSettingFileSource.CheckInterval);
			Assert.Equal(7, await ServedRevisionAsync(server));
		}

		[Fact]
		public async Task DeletedFile_KeepsServingTheCurrentDocument()
		{
			await using var server = await AppSettingTestServer.StartAsync(TestKeys.Document(3));

			File.Delete(server.DocumentPath);
			server.Time.Advance(AppSettingFileSource.CheckInterval);

			Assert.Equal(3, await ServedRevisionAsync(server));
			server.Time.Advance(AppSettingFileSource.CheckInterval);
			Assert.Equal(3, await ServedRevisionAsync(server));
			Assert.Equal(1, server.Logs.Count(LogLevel.Error, "Cannot read app setting document"));
		}

		[Fact]
		public void Startup_InvalidSetup_Throws()
		{
			var folder = TestKeys.NewFolder();

			try
			{
				File.WriteAllBytes(Path.Combine(folder, "signed-by-k2.json"), TestKeys.Document(1, key: TestKeys.Second));
				var time = new FakeTimeProvider();

				void Map(Action<AppSettingServerOptions> configure)
				{
					using var app = AppSettingTestServer.Build(folder, time, new ListLoggerProvider(), configure);
					app.MapGeunedaAppSetting();
				}

				var untrusted = Assert.Throws<InvalidOperationException>(() => Map(options =>
				{
					options.DocumentPath = "signed-by-k2.json";
					options.PublicKeys.Add(TestKeys.First.PublicKey);
				}));
				Assert.Contains(AppSettingResultCodes.InvalidSignature.ToString(), untrusted.Message);

				Assert.Throws<InvalidOperationException>(() => Map(options =>
				{
					options.DocumentPath = "missing.json";
					options.PublicKeys.Add(TestKeys.First.PublicKey);
				}));
				Assert.Throws<InvalidOperationException>(() => Map(options => options.DocumentPath = "signed-by-k2.json"));
				Assert.Throws<InvalidOperationException>(() => Map(options => options.PublicKeys.Add(TestKeys.Second.PublicKey)));
				Assert.Throws<InvalidOperationException>(() => Map(options =>
				{
					options.DocumentPath = "signed-by-k2.json";
					options.PublicKeys.Add(TestKeys.Second.PublicKey);
					options.PublicKeys.Add(TestKeys.Second.PublicKey);
				}));

				var builder = WebApplication.CreateSlimBuilder();
				using var withoutServices = builder.Build();
				Assert.Throws<InvalidOperationException>(() => withoutServices.MapGeunedaAppSetting());
			}
			finally
			{
				TestKeys.DeleteFolder(folder);
			}
		}
	}
}
