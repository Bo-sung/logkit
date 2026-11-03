# impl-combined-report 보고서

- 신규 GET /report/full 라우트: ApiKey 검증 후 4개 페이지 데이터를 기존 Build 함수로 조합 → 셸+iframe srcdoc 스냅샷 주입, attachment 다운로드
- 검증: dotnet build 오류 0; report/full?month=all → 443KB, LOGKIT_SNAPSHOT 4회 주입 확인; month=2026-10 저장 완료; iframe 4개; data에 versions 존재; exportSnapshot 미변경
