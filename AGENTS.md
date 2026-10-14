# Geuneda.AppSetting - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.appsetting`
- **Unity**: 2021.3+
- **의존성**: `com.geuneda.core`(Result, ModuleLogger), `com.geuneda.http`(문서 요청), `com.cysharp.unitask`, `com.unity.nuget.newtonsoft-json`
- **서버**: `Server~/`(Unity 가 가져오지 않는 폴더)의 .NET 10 솔루션. 서명·배포 라이브러리, 서명 CLI, 샘플 서버, xUnit 테스트
- **출처**: Lucky Defense 의 Perbase AppSetting(로드맵 3-5). 앱 버전별 상태 맵(플랫폼별 상태, 서버 주소, 점검 공지, 기능 플래그), 닫힘이면 새 공개 버전 유무로 업데이트·점검을 가르는 판정, 조건부 요청, 본문 해시 서명 검증, 메모리에만 두기는 따랐습니다. 바꾼 것: 서버 상태 4종 대신 `open`·`internal`·`closed`, 버전+해시 쿼리와 204 대신 표준 ETag/304, Ed25519(BouncyCastle) 대신 RSA PKCS#1 v1.5 SHA-256(Unity·.NET 기본 지원), 응답 봉투 암호화(ECIES)와 메모리 XOR 난독화는 뺌(TLS + 서명), 서명 건너뛰기 옵션 없음, `*` 기본 노드, 열림 노드의 점검 시간 판정, 같은 실행 안의 낮은 판 무시, 서명 입력 앞 형식 이름, 서명 CLI 의 엄격한 검사(오타), 배포 헬퍼의 검증 후 교체.

## 2. 구조

| 파일 | 역할 |
|------|------|
| `Runtime/Document/` | 문서 형식과 해석. 서버·CLI 가 링크해 컴파일. `AppSettingEnvelope`(봉투 읽기·쓰기, 서명 검증, `ReadDocument`), `AppSettingPayload`(본문 규칙, 엄격·관대), `AppSettingJson`(JSON 읽기 도구), `AppSettingStrictJson`(표준 JSON 확인, `features` 원문), `AppSettingFormat`(형식 이름, 서명 입력), `AppSettingPublicKey`, `Base64Url`, 모델(`AppSettingDocument`, `AppVersionSetting`, `MaintenanceNotice`, `AppSettingResource`(리소스 매니페스트 항목, 이름·해시 규칙), `AppPlatformStatus`), `AppSettingResultCodes`, `AppSettingDocumentException`, `AppSettingPlatforms`(상수 부분) |
| `Runtime/AppSettingService.cs` | 갱신 single-flight, 조건부 요청, 판 비교, `Changed` |
| `Runtime/AppSetting.cs` | 노드 고르기, 접속 판정, 새 버전 목록, 서버 주소, 기능 플래그 |
| `Runtime/AppVersionNumber.cs` | 숫자 버전 비교(1~4 부분) |
| `Runtime/AppSettingPlatforms.Current.cs` | 빌드 대상 플랫폼 이름(Unity define) |
| `Server~/src/Geuneda.AppSetting.Server/` | `AppSettingSigningKey`, `AppSettingSigner`, `AppSettingPublicKeys`, `AppSettingResources`(리소스 항목 JSON), `AppSettingFileSource`(파일 검증·교체), `AppSettingServerExtensions`(`AddGeunedaAppSetting`, `MapGeunedaAppSetting`) |
| `Server~/src/Geuneda.AppSetting.Tool/` | CLI `geuneda-appsetting`(`ToolCommands`, `ToolArguments`) |
| `Server~/samples/`, `Server~/tests/` | 샘플 서버(시작 때 새 키, 샘플 전용 공개 키·배포 엔드포인트), xUnit 테스트 |

## 3. 규칙

