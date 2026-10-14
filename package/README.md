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
```

- `Debug.Log` 계열은 설정을 끄지 않는 한 자동 수집된다.
- 서버 주소·API 키 등은 씬에 배치한 `GameLogger` 컴포넌트의 Inspector에서 바꾼다(없으면 코드 기본값).
- 데이터는 `Application.persistentDataPath/Logs/{세션ID}.jsonl`에 버퍼링되고, 종료 시 서버로 전송된다. 실패 시 다음 기동에 자동 재전송된다.

자세한 운영 방식과 수신 서버는 저장소 루트 README를 참고한다.
