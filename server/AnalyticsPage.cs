namespace LogServer;

/// <summary>
/// 버전별 누적 플레이 통계 대시보드의 단일 페이지 HTML.
/// GM툴이 세션 로그 하나씩을 보는 화면이라면, 이 페이지는 같은 앱 버전의 모든 세션 로그를 합쳐 비교한다.
/// 외부 자산 없이 vanilla JS + SVG로 차트를 그리며 /api/analytics를 호출한다. API 키는 GM툴과 같은 localStorage 키를 공유한다.
/// </summary>
public static class AnalyticsPage
{
    /// <summary>분석 페이지 전체 HTML.</summary>
    public const string Html = """
<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>LogKit 버전 통계</title>
<style>
"""
    + PageCommon.Css + """
</style>
</head>
<body>
<header>
  <div class="brand">LOGKIT<span>VERSION STATS</span></div>
  <a href="/dashboard">GM 툴(세션별 로그) →</a>
  <a href="/weapons">총기 통계 →</a>
  <div id="status" class="status"></div>
  <span class="spacer"></span>
  <select id="month"></select>
  <label class="chk"><input type="checkbox" id="noeditor"> 에디터 세션 제외</label>
  <input id="key" type="password" placeholder="API 키" style="width:150px">
  <button id="load">불러오기</button>
</header>
<main>
  <section class="card wide">
    <div class="row" style="margin-bottom:6px">
      <b>비교할 버전</b><span class="muted">(앱 버전별로 모든 세션 로그를 합산 · 최대 8개 · 클릭해 켜고 끔)</span>
    </div>
    <div id="vchips" class="row"></div>
  </section>

  <section class="card wide">
    <h3>버전별 누적 지표</h3>
    <p class="sub">세션 수가 버전마다 다르므로 비교는 세션당·습격당 값을 보세요. 기록이 없는 값은 0이 아니라 –로 표시합니다.</p>
    <div id="kpi"></div>
  </section>

  <div class="section">상점 운영</div>

  <section class="card">
    <h3>판매액 분포</h3>
    <p class="sub">Shop / weapon_sold(초기 빌드는 총 판매)의 price · 버전 내 판매 비율 (Unity 콘솔 중복 출력 제외)</p>
    <div id="c-price" class="chart"></div>
  </section>

  <section class="card">
    <h3>판매 태그 수 분포</h3>
    <p class="sub">판매의 tagCount · 버전 내 판매 비율</p>
    <div id="c-tags" class="chart"></div>
  </section>

  <section class="card">
    <h3>작업대 체류 시간 분포</h3>
    <p class="sub">같은 씬의 시간순 작업대 진입 → 퇴장 쌍 · 버전 내 비율</p>
    <div id="c-workbench" class="chart"></div>
  </section>

  <section class="card">
    <h3>미수집 항목</h3>
    <p class="sub">현재 로그에 없어 집계하지 않는 항목입니다. 추정값을 표시하지 않습니다.
      공방의 총기 구성·파츠는 <a href="/weapons" style="color:var(--accent)">총기 통계</a>에서 봅니다.</p>
    <div class="missing">
      <div><b>습격 중 휴대 총기</b>습격 시작에 총기 코드가 없음</div>
      <div><b>실제 발사 수</b>attack_input은 발사 입력이며 성공 여부가 없음 · 야간은 미기록</div>
      <div><b>위치별 사격</b>발사 이벤트에 위치·구역이 없음</div>
    </div>
  </section>

  <div class="section">야간 습격</div>

  <section class="card">
    <h3>스테이지별 생존 비율</h3>
    <p class="sub">종료 이벤트의 survived 기준 · 막대 옆은 생존 / 종료 기록 습격 수</p>
    <div id="c-survival" class="chart"></div>
  </section>

  <section class="card">
    <h3>습격 게임 시간별 평균 상태</h3>
    <p class="sub">플레이어 상태의 data_json.time을 구간으로 묶은 평균
      <select id="tlmetric" style="padding:2px 6px">
        <option value="health">체력</option><option value="moduleCrates">남은 모듈 상자</option>
        <option value="materialCrates">남은 재료 상자</option><option value="tanks">탱크 수</option>
        <option value="helicopters">헬기 수</option>
      </select></p>
    <div id="c-timeline" class="chart"></div>
  </section>

  <section class="card wide">
    <h3>스테이지별 습격 통계</h3>
    <p class="sub">습격 구간 = 습격 시작 ~ 종료 이벤트. 블록·적 수는 습격당 평균, 종료 값은 종료 이벤트 기록의 평균입니다.</p>
    <div id="t-stages"></div>
  </section>

  <section class="card">
    <h3>종료 보상 모듈 ID</h3>
    <p class="sub">종료 이벤트 moduleCrateRewards · 버전 내 비율. 사망 후 실제 지급 여부는 알 수 없습니다.</p>
    <div id="c-rewards" class="chart"></div>
  </section>

  <section class="card">
    <h3>습격당 블록 파괴 · 적 처치</h3>
    <p class="sub">전체 스테이지 합계를 습격 수로 나눈 값 · 단위가 달라 항목마다 축이 따로입니다</p>
    <div id="c-perraid" class="chart"></div>
  </section>

  <section class="card wide">
    <div class="row" style="justify-content:space-between">
      <div><h3>플레이어 위치 히트맵</h3>
        <p class="sub" style="margin:0">플레이어 상태 position의 X·Z 밀도 · 동일 축척 · 버전 내 비율, 제곱근 스케일 · 위쪽이 +Z · 지형 데이터 없음</p></div>
      <div class="row"><span class="muted">스테이지</span><select id="stage"></select>
        <span class="muted">적음</span><span class="ramp"></span><span class="muted">많음</span></div>
    </div>
    <div id="heat" class="heatgrid" style="margin-top:10px"></div>
  </section>

  <section class="card wide">
    <details>
      <summary>집계 기준 — 게임이 남기는 로그 이벤트</summary>
<pre>버전은 세션의 appVersion(Application.version)입니다. 같은 버전의 모든 세션 로그를 합산합니다.

Shop      / weapon_sold      price, satisfied, tagCount       (초기 빌드: Shop / 총 판매. Unity / [Customer] 총 판매 … 는 중복 출력이므로 제외)
Shop      / workbench_entered · workbench_exited             (초기 빌드: Workshop / 작업대 진입 · 작업대 퇴장)
                             같은 씬의 시간순 진입→퇴장 쌍만 체류 시간으로 계산
Shop      / order_completed  success, customerDead           (주문 성공 비율 = success / 완료 주문. 판매 조건 만족과 다름)
NightRaid / 습격 시작        scene                            (씬 전환 전 Main에서 기록되므로 습격 씬은 data_json.scene)
NightRaid / 플레이어 상태    time, position{x,y,z}, cameraRotation, health,
                             remainingModuleCrates, remainingMaterialCrates, tanks, helicopters
NightRaid / 블록 파괴        stageId, time, blockId
NightRaid / 적 생성 · 적 발견 · 적 처치    time, enemyId      (enemyId는 개체 ID라 종류로 묶지 않음)
NightRaid / 습격 사망 · 습격 탈출 성공 · 습격 성공
                             scene, survived, time, duration, enemyKills, health,
                             moduleCrateRewards[], moduleCrateCredits, materialCrateCredits

timestamp_utc(실제 시각)와 data_json.time(게임 시간)은 섞지 않습니다. 습격 차트는 게임 시간 기준입니다.
모듈 상자 · 재료 상자는 별도로 표시하고 다른 이름(금고 등)으로 바꾸지 않습니다.</pre>
    </details>
  </section>
</main>
<div id="tip"></div>
<script>
"""
    + PageCommon.Script + """
</script>
<script>
"use strict";
const ENDPOINT = "/api/analytics";
const PREF_STORE = "logkit.analytics";

function renderKpi(vs) {
  const R = (v) => v.raids;
  const rows = [
    ["세션"],
    ["세션 수", (v) => num(v.sessions, 0)],
    ["기간 (로컬 시각)", (v) => toLocal(v.firstSessionUtc) + " ~ " + toLocal(v.lastSessionUtc).slice(5)],
    ["플레이 시간 합계", (v) => dur(v.playSeconds)],
    ["세션당 플레이 시간", (v) => dur(ratio(v.playSeconds, v.sessions))],
    ["정상 종료 비율", (v) => pct(ratio(v.cleanEnds, v.sessions))],
    ["상점"],
    ["판매 건수", (v) => num(v.sales, 0)],
    ["세션당 판매 건수", (v) => num(ratio(v.sales, v.sessions), 2)],
    ["판매액 합계", (v) => num(v.revenue, 0)],
    ["판매 1건 평균 판매액", (v) => num(ratio(v.revenue, v.pricedSales), 0)],
    ["세션당 판매액", (v) => num(ratio(v.revenue, v.sessions), 0)],
    ["판매 조건 만족 비율", (v) => pct(v.satisfiedRate)],
    ["판매 1건 평균 태그 수", (v) => num(avg(v.tagCounts), 2)],
    ["주문 완료 (order_completed)", (v) => num(v.ordersCompleted, 0)],
    ["주문 성공 비율", (v) => pct(ratio(v.ordersSucceeded, v.ordersCompleted))],
    ["고객 사망", (v) => num(v.customerDeaths, 0)],
    ["작업대 이용 (진입→퇴장 쌍)", (v) => num(v.workbenchVisits, 0)],
    ["평균 작업대 체류", (v) => dur(avg(v.workbenchSeconds))],
    ["짝 없는 진입·퇴장", (v) => num(v.unmatchedWorkbench, 0)],
    ["야간 습격"],
    ["습격 수", (v) => num(R(v).raids, 0)],
    ["세션당 습격 수", (v) => num(ratio(R(v).raids, v.sessions), 2)],
    ["종료 기록 / 시작 누락", (v) => num(R(v).ended, 0) + " / " + num(R(v).withoutStart, 0)],
    ["생존 비율", (v) => pct(R(v).survivedRate)],
    ["평균 소요 (게임 시간)", (v) => dur(R(v).avgDuration)],
    ["습격당 블록 파괴", (v) => num(ratio(R(v).blocks, R(v).raids), 1)],
    ["습격당 적 생성 / 발견 / 처치", (v) => [R(v).enemiesSpawned, R(v).enemiesSpotted, R(v).enemiesKilled].map((x) => num(ratio(x, R(v).raids), 2)).join(" / ")],
    ["종료 기록 처치 수 평균", (v) => num(R(v).avgEnemyKills, 2)],
    ["종료 체력 평균", (v) => num(R(v).avgEndHealth, 2)],
    ["모듈 / 재료 상자 크레딧 평균", (v) => num(R(v).avgModuleCrateCredits, 0) + " / " + num(R(v).avgMaterialCrateCredits, 0)],
  ];
  let h = '<div class="tablewrap" style="max-height:none"><table><thead><tr><th>지표</th>' +
    vs.map((v) => '<th><span class="sw" style="background:' + color(v) + '"></span>' + esc(v.version) + "</th>").join("") +
    "</tr></thead><tbody>";
  for (const [label, f] of rows) {
    if (!f) { h += '<tr class="group"><td colspan="' + (vs.length + 1) + '">' + esc(label) + "</td></tr>"; continue; }
    h += "<tr><td>" + esc(label) + "</td>" + vs.map((v) => {
      const t = f(v);
      return "<td" + (/^[–\s/]*$/.test(t) ? ' class="na"' : "") + ">" + esc(t) + "</td>";
    }).join("") + "</tr>";
  }
  $("kpi").innerHTML = h + "</tbody></table></div>";
}

function renderShop(vs) {
  let maxPrice = 0;
  vs.forEach((v) => v.prices.forEach((p) => { maxPrice = Math.max(maxPrice, p); }));
  const step = niceMax(maxPrice / 8 || 1);
  const bins = Math.max(1, Math.ceil((maxPrice + 1e-9) / step));
  const short = (x) => x >= 1000 ? num(x / 1000, 1) + "k" : num(x, 0);
  const priceCats = Array.from({ length: bins }, (_, i) => short(i * step) + "~");
  distribution($("c-price"), vs, (v) => v.prices, priceCats, (p) => Math.min(bins - 1, Math.floor(p / step)), "건");

  distribution($("c-tags"), vs, (v) => v.tagCounts, ["0", "1", "2", "3", "4+"], (t) => Math.min(4, Math.max(0, t)), "건");

  const edges = [5, 15, 30, 60];
  distribution($("c-workbench"), vs, (v) => v.workbenchSeconds, ["~5초", "5~15초", "15~30초", "30~60초", "60초~"],
    (s) => { const i = edges.findIndex((e) => s < e); return i < 0 ? edges.length : i; }, "회");
}

function renderRaid(vs) {
  const stages = [...new Set(vs.flatMap((v) => v.stages.map((s) => s.scene)))].sort();
  const stageOf = (v, scene) => v.stages.find((s) => s.scene === scene);
  hbars($("c-survival"), vs, stages, (v, scene) => {
    const s = stageOf(v, scene);
    if (!s || s.survivedRate == null) return null;
    return { value: s.survivedRate, label: pct(s.survivedRate, 0) + " · " + s.survived + "/" + s.ended,
      tip: "생존 " + pct(s.survivedRate) + " (" + s.survived + " / 종료 " + s.ended + "회)" };
  }, 1);

  const metric = $("tlmetric").value;
  const metricName = $("tlmetric").selectedOptions[0].textContent;
  lines($("c-timeline"), vs, (v) => v.timeline.map((t) => ({ x: t.sec, y: t[metric], n: t.samples })),
    (x) => Math.round(x) + "s", (y) => num(y, 2),
    (v, p) => v.version + " · 게임 " + p.x + "~" + (p.x + state.data.timelineBinSeconds) + "초\n" + metricName + " 평균 " + num(p.y, 2) + " (표본 " + num(p.n, 0) + ")");

  let h = '<div class="tablewrap"><table><thead><tr><th>스테이지</th><th>버전</th><th>습격</th><th>종료 기록</th><th>생존 비율</th>' +
    "<th>평균 소요</th><th>습격당 블록 파괴</th><th>습격당 적 생성</th><th>습격당 적 발견</th><th>습격당 적 처치</th>" +
    "<th>종료 체력</th><th>모듈 상자 크레딧</th><th>재료 상자 크레딧</th></tr></thead><tbody>";
  for (const scene of stages) {
    for (const v of vs) {
      const s = stageOf(v, scene);
      if (!s) continue;
      const per = (x) => num(ratio(x, s.raids), 2);
      h += "<tr><td>" + esc(scene) + '</td><td><span class="sw" style="background:' + color(v) + '"></span>' + esc(v.version) +
        "</td><td>" + num(s.raids, 0) + "</td><td>" + num(s.ended, 0) + "</td><td>" + pct(s.survivedRate) +
        "</td><td>" + dur(s.avgDuration) + "</td><td>" + per(s.blocks) + "</td><td>" + per(s.enemiesSpawned) +
        "</td><td>" + per(s.enemiesSpotted) + "</td><td>" + per(s.enemiesKilled) + "</td><td>" + num(s.avgEndHealth, 2) +
        "</td><td>" + num(s.avgModuleCrateCredits, 0) + "</td><td>" + num(s.avgMaterialCrateCredits, 0) + "</td></tr>";
    }
  }
  $("t-stages").innerHTML = stages.length ? h + "</tbody></table></div>" : emptyBox("습격 기록이 없습니다.");

  const rewardKeys = [];
  const rewardTotal = new Map();
  vs.forEach((v) => {
    const total = v.moduleRewards.reduce((a, k) => a + k.count, 0);
    rewardTotal.set(v.version, total);
    v.moduleRewards.forEach((k) => { if (!rewardKeys.includes(k.key)) rewardKeys.push(k.key); });
  });
  hbars($("c-rewards"), vs, rewardKeys.slice(0, 12), (v, key) => {
    const k = v.moduleRewards.find((x) => x.key === key);
    const total = rewardTotal.get(v.version);
    if (!k || !total) return null;
    return { value: k.count / total, label: pct(k.count / total, 0), tip: pct(k.count / total) + " (" + k.count + " / " + total + "건)" };
  });

  const per = [["습격당 블록 파괴", (r) => r.blocks], ["습격당 적 처치 (이벤트)", (r) => r.enemiesKilled]];
  hbars($("c-perraid"), vs, per.map((p) => p[0]), (v, key) => {
    const f = per.find((p) => p[0] === key)[1];
    const val = ratio(f(v.raids), v.raids.raids);
    if (val == null) return null;
    return { value: val, label: num(val, 2), tip: num(val, 2) + " (" + num(f(v.raids), 0) + " / 습격 " + v.raids.raids + "회)" };
  }, 0, true);

  renderHeat(vs);
}

function renderHeat(vs) {
  const stages = [...new Set(vs.flatMap((v) => v.heatmaps.map((h) => h.stageId)))].sort();
  const sel = $("stage");
  const prev = sel.value;
  sel.innerHTML = stages.map((s) => '<option value="' + esc(s) + '">' + esc(s) + "</option>").join("");
  if (stages.includes(prev)) sel.value = prev;
  const stage = sel.value;
  const box = $("heat");
  if (!stage) { box.innerHTML = emptyBox("플레이어 상태 위치 표본이 없습니다."); return; }
  const maps = vs.map((v) => ({ v, h: v.heatmaps.find((x) => x.stageId === stage) }));
  let maxShare = 0;
  maps.forEach(({ h }) => {
    if (!h) return;
    h._total = h.cells.reduce((a, b) => a + b, 0);
    h.cells.forEach((c) => { if (h._total) maxShare = Math.max(maxShare, c / h._total); });
  });
  box.innerHTML = "";
  for (const { v, h } of maps) {
    const wrap = document.createElement("div");
    wrap.innerHTML = '<div class="cap"><span class="sw" style="background:' + color(v) + '"></span>' + esc(v.version) +
      '<span class="muted" style="margin-left:6px">' + (h ? num(h._total, 0) + " 표본" : "표본 없음") + "</span></div>";
    if (h) {
      const cv = document.createElement("canvas");
      cv.width = h.grid; cv.height = h.grid;
      const ctx = cv.getContext("2d");
      const img = ctx.createImageData(h.grid, h.grid);
      for (let r = 0; r < h.grid; r++) {
        for (let c = 0; c < h.grid; c++) {
          const val = h.cells[r * h.grid + c];
          if (!val) continue;
          const rgb = ramp(Math.sqrt(val / h._total / maxShare));
          const i = ((h.grid - 1 - r) * h.grid + c) * 4;
          img.data[i] = rgb[0]; img.data[i + 1] = rgb[1]; img.data[i + 2] = rgb[2]; img.data[i + 3] = 255;
        }
      }
      ctx.putImageData(img, 0, 0);
      cv.title = "X " + num(h.minX) + " ~ " + num(h.maxX) + ", Z " + num(h.minZ) + " ~ " + num(h.maxZ);
      wrap.appendChild(cv);
    }
    box.appendChild(wrap);
  }
}

function render() {
  const vs = selectedVersions();
  const charts = ["c-price", "c-tags", "c-workbench", "c-survival", "c-timeline", "c-rewards", "c-perraid", "t-stages", "heat"];
  if (vs.length === 0) {
    $("kpi").innerHTML = emptyBox(state.data && state.data.versions.length ? "비교할 버전을 선택하세요." : "범위 안에 세션이 없습니다.");
    charts.forEach((id) => { $(id).innerHTML = ""; });
    return;
  }
  renderKpi(vs);
  renderShop(vs);
  renderRaid(vs);
}

$("tlmetric").onchange = render;
$("stage").onchange = () => renderHeat(selectedVersions());
initPage();
</script>
</body>
</html>
""";
}