- **결과 규약**: 정상 실패(네트워크, 서버 응답, 문서 오류)는 `Result` 코드, 오용(필수 설정 없음, 키 없음·겹침·작은 키, UTC 가 아닌 시각)은 예외입니다. 문서 오류는 공유 코드가 `AppSettingDocumentException`(코드 포함)으로 던지고 클라이언트 서비스만 그 타입을 잡아 `Result` 로 바꿉니다.
- **공유 파일**: `Runtime/Document/` 는 서버·CLI 가 링크해 컴파일합니다. UnityEngine·UniTask·Geuneda.Core 를 쓰지 말고(Newtonsoft 는 됨, 서버는 NuGet 13.x), C# 9 문법만 씁니다. 고치면 `dotnet test "Server~/Geuneda.AppSetting.Server.slnx"` 도 돌립니다. 클라이언트 전용 코드는 `Runtime/` 바로 아래에 둡니다(`AppSettingPlatforms` 는 partial 로 나눔).
- **서명 형식**: 서명 입력(`AppSettingFormat.SigningInput`), 봉투 속성 이름, `format` 값은 이미 배포된 앱과의 약속입니다. 바꾸려면 새 형식 이름(`geuneda.appsetting.v2`)과 새 문서 주소를 씁니다. 서명 검증을 건너뛰는 옵션을 만들지 마세요(로드맵 결정: 운영 빌드 서명 생략 금지). 개발 환경도 개발용 키로 서명합니다.
- **RSA**: Unity(Mono)는 ECDSA·RSA-PSS·SPKI 가져오기가 없고(`NotImplementedException`, `CryptographicException`, `PlatformNotSupportedException`), `RSA.Create(2048)` 은 1024 비트 키를 만듭니다(2026-10-14 실험). 검증은 `RSA.Create()` + `ImportParameters` + `VerifyData(SHA256, Pkcs1)` 이고(iOS IL2CPP High 코드 제거에서 확인), Unity 테스트의 키 생성은 `new RSACryptoServiceProvider(bits)` 입니다. 서명 길이가 modulus 와 다르면 검증 전에 false 입니다.
- **관대·엄격**: 클라이언트(관대)는 모르는 속성을 무시하고 모르는 상태 문자열을 닫힘으로 봅니다(새 형식 문서와 섞여도 열리지 않게). CLI·`AppSettingSigner.Sign`(엄격)은 그것들과 호스트 없는 서버 주소(Unix 에서 `/path` 는 절대 파일 주소로 읽힘), 앞뒤 공백 버전 키를 거절합니다. 두 모드의 나머지 규칙은 같아야 합니다(서명한 문서를 클라이언트가 거절하면 안 됨).
- **JSON**: `DateParseHandling.None`(Newtonsoft 기본값은 날짜 문자열을 지역 시간으로 바꿈, 공개 키 파일도 같음), 중복 속성 오류, 뒤따르는 내용 오류, 엄격한 UTF-8(앞 BOM 허용), JSON null 은 없는 값. 시각은 `Z` 나 오프셋이 있어야 합니다. Newtonsoft 로 먼저 읽어(깊이 제한) 구조를 확인한 뒤 `AppSettingStrictJson` 이 표준 JSON 인지(주석·작은따옴표·NaN·뒤 쉼표·double 범위 밖 숫자 거절) 보고(루트 앞뒤 표준 공백만) `features` 원문을 꺼냅니다. 기능 플래그를 토큰에서 다시 쓰지 마세요(Newtonsoft 는 실수를 double 이나 decimal 하나로만 읽어, decimal 이면 1e-30 이 0 이 되고 1e30 이 거절되며 double 이면 정밀한 decimal 이 깎임, 2026-10-14 리뷰).
- **디스크에 저장하지 않음**: 사용자 판단 위임(2026-10-14)에 따라 레퍼런스처럼 메모리에만 둡니다. 부팅 관문을 지난 실행의 사본으로 지나지 않게 하려는 것입니다. 저장본 폴백을 넣으려면 먼저 사용자와 정합니다.
- **갱신**: 진행 중 갱신은 `UniTaskCompletionSource` 하나로 나누고(`UniTask.Preserve` 는 완료 전 다중 대기 불가), 요청에는 호출자 토큰을 넘기지 않습니다(요청 제한 시간이 끝을 보장). 끝날 때 `ReferenceEquals` 로 자기 것일 때만 비웁니다. 낮은 판은 무시하고 ETag 도 바꾸지 않습니다. 같은 판·같은 본문 해시는 ETag 만 바꿉니다.
- **리소스 매니페스트(3-6)**: `resources` 항목은 com.geuneda.resource 가 파일 이름(`<카테고리>/<버전>.<해시 앞 16자>.bin`)과 검증 값으로 쓰므로 이름(`[a-z0-9._-]` 1~64자, 소문자만, 끝의 점·Windows 장치 이름(`con` 등) 제외, `.`·`..` 제외)·크기·해시·주소 규칙을 관대한 모드에서도 적용합니다(모르는 속성만 엄격 모드에서 거절). 이름 규칙은 resource 의 `ResourceEntry` 와 같아야 하니 함께 바꿉니다. 두 패키지는 서로 의존하지 않습니다(사용자 결정 2026-10-14).
- **IL2CPP**: 구조체 키의 `Comparer<T>.Default`(리플렉션 생성)를 피해 `List.Sort(Comparison)` 을 씁니다. 공유 모델은 JSON 으로 만들지 않아(직접 읽음) `[Preserve]` 가 필요 없습니다. 게임의 기능 플래그 형식에는 게임이 `[Preserve]` 를 붙입니다.
- **배포 헬퍼**: 파일 확인은 1초에 한 번(`CheckInterval`, `TimeProvider`)이고 내용(바이트)으로 비교합니다. 거절한 바이트를 기억해 같은 파일로 로그를 반복하지 않습니다. 처음 읽기(`MapGeunedaAppSetting`)가 실패하면 시작을 막습니다. 이 서버에는 개인 키를 두지 않습니다.
- **CLI**: `keygen` 은 덮어쓰지 않고(파일 권한 600), `sign` 은 엄격한 검사 후 같은 폴더 잠금 파일(`.<파일>.lock`, `FileShare.None` = Unix flock, 지우지 않음)을 잡은 채 기존 판을 다시 읽어 비교하고 임시 파일에 쓴 뒤 이름을 바꿉니다(`--out` 의 판보다 낮으면 거절). 잠금 없이 비교와 교체를 나누면 동시 발행에서 낮은 판이 나중에 덮어씁니다(2026-10-14 리뷰). `ToCSharp` 는 키 ID 를 C# 리터럴로 이스케이프합니다. 종료 코드 0·1·2.
- 결과 코드는 4000 블록(core README 범위 표에 등록). 값은 바꾸거나 재사용하지 않습니다.
- static 가변 상태 금지(D2). 서비스는 인스턴스로 만들어 `MainInstaller` 에 인터페이스로 바인딩합니다.

