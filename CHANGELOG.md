# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.1.0] - 2026-10-14

**New**:
- 원격 앱 설정 `IAppSettingService`/`AppSettingService`: 서명된 설정 문서를 받아 검증하고 이 실행의 메모리에만 둠(디스크 저장 없음), `RefreshAsync` single-flight(호출자 취소는 그 호출만), ETag 조건부 요청(304 면 그대로), 같은 판·같은 본문이면 그대로, 이 실행에서 받아들인 것보다 낮은 판 무시, 실패 시 `Current` 유지, `Changed` 이벤트(처리기 예외 격리), 요청 제한 시간(기본 10초)
- 판정 `AppSetting`: 앱 버전 정확히 같은 노드, 없으면 `*` 노드. `GetAccess(utcNow)` 가 `Open`·`Internal`·`Maintenance`(열림 노드의 점검 시간 포함)·`UpdateRequired`(노드 없음, 닫힘인데 더 높은 공개 버전 있음). `NewerVersions`(숫자 버전 비교, 심사 중·비공개 제외), `TryGetEndpoint`, `GetFeatures<T>`
- 문서 형식 `geuneda.appsetting.v1`: 본문 바이트를 그대로 담은 봉투, RSA PKCS#1 v1.5 SHA-256 서명 여러 개(키 교체), 형식 이름을 앞에 붙인 서명 입력, `AppSettingPublicKey`(modulus·exponent, 2048 비트 이상). 본문 규칙(판, 버전별 플랫폼 상태, 심사, 서버 주소, 점검 공지, 기능 플래그), 표준 JSON 만(주석·작은따옴표·NaN·뒤 쉼표·double 범위 밖 숫자 거절), 기능 플래그는 서명한 원문 그대로 게임 형식으로 읽음(숫자 보존), 클라이언트는 모르는 속성·상태에 관대하고 서명 도구는 엄격
- 결과 코드 `AppSettingResultCodes`(4000 블록: `InvalidDocument` 4001, `InvalidSignature` 4002, `UnsupportedFormat` 4003)
- 서명·배포(`Server~/`, .NET 10): `AppSettingSigningKey`(RSA-3072 생성, PEM), `AppSettingSigner`(엄격한 검사 후 서명, 검증), `AppSettingPublicKeys`(공개 키 파일, Unity 코드 줄), CLI `geuneda-appsetting`(`keygen`·`public-key`·`sign`·`verify`, 개인 키 파일 권한 600·덮어쓰기 거절, 원자적 쓰기, 같은 출력에 대한 동시 서명은 잠금 파일로 차례를 지켜 잠금 안에서 낮은 판 거절, 날짜 모양 키 ID 보존, C# 줄의 키 ID 이스케이프), ASP.NET Core 헬퍼 `AddGeunedaAppSetting`/`MapGeunedaAppSetting`(시작할 때 검증, ETag·304, 1초마다 내용 비교로 다시 읽기, 검증 실패·낮은 판·파일 삭제 시 지금 문서 유지). 샘플 서버와 xUnit 테스트
