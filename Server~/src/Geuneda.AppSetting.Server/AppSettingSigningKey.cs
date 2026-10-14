using System;
using System.Security.Cryptography;

namespace Geuneda.AppSetting.Server
{
	/// <summary>
	/// 설정 문서에 서명하는 RSA 개인 키입니다(PKCS#1 v1.5, SHA-256). 서명 도구와 CI 에서만 쓰고 배포 서버에는 두지 않습니다.
	/// </summary>
	public sealed class AppSettingSigningKey : IDisposable
	{
		/// <summary>새로 만드는 키의 크기(비트)입니다.</summary>
		public const int DefaultKeySizeBits = 3072;

		private readonly RSA _rsa;

		public string KeyId { get; }

		/// <summary>클라이언트와 배포 서버에 넣을 공개 키입니다.</summary>
		public AppSettingPublicKey PublicKey { get; }

		private AppSettingSigningKey(string keyId, RSA rsa)
		{
			var parameters = rsa.ExportParameters(false);
			PublicKey = new AppSettingPublicKey(keyId, Base64Url.Encode(parameters.Modulus), Base64Url.Encode(parameters.Exponent));
			KeyId = keyId;
			_rsa = rsa;
		}

		/// <summary>새 키를 만듭니다.</summary>
		/// <exception cref="ArgumentException">키 ID 가 비었거나 키 크기가 2048 비트보다 작은 경우</exception>
		public static AppSettingSigningKey Generate(string keyId, int keySizeBits = DefaultKeySizeBits)
		{
			if (keySizeBits < AppSettingPublicKey.MinimumKeySizeBits)
			{
				throw new ArgumentException($"Key size must be at least {AppSettingPublicKey.MinimumKeySizeBits} bits", nameof(keySizeBits));
			}

			return Create(keyId, RSA.Create(keySizeBits));
		}

		/// <summary>PEM(PKCS#8 <c>PRIVATE KEY</c> 또는 PKCS#1 <c>RSA PRIVATE KEY</c>) 개인 키를 읽습니다.</summary>
		/// <exception cref="ArgumentException">키 ID 가 비었거나, PEM 이 RSA 개인 키가 아니거나, 2048 비트보다 작은 경우</exception>
		public static AppSettingSigningKey FromPem(string keyId, string pem)
		{
			var rsa = RSA.Create();

			try
			{
				rsa.ImportFromPem(pem ?? throw new ArgumentNullException(nameof(pem)));
				// 공개 키만 든 PEM 이면 여기서 실패한다
				rsa.ExportParameters(true);
			}
			catch (Exception exception) when (exception is CryptographicException || exception is ArgumentException)
			{
				rsa.Dispose();
				throw new ArgumentException($"Key '{keyId}' is not an RSA private key PEM: {exception.Message}", nameof(pem));
			}

			return Create(keyId, rsa);
		}

		/// <summary>PKCS#8 PEM 으로 내보냅니다. 파일은 저장소에 넣지 말고 비밀 관리 도구에 둡니다.</summary>
		public string ExportPrivateKeyPem()
		{
			return _rsa.ExportPkcs8PrivateKeyPem();
		}

		public void Dispose()
		{
			_rsa.Dispose();
		}

		internal byte[] Sign(byte[] payload)
		{
			return _rsa.SignData(AppSettingFormat.SigningInput(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		}

		private static AppSettingSigningKey Create(string keyId, RSA rsa)
		{
			try
			{
				if (string.IsNullOrEmpty(keyId))
				{
					throw new ArgumentException("Key id must not be empty", nameof(keyId));
				}

				// 공개 키 검사(크기, 형식)를 클라이언트와 같은 코드로 한다
				return new AppSettingSigningKey(keyId, rsa);
			}
			catch
			{
				rsa.Dispose();
				throw;
			}
		}
	}
}
