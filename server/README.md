# LogServer

LogKit 유니티 패키지가 전송한 게임 로그를 받아 저장·집계하고, 브라우저에서 열람하는 수신 서버. ASP.NET Core 8 미니멀 API 기반으로 외부 NuGet 의존성이 없다.

```
gzip POST 수신 → 크기·키만 검사해 data/inbox에 저장 → 즉시 202
수신함 처리기(백그라운드 1개) → 도착순으로 검증(gzip·JSON·sessionId·partIndex) → 파트별 저장 + 원문(raw) 보존
→ 이번 바퀴에 건드린 세션마다 통합 CSV 재구성·월별 요약 재계산 한 번

Count/Measure 지표 → sessions/{id}.metrics.csv 저장 → 세션·월 단위 집계 + 파생 지표(명중률) 계산

브라우저 → /dashboard (GM툴) → 월별 세션 목록 · 항목 검색 · 지표 집계 · CSV/raw 내려받기 · 삭제
```

## 실행

요구사항: .NET 8 SDK(빌드) 또는 ASP.NET Core 8 Runtime(실행만).

```bash
cd server
dotnet run        # http://0.0.0.0:5000 에서 대기
```

설정은 `appsettings.json`:

| 키 | 기본값 | 설명 |
| --- | --- | --- |
| Urls | `http://0.0.0.0:5000` | 대기 주소 |
| ApiKey | `dev-log-key` | 클라이언트 `Api Key`와 일치해야 한다. 비어 있으면 `/api` 요청을 전부 401로 거부 |
| DataPath | `data` | CSV·raw 저장 루트(프로세스 작업 디렉터리 기준, 상시 운영 시 절대 경로 권장) |
| MaxBodyBytes | 5242880 | gzip 본문 상한(초과 시 413) |
| KeepRawJsonl | true | 수신 원문을 `data/raw/`에 보존할지 여부 |
| RawRetentionDays | 90 | raw 보존 일수. 기동 시 경과분을 삭제 |
| InboxProcessIntervalSeconds | 2 | 업로드가 오면 이만큼 더 기다렸다가 그사이 도착분까지 한 번에 처리 |
| DerivedMetrics | (예시 3건) | 파생 지표 규칙. 분자·분모 지표 키의 몫을 계산한다. 키는 클라이언트 `GameLogger.Count`의 key와 일치 |

```json
"DerivedMetrics": [
  { "Name": "공격 명중률", "Numerator": "AttackHit", "Denominator": "Attack", "Unit": "%" },
  { "Name": "스캔 사용률", "Numerator": "ScanUsed", "Denominator": "Scan", "Unit": "%" },
  { "Name": "소리 발생원 접근률", "Numerator": "SoundSourceAccessed", "Denominator": "SoundSource", "Unit": "%" }
]
```

## 엔드포인트

| 엔드포인트 | 인증 | 성공 응답 | 설명 |
| --- | --- | --- | --- |
| `POST /api/logs` | 필요 | 202 `{"queued":true}` | 로그 업로드. 수신함에 저장만 하고 처리는 백그라운드 |
| `GET /health` | 불필요 | 200 `ok` | 생존 확인 |
| `GET /api/months` | 필요 | 200 JSON | 월 목록 + 월별 집계(세션·로그·경고·오류·예외 합계) |
| `GET /api/sessions?month=` | 필요 | 200 JSON | 월별 세션 목록. `q`(ID·플랫폼·버전 부분 일치), `onlyIssues=true`(문제 세션만), `sort=date\|errors\|total` |
| `GET /api/sessions/{id}` | 필요 | 200 JSON | 세션 메타 + 로그 항목. `levels`(쉼표 구분), `category`, `scene`, `q`(메시지·데이터·스택 검색), `offset`, `limit`(기본 500, 최대 2000). 응답에 플레이 지표(`metrics`)와 파생 지표(`derived`) 포함 |
| `GET /api/metrics?month=` | 필요 | 200 JSON | 월 전체 세션의 지표 합산 + 파생 지표(명중률 등) 계산 |
| `GET /api/sessions/{id}/csv` | 필요 | 200 `text/csv` | 세션 CSV 내려받기 |
| `GET /api/sessions/{id}/raw` | 필요 | 200 `application/gzip` | 수신 원문 gzip 내려받기 |
| `DELETE /api/sessions/{id}` | 필요 | 200 JSON | 세션 CSV·raw·요약 행 일괄 삭제(없으면 404) |
| `DELETE /api/months/{yyyy-MM}` | 필요 | 200 JSON | 월 전체 세션·요약 삭제(없으면 404) |
| `GET /api/summary/{yyyy-MM}.csv` | 필요 | 200 `text/csv` | 월별 요약 CSV 다운로드(없거나 형식이 틀리면 404) |
| `GET /dashboard` | 불필요 | 200 `text/html` | 브라우저 열람 대시보드. `/`는 `/dashboard`로 리디렉션 |
| `GET /analytics` | 불필요 | 200 `text/html` | 버전별 누적 통계 페이지 |
| `GET /api/analytics` | 필요 | 200 JSON | 같은 앱 버전의 모든 세션 로그를 합산한 누적 통계. `month`(yyyy-MM, 생략 시 전체 기간), `excludeEditor=true`(에디터 세션 제외). 형식이 틀리면 400 |

