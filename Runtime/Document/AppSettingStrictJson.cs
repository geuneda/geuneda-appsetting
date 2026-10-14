using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 본문 텍스트가 표준 JSON(RFC 8259)인지 확인하고, 버전 노드별 <c>features</c> 값의 원문을 꺼냅니다. 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Newtonsoft 는 주석, 작은따옴표, 따옴표 없는 이름, NaN 같은 비표준 문법도 받아 줍니다. 서명 도구와 클라이언트가 같은 텍스트를 같게 읽도록 표준 JSON 만 받습니다.
	/// </para>
	/// <para>
	/// 기능 플래그는 게임 형식으로 읽기 전에 숫자가 바뀌지 않게 서명한 원문을 그대로 넘깁니다(Newtonsoft 토큰은 실수를 double 이나 decimal 하나로만 읽어 다른 쪽 값을 잃음).
	/// Newtonsoft 로 먼저 읽어(깊이 제한) 올바른 구조인지 확인한 텍스트에만 씁니다.
	/// </para>
	/// </remarks>
	internal static class AppSettingStrictJson
	{
		/// <summary>
		/// 루트 <c>versions</c> 객체의 속성 순서대로 각 노드의 <c>features</c> 원문(없거나 노드가 객체가 아니면 null)을 돌려줍니다.
		/// </summary>
		/// <exception cref="AppSettingDocumentException">표준 JSON 이 아니거나 double 범위를 넘는 숫자가 있는 경우</exception>
		public static List<string> FeaturesByVersion(string text, string what)
		{
			var scanner = new Scanner(text, what);
			var features = new List<string>();
			var index = scanner.SkipWhitespace(0);

			var end = scanner.ForEachProperty(index, (name, valueStart, valueEnd) =>
			{
				if (name != "versions" || text[valueStart] != '{')
				{
					return;
				}

				scanner.ForEachProperty(valueStart, (version, nodeStart, nodeEnd) =>
				{
					string raw = null;

					if (text[nodeStart] == '{')
					{
						scanner.ForEachProperty(nodeStart, (property, start, end) =>
						{
							if (property == "features")
							{
								raw = text.Substring(start, end - start);
							}
						});
					}

					features.Add(raw);
				});
			});

			// 루트 뒤에는 표준 공백(공백, 탭, CR, LF)만 올 수 있다. Newtonsoft 는 NBSP 같은 공백도 받아 준다
			scanner.ExpectEnd(scanner.SkipWhitespace(end));
			return features;
		}

		private sealed class Scanner
		{
			private readonly string _text;
			private readonly string _what;

			public Scanner(string text, string what)
			{
				_text = text;
				_what = what;
			}

			public int SkipWhitespace(int index)
			{
				while (index < _text.Length && (_text[index] == ' ' || _text[index] == '\t' || _text[index] == '\n' || _text[index] == '\r'))
				{
					index++;
				}

				return index;
			}

			/// <summary>객체 하나(<paramref name="start"/> 는 <c>{</c>)의 속성마다 이름(이스케이프를 푼 값)과 값의 범위를 넘기고, 객체 끝 다음 위치를 돌려줍니다.</summary>
			public int ForEachProperty(int start, Action<string, int, int> onProperty)
			{
				Expect(start, '{');
				var index = SkipWhitespace(start + 1);

				if (At(index) == '}')
				{
					return index + 1;
				}

				while (true)
				{
					Expect(index, '"');
					var name = ReadString(index, out index);
					index = SkipWhitespace(index);
					Expect(index, ':');
					var valueStart = SkipWhitespace(index + 1);
					var valueEnd = SkipValue(valueStart);
					onProperty?.Invoke(name, valueStart, valueEnd);
					index = SkipWhitespace(valueEnd);

					if (At(index) == ',')
					{
						index = SkipWhitespace(index + 1);
						continue;
					}

					Expect(index, '}');
					return index + 1;
				}
			}

			private int SkipValue(int index)
			{
				switch (At(index))
				{
					case '{':
						return ForEachProperty(index, null);
					case '[':
						return SkipArray(index);
					case '"':
						ReadString(index, out var end);
						return end;
					case 't':
						return SkipLiteral(index, "true");
					case 'f':
						return SkipLiteral(index, "false");
					case 'n':
						return SkipLiteral(index, "null");
					default:
						return SkipNumber(index);
				}
			}

			private int SkipArray(int start)
			{
				var index = SkipWhitespace(start + 1);

				if (At(index) == ']')
				{
					return index + 1;
				}

				while (true)
				{
					index = SkipWhitespace(SkipValue(index));

					if (At(index) == ',')
					{
						index = SkipWhitespace(index + 1);
						continue;
					}

					Expect(index, ']');
					return index + 1;
				}
			}

			private string ReadString(int start, out int end)
			{
				var value = new StringBuilder();
				var index = start + 1;

				while (true)
				{
					var c = At(index);

					if (c == '"')
					{
						end = index + 1;
						return value.ToString();
					}

					if (c < 0x20)
					{
						throw Error(index, "a control character in a string must be escaped");
					}

					if (c != '\\')
					{
						value.Append(c);
						index++;
						continue;
					}

					var escape = At(index + 1);
					index += 2;

					switch (escape)
					{
						case '"': value.Append('"'); break;
						case '\\': value.Append('\\'); break;
						case '/': value.Append('/'); break;
						case 'b': value.Append('\b'); break;
						case 'f': value.Append('\f'); break;
						case 'n': value.Append('\n'); break;
						case 'r': value.Append('\r'); break;
						case 't': value.Append('\t'); break;
						case 'u':
							if (index + 4 > _text.Length || !int.TryParse(_text.Substring(index, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
							{
								throw Error(index, "\\u needs four hex digits");
							}

							value.Append((char) code);
							index += 4;
							break;
						default:
							throw Error(index - 1, $"invalid escape '\\{escape}'");
					}
				}
			}

			private int SkipLiteral(int index, string literal)
			{
				if (string.CompareOrdinal(_text, index, literal, 0, literal.Length) != 0)
				{
					throw Error(index, "unexpected value");
				}

				return index + literal.Length;
			}

			private int SkipNumber(int start)
			{
				// -?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?
				var index = start;

				if (At(index) == '-')
				{
					index++;
				}

				if (At(index) == '0')
				{
					index++;
				}
				else
				{
					index = Digits(index, "a value");
				}

				if (At(index) == '.')
				{
					index = Digits(index + 1, "digits after '.'");
				}

				if (At(index) == 'e' || At(index) == 'E')
				{
					index++;

					if (At(index) == '+' || At(index) == '-')
					{
						index++;
					}

					index = Digits(index, "exponent digits");
				}

				// 기기마다 다르게 읽히지 않게 double 로 표현할 수 없는 크기(1e400 등)는 받지 않는다
				var literal = _text.Substring(start, index - start);

				if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsInfinity(number))
				{
					throw Error(start, $"number {literal} is out of the double range");
				}

				return index;
			}

			private int Digits(int index, string expected)
			{
				var start = index;

				while (At(index) >= '0' && At(index) <= '9')
				{
					index++;
				}

				return index > start ? index : throw Error(start, $"expected {expected}");
			}

			public void ExpectEnd(int index)
			{
				if (index != _text.Length)
				{
					throw Error(index, "unexpected content after the JSON value");
				}
			}

			private char At(int index)
			{
				return index < _text.Length ? _text[index] : throw Error(index, "unexpected end");
			}

			private void Expect(int index, char expected)
			{
				if (At(index) != expected)
				{
					throw Error(index, $"expected '{expected}'");
				}
			}

			private AppSettingDocumentException Error(int index, string message)
			{
				return AppSettingJson.Invalid($"{_what} is not standard JSON at offset {index}: {message} (no comments, single quotes, NaN or trailing commas)");
			}
		}
	}
}
