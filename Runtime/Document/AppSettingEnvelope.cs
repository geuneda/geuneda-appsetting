using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 서명된 설정 문서(봉투)입니다: <c>{"format", "payload"(본문 base64), "signatures": [{"keyId", "signature"(base64)}]}</c>.
	/// 본문을 바이트 그대로 base64 로 담아, 서명한 바이트와 검증하는 바이트가 같습니다(JSON 정규화가 필요 없음). 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	internal sealed class AppSettingEnvelope
	{
		public byte[] Payload { get; }

		public IReadOnlyList<KeyValuePair<string, byte[]>> Signatures { get; }

		public AppSettingEnvelope(byte[] payload, IReadOnlyList<KeyValuePair<string, byte[]>> signatures)
		{
			Payload = payload;
			Signatures = signatures;
		}

		/// <summary>봉투를 읽고 서명을 검증한 뒤 본문을 해석합니다. <paramref name="strict"/> 는 <see cref="AppSettingPayload"/> 를 봅니다.</summary>
		/// <exception cref="AppSettingDocumentException">봉투·서명·본문 중 하나가 맞지 않는 경우</exception>
		public static AppSettingDocument ReadDocument(byte[] document, IReadOnlyList<AppSettingPublicKey> trustedKeys, bool strict)
		{
			var envelope = Read(document);
			envelope.Verify(trustedKeys);
			return AppSettingPayload.Parse(envelope.Payload, strict);
		}

		/// <summary>봉투를 읽습니다. 서명은 검증하지 않습니다(<see cref="Verify"/>). 모르는 속성은 무시합니다.</summary>
		/// <exception cref="AppSettingDocumentException">형식 이름이 다르거나(<see cref="AppSettingResultCodes.UnsupportedFormat"/>) 봉투가 깨진 경우</exception>
		public static AppSettingEnvelope Read(byte[] document)
		{
			var root = AppSettingJson.ParseObject(document, "document");
			var format = AppSettingJson.RequireString(root, "format", "document");

			if (!string.Equals(format, AppSettingFormat.Name, StringComparison.Ordinal))
			{
				throw new AppSettingDocumentException(AppSettingResultCodes.UnsupportedFormat, $"Unsupported document format '{format}' (expected '{AppSettingFormat.Name}')");
			}

			var payload = DecodeBase64(AppSettingJson.RequireString(root, "payload", "document"), "document.payload");
			var signatures = new List<KeyValuePair<string, byte[]>>();
			var entries = AppSettingJson.RequireArray(root, "signatures", "document");

			for (var i = 0; i < entries.Count; i++)
			{
				var path = $"document.signatures[{i}]";
				var entry = entries[i] as JObject ?? throw AppSettingJson.Invalid($"{path} must be an object");
				var keyId = AppSettingJson.RequireString(entry, "keyId", path);
				var signature = DecodeBase64(AppSettingJson.RequireString(entry, "signature", path), path + ".signature");
				signatures.Add(new KeyValuePair<string, byte[]>(keyId, signature));
			}

			return new AppSettingEnvelope(payload, signatures);
		}

		/// <summary>믿는 키로 만든 맞는 서명이 하나라도 있는지 확인합니다.</summary>
		/// <exception cref="AppSettingDocumentException"><see cref="AppSettingResultCodes.InvalidSignature"/></exception>
		public void Verify(IReadOnlyList<AppSettingPublicKey> trustedKeys)
		{
			var input = AppSettingFormat.SigningInput(Payload);

			foreach (var signature in Signatures)
			{
				foreach (var key in trustedKeys)
				{
					if (string.Equals(key.KeyId, signature.Key, StringComparison.Ordinal) && key.Verify(input, signature.Value))
					{
						return;
					}
				}
			}

			var signedBy = string.Join(", ", Signatures.Select(signature => signature.Key));
			var trusted = string.Join(", ", trustedKeys.Select(key => key.KeyId));
			throw new AppSettingDocumentException(AppSettingResultCodes.InvalidSignature,
				$"No valid signature from a trusted key (signed by [{signedBy}], trusted [{trusted}])");
		}

		/// <summary>봉투 JSON(UTF-8, 들여쓰기)을 만듭니다. 서명 도구와 테스트가 씁니다.</summary>
		public byte[] Write()
		{
			var root = new JObject
			{
				["format"] = AppSettingFormat.Name,
				["payload"] = Convert.ToBase64String(Payload),
				["signatures"] = new JArray(Signatures.Select(signature => new JObject
				{
					["keyId"] = signature.Key,
					["signature"] = Convert.ToBase64String(signature.Value)
				}))
			};

			return AppSettingFormat.StrictUtf8.GetBytes(root.ToString(Formatting.Indented) + "\n");
		}

		private static byte[] DecodeBase64(string value, string path)
		{
			return Base64Url.TryDecode(value) ?? throw AppSettingJson.Invalid($"{path} must be base64");
		}
	}
}
