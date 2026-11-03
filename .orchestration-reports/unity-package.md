# LogKit Unity 패키지 — 게임 프로젝트(ARMORY) 종속 요소 전수 분석 보고서

대상: `C:/Gits/logkit/package` 전체 (Runtime 6개 + Editor 1개 + asmdef 2개 + package.json + README.md)
판독 범위: bin/obj 생성물 제외, `.meta` 파일은 부수 데이터로 참고만 함.
범용 로깅 인프라(직렬화 모델, 파일 I/O, gzip 전송, 세션 읽기 유틸)는 종속이 아니므로 제외.

## 요약 표

| # | 파일경로:라인 | 종속 유형 | 종속 내용 요약 | 추상화 제안 |
|---|---|---|---|---|
| 1 | Runtime/GameLogger.cs:24 | 하드코딩 도메인 상수·데이터 | 기본 수신 서버 주소 `http://localhost:5000/api/logs` 하드코딩 | 서버 URL은 설정 주입(Configure/Inspector) 경계로 분리 |
| 2 | Runtime/GameLogger.cs:25 | 경로·키·시크릿 | 기본 API 키 `"dev-log-key"` 하드코딩 | 키는 환경변수/설정 플러그인 경계로 외부 주입 |
| 3 | Runtime/GameLogger.cs:26 | 경로·키·시크릿 | 로그 폴더명 `"Logs"` 하드코딩 경로 상수 | 디렉터리명·경로는 설정 플러그인 경계로 분리 |
| 4 | Runtime/GameLogger.cs:40 | 특정 게임 UI·리포트 | `[Header("서버 연결")]` Inspector 섹션 UI 라벨 | UI 메타는 에디터 전용 플러그인 경계로 분리 |
| 5 | Runtime/GameLogger.cs:42-44 | 경로·키·시크릿 | `_developmentServerUrl/_releaseServerUrl/_apiKey` 직렬화 필드 | 전송 설정은 플러그인/설정 스키마로 추상화 |
| 6 | Runtime/GameLogger.cs:251-286 | 도메인 특화 비즈니스 로직 | `Count("Attack")`/`Measure("ComboLength")` — 공격·명중률·콤보 등 전투 지표 집계 | 지표 키는 설정/플러그인 경계로, 집계는 범용 Metric API로 분리 |
| 7 | Runtime/GameLogger.cs:27,422-424 | 기타 | `UNITY_CATEGORY`, 씬 이름(`GetCurrentSceneName`) 캐싱 — 게임 씬 결합 | 씬 추적은 게임 도메인 어댑터 경계로 분리 |
| 8 | Editor/MapTopViewCapture.cs:17-19 | 특정 게임 UI·리포트 | "습격 씬", "위치 히트맵·습격 리플레이" 전용 대시보드 용도 | 맵 캡처는 게임 UI/리포트 플러그인으로 분리 |
| 9 | Editor/MapTopViewCapture.cs:26-31 | 하드코딩 도메인 상수·데이터 | `_cutHeight=3f`, `_maxRendererSize=400f`, `_minX=-50/_maxX=50` 등 좌표·크기 기본값 | 값들은 씬/맵 설정 경계로 주입 |
| 10 | Editor/MapTopViewCapture.cs:39 | 특정 게임 UI·리포트 | `[MenuItem("Tools/LogKit/맵 윗면 캡처")]` 게임 에디터 메뉴 | 메뉴 항목은 에디터 플러그인 경계로 분리 |
| 11 | Editor/MapTopViewCapture.cs:315-323 | 특정 게임 UI·리포트 | `POST /api/maps/{씬}` 맵 이미지·범위 등록(게임 대시보드 연동) | 맵 등록 API는 게임 리포트 플러그인 경계로 분리 |
| 12 | README.md:23,31-33 | 도메인 특화 비즈니스 로직 | `FailData{Stage=3}`, `Count("Attack")`, `Measure("ComboLength")` 예시 — ARMORY 전투 데이터 | 예시 데이터는 범용 샘플로 교체/설정 분리 |
| 13 | README.md:사용법 | 경로·키·시크릿 | `developmentServerUrl: "http://172.21.10.67:5000/api/logs"` — ARMORY 개발 서버 IP 하드코딩 | 서버 주소는 설정 파일로 분리 |
| 14 | package.json:설명 | 기타 | `수신 서버는 저장소의 server 폴더(ASP.NET Core 8)` — 외부 프로젝트(서버) 결합 참조 | 서버 의존성은 플러그인 경계로 분리 |

