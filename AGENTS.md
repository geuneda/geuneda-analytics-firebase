# Geuneda.Analytics.Firebase - AI 에이전트 가이드

> **관련 파일**: `CLAUDE.md`는 이 파일을 감쌉니다 — `CLAUDE.md`가 아니라 `AGENTS.md`를 편집하세요. `README.md`는 사용자 대상 문서입니다.

## 1. 패키지 개요
- **패키지**: `com.geuneda.analytics.firebase`
- **역할**: `com.geuneda.analytics` 의 Firebase Analytics sink(결정 D3: 벤더 SDK 를 쓰는 게임만 설치하는 별도 패키지)
- **의존성**: `com.geuneda.analytics` 0.2.0(표준 이벤트), `com.geuneda.core` 0.4.0, `com.cysharp.unitask`. 벤더 SDK 는 UPM 의존성으로 적을 수 없어(에셋 설치·git URL) 게임이 먼저 설치합니다
- **SDK**: `FirebaseAnalytics`(`Firebase.Analytics` 네임스페이스, 전역 `using`)
- **준비**: `MarkReady` 를 게임이 부름. 준비 전 `SetUserId`·`SetUserProperty` 는 sink 가 들고 있다가 `MarkReady` 때 넘기고, 다 넘긴 뒤에 준비 상태가 됨(넘기다 실패하면 다시 부를 수 있음). 분석 서비스는 등록할 때 바로 부르므로 Firebase 초기화 전 SDK 호출을 막음
- **출처**: 로드맵 4-1. 사용자 결정(2026-10-14): 표준 이벤트 계약 + 어댑터 변환, SDK 초기화는 게임이 하고 어댑터는 준비만 확인, 호스트에서만 제외 define

## 2. 구조

`FirebaseNames`(이름 규칙·예약 이름 목록·자르기), `FirebaseAnalyticsApi`/`IFirebaseAnalyticsApi`/`FirebaseParameter`(SDK 호출과 SDK 와 분리한 파라미터), `FirebaseAnalyticsSink`, `FirebaseAnalyticsSinkOptions`. 테스트는 SDK 호출 인터페이스를 기록하는 대역으로 바꿔 변환 결과를 확인합니다(SDK 를 실제로 부르지 않음).

## 3. 규칙

- **호스트 제외**: 런타임·테스트 asmdef 모두 `defineConstraints: ["!GEUNEDA_SKIP_VENDOR_ADAPTERS"]` 입니다. geuneda-frameworks 호스트는 SDK 가 없어 ProjectSettings 에 이 define 을 두고, 게임은 define 없이 설치하면 컴파일됩니다. 다른 define 조건(예: SDK 감지용)을 더하지 마세요(게임마다 define 을 넣어야 하는 부담).
- **검증 환경**: 호스트에서는 컴파일되지 않으므로 SDK 를 넣은 임시 프로젝트에서 EditMode 테스트와 Android·iOS 플레이어 스크립트 컴파일(`PlayerBuildInterface.CompilePlayerScripts`)로 확인합니다(2026-10-15: SpellDefense 의 Firebase 13.12(네이티브 12.14)·Facebook·Airbridge 4.x, GitHub 의 AppsFlyer 7.0.3).
- **표준 이벤트**: `AnalyticsPurchase.TryRead`, `AnalyticsAdImpression.TryRead` 로 읽고 벤더 표준으로 바꿉니다. 표준이 아닌 파라미터(`IsStandardParameter` 가 false)는 추가 파라미터로 함께 보냅니다. 읽을 수 없는 표준 이름 이벤트는 일반 이벤트로 보냅니다.
- **게임 처리**: `HandleEvent` 가 true 면 보내지 않습니다. 이 함수의 예외는 분석 서비스가 sink 예외로 보고 Error 로그 후 그 배치를 버립니다.
- **사용자 귀속**: `SendAsync` 는 이벤트마다(`HandleEvent` 호출 전에도) 이벤트의 사용자 스냅샷(`AnalyticsEvent.UserId`·`UserProperties`)으로 SDK 를 맞추고, 배치 끝에 분석 서비스가 알려 준 지금 상태(`_userId`·`_properties`)로 되돌립니다. SDK 에 넘긴 상태(`_sdk*`)를 따로 두어 바뀐 것만 부릅니다. 같은 스냅샷 사전이면 비교를 건너뜁니다(분석 서비스는 속성이 바뀔 때만 새 사전을 만듦). 배치가 예외로 끝나도 `finally` 에서 되돌리고(되돌리기 실패는 원래 예외를 가리지 않게 Error 로그), 속성을 맞추기 시작할 때 `_sdkPropertiesSource` 를 지워 중간 실패 뒤 같은 사전을 건너뛰지 않게 합니다. 지우기(`SetUserProperty(name, null)`)는 sink 가 모르는 속성(이전 실행에서 SDK 에 남은 값)도 SDK 에 넘깁니다 (준비 전이면 `_pendingRemovals` 에 두었다가 `MarkReady` 때). 이 순서를 빼면 기다렸거나 저장했던 이벤트가 나중 사용자에게 붙습니다(2026-10-15 리뷰 1·2회차).
- **취소**: 서비스를 닫으면 토큰이 취소됩니다. 취소된 토큰이면 보내지 않고 `Retry`(배치를 저장해 둠)를 돌려줍니다(SDK 호출이 동기라 배치 도중에는 취소되지 않음).
- **sink 계약**: 모든 멤버는 메인 스레드에서 불립니다. `SendAsync` 는 SDK 를 동기로 부른 뒤 `Delivered` 입니다(SDK 가 자체 큐를 가짐). static 가변 상태는 로거뿐입니다(D2).
- 네임스페이스 `Geuneda.Analytics.Firebase` 안에서 `Firebase.` 로 시작하는 이름은 이 네임스페이스로 해석됩니다. SDK 형식은 파일 맨 위 `using Firebase.Analytics;` 로만 씁니다.
- 예약 이벤트·사용자 속성 이름 목록은 Firebase 문서(자동 수집 이벤트, 예약 사용자 속성)를 따릅니다. 목록을 바꾸면 테스트도 고칩니다.
- 이름을 고칠 수 없으면 버리고 Warning 은 이름마다 한 번입니다(`_warned`). 잘라서 보내지 않습니다(다른 이름과 합쳐짐).

## 4. 테스트

- `Tests/EditMode/` (`Geuneda.Analytics.Firebase.Tests.EditMode`): 일반 이벤트 변환, 표준 이벤트 변환, `HandleEvent`, 사용자 정보, 이름·개수·길이 제한(해당하는 것).

## 5. 문서

공개 API 를 바꾸면 `README.md`, `CHANGELOG.md`, 호스트 `claude-skills/geuneda-analytics/` 스킬을 함께 고칩니다.
