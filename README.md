# Geuneda AppSetting

원격 앱 설정입니다. 서명된 설정 문서를 받아 검증하고, 이 앱 버전·플랫폼의 상태(열림·내부·닫힘), 점검 공지, 서버 주소, 기능 플래그를 읽어 접속 판정(열림·내부·점검·업데이트 필요)을 돌려줍니다. 문서를 서명하는 CLI 와 ASP.NET Core 배포 헬퍼(`Server~/`)를 함께 둡니다. 결과는 core 결과 규약(`Result`)을 따릅니다.

| 기능 | 타입 | 네임스페이스 |
|------|------|-------------|
| 설정 받기 | `IAppSettingService`, `AppSettingService`, `AppSettingServiceOptions` | `Geuneda.AppSetting` |
| 판정 | `AppSetting`, `AppAccess`, `AppPlatformStatus`, `AppSettingPlatforms` | `Geuneda.AppSetting` |
| 문서 | `AppSettingDocument`, `AppVersionSetting`, `MaintenanceNotice`, `AppSettingResource`, `AppSettingPublicKey` | `Geuneda.AppSetting` |
| 코드 | `AppSettingResultCodes`(4000 블록), `AppSettingDocumentException` | `Geuneda.AppSetting` |
| 서명·배포(.NET, `Server~/`) | `AppSettingSigningKey`, `AppSettingSigner`, `AppSettingPublicKeys`, `AppSettingResources`, `AddGeunedaAppSetting`, `MapGeunedaAppSetting`, CLI `geuneda-appsetting` | `Geuneda.AppSetting.Server` |

## 설치

`Packages/manifest.json` 에 추가합니다. UPM 은 git 의존성을 자동으로 받지 않으므로 core, http, UniTask 도 함께 넣습니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.http": "https://github.com/geuneda/geuneda-http.git",
    "com.geuneda.appsetting": "https://github.com/geuneda/geuneda-appsetting.git"
  }
}
```

## 사용법

### 부트스트랩

```csharp
using Geuneda.AppSetting;
using Geuneda.Core;
using Geuneda.Http;

var options = new AppSettingServiceOptions
{
    // GET 이라 재시도해도 된다. 인증 토큰 미들웨어는 넣지 않는다(로그인 전에 받는 문서)
    HttpClient = new HttpClientBuilder().AddMiddleware(new RetryMiddleware()).Build(),
    DocumentUri = new EnvValue<string>("https://cdn.example.com/dev/appsetting.json",
                                       "https://cdn.example.com/qa/appsetting.json",
                                       "https://cdn.example.com/appsetting.json").Value,
    // AppVersion 기본 Application.version, Platform 기본 AppSettingPlatforms.Current, RequestTimeout 기본 10초
};
// 서명 CLI 의 keygen·public-key 가 출력한 줄
options.PublicKeys.Add(new AppSettingPublicKey("2026-10", "<modulus>", "AQAB"));

var appSetting = new AppSettingService(options);
MainInstaller.Bind<IAppSettingService>(appSetting);
```

### 부팅 관문

```csharp
var result = await appSetting.RefreshAsync(token);

if (!result.IsSuccess)
{
    // NetworkError·Timeout·HttpError 면 재시도 팝업, InvalidSignature·InvalidDocument 는 운영 사고(로그가 남음)
    ShowRetry(result.Code);
    return;
}

var setting = result.Value;

switch (setting.GetAccess(DateTime.UtcNow))   // 서버 시각이 있으면 그것을 넘긴다
{
    case AppAccess.UpdateRequired:
        OpenStore();
        return;
    case AppAccess.Maintenance:
        ShowMaintenance(setting.Maintenance?.GetMessage("ko"));   // 점검 공지가 없을 수 있다
        return;
}

