# GGGG 분석 서버

## 저장소 구성

이 저장소는 공용 코어를 서브모듈 `core/`(LogKit Core 저장소)로 참조한다. 받을 때는 서브모듈도 함께 받는다.

```bash
git clone --recurse-submodules <이 저장소 URL>
# 이미 받은 경우
git submodule update --init --recursive
```

GGGG 게임 전용 분석 서버. 공통 수신 서버(LogKit 저장소의 `server/`)의 내보내기 API에서 세션 로그를 주기적으로 복사해 미러(`data/`)에 쌓고,
그 사본으로 버전 통계·가설 통계·총기 통계·습격 리플레이를 계산한다. 두 서버는 HTTP로만 이야기하므로 따로 배포·재시작한다.

## 실행

요구사항: .NET 8 SDK. 수신 서버가 떠 있어야 최신 세션을 받는다(꺼져 있으면 마지막 미러로 계속 응답).

```bash
dotnet run        # http://0.0.0.0:5001 에서 대기
```

설정은 `appsettings.json`:

| 키 | 기본값 | 설명 |
| --- | --- | --- |
| Urls | `http://0.0.0.0:5001` | 대기 주소 |
| ApiKey | `dev-log-key` | 분석 페이지와 `/api` 호출이 요구하는 키 |
| IngestUrl | `http://localhost:5000` | 세션을 복사해 올 수신 서버 주소 |
| IngestApiKey | `dev-log-key` | 수신 서버의 열람 키(`ReadApiKey`)와 맞춘다. 읽기만 하므로 열람 키면 충분하다 |
| Project | `gggg` | 이 분석 서버가 맡는 프로젝트. 수신 서버에서 이 프로젝트 세션만 복사한다. 수신 서버 `Projects`의 이름과 같아야 한다 |
| SyncIntervalSeconds | 60 | 수신 서버 세션 목록을 다시 확인하는 간격(5초 이상) |
| IngestTimeoutSeconds | 120 | 수신 서버 요청 한 번의 제한 시간 |
| GmToolUrl | `http://localhost:5000/dashboard` | 분석 페이지 머리글의 "GM 툴" 링크 주소. 브라우저가 열 수 있는 주소(사내망이면 `172.21.10.67`)로 맞춘다 |
| DataPath | `data` | 미러 저장소. 이 서버만 쓴다. 수신 서버의 `data`와 같은 폴더로 두지 않는다 |
| WeaponCodeHmacKey | `WGG.weapon-code.v1.compact-integrity` | 총기 공유 코드 무결성 키(클라이언트와 맞춘 값) |

## 동기화 방식

- 기동 직후 한 번, 이후 `SyncIntervalSeconds`마다 `GET /api/export/index`로 수신 서버의 세션 목록(ID·`updatedUtc`)을 읽는다.
- 미러에 없거나 변경 시각이 다른 세션만 통합 CSV·메타·지표를 내려받아 저장하고 월별 요약 행을 다시 계산한다.
- 수신 서버에서 사라진 세션(삭제·휴지통)은 미러에서도 지우고 요약 행을 뺀다. 휴지통에서 복원되면 다음 주기에 다시 들어온다.
- 미러 파일의 수정 시각을 수신 서버 시각으로 맞춰 두므로, 분석 캐시도 바뀐 세션만 다시 계산한다.
- 원문(raw)은 가져오지 않는다. 분석은 통합 CSV와 메타·지표만 쓴다.
- 상태는 `GET /api/mirror/status`에서 본다. 마지막 오류가 있어도 서버는 멈추지 않고 다음 주기에 다시 시도한다.

## 엔드포인트

