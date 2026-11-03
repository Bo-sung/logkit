# 서버 UI/대시보드 프로젝트 종속 요소 전수 분석 보고서 (ARMORY)

대상: `server/{Analytics,Weapons,Dashboard,Replay,Insights}Page.cs, PageCommon.cs`, `armory-dashboard/{dist/*,index.html,README.md,CODEX_HANDOFF.md,tests/verify.cjs}`
읽기 전용 분석, `server/bin`/`server/obj` 미열람. 범용 브라우저 UI 인프라(차트 유틸, 어두운 테마 등)는 제외.

## 요약 표

| # | 파일:라인 | 종속 유형 | 종속 내용 요약 | 추상화 제안 |
|---|---|---|---|---|
| 1 | AnalyticsPage.cs:11-214 | 특정 게임 UI·리포트·문구 | 페이지 제목/내비 "GM 툴·총기 통계·가설 통계·습격 리플레이", 섹션 "상점 운영/야간 습격" 등 게임 화면 명칭 | 페이지 라우트·메뉴를 게임 중립 설정 테이블로 |
| 2 | AnalyticsPage.cs:166-167 | 경로·키 | ENDPOINT `/api/analytics`, PREF_STORE `logkit.analytics` | 엔드포인트/프리퍼런스 키 설정 주입 |
| 3 | AnalyticsPage.cs:205-232 | 하드코딩 도메인 상수·데이터 | 집계 기준 pre에 Shop/NightRaid 이벤트명 나열, `price/satisfied/tagCount` 필드 | 이벤트 스키마 플러그인/설정 |
| 4 | WeaponsPage.cs:10-659 | 특정 게임 UI·문구 | BASE_BLOCK "기본 블럭", TUTORIAL "튜토리얼 기본 구성", 파츠 명칭 | 튜토리얼/기본 블럭 도메인 상수 분리 |
| 5 | WeaponsPage.cs:215-216 | 하드코딩 도메인 상수 | `weaponCode`는 게임 공유 코드(WSC:…), 모듈명은 Assets/Data/Modules displayName | 무기 코드 복호화 어댑터 |
| 6 | DashboardPage.cs:200 | 하드코딩 도메인 상수 | HIDDEN_METRICS `Scan/SoundSource` = 게임 계측 지표명 | 지표 키 화이트리스트 설정 |
| 7 | ReplayPage.cs:128 | 경로·키 | 맵 씬 placeholder `NightRaid_BankGrand` | 씬 목록 동적 주입 |
| 8 | InsightsPage.cs:383,427 | 하드코딩 도메인 상수 | 은퇴 비용 `현재 $250,000` | 경제 상수 설정화 |
| 9 | InsightsPage.cs:569-574 | 도메인 특화 비즈니스 로직 | RULE_DEFAULT 관통 8/폭발 11..., ENDLESS_FROM 7일차 = 게임 고정 일차 | 규칙/일차 정책 설정 |
| 10 | InsightsPage.cs:1357-1358 | 하드코딩 도메인 데이터 | oddsPool 확률표 가중치(꽝 614 등) | 슬롯 확률표 플러그인 |
| 11 | armory-dashboard dist/app.js | 특정 게임 UI·문구 | 페이지 탭 "플레이테스트 개요/야간 습격/총기·모듈", `프로젝트 / 총기 제작 게임` | 프로젝트 중립 페이지 레지스트리 |
| 12 | armory-dashboard README/verify.cjs | 하드코딩 데이터 | 매출 29155, 4196행 등 ARMORY 실측 수치 | 집계 정의만 유지, 수치는 외부 데이터 |

## 항목별 상세

### 1. server/AnalyticsPage.cs:11-214 — 특정 게임 UI·리포트·문구
- 내용: `<title>LogKit 버전 통계`, nav 링크(`/weapons`,`/insights`,`/replay`), "상점 운영", "야간 습격" 구분, "판매 조건 만족 ≠ 전체 주문 성공률" 문구 등 게임 도메인 화면 구성.
- 제안: 게임 중립 공통 레이아웃 + 게임별 화면 메타(제목/메뉴/섹션) 설정.

### 2. server/AnalyticsPage.cs:166-167 — 경로·키
- `const ENDPOINT = "/api/analytics";`, `PREF_STORE = "logkit.analytics"` — API 경로·localStorage 프리퍼런스 키 하드코딩.

### 3. server/AnalyticsPage.cs:205-232 — 하드코딩 도메인 상수·데이터
- pre 블록: `Shop / weapon_sold price...`, `NightRaid / 습격 시작 scene` 등 이벤트 정의 — 게임 로그 이벤트명 결합.

### 4. server/WeaponsPage.cs:10-659 — 특정 게임 UI·문구 / 하드코딩 상수
- `BASE_BLOCK = "기본 블럭"`, `TUTORIAL = "튜토리얼 기본 구성"` — 게임 튜토리얼 구성 명칭.

### 5. server/WeaponsPage.cs:215-216 — 경로·키/도메인 상수
- `weaponCode` = 게임 무기 공유 코드(WSC:…), 파츠 = 게임 `Assets/Data/Modules` displayName → 게임 데이터 의존.

### 6. server/DashboardPage.cs:200 — 하드코딩 도메인 상수
- `HIDDEN_METRICS = new Set(["Scan","ScanUsed","SoundSource","SoundSourceAccessed"])` — 게임 전용 스캔/사운드 계측명.

### 7. server/ReplayPage.cs:128 — 경로·키
- `placeholder="NightRaid_BankGrand"` 등 맵 씬 이름 하드코딩.

### 8. server/InsightsPage.cs:383,427 — 하드코딩 도메인 상수
- 은퇴 비용 `$250,000` 등 게임 경제 수치.

### 9. server/InsightsPage.cs:569-574 — 도메인 특화 비즈니스 로직
- `RULE_DEFAULT = {pen:8,exp:11,...}`, `ENDLESS_FROM = 7`(7일차부터 무한 모드) — 게임 규칙/일차 체계.

### 10. server/InsightsPage.cs:1357-1358 — 하드코딩 도메인 데이터
- 슬롯머신 oddsPool 가중치 표(현재 $1,000 등).

### 11. armory-dashboard/dist/app.js — 특정 게임 UI·문구
- `titles` 맵: overview "플레이테스트 개요", raid "야간 습격" 등 ARMORY 프로젝트 명칭; `filename='7f18f899_20261015_102421.csv'`.

### 12. armory-dashboard/README.md:12 / tests/verify.cjs — 하드코딩 데이터
- 원본 4,196행·판매액 29155 등 실제 플레이 로그 기반 수치; verify.cjs가 `records.length==4196` 등 고정.

## 범용 인프라(제외)
PageCommon.Css/Script, vanilla JS 차트, localStorage 키 `logkit.key` 등은 범용 UI 인프라로 분류해 분리 대상에서 제외하되, 게임 도메인 결합 부분(예: PALETTE 색 배정)은 필요 시 설정화 제안.
