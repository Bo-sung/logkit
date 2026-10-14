# LogServer

LogKit 유니티 패키지가 전송한 게임 로그를 받아 저장·집계하고, 브라우저에서 열람하는 수신 서버. ASP.NET Core 8 미니멀 API 기반으로 외부 NuGet 의존성이 없다.

```
gzip POST 수신 → 검증(크기·gzip·JSON·sessionId·partIndex) → 파트별 저장 + 통합 CSV 재구성
→ 원문(raw) 보존 → 월별 요약 재계산 → 요약 JSON 응답

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
| `POST /api/logs` | 필요 | 202(신규) / 200(동일 세션 교체) + 요약 JSON | 로그 업로드 |
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

## 대시보드가 없어도 되는 이유

같은 기능을 전부 API로 제공한다. 대시보드는 API를 부르는 단일 페이지일 뿐이라, 사내 위키에 위 curl 예시를 붙여두고 API로만 쓰는 팀은 브라우저 없이 운영해도 된다.

### 업로드 요청 형식

- 본문: gzip 압축(`Content-Encoding: gzip`) 또는 평문 JSON. gzip 해제 실패 시 400.
- 긴 세션은 20,000건씩 파트로 나뉘며 `partIndex` 순서대로 도착한다.
- JSON 구조 (`UploadBatch`):

| 필드 | 설명 |
| --- | --- |
| sessionId | 파일명으로 쓰인다. 영문·숫자·하이픈·밑줄만 허용(형식 검증 실패 시 400) |
| startedAtUtc / endedAtUtc | 세션 시작·종료 시각(ISO 8601 UTC) |
| appVersion / unityVersion / platform / isEditor | 실행 환경 정보 |
| endedCleanly | 정상 종료 여부(강제 종료·크래시면 false) |
| truncated | 세션 상한 초과로 일부 로그를 버렸는지 여부 |
| partIndex | 0부터의 파트 번호(9999까지). 긴 세션은 20,000건씩 나뉘며 재전송 시 같은 파트를 다시 보낸다 |
| metrics[] | 플레이 지표. key·count·sum. 비어 있으면 서버의 기존 지표를 유지 |
| entries[] | timestampUtc, level, category, scene, message, dataJson, stackTrace |

- 동일 `partIndex` 재전송은 해당 파트 파일을 교체하고 통합 CSV를 다시 만든다(멱등, 응답에 `replaced: true`).
- 응답 본문: sessionId, accepted(이번 파트 행 수), partIndex, partsStored(보유 파트 수), 정제 요약(month, durationSec, endedCleanly, total, warnings, errors, exceptions, topCategory).

## 저장 구조

```
{DataPath}/
├─ sessions/{sessionId}.csv           파트를 병합한 통합 로그 전문. 행 단위 길이 제한 적용
├─ sessions/{sessionId}.p0000.csv     파트별 원본 행(재전송 멱등성의 기준). 헤더 없음
├─ sessions/{sessionId}.meta.json     세션 헤더(시각·버전·플랫폼 등). 마지막 파트 값으로 갱신
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