| 엔드포인트 | 인증 | 성공 응답 | 설명 |
| --- | --- | --- | --- |
| `GET /api/analytics` | 필요 | 200 JSON | 같은 앱 버전의 모든 세션 로그를 합산한 누적 통계. `month`(yyyy-MM, 생략 시 전체 기간), `excludeEditor=true`(에디터 세션 제외). 형식이 틀리면 400 |
| `GET /api/analytics/weapons` | 필요 | 200 JSON | 작업대 퇴장·시험 사격 입력의 총기 코드를 해석해 버전별 구성(Configs) 합계로 돌려준다. 파라미터는 `/api/analytics`와 같다 |
| `GET /api/analytics/insights` | 필요 | 200 JSON | 세션별 일차·주문·습격 기록을 버전별로 돌려준다. 분포·비율은 화면이 계산한다. 파라미터는 `/api/analytics`와 같다 |
| `GET /api/maps` | 필요 | 200 JSON | 맵 윗면 이미지 목록(씬, 월드 범위 minX·maxX·minZ·maxZ, 등록 시각) |
| `GET /api/maps/{scene}/image` | 필요 | 200 이미지 | 맵 이미지(`?key=` 가능) |
| `POST /api/maps/{scene}` | 필요 | 200 JSON | multipart: image(PNG·JPEG·WebP, 20MB 이하) + minX·maxX·minZ·maxZ 또는 bounds JSON 파일. 같은 씬은 교체 |
| `DELETE /api/maps/{scene}` | 필요 | 200 JSON | 맵 이미지 삭제 |
| `GET /api/analytics/replay` | 필요 | 200 JSON | 버전·스테이지별 습격 궤적(0.2초 간격 위치·시선·체력, 발사·피격 위치, 적 처치 시각). month·excludeEditor·version·stage, 최근 300회 |
| `GET /api/months` | 필요 | 200 JSON | 미러에 있는 월 목록과 월별 집계(분석 페이지 월 선택용) |
| `GET /api/mirror/status` | 필요 | 200 JSON | 미러 동기화 상태(마지막 성공 시각·오류·세션 수·갱신·삭제 수) |
| `GET /report/full` | 키 쿼리 | 200 HTML | 버전·총기·가설·리플레이 4개 페이지를 탭으로 묶은 통합 리포트 파일 |

인증은 `X-Api-Key` 헤더 하나다. 브라우저 다운로드 링크처럼 헤더를 붙일 수 없는 요청은 `?key=API키` 쿼리로 대신할 수 있다. `/health`와 `/dashboard` 페이지 자체는 예외고, 대시보드가 부르는 `/api` 호출은 전부 검사한다.


## 버전별 누적 통계 (/analytics)

GM툴이 세션 로그를 하나씩 보는 화면이라면, `http://localhost:5000/analytics`(GM툴 상단 링크)는 **같은 앱 버전(`appVersion`, 예: 0.1.2)의 모든 세션 로그를 합쳐** 버전끼리 비교한다. API 키는 GM툴과 공유한다.

- **버전 선택**: 최대 8개. 색은 버전에 고정되어 선택을 바꿔도 남은 버전의 색이 바뀌지 않는다. 월 단위·전체 기간, 에디터 세션 제외를 고를 수 있다.
- **누적 지표 표**: 세션 수·기간·플레이 시간·정상 종료 비율 / 판매 건수·판매액·판매 조건 만족 비율·태그 수·주문 성공 비율·작업대 체류 / 습격 수·생존 비율·소요 시간·습격당 블록 파괴와 적 생성·발견·처치·종료 체력·상자 크레딧. 세션 수가 버전마다 다르므로 세션당·습격당 값을 함께 보여 준다.
- **상점**: 판매액 분포, 판매 태그 수 분포, 작업대 체류 시간 분포(버전 내 비율).
- **습격**: 스테이지별 생존 비율, 게임 시간별 평균 상태(체력·남은 모듈 상자·남은 재료 상자·탱크·헬기), 스테이지별 습격 통계 표, 종료 보상 모듈 ID 비율, 스테이지별 위치 히트맵(X·Z 동일 축척, 버전별 나란히, 전체·일차별 나눠 보기·한 일차 선택 — 습격 일차는 `습격 시작`의 `day`, 없으면 직전 상점 일차).
- **미수집 항목**: 습격 중 휴대 총기·실제 발사 수·위치별 사격은 로그에 없어 집계하지 않고 화면에 미수집으로 표시한다.