if (setting.HasNewerVersion && setting.GetFeatures<MyFeatures>().TryGetValue(out var features) && features.SoftUpdate)
{
    // 선택 업데이트 안내
}
setting.TryGetEndpoint("game", out var gameUrl);
setting.TryGetEndpoint("realtime", out var realtimeUrl);
// 이어서 로그인(com.geuneda.account)
```

- 앱이 오래 백그라운드에 있다 돌아오면(예: 로비 진입, 60분 이상 백그라운드 후 복귀) 다시 `RefreshAsync` 를 부르고, 성공하면 304 여도 `GetAccess` 로 다시 판정합니다.
- `Changed` 는 **문서가 바뀌었다는 알림이지 접속 판정이 바뀌었다는 알림이 아닙니다.** 같은 문서여도 `GetAccess(utcNow)` 는 예약 점검의 시작·끝에서 바뀌므로, 판정을 `Changed` 처리기에만 두지 않습니다. 로비처럼 오래 머무는 화면은 `Maintenance.StartAt`·`EndAt` 에 맞춰 다시 판정하거나 주기적으로 판정합니다.
- 갱신이 실패해도 `Current` 는 이 실행에서 마지막으로 받아들인 설정 그대로입니다. 그 값으로 계속할지는 게임이 정합니다.
- 기능 플래그 형식은 게임이 정합니다(`GetFeatures<T>()`, 호출마다 새로 읽음). IL2CPP 코드 제거에 대비해 형식에 `[Preserve]` 를 붙입니다.
- 메인 스레드에서 씁니다. `Changed` 도 메인 스레드에서 옵니다.

### 판정 규칙

노드는 앱 버전 문자열이 정확히 같은 키, 없으면 `*` 키에서 고릅니다(`1.0` 과 `1.0.0` 은 다른 키). 상태는 노드의 플랫폼 이름(대소문자 무시)으로 고르고, 없으면 닫힘입니다.

| 노드 | 이 플랫폼 상태 | `GetAccess` |
|------|---------------|-------------|
| 없음(`*` 도 없음) | - | `UpdateRequired` |
| 있음 | `internal` | `Internal`(점검 공지와 관계없이 로그인으로. 테스터인지는 게임 서버가 판단) |
| 있음 | `open` | 점검 공지 시간(`startAt` 이상 `endAt` 미만) 안이면 `Maintenance`, 아니면 `Open` |
| 있음 | `closed`(또는 이 버전이 모르는 상태 문자열) | 더 높은 공개 버전이 있으면 `UpdateRequired`, 없으면 `Maintenance` |

- **더 높은 공개 버전(`NewerVersions`)**: 숫자 버전(`1.10.2`, 1~4 부분)으로 읽히고 이 앱보다 높으며, 심사 중(`review`)이 아니고 이 플랫폼이 `open` 인 키입니다. `*`, 접미사가 있는 키(`1.2.0-beta`)는 넣지 않고, 이 앱 버전을 숫자로 읽지 못하면 비어 있습니다.
- 점검은 예약해 둘 수 있습니다. 서명한 문서를 미리 배포해 두면 `open` 노드도 점검 시간 동안 `Maintenance` 입니다. 상태를 `closed` 로 바꾼 문서를 배포하면 시간과 관계없이 막힙니다.
- 이 판정은 클라이언트 안내용입니다. 점검·지원 버전은 게임 서버도 로그인에서 따로 막아야 합니다(기기 시계를 바꾸거나 클라이언트를 고친 사용자).

### 결과

| 상황 | 결과 코드 |
|------|-----------|
| 성공(새 설정, 304, 같은 내용, 무시한 낮은 판) | 0 |
| 연결 실패, 제한 시간, 이 호출의 취소 | `ResultCodes.NetworkError`, `Timeout`, `Cancelled` |
| 2xx·304 가 아닌 응답, 조건부 요청이 아닌데 304 | `HttpResultCodes.HttpError` |
| 문서·본문이 규칙에 맞지 않음, 기능 플래그가 게임 형식과 맞지 않음 | `AppSettingResultCodes.InvalidDocument`(4001) |
| 믿는 키의 맞는 서명이 없음 | `AppSettingResultCodes.InvalidSignature`(4002) |
| 모르는 문서 형식(`format`) | `AppSettingResultCodes.UnsupportedFormat`(4003) |
| 필수 설정 없음, 공개 키 없음·겹침·2048 비트 미만, `GetAccess` 에 UTC 가 아닌 시각 | 예외 |

### 갱신 규칙

- **디스크에 두지 않습니다.** 설정 문서가 부팅 관문이라 지난 실행의 사본으로 점검·강제 업데이트를 지나지 않게 합니다(레퍼런스 Perbase 와 같은 선택). 온라인 게임은 설정 서버에 닿지 못하면 로그인도 대개 못 하므로 저장본의 이득이 작습니다.
- **조건부 요청**: 받은 적이 있고 서버가 ETag 를 줬으면 `If-None-Match` 로 묻고, 304 면 지금 설정을 그대로 씁니다. ETag 가 없는 서버여도 같은 판·같은 본문이면 새 설정을 만들지 않고 `Changed` 도 오지 않습니다.
- **낮은 판 무시**: 이 실행에서 받아들인 판보다 낮은 판의 문서(오래된 CDN 사본)는 무시하고 지금 설정으로 성공합니다(경고 로그). 같은 판인데 본문이 다르면 받아들입니다.
- **single-flight**: 동시에 부르면 요청 하나를 함께 기다립니다. 호출자 토큰은 그 호출의 기다림만 멈추고 요청은 제한 시간까지 계속되어 결과가 적용됩니다.

## 설정 문서

배포하는 파일은 본문(설정 JSON)을 바이트 그대로 base64 로 담은 봉투입니다. 서명한 바이트와 검증하는 바이트가 같아 JSON 정규화가 필요 없습니다.

```json
{
  "format": "geuneda.appsetting.v1",
  "payload": "<본문 UTF-8 바이트의 base64>",
  "signatures": [ { "keyId": "2026-10", "signature": "<RSA PKCS#1 v1.5 SHA-256 서명의 base64>" } ]
}
```

- 서명 입력은 `"geuneda.appsetting.v1"`(ASCII) + `0x00` + 본문 바이트입니다(같은 키의 다른 용도 서명을 설정 문서로 쓰지 못하게).
- 서명은 여러 개 둘 수 있고 클라이언트가 믿는 키 ID 의 서명 하나가 맞으면 통과합니다(키 교체).
- Unity(Mono)는 ECDSA·RSA-PSS·Ed25519 와 SPKI 공개 키 가져오기를 지원하지 않아(2026-10-14 확인) RSA PKCS#1 v1.5 SHA-256 을 쓰고 공개 키는 modulus·exponent 로 넣습니다. 클라이언트는 2048 비트 미만 키를 받지 않고, CLI 는 3072 비트 키를 만듭니다.

본문(예: `Server~/samples/Geuneda.AppSetting.SampleServer/appsetting.payload.json`):

```json
{
  "revision": 12,
  "versions": {
    "1.2.0": {
      "platforms": { "android": "open", "ios": "open" },
      "review": false,
      "endpoints": { "game": "https://game.example.com/", "realtime": "wss://rt.example.com/rpc" },
      "maintenance": {
        "startAt": "2026-10-20T02:00:00Z",
        "endAt": "2026-10-20T04:00:00Z",
        "defaultLanguage": "en",
        "messages": { "en": "Scheduled maintenance", "ko": "정기 점검" }
      },
      "features": { "softUpdate": true },
      "resources": {
        "tables": {
          "version": "20261014-1",
          "size": 48213,
          "sha256": "<파일 SHA-256 16진수 64자>",
          "url": "https://cdn.example.com/tables/20261014-1.json"
        }
      }
    },
    "1.3.0": { "platforms": { "android": "internal", "ios": "internal" }, "review": true },
    "*": { "platforms": { "android": "closed", "ios": "closed" } }
  }
}
```

| 값 | 규칙 |
|----|------|
| `revision` | 1 이상의 정수. 배포할 때마다 올립니다 |
| `versions` | 비어 있지 않은 객체. 키는 앱 버전 문자열(`Application.version` 과 같게) 또는 `*` |
| `platforms` | `android`·`ios`·`standalone`(에디터·데스크톱)·`webgl` 등 → `open`·`internal`·`closed`. 이름·값 모두 대소문자 무시 |
| `review` | 스토어 심사 중인 빌드. 다른 버전의 '새 버전' 목록에서 뺍니다 |
| `endpoints` | 이름 → 주소(호스트가 있는 절대 주소). 이름은 대소문자를 가립니다 |
| `maintenance` | 시간대(`Z` 나 오프셋)가 있는 ISO 8601 시각, `endAt` 은 `startAt` 보다 뒤, 기본 언어 문구 필수. 문구는 정확한 코드, 기본 하위 태그(`ko-KR` → `ko`), 기본 언어 순으로 찾습니다 |
| `features` | 게임이 정하는 JSON 객체 |
| `resources` | 카테고리 → `version`·`size`·`sha256`·`url`. 카테고리·버전은 `[a-z0-9._-]` 1~64자(소문자만, 끝의 점·Windows 장치 이름(`con` 등) 제외, `.`, `..` 제외, 캐시 파일 이름이 됨), `sha256` 은 16진수 64자, `url` 은 http·https 절대 주소. 두 모드 모두 이 규칙을 적용합니다 |

- 본문은 표준 JSON(RFC 8259)이어야 합니다. 주석, 작은따옴표, 따옴표 없는 이름, 뒤 쉼표, `NaN`·`Infinity`, double 로 나타낼 수 없는 숫자(`1e400`), 같은 이름의 속성 두 번, JSON 뒤의 다른 내용(루트 앞뒤는 공백·탭·CR·LF 만, NBSP 등 거절), UTF-8 이 아닌 바이트는 거절합니다(Newtonsoft 가 받아 주는 비표준 문법을 막아 CLI 와 클라이언트가 같게 읽음). JSON `null` 은 없는 값으로 봅니다.
- `features` 는 서명한 본문에 쓴 텍스트 그대로 게임 형식으로 읽습니다(`1e-30` 같은 작은 double, `1e30` 같은 큰 double, 28자리 decimal 이 그대로 남음).
- 클라이언트는 새 버전 문서와 섞여 쓰일 수 있어 모르는 속성은 무시하고 모르는 상태 문자열은 닫힘으로 봅니다. 서명 CLI 는 오타를 막으려고 모르는 속성·상태, 호스트 없는 주소, 앞뒤 공백이 있는 버전 키도 거절합니다.
- 봉투·본문 해석 코드는 클라이언트(`Runtime/Document/`)와 CLI·서버가 같은 파일을 컴파일합니다. CLI 가 받아들인 문서는 클라이언트도 받아들입니다.

### 리소스 매니페스트

`AppSetting.Resources`(고른 노드의 카테고리 → `AppSettingResource`)는 문서 서명이 크기·해시를 보장하는 다운로드 목록입니다. 받기·캐시는 [com.geuneda.resource](https://github.com/geuneda/geuneda-resource) 가 하고, 두 패키지는 서로 의존하지 않아 게임이 옮겨 줍니다.

```csharp
var entries = setting.Resources
    .Select(pair => new ResourceEntry(pair.Key, pair.Value.Version, pair.Value.Size, pair.Value.Sha256, pair.Value.Url))
    .ToList();
