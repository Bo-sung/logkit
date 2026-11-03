# impl-combined-report-fix 보고서

결함 수정: (1) 스냅샷 data Web 옵션 직렬화 → 키가 API와 일치 (2) selected=기본 전체 버전 목록, colorOf=SERIES 팔레트 복제 (3) 셸 문서 doctype/title/탭바/iframe/전환 script 추가 (4) export 버튼 옆 전체 리포트 버튼 동적 삽입.

검증: dotnet 빌드 오류 0; report/full 4개 스냅샷 주입 확인; 각 페이지 전체 리포트 버튼 존재; exportSnapshot 미변경.
