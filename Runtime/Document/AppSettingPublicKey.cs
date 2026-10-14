using System;
using System.Security.Cryptography;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 설정 문서 서명을 검증하는 RSA 공개 키입니다(PKCS#1 v1.5, SHA-256). 서명 CLI 의 <c>keygen</c>·<c>public-key</c> 가 이 생성자 호출을 출력합니다.
	/// 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>
	/// Unity(Mono)는 SPKI·PEM 공개 키 가져오기와 ECDSA·RSA-PSS 를 지원하지 않아 modulus·exponent 로 받습니다(2026-10-14 확인).
	/// 키를 바꿀 때는 새 키 ID 로 두 키 모두 서명한 문서를 배포하고, 새 키를 넣은 앱이 퍼진 뒤 옛 키를 뺍니다.
	/// </remarks>
	public sealed class AppSettingPublicKey
	{
		/// <summary>받는 가장 작은 키 크기(비트)입니다. CLI 는 3072 비트 키를 만듭니다.</summary>
		public const int MinimumKeySizeBits = 2048;

		private readonly byte[] _modulus;
		private readonly byte[] _exponent;

		/// <summary>문서의 서명 항목과 맞춰 보는 키 ID 입니다.</summary>
		public string KeyId { get; }

		public int KeySizeBits { get; }

		/// <summary>modulus(big-endian, 패딩 없는 base64url)입니다.</summary>
		public string Modulus => Base64Url.Encode(_modulus);

		/// <summary>exponent(big-endian, 패딩 없는 base64url)입니다. 보통 <c>AQAB</c>(65537)입니다.</summary>
		public string Exponent => Base64Url.Encode(_exponent);

		/// <param name="keyId">키 ID</param>
		/// <param name="modulus">modulus(big-endian, base64url 또는 base64)</param>
		/// <param name="exponent">exponent(big-endian, base64url 또는 base64)</param>
		/// <exception cref="ArgumentException">키 ID 가 비었거나, 값이 base64 가 아니거나, 키가 2048 비트보다 작거나, RSA 키로 쓸 수 없는 경우</exception>
		public AppSettingPublicKey(string keyId, string modulus, string exponent)
		{
			if (string.IsNullOrEmpty(keyId))
			{
				throw new ArgumentException("Key id must not be empty", nameof(keyId));
			}

			_modulus = TrimLeadingZeros(Decode(modulus, nameof(modulus)));
			_exponent = TrimLeadingZeros(Decode(exponent, nameof(exponent)));
			KeyId = keyId;
			KeySizeBits = BitLength(_modulus);

			if (KeySizeBits < MinimumKeySizeBits)
			{
				throw new ArgumentException($"Key '{keyId}' has {KeySizeBits} bits; at least {MinimumKeySizeBits} are required", nameof(modulus));
			}

			// 짝수 modulus 나 1 이하의 짝수 exponent 는 RSA 키가 아니다
			if ((_modulus[_modulus.Length - 1] & 1) == 0 || (_exponent.Length == 1 && _exponent[0] < 3) || (_exponent[_exponent.Length - 1] & 1) == 0)
			{
				throw new ArgumentException($"Key '{keyId}' is not a valid RSA public key", nameof(modulus));
			}

			try
			{
				using (var rsa = RSA.Create())
				{
					rsa.ImportParameters(Parameters());
				}
			}
			catch (CryptographicException exception)
			{
				throw new ArgumentException($"Key '{keyId}' is not a valid RSA public key: {exception.Message}", nameof(modulus));
			}
		}

		public override string ToString()
		{
			return $"{KeyId} (RSA {KeySizeBits})";
		}

		/// <summary><paramref name="data"/> 의 PKCS#1 v1.5 SHA-256 서명이 맞으면 true 입니다. 길이가 맞지 않거나 형식이 깨진 서명도 false 입니다.</summary>
		internal bool Verify(byte[] data, byte[] signature)
		{
			if (signature == null || signature.Length != _modulus.Length)
			{
				return false;
			}

			try
			{
				using (var rsa = RSA.Create())
				{
					rsa.ImportParameters(Parameters());
					return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
				}
			}
			catch (CryptographicException)
			{
				return false;
			}
		}

		private RSAParameters Parameters()
		{
			// 가져오는 쪽이 배열을 붙잡을 수 있어 복사본을 준다
			return new RSAParameters { Modulus = (byte[]) _modulus.Clone(), Exponent = (byte[]) _exponent.Clone() };
		}

		private static byte[] Decode(string value, string parameterName)
		{
			var bytes = Base64Url.TryDecode(value);

			if (bytes == null || bytes.Length == 0)
			{
				throw new ArgumentException($"'{parameterName}' must be base64url", parameterName);
			}

			return bytes;
		}

		private static byte[] TrimLeadingZeros(byte[] value)
		{
			var start = 0;

			while (start < value.Length - 1 && value[start] == 0)
			{
				start++;
			}

			if (start == 0)
			{
				return value;
			}

			var trimmed = new byte[value.Length - start];
			Buffer.BlockCopy(value, start, trimmed, 0, trimmed.Length);
			return trimmed;
		}

		private static int BitLength(byte[] value)
		{
			var first = value[0];
			var bits = 0;

			while (first != 0)
			{
				bits++;
				first >>= 1;
			}

			return (value.Length - 1) * 8 + bits;
		}
	}
}