```

리소스 버전은 앱 버전 노드마다 둡니다(테이블 형식이 앱 버전에 따라 다를 수 있음). 파일 이름에 버전을 넣어 올리면 CDN 캐시 무효화가 필요 없습니다.

## 서명 도구

`Server~/` 는 Unity 가 가져오지 않는 .NET 10 솔루션입니다(`dotnet test "Server~/Geuneda.AppSetting.Server.slnx"`). CLI 는 `dotnet run --project "Server~/src/Geuneda.AppSetting.Tool" --` 뒤에 명령을 붙여 실행합니다(이하 `geuneda-appsetting`).

```sh
# 1. 키 만들기(RSA-3072, 파일 권한 600, 이미 있으면 덮어쓰지 않음). 공개 키 JSON 과 Unity 코드 줄을 출력
geuneda-appsetting keygen --key-id 2026-10 --out appsetting-2026-10.pem
# 잃어버린 공개 키 출력 다시 보기
geuneda-appsetting public-key --key appsetting-2026-10.pem --key-id 2026-10

# 2. 서명(본문을 엄격하게 검사한 뒤 임시 파일에 쓰고 이름을 바꿈). --out 의 판보다 낮으면 거절, 같으면 경고
geuneda-appsetting sign --payload payload.json --key appsetting-2026-10.pem --key-id 2026-10 --out appsetting.json

