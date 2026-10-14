using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 서명을 검증하고 해석한 설정 문서입니다. 바꾸지 않는 객체입니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>본문 JSON 형식은 README 의 '설정 문서' 절을 봅니다.</remarks>
	public sealed class AppSettingDocument
	{
		/// <summary>등록하지 않은 앱 버전이 쓰는 기본 노드의 키입니다.</summary>
		public const string DefaultVersionKey = "*";

		/// <summary>
		/// 문서 판(1 이상)입니다. 배포할 때마다 올립니다. 클라이언트는 한 실행 안에서 지금보다 낮은 판을 받으면 무시합니다(오래된 CDN 사본).
		/// </summary>
		public long Revision { get; }

		/// <summary>앱 버전 문자열(또는 <see cref="DefaultVersionKey"/>)별 설정입니다. 키는 대소문자를 가립니다.</summary>
		public IReadOnlyDictionary<string, AppVersionSetting> Versions { get; }

		/// <summary>본문 바이트의 SHA-256(base64)입니다. 같은 판의 같은 내용인지 비교할 때 씁니다.</summary>
		internal string PayloadHash { get; }

		internal AppSettingDocument(long revision, IReadOnlyDictionary<string, AppVersionSetting> versions, string payloadHash)
		{
			Revision = revision;
			Versions = versions;
			PayloadHash = payloadHash;
		}
	}
}