세션별 중간 결과는 세션 CSV 수정 시각 기준으로 메모리에 캐시한다.

### 집계 기준 — 게임이 남기는 이벤트

category와 message로 식별하고 data_json 필드를 읽는다. 규칙은 `gggg-dashboard/README.md`와 같다. 상점 이벤트는 초기 빌드의 한글 이름과 이후 `ShopLogCollector`의 eventType 이름을 모두 받는다.

| category | message | data_json | 집계 |
| --- | --- | --- | --- |
| Shop | weapon_sold (초기: 총 판매) | `price`, `satisfied`, `tagCount`, `orderId` | 판매·판매액. Unity 카테고리의 `[Customer] 총 판매 …` 출력은 중복이라 제외 |
| Shop | workbench_entered / workbench_exited (초기: Workshop / 작업대 진입·퇴장) | `orderId`, 퇴장은 `weaponCode`, `weaponCodeStatus` | 같은 씬의 시간순 진입→퇴장 쌍만 체류 시간. 퇴장 코드는 총기 통계 |
| Shop | attack_input / weapon_fired | `weaponCode` / `weaponAssemblyId`, `position` | 총기 통계(시험 사격 입력 / 실제 발사) |
| Shop | order_completed | `success`, `customerDead` | 주문 성공 비율·고객 사망 |
| NightRaid | 습격 시작 | `scene` | 습격 구간 시작. 씬 전환 전(Main)에 기록되므로 습격 씬은 `data_json.scene` |
| NightRaid | 플레이어 상태 | `time`, `position{x,y,z}`, `cameraRotation`, `health`, `remainingModuleCrates`, `remainingMaterialCrates`, `tanks`, `helicopters` | 게임 시간 타임라인·위치 히트맵 |
| NightRaid | 블록 파괴 | `stageId`, `time`, `blockId` | 습격당 파괴 수 |
| NightRaid | 적 생성 / 적 발견 / 적 처치 | `time`, `enemyId` | 습격당 수. `enemyId`는 개체 ID라 종류로 묶지 않음 |
| NightRaid | 습격 사망 / 습격 탈출 성공 / 습격 성공 | `scene`, `survived`, `time`, `duration`, `enemyKills`, `health`, `moduleCrateRewards[]`, `moduleCrateCredits`, `materialCrateCredits` | 습격 구간 종료. 생존 비율·소요 시간·종료 값 평균 |

- `timestamp_utc`(실제 시각)와 `data_json.time`(게임 시간)은 섞지 않는다. 습격 차트는 게임 시간 기준이다.
- 기록되지 않은 값은 0으로 바꾸지 않고 `null`(화면에는 –)로 둔다. 종료 이벤트 없이 끝난 습격은 종료 기록 수에서 빠진다.
- 모듈 상자·재료 상자는 따로 표시하고 다른 이름(금고 등)으로 바꾸지 않는다. 종료 보상은 기록값이며 사망 후 실제 지급 여부는 알 수 없다.
- 플레이어 상태를 물리 프레임(0.02초)마다 남기면 10분 습격 한 판에 약 3만 행이 된다. 0.2~0.5초 간격으로 솎아 기록하길 권장한다.

## 총기 모딩 통계 (/weapons)

`http://localhost:5000/weapons`(버전 통계 상단 링크)는 로그에 실린 무기 공유 코드(`WSC:…`)를 서버에서 해석해 버전별로 비교한다. 해석은 `WeaponCode.cs`가 하며 게임의 `WeaponShareCode.Decode`와 같은 형식이다(HMAC 무결성 검사 포함, 무기 이름은 복호화하지 않음).