인증은 `X-Api-Key` 헤더 하나다. 브라우저 다운로드 링크처럼 헤더를 붙일 수 없는 요청은 `?key=API키` 쿼리로 대신할 수 있다. `/health`와 `/dashboard` 페이지 자체는 예외고, 대시보드가 부르는 `/api` 호출은 전부 검사한다.

```bash
# 월 목록과 세션 검색
curl -H "X-Api-Key: dev-log-key" http://localhost:5000/api/months
curl -H "X-Api-Key: dev-log-key" "http://localhost:5000/api/sessions?month=2026-10&onlyIssues=true&sort=errors"

# 세션 상세 — Error·Exception만, '보스' 포함 항목 최대 100건
curl -H "X-Api-Key: dev-log-key" \
  "http://localhost:5000/api/sessions/s1234abcd-2026101401?levels=Error,Exception&q=%EB%B3%B4%EC%8A%A4&limit=100"

# 세션 삭제, 월 전체 삭제
curl -X DELETE -H "X-Api-Key: dev-log-key" http://localhost:5000/api/sessions/s1234abcd-2026101401
curl -X DELETE -H "X-Api-Key: dev-log-key" http://localhost:5000/api/months/2026-10

# 요약 열람
curl -H "X-Api-Key: dev-log-key" http://localhost:5000/api/summary/2026-10.csv
```

## 대시보드 (GM툴)

`http://localhost:5000/`을 열면 로그 열람 화면이 나온다. 외부 프레임워크 없이 서버가 단일 HTML을 직접 서빙한다.

- **연결**: 첫 화면에서 API 키를 입력하면 localStorage에 저장되어 이후 자동 연결된다.
- **월 선택**: 월별 집계(세션·로그·경고·오류·예외 합계)를 카드로 표시한다.
- **세션 목록**: 최신순/오류순/로그 많은순 정렬, ID·플랫폼·버전 검색, 문제 세션만 보기. 행을 클릭하면 상세가 열린다.
- **세션 상세**: 레벨(Info~Exception) 체크박스, 카테고리·씬·텍스트 필터, 500건 단위 페이지 이동. 항목 행을 클릭하면 메시지 전문·dataJson(정렬됨)·스택트레이스가 펼쳐진다. 하단에 플레이 지표 표와 파생 지표(명중률 등)가 나온다.
- **월 지표**: 세션 목록 위에 그 달 전체 지표 합계와 파생 지표(공격 명중률 등)가 표시된다(`/api/metrics`).
- **내려받기/삭제**: 세션 CSV·raw gzip 내려받기, 세션 삭제, 월 전체 삭제(월 키 입력 확인).

## 버전별 누적 통계 (/analytics)

GM툴이 세션 로그를 하나씩 보는 화면이라면, `http://localhost:5000/analytics`(GM툴 상단 링크)는 **같은 앱 버전(`appVersion`, 예: 0.1.2)의 모든 세션 로그를 합쳐** 버전끼리 비교한다. API 키는 GM툴과 공유한다.

