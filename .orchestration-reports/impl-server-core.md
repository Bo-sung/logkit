# impl-server-core 보고서

## 변경 파일 목록
- server/GameConfig/game-events.json (신규: 게임 이벤트 카테고리·이벤트명 규약)
- server/GameConfig/raid-stages.json (신규: 습격 스테이지 씬명·표시명·내성 수치)
- server/GameConfig/module-catalog.json (신규: _moduleNames/_modulePrices/CorePrice 카탈로그)
- server/GameConfig/customer-catalog.json (신규: CustomerNames 카탈로그)
- server/GameConfig.cs (신규: GameConfig 기동 시 로딩 + 파일 부재 시 기동 실패)
- server/ModuleCatalog.cs (신규: 모듈 카탈로그 로딩)
- server/CustomerCatalog.cs (신규: 손님 이름 카탈로그 로딩)
- server/GameAnalytics.cs (GameEvents facade/RaidStages → GameConfig 로딩, ARMORY→GGGG)
- server/WeaponCode.cs (HMAC 키 설정화, 카탈로그 로딩)
- server/Insights.cs (CustomerNames → CustomerCatalog)
- server/Program.cs (GameConfig/ModuleCatalog/CustomerCatalog 로딩, WeaponCodeHmacKey 바인딩)
- server/appsettings.json (WeaponCodeHmacKey 추가)
- server/LogServer.csproj (GameConfig JSON 출력 복사 설정)

## 검증 명령과 결과
- `cd C:/Gits/logkit/server && dotnet build -c Release` → 빌드했습니다. 오류 0 (경고 1: 미사용 필드)
- `server/ 에서 armory 대소문자 무관 grep` → 0건
- GameConfig JSON 출력 복사: `bin/Release/net8.0/GameConfig/{game-events.json,raid-stages.json,module-catalog.json,customer-catalog.json}` 확인

## 호환성 메모
- 이벤트명 값·WSC 접두사(WSC:)·HMAC 키 값·api 라우트 경로·CSV 헤더는 불변 유지
- ApiKey 기본값 dev-log-key, WeaponCode HMAC 기본값 기존 문자열로 클라이언트 호환
