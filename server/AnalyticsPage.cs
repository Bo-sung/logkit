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
:root {
  --bg: #0d1117; --panel: #161b22; --border: #30363d; --text: #e6edf3;
  --muted: #8b949e; --accent: #58a6ff; --err: #f85149;
  --grid: #2c2c2a; --axis: #383835;
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--text);
  font: 13px/1.5 "Segoe UI", "Malgun Gothic", sans-serif; }
header { display: flex; align-items: center; gap: 12px; padding: 10px 16px; flex-wrap: wrap;
  background: var(--panel); border-bottom: 1px solid var(--border); position: sticky; top: 0; z-index: 5; }
.brand { font-weight: 700; font-size: 15px; letter-spacing: 1px; }
.brand span { color: var(--accent); font-size: 11px; margin-left: 6px; }
header a { color: var(--muted); text-decoration: none; font-size: 12px; }
header a:hover { color: var(--accent); }
.status { color: var(--muted); font-size: 12px; }
.status.err { color: var(--err); }
.spacer { flex: 1; }
input, select, button {
  background: #0d1117; color: var(--text); border: 1px solid var(--border);
  border-radius: 6px; padding: 5px 8px; font: inherit;
}
input:focus, select:focus { outline: 1px solid var(--accent); }
button { cursor: pointer; }
button:hover { border-color: var(--accent); }
main { max-width: 1400px; margin: 0 auto; padding: 14px 16px; display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 620px), 1fr)); gap: 14px; }
.card { background: var(--panel); border: 1px solid var(--border); border-radius: 8px; padding: 12px; min-width: 0; }
.card.wide { grid-column: 1 / -1; }
.card h3 { margin: 0 0 2px; font-size: 13.5px; }
.card .sub { color: var(--muted); font-size: 12px; margin: 0 0 8px; }
.section { grid-column: 1 / -1; margin: 8px 0 -4px; font-size: 12px; color: var(--muted); letter-spacing: 1px; }
.row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.muted { color: var(--muted); }
.chk { display: flex; align-items: center; gap: 4px; color: var(--muted); user-select: none; }
.vchip { display: inline-flex; align-items: center; gap: 6px; border: 1px solid var(--border); border-radius: 14px;
  padding: 2px 10px; cursor: pointer; color: var(--muted); user-select: none; font-size: 12px; }
