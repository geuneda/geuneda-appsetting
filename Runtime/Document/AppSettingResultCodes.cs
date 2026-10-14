// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// appsetting 패키지의 결과 코드입니다(core 결과 코드 범위 표의 4000 블록). 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>
	/// 연결 실패, 타임아웃, 취소는 core 공용 코드를, 2xx·304 가 아닌 응답은 http 의 <c>HttpResultCodes.HttpError</c> 를 씁니다.
	/// </remarks>
	public static class AppSettingResultCodes
	{
		/// <summary>이 패키지 코드 블록의 시작입니다. 코드로는 쓰지 않습니다.</summary>
		public const int RangeStart = 4000;

		/// <summary>문서나 본문을 해석하지 못했거나 규칙에 맞지 않습니다(JSON 오류, 필수 값 없음, 잘못된 날짜 등). 기능 플래그가 게임 형식에 맞지 않을 때도 씁니다.</summary>
		public const int InvalidDocument = RangeStart + 1;

		/// <summary>믿는 키로 만든 맞는 서명이 없습니다.</summary>
		public const int InvalidSignature = RangeStart + 2;

		/// <summary>문서의 <c>format</c> 이 이 버전이 아는 형식이 아닙니다.</summary>
		public const int UnsupportedFormat = RangeStart + 3;
	}
}
