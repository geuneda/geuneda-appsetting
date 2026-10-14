using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 서명한 본문(설정 JSON)을 <see cref="AppSettingDocument"/> 로 읽습니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 관대한 모드(클라이언트)는 새 버전 문서와 섞여 쓰일 수 있어 모르는 속성은 무시하고 모르는 상태 문자열은 <see cref="AppPlatformStatus.Closed"/> 로 봅니다.
	/// 엄격한 모드(서명 도구)는 오타를 막으려고 그것들과 절대 주소가 아닌 서버 주소, 앞뒤 공백이 있는 버전 키도 거절합니다. 그 밖의 규칙은 같습니다.
	/// </para>
	/// </remarks>
	internal static class AppSettingPayload
	{
		/// <exception cref="AppSettingDocumentException"><see cref="AppSettingResultCodes.InvalidDocument"/></exception>
		public static AppSettingDocument Parse(byte[] payload, bool strict)
		{
			var text = AppSettingJson.DecodeUtf8(payload, "payload");
			var root = AppSettingJson.ParseObject(text, "payload");
			// Newtonsoft 가 구조(깊이 제한 포함)를 확인한 뒤 표준 JSON 인지 보고 기능 플래그 원문을 꺼낸다
			var featuresByVersion = AppSettingStrictJson.FeaturesByVersion(text, "payload");

			if (strict)
			{
				AppSettingJson.RejectUnknown(root, "payload", "revision", "versions");
			}

			var revision = AppSettingJson.RequireLong(root, "revision", "payload");

			if (revision < 1)
			{
				throw AppSettingJson.Invalid("payload.revision must be 1 or greater");
			}

			var versionsObject = AppSettingJson.RequireObject(root, "versions", "payload");

			if (versionsObject.Count == 0)
			{
				throw AppSettingJson.Invalid("payload.versions must not be empty");
			}

			var versions = new Dictionary<string, AppVersionSetting>(StringComparer.Ordinal);
			var index = 0;

			foreach (var property in versionsObject.Properties())
			{
				var path = $"payload.versions['{property.Name}']";

				if (property.Name.Length == 0 || (strict && property.Name.Trim().Length != property.Name.Length))
				{
					throw AppSettingJson.Invalid($"{path} has an empty key or surrounding spaces");
				}

				var node = property.Value as JObject ?? throw AppSettingJson.Invalid($"{path} must be an object");
				versions.Add(property.Name, ParseVersion(node, path, strict, featuresByVersion[index++]));
			}

			return new AppSettingDocument(revision, new ReadOnlyDictionary<string, AppVersionSetting>(versions), Hash(payload));
		}

		private static AppVersionSetting ParseVersion(JObject node, string path, bool strict, string rawFeatures)
		{
			if (strict)
			{
				AppSettingJson.RejectUnknown(node, path, "platforms", "review", "endpoints", "maintenance", "features", "resources");
			}

			var platformsObject = strict
				? AppSettingJson.RequireObject(node, "platforms", path)
				: AppSettingJson.OptionalObject(node, "platforms", path);
			var platforms = new Dictionary<string, AppPlatformStatus>(StringComparer.OrdinalIgnoreCase);

			foreach (var property in platformsObject?.Properties() ?? new JProperty[0])
			{
				var platformPath = $"{path}.platforms['{property.Name}']";

				if (property.Name.Length == 0)
				{
					throw AppSettingJson.Invalid($"{platformPath} has an empty platform name");
				}

				if (property.Value.Type != JTokenType.String)
				{
					throw AppSettingJson.Invalid($"{platformPath} must be a string (open, internal, closed)");
				}

				if (!platforms.TryAdd(property.Name, ParseStatus((string) property.Value, platformPath, strict)))
				{
					throw AppSettingJson.Invalid($"{platformPath} is listed twice (platform names ignore case)");
				}
			}

			var endpoints = new Dictionary<string, string>(StringComparer.Ordinal);
			var endpointsObject = AppSettingJson.OptionalObject(node, "endpoints", path);

			foreach (var property in endpointsObject?.Properties() ?? new JProperty[0])
			{
				var endpointPath = $"{path}.endpoints['{property.Name}']";
				var value = property.Value.Type == JTokenType.String ? (string) property.Value : null;

				if (property.Name.Length == 0 || string.IsNullOrEmpty(value))
				{
					throw AppSettingJson.Invalid($"{endpointPath} must have a name and a non-empty string address");
				}

				// Unix 의 .NET·Mono 는 "/path" 를 절대 파일 주소로 읽으므로 호스트까지 본다
				if (strict && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.IsFile || string.IsNullOrEmpty(uri.Host)))
				{
					throw AppSettingJson.Invalid($"{endpointPath} must be an absolute uri with a host: '{value}'");
				}

				endpoints.Add(property.Name, value);
			}

			var resources = new Dictionary<string, AppSettingResource>(StringComparer.Ordinal);
			var resourcesObject = AppSettingJson.OptionalObject(node, "resources", path);

			foreach (var property in resourcesObject?.Properties() ?? new JProperty[0])
			{
				var resourcePath = $"{path}.resources['{property.Name}']";

				if (!AppSettingResource.IsValidName(property.Name))
				{
					throw AppSettingJson.Invalid($"{resourcePath} needs a category of 1-64 characters from a-z 0-9 . _ - (lowercase only, no trailing '.', not a Windows device name such as con)");
				}

				var entry = property.Value as JObject ?? throw AppSettingJson.Invalid($"{resourcePath} must be an object");
				resources.Add(property.Name, ParseResource(entry, resourcePath, strict));
			}

			var maintenanceObject = AppSettingJson.OptionalObject(node, "maintenance", path);
			var features = AppSettingJson.OptionalObject(node, "features", path);

			return new AppVersionSetting(
				new ReadOnlyDictionary<string, AppPlatformStatus>(platforms),
				AppSettingJson.OptionalBool(node, "review", path),
				new ReadOnlyDictionary<string, string>(endpoints),
				new ReadOnlyDictionary<string, AppSettingResource>(resources),
				maintenanceObject == null ? null : ParseMaintenance(maintenanceObject, path + ".maintenance", strict),
				features == null ? "{}" : rawFeatures);
		}

		private static AppSettingResource ParseResource(JObject entry, string path, bool strict)
		{
			if (strict)
			{
				AppSettingJson.RejectUnknown(entry, path, "version", "size", "sha256", "url");
			}

			// 리소스 캐시가 이름을 파일 이름으로 쓰고 크기·해시로 받은 파일을 검증하므로 두 모드 모두 엄격하게 본다
			var version = AppSettingJson.RequireString(entry, "version", path);

			if (!AppSettingResource.IsValidName(version))
			{
				throw AppSettingJson.Invalid($"{path}.version must be 1-64 characters from a-z 0-9 . _ - (lowercase only, no trailing '.', not . .. or a Windows device name such as con): '{version}'");
			}

			var size = AppSettingJson.RequireLong(entry, "size", path);

			if (size < 0)
			{
				throw AppSettingJson.Invalid($"{path}.size must not be negative");
			}

			var sha256 = AppSettingResource.NormalizeSha256(AppSettingJson.RequireString(entry, "sha256", path))
				?? throw AppSettingJson.Invalid($"{path}.sha256 must be 64 hex characters");
			var url = AppSettingJson.RequireString(entry, "url", path);

			if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || string.IsNullOrEmpty(uri.Host))
			{
				throw AppSettingJson.Invalid($"{path}.url must be an absolute http or https address: '{url}'");
			}

			return new AppSettingResource(version, size, sha256, url);
		}

		private static AppPlatformStatus ParseStatus(string value, string path, bool strict)
		{
			switch (value.ToLowerInvariant())
			{
				case "open":
					return AppPlatformStatus.Open;
				case "internal":
					return AppPlatformStatus.Internal;
				case "closed":
					return AppPlatformStatus.Closed;
				default:
					// 새 버전 문서의 상태를 모르면 열지 않는다
					return strict ? throw AppSettingJson.Invalid($"{path} has unknown status '{value}' (open, internal, closed)") : AppPlatformStatus.Closed;
			}
		}

		private static MaintenanceNotice ParseMaintenance(JObject node, string path, bool strict)
		{
			if (strict)
			{
				AppSettingJson.RejectUnknown(node, path, "startAt", "endAt", "defaultLanguage", "messages");
			}

			var startAt = AppSettingJson.RequireUtc(node, "startAt", path);
			var endAt = AppSettingJson.RequireUtc(node, "endAt", path);

			if (endAt <= startAt)
			{
				throw AppSettingJson.Invalid($"{path}.endAt must be after startAt");
			}

			var defaultLanguage = AppSettingJson.RequireString(node, "defaultLanguage", path);
			var messages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (var property in AppSettingJson.RequireObject(node, "messages", path).Properties())
			{
				var messagePath = $"{path}.messages['{property.Name}']";

				if (property.Name.Length == 0 || property.Value.Type != JTokenType.String)
				{
					throw AppSettingJson.Invalid($"{messagePath} must have a language and a string message");
				}

				if (!messages.TryAdd(property.Name, (string) property.Value))
				{
					throw AppSettingJson.Invalid($"{messagePath} is listed twice (languages ignore case)");
				}
			}

			if (!messages.ContainsKey(defaultLanguage))
			{
				throw AppSettingJson.Invalid($"{path}.messages has no message for the default language '{defaultLanguage}'");
			}

			return new MaintenanceNotice(startAt, endAt, defaultLanguage, new ReadOnlyDictionary<string, string>(messages));
		}

		private static string Hash(byte[] payload)
		{
			using (var sha = SHA256.Create())
			{
				return Convert.ToBase64String(sha.ComputeHash(payload));
			}
		}
	}
}
