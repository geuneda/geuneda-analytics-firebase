# Changelog
이 패키지의 모든 주요 변경사항은 이 파일에 기록됩니다.

이 형식은 [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)를 기반으로 하며,
이 프로젝트는 [Semantic Versioning](http://semver.org/spec/v2.0.0.html)을 따릅니다.

## [0.1.0] - 2026-10-15

**New**:
- `FirebaseAnalyticsSink`/`FirebaseAnalyticsSinkOptions`: `com.geuneda.analytics` 0.2.0 의 Firebase Analytics sink. 일반 이벤트 변환, 표준 이벤트(구매, 광고 노출 수익)를 Firebase Analytics 표준으로 변환, `HandleEvent` 로 게임이 직접 보낼 이벤트 지정. 자세한 변환 규칙은 README
- 호스트 제외 define `GEUNEDA_SKIP_VENDOR_ADAPTERS`(있으면 컴파일하지 않음)
