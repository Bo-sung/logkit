namespace LogServer;

/// <summary>
/// 야간 습격 고스트 리플레이 페이지 HTML. /api/analytics/replay의 습격 궤적(0.2초 간격 위치·시선·체력, 발사·피격·처치 시각)을
/// 한 버전·한 스테이지의 모든 습격을 같은 시간축(습격 시작 후 게임 초)에 겹쳐 canvas로 재생한다. 공통 스타일·스크립트는 PageCommon을 쓴다.
/// </summary>
public static class ReplayPage
{
    /// <summary>리플레이 페이지 전체 HTML.</summary>
    public const string Html = """
<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>LogKit 습격 리플레이</title>
<style>
"""
    + PageCommon.Css + """

.stage { position: relative; }
#field { width: 100%; aspect-ratio: 1; display: block; border: 1px solid var(--border); border-radius: 6px; background: #12261b; cursor: crosshair; }
.controls { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.controls input[type=range] { flex: 1; min-width: 200px; accent-color: var(--accent); }
#play { min-width: 74px; font-weight: 600; }
#clock { font-variant-numeric: tabular-nums; min-width: 120px; }
.layout { display: grid; grid-template-columns: minmax(0, 1fr) 340px; gap: 14px; }
@media (max-width: 900px) { .layout { grid-template-columns: 1fr; } }
.list { max-height: 640px; overflow: auto; border: 1px solid var(--border); border-radius: 6px; }
.list table td, .list table th { padding: 4px 8px; }
.list tr.dim td { opacity: .45; }
.list tr.solo td { background: #1f2a3a; }
.list tr { cursor: pointer; }
.dot { display: inline-block; width: 9px; height: 9px; border-radius: 50%; margin-right: 4px; vertical-align: middle; }
.stats { display: flex; gap: 14px; flex-wrap: wrap; font-size: 12px; color: var(--muted); margin-top: 6px; }
.stats b { color: var(--text); font-variant-numeric: tabular-nums; }
.legend i.ring { background: none; border: 2px solid #f85149; border-radius: 50%; }
</style>
</head>
<body>
<header>
  <div class="brand">LOGKIT<span>RAID REPLAY</span></div>
  <nav class="pages">
    <a href="/dashboard">GM 툴(세션별 로그)</a>
    <a href="/analytics">버전 통계</a>
    <a href="/weapons">총기 통계</a>
    <a href="/insights">가설 통계</a>
    <b>습격 리플레이</b>
  </nav>
  <div id="status" class="status"></div>
  <span class="spacer"></span>
  <select id="month"></select>
  <label class="chk"><input type="checkbox" id="noeditor"> 에디터 세션 제외</label>
  <input id="key" type="password" placeholder="API 키" style="width:150px">
  <button id="load">불러오기</button>
</header>
<main>
  <section class="card wide">
    <div class="row">
      <b>버전</b><select id="ver"></select>
      <b>스테이지</b><select id="stage"></select>
      <b>결과</b><select id="outcome">
        <option value="all">전체</option>
        <option value="success">탈출 성공</option>
        <option value="fail">사망</option>
        <option value="ending">엔딩 종료</option>
      </select>
      <b>일차</b><select id="day"></select>
      <span id="info" class="muted"></span>
    </div>
    <p class="sub" style="margin:8px 0 0">그 버전·스테이지의 모든 습격을 습격 시작 시각에 맞춰 겹쳐 재생합니다 · 키 입력 자체는 기록되지 않아
      입력의 결과(위치·시선 방향·체력 0.2초 간격, 발사·피격 위치, 적 처치 시각)로 재현합니다 · 위쪽이 +Z · 최근 습격 300개까지</p>
  </section>

  <section class="card wide">
    <div class="controls">
      <button id="play" type="button">▶ 재생</button>
      <select id="speed">
        <option value="1">1배속</option><option value="2">2배속</option><option value="4" selected>4배속</option>
        <option value="8">8배속</option><option value="16">16배속</option>
      </select>
      <input id="seek" type="range" min="0" max="100" step="0.1" value="0">
      <span id="clock">0:00.0 / 0:00</span>
    </div>
    <div class="controls" style="margin-top:8px">
      <span class="muted">꼬리</span><select id="trail">
        <option value="0">없음</option><option value="5">5초</option><option value="15" selected>15초</option>
        <option value="60">60초</option><option value="-1">전체 경로</option>
      </select>
      <label class="chk"><input type="checkbox" id="showaim" checked> 시선 방향</label>
      <label class="chk"><input type="checkbox" id="showshots" checked> 발사</label>
      <label class="chk"><input type="checkbox" id="showhits" checked> 피격</label>
      <label class="chk"><input type="checkbox" id="showbg" checked> 전체 동선 바탕</label>
      <div class="legend" style="margin:0 0 0 auto">
        <span><i style="background:#58a6ff"></i>탈출 성공</span><span><i style="background:#f0883e"></i>사망</span>
        <span><i style="background:#3fb950"></i>엔딩 종료</span><span><i style="background:#8b949e"></i>결과 미기록</span>
        <span><i style="background:#ffd33d"></i>발사</span><span><i class="ring"></i>피격</span>
      </div>
    </div>
    <div class="stats" id="stats"></div>
  </section>

  <section class="card wide">
    <div class="layout">
      <div class="stage"><canvas id="field" width="900" height="900"></canvas></div>
      <div>
        <div class="row" style="justify-content:space-between;margin-bottom:6px">
          <b>습격 목록</b>
          <span class="muted" style="font-size:12px">클릭 = 그 습격만 강조 · 체크 = 표시</span>
        </div>
        <div class="row" style="margin-bottom:6px">
          <button id="allon" type="button">모두 표시</button><button id="alloff" type="button">모두 숨김</button><button id="nosolo" type="button">강조 해제</button>
        </div>
        <div class="list"><table><thead><tr><th></th><th>세션 시작</th><th>일차</th><th>결과</th><th>길이</th></tr></thead><tbody id="rlist"></tbody></table></div>
      </div>
    </div>
  </section>
</main>
<div id="tip"></div>
<script>
"""
    + PageCommon.Script + """
</script>
<script>
"use strict";
const ENDPOINT = "/api/analytics/replay";
const PREF_STORE = "logkit.replay";
const OUTCOME_COLOR = { success: "#58a6ff", fail: "#f0883e", ending: "#3fb950", unknown: "#8b949e" };
const OUTCOME_NAME = { success: "탈출 성공", fail: "사망", ending: "엔딩 종료", unknown: "미기록" };
const STRIDE = 5;
const rp = { raids: [], t: 0, maxT: 0, playing: false, last: 0, solo: null, bounds: null, bg: null };

const outcomeOf = (r) => r.exit === "ending_quit" ? "ending" : r.survived === true ? "success" : r.survived === false ? "fail" : "unknown";
const clock = (s) => Math.floor(s / 60) + ":" + (s % 60).toFixed(1).padStart(4, "0");

async function fetchReplay(version, stage) {
  const p = new URLSearchParams();
  if ($("month").value) p.set("month", $("month").value);
  if ($("noeditor").checked) p.set("excludeEditor", "true");
  if (version) p.set("version", version);
  if (stage) p.set("stage", stage);
  return api(ENDPOINT + "?" + p);
}

async function load() {
  setKey($("key").value.trim());
  setStatus("불러오는 중…");
  try {
    await loadMonths();
    savePrefs({ month: $("month").value, noEditor: $("noeditor").checked });
    const prefs = loadPrefs();
    const data = await fetchReplay(prefs.ver, prefs.stage);
    applyData(data);
  } catch (err) { setStatus(String(err.message || err), true); }
}

function applyData(data) {
  state.data = data;
  $("ver").innerHTML = data.versions.map((v) => '<option value="' + esc(v.version) + '">' + esc(v.version) + "</option>").join("");
  $("ver").value = data.version;
  const vs = data.versions.find((v) => v.version === data.version);
  $("stage").innerHTML = (vs ? vs.stages : []).map((s) => '<option value="' + esc(s.scene) + '">' + esc(s.scene) + " (" + s.raids + "회)</option>").join("");
  $("stage").value = data.stage;
  rp.raids = data.raids.map((r, i) => prepare(r, i));
  const days = [...new Set(rp.raids.map((r) => r.day).filter((d) => d != null))].sort((a, b) => a - b);
  const prevDay = $("day").value;
  $("day").innerHTML = '<option value="all">전체</option>' + days.map((d) => '<option value="' + d + '">' + d + "일차</option>").join("");
  if ([...$("day").options].some((o) => o.value === prevDay)) $("day").value = prevDay;
  rp.solo = null;
  rp.t = 0;
  rp.bounds = boundsOf(rp.raids);
  rp.bg = null;
  setStatus(data.version ? data.version + " · " + data.stage + " · 습격 " + rp.raids.length + "회" + (data.truncated ? " (최근 300회)" : "")
    : "리플레이할 습격 궤적이 없습니다");
  refilter();
}

/** 습격 하나를 그리기 쉬운 형태로 바꾼다. */
function prepare(r, i) {
  const n = r.track.length / STRIDE;
  const end = n ? r.track[(n - 1) * STRIDE] : 0;
  return { i, raw: r, day: r.day, outcome: outcomeOf(r), track: Float32Array.from(r.track), n, end,
    shots: Float32Array.from(r.shots), hits: Float32Array.from(r.hits), kills: r.kills, on: true,
    label: toLocal(r.dateUtc) + " #" + r.index, short: toLocal(r.dateUtc).slice(5, 16) + " #" + r.index };
}

function boundsOf(raids) {
  let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
  raids.forEach((r) => { for (let k = 0; k < r.n; k++) {
    const x = r.track[k * STRIDE + 1], z = r.track[k * STRIDE + 2];
    if (x < minX) minX = x; if (x > maxX) maxX = x; if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
  } });
  if (!isFinite(minX)) return null;
  // 정사각형 축척, 가장자리 6% 여백
  const span = Math.max(maxX - minX, maxZ - minZ, 1) * 1.12, cx = (minX + maxX) / 2, cz = (minZ + maxZ) / 2;
  return { minX: cx - span / 2, minZ: cz - span / 2, span };
}

function visible() {
  const out = $("outcome").value, day = $("day").value;
  return rp.raids.filter((r) => (out === "all" || r.outcome === out) && (day === "all" || String(r.day) === day));
}

function refilter() {
  const list = visible();
  rp.maxT = Math.max(1, ...list.map((r) => r.end));
  $("seek").max = String(rp.maxT);
  rp.t = Math.min(rp.t, rp.maxT);
  rp.bg = null;
  const body = $("rlist");
  body.innerHTML = list.map((r) => '<tr data-i="' + r.i + '" class="' + (r.on ? "" : "dim") + (rp.solo === r.i ? " solo" : "") + '">' +
    '<td><input type="checkbox" data-on="' + r.i + '"' + (r.on ? " checked" : "") + "></td>" +
    "<td title=\"" + esc(r.raw.sessionId) + "\">" + esc(r.short) + "</td><td>" + (r.day != null ? r.day : "–") + "</td>" +
    '<td><span class="dot" style="background:' + OUTCOME_COLOR[r.outcome] + '"></span>' + OUTCOME_NAME[r.outcome] + "</td>" +
    "<td>" + clock(r.end) + "</td></tr>").join("") || '<tr><td colspan="5" class="muted">조건에 맞는 습격이 없습니다</td></tr>';
  $("info").textContent = "표시 " + list.filter((r) => r.on).length + " / " + list.length + "회";
  draw();
}

// ─── 좌표 · 보간 ───
function toCanvas(x, z, W) {
  const b = rp.bounds;
  return [(x - b.minX) / b.span * W, W - (z - b.minZ) / b.span * W];
}
/** 시각 t의 표본 위치(이분 탐색 + 선형 보간). t가 끝을 넘으면 마지막 표본, 시작 전이면 null. */
function sampleAt(r, t) {
  const tr = r.track;
  if (!r.n || t < tr[0]) return null;
  if (t >= r.end) { const k = (r.n - 1) * STRIDE; return { x: tr[k + 1], z: tr[k + 2], yaw: tr[k + 3], hp: tr[k + 4], k: r.n - 1, done: true }; }
  let lo = 0, hi = r.n - 1;
  while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (tr[mid * STRIDE] <= t) lo = mid; else hi = mid; }
  const a = lo * STRIDE, b = hi * STRIDE, f = (t - tr[a]) / Math.max(1e-6, tr[b] - tr[a]);
  let dy = tr[b + 3] - tr[a + 3];
  if (dy > 180) dy -= 360; else if (dy < -180) dy += 360;
  return { x: tr[a + 1] + (tr[b + 1] - tr[a + 1]) * f, z: tr[a + 2] + (tr[b + 2] - tr[a + 2]) * f, yaw: tr[a + 3] + dy * f, hp: tr[a + 4], k: lo, done: false };
}

/** 표시 중인 모든 습격의 전체 동선을 옅은 바탕으로 한 번 그려 둔다. */
function background(W) {
  const cv = document.createElement("canvas");
  cv.width = W; cv.height = W;
  const ctx = cv.getContext("2d");
  ctx.strokeStyle = "rgba(145,207,96,0.10)";
  ctx.lineWidth = 3;
  ctx.lineCap = "round";
  visible().filter((r) => r.on).forEach((r) => {
    ctx.beginPath();
    for (let k = 0; k < r.n; k++) {
      const [px, py] = toCanvas(r.track[k * STRIDE + 1], r.track[k * STRIDE + 2], W);
      if (k) ctx.lineTo(px, py); else ctx.moveTo(px, py);
    }
    ctx.stroke();
  });
  return cv;
}

// ─── 그리기 ───
function draw() {
  const cv = $("field"), W = Math.round(cv.clientWidth * (window.devicePixelRatio || 1)) || 900;
  if (cv.width !== W) { cv.width = W; cv.height = W; rp.bg = null; }
  const ctx = cv.getContext("2d");
  ctx.clearRect(0, 0, W, W);
  // 표시 크기 배율: 작은 화면에서도 점·선이 너무 작아지지 않게 하한을 둔다
  const sc = Math.max(0.75, W / 900);
  // 4×4 보조선
  ctx.strokeStyle = "rgba(255,255,255,0.07)";
  ctx.lineWidth = 1;
  for (let k = 1; k < 4; k++) { const p = Math.round(W * k / 4) + 0.5; ctx.beginPath(); ctx.moveTo(p, 0); ctx.lineTo(p, W); ctx.moveTo(0, p); ctx.lineTo(W, p); ctx.stroke(); }
  if (!rp.bounds) { updateHud([]); return; }
  if ($("showbg").checked) { if (!rp.bg) rp.bg = background(W); ctx.drawImage(rp.bg, 0, 0); }
  const t = rp.t, trail = Number($("trail").value), list = visible().filter((r) => r.on);
  const alive = [];
  list.forEach((r) => {
    const s = sampleAt(r, t);
    if (!s) return;
    const dim = rp.solo != null && rp.solo !== r.i, col = OUTCOME_COLOR[r.outcome];
    ctx.globalAlpha = dim ? 0.15 : 1;
    // 꼬리
    if (trail !== 0) {
      const from = trail < 0 ? 0 : t - trail;
      ctx.strokeStyle = col;
      ctx.lineWidth = (rp.solo === r.i ? 3 : 1.6) * sc;
      ctx.globalAlpha = dim ? 0.08 : 0.55;
      ctx.beginPath();
      let started = false;
      for (let k = 0; k <= s.k; k++) {
        const kt = r.track[k * STRIDE];
        if (kt < from) continue;
        const [px, py] = toCanvas(r.track[k * STRIDE + 1], r.track[k * STRIDE + 2], W);
        if (started) ctx.lineTo(px, py); else { ctx.moveTo(px, py); started = true; }
      }
      const [cx, cy] = toCanvas(s.x, s.z, W);
      if (started) ctx.lineTo(cx, cy);
      ctx.stroke();
      ctx.globalAlpha = dim ? 0.15 : 1;
    }
    // 발사(최근 0.6초)·피격(최근 1.2초)
    if ($("showshots").checked) {
      ctx.fillStyle = "#ffd33d";
      for (let k = 0; k < r.shots.length; k += 3) {
        const age = t - r.shots[k];
        if (age < 0 || age > 0.6) continue;
        const [px, py] = toCanvas(r.shots[k + 1], r.shots[k + 2], W);
        ctx.globalAlpha = (dim ? 0.15 : 1) * (1 - age / 0.6);
        ctx.beginPath(); ctx.arc(px, py, 4 * sc, 0, Math.PI * 2); ctx.fill();
      }
    }
    if ($("showhits").checked) {
      ctx.strokeStyle = "#f85149";
      ctx.lineWidth = 2.5 * sc;
      for (let k = 0; k < r.hits.length; k += 3) {
        const age = t - r.hits[k];
        if (age < 0 || age > 1.2) continue;
        const [px, py] = toCanvas(r.hits[k + 1], r.hits[k + 2], W);
        ctx.globalAlpha = (dim ? 0.15 : 1) * (1 - age / 1.2);
        ctx.beginPath(); ctx.arc(px, py, (8 + age * 14) * sc, 0, Math.PI * 2); ctx.stroke();
      }
    }
    ctx.globalAlpha = dim ? 0.15 : 1;
    const [px, py] = toCanvas(s.x, s.z, W);
    if (s.done) {
      // 끝난 습격: 사망은 ✕, 그 밖은 빈 원
      ctx.strokeStyle = col;
      ctx.lineWidth = 2 * sc;
      ctx.beginPath();
      if (r.outcome === "fail") { const d = 5 * sc; ctx.moveTo(px - d, py - d); ctx.lineTo(px + d, py + d); ctx.moveTo(px + d, py - d); ctx.lineTo(px - d, py + d); }
      else ctx.arc(px, py, 5 * sc, 0, Math.PI * 2);
      ctx.stroke();
    } else {
      alive.push({ r, s, px, py });
      if ($("showaim").checked) {
        // 시선: Unity yaw(도) → +Z 기준 시계 방향
        const a = s.yaw * Math.PI / 180, len = 16 * sc;
        ctx.strokeStyle = col;
        ctx.lineWidth = 2 * sc;
        ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(px + Math.sin(a) * len, py - Math.cos(a) * len); ctx.stroke();
      }
      ctx.fillStyle = col;
      ctx.strokeStyle = "#0d1117";
      ctx.lineWidth = 2 * sc;
      ctx.beginPath(); ctx.arc(px, py, (rp.solo === r.i ? 7 : 5) * sc, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
    }
  });
  ctx.globalAlpha = 1;
  rp.alive = alive;
  updateHud(list);
}

function updateHud(list) {
  const t = rp.t;
  $("seek").value = String(t);
  $("clock").textContent = clock(t) + " / " + clock(rp.maxT);
  const started = list.filter((r) => r.n && r.track[0] <= t);
  const running = started.filter((r) => t < r.end).length;
  const ended = (o) => started.filter((r) => t >= r.end && r.outcome === o).length;
  const count = (key) => list.reduce((a, r) => { let n = 0; const arr = r[key], step = key === "kills" ? 1 : 3; for (let k = 0; k < arr.length; k += step) if (arr[k] <= t) n++; return a + n; }, 0);
  $("stats").innerHTML = "진행 중 <b>" + running + "</b> · 탈출 <b>" + ended("success") + "</b> · 사망 <b>" + ended("fail") + "</b> · 엔딩 <b>" +
    ended("ending") + "</b> · 지금까지 발사 <b>" + num(count("shots"), 0) + "</b> · 피격 <b>" + num(count("hits"), 0) + "</b> · 적 처치 <b>" + num(count("kills"), 0) + "</b>";
}

// ─── 재생 ───
function tick(now) {
  if (!rp.playing) return;
  const dt = (now - rp.last) / 1000;
  rp.last = now;
  rp.t = Math.min(rp.maxT, rp.t + dt * Number($("speed").value));
  draw();
  if (rp.t >= rp.maxT) { setPlaying(false); return; }
  requestAnimationFrame(tick);
}
function setPlaying(on) {
  rp.playing = on;
  $("play").textContent = on ? "❚❚ 정지" : "▶ 재생";
  if (on) {
    if (rp.t >= rp.maxT) rp.t = 0;
    rp.last = performance.now();
    requestAnimationFrame(tick);
  }
}

$("play").onclick = () => setPlaying(!rp.playing);
$("seek").oninput = () => { rp.t = Number($("seek").value); draw(); };
for (const id of ["trail", "showaim", "showshots", "showhits", "speed"]) $(id).onchange = draw;
$("showbg").onchange = () => { rp.bg = null; draw(); };
$("outcome").onchange = refilter;
$("day").onchange = refilter;
$("ver").onchange = async () => {
  savePrefs({ ver: $("ver").value, stage: "" });
  setPlaying(false);
  try { applyData(await fetchReplay($("ver").value, "")); } catch (err) { setStatus(String(err.message || err), true); }
};
$("stage").onchange = async () => {
  savePrefs({ ver: $("ver").value, stage: $("stage").value });
  setPlaying(false);
  try { applyData(await fetchReplay($("ver").value, $("stage").value)); } catch (err) { setStatus(String(err.message || err), true); }
};
$("rlist").onclick = (ev) => {
  const box = ev.target.closest("[data-on]");
  if (box) { const r = rp.raids[+box.dataset.on]; r.on = box.checked; if (!r.on && rp.solo === r.i) rp.solo = null; refilter(); return; }
  const tr = ev.target.closest("tr[data-i]");
  if (!tr) return;
  const i = +tr.dataset.i;
  rp.solo = rp.solo === i ? null : i;
  rp.raids[i].on = true;
  refilter();
};
$("allon").onclick = () => { visible().forEach((r) => { r.on = true; }); refilter(); };
$("alloff").onclick = () => { visible().forEach((r) => { r.on = false; }); rp.solo = null; refilter(); };
$("nosolo").onclick = () => { rp.solo = null; refilter(); };
// 점 위에 올리면 그 습격 정보
function hitAt(ev) {
  const cv = $("field"), rect = cv.getBoundingClientRect(), k = cv.width / rect.width;
  const mx = (ev.clientX - rect.left) * k, my = (ev.clientY - rect.top) * k;
  return (rp.alive || []).find((a) => Math.hypot(a.px - mx, a.py - my) < 10 * k);
}
$("field").addEventListener("mousemove", (ev) => {
  const hit = hitAt(ev), tipEl = $("tip");
  if (!hit) { tipEl.style.display = "none"; return; }
  tipEl.textContent = hit.r.label + "\n" + (hit.r.day != null ? hit.r.day + "일차 · " : "") + OUTCOME_NAME[hit.r.outcome] +
    "\n체력 " + num(hit.s.hp, 1) + " · X " + num(hit.s.x, 1) + " Z " + num(hit.s.z, 1);
  tipEl.style.display = "block";
  tipEl.style.left = (ev.clientX + 14) + "px";
  tipEl.style.top = (ev.clientY + 14) + "px";
});
$("field").addEventListener("mouseleave", () => { $("tip").style.display = "none"; });
// 점을 누르면 그 습격만 강조(다시 누르거나 빈 곳을 누르면 해제)
$("field").addEventListener("click", (ev) => {
  const hit = hitAt(ev);
  rp.solo = hit && rp.solo !== hit.r.i ? hit.r.i : null;
  refilter();
});

state.units = {};
$("load").onclick = load;
$("key").addEventListener("keydown", (ev) => { if (ev.key === "Enter") load(); });
$("month").onchange = load;
$("noeditor").onchange = load;
let resizeTimer = 0;
window.addEventListener("resize", () => { clearTimeout(resizeTimer); resizeTimer = setTimeout(() => { rp.bg = null; draw(); }, 150); });
$("noeditor").checked = !!loadPrefs().noEditor;
$("key").value = getKey();
if (getKey()) load(); else setStatus("API 키를 입력하고 불러오세요.");
</script>
</body>
</html>
""";
}
