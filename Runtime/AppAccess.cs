// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary><see cref="AppSetting.GetAccess"/> 의 접속 판정입니다. 이 앱 버전·플랫폼의 상태와 점검 공지, 더 높은 공개 버전으로 정합니다.</summary>
	public enum AppAccess
	{
		/// <summary>로그인으로 진행합니다.</summary>
		Open,

		/// <summary>내부 공개 상태입니다. 로그인으로 진행하고 테스터인지는 게임 서버가 판단합니다.</summary>
		Internal,

		/// <summary>점검 중입니다. <see cref="AppSetting.Maintenance"/> 의 문구를 보여 줍니다(없을 수 있음).</summary>
		Maintenance,

		/// <summary>이 버전은 더 쓸 수 없습니다. 스토어로 보냅니다. 등록되지 않은 버전(<c>*</c> 노드도 없음)도 여기에 해당합니다.</summary>
		UpdateRequired
	}
}
