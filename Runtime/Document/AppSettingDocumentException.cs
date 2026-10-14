using System;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 설정 문서를 읽거나 검증하지 못했습니다. <see cref="Code"/> 는 <see cref="AppSettingResultCodes"/> 의 값입니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>클라이언트(<c>AppSettingService</c>)는 이 예외를 <c>Result</c> 실패로 바꿔 돌려주고, 서명 도구와 서버 헬퍼는 그대로 던집니다.</remarks>
	public sealed class AppSettingDocumentException : Exception
	{
		public int Code { get; }

		public AppSettingDocumentException(int code, string message) : base(message)
		{
			Code = code;
		}
	}
}