- **버전 선택**: 최대 8개. 색은 버전에 고정되어 선택을 바꿔도 남은 버전의 색이 바뀌지 않는다. 월 단위·전체 기간, 에디터 세션 제외를 고를 수 있다.
- **누적 지표 표**: 세션 수·기간·플레이 시간·정상 종료 비율 / 판매 건수·판매액·판매 조건 만족 비율·태그 수·작업대 체류 / 습격 수·생존 비율·소요 시간·습격당 블록 파괴와 적 생성·발견·처치·종료 체력·상자 크레딧. 세션 수가 버전마다 다르므로 세션당·습격당 값을 함께 보여 준다.
- **상점**: 판매액 분포, 판매 태그 수 분포, 작업대 체류 시간 분포(버전 내 비율).
- **습격**: 스테이지별 생존 비율, 게임 시간별 평균 상태(체력·남은 모듈 상자·남은 재료 상자·탱크·헬기), 스테이지별 습격 통계 표, 종료 보상 모듈 ID 비율, 스테이지별 위치 히트맵(X·Z 동일 축척, 버전별 나란히).
- **미수집 항목**: 총기 코드·휴대 모듈·사격 테스트·주문 성공률은 로그에 없어 집계하지 않고 화면에 미수집으로 표시한다.

세션별 중간 결과는 세션 CSV 수정 시각 기준으로 메모리에 캐시한다.

### 집계 기준 — 게임이 남기는 이벤트

category와 message(한글 이벤트 이름)로 식별하고 data_json 필드를 읽는다. 규칙은 `armory-dashboard/README.md`와 같다.

| category | message | data_json | 집계 |
| --- | --- | --- | --- |
| Shop | 총 판매 | `price`, `satisfied`, `tagCount` | 판매·판매액. Unity 카테고리의 `[Customer] 총 판매 …` 출력은 중복이라 제외 |
| Workshop | 작업대 진입 / 작업대 퇴장 | — | 같은 씬의 시간순 진입→퇴장 쌍만 체류 시간. 주문 ID가 없어 주문별로 연결하지 않음 |
| NightRaid | 습격 시작 | `scene` | 습격 구간 시작. 씬 전환 전(Main)에 기록되므로 습격 씬은 `data_json.scene` |
| NightRaid | 플레이어 상태 | `time`, `position{x,y,z}`, `cameraRotation`, `health`, `remainingModuleCrates`, `remainingMaterialCrates`, `tanks`, `helicopters` | 게임 시간 타임라인·위치 히트맵 |
| NightRaid | 블록 파괴 | `stageId`, `time`, `blockId` | 습격당 파괴 수 |
| NightRaid | 적 생성 / 적 발견 / 적 처치 | `time`, `enemyId` | 습격당 수. `enemyId`는 개체 ID라 종류로 묶지 않음 |
| NightRaid | 습격 사망 / 탈출 성공 / 습격 성공 | `scene`, `survived`, `time`, `duration`, `enemyKills`, `health`, `moduleCrateRewards[]`, `moduleCrateCredits`, `materialCrateCredits` | 습격 구간 종료. 생존 비율·소요 시간·종료 값 평균 |

- `timestamp_utc`(실제 시각)와 `data_json.time`(게임 시간)은 섞지 않는다. 습격 차트는 게임 시간 기준이다.
- 기록되지 않은 값은 0으로 바꾸지 않고 `null`(화면에는 –)로 둔다. 종료 이벤트 없이 끝난 습격은 종료 기록 수에서 빠진다.
- 모듈 상자·재료 상자는 따로 표시하고 다른 이름(금고 등)으로 바꾸지 않는다. 종료 보상은 기록값이며 사망 후 실제 지급 여부는 알 수 없다.
- 플레이어 상태를 물리 프레임(0.02초)마다 남기면 10분 습격 한 판에 약 3만 행이 된다. 0.2~0.5초 간격으로 솎아 기록하길 권장한다.

## 대시보드가 없어도 되는 이유

같은 기능을 전부 API로 제공한다. 대시보드는 API를 부르는 단일 페이지일 뿐이라, 사내 위키에 위 curl 예시를 붙여두고 API로만 쓰는 팀은 브라우저 없이 운영해도 된다.

### 업로드 요청 형식

- 본문: gzip 압축(`Content-Encoding: gzip`) 또는 평문 JSON. 요청 단계에서는 크기(413)와 빈 본문(400)만 검사한다.
- 클라이언트는 5초마다 새로 쌓인 로그를 다음 `partIndex`로 보낸다. 한 파트는 최대 20,000건 또는 4MB(압축 전)다.
- 깨진 gzip·JSON, 잘못된 sessionId·partIndex는 처리기가 `data/inbox/failed/`로 옮기고 경고 로그를 남긴다(클라이언트는 이미 202를 받았다).
- JSON 구조 (`UploadBatch`):

