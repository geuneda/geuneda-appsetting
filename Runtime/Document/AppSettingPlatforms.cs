// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 설정 문서의 플랫폼 이름입니다. 문서의 이름은 대소문자를 가리지 않습니다. 이 부분은 서버(<c>Server~</c>)와 함께 컴파일합니다.
	/// </summary>
	public static partial class AppSettingPlatforms
	{
		public const string Android = "android";
		public const string Ios = "ios";
		public const string WebGl = "webgl";

		/// <summary>에디터와 데스크톱입니다.</summary>
		public const string Standalone = "standalone";
	}
}