| category | message | 총기 필드 | 쓰임 |
| --- | --- | --- | --- |
| Shop | workbench_exited | `weaponCode` | 빌드(작업대에서 나올 때의 총) |
| Shop | weapon_sold | `weaponCode` | 실제 판매된 총. 코드가 없는 이전 빌드 로그는 같은 `orderId`의 마지막 빌드로 추정 |
| Shop | attack_input | `weaponCode` | 시험 사격 입력(누를 때 1회, 발사 성공 아님) |
| Shop | weapon_fired | `weaponAssemblyId`, `position` | 공방 실제 발사 |
| NightRaid | 습격 시작 | `weaponCode`, `weaponAssemblyId` | 습격에 들고 간 총 |
| NightRaid | raid_weapon_config | `weaponCode`, `weaponAssemblyId` | 습격 중 처음 쓰는 구성 |
| NightRaid | weapon_fired | `weaponAssemblyId`, `position` | 습격 실제 발사(스테이지별 파츠 사용·발사 위치 히트맵) |

- **구성 ID**: 서버는 코드에서 게임 `GetAssemblyKey`(= `TestAssemblyKey`, 로그의 `weaponAssemblyId`)와 같은 값을 계산해 구성 키로 쓴다. 그래서 코드 없이 구성 ID만 있는 발사도 같은 세션에서 코드를 본 구성이면 파츠로 풀린다. 못 본 구성은 "구성 미확인"으로 따로 센다.
- **빌드**: 작업대 퇴장 1회 = 빌드 1개. 같은 `orderId`에서 직전 퇴장과 구성이 같으면 세지 않는다.
- **튜토리얼 기본 구성**: 코어 좌·우·위·아래·뒤에 기본 블럭 5개를 붙인 Day0 튜토리얼 총. 화면에서 기본으로 제외한다.
- **집계 대상**: 빌드 / 판매된 총 / 습격 휴대 / 습격 발사 / 공방 발사 / 시험 사격 입력 중 하나를 골라, 그 수를 가중치로 파츠 사용량(채택률·블록 점유율·넣었을 때 평균 개수)·파츠 조합 빈도·동시 사용 행렬·총 크기 분포·많이 쓴 구성을 계산한다. API는 구성별 합계(Configs)만 보낸다.
- **모듈 이름**: 게임 저장소 `Assets/Data/Modules/*.asset`의 `codeId`(에셋 GUID)·`displayName`을 `WeaponCode.cs`에 표로 둔다. 모듈이 추가되면 표에 더한다. 모르는 GUID는 `모듈 + 앞 8자리`로 표시된다.

## 맵 배경 이미지

`data/maps/{scene}.png|jpg|webp`와 `{scene}.json`(이미지가 덮는 월드 범위)을 위치 히트맵·발사 위치 히트맵·습격 리플레이의 배경으로 깐다
(이미지 왼쪽~오른쪽 = minX~maxX, 아래~위 = minZ~maxZ). 이미지는 Unity 패키지의 `Tools > LogKit > 맵 윗면 캡처`로 만들고,
리플레이 페이지의 **맵 배경 이미지 관리**에서 올리거나 캡처 도구에서 바로 올린다. 각 페이지의 **맵 배경** 체크로 켜고 끈다.
HTML 저장본에는 맵 이미지가 긴 변 1024px JPEG로 함께 들어간다.

## HTML 저장

버전 통계·총기 통계·가설 통계·습격 리플레이 머리글의 **HTML 저장** 버튼은 지금 불러온 데이터(기간·에디터 제외·선택 버전·화면 설정 포함)를
페이지 안에 넣은 단독 HTML(`logkit-{페이지}-{월|all}-{yyyyMMdd-HHmm}.html`)로 내려받는다. 저장본은 서버 없이 열리며 차트·탭·필터·툴팁·재생이
그대로 동작한다. 저장본에서는 불러오기·기간 선택이 숨겨지고 저장 시각 표시가 붙으며, 리플레이 저장본은 저장할 때의 버전·스테이지만 담는다.

## 습격 리플레이 (/replay)

`http://localhost:5000/replay`는 한 버전·한 스테이지의 모든 야간 습격을 습격 시작 시각에 맞춰 겹쳐 재생한다. 키 입력 자체는 기록되지 않으므로
입력의 결과인 `NightRaid / 플레이어 상태`(위치·cameraRotation.y·체력, 0.2초 간격으로 줄임), `weapon_fired`·`player_hit` 위치, `적 처치` 시각으로 재현한다.
재생·배속·시간 막대, 꼬리 길이, 시선·발사·피격 표시, 결과·일차 필터, 습격별 표시·강조를 지원한다. 결과 색은 습격 종료의 survived·exitReason을 따른다.

