# 구현 보고서: gggg-dashboard 전환 및 실측 데이터 커플링 제거

## 변경 목록
1. `git mv armory-dashboard gggg-dashboard` 로 폴더명을 변경하고, 내부 모든 ARMORY/Armory/armory 를 GGGG/Gggg/gggg 로 교체(README.md, CODEX_HANDOFF.md, dist/app.js, dist/index.html). 데이터 CSV 파일명(7f18f899_20261015_102421.csv 등)은 유지.
2. `tests/verify.cjs`의 고정 수치(4196, 29155, 9, 1737, 2411 등)를 제거하고 data CSV를 직접 파싱해 동적 기대값을 계산해 비교하도록 재작성. 지표 집계 정의는 유지.
3. `dist/app.js`, `dist/data.js`, `dist/index.html`에서 프로젝트 고유 명사만 GGGG로 교체하고 일반 용어 탭명(플레이테스트 개요 등)은 유지. export 파일명을 armory-filtered.csv -> gggg-filtered.csv로 변경.
4. 디자인 토큰 적용: 배경 0f1115, 카드 1a1d24, 그리드선 2a2e37, 시리즈 팔레트 4f8cff 34d399 fbbf24 f87171 a78bfa 22d3ee 순, 본문 13px system-ui. 차트 축/툴팁/범례 스타일 통일.

## 검증 결과
(a) `node tests/verify.cjs` -> PASS (동적 기대값: records 4196, revenue 29155, sales 9, states 1737, blocks 2411, visits 9, raids 1, unmatched 0)
(b) gggg-dashboard 내 armory 대소문자 무관 grep -> 0건
(c) 수정 파일 diff 재독 완료(git diff -- gggg-dashboard/)
