# LogKit

유니티 게임 로그 수집 → 로컬 버퍼링 → 종료 시 서버 전송 → CSV/요약 집계까지 한 번에 처리하는 경량 파이프라인.

```
logkit/
├─ package/   유니티 패키지 (UPM). 다른 프로젝트에서 git URL로 설치한다.
├─ server/    로그 수신 서버 (ASP.NET Core 8, 의존성 없음).
└─ README.md
```

## 데이터 흐름

```
Play 중 수집 → 5초마다 백그라운드 워커가 로컬 JSONL 기록 → 게임 종료 시 20,000건 단위 파트로 분할 gzip POST
→ 서버 검증(크기·키·gzip·JSON) → 파트별 저장 + 통합 CSV 재구성 + 월별 요약 재계산 + raw 보존
→ 클라이언트로 요약 JSON 응답 (실패 시 클라이언트가 .pending 보존 후 다음 기동 재전송 — 파트 전체 재전송, 서버 병합은 멱등)

Count/Measure로 모은 플레이 지표(공격 횟수 등)는 배치와 함께 전송 → 세션·월 단위 집계 + 명중률 같은 파생 지표 자동 계산

서버 브라우저(/) 열람 → 월별 세션 목록 · 항목 검색 · 지표 집계 · CSV/raw 내려받기 · 세션/월 삭제
```

## 유니티 패키지 설치

Package Manager → `Add package from git URL`:

```
https://github.com/Bo-sung/logkit.git?path=package#v0.1.0
```

- `#v0.1.0` 태그로 버전 고정을 권장한다. 최신이면 태그 부분을 생략한다.
- 오프라인 프로젝트는 `package/` 폴더를 대상 프로젝트의 `Packages/` 아래에 통째로 복사해도 된다(embedded package).

### 사용법

부트스트랩은 자동이다(`RuntimeInitializeOnLoadMethod`). 코드에서는 바로 호출한다:

```csharp
using LogKit;

GameLogger.Info("Combat", "보스 격파");
GameLogger.Warn("System", "메모리 부족");
GameLogger.Data("Info", "Run", "런 종료", new RunData { Score = 1200 });

// 플레이 지표 — 서버가 세션·월 단위로 집계하고 appsettings의 DerivedMetrics로 명중률까지 계산한다
GameLogger.Count("Attack");          // 공격 시도 +1
GameLogger.Count("AttackHit");       // 명중 +1
GameLogger.Measure("ComboLength", 5); // 콤보 길이 기록(횟수 + 합계 → 평균 자동 계산)
```

설정은 씬에 `GameLogger` 컴포넌트를 배치해 Inspector에서 덮어쓸 수 있다(없으면 코드 기본값).

| 설정 | 기본값 | 설명 |
| --- | --- | --- |
| Development Server Url | `http://localhost:5000/api/logs` | 에디터·Development Build에서 사용 |
| Release Server Url | `http://localhost:5000/api/logs` | 일반 빌드에서 사용 |
| Api Key | `dev-log-key` | 서버 `ApiKey`와 일치해야 한다 |
| Capture Unity Logs | true | `Debug.Log` 계열 자동 캡처 |
| Max Entries Per Session | 100000 | 세션당 캡처 상한(총량 기준, 0 이하는 무제한). 초과분은 버리고 마커 1건. 전송은 20,000건씩 파트로 나눠 모두 보낸다 |
| Flush Interval Seconds | 5 | 큐 → 로컬 파일 기록 주기 |
| Upload On Quit | true | 종료 시 전송 |
| Retry Pending On Startup | true | 기동 시 미전송 파일 재전송 |
| Request Timeout Seconds | 5 | HTTP 타임아웃 |

- 로컬 버퍼: `Application.persistentDataPath/Logs/{세션ID}.jsonl` + `{세션ID}.metrics.json` (전송 성공 시 함께 삭제)
- 파일 기록은 메인 스레드가 아닌 전용 워커 스레드가 담당한다. Update는 신호만 보낸다.
- 대상 플랫폼: Windows 스탠드얼론(에디터 포함). WebGL은 스레드·동기 HTTP 제약으로 미지원