# 리소스 파일을 CDN 에 올리기 전에 본문의 resources 항목 만들기(크기·SHA-256 계산)
geuneda-appsetting resource --category tables --version 20261014-1 --url https://cdn.example.com/tables/20261014-1.json --file tables.json

# 3. 확인(클라이언트와 같은 규칙, --strict 면 서명 도구 규칙)
geuneda-appsetting verify --document appsetting.json --public-keys public-keys.json
```

- 종료 코드는 0 성공, 1 실패(문서·키·파일 오류), 2 사용법 오류입니다.
- 같은 `--out` 에 동시에 서명하면 옆의 잠금 파일 `.<파일>.lock` 으로 차례를 기다리고(최대 30초), 잠금 안에서 기존 판을 다시 읽어 비교한 뒤 교체합니다. 잠금 파일은 남겨 두며 배포하지 않습니다. 잠금은 같은 파일 시스템(flock)에서만 효과가 있으니 여러 기기의 CI 는 발행을 한 줄로 세웁니다.
- 개인 키는 저장소에 넣지 말고 비밀 관리 도구나 CI 비밀 값에 둡니다. 배포 서버에는 공개 키만 둡니다(서버가 털려도 새 문서를 만들 수 없음). 서명을 건너뛰는 옵션은 없습니다. 개발 환경도 개발용 키로 서명합니다.
- **키 교체**: 새 키를 만들어 앱의 `PublicKeys` 에 더해 배포하고, 그동안 문서는 두 키로 서명합니다(`--key` 와 `--key-id` 를 두 쌍). 새 키를 넣은 앱 버전만 남으면 옛 키를 뺍니다.
- 공개 키 파일(`public-keys.json`)은 `[{"keyId", "modulus", "exponent"}]` 배열입니다(keygen 출력).

## 배포

**정적 호스팅(CDN·스토리지)**: 서명한 파일을 그대로 올립니다. 클라이언트는 ETag 가 있으면 조건부 요청을 하고, 없어도 동작합니다. 캐시는 짧게(`Cache-Control: no-cache` 또는 짧은 `max-age`) 두고 배포 때 무효화합니다. 오래된 엣지 사본은 한 실행 안에서는 낮은 판 무시로 걸러집니다.

**ASP.NET Core 헬퍼**:

```csharp
using Geuneda.AppSetting.Server;

