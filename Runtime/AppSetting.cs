using System;
using System.Collections.Generic;
using System.Linq;
using Geuneda.Core;
using Newtonsoft.Json;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 받은 설정 문서를 이 앱 버전·플랫폼에 맞춰 고른 결과입니다. 바꾸지 않는 객체입니다.
	/// </summary>
	/// <remarks>
	/// 노드는 앱 버전 문자열이 정확히 같은 키, 없으면 <c>*</c> 키에서 고릅니다. 둘 다 없으면 <see cref="Version"/> 이 null 이고 업데이트 필요로 판정합니다.
	/// </remarks>
	public sealed class AppSetting
	{
		/// <summary>문서 전체입니다(다른 버전의 노드 포함).</summary>
		public AppSettingDocument Document { get; }

		public long Revision => Document.Revision;

		/// <summary>노드를 고를 때 쓴 앱 버전입니다(기본 <c>Application.version</c>).</summary>
		public string AppVersion { get; }

		/// <summary>상태를 고를 때 쓴 플랫폼 이름입니다(기본 <see cref="AppSettingPlatforms.Current"/>).</summary>
		public string Platform { get; }

		/// <summary>고른 노드의 키입니다(<see cref="AppVersion"/> 또는 <c>*</c>). 고르지 못했으면 null 입니다.</summary>
		public string MatchedVersion { get; }

		/// <summary>고른 노드입니다. 없으면 null 입니다.</summary>
		public AppVersionSetting Version { get; }

		/// <summary>이 플랫폼의 상태입니다. 노드가 없거나 노드에 플랫폼이 없으면 <see cref="AppPlatformStatus.Closed"/> 입니다.</summary>
		public AppPlatformStatus Status { get; }

		public bool IsReview => Version != null && Version.IsReview;

		/// <summary>고른 노드의 점검 공지입니다. 지금 점검 시간인지는 <see cref="MaintenanceNotice.IsActiveAt"/> 로 봅니다.</summary>
		public MaintenanceNotice Maintenance => Version?.Maintenance;

		/// <summary>
		/// 이 앱보다 높은 공개 버전(심사 중이 아니고 이 플랫폼이 <see cref="AppPlatformStatus.Open"/>)의 키를 낮은 순서로 둡니다.
		/// 숫자 버전으로 읽지 못하는 키와 <c>*</c> 는 넣지 않고, 이 앱 버전을 읽지 못하면 비어 있습니다. 선택 업데이트 안내에 씁니다.
		/// </summary>
		public IReadOnlyList<string> NewerVersions { get; }

		public bool HasNewerVersion => NewerVersions.Count > 0;

		internal AppSetting(AppSettingDocument document, string appVersion, string platform)
		{
			Document = document;
			AppVersion = appVersion;
			Platform = platform;

			if (document.Versions.TryGetValue(appVersion, out var node))
			{
				MatchedVersion = appVersion;
				Version = node;
			}
			else if (document.Versions.TryGetValue(AppSettingDocument.DefaultVersionKey, out node))
			{
				MatchedVersion = AppSettingDocument.DefaultVersionKey;
				Version = node;
			}

			Status = Version?.GetStatus(platform) ?? AppPlatformStatus.Closed;
			NewerVersions = FindNewerVersions(document, appVersion, platform);
		}

		/// <summary>
		/// 접속 판정입니다. 노드가 없으면 업데이트 필요, 내부 공개면 점검 공지와 관계없이 내부, 열림이면 점검 시간 안일 때만 점검,
		/// 닫힘이면 더 높은 공개 버전이 있을 때 업데이트 필요이고 없으면 점검입니다.
		/// </summary>
		/// <param name="utcNow">점검 시간 판정에 쓸 현재 시각(UTC). 기기 시계를 믿지 않으면 서버 시각을 넘깁니다</param>
		/// <exception cref="ArgumentException"><paramref name="utcNow"/> 가 UTC 가 아닌 경우</exception>
		public AppAccess GetAccess(DateTime utcNow)
		{
			if (utcNow.Kind != DateTimeKind.Utc)
			{
				throw new ArgumentException("Time must be UTC", nameof(utcNow));
			}

			if (Version == null)
			{
				return AppAccess.UpdateRequired;
			}

			switch (Status)
			{
				case AppPlatformStatus.Internal:
					return AppAccess.Internal;
				case AppPlatformStatus.Open:
					return Maintenance != null && Maintenance.IsActiveAt(utcNow) ? AppAccess.Maintenance : AppAccess.Open;
				default:
					return HasNewerVersion ? AppAccess.UpdateRequired : AppAccess.Maintenance;
			}
		}

		/// <summary>고른 노드의 서버 주소입니다. 노드나 이름이 없으면 false 입니다.</summary>
		public bool TryGetEndpoint(string name, out string address)
		{
			address = null;
			return Version != null && name != null && Version.Endpoints.TryGetValue(name, out address);
		}

		/// <summary>
		/// 고른 노드의 기능 플래그를 게임 형식으로 읽습니다(호출마다 새로 읽음). 노드나 플래그가 없으면 빈 객체 <c>{}</c> 를 읽습니다.
		/// 형식이 맞지 않으면 <see cref="AppSettingResultCodes.InvalidDocument"/> 입니다. IL2CPP 코드 제거에 대비해 형식에 <c>[Preserve]</c> 를 붙입니다.
		/// </summary>
		public Result<T> GetFeatures<T>(JsonSerializerSettings settings = null)
		{
			var json = Version?.FeaturesJson ?? "{}";

			try
			{
				var features = JsonConvert.DeserializeObject<T>(json, settings);

				return features == null
					? Result<T>.Fail(AppSettingResultCodes.InvalidDocument, $"Features could not be read as {typeof(T).Name}")
					: Result<T>.Ok(features);
			}
			catch (JsonException exception)
			{
				return Result<T>.Fail(AppSettingResultCodes.InvalidDocument, $"Features could not be read as {typeof(T).Name}: {exception.Message}");
			}
		}

		public override string ToString()
		{
			return $"AppSetting r{Revision} {AppVersion}/{Platform} -> {MatchedVersion ?? "(none)"} {Status}";
		}

		private static IReadOnlyList<string> FindNewerVersions(AppSettingDocument document, string appVersion, string platform)
		{
			if (!AppVersionNumber.TryParse(appVersion, out var current))
			{
				return Array.Empty<string>();
			}

			var newer = new List<KeyValuePair<AppVersionNumber, string>>();

			foreach (var entry in document.Versions)
			{
				if (!entry.Value.IsReview
					&& entry.Value.GetStatus(platform) == AppPlatformStatus.Open
					&& AppVersionNumber.TryParse(entry.Key, out var candidate)
					&& candidate.CompareTo(current) > 0)
				{
					newer.Add(new KeyValuePair<AppVersionNumber, string>(candidate, entry.Key));
				}
			}

			// 구조체 키의 Comparer<T>.Default 를 피한다(IL2CPP 에서 리플렉션으로 만듦). 같은 숫자 버전("1.2", "1.2.0")은 문자열 순서
			newer.Sort((left, right) =>
			{
				var compared = left.Key.CompareTo(right.Key);
				return compared != 0 ? compared : string.CompareOrdinal(left.Value, right.Value);
			});

			return newer.Select(entry => entry.Value).ToArray();
		}
	}
}
