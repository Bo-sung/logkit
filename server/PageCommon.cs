namespace LogServer;

/// <summary>
/// 버전 통계 페이지(/analytics, /weapons)가 함께 쓰는 스타일과 스크립트.
/// 버전 칩·색 배정·차트(분포, 가로 막대, 선)·툴팁·불러오기 흐름이 여기에 있고, 각 페이지는 집계 화면만 그린다.
/// </summary>
public static class PageCommon
{
    /// <summary>공통 CSS(어두운 표면 토큰, 카드, 표, 차트, 툴팁).</summary>
    public const string Css = """
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
nav.pages { display: flex; align-items: center; gap: 12px; font-size: 12px; }
nav.pages b { color: var(--text); font-weight: 600; }
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
.sectabs { grid-column: 1 / -1; position: sticky; z-index: 4; align-self: start; margin: 0 -16px; padding: 8px 16px 6px;
  background: var(--bg); border-bottom: 1px solid var(--border); }
.sectabs .tabs { display: flex; gap: 2px; overflow-x: auto; scrollbar-width: thin; }
.sectabs .tabs button { border: none; border-bottom: 2px solid transparent; border-radius: 0; background: none;
  color: var(--muted); padding: 6px 12px; white-space: nowrap; }
.sectabs .tabs button:hover { color: var(--text); }
.sectabs .tabs button.on { color: var(--text); border-bottom-color: var(--accent); font-weight: 600; }
.sectabs .tabs button span { font-size: 11px; margin-left: 3px; }
.sectabs .toc { display: flex; gap: 6px; flex-wrap: wrap; margin-top: 6px; }
.sectabs .toc:empty { display: none; }
.sectabs .toc a { color: var(--muted); font-size: 11.5px; text-decoration: none; border: 1px solid var(--border);
  border-radius: 12px; padding: 1px 9px; cursor: pointer; }
.sectabs .toc a:hover { color: var(--accent); border-color: var(--accent); }
.card, .section { scroll-margin-top: var(--stick, 120px); }
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
  background: #12261b; }
.heatgrid .cap { font-size: 12px; margin-bottom: 4px; display: flex; align-items: center; }
.ramp { height: 8px; width: 160px; border-radius: 4px; background: linear-gradient(90deg, #104281, #3987e5, #cde2fb); }
.ramp.pitch { background: linear-gradient(90deg, #12261b, #1a9850, #91cf60, #fee08b, #fc8d59, #d73027); }
.missing { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 8px; }
.missing div { border: 1px dashed var(--border); border-radius: 6px; padding: 8px 10px; color: var(--muted); font-size: 12px; }
.missing b { color: var(--text); display: block; font-weight: 600; }
details summary { cursor: pointer; color: var(--muted); }
.snapbadge { font-size: 12px; color: #e3b341; border: 1px solid #9e6a03; border-radius: 12px; padding: 1px 10px; }
.unitsw { display: inline-flex; margin-left: 10px; vertical-align: middle; border: 1px solid var(--border); border-radius: 6px;
  overflow: hidden; font-weight: 400; }
.unitsw button { border: 0; border-radius: 0; padding: 1px 8px; font-size: 11px; color: var(--muted); background: transparent; }
.unitsw button + button { border-left: 1px solid var(--border); }
.unitsw button.on { background: #1f6feb40; color: var(--text); }
details pre { font: 11.5px/1.6 Consolas, monospace; background: #0d1117; border: 1px solid var(--border);
  border-radius: 6px; padding: 10px; overflow: auto; }
""";

