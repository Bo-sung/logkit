namespace LogServer;

/// <summary>
/// 버전별 총기 모딩 통계 페이지 HTML. /api/analytics/weapons의 구성별 합계(Configs)로 파츠 사용량, 파츠 조합 빈도,
/// 동시 사용, 빌드 크기, 인기 구성을 화면에서 계산해 그린다. 공통 스타일·스크립트는 PageCommon을 쓴다.
/// </summary>
public static class WeaponsPage
{
    /// <summary>총기 통계 페이지 전체 HTML.</summary>
    public const string Html = """
<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>LogKit 총기 통계</title>
<style>
"""
    + PageCommon.Css + """

.matrix { overflow: auto; }
.matrix svg { display: block; }
.matrix text { fill: var(--muted); font-size: 11px; }
.matrix text.lbl { fill: var(--text); }
.parts { color: var(--text); white-space: normal; text-align: left !important; min-width: 220px; }
.key { font-family: Consolas, monospace; color: var(--muted); font-size: 11.5px; }
</style>
</head>
<body>
<header>
  <div class="brand">LOGKIT<span>WEAPON STATS</span></div>
  <nav class="pages">
    <a href="/dashboard">GM 툴(세션별 로그)</a>
    <a href="/analytics">버전 통계</a>
    <b>총기 통계</b>
    <a href="/insights">가설 통계</a>
    <a href="/replay">습격 리플레이</a>
  </nav>
  <div id="status" class="status"></div>
  <span class="spacer"></span>
  <select id="month"></select>
  <label class="chk"><input type="checkbox" id="noeditor"> 에디터 세션 제외</label>
  <input id="key" type="password" placeholder="API 키" style="width:150px">
  <button id="load">불러오기</button>
  <button id="export" title="지금 화면의 데이터를 넣은 HTML 파일로 저장(서버 없이 열림)">HTML 저장</button>
</header>
<main>
  <section class="card wide">
    <div class="row" style="margin-bottom:6px">
      <b>비교할 버전</b><span class="muted">(앱 버전별로 모든 세션 로그를 합산 · 최대 8개 · 클릭해 켜고 끔)</span>
    </div>
    <div id="vchips" class="row"></div>
    <div class="row" style="margin-top:10px">
      <b>집계 대상</b>
      <select id="basis">
        <option value="builds">제작한 총 (빌드)</option>
        <option value="sold">판매된 총</option>
        <option value="raidLoadouts">습격에 들고 간 총 (습격 수)</option>
        <option value="raidShots">습격 중 실제 발사</option>
        <option value="shopShots">공방 실제 발사</option>
        <option value="attackInputs">시험 사격 입력 (발사 입력)</option>
      </select>
      <label class="chk"><input type="checkbox" id="notutorial" checked> 튜토리얼 기본 구성 제외</label>
      <label class="chk"><input type="checkbox" id="nobase"> 조합에서 기본 블럭 제외</label>
    </div>
    <p class="sub" id="basis-sub" style="margin:6px 0 0"></p>
  </section>

  <section class="card wide">
    <h3>버전별 총기 지표</h3>
    <p class="sub">집계 대상·튜토리얼 선택과 무관한 전체 값입니다. 판매 코드·습격 휴대 총·실제 발사는 이 기록을 넣은 게임 빌드부터 쌓이므로 이전 버전은 0입니다.</p>
    <div id="kpi"></div>
  </section>

  <div class="section">파츠별 사용량</div>

  <section class="card wide">
    <div class="row" style="justify-content:space-between">
      <div><h3>파츠별 사용량<span class="unitsw" data-unit="parts"></span></h3>
        <p class="sub" id="parts-sub" style="margin:0"></p></div>
      <select id="partmetric">
        <option value="adoption">채택률 (그 파츠를 넣은 총 비율)</option>
        <option value="share">블록 점유율 (전체 파츠 블록 중 비율)</option>
        <option value="perGun">넣었을 때 평균 개수</option>
      </select>
    </div>
    <div id="c-parts" class="chart" style="margin-top:8px"></div>
  </section>

  <section class="card wide">
    <h3>파츠별 상세</h3>
    <p class="sub">판매 평균가는 그 파츠가 들어간 판매 총의 평균이며, 괄호는 같은 버전 전체 판매 평균과의 차이입니다. 표본이 적으면 차이를 해석하지 마세요.</p>
    <div id="t-parts"></div>
  </section>

  <div class="section">파츠 조합</div>

  <section class="card">
    <h3>파츠 조합 빈도<span class="unitsw" data-unit="combos"></span></h3>
    <p class="sub">개수·배치와 무관하게 들어간 파츠 종류의 집합 · 버전 내 비율 · 상위 15개</p>
    <div id="c-combos" class="chart"></div>
  </section>

  <section class="card">
    <div class="row" style="justify-content:space-between">
      <div><h3>파츠 동시 사용</h3>
        <p class="sub" style="margin:0">두 파츠를 함께 넣은 총의 비율 · 대각선은 채택률</p></div>
      <div class="row"><span class="muted">버전</span><select id="mver"></select></div>
    </div>
    <div class="row" style="margin:6px 0"><span class="muted">적음</span><span class="ramp"></span><span class="muted">많음</span></div>
    <div id="c-matrix" class="matrix"></div>
  </section>

  <section class="card">
    <h3>총 크기 분포<span class="unitsw" data-unit="size"></span></h3>
    <p class="sub">코어를 뺀 파츠 블록 수 · 버전 내 비율</p>
    <div id="c-size" class="chart"></div>
  </section>

  <section class="card">
    <h3>기록 한계</h3>
    <p class="sub">추정값을 표시하지 않습니다.</p>
    <div class="missing">
      <div><b>이전 빌드의 판매 총</b>판매 코드가 없는 로그는 같은 주문의 마지막 작업대 퇴장으로 추정 — 코어 지급기·바닥 총 교환이 있으면 틀릴 수 있음</div>
      <div><b>구성 미확인 발사</b>발사에는 구성 ID만 있어, 그 세션에서 코드를 본 적 없는 총의 발사는 파츠를 모름</div>
      <div><b>과거 판매 복원</b>코드 없이 끝난 판매는 소급 복원할 수 없음</div>
      <div><b>시험 사격 입력</b>attack_input은 누를 때 1회 기록되는 발사 입력이며 발사 성공이 아님</div>
    </div>
  </section>

  <div class="section">습격 중 사용</div>

  <section class="card wide">
    <div class="row" style="justify-content:space-between">
      <div><h3>스테이지별 파츠 사용 (습격 발사 기준)</h3>
        <p class="sub" style="margin:0">그 파츠가 들어간 총으로 쏜 발사 / 그 스테이지의 구성 확인 발사 · 튜토리얼 선택 반영</p></div>
      <div class="row"><span class="muted">버전</span><select id="sver"></select></div>
    </div>
    <div id="t-stage" style="margin-top:8px"></div>
  </section>

  <section class="card wide">
    <div class="row" style="justify-content:space-between">
      <div><h3>습격 발사 위치 히트맵</h3>
        <p class="sub" style="margin:0">NightRaid / weapon_fired의 position X·Z 밀도 · 동일 축척 · 버전 내 비율, 제곱근 스케일 · 위쪽이 +Z · 모든 총 합계</p></div>
      <div class="row"><span class="muted">스테이지</span><select id="stage"></select>
        <span class="muted">적음</span><span class="ramp pitch"></span><span class="muted">많음</span></div>
    </div>
    <div id="heat" class="heatgrid" style="margin-top:10px"></div>
  </section>

  <section class="card wide">
    <h3>많이 만든 구성</h3>
    <p class="sub">모듈·위치·방향까지 같은 구성(이름·색 제외)을 하나로 묶은 상위 15개 · 선택한 버전 합계 기준 정렬</p>
    <div id="t-configs"></div>
  </section>

  <section class="card wide">
    <details>
      <summary>집계 기준 — 게임이 남기는 로그 이벤트</summary>
<pre>Shop      / workbench_exited    weaponCode, orderId                  작업대에서 나올 때 손에 든 총
Shop      / weapon_sold         weaponCode, price, satisfied, orderId 실제 판매된 총 (초기 빌드의 Shop / 총 판매도 판매로 셈)
Shop      / attack_input        weaponCode, orderId                  공방(주간) 발사 입력 — 누를 때 1회, 발사 성공 아님
Shop      / weapon_fired        weaponAssemblyId, position, time     공방(주간) 실제 발사
NightRaid / 습격 시작           scene, weaponCode, weaponAssemblyId  습격에 들고 간 총
NightRaid / raid_weapon_config  weaponCode, weaponAssemblyId         습격 중 처음 쓰는 구성 (발사 구성 ID를 파츠로 풀 때 사용)
NightRaid / weapon_fired        weaponAssemblyId, position, time     습격 중 실제 발사

weaponCode는 게임의 무기 공유 코드(WSC:…)입니다. 서버가 무결성 검사 후 모듈 GUID·셀 좌표·방향·색 여부를 풀어 씁니다.
무기 이름은 복호화하지 않고 기본 이름인지만 봅니다. 코어 블록은 코드에 들어가지 않아 파츠 수에서 빠집니다.
모듈 이름은 게임 저장소 Assets/Data/Modules의 displayName입니다. 모르는 모듈은 "모듈 + GUID 앞 8자리"로 표시합니다.

빌드      작업대 퇴장 1회 = 빌드 1개. 같은 orderId에서 직전 퇴장과 구성이 같으면 빌드로 세지 않음
판매된 총 weapon_sold의 코드. 코드가 없는 이전 빌드 로그는 같은 orderId의 마지막 빌드로 추정
구성      모듈·셀·출력 방향·법선이 모두 같으면 같은 구성 (이름·색 제외). 서버가 코드에서 게임 GetAssemblyKey와
          같은 구성 ID를 계산해, 코드 없이 구성 ID만 있는 발사도 같은 구성으로 묶음
튜토리얼  코어 좌·우·위·아래·뒤 다섯 면에 기본 블럭을 붙인 Day0 튜토리얼 구성. 기본으로 통계에서 뺌
조합      구성에 들어간 파츠 종류의 집합 (개수·배치 무시)</pre>
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
const ENDPOINT = "/api/analytics/weapons";
const PREF_STORE = "logkit.weapons";
const BASE_BLOCK = "기본 블럭";
const TUTORIAL = "튜토리얼 기본 구성";
const TOP = 15;
// 집계 대상: 구성(config)마다 어느 수를 가중치로 쓸지. unit은 한 건을 부르는 말이다.
const BASES = {
  builds: { field: "builds", name: "빌드", unit: "정", desc: "빌드 = 작업대에서 나올 때의 총 (같은 주문에서 바꾸지 않고 다시 나온 경우 제외)" },
  sold: { field: "sold", name: "판매된 총", unit: "정", desc: "판매 기록의 코드(이전 빌드는 같은 주문의 마지막 빌드로 추정)" },
  raidLoadouts: { field: "raidLoadouts", name: "습격 휴대", unit: "회", desc: "습격 시작 때 들고 있던 총 · 습격 1회 = 1" },
  raidShots: { field: "raidShots", name: "습격 발사", unit: "발", desc: "습격 중 실제 발사 · 발사 1회 = 1 · 그 세션에서 코드를 본 적 없는 총의 발사는 빠짐" },
  shopShots: { field: "shopShots", name: "공방 발사", unit: "발", desc: "공방(주간) 실제 발사 · 발사 1회 = 1 · 그 세션에서 코드를 본 적 없는 총의 발사는 빠짐" },
  attackInputs: { field: "attackInputs", name: "시험 사격 입력", unit: "회", desc: "공방(주간) 발사 입력 · 누를 때 1회 · 발사 성공 아님" },
};
state.basis = BASES[loadPrefs().basis] ? loadPrefs().basis : "builds";

const basis = () => BASES[state.basis];
const basisName = () => basis().name;
const skipTutorial = () => $("notutorial").checked;
/** 집계에 넣는 구성: 파츠를 알고, 튜토리얼 제외를 켰으면 튜토리얼 구성이 아닌 것. */
const counted = (c) => c.known && !(skipTutorial() && c.tutorial);
const raw = (c) => c[basis().field] || 0;
const weight = (c) => counted(c) ? raw(c) : 0;
const total = (v) => v.configs.reduce((a, c) => a + weight(c), 0);

/** 버전 하나의 파츠별 합계. 집계 대상 가중치로 세고, 판매·발사 수는 대상과 무관하게 함께 센다. */
function partStats(v) {
  const map = new Map();
  for (const c of v.configs) {
    if (!counted(c)) continue;
    const w = raw(c);
    for (const [name, count] of Object.entries(c.parts)) {
      if (!map.has(name)) {
        map.set(name, { guns: 0, blocks: 0, sold: 0, priceSum: 0, priced: 0, satisfied: 0, satisfiedKnown: 0, raidLoadouts: 0, raidShots: 0, shopShots: 0 });
      }
      const p = map.get(name);
      p.guns += w; p.blocks += w * count;
      p.sold += c.sold; p.priceSum += c.priceSum; p.priced += c.priced;
      p.satisfied += c.satisfied; p.satisfiedKnown += c.satisfiedKnown;
      p.raidLoadouts += c.raidLoadouts; p.raidShots += c.raidShots; p.shopShots += c.shopShots;
    }
  }
  return map;
}
function versionSales(v) {
  let sum = 0, n = 0;
  v.configs.forEach((c) => { if (counted(c)) { sum += c.priceSum; n += c.priced; } });
  return n ? sum / n : null;
}
function cache(vs) {
  vs.forEach((v) => {
    v._parts = partStats(v);
    v._total = total(v);
    v._blocks = v.configs.reduce((a, c) => a + weight(c) * c.blocks, 0);
    // 대상 수 중 집계에서 빠진 몫: 튜토리얼 구성, 구성 미확인(파츠 모름)
    v._tutorial = v.configs.reduce((a, c) => a + (c.known && c.tutorial ? raw(c) : 0), 0);
    v._unknown = v.configs.reduce((a, c) => a + (c.known ? 0 : raw(c)), 0);
  });
}
/** 선택한 버전 전체에서 많이 쓰인 순서의 파츠 이름. */
function partOrder(vs) {
  const score = new Map();
  vs.forEach((v) => v._parts.forEach((p, name) => {
    score.set(name, (score.get(name) || 0) + (v._total ? p.guns / v._total : 0) + (p.raidShots + p.shopShots + p.sold ? 1e-6 : 0));
  }));
  return [...score.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])).map((e) => e[0]);
}

function renderBasisNote(vs) {
  const parts = vs.map((v) => {
    const extra = [];
    if (skipTutorial() && v._tutorial) extra.push("튜토리얼 " + num(v._tutorial, 0));
    if (v._unknown) extra.push("구성 미확인 " + num(v._unknown, 0));
    return v.version + " " + num(v._total, 0) + basis().unit + (extra.length ? " (제외: " + extra.join(", ") + ")" : "");
  });
  $("basis-sub").textContent = basis().desc + " · 집계 " + parts.join(" · ");
}

function renderKpi(vs) {
  const avgBlocks = (v) => ratio(v.configs.reduce((a, c) => a + c.builds * c.blocks, 0), v.builds);
  const median = (v) => {
    const list = [];
    v.configs.forEach((c) => { for (let i = 0; i < c.builds; i++) list.push(c.blocks); });
    if (!list.length) return null;
    list.sort((a, b) => a - b);
    return list.length % 2 ? list[(list.length - 1) / 2] : (list[list.length / 2 - 1] + list[list.length / 2]) / 2;
  };
  const builds = (v, f) => v.configs.filter(f).reduce((a, c) => a + c.builds, 0);
  const distinct = (v) => v.configs.filter((c) => c.builds > 0).length;
  const priced = (v) => {
    let sum = 0, n = 0;
    v.configs.forEach((c) => { sum += c.priceSum; n += c.priced; });
    return n ? sum / n : null;
  };
  const rows = [
    ["제작"],
    ["세션 수 / 빌드가 있는 세션", (v) => num(v.sessions, 0) + " / " + num(v.sessionsWithBuilds, 0)],
    ["작업대 퇴장 기록", (v) => num(v.workbenchExits, 0)],
    ["빌드 수", (v) => num(v.builds, 0)],
    ["튜토리얼 기본 구성 빌드", (v) => num(builds(v, (c) => c.tutorial), 0)],
    ["구성 변경 없는 재퇴장", (v) => num(v.repeatExits, 0)],
    ["세션당 빌드 (빌드가 있는 세션)", (v) => num(ratio(v.builds, v.sessionsWithBuilds), 2)],
    ["서로 다른 구성 수", (v) => num(distinct(v), 0)],
    ["빌드당 파츠 블록 평균 / 중앙값", (v) => num(avgBlocks(v), 1) + " / " + num(median(v), 1)],
    ["코어만 있는 빌드", (v) => pct(ratio(builds(v, (c) => c.known && c.blocks === 0), v.builds))],
    ["이름 지정 빌드", (v) => pct(ratio(v.customNamedBuilds, v.builds))],
    ["색 지정 빌드", (v) => pct(ratio(v.coloredBuilds, v.builds))],
    ["판매"],
    ["판매 수", (v) => num(v.sales, 0)],
    ["구성 확인: 판매 코드 / 주문으로 추정", (v) => num(v.salesWithCode, 0) + " / " + num(v.salesInferred, 0) + " (" + pct(ratio(v.salesLinked, v.sales), 0) + ")"],
    ["구성 확인 판매 평균가", (v) => num(priced(v), 0)],
    ["발사"],
    ["공방 실제 발사 (구성 ID 없음)", (v) => num(v.shopShots, 0) + " (" + num(v.shopShotsWithoutId, 0) + ")"],
    ["시험 사격 입력", (v) => num(v.attackInputs, 0)],
    ["습격 수 / 휴대 총 기록된 습격", (v) => num(v.raids, 0) + " / " + num(v.raidsWithLoadout, 0)],
    ["습격 중 새 구성 사용", (v) => num(v.raidWeaponChanges, 0)],
    ["습격 실제 발사 (구성 ID 없음)", (v) => num(v.raidShots, 0) + " (" + num(v.raidShotsWithoutId, 0) + ")"],
    ["습격당 발사", (v) => num(ratio(v.raidShots, v.raidsWithLoadout), 1)],
    ["기록 품질"],
    ["코드 미생성 퇴장 (게임 측 실패)", (v) => num(v.exportFailed, 0)],
    ["코드 해석 실패 (퇴장 / 사격 입력)", (v) => num(v.decodeFailed, 0) + " / " + num(v.attackInputs - v.attackInputsDecoded, 0)],
  ];
  let h = '<div class="tablewrap" style="max-height:none"><table><thead><tr><th>지표</th>' +
    vs.map((v) => '<th><span class="sw" style="background:' + color(v) + '"></span>' + esc(v.version) + "</th>").join("") +
    "</tr></thead><tbody>";
  for (const [label, f] of rows) {
    if (!f) { h += '<tr class="group"><td colspan="' + (vs.length + 1) + '">' + esc(label) + "</td></tr>"; continue; }
    h += "<tr><td>" + esc(label) + "</td>" + vs.map((v) => {
      const t = f(v);
      return "<td" + (/^[–\s/()%]*$/.test(t) ? ' class="na"' : "") + ">" + esc(t) + "</td>";
    }).join("") + "</tr>";
  }
  $("kpi").innerHTML = h + "</tbody></table></div>";
}

function renderParts(vs) {
  const metric = $("partmetric").value;
  const order = partOrder(vs);
  const n = basisName();
  const subs = {
    adoption: "그 파츠가 들어간 총의 " + n + " / 전체 " + n,
    share: "그 파츠 블록 수 / 전체 파츠 블록 수 (" + n + " 가중, 코어 제외)",
    perGun: "그 파츠가 들어간 총에 평균 몇 개가 있는지 (" + n + " 가중) · 항목마다 축이 따로입니다",
  };
  $("parts-sub").textContent = subs[metric];
  hbars($("c-parts"), vs, order, (v, name) => {
    const p = v._parts.get(name);
    if (metric === "adoption") {
      if (!v._total) return null;
      return shareBar("parts", p ? p.guns : 0, v._total, basis().unit);
    }
    if (metric === "share") {
      if (!v._blocks) return null;
      return shareBar("parts", p ? p.blocks : 0, v._blocks, " 블록", 1);
    }
    if (!p || !p.guns) return null;
    const r = p.blocks / p.guns;
    return { value: r, label: num(r, 2) + "개", tip: "평균 " + num(r, 2) + "개 (" + num(p.blocks, 0) + " 블록 / " + num(p.guns, 0) + basis().unit + ")" };
  }, metric === "adoption" && !isCount("parts") ? 1 : 0, metric === "perGun");

  let h = '<div class="tablewrap"><table><thead><tr><th>파츠</th><th>버전</th><th>' + esc(n) + "</th><th>비율</th>" +
    "<th>블록 점유율</th><th>넣었을 때 평균 개수</th><th>판매 총</th><th>판매 평균가 (전체 대비)</th>" +
    "<th>판매 조건 만족</th><th>습격 휴대</th><th>습격 발사</th><th>공방 발사</th></tr></thead><tbody>";
  for (const name of order) {
    for (const v of vs) {
      const p = v._parts.get(name);
      if (!p) continue;
      const avgPrice = p.priced ? p.priceSum / p.priced : null;
      const base = versionSales(v);
      const diff = avgPrice != null && base != null ? avgPrice - base : null;
      h += "<tr><td>" + esc(name) + '</td><td><span class="sw" style="background:' + color(v) + '"></span>' + esc(v.version) +
        "</td><td>" + num(p.guns, 0) + "</td><td>" + pct(ratio(p.guns, v._total)) +
        "</td><td>" + pct(ratio(p.blocks, v._blocks)) + "</td><td>" + num(ratio(p.blocks, p.guns), 2) +
        "</td><td>" + num(p.sold, 0) + "</td><td" + (avgPrice == null ? ' class="na"' : "") + ">" + num(avgPrice, 0) +
        (diff == null ? "" : " (" + (diff >= 0 ? "+" : "") + num(diff, 0) + ")") +
        "</td><td>" + pct(ratio(p.satisfied, p.satisfiedKnown), 0) + "</td><td>" + num(p.raidLoadouts, 0) +
        "</td><td>" + num(p.raidShots, 0) + "</td><td>" + num(p.shopShots, 0) + "</td></tr>";
    }
  }
  $("t-parts").innerHTML = order.length ? h + "</tbody></table></div>" : emptyBox("파츠 기록이 없습니다.");
}

function comboName(c) {
  if (c.tutorial) return TUTORIAL;
  const names = Object.keys(c.parts).filter((n) => !$("nobase").checked || n !== BASE_BLOCK).sort((a, b) => a.localeCompare(b));
  if (names.length) return names.join(" + ");
  return c.blocks === 0 ? "코어만" : BASE_BLOCK + "만";
}

function renderCombos(vs) {
  const perVersion = new Map();
  const score = new Map();
  vs.forEach((v) => {
    const m = new Map();
    v.configs.forEach((c) => { const w = weight(c); if (w) { const k = comboName(c); m.set(k, (m.get(k) || 0) + w); } });
    perVersion.set(v.version, m);
    m.forEach((n, k) => score.set(k, (score.get(k) || 0) + (v._total ? n / v._total : 0)));
  });
  const keys = [...score.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])).slice(0, TOP).map((e) => e[0]);
  hbars($("c-combos"), vs, keys, (v, key) => {
    if (!v._total) return null;
    const n = perVersion.get(v.version).get(key) || 0;
    return shareBar("combos", n, v._total, basis().unit);
  }, 0, false, 230);
}

/** 버전 선택 상자를 선택한 버전으로 채우고 고른 버전을 돌려준다. */
function pickVersion(sel, vs, prefKey) {
  const prev = sel.value || loadPrefs()[prefKey];
  sel.innerHTML = vs.map((v) => '<option value="' + esc(v.version) + '">' + esc(v.version) + "</option>").join("");
  if (vs.some((v) => v.version === prev)) sel.value = prev;
  return vs.find((x) => x.version === sel.value);
}

function renderMatrix(vs) {
  const v = pickVersion($("mver"), vs, "matrixVersion");
  const box = $("c-matrix");
  if (!v || !v._total) { box.innerHTML = emptyBox("기록된 데이터가 없습니다."); return; }
  const names = partOrder([v]).filter((n) => (v._parts.get(n) || {}).guns > 0);
  if (names.length === 0) { box.innerHTML = emptyBox("파츠가 들어간 총이 없습니다."); return; }
  const idx = new Map(names.map((n, i) => [n, i]));
  const both = names.map(() => new Array(names.length).fill(0));
  v.configs.forEach((c) => {
    const w = weight(c);
    if (!w) return;
    const ids = Object.keys(c.parts).filter((n) => idx.has(n)).map((n) => idx.get(n));
    ids.forEach((i) => ids.forEach((j) => { both[i][j] += w; }));
  });
  const cell = 26, gap = 2, labelW = 84, top = 76;
  const W = labelW + names.length * cell, H = top + names.length * cell;
  let max = 0;
  names.forEach((_, i) => names.forEach((_, j) => { max = Math.max(max, both[i][j] / v._total); }));
  let s = "";
  names.forEach((n, i) => {
    s += '<text class="lbl" x="' + (labelW - 6) + '" y="' + (top + i * cell + cell / 2 + 4) + '" text-anchor="end">' + esc(n) + "</text>";
    const x = labelW + i * cell + cell / 2;
    s += '<text class="lbl" transform="translate(' + x + "," + (top - 6) + ') rotate(-55)">' + esc(n) + "</text>";
  });
  names.forEach((a, i) => names.forEach((b, j) => {
    const n = both[i][j];
    const r = n / v._total;
    const x = labelW + j * cell + gap / 2, y = top + i * cell + gap / 2, size = cell - gap;
    const pa = v._parts.get(a).guns / v._total, pb = v._parts.get(b).guns / v._total;
    const lift = i !== j && pa > 0 && pb > 0 ? r / (pa * pb) : null;
    const tipText = i === j
      ? a + " 비율 " + pct(r) + " (" + num(n, 0) + " / " + num(v._total, 0) + basis().unit + ")"
      : a + " + " + b + "\n함께 들어간 비율 " + pct(r) + " (" + num(n, 0) + " / " + num(v._total, 0) + basis().unit + ")" +
        "\n" + a + "가 들어간 것 중 " + pct(ratio(n, v._parts.get(a).guns)) + " · 독립 가정 대비 " + num(lift, 2) + "배";
    s += '<rect x="' + x + '" y="' + y + '" width="' + size + '" height="' + size + '" rx="3" fill="' +
      (n ? rgb(Math.sqrt(r / (max || 1))) : "#0d1117") + '" stroke="' + (i === j ? "var(--muted)" : "none") +
      '" data-tip="' + esc(tipText) + '"/>';
  }));
  box.innerHTML = '<svg width="' + W + '" height="' + H + '" viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

function renderSize(vs) {
  const edges = [1, 3, 6, 11, 21, 41];
  const cats = ["0 (코어만)", "1~2", "3~5", "6~10", "11~20", "21~40", "41+"];
  distribution($("c-size"), vs, (v) => {
    const list = [];
    v.configs.forEach((c) => { for (let i = 0; i < weight(c); i++) list.push(c.blocks); });
    return list;
  }, cats, (n) => { const i = edges.findIndex((e) => n < e); return i < 0 ? edges.length : i; }, basis().unit, "size");
}

function renderConfigs(vs) {
  const merged = new Map();
  vs.forEach((v) => v.configs.forEach((c) => {
    if (!merged.has(c.key)) merged.set(c.key, { c, score: 0, by: new Map(), sessions: 0, sold: 0, priceSum: 0, priced: 0, loadouts: 0, raidShots: 0, shopShots: 0 });
    const m = merged.get(c.key);
    m.score += v._total ? weight(c) / v._total : 0;
    m.by.set(v.version, weight(c));
    m.sessions += c.sessions; m.sold += c.sold; m.priceSum += c.priceSum; m.priced += c.priced;
    m.loadouts += c.raidLoadouts; m.raidShots += c.raidShots; m.shopShots += c.shopShots;
  }));
  const top = [...merged.values()].filter((m) => m.score > 0).sort((a, b) => b.score - a.score).slice(0, TOP);
  if (top.length === 0) { $("t-configs").innerHTML = emptyBox("구성 기록이 없습니다."); return; }
  const partsText = (c) => {
    const e = Object.entries(c.parts).sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));
    const text = e.length ? e.map(([n, k]) => n + " ×" + k).join(", ") : "코어만";
    return c.tutorial ? TUTORIAL + " · " + text : text;
  };
  let h = '<div class="tablewrap"><table><thead><tr><th>구성 ID</th><th style="text-align:left">파츠 (개수)</th><th>블록</th>' +
    vs.map((v) => '<th><span class="sw" style="background:' + color(v) + '"></span>' + esc(v.version) + " " + esc(basisName()) + "</th>").join("") +
    "<th>만든 세션</th><th>판매</th><th>평균 판매가</th><th>습격 휴대</th><th>습격 발사</th><th>공방 발사</th></tr></thead><tbody>";
  for (const m of top) {
    h += '<tr><td class="key" title="' + esc(m.c.key) + '">' + esc(m.c.key.slice(0, 10)) + '</td><td class="parts">' + esc(partsText(m.c)) +
      "</td><td>" + num(m.c.blocks, 0) + "</td>" +
      vs.map((v) => {
        const n = m.by.get(v.version) || 0;
        return n ? "<td>" + num(n, 0) + ' <span class="muted">' + pct(ratio(n, v._total), 0) + "</span></td>" : '<td class="na">–</td>';
      }).join("") +
      "<td>" + num(m.sessions, 0) + "</td><td>" + num(m.sold, 0) + "</td><td" + (m.priced ? "" : ' class="na"') + ">" +
      num(ratio(m.priceSum, m.priced), 0) + "</td><td>" + num(m.loadouts, 0) + "</td><td>" + num(m.raidShots, 0) +
      "</td><td>" + num(m.shopShots, 0) + "</td></tr>";
  }
  $("t-configs").innerHTML = h + "</tbody></table></div>";
}

/** 스테이지 × 파츠: 그 스테이지의 구성 확인 습격 발사 중 그 파츠가 들어간 총의 몫. 집계 대상 선택과 무관하다. */
function renderStages(vs) {
  const v = pickVersion($("sver"), vs, "stageVersion");
  const box = $("t-stage");
  if (!v) { box.innerHTML = ""; return; }
  const stages = [...new Set(v.configs.flatMap((c) => Object.keys(c.stageShots || {})))].sort();
  if (stages.length === 0) { box.innerHTML = emptyBox("습격 발사 기록이 없습니다. 발사 기록을 넣은 게임 빌드부터 쌓입니다."); return; }
  const shots = new Map(stages.map((s) => [s, 0]));
  const excluded = new Map(stages.map((s) => [s, 0]));
  const byPart = new Map();
  v.configs.forEach((c) => {
    for (const [stage, n] of Object.entries(c.stageShots || {})) {
      if (!counted(c)) { excluded.set(stage, excluded.get(stage) + n); continue; }
      shots.set(stage, shots.get(stage) + n);
      for (const name of Object.keys(c.parts)) {
        if (!byPart.has(name)) byPart.set(name, new Map());
        const m = byPart.get(name);
        m.set(stage, (m.get(stage) || 0) + n);
      }
    }
  });
  const parts = [...byPart.keys()].sort((a, b) =>
    stages.reduce((s, st) => s + ((byPart.get(b).get(st) || 0) / (shots.get(st) || 1)), 0) -
    stages.reduce((s, st) => s + ((byPart.get(a).get(st) || 0) / (shots.get(st) || 1)), 0) || a.localeCompare(b));
  let h = '<div class="tablewrap"><table><thead><tr><th>파츠</th>' + stages.map((s) => "<th>" + esc(s) + "</th>").join("") + "</tr></thead><tbody>";
  h += '<tr class="group"><td>구성 확인 발사 (제외)</td>' + stages.map((s) =>
    "<td>" + num(shots.get(s), 0) + " (" + num(excluded.get(s), 0) + ")</td>").join("") + "</tr>";
  for (const name of parts) {
    h += "<tr><td>" + esc(name) + "</td>" + stages.map((s) => {
      const n = byPart.get(name).get(s) || 0;
      const r = ratio(n, shots.get(s));
      if (r == null) return '<td class="na">–</td>';
      const bg = n ? "background:" + rgb(Math.sqrt(r)) + ";color:" + (r > 0.45 ? "#0d1117" : "var(--text)") : "";
      return '<td style="' + bg + '" data-tip="' + esc(v.version + " · " + s + " · " + name + "\n" + pct(r) + " (" + num(n, 0) + " / " + num(shots.get(s), 0) + "발)") + '">' + pct(r, 0) + "</td>";
    }).join("") + "</tr>";
  }
  box.innerHTML = h + "</tbody></table></div>";
}

function renderHeat(vs) {
  const stages = [...new Set(vs.flatMap((v) => v.shotHeatmaps.map((h) => h.stageId)))].sort();
  const sel = $("stage");
  const prev = sel.value;
  sel.innerHTML = stages.map((s) => '<option value="' + esc(s) + '">' + esc(s) + "</option>").join("");
  if (stages.includes(prev)) sel.value = prev;
  const stage = sel.value;
  const box = $("heat");
  if (!stage) { box.innerHTML = emptyBox("습격 발사 위치 기록이 없습니다. 발사 기록을 넣은 게임 빌드부터 쌓입니다."); return; }
  const maps = vs.map((v) => ({ v, h: v.shotHeatmaps.find((x) => x.stageId === stage) }));
  let maxShare = 0;
  maps.forEach(({ h }) => {
    if (!h) return;
    h._total = h.cells.reduce((a, b) => a + b, 0);
    h.cells.forEach((c) => { if (h._total) maxShare = Math.max(maxShare, c / h._total); });
  });
  pitchHeats(maps.map((m) => m.h), maxShare);
  box.innerHTML = "";
  for (const { v, h } of maps) {
    const wrap = document.createElement("div");
    wrap.innerHTML = '<div class="cap"><span class="sw" style="background:' + color(v) + '"></span>' + esc(v.version) +
      '<span class="muted" style="margin-left:6px">' + (h ? num(h._total, 0) + "발" : "기록 없음") + "</span></div>";
    if (h) wrap.appendChild(h._canvas);
    box.appendChild(wrap);
  }
}

function render() {
  const vs = selectedVersions();
  const charts = ["c-parts", "t-parts", "c-combos", "c-matrix", "c-size", "t-configs", "t-stage", "heat"];
  if (vs.length === 0) {
    $("kpi").innerHTML = emptyBox(state.data && state.data.versions.length ? "비교할 버전을 선택하세요." : "범위 안에 세션이 없습니다.");
    $("basis-sub").textContent = "";
    charts.forEach((id) => { $(id).innerHTML = ""; });
    return;
  }
  cache(vs);
  renderBasisNote(vs);
  renderKpi(vs);
  renderParts(vs);
  renderCombos(vs);
  renderMatrix(vs);
  renderSize(vs);
  renderConfigs(vs);
  renderStages(vs);
  renderHeat(vs);
}

$("basis").value = state.basis;
$("basis").onchange = () => { state.basis = $("basis").value; savePrefs({ basis: state.basis }); render(); };
if (loadPrefs().noTutorial === false) $("notutorial").checked = false;
$("notutorial").onchange = () => { savePrefs({ noTutorial: $("notutorial").checked }); render(); };
$("nobase").checked = !!loadPrefs().noBase;
$("nobase").onchange = () => { savePrefs({ noBase: $("nobase").checked }); render(); };
$("partmetric").value = loadPrefs().partMetric || "adoption";
if (!$("partmetric").value) $("partmetric").value = "adoption";
$("partmetric").onchange = () => { savePrefs({ partMetric: $("partmetric").value }); render(); };
$("mver").onchange = () => { savePrefs({ matrixVersion: $("mver").value }); renderMatrix(selectedVersions()); };
$("sver").onchange = () => { savePrefs({ stageVersion: $("sver").value }); renderStages(selectedVersions()); };
$("stage").onchange = () => renderHeat(selectedVersions());
initPage();
</script>
</body>
</html>
""";
}
