# 서버 게임 프로젝트(ARMORY) 종속 요소 분석 보고서

대상: `C:/Gits/logkit/server` 중 나열된 파일 전수 정독 결과. 범용 로그 수신·저장 인프라(CSV 파서, 세션 저장소, 업로드 수신함, 인증 미들웨어 골격)는 종속에서 제외하고 게임 도메인 결합분만 수록.

## 요약 표

| # | 파일경로 | 종속 유형 | 항목 |
|---|----------|-----------|------|
| 1 | GameAnalytics.cs:14-73 | 하드코딩 도메인 상수·데이터(이벤트명·카테고리) | GameEvents: ARMORY 전용 이벤트명/카테고리 상수 |
| 2 | GameAnalytics.cs:238-250 | 특정 게임 UI·리포트(스테이지 표) | RaidStages: 습격 씬·표시명·Vault 내성 하드코딩 |
| 3 | GameAnalytics.cs:74-1559 | 도메인 특화 비즈니스 로직(집계·파생 규칙) | Digest/WeaponTracker/InsightTracker 세션 요약 집계 |
| 4 | WeaponCode.cs:22,28-81 | 하드코딩 도메인 상수·데이터(GUID·테이블) + 경로·키·시크릿 | 모듈 GUID 테이블(_moduleNames/_modulePrices), HMAC 키 |
| 5 | WeaponCode.cs:16,52 | 하드코딩 도메인 상수 | WSC: 포맷·CorePrice 등 게임 공유코드 규칙 |
| 6 | MapStore.cs:16,31,136 | 경로·키·시크릿 / 게임 맵 데이터 | data/maps 경로, 씬 이미지 형식 테이블 |
| 7 | Insights.cs:863-926 | 하드코딩 도메인 상수·데이터 | CustomerNames: 손님 프로필 GUID→에셋명 테이블 |
| 8 | Program.cs:16 | 경로·키·시크릿 | ApiKey 기본값 "dev-log-key" |
| 9 | Program.cs:라우트 | 특정 게임 UI·리포트 | /analytics, /weapons, /insights, /replay, /maps 엔드포인트 |
|10| ApiKeyAuth.cs:16 | 경로·키·시크릿 | ApiKey 옵션 기본값 |
|11| SessionSummary/Metrics/Csv*/UploadInbox/SessionTrash/CsvText/SessionQuery/Dto | 범용 인프라 | 게임 종속 요소 없음(제외) |

## 항목별 상세

### 1. GameAnalytics.cs:14-73 — 하드코딩 도메인 상수·데이터
- **종속 내용**: `GameEvents` 정적 클래스. 카테고리(`Shop`, `Workshop`, `NightRaid`, `System`, `Tutorial`)와 이벤트명(`weapon_sold`, `workbench_entered`, `raid_first_clear`, `총 판매`, `작업대 진입` 등 ARMORY 게임 전용)을 하드코딩. 주석: "ARMORY 대시보드(armory-dashboard/README.md)와 같다", ShopLogCollector eventType 호환 등 게임 규약.
- **추상화 제안**: 이벤트 이름·카테고리를 설정 파일/플러그인(이벤트 규약 테이블)으로 분리해 게임 교체 시 상수 수정 없이 매핑만 교체.

### 2. GameAnalytics.cs:238-250 — 특정 게임 UI·리포트
- **종속 내용**: `RaidStages` 배열 — 습격 씬 이름(`NightRaid_BankGrand` 등), 표시 이름, Vault 관통 내성(3/1)은 게임 Assets/Resources/RaidMapData.json 기반 리포트 표.
- **추상화 제안**: 스테이지 메타를 설정/외부 데이터(RaidMapData) 플러그인으로 분리.

### 3. GameAnalytics.cs:74-1559 — 도메인 특화 비즈니스 로직
- **종속 내용**: 세션 CSV를 이벤트 규약으로 줄이는 `Digest`(판매 합산, 작업대 체류 쌍, 습격 구간, 총기 구성 Key 집계), `WeaponTracker`(WSC 코드 디코딩·구성 빌드 규칙), `CompareVersions` 파생 지표 규칙.
- **추상화 제안**: 집계/파생 규칙 인터페이스 + 게임별 플러그인 또는 규칙 설정(DerivedMetricOptions 확장).

### 4. WeaponCode.cs:22 — 경로·키·시크릿
- **종속 내용**: HMAC 무결성 키 `_macKey = SHA256(... "WGG.weapon-code.v1.compact-integrity")` — 게임 전용 공유코드 서명 키.
- **추상화 제안**: 설정 파일/환경변수 기반 서명 키로 분리.

### 5. WeaponCode.cs:28-81 — 하드코딩 도메인 상수·데이터
- **종속 내용**: `_moduleNames`(모듈 GUID→표시명 테이블, Assets/Data/Modules 기준), `_modulePrices`(GUID→buy/credits/산탄 여부 = WeaponPurchasePrices), `CorePrice = 50`(게임 구매가).
- **추상화 제안**: 모듈 카탈로그 설정 파일/플러그인으로 분리.

### 6. WeaponCode.cs:16,52 — 하드코딩 도메인 상수
- **종속 내용**: `WSC:` 공유코드 포맷 상수, 코어 구매가 규칙 등 게임 공유코드 규격.
- **추상화 제안**: 코덱을 게임 모듈 플러그인 경계로 분리.

### 7. MapStore.cs:16 — 하드코딩 도메인 상수
- **종속 내용**: 이미지 형식→MIME 테이블(png/jpg/webp) 및 `MaxImageBytes` 등 게임 맵 UI 등록 규칙.
- **추상화 제안**: 맵 형식 설정 분리.

### 8. MapStore.cs:31 — 경로
- **종속 내용**: `MapsDirectory = Path.Combine(_options.DataPath, "maps")` — 게임 습격 맵 이미지 저장 경로(프로젝트 종속 경로).
- **추상화 제안**: 맵 저장소 경로를 설정(DataPath) 추상화로 유지하되 게임명 하드코딩 제거.

### 9. Insights.cs:863-926 — 하드코딩 도메인 상수·데이터
- **종속 내용**: `CustomerNames` — 손님 프로필 GUID→에셋명 테이블(Assets/Resources/CustomerOrderData.json 기준).
- **추상화 제안**: 손님 카탈로그 설정/플러그인 분리.

### 10. Program.cs:16 — 경로·키·시크릿
- **종속 내용**: `ServerOptions.ApiKey` 기본값 `"dev-log-key"`가 ARMORY 클라이언트 기본 인증 키와 결합.
- **추상화 제안**: 인증 키를 설정 파일/외부 비밀관리로 분리.

### 11. Program.cs(라우트) — 특정 게임 UI·리포트
- **종속 내용**: `/analytics`, `/weapons`, `/insights`, `/replay`, `/maps`, `/dashboard` 등 ARMORY 전용 분석·리포트 페이지 엔드포인트.
- **추상화 제안**: 범용 인프라(`/api/logs` 등)와 게임 리포트 라우트를 플러그인 경계로 분리.

### 12. ApiKeyAuth.cs:16 — 경로·키·시크릿
- **종속 내용**: `options.ApiKey`와 비교하는 인증 키 기본값.
- **추상화 제안**: 위 10과 동일(설정 분리).

### 제외 항목
- CsvSessionStore, CsvText, SessionQuery, Metrics, Dto, UploadInbox, SessionSummary, SessionTrash: 범용 로그 수신·저장 인프라로 게임 종속 아님.