    /// <summary>공통 스크립트. 페이지 스크립트보다 먼저 별도 script 태그로 넣는다.</summary>
    public const string Script = """
"use strict";
// 페이지 스크립트가 ENDPOINT(집계 API 경로), PREF_STORE(설정 저장 키), render()를 정의한다.
const $ = (id) => document.getElementById(id);
const KEY_STORE = "logkit.key";
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
// HTML 저장본이면 페이지에 넣어 둔 데이터(window.LOGKIT_SNAPSHOT)로 그리고, 화면 설정은 저장본 안에서만 바꾼다
const SNAP = window.LOGKIT_SNAPSHOT || null;
function loadPrefs() {
  if (SNAP) return SNAP.prefs || (SNAP.prefs = {});
  try { return JSON.parse(localStorage.getItem(PREF_STORE) || "{}"); } catch (_) { return {}; }
}
function savePrefs(p) {
  if (SNAP) { Object.assign(loadPrefs(), p); return; }
  try { localStorage.setItem(PREF_STORE, JSON.stringify(Object.assign(loadPrefs(), p))); } catch (_) {}
}

/**
 * 지금 불러온 데이터를 넣은 단독 HTML을 내려받는다. 페이지 원본(서버의 같은 경로)에 데이터·선택 버전·화면 설정을 스크립트로 끼워 넣으므로
 * 저장본은 서버 없이 열리고 차트·탭·필터·툴팁을 그대로 쓸 수 있다.
 */
async function exportSnapshot() {
  if (!state.data) { setStatus("먼저 데이터를 불러오세요.", true); return; }
  const status = $("status").textContent;
  setStatus("HTML 만드는 중…");
  try {
    const page = await (await fetch(location.pathname)).text();
    const snap = { page: location.pathname, origin: location.origin, savedAt: new Date().toISOString(), status,
      data: state.data, selected: state.selected, colorOf: state.colorOf, prefs: loadPrefs() };
    // 데이터 안의 닫는 스크립트 태그가 저장본의 스크립트를 끊지 않게 < 를 이스케이프한다(페이지 안에도 그 태그를 그대로 쓰지 않는다)
    const json = JSON.stringify(snap).replace(/</g, "\\u003c");
    const at = page.indexOf("<script>");
    const html = page.slice(0, at) + "<script>window.LOGKIT_SNAPSHOT = " + json + ";<" + "/script>\n" + page.slice(at);
    const now = new Date(), pad = (n) => String(n).padStart(2, "0");
    const stamp = now.getFullYear() + pad(now.getMonth() + 1) + pad(now.getDate()) + "-" + pad(now.getHours()) + pad(now.getMinutes());
    const name = "logkit-" + location.pathname.replace(/\//g, "") + "-" + (state.data.month || "all") + "-" + stamp + ".html";
    const a = document.createElement("a");
    a.href = URL.createObjectURL(new Blob([html], { type: "text/html;charset=utf-8" }));
    a.download = name;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 10000);
    setStatus(status + " · 저장함 " + name + " (" + num(html.length / 1024, 0) + "KB)");
  } catch (err) { setStatus("HTML 저장 실패 — " + String(err.message || err), true); }
}

/** 저장본이면 불러오기·API 키·기간 선택을 숨기고 저장 시각 표시를 붙인다. 저장본이면 true. */
function initSnapshotMode() {
  if (!SNAP) {
    if ($("export")) $("export").onclick = exportSnapshot;
    return false;
  }
  for (const id of ["key", "load", "month", "noeditor", "export"]) {
    const el = $(id);
    if (el) (el.closest("label") || el).style.display = "none";
  }
  // 다른 페이지 링크는 저장한 서버 주소로
  document.querySelectorAll("nav.pages a").forEach((a) => { a.href = SNAP.origin + a.getAttribute("href"); });
  const badge = document.createElement("span");
  badge.className = "snapbadge";
  badge.textContent = "저장본 · " + new Date(SNAP.savedAt).toLocaleString("sv-SE").slice(0, 16) + " 저장";
  badge.title = SNAP.origin + SNAP.page;
  $("status").before(badge);
  document.title += " (저장본)";
  return true;
}
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

/** 비율 ↔ 표본 수 토글 상태. 카드 제목에 <span class="unitsw" data-unit="키"></span>를 두면 initPage가 버튼을 채운다. */
function isCount(key) { return ((state.units || {})[key] || "pct") === "count"; }
/** 가로 막대 값: 비율 모드면 n / total, 표본 수 모드면 n. unit = 표본 단위(건·명·회). */
function shareBar(key, n, total, unit, d = 0) {
  const r = total ? n / total : 0, tip = pct(r) + " (" + num(n, 0) + " / " + num(total, 0) + unit + ")";
  return isCount(key) ? { value: n, label: num(n, 0) + unit, tip } : { value: r, label: pct(r, d), tip };
}
function initUnitSwitches() {
  state.units = loadPrefs().units || {};
  document.querySelectorAll(".unitsw").forEach((el) => {
    const key = el.dataset.unit;
    const draw = () => {
      el.innerHTML = [["pct", "비율"], ["count", "표본 수"]].map(([k, n]) => '<button type="button" data-k="' + k + '"' +
        ((isCount(key) ? "count" : "pct") === k ? ' class="on"' : "") + ">" + n + "</button>").join("");
    };
    el.onclick = (ev) => {
      const b = ev.target.closest("button");
      if (!b) return;
      state.units[key] = b.dataset.k;
      savePrefs({ units: state.units });
      draw();
      if (state.data) render();
    };
    draw();
  });
}
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
/** 건수 축 최댓값. 눈금 4칸이 모두 정수가 되도록 4의 배수로 올린다. */
function countMax(n) { return Math.max(4, Math.ceil(niceMax(Math.max(1, n) / 4)) * 4); }
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

/**
 * 버전별 값 목록을 구간 비율로 바꿔 세로 묶음 막대로 그린다. bucketOf(value) → 구간 인덱스.
 * unitKey의 토글이 표본 수면 비율 대신 구간별 건수를 그린다.
 */
function distribution(el, vs, valuesOf, cats, bucketOf, unit, unitKey) {
  const count = !!unitKey && isCount(unitKey);
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
  series.forEach((s) => s.counts.forEach((c) => { max = Math.max(max, count ? c : c / s.total); }));
  max = count ? countMax(max) : niceMax(max);
  const groupW = plotW / cats.length, gap = 2;
  const barW = Math.max(2, Math.min(18, (groupW * 0.78 - gap * (series.length - 1)) / series.length));
  const usedW = barW * series.length + gap * (series.length - 1);
  let s = yAxis(W, top, plotH, left, max, count ? (x) => num(x, 0) : (x) => num(x * 100, 1) + "%");
  cats.forEach((cat, c) => {
    const gx = left + groupW * c + (groupW - usedW) / 2;
    series.forEach((sr, i) => {
      const share = sr.counts[c] / sr.total;
      const h = plotH * (count ? sr.counts[c] : share) / max;
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
 * labelW는 항목 이름 영역 너비(px)이며 넘치는 이름은 줄여 쓰고 전체 이름은 title로 둔다.
 */
function hbars(el, vs, keys, valueOf, maxValue, perKey, labelW = 150) {
  if (keys.length === 0) { el.innerHTML = emptyBox("기록된 데이터가 없습니다."); return; }
  const W = widthOf(el), valueW = 110, barH = 8, gap = 2, rowPad = 10;
  const maxChars = Math.max(6, Math.floor(labelW / 7.5));
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
      esc(key.length > maxChars ? key.slice(0, maxChars - 1) + "…" : key) + "<title>" + esc(key) + "</title></text>";
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

// 순차 램프(파랑 한 색): 어두운 표면 위에서 낮을수록 어둡고 높을수록 밝다.
const RAMP = [[0x10, 0x42, 0x81], [0x25, 0x6a, 0xbf], [0x39, 0x87, 0xe5], [0x86, 0xb6, 0xef], [0xcd, 0xe2, 0xfb]];
function ramp(t) {
  t = Math.max(0, Math.min(1, t)) * (RAMP.length - 1);
  const i = Math.min(RAMP.length - 2, Math.floor(t)), f = t - i;
  return RAMP[i].map((a, k) => Math.round(a + (RAMP[i + 1][k] - a) * f));
}
function rgb(t) { return "rgb(" + ramp(t).join(",") + ")"; }

// 축구 히트맵 색: 낮은 밀도는 잔디 바탕이 비치고 초록 → 노랑 → 주황 → 빨강으로 진해진다
const PITCH_STOPS = [[0, [26, 152, 80, 0]], [0.12, [26, 152, 80, 120]], [0.35, [145, 207, 96, 190]],
  [0.6, [254, 224, 139, 220]], [0.8, [252, 141, 89, 235]], [1, [215, 48, 39, 245]]];
const PITCH_LUT = (() => {
  const lut = new Uint8ClampedArray(256 * 4);
  for (let i = 0; i < 256; i++) {
    const t = i / 255, k = PITCH_STOPS.findIndex(([at]) => at >= t);
    const [a0, c0] = PITCH_STOPS[Math.max(0, k - 1)], [a1, c1] = PITCH_STOPS[Math.max(0, k)];
    const f = a1 === a0 ? 0 : (t - a0) / (a1 - a0);
    for (let j = 0; j < 4; j++) lut[i * 4 + j] = c0[j] + (c1[j] - c0[j]) * f;
  }
  return lut;
})();
/**
 * 격자 히트맵들(h.grid × h.grid 칸 수, 아래 행부터)을 축구 히트맵처럼 부드럽게 그려 각 h._canvas에 둔다.
 * 칸 밀도(전체 대비 비율 / maxShare의 제곱근)를 크게 늘려 흐리게 번진 뒤, 함께 그린 모든 히트맵 중 가장 진한 곳을 1로 맞춰
 * 색표를 입힌다(버전·일차끼리 같은 축척으로 비교).
 */
function pitchHeats(list, maxShare) {
  const size = 480, drawn = [];
  let peak = 0;
  list.forEach((h) => {
    if (!h) return;
    const n = h.grid, total = h._total;
    const src = document.createElement("canvas");
    src.width = n; src.height = n;
    const sctx = src.getContext("2d"), img = sctx.createImageData(n, n);
    for (let r = 0; r < n; r++) {
      for (let c = 0; c < n; c++) {
        const val = h.cells[r * n + c];
        if (!val) continue;
        const i = ((n - 1 - r) * n + c) * 4;
        img.data[i] = img.data[i + 1] = img.data[i + 2] = 255;
        img.data[i + 3] = Math.round(255 * Math.min(1, Math.sqrt(val / total / maxShare)));
      }
    }
    sctx.putImageData(img, 0, 0);
    const cv = document.createElement("canvas");
    cv.width = size; cv.height = size;
    const ctx = cv.getContext("2d", { willReadFrequently: true });
    ctx.imageSmoothingEnabled = true;
    ctx.filter = "blur(" + Math.max(2, size / n * 0.9) + "px)";
    ctx.drawImage(src, 0, 0, size, size);
    ctx.filter = "none";
    const out = ctx.getImageData(0, 0, size, size);
    for (let i = 3; i < out.data.length; i += 4) peak = Math.max(peak, out.data[i]);
    drawn.push({ h, cv, ctx, out });
  });
  drawn.forEach(({ h, cv, ctx, out }) => {
    const d = out.data;
    for (let i = 0; i < d.length; i += 4) {
      const k = peak ? Math.min(255, Math.round(d[i + 3] / peak * 255)) * 4 : 0;
      d[i] = PITCH_LUT[k]; d[i + 1] = PITCH_LUT[k + 1]; d[i + 2] = PITCH_LUT[k + 2]; d[i + 3] = PITCH_LUT[k + 3];
    }
    ctx.putImageData(out, 0, 0);
    // 위치를 읽기 쉽게 4×4 보조선
    ctx.strokeStyle = "rgba(255,255,255,0.08)";
    ctx.lineWidth = 1;
    for (let k = 1; k < 4; k++) {
      const p = Math.round(size * k / 4) + 0.5;
      ctx.beginPath(); ctx.moveTo(p, 0); ctx.lineTo(p, size); ctx.moveTo(0, p); ctx.lineTo(size, p); ctx.stroke();
    }
    cv.title = "X " + num(h.minX) + " ~ " + num(h.maxX) + ", Z " + num(h.minZ) + " ~ " + num(h.maxZ);
    h._canvas = cv;
  });
}

function avg(list) { return list.length ? list.reduce((a, b) => a + b, 0) / list.length : null; }

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
    state.data = await api(ENDPOINT + "?" + p);
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

/** 머리글 컨트롤을 연결하고 저장된 키가 있으면 바로 불러온다. 페이지 스크립트 끝에서 부른다. */
/**
 * main의 .section 구분선마다 탭을 만든다. 탭을 고르면 그 섹션의 카드만 보이고, 아래 줄에 카드 바로가기가 나온다.
 * 맨 앞 카드(버전 선택)와 <details>가 든 카드(집계 기준)는 늘 보이고, 그 사이 첫 섹션 앞 카드(버전별 요약 표)는 "요약" 탭이 된다.
 * 고른 탭은 prefs의 tab에 남긴다.
 */
function initSectionTabs() {
  const main = document.querySelector("main"), kids = [...main.children];
  const first = kids.findIndex((el) => el.classList.contains("section"));
  if (first < 0) return;
  const groups = [];
  if (first > 1) groups.push({ name: "요약", head: null, cards: kids.slice(1, first) });
  kids.slice(first).forEach((el) => {
    if (el.classList.contains("section")) { groups.push({ name: el.textContent.trim(), head: el, cards: [] }); return; }
    if (!el.querySelector("details")) groups[groups.length - 1].cards.push(el);
  });
  const ALL = "전체";
  const nav = document.createElement("nav");
  nav.className = "sectabs";
  main.insertBefore(nav, kids[1]);
  const saved = loadPrefs().tab;
  let current = saved === ALL || groups.some((g) => g.name === saved) ? saved : groups[0].name;
  const titleOf = (card) => {
    const h = card.querySelector("h3");
    if (!h) return "";
    const c = h.cloneNode(true);
    c.querySelectorAll("span").forEach((x) => x.remove());
    return c.textContent.trim();
  };
  const draw = () => {
    const tabs = [ALL].concat(groups.map((g) => g.name));
    const links = current === ALL
      ? groups.map((g, i) => '<a data-sec="' + i + '">' + esc(g.name) + "</a>")
      : groups.find((g) => g.name === current).cards.map((c, i) => titleOf(c) ? '<a data-card="' + i + '">' + esc(titleOf(c)) + "</a>" : "");
    nav.innerHTML = '<div class="tabs">' + tabs.map((n) => '<button type="button" data-tab="' + esc(n) + '"' + (n === current ? ' class="on"' : "") + ">" +
      esc(n) + (n === ALL ? "" : '<span class="muted">' + groups.find((g) => g.name === n).cards.length + "</span>") + "</button>").join("") +
      '</div><div class="toc">' + links.join("") + "</div>";
    groups.forEach((g) => {
      const show = current === ALL || g.name === current;
      if (g.head) g.head.hidden = current !== ALL;
      g.cards.forEach((c) => { c.hidden = !show; });
    });
  };
  // 붙박이 머리글 + 탭 줄 높이만큼 카드 스크롤 위치를 내린다
  const stick = () => {
    const top = document.querySelector("header").offsetHeight;
    nav.style.top = top + "px";
    document.documentElement.style.setProperty("--stick", (top + nav.offsetHeight + 8) + "px");
  };
  nav.onclick = (ev) => {
    const tab = ev.target.closest("[data-tab]"), sec = ev.target.closest("[data-sec]"), card = ev.target.closest("[data-card]");
    if (tab) {
      if (tab.dataset.tab === current) return;
      current = tab.dataset.tab;
      savePrefs({ tab: current });
      draw(); stick();
      if (state.data) render();
      // 탭을 바꾸면 새 섹션의 맨 위부터 보이게 한다
      const top = nav.offsetTop - parseFloat(nav.style.top);
      if (window.scrollY > top) window.scrollTo({ top });
    } else if (sec) {
      const g = groups[+sec.dataset.sec];
      (g.head || g.cards[0]).scrollIntoView({ behavior: "smooth", block: "start" });
    } else if (card) {
      groups.find((g) => g.name === current).cards[+card.dataset.card].scrollIntoView({ behavior: "smooth", block: "start" });
    }
  };
  draw();
  stick();
  window.addEventListener("resize", stick);
}

function initPage() {
  initUnitSwitches();
  initSectionTabs();
  $("load").onclick = load;
  $("key").addEventListener("keydown", (ev) => { if (ev.key === "Enter") load(); });
  $("month").onchange = load;
  $("noeditor").onchange = load;
  let resizeTimer = 0;
  window.addEventListener("resize", () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(render, 150); });
  if (initSnapshotMode()) {
    state.data = SNAP.data;
    state.selected = SNAP.selected || [];
    state.colorOf = SNAP.colorOf || {};
    renderChips();
    render();
    setStatus(SNAP.status || "");
    return;
  }
  $("noeditor").checked = !!loadPrefs().noEditor;
  $("key").value = getKey();
  if (getKey()) load(); else setStatus("API 키를 입력하고 불러오세요.");
}
""";
}
