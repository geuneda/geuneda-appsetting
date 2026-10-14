using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Geuneda.Core;

// ReSharper disable once CheckNamespace

namespace Geuneda.AppSetting
{
	/// <summary>
	/// 원격 앱 설정입니다. 기본 구현은 <see cref="AppSettingService"/> 이고, services 의 <c>MainInstaller</c> 에 이 인터페이스로 바인딩합니다.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 설정은 디스크에 두지 않고 이 실행의 메모리에만 둡니다. 설정 문서가 부팅 관문(점검·강제 업데이트)이라 지난 실행의 사본으로 관문을 지나지 않게 하려는 것입니다.
	/// 갱신에 실패하면 <see cref="Current"/> 는 그대로 두고 실패를 돌려줍니다. 그 값으로 계속할지는 게임이 정합니다.
	/// </para>
	/// <para>메인 스레드에서 씁니다. <see cref="Changed"/> 도 메인 스레드에서 옵니다.</para>
	/// </remarks>
	public interface IAppSettingService
	{
		/// <summary>이 실행에서 마지막으로 받아들인 설정입니다. 처음 갱신이 성공하기 전에는 null 입니다.</summary>
		AppSetting Current { get; }

		/// <summary>
		/// <see cref="Current"/> 가 새 설정으로 바뀌면 옵니다(처음 받을 때 포함). 같은 판·같은 내용이면 오지 않습니다. 처리기 예외는 로그만 남깁니다.
		/// 문서 변경 알림이지 접속 판정 변경 알림이 아닙니다. <see cref="AppSetting.GetAccess"/> 는 같은 문서에서도 점검 시간 경계에서 바뀌므로 성공한 갱신마다(304 포함), 그리고 점검 시작·끝에 다시 판정합니다.
		/// </summary>
		event Action<AppSetting> Changed;

		/// <summary>
		/// 설정 문서를 받아 서명을 검증하고 <see cref="Current"/> 를 바꿉니다. 성공하면 지금 설정(바뀌지 않았으면 그대로)입니다.
		/// </summary>
		/// <remarks>
		/// <para>
		/// 받은 적이 있고 서버가 ETag 를 줬으면 <c>If-None-Match</c> 로 묻고, 304 면 그대로 둡니다. 지금보다 낮은 판의 문서(오래된 CDN 사본)는 무시하고 지금 설정으로 성공합니다.
		/// </para>
		/// <para>
		/// 동시에 부르면 요청 하나를 함께 기다립니다. <paramref name="cancellationToken"/> 은 이 호출의 기다림만 멈추고(<c>ResultCodes.Cancelled</c>), 요청은 제한 시간까지 계속되어 결과가 적용됩니다.
		/// </para>
		/// <para>
		/// 실패: 연결 실패·제한 시간·취소는 core 공용 코드, 2xx·304 가 아닌 응답은 <c>HttpResultCodes.HttpError</c>, 문서 오류는 <see cref="AppSettingResultCodes"/> 입니다.
		/// </para>
		/// </remarks>
		UniTask<Result<AppSetting>> RefreshAsync(CancellationToken cancellationToken = default);
	}
}
