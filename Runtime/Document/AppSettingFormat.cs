using System;
using System.Text;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 서명된 설정 문서(봉투)의 형식 상수와 서명 입력입니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	internal static class AppSettingFormat
	{
		/// <summary>봉투의 <c>format</c> 값이자 서명 입력의 앞부분입니다. 형식이 바뀌면 새 이름을 씁니다.</summary>
		public const string Name = "geuneda.appsetting.v1";

		/// <summary>잘못된 UTF-8 을 대체 문자로 바꾸지 않고 예외로 거절합니다.</summary>
		public static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

		/// <summary>
		/// 서명하는 바이트입니다: 형식 이름(ASCII), 0x00, 본문. 같은 키로 만든 다른 용도의 서명을 설정 문서로 쓰지 못하게 앞에 형식 이름을 붙입니다.
		/// </summary>
		public static byte[] SigningInput(byte[] payload)
		{
			var prefix = Encoding.ASCII.GetBytes(Name);
			var input = new byte[prefix.Length + 1 + payload.Length];
			Buffer.BlockCopy(prefix, 0, input, 0, prefix.Length);
			input[prefix.Length] = 0;
			Buffer.BlockCopy(payload, 0, input, prefix.Length + 1, payload.Length);
			return input;
		}
	}
}
