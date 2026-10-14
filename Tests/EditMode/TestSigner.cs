using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting.Tests
{
	/// <summary>테스트용 RSA 서명 키입니다. 키 생성이 느려 키 ID 별로 한 번만 만듭니다.</summary>
	internal sealed class TestSigner
	{
		private static readonly Dictionary<string, TestSigner> Cache = new Dictionary<string, TestSigner>();

		private readonly RSAParameters _private;

		public string KeyId { get; }

		public AppSettingPublicKey PublicKey { get; }

		private TestSigner(string keyId)
		{
			using (var rsa = new RSACryptoServiceProvider(2048))
			{
				_private = rsa.ExportParameters(true);
			}

			KeyId = keyId;
			PublicKey = new AppSettingPublicKey(keyId, Base64Url.Encode(_private.Modulus), Base64Url.Encode(_private.Exponent));
		}

		public static TestSigner Get(string keyId)
		{
			if (!Cache.TryGetValue(keyId, out var signer))
			{
				signer = new TestSigner(keyId);
				Cache.Add(keyId, signer);
			}

			return signer;
		}

		public byte[] Sign(byte[] payload)
		{
			using (var rsa = RSA.Create())
			{
				rsa.ImportParameters(_private);
				return rsa.SignData(AppSettingFormat.SigningInput(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			}
		}

		/// <summary>본문 JSON 을 서명한 봉투입니다.</summary>
		public static byte[] Document(string payloadJson, params TestSigner[] signers)
		{
			return Document(Encoding.UTF8.GetBytes(payloadJson), signers);
		}

		public static byte[] Document(byte[] payload, params TestSigner[] signers)
		{
			var signatures = signers.Select(signer => new KeyValuePair<string, byte[]>(signer.KeyId, signer.Sign(payload))).ToList();
			return new AppSettingEnvelope(payload, signatures).Write();
		}
	}
}