## 4. 테스트

- `Tests/EditMode/` (`Geuneda.AppSetting.Tests.EditMode`)
  - `AppSettingEnvelopeTest`: 서명 통과·본문 변조·서명 변조·짧은 서명·모르는 키 ID·같은 ID 다른 키·두 키 서명·서명 없음·형식 이름 없는 서명, 형식 이름·필수 값·base64·깨진 JSON, 공개 키(앞 0, 작은 키, 짝수 modulus·exponent)
  - `AppSettingPayloadTest`: 전체 필드, 점검 문구·시간 경계·UTC, 깨진 루트·노드(두 모드), 관대·엄격 차이, JSON null, BOM·잘못된 UTF-8, 날짜 모양 문자열 보존, `features` 원문(이스케이프한 이름, 특이한 숫자 표기), 비표준 JSON 거절과 표준 JSON 경계 사례, `resources` 읽기와 이름(경로 조작)·크기·해시·주소 규칙(두 모드), 본문 해시
  - `AppSettingTest`: 노드 고르기, 판정 표, 새 버전 목록(숫자 비교, 심사·비공개·`*`·접미사 제외, 플랫폼별), 서버 주소, 기능 플래그
  - `AppSettingServiceTest`: `FakeDocumentServer`(ETag·304·게이트·실패)로 첫 갱신, 304, ETag 없는 같은 문서, 새 판, 같은 판 다른 본문, 낮은 판 무시, 믿지 않는 서명, 깨진 문서, 5xx, 조건 없는 304, 전송 실패, single-flight, 호출자 취소, 이미 취소된 토큰, 처리기 예외
  - `ToolFixtureTest`: .NET CLI 가 RSA-3072 로 서명한 문서(`ToolFixture`, 개인 키는 버림)를 Unity 가 검증·해석
  - `SampleServerInteropTest`(`[Explicit]`, `Interop` 범주): 샘플 서버를 띄우고(`dotnet run --project "Server~/samples/Geuneda.AppSetting.SampleServer" --no-launch-profile -- --urls http://127.0.0.1:5399`) `GEUNEDA_APPSETTING_INTEROP_URL=http://127.0.0.1:5399` 로 이름을 골라 실행. UnityWebRequest 로 받기·304·새 판 배포·잘못된 본문 거절
- `Server~/tests/Geuneda.AppSetting.Server.Tests`: 서명·검증·키 PEM·공개 키 파일(날짜 모양 키 ID)·C# 줄 이스케이프·`features` 숫자 보존·비표준 JSON 거절·루트 뒤 비표준 공백 거절·공개 키 파일 뒤 내용 거절(`SignerTest`), `resource` 명령 출력이 서명·`verify` 를 통과하고 경로 조작 이름 거절, 서명 잠금 경쟁(`ToolTest`: 잠금을 잡은 채 더 높은 판을 써 두면 기다리던 서명이 다시 읽어 거절), 127.0.0.1 Kestrel 과 `FakeTimeProvider` 로 ETag·304(약한 비교, 목록, `*`)·교체·같은 크기 같은 초 교체·검증 실패·낮은 판·삭제·시작 검사(`EndpointTest`), CLI 명령·종료 코드(`ToolTest`)
