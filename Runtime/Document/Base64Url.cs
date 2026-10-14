using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>패딩 없는 base64url 입니다. 읽을 때는 표준 base64(패딩 포함)도 받습니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.</summary>
	internal static class Base64Url
	{
		public static string Encode(byte[] value)
		{
			return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}

		/// <summary>base64url 이나 base64 를 읽습니다. 형식이 틀리면 null 입니다.</summary>
		public static byte[] TryDecode(string value)
		{
			if (value == null)
			{
				return null;
			}

			var text = value.Replace('-', '+').Replace('_', '/').TrimEnd('=');

			switch (text.Length % 4)
			{
				case 1:
					return null;
				case 2:
					text += "==";
					break;
				case 3:
					text += "=";
					break;
			}

			try
			{
				return Convert.FromBase64String(text);
			}
			catch (FormatException)
			{
				return null;
			}
		}
	}
}
