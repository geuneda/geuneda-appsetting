using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Geuneda.AppSetting.Server
{
	/// <summary>
	/// 공개 키 목록 파일(<c>[{"keyId", "modulus", "exponent"}]</c>)과 Unity 코드 출력입니다. 배포 서버와 <c>verify</c> 명령이 이 파일을 읽습니다.
	/// </summary>
	public static class AppSettingPublicKeys
	{
		public static string ToJson(IEnumerable<AppSettingPublicKey> keys)
		{
			ArgumentNullException.ThrowIfNull(keys);

			var array = new JArray(keys.Select(key => new JObject
			{
				["keyId"] = key.KeyId,
				["modulus"] = key.Modulus,
				["exponent"] = key.Exponent
			}));
			return array.ToString(Formatting.Indented);
		}

		/// <exception cref="FormatException">JSON 배열이 아니거나 항목에 값이 없는 경우</exception>
		/// <exception cref="ArgumentException">키로 쓸 수 없는 값(2048 비트 미만 등)이거나 키 ID 가 겹치는 경우</exception>
		public static IReadOnlyList<AppSettingPublicKey> FromJson(string json)
		{
			ArgumentNullException.ThrowIfNull(json);
			JArray array;

			try
			{
				// 날짜 모양 키 ID(2026-10-14T02:00:00Z)가 DateTime 으로 바뀌어 다른 문자열이 되지 않게 한다(봉투도 같은 설정으로 읽음)
				using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
				array = JArray.Load(reader);

				// 두 파일을 이어 붙인 경우처럼 배열 뒤에 내용이 있으면 첫 배열만 읽고 넘어가지 않는다
				if (reader.Read())
				{
					throw new FormatException("Public keys file has content after the JSON array");
				}
			}
			catch (JsonException exception)
			{
				throw new FormatException($"Public keys must be a JSON array: {exception.Message}");
			}

			var keys = new List<AppSettingPublicKey>();

			foreach (var token in array)
			{
				var entry = token as JObject ?? throw new FormatException("Each public key must be a JSON object");
				keys.Add(new AppSettingPublicKey(StringField(entry, "keyId"), StringField(entry, "modulus"), StringField(entry, "exponent")));
			}

			if (keys.Select(key => key.KeyId).Distinct(StringComparer.Ordinal).Count() != keys.Count)
			{
				throw new ArgumentException("Public keys need distinct key ids", nameof(json));
			}

			return keys;
		}

		/// <summary>Unity 부트스트랩에 붙여 넣을 코드입니다. 키 ID 는 C# 문자열 리터럴로 이스케이프합니다.</summary>
		public static string ToCSharp(AppSettingPublicKey key)
		{
			ArgumentNullException.ThrowIfNull(key);
			return $"options.PublicKeys.Add(new AppSettingPublicKey({CSharpLiteral(key.KeyId)}, \"{key.Modulus}\", \"{key.Exponent}\"));";
		}

		private static string StringField(JObject entry, string name)
		{
			var token = entry[name];
			return token?.Type == JTokenType.String ? (string) token : throw new FormatException($"Each public key needs a string {name}");
		}

		private static string CSharpLiteral(string value)
		{
			var literal = new StringBuilder("\"");

			foreach (var c in value)
			{
				switch (c)
				{
					case '"': literal.Append("\\\""); break;
					case '\\': literal.Append("\\\\"); break;
					default:
						if (char.IsControl(c) || char.IsSurrogate(c) || c > 0x7E)
						{
							literal.Append("\\u").Append(((int) c).ToString("x4", CultureInfo.InvariantCulture));
						}
						else
						{
							literal.Append(c);
						}

						break;
				}
			}

			return literal.Append('"').ToString();
		}
	}
}