.vchip.on { color: var(--text); border-color: #484f58; }
.vchip i { width: 10px; height: 10px; border-radius: 3px; background: transparent; border: 1px solid var(--border); }
.vchip.on i { border: none; }
.legend { display: flex; flex-wrap: wrap; gap: 12px; font-size: 12px; color: var(--muted); margin-bottom: 4px; }
.legend span { display: inline-flex; align-items: center; gap: 5px; }
.legend i { width: 10px; height: 10px; border-radius: 3px; display: inline-block; }
.chart { width: 100%; }
.chart svg { display: block; width: 100%; height: auto; overflow: visible; }
.chart text { fill: var(--muted); font-size: 11px; font-variant-numeric: tabular-nums; }
.chart text.lbl { fill: var(--text); }
.tablewrap { overflow: auto; max-height: 420px; border: 1px solid var(--border); border-radius: 6px; }
table { border-collapse: collapse; width: 100%; }
th, td { padding: 5px 10px; text-align: right; border-bottom: 1px solid var(--border); white-space: nowrap;
  font-size: 12.5px; font-variant-numeric: tabular-nums; }
th:first-child, td:first-child { text-align: left; }
th { position: sticky; top: 0; background: #1c2128; color: var(--muted); font-weight: 600; z-index: 1; }
td.na { color: var(--muted); }
tr.group td { background: #1c2128; color: var(--muted); font-size: 11.5px; letter-spacing: 1px; }
.sw { display: inline-block; width: 9px; height: 9px; border-radius: 2px; margin-right: 6px; }
.empty { padding: 18px; text-align: center; color: var(--muted); }
#tip { position: fixed; pointer-events: none; z-index: 20; background: #1c2128; border: 1px solid var(--border);
  border-radius: 6px; padding: 6px 9px; font-size: 12px; white-space: pre; display: none; box-shadow: 0 4px 12px rgba(0,0,0,.4); }
.heatgrid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 12px; }
.heatgrid canvas { width: 100%; aspect-ratio: 1; display: block; border: 1px solid var(--border); border-radius: 6px;
  image-rendering: pixelated; background: #0d1117; }
.heatgrid .cap { font-size: 12px; margin-bottom: 4px; display: flex; align-items: center; }
.ramp { height: 8px; width: 160px; border-radius: 4px; background: linear-gradient(90deg, #104281, #3987e5, #cde2fb); }
.missing { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 8px; }
.missing div { border: 1px dashed var(--border); border-radius: 6px; padding: 8px 10px; color: var(--muted); font-size: 12px; }
.missing b { color: var(--text); display: block; font-weight: 600; }
details summary { cursor: pointer; color: var(--muted); }
details pre { font: 11.5px/1.6 Consolas, monospace; background: #0d1117; border: 1px solid var(--border);
  border-radius: 6px; padding: 10px; overflow: auto; }
</style>
</head>
<body>
<header>
  <div class="brand">LOGKIT<span>VERSION STATS</span></div>
  <a href="/dashboard">GM 툴(세션별 로그) →</a>
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
    <p class="sub">Shop / 총 판매의 price · 버전 내 판매 비율 (Unity 콘솔 중복 출력 제외)</p>
    <div id="c-price" class="chart"></div>
  </section>

  <section class="card">
    <h3>판매 태그 수 분포</h3>
    <p class="sub">총 판매의 tagCount · 버전 내 판매 비율</p>
    <div id="c-tags" class="chart"></div>
  </section>

  <section class="card">
    <h3>작업대 체류 시간 분포</h3>
    <p class="sub">같은 씬의 시간순 진입 → 퇴장 쌍 · 버전 내 비율. 주문 ID가 없어 주문별로는 연결하지 않습니다.</p>
    <div id="c-workbench" class="chart"></div>
  </section>

  <section class="card">
    <h3>미수집 항목</h3>
    <p class="sub">현재 로그에 없어 집계하지 않는 항목입니다. 추정값을 표시하지 않습니다.</p>
    <div class="missing">
      <div><b>제출 · 휴대 총기 코드</b>판매·습격 시작에 gunCode가 없음</div>
      <div><b>모듈 구성</b>휴대 모듈 ID가 없음 (종료 보상 ID만 있음)</div>
      <div><b>사격 테스트 · 사격 인풋</b>사격 이벤트가 없음</div>
      <div><b>주문 성공률</b>주문 ID·전체 주문 수가 없음 — 판매 조건 만족과 다름</div>
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

Shop      / 총 판매         price, satisfied, tagCount        (Unity / [Customer] 총 판매 … 는 중복 출력이므로 제외)
Workshop  / 작업대 진입 · 작업대 퇴장                          (같은 씬의 시간순 진입→퇴장 쌍만 체류 시간으로 계산)
NightRaid / 습격 시작        scene                            (씬 전환 전 Main에서 기록되므로 습격 씬은 data_json.scene)
NightRaid / 플레이어 상태    time, position{x,y,z}, cameraRotation, health,
                             remainingModuleCrates, remainingMaterialCrates, tanks, helicopters
NightRaid / 블록 파괴        stageId, time, blockId
NightRaid / 적 생성 · 적 발견 · 적 처치    time, enemyId      (enemyId는 개체 ID라 종류로 묶지 않음)
NightRaid / 습격 사망 · 탈출 성공 · 습격 성공
                             scene, survived, time, duration, enemyKills, health,
                             moduleCrateRewards[], moduleCrateCredits, materialCrateCredits

timestamp_utc(실제 시각)와 data_json.time(게임 시간)은 섞지 않습니다. 습격 차트는 게임 시간 기준입니다.
모듈 상자 · 재료 상자는 별도로 표시하고 다른 이름(금고 등)으로 바꾸지 않습니다.</pre>
    </details>
  </section>
</main>
<div id="tip"></div>
<script>
"use strict";
const $ = (id) => document.getElementById(id);
const KEY_STORE = "logkit.key";
const PREF_STORE = "logkit.analytics";
// 어두운 표면용 범주형 팔레트(검증된 순서). 색은 버전에 고정되며 선택 순서가 아닌 슬롯 배정으로 유지된다.
const PALETTE = ["#3987e5", "#d95926", "#199e70", "#c98500", "#d55181", "#008300", "#9085e9", "#e66767"];
const MAX_SELECTED = PALETTE.length;
const state = { data: null, selected: [], colorOf: {} };

function esc(s) {
  return String(s ?? "").replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}
function getKey() { try { return localStorage.getItem(KEY_STORE) || ""; } catch (_) { return ""; } }
function setKey(k) { try { localStorage.setItem(KEY_STORE, k); } catch (_) {} }
function loadPrefs() { try { return JSON.parse(localStorage.getItem(PREF_STORE) || "{}"); } catch (_) { return {}; } }
function savePrefs(p) { try { localStorage.setItem(PREF_STORE, JSON.stringify(Object.assign(loadPrefs(), p))); } catch (_) {} }
function setStatus(text, isErr) { const el = $("status"); el.textContent = text; el.className = isErr ? "status err" : "status"; }
async function api(path) {
  const res = await fetch(path, { headers: { "X-Api-Key": getKey() } });
  if (res.status === 401) throw new Error("인증 실패 — API 키를 확인하세요");
  if (!res.ok) throw new Error("HTTP " + res.status + " " + (await res.text()).slice(0, 120));
  return res.json();
}
const num = (n, d = 1) => n == null || !isFinite(n) ? "–" : Number(n).toLocaleString(undefined, { maximumFractionDigits: d });
const pct = (r, d = 1) => r == null || !isFinite(r) ? "–" : (r * 100).toFixed(d) + "%";
const ratio = (a, b) => b > 0 ? a / b : null;
function dur(sec) {
  if (sec == null || !isFinite(sec)) return "–";
  if (sec < 60) return sec.toFixed(1) + "초";
  if (sec < 3600) return Math.floor(sec / 60) + "분 " + Math.round(sec % 60) + "초";
  return Math.floor(sec / 3600) + "시간 " + Math.round(sec % 3600 / 60) + "분";
}
function toLocal(ts) {
  const m = String(ts || "").match(/^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})/);
  if (!m) return "–";
  const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6]));
  return d.toLocaleString("sv-SE").slice(0, 16);
}
function niceMax(v) {
  if (!(v > 0)) return 1;
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  for (const m of [1, 2, 2.5, 5, 10]) if (m * p >= v - 1e-12) return m * p;
  return 10 * p;
}
function selectedVersions() {
  if (!state.data) return [];
  return state.data.versions.filter((v) => state.selected.includes(v.version));
}
function color(v) { return state.colorOf[v.version]; }
function legend(vs) {
  if (vs.length < 2) return "";
  return '<div class="legend">' + vs.map((v) =>
    '<span><i style="background:' + color(v) + '"></i>' + esc(v.version) + "</span>").join("") + "</div>";
}
function emptyBox(text) { return '<div class="empty">' + esc(text) + "</div>"; }
function widthOf(el) { return Math.max(300, Math.round(el.clientWidth || 600)); }

