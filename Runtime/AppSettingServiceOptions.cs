using System;
using System.Collections.Generic;
using Geuneda.Http;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary><see cref="AppSettingService"/> 설정입니다.</summary>
	public sealed class AppSettingServiceOptions
	{
		/// <summary>
		/// 문서를 받을 HTTP 클라이언트입니다. 필수입니다. 재시도가 필요하면 <c>RetryMiddleware</c> 를 넣습니다(GET 이라 재시도해도 됨).
		/// 인증 토큰 미들웨어는 넣지 않습니다(로그인 전에 받는 문서).
		/// </summary>
		public IHttpClient HttpClient { get; set; }

		/// <summary>문서 주소입니다. 필수입니다. 절대 주소이거나 <see cref="HttpClient"/> 기본 주소에 대한 상대 주소입니다. 빌드 환경별로 다르면 core 의 <c>EnvValue</c> 로 고릅니다.</summary>
		public string DocumentUri { get; set; }

		/// <summary>서명을 검증할 공개 키입니다. 하나 이상 필요하고 키 ID 가 겹치면 안 됩니다. 서명 CLI 가 출력한 생성자 호출을 넣습니다.</summary>
		public IList<AppSettingPublicKey> PublicKeys { get; } = new List<AppSettingPublicKey>();

		/// <summary>노드를 고를 앱 버전입니다. null 이면 <c>Application.version</c> 입니다.</summary>
		public string AppVersion { get; set; }

		/// <summary>상태를 고를 플랫폼 이름입니다. null 이면 <see cref="AppSettingPlatforms.Current"/> 입니다.</summary>
		public string Platform { get; set; }

		/// <summary>문서 요청 하나의 제한 시간입니다. 넘으면 <c>ResultCodes.Timeout</c> 입니다. 기본 10초입니다.</summary>
		public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
	}
}
