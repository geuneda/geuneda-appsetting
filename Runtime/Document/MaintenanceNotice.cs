using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 점검 공지입니다. <see cref="StartAt"/> 부터 <see cref="EndAt"/> 전까지 점검 시간입니다. 바꾸지 않는 객체입니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	public sealed class MaintenanceNotice
	{
		/// <summary>점검 시작(UTC)입니다.</summary>
		public DateTime StartAt { get; }

		/// <summary>점검 끝(UTC, 이 시각부터는 점검이 아님)입니다. 항상 <see cref="StartAt"/> 보다 뒤입니다.</summary>
		public DateTime EndAt { get; }

		/// <summary>요청한 언어의 문구가 없을 때 쓰는 언어입니다. <see cref="Messages"/> 에 항상 있습니다.</summary>
		public string DefaultLanguage { get; }

		/// <summary>언어 코드(대소문자 무시, 예: <c>ko</c>, <c>en</c>, <c>zh-Hant</c>)별 문구입니다.</summary>
		public IReadOnlyDictionary<string, string> Messages { get; }

		internal MaintenanceNotice(DateTime startAt, DateTime endAt, string defaultLanguage, IReadOnlyDictionary<string, string> messages)
		{
			StartAt = startAt;
			EndAt = endAt;
			DefaultLanguage = defaultLanguage;
			Messages = messages;
		}

		/// <summary><paramref name="utcNow"/> 가 점검 시간 안이면 true 입니다.</summary>
		/// <exception cref="ArgumentException"><paramref name="utcNow"/> 의 <see cref="DateTime.Kind"/> 가 <see cref="DateTimeKind.Utc"/> 가 아닌 경우</exception>
		public bool IsActiveAt(DateTime utcNow)
		{
			if (utcNow.Kind != DateTimeKind.Utc)
			{
				throw new ArgumentException("Time must be UTC", nameof(utcNow));
			}

			return StartAt <= utcNow && utcNow < EndAt;
		}

		/// <summary>
		/// 언어의 문구입니다. 정확히 같은 코드, 기본 하위 태그(<c>ko-KR</c> 이면 <c>ko</c>), <see cref="DefaultLanguage"/> 순으로 찾습니다.
		/// <paramref name="language"/> 가 null 이나 빈 문자열이면 기본 언어의 문구입니다.
		/// </summary>
		public string GetMessage(string language)
		{
			if (!string.IsNullOrEmpty(language))
			{
				if (Messages.TryGetValue(language, out var exact))
				{
					return exact;
				}

				var separator = language.IndexOf('-');

				if (separator > 0 && Messages.TryGetValue(language.Substring(0, separator), out var primary))
				{
					return primary;
				}
			}

			return Messages[DefaultLanguage];
		}
	}
}
