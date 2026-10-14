using System;
using System.Collections.Generic;
using System.Linq;

namespace Geuneda.AppSetting.Server
{
	/// <summary>설정 문서를 서명하고 검증합니다. 문서 형식과 본문 규칙은 클라이언트와 같은 코드입니다.</summary>
	public static class AppSettingSigner
	{
		/// <summary>
		/// 본문(설정 JSON, UTF-8)을 엄격하게 검사한 뒤 키마다 서명한 문서(봉투 JSON, UTF-8)를 만듭니다. 본문 바이트는 바꾸지 않고 그대로 담습니다.
		/// </summary>
		/// <exception cref="AppSettingDocumentException">본문이 규칙에 맞지 않는 경우(모르는 속성, 모르는 상태, 절대 주소가 아닌 서버 주소 포함)</exception>
		/// <exception cref="ArgumentException">키가 없거나 null 이거나 키 ID 가 겹치는 경우</exception>
		public static byte[] Sign(byte[] payload, IEnumerable<AppSettingSigningKey> keys)
		{
			ArgumentNullException.ThrowIfNull(payload);
			ArgumentNullException.ThrowIfNull(keys);

			var signingKeys = keys.ToList();

			if (signingKeys.Count == 0 || signingKeys.Any(key => key == null))
			{
				throw new ArgumentException("At least one signing key is required and none may be null", nameof(keys));
			}

			if (signingKeys.Select(key => key.KeyId).Distinct(StringComparer.Ordinal).Count() != signingKeys.Count)
			{
				throw new ArgumentException("Signing keys need distinct key ids", nameof(keys));
			}

			AppSettingPayload.Parse(payload, true);

			var signatures = signingKeys
				.Select(key => new KeyValuePair<string, byte[]>(key.KeyId, key.Sign(payload)))
				.ToList();
			return new AppSettingEnvelope(payload, signatures).Write();
		}

		/// <summary>
		/// 문서를 읽고 믿는 키의 서명과 본문을 검증합니다. <paramref name="strict"/> 가 false 면 클라이언트와 같은 관대한 규칙입니다.
		/// </summary>
		/// <exception cref="AppSettingDocumentException">봉투·서명·본문이 맞지 않는 경우(<see cref="AppSettingDocumentException.Code"/>)</exception>
		/// <exception cref="ArgumentException">공개 키가 없는 경우</exception>
		public static AppSettingDocument Verify(byte[] document, IEnumerable<AppSettingPublicKey> trustedKeys, bool strict = false)
		{
			ArgumentNullException.ThrowIfNull(document);
			ArgumentNullException.ThrowIfNull(trustedKeys);

			var keys = trustedKeys.ToList();

			if (keys.Count == 0 || keys.Any(key => key == null))
			{
				throw new ArgumentException("At least one public key is required and none may be null", nameof(trustedKeys));
			}

			return AppSettingEnvelope.ReadDocument(document, keys, strict);
		}

		/// <summary>서명을 확인하지 않고 문서의 판(revision)만 읽습니다. 형식이 맞지 않으면 null 입니다. 덮어쓸 문서와 판을 비교할 때 씁니다.</summary>
		public static long? PeekRevision(byte[] document)
		{
			try
			{
				return AppSettingPayload.Parse(AppSettingEnvelope.Read(document).Payload, false).Revision;
			}
			catch (AppSettingDocumentException)
			{
				return null;
			}
		}
	}
}