| 필드 | 설명 |
| --- | --- |
| sessionId | 파일명으로 쓰인다. 영문·숫자·점·하이픈·밑줄만 허용(형식이 틀리면 failed로 이동) |
| startedAtUtc / endedAtUtc | 세션 시작·종료 시각(ISO 8601 UTC) |
| appVersion / unityVersion / platform / isEditor | 실행 환경 정보 |
| endedCleanly | 정상 종료 여부. 중간 파트는 false, 정상 종료 시 마무리 파트만 true. 한 파트라도 true면 세션이 정상 종료로 남는다 |
| truncated | 클라이언트가 일부 로그를 버렸는지 여부. 세션 상한이 없어진 v0.3.0부터 항상 false |
| partIndex | 0부터의 파트 번호(999999까지). 실패한 파트는 같은 번호로 다시 보낸다(그사이 쌓인 줄이 더해질 수 있다) |
| metrics[] | 플레이 지표. key·count·sum. 비어 있으면 서버의 기존 지표를 유지 |
| entries[] | timestampUtc, level, category, scene, message, dataJson, stackTrace |

- 동일 `partIndex` 재전송은 해당 파트 파일을 교체한다(멱등). 처리기가 하나라 도착순으로 처리되며 나중에 온 것이 남는다.
- 응답 본문은 `{"queued":true}`뿐이다. 처리 결과는 서버 콘솔의 `processed session=...` 줄과 대시보드에서 확인한다.

## 저장 구조

```
{DataPath}/
├─ sessions/{sessionId}.csv           파트를 병합한 통합 로그 전문. 행 단위 길이 제한 적용
├─ sessions/{sessionId}.p0000.csv     파트별 원본 행(재전송 멱등성의 기준). 헤더 없음
├─ inbox/{틱}_{순번}.json.gz          처리 대기 수신 본문. 처리 후 삭제(서버가 꺼져도 남아 다음 기동에 처리)
├─ inbox/failed/                      처리할 수 없었던 수신 본문
├─ sessions/{sessionId}.meta.json     세션 헤더(시각·버전·플랫폼 등). 종료 시각은 최댓값, 정상 종료는 OR로 합침
├─ sessions/{sessionId}.metrics.csv   플레이 지표(key,count,sum). 지표가 있는 업로드에서만 갱신
├─ summary/{yyyy-MM}.csv              월별 집계. sessionId 키 upsert (시작시각·지속시간·레벨 카운트·에디터 여부 등)
└─ raw/{sessionId}.p0000.jsonl.gz     파트별 수신 원문. KeepRawJsonl=false면 미저장, RawRetentionDays 경과 시 기동 중 삭제
```

- `/api/sessions/{id}/raw`는 파트 gzip을 다중 멤버 gzip으로 이어 붙여 돌려준다(`gzip -d`로 그대로 해제된다).
- 구버전 단일 파일 세션(`{id}.csv`, `{id}.jsonl.gz`)도 조회·삭제·만료 정리가 그대로 동작한다.

- CSV는 RFC 4180 규칙으로 이스케이프하고 개행은 CRLF로 정규화한다. 메시지·dataJson·스택트레이스는 길이 상한을 잘라낸다.
- 같은 파일에 대한 동시 쓰기는 파일 단위 세마포어로 직렬화하고, 임시 파일 쓰기 후 교체(atomic write)로 저장한다.

## 다른 PC에 상시 배포

1. `dotnet publish -c Release -o publish` 후 `publish/`를 대상 PC로 복사(예: `C:\UnityCI\LogServer`).
2. `appsettings.json`의 `DataPath`를 절대 경로로 지정(작업 디렉터리 의존 제거).
3. 자동시작: 시작 레지스트리(HKCU Run) 또는 예약 작업으로 `deploy/launch.vbs`(숨김 실행 런처)를 실행. 레지스트리 등록 파일은 `deploy/logserver-autostart.reg`.
4. 방화벽: 포트 5000 인바운드 허용 규칙 추가. **exe 최초 실행 시 Windows가 만드는 프로그램 단위 차단 규칙이 포트 허용보다 우선하므로, 접속이 안 되면 차단 규칙(LogServer) 존재 여부를 먼저 확인하고 제거한다.**

## 유의사항

- HTTP 평문 통신이다. 외부 공개 서버라면 리버스 프록시 + HTTPS 구성과 키 관리를 강화한다. `?key=` 쿼리는 서버 접근 로그에 남을 수 있으니 가능하면 헤더 인증을 쓴다.
- 수신 로그의 스택트레이스에 클라이언트 PC의 사용자명이 포함될 수 있다.
- 대시보드와 API에 로그 수정 기능은 없다. 삭제는 세션 단위·월 단위만 지원한다.
