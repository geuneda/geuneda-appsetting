using System;
using System.Collections.Generic;
using System.Linq;

namespace Geuneda.AppSetting.Tool
{
	/// <summary>잘못된 명령 인자입니다. 사용법을 보여 주고 종료 코드 2 로 끝냅니다.</summary>
	internal sealed class ToolUsageException : Exception
	{
		public ToolUsageException(string message) : base(message)
		{
		}
	}

	/// <summary><c>--이름 값</c> 인자와 값 없는 플래그입니다. 같은 이름을 여러 번 줄 수 있습니다(<c>--key</c>).</summary>
	internal sealed class ToolArguments
	{
		private static readonly HashSet<string> FlagNames = new HashSet<string>(StringComparer.Ordinal) { "--strict" };

		private readonly Dictionary<string, List<string>> _values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		private readonly HashSet<string> _flags = new HashSet<string>(StringComparer.Ordinal);

		/// <exception cref="ToolUsageException">이름이 <c>--</c> 로 시작하지 않거나 값이 없는 경우</exception>
		public static ToolArguments Parse(IReadOnlyList<string> args)
		{
			var parsed = new ToolArguments();

			for (var i = 0; i < args.Count; i++)
			{
				var name = args[i];

				if (!name.StartsWith("--", StringComparison.Ordinal))
				{
					throw new ToolUsageException($"Unexpected argument '{name}'");
				}

				if (FlagNames.Contains(name))
				{
					parsed._flags.Add(name);
					continue;
				}

				if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
				{
					throw new ToolUsageException($"Missing value for {name}");
				}

				if (!parsed._values.TryGetValue(name, out var list))
				{
					list = new List<string>();
					parsed._values.Add(name, list);
				}

				list.Add(args[++i]);
			}

			return parsed;
		}

		/// <exception cref="ToolUsageException">명령이 받지 않는 인자가 있는 경우</exception>
		public void RejectUnknown(params string[] known)
		{
			var unknown = _values.Keys.Concat(_flags).FirstOrDefault(name => !known.Contains(name, StringComparer.Ordinal));

			if (unknown != null)
			{
				throw new ToolUsageException($"Unknown option {unknown}");
			}
		}

		/// <summary>한 번만 준 값입니다.</summary>
		/// <exception cref="ToolUsageException">없거나 여러 번 준 경우</exception>
		public string Single(string name)
		{
			var values = All(name);

			if (values.Count != 1)
			{
				throw new ToolUsageException(values.Count == 0 ? $"{name} is required" : $"{name} must be given once");
			}

			return values[0];
		}

		public IReadOnlyList<string> All(string name)
		{
			return _values.TryGetValue(name, out var list) ? list : (IReadOnlyList<string>) Array.Empty<string>();
		}

		public bool Flag(string name)
		{
			return _flags.Contains(name);
		}
	}
}
