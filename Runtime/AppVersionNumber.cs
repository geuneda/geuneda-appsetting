using System;
using System.Globalization;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 점으로 나눈 숫자 버전(1~4 부분, 예: <c>1.10.2</c>)입니다. 빠진 부분은 0 으로 보고 비교합니다(<c>1.2</c> 와 <c>1.2.0</c> 은 같음).
	/// 접미사(<c>-beta</c>)나 숫자가 아닌 부분이 있으면 읽지 않습니다.
	/// </summary>
	internal readonly struct AppVersionNumber : IComparable<AppVersionNumber>
	{
		private const int MaxParts = 4;

		private readonly int[] _parts;

		private AppVersionNumber(int[] parts)
		{
			_parts = parts;
		}

		public static bool TryParse(string text, out AppVersionNumber version)
		{
			version = default;

			if (string.IsNullOrEmpty(text))
			{
				return false;
			}

			var pieces = text.Split('.');

			if (pieces.Length > MaxParts)
			{
				return false;
			}

			var parts = new int[pieces.Length];

			for (var i = 0; i < pieces.Length; i++)
			{
				if (!int.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out parts[i]))
				{
					return false;
				}
			}

			version = new AppVersionNumber(parts);
			return true;
		}

		public int CompareTo(AppVersionNumber other)
		{
			for (var i = 0; i < MaxParts; i++)
			{
				var compared = Part(i).CompareTo(other.Part(i));

				if (compared != 0)
				{
					return compared;
				}
			}

			return 0;
		}

		private int Part(int index)
		{
			return _parts != null && index < _parts.Length ? _parts[index] : 0;
		}
	}
}