## 항목별 상세

1. **Runtime/GameLogger.cs:24** `private const string DEFAULT_SERVER_URL = "http://localhost:5000/api/logs";`
   - 유형: 하드코딩 도메인 상수·데이터
   - 요약: 수신 서버 기본 주소가 localhost:5000으로 고정 → ARMORY 로그 서버 도메인에 종속.
   - 제안: `IServerEndpointProvider` 또는 `LogKitSettings` 설정으로 주입.

2. **Runtime/GameLogger.cs:25** `private const string DEFAULT_API_KEY = "dev-log-key";`
   - 유형: 경로·키·시크릿
   - 요약: 기본 API 키 평문 하드코딩.
   - 제안: 시크릿은 설정 플러그인/환경 변수 경계로 분리.

3. **Runtime/GameLogger.cs:26** `private const string LOG_DIRECTORY_NAME = "Logs";`
   - 유형: 경로·키·시크릿
   - 요약: `Application.persistentDataPath/Logs` 폴더명 고정.
   - 제안: 스토리지 레이아웃은 설정 플러그인 경계로 분리.

4. **Runtime/GameLogger.cs:40** `[Header("서버 연결")]`
   - 유형: 특정 게임 UI·리포트
   - 요약: Unity 인스펙터 UI 라벨이 서버 연결이라는 게임 운영 UI에 결합.
   - 제안: 에디터 전용 UI 레이어로 분리.

5. **Runtime/GameLogger.cs:42-44** `[SerializeField] private string _developmentServerUrl...`
   - 유형: 경로·키·시크릿
   - 요약: 전송 URL·키가 Inspector로 노출 → 프로젝트별 서버/키 설정.
   - 제안: 전송 설정은 플러그인 경계로 추상화.

6. **Runtime/GameLogger.cs:251-286** `Count/Measure`
   - 유형: 도메인 특화 비즈니스 로직
   - 요약: 지표 키가 "Attack", "AttackHit", "ComboLength" 등 전투/조준 도메인 집계.
   - 제안: 범용 `IMetricSink`/플러그인 경계로 지표 정의 분리.

7. **Runtime/GameLogger.cs:27, 422-424, 776** `UNITY_CATEGORY`, `GetCurrentSceneName`
   - 유형: 기타
   - 요약: Unity 카테고리/씬 이름 추적은 게임 도메인 결합.
   - 제안: 씬 어댑터로 분리.

8. **Editor/MapTopViewCapture.cs:17-19**
   - 유형: 특정 게임 UI·리포트
   - 요약: 습격 씬 캡처 → 위치 히트맵·습격 리플레이(게임 리포트).
   - 제안: 게임 UI 플러그인으로 분리.

9. **Editor/MapTopViewCapture.cs:26-31**
   - 유형: 하드코딩 도메인 상수·데이터
   - 요약: 절단 높이 3m, 렌더러 최대 400, 기본 맵 범위 [-50,50] 고정.
   - 제안: 맵 좌표/크기는 설정 경계로 주입.

10. **Editor/MapTopViewCapture.cs:39** `[MenuItem("Tools/LogKit/맵 윗면 캡처")]`
    - 유형: 특정 게임 UI·리포트
    - 요약: 게임 에디터 메뉴 등록.
    - 제안: 에디터 플러그인 경계로 분리.

11. **Editor/MapTopViewCapture.cs:315-323** `POST /api/maps/{scene}`
    - 유형: 특정 게임 UI·리포트
    - 요약: 맵 이미지/범위 JSON을 게임 대시보드에 등록.
    - 제안: 게임 리포트 플러그인 경계로 분리.

12. **README.md:23,31-33**
    - 유형: 도메인 특화 비즈니스 로직
    - 요약: `FailData{Stage=3}`, `Attack`, `ComboLength` 예시.
    - 제안: 도메인 예시는 범용화/설정 분리.

13. **README.md:사용법** `http://172.21.10.67:5000/api/logs`
    - 유형: 경로·키·시크릿
    - 요약: ARMORY 개발 서버 IP가 문서에 하드코딩.
    - 제안: 서버 주소는 설정 파일로 분리.

14. **package.json:설명** `수신 서버는 저장소의 server 폴더(ASP.NET Core 8)`
    - 유형: 기타
    - 요약: 별도 서버 프로젝트(non-Unity) 결합 참조.
    - 제안: 서버 의존은 플러그인 경계로 분리.
