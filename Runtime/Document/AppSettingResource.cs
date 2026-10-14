using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 버전 노드의 <c>resources</c> 항목 하나입니다(리소스 매니페스트). 문서 서명이 크기·해시를 보장하므로 받은 파일은 이 값으로 검증합니다.
	/// 바꾸지 않는 객체입니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>com.geuneda.resource 의 <c>ResourceEntry</c> 로 옮겨 받습니다. 두 패키지는 서로 의존하지 않습니다.</remarks>
	public sealed class AppSettingResource
	{
		// Windows 장치 이름은 확장자를 붙여도(con.1) 파일 이름으로 쓸 수 없다
		private static readonly string[] ReservedNames =
		{
			"con", "prn", "aux", "nul",
			"com0", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
			"lpt0", "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"
		};

		/// <summary>파일 버전입니다. 카테고리와 함께 캐시 파일 이름이 되므로 <c>[a-z0-9._-]</c> 1~64자이고 <c>.</c>·<c>..</c>, 끝의 점, Windows 장치 이름(<c>con</c> 등, 뒤에 점이 붙어도)은 안 됩니다. 대소문자를 가리지 않는 파일 시스템에서 겹치지 않게 소문자만 받습니다.</summary>
		public string Version { get; }

		/// <summary>파일 크기(바이트)입니다.</summary>
		public long Size { get; }

		/// <summary>파일 SHA-256(소문자 16진수 64자)입니다.</summary>
		public string Sha256 { get; }

		/// <summary>파일 주소(호스트가 있는 http·https 절대 주소)입니다.</summary>
		public string Url { get; }

		internal AppSettingResource(string version, long size, string sha256, string url)
		{
			Version = version;
			Size = size;
			Sha256 = sha256;
			Url = url;
		}

		/// <summary>카테고리·버전 이름 규칙입니다. 파일 이름으로 쓰여 경로를 벗어나거나 플랫폼마다 다른 파일이 되지 않게 합니다. com.geuneda.resource 의 <c>ResourceEntry</c> 와 같아야 합니다.</summary>
		internal static bool IsValidName(string name)
		{
			if (string.IsNullOrEmpty(name) || name.Length > 64 || name == "." || name == "..")
			{
				return false;
			}

			foreach (var c in name)
			{
				if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-'))
				{
					return false;
				}
			}

			// 끝의 점은 Windows 가 지워 다른 이름이 된다. 첫 점 앞이 장치 이름이면 예약 이름이다
			if (name[name.Length - 1] == '.')
			{
				return false;
			}

			var dot = name.IndexOf('.');
			return Array.IndexOf(ReservedNames, dot < 0 ? name : name.Substring(0, dot)) < 0;
		}

		/// <summary>16진수 64자면 소문자로 바꿔 돌려주고, 아니면 null 입니다.</summary>
		internal static string NormalizeSha256(string value)
		{
			if (value == null || value.Length != 64)
			{
				return null;
			}

			foreach (var c in value)
			{
				if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F'))
				{
					return null;
				}
			}

			return value.ToLowerInvariant();
		}
	}
}