// 막대 끝만 둥근(4px) 경로. 기준선 쪽은 각지게 둔다.
function barUp(x, y, w, h) {
  if (h <= 0) return "";
  const r = Math.min(4, w / 2, h);
  return "M" + x + "," + (y + h) + "V" + (y + r) + "Q" + x + "," + y + " " + (x + r) + "," + y +
    "H" + (x + w - r) + "Q" + (x + w) + "," + y + " " + (x + w) + "," + (y + r) + "V" + (y + h) + "Z";
}
function barRight(x, y, w, h) {
  if (w <= 0) return "";
  const r = Math.min(4, h / 2, w);
  return "M" + x + "," + y + "H" + (x + w - r) + "Q" + (x + w) + "," + y + " " + (x + w) + "," + (y + r) +
    "V" + (y + h - r) + "Q" + (x + w) + "," + (y + h) + " " + (x + w - r) + "," + (y + h) + "H" + x + "Z";
}
function yAxis(W, top, plotH, left, max, fmt) {
  let s = "";
  for (let i = 0; i <= 4; i++) {
    const val = max * i / 4;
    const y = top + plotH - plotH * i / 4;
    s += '<line x1="' + left + '" x2="' + (W - 8) + '" y1="' + y + '" y2="' + y + '" stroke="' +
      (i === 0 ? "var(--axis)" : "var(--grid)") + '" stroke-width="1"/>';
    s += '<text x="' + (left - 6) + '" y="' + (y + 4) + '" text-anchor="end">' + esc(fmt(val)) + "</text>";
  }
  return s;
}

