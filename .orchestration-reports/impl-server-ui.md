# impl-server-ui 보고서

## 변경 파일 목록
- server/PageCommon.cs (CSS 토큰 교체, 공통 차트 키트 JS 헬퍼 추가)
- server/DashboardPage.cs, server/InsightsPage.cs (구 팔레트 리터럴 교체)

## 검증 명령과 결과
- `dotnet build -c Release` → 빌드했습니다 (오류 0)
- 구 팔레트 값(`#3987e5` 등) grep → 0건
- 차트 키트 함수 bar/groupBar/stackBar/lineChart/areaChart/heatmap/boxplot 정의 확인

## 호환성 메모
- 기능·표시 값·엔드포인트·집계 로직 변경 없음. 시리즈 팔레트 값만 고정 순서로 교체.
