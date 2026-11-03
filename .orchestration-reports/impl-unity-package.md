# 구현 보고서: Unity 패키지 게임 종속 제거 / 명칭 GGGG 정리

## 변경 목록
1. `package/Editor/MapTopViewCapture.cs` — 습격 전용 문구 제거, 씬 라벨·좌표 범위(minX/maxX/minZ/maxZ)·절단 높이·렌더러 크기 필터·manualBounds를 모두 `[SerializeField]`로 노출(기본값 현재 값 유지). POST /api/maps/{scene} 등록 엔드포인트와 필드 와이어 호환 유지.
2. `package/Runtime/GameLogger.cs` — 기본값 상수·동작 유지, 프로젝트별 설정 방법 주석 정리(서버 연결 헤더 주석 중립화, Count/Measure 요약 문구 게임 중립화).
3. `README.md` — Attack/ComboLength/FailData 예시를 Count(EnemyDefeated)/Measure(RunDuration) 중립 지표 예시로 교체, 개발 서버 IP 172.21.10.67 제거 후 localhost 표기, DerivedMetrics 예시 중립화, 패키지 태그 v0.3.0→v0.4.0.
4. `package/package.json` — 버전 0.4.0 유지(0.3.0→0.4.0 정리), `package/README.md` — FailData/Attack/ComboLength 중립화, 서버 주소 localhost 표기.
5. 내부 ARMORY/armory 문자열 전수 GGGG 교체 — package/ 및 루트 README 대상 grep 결과 0건(기존 잔존 없음).

## 검증 결과
(a) 수정 cs 파일(MapTopViewCapture.cs, GameLogger.cs) 전체 재독: 중괄호/괄호/대괄호 균형 0 차이, 참조 무결성 확인.
(b) 파이썬 중괄호 균형 검사: 모든 패키지 cs 파일 `{}()[]` 균형 확인.
(c) package/ 및 루트 README에서 armory 대소문자 무관 grep: 0건.
(d) asmdef 무손상: LogKit.Editor / LogKit.Runtime asmdef 원본 그대로 유지.
(e) git diff 전체 재확인: README.md, package/Editor/MapTopViewCapture.cs, package/README.md, package/Runtime/GameLogger.cs 변경분만 반영.