/** 버전별 값 목록을 구간 비율로 바꿔 세로 묶음 막대로 그린다. bucketOf(value) → 구간 인덱스. */
function distribution(el, vs, valuesOf, cats, bucketOf, unit) {
  const series = vs.map((v) => {
    const counts = new Array(cats.length).fill(0);
    const values = valuesOf(v);
    values.forEach((x) => { const b = bucketOf(x); if (b >= 0 && b < cats.length) counts[b]++; });
    return { v, counts, total: values.length };
  }).filter((s) => s.total > 0);
  if (series.length === 0) { el.innerHTML = emptyBox("기록된 데이터가 없습니다."); return; }
  const W = widthOf(el), H = 240, left = 48, top = 10, bottom = 26;
  const plotW = W - left - 8, plotH = H - top - bottom;
  let max = 0;
  series.forEach((s) => s.counts.forEach((c) => { max = Math.max(max, c / s.total); }));
  max = niceMax(max);
  const groupW = plotW / cats.length, gap = 2;
  const barW = Math.max(2, Math.min(18, (groupW * 0.78 - gap * (series.length - 1)) / series.length));
  const usedW = barW * series.length + gap * (series.length - 1);
  let s = yAxis(W, top, plotH, left, max, (x) => num(x * 100, 1) + "%");
  cats.forEach((cat, c) => {
    const gx = left + groupW * c + (groupW - usedW) / 2;
    series.forEach((sr, i) => {
      const share = sr.counts[c] / sr.total;
      const h = plotH * share / max;
      const x = gx + i * (barW + gap);
      s += '<path d="' + barUp(x, top + plotH - h, barW, h) + '" fill="' + color(sr.v) + '"/>';
      s += '<rect x="' + (x - gap / 2) + '" y="' + top + '" width="' + (barW + gap) + '" height="' + plotH +
        '" fill="transparent" data-tip="' + esc(sr.v.version + " · " + cat + "\n" + pct(share) + " (" + num(sr.counts[c], 0) + unit + " / " + num(sr.total, 0) + ")") + '"/>';
    });
    s += '<text x="' + (left + groupW * c + groupW / 2) + '" y="' + (H - 8) + '" text-anchor="middle">' + esc(cat) + "</text>";
  });
  el.innerHTML = legend(series.map((x) => x.v)) + '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

/**
 * 항목 × 버전 가로 묶음 막대. valueOf(v, key) → { value, label, tip } 또는 null(기록 없음).
 * maxValue를 주면 그 값을 축 최대로 쓴다(비율이면 1). perKey면 항목마다 따로 축을 잡는다(단위가 다른 항목을 나열할 때).
 */
function hbars(el, vs, keys, valueOf, maxValue, perKey) {
  if (keys.length === 0) { el.innerHTML = emptyBox("기록된 데이터가 없습니다."); return; }
  const W = widthOf(el), labelW = 150, valueW = 110, barH = 8, gap = 2, rowPad = 10;
  const rowH = vs.length * barH + (vs.length - 1) * gap + rowPad;
  const H = keys.length * rowH + 4;
  const plotW = W - labelW - valueW;
  let max = maxValue || 0;
  if (!maxValue) keys.forEach((k) => vs.forEach((v) => { const r = valueOf(v, k); if (r) max = Math.max(max, r.value); }));
  max = max || 1;
  let s = "";
  keys.forEach((key, r) => {
    const y0 = r * rowH + 2;
    if (perKey) {
      max = 0;
      vs.forEach((v) => { const rv = valueOf(v, key); if (rv) max = Math.max(max, rv.value); });
      max = max || 1;
    }
    s += '<text class="lbl" x="' + (labelW - 8) + '" y="' + (y0 + (rowH - rowPad) / 2 + 4) + '" text-anchor="end">' +
      esc(key.length > 20 ? key.slice(0, 19) + "…" : key) + "<title>" + esc(key) + "</title></text>";
    vs.forEach((v, i) => {
      const rv = valueOf(v, key);
      const y = y0 + i * (barH + gap);
      const w = rv ? plotW * rv.value / max : 0;
      if (rv) s += '<path d="' + barRight(labelW, y, w, barH) + '" fill="' + color(v) + '"/>';
      s += '<rect x="' + labelW + '" y="' + (y - gap / 2) + '" width="' + (plotW + valueW) + '" height="' + (barH + gap) +
        '" fill="transparent" data-tip="' + esc(v.version + " · " + key + "\n" + (rv ? rv.tip : "기록 없음")) + '"/>';
      if (vs.length <= 4) {
        s += '<text x="' + (labelW + w + 6) + '" y="' + (y + barH - 0.5) + '">' + esc(rv ? rv.label : "–") + "</text>";
      }
    });
  });
  el.innerHTML = legend(vs) + '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

/** 버전별 (x, y) 점 목록을 선으로 그린다. */
function lines(el, vs, pointsOf, xFmt, yFmt, tipOf) {
  const series = vs.map((v) => ({ v, pts: pointsOf(v).filter((p) => p.y != null) })).filter((s) => s.pts.length);
  if (series.length === 0) { el.innerHTML = emptyBox("기록된 데이터가 없습니다."); return; }
  const W = widthOf(el), H = 240, left = 52, top = 10, bottom = 26;
  const plotW = W - left - 16, plotH = H - top - bottom;
  let minX = Infinity, maxX = -Infinity, maxY = 0;
  series.forEach((s) => s.pts.forEach((p) => { minX = Math.min(minX, p.x); maxX = Math.max(maxX, p.x); maxY = Math.max(maxY, p.y); }));
  maxY = niceMax(maxY);
  if (maxX === minX) maxX = minX + 1;
  const sx = (x) => left + (x - minX) / (maxX - minX) * plotW;
  const sy = (y) => top + plotH - y / maxY * plotH;
  let s = yAxis(W, top, plotH, left, maxY, yFmt);
  for (let i = 0; i <= 6; i++) {
    const x = minX + (maxX - minX) * i / 6;
    s += '<text x="' + sx(x) + '" y="' + (H - 8) + '" text-anchor="middle">' + esc(xFmt(x)) + "</text>";
  }
  series.forEach(({ v, pts }) => {
    s += '<polyline fill="none" stroke="' + color(v) + '" stroke-width="2" stroke-linejoin="round" points="' +
      pts.map((p) => sx(p.x).toFixed(1) + "," + sy(p.y).toFixed(1)).join(" ") + '"/>';
  });
  const dense = series.some((x) => x.pts.length > 40);
  series.forEach(({ v, pts }) => {
    pts.forEach((p) => {
      const cx = sx(p.x), cy = sy(p.y);
      if (!dense) s += '<circle cx="' + cx + '" cy="' + cy + '" r="4" fill="' + color(v) + '" stroke="var(--panel)" stroke-width="2"/>';
      s += '<circle cx="' + cx + '" cy="' + cy + '" r="9" fill="transparent" data-tip="' + esc(tipOf(v, p)) + '"/>';
    });
  });
  el.innerHTML = legend(series.map((x) => x.v)) + '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

function avg(list) { return list.length ? list.reduce((a, b) => a + b, 0) / list.length : null; }

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
// 순차 램프(파랑 한 색): 어두운 표면 위에서 낮을수록 어둡고 높을수록 밝다.
const RAMP = [[0x10, 0x42, 0x81], [0x25, 0x6a, 0xbf], [0x39, 0x87, 0xe5], [0x86, 0xb6, 0xef], [0xcd, 0xe2, 0xfb]];
function ramp(t) {
  t = Math.max(0, Math.min(1, t)) * (RAMP.length - 1);
  const i = Math.min(RAMP.length - 2, Math.floor(t)), f = t - i;
  return RAMP[i].map((a, k) => Math.round(a + (RAMP[i + 1][k] - a) * f));
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

function renderChips() {
  const box = $("vchips");
  box.innerHTML = "";
  if (!state.data || state.data.versions.length === 0) { box.innerHTML = '<span class="muted">버전 데이터가 없습니다.</span>'; return; }
  for (const v of state.data.versions) {
    const on = state.selected.includes(v.version);
    const chip = document.createElement("span");
    chip.className = "vchip" + (on ? " on" : "");
    chip.innerHTML = '<i style="' + (on ? "background:" + state.colorOf[v.version] : "") + '"></i>' + esc(v.version) +
      ' <span class="muted">' + num(v.sessions, 0) + "세션</span>";
    chip.onclick = () => toggleVersion(v.version);
    box.appendChild(chip);
  }
}

function toggleVersion(ver) {
  if (state.selected.includes(ver)) {
    state.selected = state.selected.filter((x) => x !== ver);
    delete state.colorOf[ver];
  } else {
    if (state.selected.length >= MAX_SELECTED) { setStatus("버전은 최대 " + MAX_SELECTED + "개까지 비교할 수 있습니다.", true); return; }
    state.selected.push(ver);
    assignColors();
  }
  savePrefs({ selected: state.selected });
  renderChips();
  render();
}
// 이미 색이 있는 버전은 그대로 두고 새 버전에만 비어 있는 슬롯을 준다.
function assignColors() {
  for (const ver of Object.keys(state.colorOf)) if (!state.selected.includes(ver)) delete state.colorOf[ver];
  const used = new Set(Object.values(state.colorOf));
  for (const ver of state.selected) {
    if (state.colorOf[ver]) continue;
    const slot = PALETTE.find((c) => !used.has(c));
    state.colorOf[ver] = slot; used.add(slot);
  }
}

async function loadMonths() {
  const data = await api("/api/months");
  const sel = $("month");
  const prev = sel.value || loadPrefs().month || "";
  sel.innerHTML = '<option value="">전체 기간</option>' +
    data.months.map((m) => '<option value="' + esc(m.month) + '">' + esc(m.month) + " (" + m.sessions + "세션)</option>").join("");
  if ([...sel.options].some((o) => o.value === prev)) sel.value = prev;
}

async function load() {
  setKey($("key").value.trim());
  setStatus("불러오는 중…");
  try {
    await loadMonths();
    const p = new URLSearchParams();
    if ($("month").value) p.set("month", $("month").value);
    if ($("noeditor").checked) p.set("excludeEditor", "true");
    savePrefs({ month: $("month").value, noEditor: $("noeditor").checked });
    state.data = await api("/api/analytics?" + p);
    const all = state.data.versions.map((v) => v.version);
    const kept = state.selected.filter((x) => all.includes(x));
    const saved = (loadPrefs().selected || []).filter((x) => all.includes(x));
    state.selected = (kept.length ? kept : saved.length ? saved : all.slice(-4)).slice(0, MAX_SELECTED);
    assignColors();
    renderChips();
    render();
    const sessions = state.data.versions.reduce((a, v) => a + v.sessions, 0);
    setStatus((state.data.month || "전체 기간") + " · " + all.length + "개 버전 · " + num(sessions, 0) + "세션");
  } catch (err) {
    setStatus(String(err.message || err), true);
  }
}

// 툴팁: data-tip 속성을 가진 표식 위에서 표시
const tip = $("tip");
document.addEventListener("mousemove", (ev) => {
  const t = ev.target.closest && ev.target.closest("[data-tip]");
  if (!t) { tip.style.display = "none"; return; }
  tip.textContent = t.getAttribute("data-tip");
  tip.style.display = "block";
  const x = Math.min(ev.clientX + 14, window.innerWidth - tip.offsetWidth - 8);
  const y = Math.min(ev.clientY + 14, window.innerHeight - tip.offsetHeight - 8);
  tip.style.left = x + "px"; tip.style.top = y + "px";
});

$("load").onclick = load;
$("key").addEventListener("keydown", (ev) => { if (ev.key === "Enter") load(); });
$("month").onchange = load;
$("noeditor").onchange = load;
$("tlmetric").onchange = render;
$("stage").onchange = () => renderHeat(selectedVersions());
let resizeTimer = 0;
window.addEventListener("resize", () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(render, 150); });

const prefs = loadPrefs();
$("noeditor").checked = !!prefs.noEditor;
$("key").value = getKey();
if (getKey()) load(); else setStatus("API 키를 입력하고 불러오세요.");
</script>
</body>
</html>
""";
}
