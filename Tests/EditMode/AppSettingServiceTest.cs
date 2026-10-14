using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using NUnit.Framework;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting.Tests
{
	[TestFixture]
	public class AppSettingServiceTest
	{
		private static readonly TestSigner Signer = TestSigner.Get("k1");
		private static readonly TestSigner Untrusted = TestSigner.Get("k2");

		private FakeDocumentServer _server;
		private ILogSink _originalSink;
		private List<LogEntry> _logs;

		private sealed class ListSink : ILogSink
		{
			private readonly List<LogEntry> _entries;

			public ListSink(List<LogEntry> entries)
			{
				_entries = entries;
			}

			public void Write(in LogEntry entry)
			{
				_entries.Add(entry);
			}
		}

		[SetUp]
		public void SetUp()
		{
			_originalSink = Log.Sink;
			_logs = new List<LogEntry>();
			Log.Sink = new ListSink(_logs);
			_server = new FakeDocumentServer { Document = Document(1, "open"), ETag = "\"e1\"" };
		}

		[TearDown]
		public void TearDown()
		{
			Log.Sink = _originalSink;
		}

		private static byte[] Document(long revision, string status, TestSigner signer = null)
		{
			var payload = "{\"revision\":" + revision + ",\"versions\":{\"1.0.0\":{\"platforms\":{\"android\":\"" + status + "\"}}}}";
			return TestSigner.Document(payload, signer ?? Signer);
		}

		private AppSettingServiceOptions Options()
		{
			var options = new AppSettingServiceOptions
			{
				HttpClient = _server.CreateClient(),
				DocumentUri = FakeDocumentServer.DocumentPath,
				AppVersion = "1.0.0",
				Platform = AppSettingPlatforms.Android,
				RequestTimeout = TimeSpan.FromSeconds(3)
			};
			options.PublicKeys.Add(Signer.PublicKey);
			return options;
		}

		private AppSettingService CreateService()
		{
			return new AppSettingService(Options());
		}

		private static T Run<T>(UniTask<T> task)
		{
			Assert.IsTrue(task.Status.IsCompleted(), "Expected the task to complete synchronously");
			return task.GetAwaiter().GetResult();
		}

		[Test]
		public void Constructor_InvalidOptions_Throws()
		{
			Assert.Throws<ArgumentNullException>(() => new AppSettingService(null));

			var noClient = Options();
			noClient.HttpClient = null;
			Assert.Throws<ArgumentException>(() => new AppSettingService(noClient));

			var noUri = Options();
			noUri.DocumentUri = "";
			Assert.Throws<ArgumentException>(() => new AppSettingService(noUri));

			var noKeys = Options();
			noKeys.PublicKeys.Clear();
			Assert.Throws<ArgumentException>(() => new AppSettingService(noKeys));

			var nullKey = Options();
			nullKey.PublicKeys.Add(null);
			Assert.Throws<ArgumentException>(() => new AppSettingService(nullKey));

			var duplicate = Options();
			duplicate.PublicKeys.Add(Signer.PublicKey);
			Assert.Throws<ArgumentException>(() => new AppSettingService(duplicate));

			var zeroTimeout = Options();
			zeroTimeout.RequestTimeout = TimeSpan.Zero;
			Assert.Throws<ArgumentException>(() => new AppSettingService(zeroTimeout));

			var emptyPlatform = Options();
			emptyPlatform.Platform = "";
			Assert.Throws<ArgumentException>(() => new AppSettingService(emptyPlatform));
		}

		[Test]
		public void Refresh_First_AppliesDocument_RaisesChangedOnce_AndSendsUnconditionalGet()
		{
			var service = CreateService();
			var changed = new List<AppSetting>();
			service.Changed += changed.Add;

			var result = Run(service.RefreshAsync());

			Assert.IsTrue(result.IsSuccess, result.ToString());
			Assert.AreSame(result.Value, service.Current);
			Assert.AreEqual(AppAccess.Open, result.Value.GetAccess(DateTime.UtcNow));
			CollectionAssert.AreEqual(new[] { result.Value }, changed);

			var request = _server.Requests[0];
			Assert.AreEqual(HttpMethod.Get, request.Method);
			Assert.AreEqual(FakeDocumentServer.BaseAddress + FakeDocumentServer.DocumentPath, request.Uri.ToString());
			Assert.AreEqual(TimeSpan.FromSeconds(3), request.Timeout);
			Assert.IsFalse(request.Headers.ContainsKey("If-None-Match"));
		}

		[Test]
		public void Refresh_NotModified_KeepsSameSetting_WithoutChanged()
		{
			var service = CreateService();
			var first = Run(service.RefreshAsync()).Value;
			var changed = 0;
			service.Changed += _ => changed++;

			var second = Run(service.RefreshAsync());

			Assert.AreEqual("\"e1\"", _server.Requests[1].Headers["If-None-Match"]);
			Assert.IsTrue(second.IsSuccess);
			Assert.AreSame(first, second.Value);
			Assert.AreEqual(0, changed);
		}

		[Test]
		public void Refresh_ServerWithoutETag_SameDocument_KeepsSameSetting_WithoutChanged()
		{
			_server.ETag = null;
			var service = CreateService();
			var first = Run(service.RefreshAsync()).Value;
			var changed = 0;
			service.Changed += _ => changed++;

			var second = Run(service.RefreshAsync());

			Assert.IsFalse(_server.Requests[1].Headers.ContainsKey("If-None-Match"));
			Assert.AreSame(first, second.Value);
			Assert.AreEqual(0, changed);
		}

		[Test]
		public void Refresh_NewRevision_ReplacesSetting_AndRaisesChanged()
		{
			var service = CreateService();
			Run(service.RefreshAsync());
			AppSetting changed = null;
			service.Changed += setting => changed = setting;
			_server.Document = Document(2, "closed");
			_server.ETag = "\"e2\"";

			var result = Run(service.RefreshAsync());

			Assert.AreEqual(2, result.Value.Revision);
			Assert.AreSame(result.Value, changed);
			Assert.AreSame(result.Value, service.Current);
			Assert.AreEqual(AppAccess.Maintenance, service.Current.GetAccess(DateTime.UtcNow));
		}

		[Test]
		public void Refresh_SameRevisionDifferentContent_IsApplied()
		{
			var service = CreateService();
			Run(service.RefreshAsync());
			var changed = 0;
			service.Changed += _ => changed++;
			_server.Document = Document(1, "internal");
			_server.ETag = "\"e1b\"";

			var result = Run(service.RefreshAsync());

			Assert.AreEqual(AppPlatformStatus.Internal, result.Value.Status);
			Assert.AreEqual(1, changed);
		}

		[Test]
		public void Refresh_OlderRevision_IsIgnored_AndCurrentIsReturned()
		{
			_server.Document = Document(5, "closed");
			var service = CreateService();
			var current = Run(service.RefreshAsync()).Value;
			var changed = 0;
			service.Changed += _ => changed++;
			_server.Document = Document(4, "open");
			_server.ETag = "\"stale\"";

			var result = Run(service.RefreshAsync());

			Assert.IsTrue(result.IsSuccess);
			Assert.AreSame(current, result.Value);
			Assert.AreEqual(0, changed);
			Assert.IsTrue(_logs.Exists(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("older than 5")));

			// 무시한 문서의 ETag 를 기억하지 않는다(다음 요청은 지금 문서의 ETag 로 묻는다)
			Run(service.RefreshAsync());
			Assert.AreEqual("\"e1\"", _server.Requests[2].Headers["If-None-Match"]);
		}

		[Test]
		public void Refresh_UntrustedSignature_FailsAndKeepsCurrent()
		{
			var service = CreateService();
			var current = Run(service.RefreshAsync()).Value;
			_server.Document = Document(9, "open", Untrusted);
			_server.ETag = "\"e9\"";

			var result = Run(service.RefreshAsync());

			Assert.AreEqual(AppSettingResultCodes.InvalidSignature, result.Code);
			Assert.AreSame(current, service.Current);
		}

		[Test]
		public void Refresh_BrokenDocument_IsInvalidDocument()
		{
			_server.Document = TestSigner.Document("{\"revision\":1}", Signer);

			var result = Run(CreateService().RefreshAsync());

			Assert.AreEqual(AppSettingResultCodes.InvalidDocument, result.Code);
		}

		[Test]
		public void Refresh_ServerError_IsHttpError_AndKeepsCurrent()
		{
			var service = CreateService();
			var current = Run(service.RefreshAsync()).Value;
			_server.StatusCode = 503;
			_server.ETag = null;

			var result = Run(service.RefreshAsync());

			Assert.AreEqual(HttpResultCodes.HttpError, result.Code);
			StringAssert.Contains("503", result.Message);
			Assert.AreSame(current, service.Current);
		}

		[Test]
		public void Refresh_NotModifiedWithoutConditionalRequest_IsHttpError()
		{
			_server.StatusCode = 304;

			var result = Run(CreateService().RefreshAsync());

			Assert.AreEqual(HttpResultCodes.HttpError, result.Code);
		}

		[TestCase(ResultCodes.NetworkError)]
		[TestCase(ResultCodes.Timeout)]
		public void Refresh_TransportFailure_IsPassedThrough(int code)
		{
			_server.Failure = Result<HttpResponse>.Fail(code, "down");
			var service = CreateService();

			var result = Run(service.RefreshAsync());

			Assert.AreEqual(code, result.Code);
			Assert.IsNull(service.Current);
		}

		[Test]
		public void Refresh_ConcurrentCalls_ShareOneRequest()
		{
			_server.Gate = new UniTaskCompletionSource();
			var service = CreateService();

			var first = service.RefreshAsync();
			var second = service.RefreshAsync();
			Assert.AreEqual(1, _server.Requests.Count);

			_server.Gate.TrySetResult();

			Assert.AreSame(Run(first).Value, Run(second).Value);
			Assert.AreEqual(1, _server.Requests.Count);

			// 끝난 뒤의 호출은 새 요청을 보낸다
			_server.Gate = null;
			Run(service.RefreshAsync());
			Assert.AreEqual(2, _server.Requests.Count);
		}

		[Test]
		public void Refresh_CallerCancels_OnlyThatCallStops_AndResultIsStillApplied()
		{
			_server.Gate = new UniTaskCompletionSource();
			var service = CreateService();
			var cancellation = new CancellationTokenSource();

			var cancelled = service.RefreshAsync(cancellation.Token);
			var other = service.RefreshAsync();
			cancellation.Cancel();

			Assert.AreEqual(ResultCodes.Cancelled, Run(cancelled).Code);
			Assert.IsFalse(other.Status.IsCompleted());

			_server.Gate.TrySetResult();

			Assert.IsTrue(Run(other).IsSuccess);
			Assert.IsNotNull(service.Current);
		}

		[Test]
		public void Refresh_AlreadyCancelledToken_DoesNotSendRequest()
		{
			var service = CreateService();

			var result = Run(service.RefreshAsync(new CancellationToken(true)));

			Assert.AreEqual(ResultCodes.Cancelled, result.Code);
			Assert.AreEqual(0, _server.Requests.Count);
		}

		[Test]
		public void Refresh_ChangedHandlerThrows_OtherHandlersRun_AndRefreshSucceeds()
		{
			var service = CreateService();
			var reached = false;
			service.Changed += _ => throw new InvalidOperationException("boom");
			service.Changed += _ => reached = true;

			var result = Run(service.RefreshAsync());

			Assert.IsTrue(result.IsSuccess);
			Assert.IsTrue(reached);
			Assert.IsTrue(_logs.Exists(entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException));
		}
	}
}
