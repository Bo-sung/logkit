namespace LogServer;

/// <summary>
/// 브라우저 로그 열람 대시보드(GM툴)의 단일 페이지 HTML.
/// 외부 자산 없이 vanilla JS로 동작하며 조회·검색·삭제 API를 호출한다. API 키는 localStorage에 보관한다.
/// </summary>
public static class DashboardPage
{
    /// <summary>대시보드 페이지 전체 HTML.</summary>
    public const string Html = """
<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>LogKit GM</title>
<style>
:root {
  --bg: #0d1117; --panel: #161b22; --border: #30363d; --text: #e6edf3;
  --muted: #8b949e; --accent: #58a6ff; --ok: #3fb950; --warn: #d29922;
  --err: #f85149; --exc: #bc8cff;
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--text);
  font: 13px/1.5 "Segoe UI", "Malgun Gothic", sans-serif; }
header { display: flex; align-items: center; gap: 12px; padding: 10px 16px;
  background: var(--panel); border-bottom: 1px solid var(--border); position: sticky; top: 0; z-index: 5; }
.brand { font-weight: 700; font-size: 15px; letter-spacing: 1px; }
.brand span { color: var(--accent); font-size: 11px; margin-left: 6px; }
.status { color: var(--muted); font-size: 12px; }
.status.err { color: var(--err); }
.spacer { flex: 1; }
.keybox { display: flex; gap: 6px; }
input, select, button {
  background: #0d1117; color: var(--text); border: 1px solid var(--border);
  border-radius: 6px; padding: 5px 8px; font: inherit;
}
input:focus, select:focus { outline: 1px solid var(--accent); }
button { cursor: pointer; }
button:hover { border-color: var(--accent); }
button.danger:hover { border-color: var(--err); color: var(--err); }
main { max-width: 1400px; margin: 0 auto; padding: 14px 16px; display: flex; flex-direction: column; gap: 14px; }
.card { background: var(--panel); border: 1px solid var(--border); border-radius: 8px; padding: 12px; }
.row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-bottom: 8px; }
.row:last-child { margin-bottom: 0; }
.muted { color: var(--muted); }
.stats { display: flex; gap: 14px; flex-wrap: wrap; color: var(--muted); font-size: 12px; margin-bottom: 8px; }
.stats b { color: var(--text); font-weight: 600; }
.stats .w { color: var(--warn); } .stats .e { color: var(--err); } .stats .x { color: var(--exc); }
.chk { display: flex; align-items: center; gap: 4px; color: var(--muted); user-select: none; }
.tablewrap { overflow: auto; max-height: 60vh; border: 1px solid var(--border); border-radius: 6px; }
table { border-collapse: collapse; width: 100%; }
th, td { padding: 5px 10px; text-align: left; border-bottom: 1px solid var(--border);
  white-space: nowrap; font-size: 12.5px; }
th { position: sticky; top: 0; background: #1c2128; color: var(--muted); font-weight: 600; z-index: 1; }
tbody tr.click { cursor: pointer; }
tbody tr.click:hover { background: #1c2128; }
tbody tr.sel { background: #14243a; }
td.msg { white-space: normal; max-width: 560px; overflow: hidden; text-overflow: ellipsis; }
td.id { color: var(--muted); font-family: Consolas, monospace; font-size: 12px; }
.lv { display: inline-block; min-width: 58px; text-align: center; border-radius: 4px;
  font-size: 11px; font-weight: 600; padding: 1px 6px; }
.lv.info { background: #21262d; color: var(--muted); }
.lv.warning { background: #33270f; color: var(--warn); }
.lv.error, .lv.assert { background: #3a1214; color: var(--err); }
.lv.exception { background: #271439; color: var(--exc); }
.badge { border-radius: 4px; font-size: 11px; padding: 1px 6px; }
.badge.ok { background: #12261e; color: var(--ok); }
.badge.crash { background: #3a1214; color: var(--err); }
.badge.ed { background: #14243a; color: var(--accent); }
tr.xrow td { background: #0d1117; white-space: normal; }
tr.xrow pre { margin: 4px 0; padding: 8px; background: #161b22; border: 1px solid var(--border);
  border-radius: 6px; overflow: auto; max-height: 260px; font: 11.5px/1.5 Consolas, monospace; white-space: pre-wrap; }
.xlabel { color: var(--muted); font-size: 11px; display: block; margin-top: 6px; }
.chip { display: inline-block; background: #0d1117; border: 1px solid var(--border); border-radius: 12px;
  padding: 2px 10px; margin: 2px 4px 2px 0; font-size: 12px; color: var(--muted); }
.chip b { color: var(--text); font-weight: 600; }
.chip.rate { border-color: var(--accent); }
.chip.rate b { color: var(--accent); }
.empty { padding: 18px; text-align: center; color: var(--muted); }
h2 { font-size: 14px; margin: 0; font-family: Consolas, monospace; }
nav { display: flex; align-items: center; gap: 8px; }
nav.pages { gap: 12px; font-size: 12px; }
nav.pages a { color: var(--muted); text-decoration: none; }
nav.pages a:hover { color: var(--accent); }
nav.pages b { color: var(--text); font-weight: 600; }
</style>
</head>
<body>
<header>
  <div class="brand">LOGKIT<span>GM TOOL</span></div>
  <nav class="pages">
    <b>GM 툴(세션별 로그)</b>
    <a href="/analytics">버전 통계</a>
    <a href="/weapons">총기 통계</a>
    <a href="/insights">가설 통계</a>
  </nav>
  <div id="status" class="status">API 키를 입력하고 연결하세요.</div>
  <span class="spacer"></span>
  <div class="keybox">
    <input id="key" type="password" placeholder="API 키" style="width:180px">
    <button id="connect">연결</button>
  </div>
</header>
<main>
  <section class="card">
    <div class="row">
      <select id="month" style="min-width:170px"></select>
      <button id="refresh">새로고침</button>
      <span class="spacer"></span>
      <button id="delmonth" class="danger">이 달 전체 삭제</button>
    </div>
    <div id="monthstats" class="stats"></div>
    <div id="monthmetrics" class="row"></div>
  </section>

  <section class="card">
    <div class="row">
      <select id="sver" style="min-width:140px"><option value="">모든 버전</option></select>
      <input id="sq" placeholder="세션 검색 (ID · 플랫폼 · 버전)" style="width:240px">
      <label class="chk"><input type="checkbox" id="sonly"> 문제 세션만</label>
      <select id="ssort">
        <option value="date">최신 시작순</option>
        <option value="errors">오류 많은순</option>
        <option value="total">로그 많은순</option>
      </select>
      <span id="scount" class="muted"></span>
    </div>
    <div class="tablewrap">
      <table>
        <thead><tr><th>시작(로컬)</th><th>종료(로컬)</th><th>플랫폼</th><th>버전</th><th>지속</th><th>로그</th><th>경고</th><th>오류</th><th>예외</th><th>상태</th><th>세션 ID</th></tr></thead>
        <tbody id="sbody"></tbody>
      </table>
      <div id="sempty" class="empty" hidden>해당 월에 세션이 없습니다.</div>
    </div>
  </section>

  <section class="card" id="detail" hidden>
    <div class="row">
      <h2 id="dsid"></h2>
      <span class="spacer"></span>
      <button id="dcsv">CSV 내려받기</button>
      <button id="draw">RAW 내려받기</button>
      <button id="ddel" class="danger">세션 삭제</button>
      <button id="dclose">닫기</button>
    </div>
    <div id="dmeta" class="stats"></div>
    <div id="dmetrics"></div>
    <div class="row">
      <label class="chk"><input type="checkbox" id="lv-Info" checked> Info</label>
      <label class="chk"><input type="checkbox" id="lv-Warning" checked> Warning</label>
      <label class="chk"><input type="checkbox" id="lv-Error" checked> Error</label>
      <label class="chk"><input type="checkbox" id="lv-Assert" checked> Assert</label>
      <label class="chk"><input type="checkbox" id="lv-Exception" checked> Exception</label>
      <input id="dcat" placeholder="카테고리" style="width:110px">
      <input id="dscene" placeholder="씬" style="width:110px">
      <input id="dq" placeholder="메시지 · 데이터 · 스택 검색" style="width:200px">
      <button id="dapply">조회</button>
      <span class="spacer"></span>
      <nav>
        <button id="dprev">◀</button>
        <span id="dpos" class="muted"></span>
        <button id="dnext">▶</button>
      </nav>
    </div>
    <div class="tablewrap">
      <table>
        <thead><tr><th>시각(로컬)</th><th>레벨</th><th>카테고리</th><th>씬</th><th style="width:50%">메시지</th></tr></thead>
        <tbody id="ebody"></tbody>
      </table>
      <div id="eempty" class="empty" hidden>조건에 맞는 항목이 없습니다.</div>
    </div>
  </section>
</main>
<script>
"use strict";
const $ = (id) => document.getElementById(id);
const KEY_STORE = "logkit.key";
const VER_STORE = "logkit.sessions.version";
// 세션 화면에서 숨기는 지표 키(쓰지 않는 스캔·소리 발생원 계측)
const HIDDEN_METRICS = new Set(["Scan", "ScanUsed", "SoundSource", "SoundSourceAccessed"]);
const shownMetrics = (list) => (list || []).filter((m) => !HIDDEN_METRICS.has(m.key));
const LIMIT = 500;
const state = { month: null, session: null, offset: 0, matched: 0, version: "" };
try { state.version = localStorage.getItem(VER_STORE) || ""; } catch (_) {}

function esc(s) {
  return String(s ?? "").replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}
function getKey() { return localStorage.getItem(KEY_STORE) || ""; }
function setStatus(text, isErr) {
  const el = $("status");
  el.textContent = text;
  el.className = isErr ? "status err" : "status";
}
async function api(path, opts = {}) {
  opts.headers = Object.assign({ "X-Api-Key": getKey() }, opts.headers || {});
  const res = await fetch(path, opts);
  if (res.status === 401) throw new Error("인증 실패 — API 키를 확인하세요");
  if (!res.ok) throw new Error("HTTP " + res.status + " " + (await res.text()).slice(0, 120));
  return res;
}
function toLocal(ts) {
  const m = String(ts || "").match(/^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?/);
  if (!m) return String(ts || "");
  const ms = m[7] ? Number(m[7].padEnd(3, "0").slice(0, 3)) : 0;
  const d = new Date(Date.UTC(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6], ms));
  return isNaN(d) ? String(ts) : d.toLocaleString("sv-SE");
}
// 종료 시각 = 시작 + 지속 시간. short가 true면 시작과 같은 날일 때 시각만 보인다.
function endLocal(startTs, sec, short) {
  const start = toLocal(startTs);
  const m = String(startTs || "").match(/^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})/);
  if (!m) return "";
  const t = Date.UTC(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6]) + Math.round((Number(sec) || 0) * 1000);
  const end = new Date(t).toLocaleString("sv-SE");
  return short && end.slice(0, 10) === start.slice(0, 10) ? end.slice(11) : end;
}
function fmtDuration(sec) {
  sec = Number(sec) || 0;
  if (sec < 60) return sec.toFixed(1) + "초";
  const m = Math.floor(sec / 60);
  return m + "분 " + Math.round(sec % 60) + "초";
}
function download(path) {
  const sep = path.includes("?") ? "&" : "?";
  location.href = path + sep + "key=" + encodeURIComponent(getKey());
}
function num(n) { return Number(n || 0).toLocaleString(); }
function fmtNum(n) {
  return Number(n || 0).toLocaleString(undefined, { maximumFractionDigits: 2 });
}
function rateText(d) {
  return d.value == null ? "–" : (d.value * 100).toFixed(1) + (d.unit || "%");
}

async function loadMonthMetrics() {
  const el = $("monthmetrics");
  if (!state.month) { el.innerHTML = ""; return; }
  const data = await (await api("/api/metrics?month=" + state.month)).json();
  let html = "";
  for (const d of data.derived) {
    html += '<span class="chip rate"><b>' + esc(d.name) + "</b> " + rateText(d) + "</span>";
  }
  for (const m of shownMetrics(data.metrics).slice(0, 12)) {
    html += '<span class="chip"><b>' + esc(m.key) + "</b> " + num(m.count) + "회"
      + (m.sum ? " · 합계 " + fmtNum(m.sum) : "") + "</span>";
  }
  el.innerHTML = html
    || '<span class="muted">이 달에 집계된 플레이 지표가 없습니다 — 클라이언트에서 GameLogger.Count/Measure를 호출하면 표시됩니다.</span>';
}

async function loadMonths(keep) {
  const data = await (await api("/api/months")).json();
  const sel = $("month");
  sel.innerHTML = "";
  if (data.months.length === 0) {
    const o = document.createElement("option");
    o.textContent = "데이터 없음";
    o.value = "";
    sel.appendChild(o);
  }
  for (const m of data.months) {
    const o = document.createElement("option");
    o.value = m.month;
    o.textContent = m.month + "  (" + num(m.sessions) + "세션)";
    sel.appendChild(o);
  }
  if (keep && data.months.some((m) => m.month === keep)) sel.value = keep;
  state.month = sel.value || null;
  const cur = data.months.find((m) => m.month === state.month);
  $("monthstats").innerHTML = cur
    ? "세션 <b>" + num(cur.sessions) + "</b> · 로그 <b>" + num(cur.total) + "</b>" +
      ' · 경고 <b class="w">' + num(cur.warnings) + "</b>" +
      ' · 오류 <b class="e">' + num(cur.errors) + "</b>" +
      ' · 예외 <b class="x">' + num(cur.exceptions) + "</b>"
    : "요약 데이터가 없습니다. 클라이언트가 로그를 올리면 여기에 집계됩니다.";
}

async function loadSessions() {
  if (!state.month) { $("sbody").innerHTML = ""; $("scount").textContent = ""; return; }
  const p = new URLSearchParams({ month: state.month });
  const ver = state.version;
  if (ver) p.set("version", ver);
  if ($("sq").value.trim()) p.set("q", $("sq").value.trim());
  if ($("sonly").checked) p.set("onlyIssues", "true");
  p.set("sort", $("ssort").value);
  let data = await (await api("/api/sessions?" + p)).json();
  if (ver && !data.versions.includes(ver)) {
    // 저장해 둔 버전이 이 달에 없으면 모든 버전으로 다시 조회
    p.delete("version");
    data = await (await api("/api/sessions?" + p)).json();
  }
  renderVersions(data.versions, data.versions.includes(ver) ? ver : "");
  state.session = null;
  $("detail").hidden = true;
  $("dmetrics").innerHTML = "";
  const body = $("sbody");
  body.innerHTML = "";
  $("sempty").hidden = data.sessions.length > 0;
  $("scount").textContent = num(data.count) + "세션";
  for (const s of data.sessions) {
    const tr = document.createElement("tr");
    tr.className = "click";
    tr.innerHTML =
      "<td>" + esc(toLocal(s.dateUtc)) + "</td>" +
      "<td>" + esc(endLocal(s.dateUtc, s.durationSec, true)) + "</td>" +
      "<td>" + esc(s.platform) + (s.isEditor ? ' <span class="badge ed">에디터</span>' : "") + "</td>" +
      "<td>" + esc(s.appVersion) + "</td>" +
      "<td>" + esc(fmtDuration(s.durationSec)) + "</td>" +
      "<td>" + num(s.total) + "</td>" +
      '<td class="w">' + (s.warnings ? num(s.warnings) : "") + "</td>" +
      '<td class="e">' + (s.errors ? num(s.errors) : "") + "</td>" +
      '<td class="x">' + (s.exceptions ? num(s.exceptions) : "") + "</td>" +
      "<td>" + (s.endedCleanly ? '<span class="badge ok">정상</span>' : '<span class="badge crash">비정상</span>') + "</td>" +
      '<td class="id">' + esc(s.sessionId) + "</td>";
    tr.onclick = () => {
      document.querySelectorAll("#sbody tr").forEach((r) => r.classList.remove("sel"));
      tr.classList.add("sel");
      openSession(s.sessionId);
    };
    body.appendChild(tr);
  }
  await loadMonthMetrics();
}

function renderVersions(versions, current) {
  const sel = $("sver");
  sel.innerHTML = '<option value="">모든 버전</option>' + versions.map((v) =>
    '<option value="' + esc(v) + '">' + esc(v || "(버전 없음)") + "</option>").join("");
  sel.value = current;
}

async function openSession(id) {
  state.session = id;
  state.offset = 0;
  $("detail").hidden = false;
  $("dsid").textContent = id;
  await loadDetail();
}

function detailQuery(offset) {
  const p = new URLSearchParams();
  const levels = ["Info", "Warning", "Error", "Assert", "Exception"]
    .filter((l) => $("lv-" + l).checked);
  if (levels.length > 0 && levels.length < 5) p.set("levels", levels.join(","));
  if ($("dcat").value.trim()) p.set("category", $("dcat").value.trim());
  if ($("dscene").value.trim()) p.set("scene", $("dscene").value.trim());
  if ($("dq").value.trim()) p.set("q", $("dq").value.trim());
  p.set("offset", String(offset));
  p.set("limit", String(LIMIT));
  return p;
}

async function loadDetail() {
  const id = state.session;
  if (!id) return;
  const data = await (await api("/api/sessions/" + encodeURIComponent(id) + "?" + detailQuery(state.offset))).json();
  if (state.session !== id) return;
  state.matched = data.matched;
  $("dmeta").innerHTML =
    "시작 <b>" + esc(toLocal(data.dateUtc)) + "</b> · 종료 <b>" + esc(endLocal(data.dateUtc, data.durationSec, false)) + "</b> · 지속 <b>" + esc(fmtDuration(data.durationSec)) + "</b> · " +
    (data.endedCleanly ? '<span class="badge ok">정상 종료</span>' : '<span class="badge crash">비정상 종료</span>') +
    (data.isEditor ? ' <span class="badge ed">에디터</span>' : "") +
    " · 플랫폼 <b>" + esc(data.platform) + "</b> · 버전 <b>" + esc(data.appVersion) + "</b>" +
    " · 로그 <b>" + num(data.total) + "</b>" +
    ' · 경고 <b class="w">' + num(data.warnings) + "</b>" +
    ' · 오류 <b class="e">' + num(data.errors) + "</b>" +
    ' · 예외 <b class="x">' + num(data.exceptions) + "</b>" +
    (data.hasCsv ? "" : ' · <span class="e">세션 파일 없음(요약만 존재)</span>');
  $("dpos").textContent = data.matched === 0
    ? "0 / 0"
    : (state.offset + 1) + "–" + (state.offset + data.returned) + " / " + num(data.matched);
  $("dprev").disabled = state.offset <= 0;
  $("dnext").disabled = state.offset + data.returned >= data.matched;

  let metricsHtml = "";
  if (data.derived && data.derived.length) {
    metricsHtml += '<div class="row">' + data.derived.map((d) =>
      '<span class="chip rate"><b>' + esc(d.name) + "</b> " + rateText(d) + '</span>').join("") + "</div>";
  }
  const metrics = shownMetrics(data.metrics);
  if (metrics.length) {
    let rows = "";
    for (const m of metrics) {
      rows += "<tr><td>" + esc(m.key) + "</td><td>" + num(m.count) + "</td><td>"
        + (m.count ? fmtNum(m.avg) : "–") + "</td><td>" + fmtNum(m.sum) + "</td></tr>";
    }
    metricsHtml += '<div class="tablewrap" style="max-height:200px"><table>'
      + '<thead><tr><th>지표</th><th>횟수</th><th>평균</th><th>합계</th></tr></thead><tbody>' + rows + "</tbody></table></div>";
  }
  $("dmetrics").innerHTML = metricsHtml;

  const body = $("ebody");
  body.innerHTML = "";
  $("eempty").hidden = data.entries.length > 0;
  for (const e of data.entries) {
    const tr = document.createElement("tr");
    tr.className = "click";
    const lv = (e.level || "info").toLowerCase();
    tr.innerHTML =
      "<td>" + esc(toLocal(e.timestampUtc)) + "</td>" +
      '<td><span class="lv ' + esc(lv) + '">' + esc(e.level) + "</span></td>" +
      "<td>" + esc(e.category) + "</td>" +
      "<td>" + esc(e.scene) + "</td>" +
      '<td class="msg">' + esc(e.message.split("\n")[0]) + "</td>";
    tr.onclick = () => toggleRow(tr, e);
    body.appendChild(tr);
  }
}

function toggleRow(tr, e) {
  const next = tr.nextElementSibling;
  if (next && next.className === "xrow") { next.remove(); return; }
  document.querySelectorAll("#ebody tr.xrow").forEach((r) => r.remove());
  const x = document.createElement("tr");
  x.className = "xrow";
  let html = '<td colspan="5"><span class="xlabel">메시지 전문</span><pre>' + esc(e.message) + "</pre>";
  if (e.dataJson) {
    let pretty = e.dataJson;
    try { pretty = JSON.stringify(JSON.parse(e.dataJson), null, 2); } catch (_) {}
    html += '<span class="xlabel">데이터 (dataJson)</span><pre>' + esc(pretty) + "</pre>";
  }
  if (e.stackTrace) html += '<span class="xlabel">스택트레이스</span><pre>' + esc(e.stackTrace) + "</pre>";
  x.innerHTML = html + "</td>";
  tr.after(x);
}

async function refreshAll() {
  await loadMonths(state.month);
  await loadSessions();
}

async function connect() {
  localStorage.setItem(KEY_STORE, $("key").value.trim());
  try {
    await fetch("/health").then((r) => r.text());
    await refreshAll();
    setStatus("연결됨 — " + (state.month || "데이터 없음"));
  } catch (err) {
    setStatus(String(err.message || err), true);
  }
}

$("connect").onclick = connect;
$("key").addEventListener("keydown", (ev) => { if (ev.key === "Enter") connect(); });
$("refresh").onclick = () => connect();
$("month").onchange = async () => { state.month = $("month").value || null; await loadSessions(); setStatus("연결됨 — " + (state.month || "데이터 없음")); };
$("sq").addEventListener("keydown", (ev) => { if (ev.key === "Enter") loadSessions(); });
$("ssort").onchange = loadSessions;
$("sonly").onchange = loadSessions;
$("sver").onchange = () => {
  state.version = $("sver").value;
  try { localStorage.setItem(VER_STORE, state.version); } catch (_) {}
  loadSessions();
};

$("dapply").onclick = () => { state.offset = 0; loadDetail(); };
$("dq").addEventListener("keydown", (ev) => { if (ev.key === "Enter") { state.offset = 0; loadDetail(); } });
for (const l of ["Info", "Warning", "Error", "Assert", "Exception"]) $("lv-" + l).onchange = () => { state.offset = 0; loadDetail(); };
$("dprev").onclick = () => { state.offset = Math.max(0, state.offset - LIMIT); loadDetail(); };
$("dnext").onclick = () => { state.offset = state.offset + LIMIT; loadDetail(); };
$("dclose").onclick = () => { $("detail").hidden = true; state.session = null; };
$("dcsv").onclick = () => state.session && download("/api/sessions/" + encodeURIComponent(state.session) + "/csv");
$("draw").onclick = () => state.session && download("/api/sessions/" + encodeURIComponent(state.session) + "/raw");
$("ddel").onclick = async () => {
  const id = state.session;
  if (!id || !confirm("세션 " + id + " 의 CSV·raw·요약 행을 모두 삭제합니다. 계속할까요?")) return;
  try {
    await api("/api/sessions/" + encodeURIComponent(id), { method: "DELETE" });
    setStatus("세션 삭제 완료 — " + id);
    await refreshAll();
  } catch (err) { setStatus(String(err.message || err), true); }
};
$("delmonth").onclick = async () => {
  const month = state.month;
  if (!month) return;
  if (!confirm(month + " 의 모든 세션(CSV·raw)과 요약을 삭제합니다. 되돌릴 수 없습니다. 계속할까요?")) return;
  if (prompt("확인하려면 월 키를 입력하세요: " + month) !== month) { setStatus("삭제 취소"); return; }
  try {
    const res = await api("/api/months/" + month, { method: "DELETE" });
    const data = await res.json();
    setStatus("월 삭제 완료 — " + data.month + " (세션 " + data.deletedSessions + "건)");
    await refreshAll();
  } catch (err) { setStatus(String(err.message || err), true); }
};

$("key").value = getKey();
if (getKey()) connect();
</script>
</body>
</html>
""";
}
