using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;
using UnityEngine;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 기본 원격 앱 설정입니다. 부트스트랩에서 만들어 <see cref="IAppSettingService"/> 로 바인딩합니다. 동작 규칙은 <see cref="IAppSettingService"/> 를 봅니다.
	/// </summary>
	public sealed class AppSettingService : IAppSettingService
	{
		private static readonly ModuleLogger Logger = new ModuleLogger("Geuneda.AppSetting");

		private readonly IHttpClient _http;
		private readonly HttpRequest _request;
		private readonly IReadOnlyList<AppSettingPublicKey> _keys;
		private readonly string _appVersion;
		private readonly string _platform;

		private AppSetting _current;
		private string _etag;
		private UniTaskCompletionSource<Result<AppSetting>> _refresh;

		public event Action<AppSetting> Changed;

		/// <summary>메인 스레드에서 만듭니다(<c>Application.version</c> 을 읽음). 요청은 <see cref="RefreshAsync"/> 에서 보냅니다.</summary>
		/// <exception cref="ArgumentNullException"><paramref name="options"/> 가 null 인 경우</exception>
		/// <exception cref="ArgumentException">필수 설정이 없거나, 문서 주소가 주소 형식이 아니거나, 공개 키가 없거나 null·겹치는 키 ID 가 있거나, 제한 시간이 0 이하인 경우</exception>
		public AppSettingService(AppSettingServiceOptions options)
		{
			if (options == null)
			{
				throw new ArgumentNullException(nameof(options));
			}

			if (options.HttpClient == null || string.IsNullOrEmpty(options.DocumentUri))
			{
				throw new ArgumentException("HttpClient and DocumentUri are required", nameof(options));
			}

			if (options.RequestTimeout <= TimeSpan.Zero)
			{
				throw new ArgumentException("RequestTimeout must be positive", nameof(options));
			}

			var keys = new List<AppSettingPublicKey>();
			var keyIds = new HashSet<string>(StringComparer.Ordinal);

			foreach (var key in options.PublicKeys)
			{
				if (key == null || !keyIds.Add(key.KeyId))
				{
					throw new ArgumentException("Public keys must not be null and need distinct key ids", nameof(options));
				}

				keys.Add(key);
			}

			if (keys.Count == 0)
			{
				throw new ArgumentException("At least one public key is required", nameof(options));
			}

			_appVersion = options.AppVersion ?? Application.version;
			_platform = options.Platform ?? AppSettingPlatforms.Current;

			if (string.IsNullOrEmpty(_appVersion) || string.IsNullOrEmpty(_platform))
			{
				throw new ArgumentException("AppVersion and Platform must not be empty", nameof(options));
			}

			_http = options.HttpClient;
			_request = HttpRequest.Get(options.DocumentUri).WithTimeout(options.RequestTimeout);
			_keys = keys;
		}

		public AppSetting Current => _current;

		public async UniTask<Result<AppSetting>> RefreshAsync(CancellationToken cancellationToken = default)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Result<AppSetting>.Fail(ResultCodes.Cancelled, "App setting refresh was cancelled");
			}

			// 진행 중인 요청 하나를 여러 호출이 나눠 받는다(UniTask.Preserve 는 완료 전 다중 대기를 받지 않음)
			var refresh = _refresh;

			if (refresh == null)
			{
				refresh = new UniTaskCompletionSource<Result<AppSetting>>();
				_refresh = refresh;
				RunRefreshAsync(refresh).Forget();
			}

			try
			{
				return await refresh.Task.AttachExternalCancellation(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// 이 호출만 기다리기를 멈춘다. 요청은 제한 시간까지 계속되고 결과는 적용된다
				return Result<AppSetting>.Fail(ResultCodes.Cancelled, "App setting refresh was cancelled");
			}
		}

		private async UniTaskVoid RunRefreshAsync(UniTaskCompletionSource<Result<AppSetting>> refresh)
		{
			Result<AppSetting> result;

			try
			{
				result = await FetchAsync();
			}
			catch (Exception exception)
			{
				ReleaseRefresh(refresh);
				refresh.TrySetException(exception);
				return;
			}

			ReleaseRefresh(refresh);
			refresh.TrySetResult(result);
		}

		private void ReleaseRefresh(UniTaskCompletionSource<Result<AppSetting>> refresh)
		{
			if (ReferenceEquals(_refresh, refresh))
			{
				_refresh = null;
			}
		}

		private async UniTask<Result<AppSetting>> FetchAsync()
		{
			var current = _current;
			var conditional = current != null && _etag != null;
			var request = conditional ? _request.WithHeader("If-None-Match", _etag) : _request;

			// 함께 기다리는 호출이 있어 호출자 토큰을 넘기지 않는다. 요청 제한 시간이 끝을 보장한다
			var sent = await _http.SendAsync(request);

			if (!sent.IsSuccess)
			{
				return Result<AppSetting>.Fail(sent.Code, sent.Message);
			}

			var response = sent.Value;

			if (response.StatusCode == 304)
			{
				return conditional
					? Result<AppSetting>.Ok(current)
					: Result<AppSetting>.Fail(HttpResultCodes.HttpError, $"304 without a conditional request from {response.Uri}");
			}

			if (!response.IsSuccessStatusCode)
			{
				return Result<AppSetting>.Fail(HttpResultCodes.HttpError, $"Unexpected status {response.StatusCode} from {response.Uri}");
			}

			AppSettingDocument document;

			try
			{
				document = AppSettingEnvelope.ReadDocument(response.Body, _keys, false);
			}
			catch (AppSettingDocumentException exception)
			{
				Logger.Warning($"Rejected app setting document from {response.Uri}: {exception.Message}");
				return Result<AppSetting>.Fail(exception.Code, exception.Message);
			}

			if (current != null && document.Revision < current.Revision)
			{
				Logger.Warning($"Ignored app setting revision {document.Revision} older than {current.Revision} from {response.Uri}");
				return Result<AppSetting>.Ok(current);
			}

			if (current != null && document.Revision == current.Revision && document.PayloadHash == current.Document.PayloadHash)
			{
				_etag = ReadETag(response);
				return Result<AppSetting>.Ok(current);
			}

			var setting = new AppSetting(document, _appVersion, _platform);
			_current = setting;
			_etag = ReadETag(response);
			Logger.Info($"Applied {setting}");
			RaiseChanged(setting);
			return Result<AppSetting>.Ok(setting);
		}

		private void RaiseChanged(AppSetting setting)
		{
			foreach (var handler in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
			{
				try
				{
					((Action<AppSetting>) handler)(setting);
				}
				catch (Exception exception)
				{
					Logger.Error(exception);
				}
			}
		}

		private static string ReadETag(HttpResponse response)
		{
			return response.Headers.TryGetValue("ETag", out var etag) && !string.IsNullOrEmpty(etag) ? etag : null;
		}
	}
}
