using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 문서 JSON 을 읽는 도구입니다. 형식 오류는 <see cref="AppSettingResultCodes.InvalidDocument"/> 예외입니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>JSON <c>null</c> 값은 없는 값으로 봅니다. 날짜 문자열을 자동 변환하지 않습니다(Newtonsoft 기본값은 지역 시간으로 바꿈).</remarks>
	internal static class AppSettingJson
	{
		private static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
		{
			DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
		};

		// 시간대가 없는 시각은 기기마다 다르게 읽히므로 Z 나 오프셋을 요구한다
		private static readonly string[] UtcFormats =
		{
			"yyyy-MM-dd'T'HH:mm:ss'Z'",
			"yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
			"yyyy-MM-dd'T'HH:mm:sszzz",
			"yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
		};

		public static AppSettingDocumentException Invalid(string message)
		{
			return new AppSettingDocumentException(AppSettingResultCodes.InvalidDocument, message);
		}

		/// <summary>UTF-8(앞의 BOM 허용) JSON 객체 하나를 읽습니다. 같은 이름의 속성이 두 번 있거나 뒤에 다른 내용이 있으면 거절합니다.</summary>
		public static JObject ParseObject(byte[] utf8, string what)
		{
			return ParseObject(DecodeUtf8(utf8, what), what);
		}

		/// <summary>엄격한 UTF-8 로 읽고 앞의 BOM 을 뺍니다.</summary>
		public static string DecodeUtf8(byte[] utf8, string what)
		{
			string text;

			try
			{
				text = AppSettingFormat.StrictUtf8.GetString(utf8);
			}
			catch (DecoderFallbackException)
			{
				throw Invalid($"{what} is not valid UTF-8");
			}

			return text.Length > 0 && text[0] == '\uFEFF' ? text.Substring(1) : text;
		}

		/// <summary>JSON 객체 하나를 읽습니다. 같은 이름의 속성이 두 번 있거나 뒤에 다른 내용이 있으면 거절합니다.</summary>
		public static JObject ParseObject(string text, string what)
		{
			try
			{
				using (var reader = new JsonTextReader(new StringReader(text)))
				{
					reader.DateParseHandling = DateParseHandling.None;
					// 구조 확인용이다. 기능 플래그는 원문을 따로 꺼내므로(AppSettingStrictJson) 실수는 범위가 넓은 double 로 읽는다
					reader.FloatParseHandling = FloatParseHandling.Double;

					var token = JToken.ReadFrom(reader, LoadSettings);

					if (reader.Read())
					{
						throw Invalid($"{what} has content after the JSON value");
					}

					return token as JObject ?? throw Invalid($"{what} must be a JSON object");
				}
			}
			catch (JsonException exception)
			{
				throw Invalid($"{what} is not valid JSON: {exception.Message}");
			}
		}

		/// <summary>값입니다. 없거나 JSON null 이면 null 입니다.</summary>
		public static JToken Get(JObject owner, string name)
		{
			var token = owner[name];
			return token == null || token.Type == JTokenType.Null ? null : token;
		}

		/// <summary>엄격한 검사(서명 도구)에서 오타를 잡기 위해 모르는 속성을 거절합니다.</summary>
		public static void RejectUnknown(JObject owner, string path, params string[] known)
		{
			foreach (var property in owner.Properties())
			{
				if (!known.Contains(property.Name, StringComparer.Ordinal))
				{
					throw Invalid($"{path} has unknown property '{property.Name}' (known: {string.Join(", ", known)})");
				}
			}
		}

		public static string OptionalString(JObject owner, string name, string path)
		{
			var token = Get(owner, name);

			if (token == null)
			{
				return null;
			}

			return token.Type == JTokenType.String ? (string) token : throw Invalid($"{path}.{name} must be a string");
		}

		/// <summary>비어 있지 않은 문자열입니다.</summary>
		public static string RequireString(JObject owner, string name, string path)
		{
			var value = OptionalString(owner, name, path);
			return string.IsNullOrEmpty(value) ? throw Invalid($"{path}.{name} is required") : value;
		}

		public static JObject OptionalObject(JObject owner, string name, string path)
		{
			var token = Get(owner, name);

			if (token == null)
			{
				return null;
			}

			return token as JObject ?? throw Invalid($"{path}.{name} must be an object");
		}

		public static JObject RequireObject(JObject owner, string name, string path)
		{
			return OptionalObject(owner, name, path) ?? throw Invalid($"{path}.{name} is required");
		}

		public static JArray RequireArray(JObject owner, string name, string path)
		{
			var token = Get(owner, name) ?? throw Invalid($"{path}.{name} is required");
			return token as JArray ?? throw Invalid($"{path}.{name} must be an array");
		}

		public static bool OptionalBool(JObject owner, string name, string path)
		{
			var token = Get(owner, name);

			if (token == null)
			{
				return false;
			}

			return token.Type == JTokenType.Boolean ? (bool) token : throw Invalid($"{path}.{name} must be true or false");
		}

		public static long RequireLong(JObject owner, string name, string path)
		{
			var token = Get(owner, name) ?? throw Invalid($"{path}.{name} is required");

			if (token.Type != JTokenType.Integer)
			{
				throw Invalid($"{path}.{name} must be an integer");
			}

			try
			{
				return (long) token;
			}
			catch (OverflowException)
			{
				throw Invalid($"{path}.{name} is out of range");
			}
		}

		/// <summary>시간대(Z 나 오프셋)가 있는 ISO 8601 시각을 UTC 로 읽습니다.</summary>
		public static DateTime RequireUtc(JObject owner, string name, string path)
		{
			var text = RequireString(owner, name, path);

			if (!DateTimeOffset.TryParseExact(text, UtcFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
			{
				throw Invalid($"{path}.{name} must be an ISO 8601 time with Z or an offset (e.g. 2026-10-14T02:00:00Z): '{text}'");
			}

			return value.UtcDateTime;
		}
	}
}
