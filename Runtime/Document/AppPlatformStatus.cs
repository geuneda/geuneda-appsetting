// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 앱 버전·플랫폼별 서버 상태입니다. 문서에는 소문자 문자열(<c>open</c>, <c>internal</c>, <c>closed</c>)로 씁니다.
	/// 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	public enum AppPlatformStatus
	{
		/// <summary>
		/// 닫힘입니다. 더 높은 공개 버전이 있으면 업데이트 필요, 없으면 점검으로 판정합니다. 문서에 없는 플랫폼과 이 버전이 모르는 상태 문자열도 닫힘입니다.
		/// </summary>
		Closed = 0,

		/// <summary>모두 접속할 수 있습니다. 점검 공지의 시간 안이면 점검으로 판정합니다.</summary>
		Open = 1,

		/// <summary>
		/// 내부 공개입니다. 클라이언트는 점검 공지와 관계없이 로그인으로 진행하고, 테스터인지는 게임 서버가 판단합니다. 다른 버전의 '새 버전' 목록에 넣지 않습니다.
		/// </summary>
		Internal = 2
	}
}