builder.Services.AddGeunedaAppSetting(options =>
{
    options.DocumentPath = "appsetting.json";   // 상대 경로는 콘텐츠 루트 기준
    foreach (var key in AppSettingPublicKeys.FromJson(File.ReadAllText("public-keys.json")))
        options.PublicKeys.Add(key);
    // CacheControl 기본 "no-cache"
});

app.MapGeunedaAppSetting("/appsetting");   // GET, ETag(문서 바이트의 SHA-256), If-None-Match(약한 비교, *) 면 304
```

- 시작할 때 문서를 읽고 공개 키로 검증해, 파일이 없거나 맞지 않으면 예외로 시작을 막습니다.
- 파일은 1초에 한 번까지 다시 읽어 내용이 바뀌었으면 검증 후 바꿉니다(크기·시각이 아니라 내용 비교). 바뀐 파일이 검증에 실패하거나, 판이 지금보다 낮거나, 파일이 사라지면 지금 문서를 계속 내려주고 오류 로그를 한 번 남깁니다.
- 파일은 CLI 처럼 임시 파일에 쓴 뒤 이름을 바꿔 교체합니다.
- 샘플: `dotnet run --project "Server~/samples/Geuneda.AppSetting.SampleServer" --no-launch-profile -- --urls http://127.0.0.1:5399`(시작할 때마다 새 키로 예제 본문을 서명, 샘플 전용 `GET /sample/public-keys`·`POST /sample/publish`).

## 요구 사항

- Unity 2021.3 이상
- `com.geuneda.core` 0.2.0, `com.geuneda.http` 0.1.0, `com.cysharp.unitask` 2.5.10, `com.unity.nuget.newtonsoft-json` 3.2.1
- 서명 도구·서버: .NET 10(ASP.NET Core), Newtonsoft.Json 13

변경 이력은 [CHANGELOG.md](CHANGELOG.md) 를 참고하세요.

## 라이선스

MIT. [LICENSE.md](LICENSE.md) 를 참고하세요.