## 수신 서버 실행 (server/)

요구사항: .NET 8 SDK(빌드) 또는 ASP.NET Core 8 Runtime(실행만). 자세한 내용은 `server/README.md`.

```bash
cd server
dotnet run            # http://0.0.0.0:5000 에서 대기, 데이터는 server/data/
```

설정은 `server/appsettings.json`:

| 키 | 기본값 | 설명 |
| --- | --- | --- |
| Urls | `http://0.0.0.0:5000` | 대기 주소 |
| ApiKey | `dev-log-key` | 클라이언트 `Api Key`와 일치 |
| DataPath | `data` | CSV·raw 저장 위치(프로세스 작업 디렉터리 기준, 절대 경로 권장) |
| MaxBodyBytes | 5242880 | gzip 본문 상한(초과 413) |
| KeepRawJsonl / RawRetentionDays | true / 90 | 원문 보존 여부와 기간 |

### 열람 — 대시보드와 API

`http://서버주소/`를 열면 GM툴 형태의 열람 화면이 나온다(API 키 입력 필요). 월별 세션 목록, 레벨·카테고리·텍스트 검색, 항목 펼치기(메시지·데이터·스택), CSV/raw 내려받기, 세션·월 삭제를 지원한다. 같은 기능은 전부 API로도 제공한다:

| 엔드포인트 | 인증 | 설명 |
| --- | --- | --- |
| `POST /api/logs` | 필요 | 업로드. 성공 202(신규)/200(교체) + 요약 JSON |
| `GET /health` | 불필요 | `ok` |
| `GET /dashboard` | 불필요 | 열람 대시보드(`/` 리디렉션) |
| `GET /api/months` | 필요 | 월 목록 + 월별 집계 |
| `GET /api/sessions?month=` | 필요 | 세션 목록(`q`, `onlyIssues`, `sort`) |
| `GET /api/sessions/{id}` | 필요 | 세션 상세(`levels`, `category`, `scene`, `q`, `offset`, `limit`) |
| `GET /api/sessions/{id}/csv` · `/raw` | 필요 | 세션 CSV · 수신 원문 내려받기 |
| `DELETE /api/sessions/{id}` · `/api/months/{yyyy-MM}` | 필요 | 세션 삭제 · 월 전체 삭제 |
| `GET /api/summary/{yyyy-MM}.csv` | 필요 | 월별 요약 CSV 다운로드 |

```bash
# 요약 열람
curl -H "X-Api-Key: dev-log-key" http://localhost:5000/api/summary/2026-10.csv

# 오류·예외만 검색
curl -H "X-Api-Key: dev-log-key" \
  "http://localhost:5000/api/sessions/s1234abcd-2026101401?levels=Error,Exception&q=%EB%B3%B4%EC%8A%A4"
```

### 다른 PC에 상시 배포

1. `dotnet publish -c Release -o publish` 후 `publish/`를 대상 PC로 복사(예: `C:\UnityCI\LogServer`).
2. `appsettings.json`의 `DataPath`를 절대 경로로 지정(작업 디렉터리 의존 제거).
3. 자동시작: 시작 레지스트리(HKCU Run) 또는 예약 작업으로 `launch.vbs`(숨김 실행 런처, `server/deploy/` 참고)를 실행.
4. 방화벽: 포트 5000 인바운드 허용 규칙 추가. **exe 최초 실행 시 Windows가 만드는 프로그램 단위 차단 규칙이 포트 허용보다 우선하므로, 접속이 안 되면 차단 규칙(LogServer) 존재 여부를 먼저 확인하고 제거한다.**

## 유의사항

- 스택트레이스에 로컬 사용자명이 포함될 수 있다. 외부 공개 서버라면 HTTPS + 키 관리를 강화한다.
- `package.json`의 이름(`com.team.logkit`)과 GitHub 저장소 주소는 팀 상황에 맞게 바꿔 쓴다.
