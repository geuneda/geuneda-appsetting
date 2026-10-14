using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Geuneda.AppSetting.Server
{
	public static class AppSettingServerExtensions
	{
		/// <summary>
		/// 서명된 설정 문서 배포를 등록합니다. 엔드포인트는 <c>app.MapGeunedaAppSetting()</c> 으로 연결합니다.
		/// </summary>
		/// <example>
		/// <code>
		/// builder.Services.AddGeunedaAppSetting(options =>
		/// {
		///     options.DocumentPath = "appsetting.json";
		///     foreach (var key in AppSettingPublicKeys.FromJson(File.ReadAllText("public-keys.json"))) options.PublicKeys.Add(key);
		/// });
		/// app.MapGeunedaAppSetting("/appsetting");
		/// </code>
		/// </example>
		public static IServiceCollection AddGeunedaAppSetting(this IServiceCollection services, Action<AppSettingServerOptions> configure)
		{
			ArgumentNullException.ThrowIfNull(services);
			ArgumentNullException.ThrowIfNull(configure);

			services.Configure(configure);
			services.TryAddSingleton(TimeProvider.System);
			services.TryAddSingleton<AppSettingFileSource>();
			return services;
		}

		/// <summary>
		/// 문서를 GET 으로 내려줍니다. 응답에 ETag(문서 바이트의 SHA-256)와 Cache-Control 을 붙이고, <c>If-None-Match</c> 가 맞으면 304 입니다.
		/// 설정과 문서를 여기서 읽고 검증해, 잘못되면 시작할 때 예외를 던집니다.
		/// </summary>
		/// <exception cref="InvalidOperationException">AddGeunedaAppSetting 을 부르지 않았거나, 설정이 잘못되었거나, 문서를 읽거나 검증하지 못한 경우</exception>
		public static IEndpointConventionBuilder MapGeunedaAppSetting(this IEndpointRouteBuilder endpoints, string pattern = "/appsetting")
		{
			ArgumentNullException.ThrowIfNull(endpoints);

			var registrations = endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>();

			if (!registrations.IsService(typeof(AppSettingFileSource)))
			{
				throw new InvalidOperationException("Call services.AddGeunedaAppSetting(...) before MapGeunedaAppSetting");
			}

			var source = endpoints.ServiceProvider.GetRequiredService<AppSettingFileSource>();
			source.Load();
			return endpoints.MapGet(pattern, context => WriteAsync(context, source));
		}

		private static Task WriteAsync(HttpContext context, AppSettingFileSource source)
		{
			var snapshot = source.GetCurrent();
			var response = context.Response;
			response.Headers.ETag = snapshot.ETag;
			response.Headers.CacheControl = source.CacheControl;

			if (Matches(context.Request.Headers.IfNoneMatch, snapshot.ETag))
			{
				response.StatusCode = StatusCodes.Status304NotModified;
				return Task.CompletedTask;
			}

			response.ContentType = "application/json";
			response.ContentLength = snapshot.Bytes.Length;
			return response.Body.WriteAsync(snapshot.Bytes, context.RequestAborted).AsTask();
		}

		private static bool Matches(StringValues ifNoneMatch, string etag)
		{
			// If-None-Match 는 약한 비교(RFC 9110 13.1.2). 프록시가 W/ 를 붙여도 같은 문서로 본다
			if (StringValues.IsNullOrEmpty(ifNoneMatch) || !EntityTagHeaderValue.TryParseList(ifNoneMatch, out var tags))
			{
				return false;
			}

			var current = EntityTagHeaderValue.Parse(etag);
			return tags.Any(tag => tag.Tag.Equals("*") || tag.Compare(current, false));
		}
	}
}
