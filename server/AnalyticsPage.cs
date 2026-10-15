namespace LogServer;

/// <summary>
/// 버전별 플레이 분석 대시보드의 단일 페이지 HTML.
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
<title>LogKit 분석</title>
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
.sw { display: inline-block; width: 9px; height: 9px; border-radius: 2px; margin-right: 6px; }
.empty { padding: 18px; text-align: center; color: var(--muted); }
#tip { position: fixed; pointer-events: none; z-index: 20; background: #1c2128; border: 1px solid var(--border);
  border-radius: 6px; padding: 6px 9px; font-size: 12px; white-space: pre; display: none; box-shadow: 0 4px 12px rgba(0,0,0,.4); }
.heatgrid { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 12px; }
.heatgrid canvas { width: 100%; height: auto; display: block; border: 1px solid var(--border); border-radius: 6px;
  image-rendering: pixelated; background: #0d1117; }
.heatgrid .cap { font-size: 12px; margin-bottom: 4px; display: flex; align-items: center; }
.ramp { height: 8px; width: 160px; border-radius: 4px; background: linear-gradient(90deg, #104281, #3987e5, #cde2fb); }
details summary { cursor: pointer; color: var(--muted); }
details pre { font: 11.5px/1.6 Consolas, monospace; background: #0d1117; border: 1px solid var(--border);
  border-radius: 6px; padding: 10px; overflow: auto; }
</style>
</head>
<body>
<header>
  <div class="brand">LOGKIT<span>PLAY ANALYTICS</span></div>
  <a href="/dashboard">GM 툴 →</a>
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
      <b>비교할 버전</b><span class="muted">(최대 8개 · 클릭해 켜고 끔)</span>
    </div>
    <div id="vchips" class="row"></div>
  </section>

  <section class="card wide">
    <h3>버전별 핵심 지표</h3>
    <p class="sub">상점은 완료 주문 기준 비율, 습격은 습격 시작 대비 탈출 비율입니다.</p>
    <div id="kpi"></div>
  </section>

  <div class="section">상점 운영</div>

  <section class="card">
    <h3>주문당 사격 테스트 횟수</h3>
    <p class="sub">주문 비율 분포. 예상 횟수를 넘는 주문이 많으면 사격 테스트 요청이 지루할 수 있습니다.
      예상 횟수 <input id="expected" type="number" min="0" max="10" value="3" style="width:56px;padding:2px 6px"></p>
    <div id="c-shots" class="chart"></div>
  </section>

  <section class="card">
    <h3>게임 일수별 평균 보유 금액</h3>
    <p class="sub">상점 종료 시점 금액의 평균</p>
    <div id="c-money" class="chart"></div>
  </section>

  <section class="card">
    <h3>NPC에게 제출한 총</h3>
    <p class="sub">완료 주문의 제출 총기 코드 · 버전 내 비율</p>
    <div id="c-submitted" class="chart"></div>
  </section>

  <section class="card">
    <h3>프리셋에 저장한 총</h3>
    <p class="sub">프리셋 저장 총기 코드 · 버전 내 비율</p>
    <div id="c-preset" class="chart"></div>
  </section>

  <div class="section">야간 습격</div>

  <section class="card">
    <h3>습격에 가져간 총</h3>
    <p class="sub">RaidStart의 guns · 버전 내 비율</p>
    <div id="c-raidguns" class="chart"></div>
  </section>

  <section class="card">
    <h3>습격에 가져간 모듈</h3>
    <p class="sub">RaidStart의 modules · 버전 내 비율</p>
    <div id="c-modules" class="chart"></div>
  </section>

  <section class="card">
    <h3>습격 경과 시간별 평균 상태</h3>
    <p class="sub">고정 주기 표본(RaidSample)을 습격 시작 기준으로 묶은 평균
      <select id="tlmetric" style="padding:2px 6px">
        <option value="hp">체력</option><option value="vaults">남은 금고</option>
        <option value="tanks">탱크 수</option><option value="helis">헬기 수</option>
      </select></p>
    <div id="c-timeline" class="chart"></div>
  </section>

  <section class="card">
    <h3>스테이지별 블록 파괴</h3>
    <p class="sub">습격 1회당 평균 파괴 블록 수</p>
    <div id="c-blocks" class="chart"></div>
  </section>

  <section class="card wide">
    <h3>적 종류별 발견 · 처치</h3>
    <p class="sub">발견률 = 발견 / 생성, 처치율 = 처치 / 생성</p>
    <div id="t-enemies"></div>
  </section>

  <section class="card wide">
    <div class="row" style="justify-content:space-between">
      <div><h3>플레이어 위치 히트맵</h3>
        <p class="sub" style="margin:0">RaidSample의 x·z 밀도 · 버전 내 비율, 제곱근 스케일 · 위쪽이 +z</p></div>
      <div class="row"><span class="muted">스테이지</span><select id="stage"></select>
        <span class="muted">적음</span><span class="ramp"></span><span class="muted">많음</span></div>
    </div>
    <div id="heat" class="heatgrid" style="margin-top:10px"></div>
  </section>

  <section class="card wide">
    <details>
      <summary>로그 이벤트 규약 — 클라이언트에서 이 형식으로 기록해야 집계됩니다</summary>
<pre>GameLogger.Data("Info", "&lt;카테고리&gt;", "&lt;이벤트&gt;", new XxxData { ... });   // 시간은 로그 시각을 그대로 사용

카테고리 Shop
  ShopStart         day, money
  WorkbenchEnter    orderId
  WorkbenchExit     orderId, gunCode
  CustomerInteract  orderId
  OrderComplete     orderId, success, died, gunCode(선택 — 없으면 마지막 WorkbenchExit의 gunCode를 제출 총으로 사용)
  PresetSave        gunCode
  Fire              orderId, gunCode
  ShopEnd           day, money

카테고리 Raid
  RaidStart         stageId, guns[], modules[](선택)
  RaidSample        x, y, z, yaw, pitch, hp, vaults, tanks, helis   (고정 주기 표본)
  BlockDestroyed    stageId, blockId
  EnemySpawn        enemyId
  EnemySpotted      enemyId
  EnemyKilled       enemyId
  EscapeSuccess     playTime, vaultReward, kills, hp

버전은 세션의 appVersion(Application.version)으로 나뉩니다.</pre>
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
const num = (n, d = 1) => n == null ? "–" : Number(n).toLocaleString(undefined, { maximumFractionDigits: d });
const pct = (r, d = 1) => r == null ? "–" : (r * 100).toFixed(d) + "%";
function dur(sec) {
  if (sec == null) return "–";
  if (sec < 60) return sec.toFixed(1) + "초";
  return Math.floor(sec / 60) + "분 " + Math.round(sec % 60) + "초";
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

/** 범주 × 버전 세로 묶음 막대. values[v][c]는 0~1 비율. */
function groupedColumns(el, cats, vs, valueOf, tipOf, marker) {
  const W = widthOf(el), H = 240, left = 44, top = 10, bottom = 26;
  const plotW = W - left - 8, plotH = H - top - bottom;
  let max = 0;
  vs.forEach((v) => cats.forEach((_, c) => { max = Math.max(max, valueOf(v, c) || 0); }));
  max = niceMax(max);
  const groupW = plotW / cats.length;
  const gap = 2;
  const barW = Math.max(2, Math.min(18, (groupW * 0.78 - gap * (vs.length - 1)) / vs.length));
  const usedW = barW * vs.length + gap * (vs.length - 1);
  let s = yAxis(W, top, plotH, left, max, (x) => num(x * 100, 1) + "%");
  if (marker != null && marker < cats.length) {
    const mx = left + groupW * (marker + 1);
    s += '<line x1="' + mx + '" x2="' + mx + '" y1="' + top + '" y2="' + (top + plotH) +
      '" stroke="var(--text)" stroke-width="1" stroke-dasharray="4 3" opacity="0.6"/>';
    s += '<text class="lbl" x="' + (mx + 4) + '" y="' + (top + 10) + '">예상 초과 →</text>';
  }
  cats.forEach((cat, c) => {
    const gx = left + groupW * c + (groupW - usedW) / 2;
    vs.forEach((v, i) => {
      const val = valueOf(v, c) || 0;
      const h = plotH * val / max;
      const x = gx + i * (barW + gap);
      s += '<path d="' + barUp(x, top + plotH - h, barW, h) + '" fill="' + color(v) + '"/>';
      s += '<rect x="' + (x - gap / 2) + '" y="' + top + '" width="' + (barW + gap) + '" height="' + plotH +
        '" fill="transparent" data-tip="' + esc(tipOf(v, c)) + '"/>';
    });
    s += '<text x="' + (left + groupW * c + groupW / 2) + '" y="' + (H - 8) + '" text-anchor="middle">' + esc(cat) + "</text>";
  });
  el.innerHTML = legend(vs) + '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

/** 버전별 KeyCount 목록을 버전 내 비율로 바꿔 가로 묶음 막대로 그린다. */
function shareBars(el, vs, listOf, unit, perRun) {
  const totals = new Map(), keys = new Map();
  vs.forEach((v) => {
    const list = listOf(v);
    const denom = perRun ? perRun(v) : list.reduce((a, k) => a + k.count, 0);
    totals.set(v.version, denom);
    list.forEach((k) => keys.set(k.key, (keys.get(k.key) || 0) + (denom > 0 ? k.count / denom : 0)));
  });
  const ordered = [...keys.entries()].sort((a, b) => b[1] - a[1]).map((e) => e[0]).slice(0, 12);
  if (ordered.length === 0) { el.innerHTML = emptyBox("기록된 데이터가 없습니다."); return; }
  const W = widthOf(el), labelW = 120, valueW = 92, barH = 8, gap = 2, rowPad = 10;
  const rowH = vs.length * barH + (vs.length - 1) * gap + rowPad;
  const H = ordered.length * rowH + 4;
  const plotW = W - labelW - valueW;
  let max = 0;
  const valOf = (v, key) => {
    const k = listOf(v).find((x) => x.key === key);
    const d = totals.get(v.version);
    return { count: k ? k.count : 0, share: k && d > 0 ? k.count / d : 0 };
  };
  ordered.forEach((key) => vs.forEach((v) => { max = Math.max(max, valOf(v, key).share); }));
  max = max || 1;
  let s = "";
  ordered.forEach((key, r) => {
    const y0 = r * rowH + 2;
    s += '<text class="lbl" x="' + (labelW - 8) + '" y="' + (y0 + (rowH - rowPad) / 2 + 4) + '" text-anchor="end">' +
      esc(key.length > 16 ? key.slice(0, 15) + "…" : key) + "<title>" + esc(key) + "</title></text>";
    vs.forEach((v, i) => {
      const { count, share } = valOf(v, key);
      const y = y0 + i * (barH + gap);
      const w = plotW * share / max;
      s += '<path d="' + barRight(labelW, y, w, barH) + '" fill="' + color(v) + '"/>';
      const tip = v.version + " · " + key + "\n" + (perRun ? num(share, 2) + unit : pct(share)) + " (" + num(count, 0) + "건)";
      s += '<rect x="' + labelW + '" y="' + (y - gap / 2) + '" width="' + (plotW + valueW) + '" height="' + (barH + gap) +
        '" fill="transparent" data-tip="' + esc(tip) + '"/>';
      if (vs.length <= 4 && share > 0) {
        s += '<text x="' + (labelW + w + 6) + '" y="' + (y + barH - 0.5) + '">' + (perRun ? num(share, 2) : pct(share, 0)) + "</text>";
      }
    });
  });
  el.innerHTML = legend(vs) + '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

/** 버전별 (x, y) 점 목록을 선으로 그린다. */
function lines(el, vs, pointsOf, xFmt, yFmt, tipOf, intX) {
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
  // 정수 축(게임 일수 등)은 범위가 좁으면 정수 위치에만 눈금을 둔다.
  const xs = intX && maxX - minX <= 12
    ? Array.from({ length: maxX - minX + 1 }, (_, i) => minX + i)
    : Array.from({ length: 7 }, (_, i) => minX + (maxX - minX) * i / 6);
  for (const x of xs) {
    s +='<text x="' + sx(x) + '" y="' + (H - 8) + '" text-anchor="middle">' + esc(xFmt(x)) + "</text>";
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

function renderKpi(vs) {
  const rows = [
    ["세션", (v) => num(v.sessions, 0)],
    ["상점 일수", (v) => num(v.shopDays, 0)],
    ["하루 평균 상점 시간", (v) => dur(v.avgDaySeconds)],
    ["하루 평균 금액 증감", (v) => num(v.avgMoneyGain, 0)],
    ["주문 수 (완료)", (v) => num(v.orders, 0) + " (" + num(v.completedOrders, 0) + ")"],
    ["주문 성공률", (v) => pct(v.successRate)],
    ["주문 사망률", (v) => pct(v.deathRate)],
    ["주문당 평균 사격 횟수", (v) => num(v.avgShotsPerOrder, 2)],
    ["예상 초과 사격 주문 비율", (v) => pct(overExpected(v))],
    ["주문당 작업대 체류", (v) => dur(v.avgWorkbenchSeconds)],
    ["주문당 손님 상호작용", (v) => num(v.avgCustomerVisits, 2)],
    ["프리셋 저장", (v) => num(v.presetSaves, 0)],
    ["습격 횟수", (v) => num(v.raidRuns, 0)],
    ["탈출 성공률", (v) => pct(v.escapeRate)],
    ["평균 탈출 소요 시간", (v) => dur(v.avgPlayTime)],
    ["평균 금고 보상", (v) => num(v.avgVaultReward, 0)],
    ["평균 처치 수", (v) => num(v.avgKills, 1)],
    ["탈출 시 평균 체력", (v) => num(v.avgEscapeHp, 1)],
  ];
  let h = '<div class="tablewrap" style="max-height:none"><table><thead><tr><th>지표</th>' +
    vs.map((v) => '<th><span class="sw" style="background:' + color(v) + '"></span>' + esc(v.version) + "</th>").join("") +
    "</tr></thead><tbody>";
  for (const [label, f] of rows) {
    h += "<tr><td>" + esc(label) + "</td>" + vs.map((v) => {
      const t = f(v);
      return "<td" + (t === "–" ? ' class="na"' : "") + ">" + esc(t) + "</td>";
    }).join("") + "</tr>";
  }
  $("kpi").innerHTML = h + "</tbody></table></div>";
}

function expected() { const n = parseInt($("expected").value, 10); return isNaN(n) ? 3 : Math.max(0, Math.min(9, n)); }
function overExpected(v) {
  const total = v.shotHistogram.reduce((a, b) => a + b, 0);
  if (!total) return null;
  return v.shotHistogram.slice(expected() + 1).reduce((a, b) => a + b, 0) / total;
}

function renderShots(vs) {
  const el = $("c-shots");
  if (!vs.some((v) => v.orders > 0)) { el.innerHTML = emptyBox("주문 기록이 없습니다."); return; }
  const n = vs[0].shotHistogram.length;
  const cats = Array.from({ length: n }, (_, i) => i === n - 1 ? i + "+" : String(i));
  const share = (v, c) => { const t = v.shotHistogram.reduce((a, b) => a + b, 0); return t ? v.shotHistogram[c] / t : 0; };
  groupedColumns(el, cats, vs, share,
    (v, c) => v.version + " · 사격 " + cats[c] + "회\n" + pct(share(v, c)) + " (" + num(v.shotHistogram[c], 0) + "주문)",
    expected());
}

function renderHeat(vs) {
  const stages = [...new Set(vs.flatMap((v) => v.heatmaps.map((h) => h.stageId)))].sort();
  const sel = $("stage");
  const prev = sel.value;
  sel.innerHTML = stages.map((s) => '<option value="' + esc(s) + '">' + esc(s) + "</option>").join("");
  if (stages.includes(prev)) sel.value = prev;
  const stage = sel.value;
  const box = $("heat");
  if (!stage) { box.innerHTML = emptyBox("RaidSample 위치 표본이 없습니다."); return; }
  const maps = vs.map((v) => ({ v, h: v.heatmaps.find((x) => x.stageId === stage) }));
  let maxShare = 0;
  maps.forEach(({ h }) => {
    if (!h) return;
    const total = h.cells.reduce((a, b) => a + b, 0);
    h._total = total;
    h.cells.forEach((c) => { if (total) maxShare = Math.max(maxShare, c / total); });
  });
  box.innerHTML = "";
  for (const { v, h } of maps) {
    const wrap = document.createElement("div");
    wrap.innerHTML = '<div class="cap"><span class="sw" style="background:' + color(v) + '"></span>' + esc(v.version) +
      '<span class="muted" style="margin-left:6px">' + (h ? num(h._total, 0) + " 표본" : "표본 없음") + "</span></div>";
    if (h) {
      const cv = document.createElement("canvas");
      const spanX = h.maxX - h.minX || 1, spanZ = h.maxZ - h.minZ || 1;
      cv.width = h.grid; cv.height = h.grid;
      cv.style.aspectRatio = Math.max(0.4, Math.min(2.5, spanX / spanZ)).toFixed(3);
      const ctx = cv.getContext("2d");
      const img = ctx.createImageData(h.grid, h.grid);
      for (let r = 0; r < h.grid; r++) {
        for (let c = 0; c < h.grid; c++) {
          const val = h.cells[r * h.grid + c];
          if (!val) continue;
          const t = Math.sqrt(val / h._total / maxShare);
          const rgb = ramp(t);
          const i = ((h.grid - 1 - r) * h.grid + c) * 4;
          img.data[i] = rgb[0]; img.data[i + 1] = rgb[1]; img.data[i + 2] = rgb[2]; img.data[i + 3] = 255;
        }
      }
      ctx.putImageData(img, 0, 0);
      cv.title = "x " + num(h.minX) + " ~ " + num(h.maxX) + ", z " + num(h.minZ) + " ~ " + num(h.maxZ);
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

function renderEnemies(vs) {
  const ids = [...new Set(vs.flatMap((v) => v.enemies.map((e) => e.enemyId)))].sort();
  if (ids.length === 0) { $("t-enemies").innerHTML = emptyBox("적 이벤트 기록이 없습니다."); return; }
  let h = '<div class="tablewrap"><table><thead><tr><th>적 ID</th><th>버전</th><th>생성</th><th>발견</th><th>처치</th><th>발견률</th><th>처치율</th></tr></thead><tbody>';
  for (const id of ids) {
    for (const v of vs) {
      const e = v.enemies.find((x) => x.enemyId === id);
      if (!e) continue;
      h += "<tr><td>" + esc(id) + '</td><td><span class="sw" style="background:' + color(v) + '"></span>' + esc(v.version) +
        "</td><td>" + num(e.spawned, 0) + "</td><td>" + num(e.spotted, 0) + "</td><td>" + num(e.killed, 0) +
        "</td><td>" + pct(e.spawned ? e.spotted / e.spawned : null) + "</td><td>" + pct(e.spawned ? e.killed / e.spawned : null) + "</td></tr>";
    }
  }
  $("t-enemies").innerHTML = h + "</tbody></table></div>";
}

function render() {
  const vs = selectedVersions();
  const charts = ["c-shots", "c-money", "c-submitted", "c-preset", "c-raidguns", "c-modules", "c-timeline", "c-blocks"];
  if (vs.length === 0) {
    $("kpi").innerHTML = emptyBox(state.data && state.data.versions.length ? "비교할 버전을 선택하세요." : "범위 안에 세션이 없습니다.");
    charts.forEach((id) => { $(id).innerHTML = ""; });
    $("t-enemies").innerHTML = ""; $("heat").innerHTML = "";
    return;
  }
  renderKpi(vs);
  renderShots(vs);
  lines($("c-money"), vs, (v) => v.moneyByDay.map((d) => ({ x: d.day, y: d.avgMoney, n: d.samples })),
    (x) => Math.round(x) + "일", (y) => num(y, 0),
    (v, p) => v.version + " · " + p.x + "일차\n평균 " + num(p.y, 0) + " (" + p.n + "세션)", true);
  shareBars($("c-submitted"), vs, (v) => v.submittedGuns);
  shareBars($("c-preset"), vs, (v) => v.presetGuns);
  shareBars($("c-raidguns"), vs, (v) => v.raidGuns);
  shareBars($("c-modules"), vs, (v) => v.raidModules);
  shareBars($("c-blocks"), vs, (v) => v.blocks, "개/습격", (v) => v.raidRuns);
  const metric = $("tlmetric").value;
  const metricName = $("tlmetric").selectedOptions[0].textContent;
  lines($("c-timeline"), vs, (v) => v.timeline.map((t) => ({ x: t.sec, y: t[metric], n: t.samples })),
    (x) => Math.floor(x / 60) + ":" + String(Math.round(x % 60)).padStart(2, "0"), (y) => num(y, 1),
    (v, p) => v.version + " · " + Math.floor(p.x / 60) + "분 " + (p.x % 60) + "초\n" + metricName + " " + num(p.y, 2) + " (표본 " + p.n + ")");
  renderEnemies(vs);
  renderHeat(vs);
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
$("stage").onchange = render;
$("expected").oninput = () => { savePrefs({ expected: $("expected").value }); render(); };
let resizeTimer = 0;
window.addEventListener("resize", () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(render, 150); });

const prefs = loadPrefs();
$("noeditor").checked = !!prefs.noEditor;
if (prefs.expected != null) $("expected").value = prefs.expected;
$("key").value = getKey();
if (getKey()) load(); else setStatus("API 키를 입력하고 불러오세요.");
</script>
</body>
</html>
""";
}
