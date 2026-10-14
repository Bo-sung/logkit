# LogKit

게임 로그 수집 → 로컬 버퍼링 → 종료 시 서버 전송 유니티 패키지.

## 설치

Package Manager → `Add package from git URL`:

```
https://github.com/Bo-sung/logkit.git?path=package#v0.1.0
```

## 사용법

부트스트랩은 자동이다. 로그를 남기고 싶은 곳에서 바로 호출한다:

```csharp
using LogKit;

GameLogger.Info("Combat", "보스 격파");
GameLogger.Warn("System", "낮은 프레임 감지");
GameLogger.Data("Error", "Run", "런 실패", new FailData { Stage = 3 });

// 플레이 지표 — 서버 대시보드에서 세션·월 단위로 집계된다
GameLogger.Count("Attack");           // 발생 횟수 +1
GameLogger.Count("AttackHit");        // 조건부 횟수 +1 (서버 설정으로 명중률 자동 계산)
GameLogger.Measure("ComboLength", 5); // 값 기록(횟수 + 합계 → 평균 자동 계산)
```

- `Debug.Log` 계열은 설정을 끄지 않는 한 자동 수집된다.
- 파일 기록은 전용 워커 스레드가 담당하므로 고빈도 수집에도 프레임을 막지 않는다.
- 세션이 아무리 길어도 20,000건씩 파트로 나눠 전부 전송한다. 실패 시 다음 기동에 파트 전체를 재전송한다(서버 병합은 멱등).
- 서버 주소·API 키 등은 씬에 배치한 `GameLogger` 컴포넌트의 Inspector에서 바꾼다(없으면 코드 기본값).
- 데이터는 `Application.persistentDataPath/Logs/{세션ID}.jsonl`과 `{세션ID}.metrics.json`에 버퍼링되고, 종료 시 서버로 전송된다. 실패 시 다음 기동에 자동 재전송된다.

자세한 운영 방식과 수신 서버는 저장소 루트 README를 참고한다.
