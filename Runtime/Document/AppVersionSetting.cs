using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 앱 버전 하나(또는 <c>*</c> 기본 노드)의 설정입니다. 바꾸지 않는 객체입니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	public sealed class AppVersionSetting
	{
		/// <summary>플랫폼 이름(<see cref="AppSettingPlatforms"/>, 대소문자 무시)별 상태입니다. 없는 플랫폼은 <see cref="AppPlatformStatus.Closed"/> 입니다.</summary>
		public IReadOnlyDictionary<string, AppPlatformStatus> Platforms { get; }

		/// <summary>스토어 심사 중인 빌드입니다. 다른 버전의 '새 버전' 목록에 넣지 않습니다.</summary>
		public bool IsReview { get; }

		/// <summary>이름별 서버 주소입니다(예: <c>game</c>, <c>realtime</c>). 이름은 대소문자를 가립니다.</summary>
		public IReadOnlyDictionary<string, string> Endpoints { get; }

		/// <summary>카테고리(대소문자 구분)별 리소스 매니페스트 항목입니다. 없으면 비어 있습니다.</summary>
		public IReadOnlyDictionary<string, AppSettingResource> Resources { get; }

		/// <summary>점검 공지입니다. 없으면 null 입니다.</summary>
		public MaintenanceNotice Maintenance { get; }

		/// <summary>
		/// 게임이 정하는 기능 플래그 JSON 객체입니다. 서명한 본문에 쓴 그대로의 텍스트(공백, 숫자 표기 포함)라 게임 형식으로 읽을 때 숫자가 바뀌지 않습니다. 없으면 <c>{}</c> 입니다.
		/// </summary>
		public string FeaturesJson { get; }

		internal AppVersionSetting(IReadOnlyDictionary<string, AppPlatformStatus> platforms, bool isReview,
			IReadOnlyDictionary<string, string> endpoints, IReadOnlyDictionary<string, AppSettingResource> resources,
			MaintenanceNotice maintenance, string featuresJson)
		{
			Platforms = platforms;
			IsReview = isReview;
			Endpoints = endpoints;
			Resources = resources;
			Maintenance = maintenance;
			FeaturesJson = featuresJson;
		}

		/// <summary>플랫폼의 상태입니다. 문서에 없는 플랫폼(null 포함)은 <see cref="AppPlatformStatus.Closed"/> 입니다.</summary>
		public AppPlatformStatus GetStatus(string platform)
		{
			return platform != null && Platforms.TryGetValue(platform, out var status) ? status : AppPlatformStatus.Closed;
		}
	}
}
