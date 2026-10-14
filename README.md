# Geuneda Analytics Firebase

`com.geuneda.analytics` 의 Firebase Analytics sink 입니다. 분석 서비스에 등록하면 이벤트를 Firebase 규칙에 맞춰 보내고, 표준 이벤트(구매, 광고 노출 수익)를 Firebase 권장 이벤트로 바꿉니다.

| 타입 | 설명 |
|------|------|
| `FirebaseAnalyticsSink` | sink(`AnalyticsSinks.Firebase`). `MarkReady()` 로 준비를 알림 |
| `FirebaseAnalyticsSinkOptions` | `HandleEvent`(게임이 직접 보낼 이벤트) |

네임스페이스는 `Geuneda.Analytics.Firebase` 입니다.

## 설치

Firebase Unity SDK 의 Firebase Analytics(`Firebase.Analytics.dll`, `Firebase.App.dll`)가 프로젝트에 있어야 합니다(.unitypackage 나 Google 레지스트리 tgz). 그다음 의존 패키지와 함께 추가합니다.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.geuneda.core": "https://github.com/geuneda/geuneda-core.git",
    "com.geuneda.analytics": "https://github.com/geuneda/geuneda-analytics.git",
    "com.geuneda.analytics.firebase": "https://github.com/geuneda/geuneda-analytics-firebase.git"
  }
}
```

SDK 없이 이 패키지만 설치하면 컴파일 오류가 납니다(Firebase 를 쓰는 게임만 설치). define `GEUNEDA_SKIP_VENDOR_ADAPTERS` 가 있으면 이 패키지는 컴파일되지 않습니다(SDK 가 없는 geuneda-frameworks 호스트용).

## 사용 예

```csharp
var firebase = new FirebaseAnalyticsSink();
analytics.AddSink(firebase);   // 부트스트랩에서 먼저 등록(초기화를 기다리지 않음)

var status = await FirebaseApp.CheckAndFixDependenciesAsync();
if (status == DependencyStatus.Available)
{
    firebase.MarkReady();      // 기다리던 이벤트와 사용자 ID·속성을 보냄
}
```

## 동작

- **준비**: Firebase 초기화는 게임이 합니다(Crashlytics 등과 함께 쓰는 경우가 많음). `MarkReady` 전에는 이벤트가 분석 서비스 큐에서 기다리고(앱이 백그라운드로 가면 디스크에 저장), 사용자 ID·속성은 sink 가 들고 있다가 `MarkReady` 때 넘깁니다. 넘기다 SDK 가 예외를 던지면 준비 상태가 되지 않으니 다시 부를 수 있습니다. 초기화에 실패하면 `MarkReady` 를 부르지 않습니다(이벤트는 큐·저장 상한까지 보관).
- **사용자 귀속**: 이벤트마다 기록할 때의 사용자 ID·속성으로 SDK 를 맞춘 뒤 보내고(바뀐 것만, 없어진 속성은 지움), 배치가 끝나면 지금 상태로 되돌립니다. 기다렸거나 저장했던 이벤트가 그 뒤에 로그인한 사용자에게 붙지 않습니다. `HandleEvent` 로 게임이 직접 보내는 이벤트도 같은 상태에서 불립니다.
- **이름**: 이벤트·파라미터 이름은 허용되지 않는 문자를 `_` 로 바꿉니다. 40자를 넘거나 영문자로 시작하지 않거나 `firebase_`·`google_`·`ga_` 로 시작하거나 Firebase 예약 이름(`session_start`, `first_open` 등)이면 그 이벤트(또는 파라미터)를 보내지 않고 Warning 을 한 번 남깁니다.
- **값**: 정수 → long, 실수 → double, 불리언 → 0/1, 문자열은 100자까지(표준 이벤트의 상품 ID·이름·광고 플랫폼도 100자에서 자름). 파라미터는 25개까지 보내고 나머지는 Warning 후 버립니다.
- **사용자 속성**: 이름 24자, 예약 이름(`user_id`, `first_open_time` 등)은 보내지 않고 값은 36자까지입니다.
- **표준 이벤트**: 구매는 `purchase`(`currency`, `value` = 단가 × 수량, `transaction_id`, `items` 에 `item_id`·`item_name`·`price`·`quantity`), 광고 노출 수익은 `ad_impression`(`ad_platform`, `ad_source`, `ad_format`, `ad_unit_name`, `ad_placement`, `value`, `currency`). 추가 파라미터는 함께 보냅니다.
- **게임 처리**: `HandleEvent` 가 true 를 돌려준 이벤트는 보내지 않습니다(예: 특정 이벤트를 다른 이름·형식으로 직접 `FirebaseAnalytics.LogEvent`).
- 다시 보낸 이벤트의 시각은 Firebase 가 받은 시각입니다.
