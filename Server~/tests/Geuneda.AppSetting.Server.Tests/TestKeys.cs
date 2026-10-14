using System;
using System.IO;
using System.Text;

namespace Geuneda.AppSetting.Server.Tests
{
	/// <summary>테스트 키와 본문입니다. 키 생성이 느려 2048 비트 키를 한 번만 만듭니다.</summary>
	internal static class TestKeys
	{
		public static readonly AppSettingSigningKey First = AppSettingSigningKey.Generate("k1", 2048);
		public static readonly AppSettingSigningKey Second = AppSettingSigningKey.Generate("k2", 2048);

		public static byte[] Payload(long revision, string status = "open", string extra = null)
		{
			var node = "{\"platforms\":{\"android\":\"" + status + "\"}" + (extra == null ? string.Empty : "," + extra) + "}";
			return Encoding.UTF8.GetBytes("{\"revision\":" + revision + ",\"versions\":{\"1.0.0\":" + node + "}}");
		}

		public static byte[] Document(long revision, string status = "open", AppSettingSigningKey key = null)
		{
			return AppSettingSigner.Sign(Payload(revision, status), new[] { key ?? First });
		}

		/// <summary>테스트마다 지우는 임시 폴더입니다.</summary>
		public static string NewFolder()
		{
			return Directory.CreateTempSubdirectory("geuneda-appsetting-test").FullName;
		}

		public static void DeleteFolder(string folder)
		{
			try
			{
				Directory.Delete(folder, true);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}
}
