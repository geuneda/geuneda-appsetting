using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;
using Geuneda.Http;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting.Tests
{
	/// <summary>
	/// 설정 문서를 내려주는 가짜 HTTP 전송입니다. <see cref="Document"/> 와 <see cref="ETag"/> 를 주고, 요청의 <c>If-None-Match</c> 가 ETag 와 같으면 304 입니다.
	/// 게이트가 없으면 요청이 동기적으로 끝납니다.
	/// </summary>
	internal sealed class FakeDocumentServer : IHttpTransport
	{
		public const string BaseAddress = "https://config.test/";
		public const string DocumentPath = "appsetting.json";

		public readonly List<HttpRequest> Requests = new List<HttpRequest>();

		public byte[] Document;
		public string ETag;
		public int StatusCode = 200;

		/// <summary>있으면 응답 대신 이 결과를 돌려줍니다(연결 실패 등).</summary>
		public Result<HttpResponse>? Failure;

		/// <summary>있으면 응답하기 전에 이것이 끝날 때까지 기다립니다.</summary>
		public UniTaskCompletionSource Gate;

		public HttpClient CreateClient()
		{
			return new HttpClientBuilder().WithBaseAddress(BaseAddress).WithTransport(this).Build();
		}

		public async UniTask<Result<HttpResponse>> SendAsync(HttpRequest request, CancellationToken cancellationToken)
		{
			Requests.Add(request);

			if (Gate != null)
			{
				await Gate.Task;
			}

			if (Failure.HasValue)
			{
				return Failure.Value;
			}

			var headers = new Dictionary<string, string>();

			if (ETag != null)
			{
				headers["ETag"] = ETag;
			}

			if (ETag != null && request.Headers.TryGetValue("If-None-Match", out var match) && match == ETag)
			{
				return Result<HttpResponse>.Ok(new HttpResponse(request.Uri, 304, Array.Empty<byte>(), headers));
			}

			return Result<HttpResponse>.Ok(new HttpResponse(request.Uri, StatusCode, Document, headers));
		}
	}
}
