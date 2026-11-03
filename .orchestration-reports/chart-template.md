# chart-template.md — 공통 차트 키트 토큰과 사용법

## CSS 토큰 (어두운 표면)
- 배경: `--bg: #0f1115`
- 카드: `--panel: #1a1d24`
- 그리드선: `--border: #2a2e37` (`--grid`)
- 본문: `13px system-ui`
- 시리즈 팔레트 고정 순서: `#4f8cff #34d399 #fbbf24 #f87171 #a78bfa #22d3ee` → `SERIES`

## JS 헬퍼 (`PageCommon.Script`)
- `bar(el, value, max, i)` — 막대
- `groupBar(el, rows, keys, valueOf)` — 그룹 막대
- `stackBar(el, segments)` — 스택 막대
- `lineChart(el, points, color)` — 선
- `areaChart(el, points, color)` — 영역
- `heatmap(el, cells, grid)` — 히트맵
- `boxplot(el, stats)` — 박스플롯 공통 래퍼
- 공통 툴팁 `tipShow/tipHide`, 범례 `legendHtml`, 축 포맷터 `axisFmt`

기존 페이지의 `distribution/hbars/lines/pitchHeats`는 점진 마이그레이션용 호환 함수로 유지.
