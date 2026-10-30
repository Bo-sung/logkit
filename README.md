# LogKit Core

LogKit 로그 수신 서버와 게임별 분석 서버가 함께 쓰는 공용 저장 코어 라이브러리(.NET 8).

- 세션 저장 형식: `sessions/{id}.csv`(통합 CSV), `{id}.meta.json`, `{id}.metrics.csv`, `{id}.pNNNN.csv`(파트), `raw/`, `summary/{yyyy-MM}.csv`
- CSV 파서, 세션 요약(`SessionSummary`), 조회 필터(`SessionQuery`), 지표 합산(`MetricMath`)
- API 키 인증 미들웨어(`ApiKeyAuth`), 공용 페이지 스타일(`PageCommon`), 앱 버전 정렬(`AppVersionOrder`)
- 게임 고유 규칙은 두지 않는다.

## 사용

다른 저장소는 이 저장소를 서브모듈로 두고 `ProjectReference`로 참조한다.

```bash
git submodule add <이 저장소 URL> core
```

```xml
<ProjectReference Include="core/LogKit.Core.csproj" />
```
