using System;
using System.Collections.Generic;
using System.Linq;

namespace Geuneda.AppSetting.Server
{
	/// <summary><c>AddGeunedaAppSetting</c> 배포 헬퍼 설정입니다.</summary>
	public sealed class AppSettingServerOptions
	{
		/// <summary>내려줄 서명된 문서 파일입니다. 필수입니다. 서명 도구가 쓴 파일을 그대로 둡니다(바꿀 때는 임시 파일에 쓰고 이름을 바꿈).</summary>
		public string DocumentPath { get; set; }

		/// <summary>내려주기 전에 문서를 검증할 공개 키입니다. 하나 이상 필요합니다. 클라이언트에 넣은 키와 같게 둡니다.</summary>
		public IList<AppSettingPublicKey> PublicKeys { get; } = new List<AppSettingPublicKey>();

		/// <summary>응답의 Cache-Control 입니다. 기본 <c>no-cache</c>(캐시는 매번 ETag 로 다시 확인)입니다.</summary>
		public string CacheControl { get; set; } = "no-cache";

		internal void Validate()
		{
			if (string.IsNullOrEmpty(DocumentPath))
			{
				throw new InvalidOperationException("AppSettingServerOptions.DocumentPath is required");
			}

			if (PublicKeys.Count == 0 || PublicKeys.Any(key => key == null))
			{
				throw new InvalidOperationException("AppSettingServerOptions.PublicKeys needs at least one key and no nulls");
			}

			if (PublicKeys.Select(key => key.KeyId).Distinct(StringComparer.Ordinal).Count() != PublicKeys.Count)
			{
				throw new InvalidOperationException("AppSettingServerOptions.PublicKeys need distinct key ids");
			}
		}
	}
}
