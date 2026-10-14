using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Geuneda.AppSetting.Server
{
	/// <summary>배포할 리소스 파일로 본문의 <c>resources</c> 항목을 만듭니다.</summary>
	public static class AppSettingResources
	{
		/// <summary>
		/// 파일의 크기·SHA-256 으로 <c>{"카테고리": {"version", "size", "sha256", "url"}}</c> JSON 을 만듭니다. 이름·주소는 본문 검사(엄격)와 같은 규칙으로 확인합니다.
		/// </summary>
		/// <exception cref="AppSettingDocumentException">카테고리·버전 이름이나 주소가 규칙에 맞지 않는 경우</exception>
		/// <exception cref="IOException">파일을 읽지 못한 경우</exception>
		public static string Describe(string category, string version, string url, string filePath)
		{
			ArgumentNullException.ThrowIfNull(filePath);

			long size;
			string sha256;

			using (var stream = File.OpenRead(filePath))
			{
				size = stream.Length;
				sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
			}

			var entry = new JObject { ["version"] = version, ["size"] = size, ["sha256"] = sha256, ["url"] = url };
			var described = new JObject { [category ?? string.Empty] = entry };

			// 본문에 넣었을 때 서명 도구가 받아들이는지 같은 코드로 확인한다
			var probe = new JObject
			{
				["revision"] = 1,
				["versions"] = new JObject { ["*"] = new JObject { ["platforms"] = new JObject(), ["resources"] = described.DeepClone() } }
			};
			AppSettingPayload.Parse(Encoding.UTF8.GetBytes(probe.ToString(Formatting.None)), true);

			return described.ToString(Formatting.Indented);
		}
	}
}