## 가설·일반 통계 (/insights)

`http://localhost:5000/insights`는 개선안 검증용 차트를 버전별로 비교한다. 플레이어 단위는 세이브(`saveId` — 저장 불러오기로 이어 한 세션을 합침)이며
saveId가 없는 이전 로그는 세션이다. 일차는 상점 이벤트의 `day`, 습격은 `습격 시작`의 `day`(없으면 직전 일차)의 밤이다.
같은 주문(`orderId`)은 여러 세션에 기록돼도 하나로 합치고, 이탈은 플레이어의 마지막 낮·밤 구간이다(파산은 `Game / bankrupt`).
습격 총은 `습격 시작`의 코드, 없으면 그 습격에서 가장 많이 쏜 구성(같은 세션에서 코드를 본 것)이고, 관통 수치는 `습격 시작`의 `penetration`이다.
금고 관통 내성은 현재 씬 값(은행 3, 나머지 1)을 임시로 쓴다. 튜토리얼 퍼널은 `Tutorial / step_started·step_completed·tutorial_skipped·tutorial_ended`로 그리며
`debugSkipped`가 있는 진행은 뺀다. n일차 낮·밤 보유 달러는 낮 = `shop_opened`의 `balance`, 밤 = 그날 `shop_close_requested`의 `balance` 또는
다음 날 영업 시작 잔액(선택)이다. 모듈 보유 개수는 기본 해금 12종 + 그 구간까지 습격 종료 `moduleCrateRewards`로 얻은 잠긴 모듈(ID 10~16) 종류 수다. 손님 이름표(`Insights.cs` `CustomerNames`)는 게임 `CustomerOrderData.json` 기준이다.
습격 경제 카드는 습격 시작의 `entryCost`·`training`·`spawnedModuleCrates`·`spawnedMaterialCrates`·`materialPerCrate`, 결과 로그의
`dollarReward`·`rewardsApplied`·`materialCrateCredits`·`materialPickups`, `Game / raid_skipped`·`raid_first_clear`로 습격 순수익(dollarReward − entryCost),
상자 회수율, 습격 vs 건너뛴 밤, 사망 손실, 구역 첫 클리어 일차를 그린다. 연습 습격은 모두 뺀다. 일차별 수익 분포의 습격 보상도 dollarReward 기준이다.
슬롯머신 카드는 `Shop / slot_machine_spin_started`(결제 성공 때 1회)로 세션당 회전 수, 플레이어별 환급률(당첨금 ÷ 지출)·순이득, 일차별 이용률,
확률표(`oddsPool`)별 실제 환급률과 게임 가중치로 계산한 이론 환급률, 당첨 심볼 분포, 회전 직전 잔액을 그린다. 당첨금은 지급 예정 금액(`plannedPrize`)이다. 확률표는 `oddsPool`로 구분한다(현재 `fixed` = 1회 $1,000, 이전 빌드 `first_three`·`last_funds`·`ordinary` = $500). 티켓 회전(`paidWithTicket`)은 환급률에서 뺀다.
엔딩 카드는 `Game / retire_completed·retire_failed·ending_choice`와 `NightRaid / end_chest_scanned·end_chest_hit·end_chest_opened`, 습격 결과의 `exitReason`으로
엔딩 도달률·n일차까지 누적 도달률·첫 엔딩까지 누적 플레이 시간(같은 세이브의 세션 시간 합, 일시정지 제외)·엔드 상자 퍼널·엔딩 화면 선택과 대기 시간·은퇴 시점 자금·
상자 조건 대비 명중 탄 수치·상자 소요 시간·습격 종료 사유를 그린다. `습격 엔딩 종료`는 습격 종료로 세며 survived 값을 따른다.

