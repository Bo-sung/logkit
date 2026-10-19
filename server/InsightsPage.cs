namespace LogServer;

/// <summary>
/// 가설·일반 통계 페이지 HTML. /api/analytics/insights의 세션 기록(일차·주문·습격)으로 개선안 검증용 분포·비율을
/// 화면에서 계산해 그린다. 플레이어 단위는 세션이다. 공통 스타일·스크립트는 PageCommon을 쓴다.
/// </summary>
public static class InsightsPage
{
    /// <summary>가설·일반 통계 페이지 전체 HTML.</summary>
    public const string Html = """
<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>LogKit 가설 통계</title>
<style>
"""
    + PageCommon.Css + """

.q { color: var(--accent); font-size: 11.5px; letter-spacing: .5px; margin-bottom: 2px; }
.scroll { max-height: 560px; overflow: auto; }
.rule { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; margin: 4px 0 8px; font-size: 12px; color: var(--muted); }
.rule input { width: 46px; padding: 2px 4px; text-align: right; }
.rule b { color: var(--text); font-weight: 600; }
.cells svg { display: block; width: 100%; height: auto; }
.cells text { fill: var(--muted); font-size: 11px; font-variant-numeric: tabular-nums; }
.cells text.lbl { fill: var(--text); }
.ramp.rate { background: linear-gradient(90deg, #104281, #3987e5, #cde2fb); }
.basever { display: inline-block; margin-left: 8px; padding: 0 7px; border: 1px solid var(--grid); border-radius: 10px;
  font-size: 11px; font-weight: 400; color: var(--muted); vertical-align: middle; }
.basever:empty { display: none; }
.subhead { margin: 16px 0 4px; font-size: 12.5px; font-weight: 600; color: var(--text); }
</style>
</head>
<body>
<header>
  <div class="brand">LOGKIT<span>HYPOTHESIS STATS</span></div>
  <nav class="pages">
    <a href="/dashboard">GM 툴(세션별 로그)</a>
    <a href="/analytics">버전 통계</a>
    <a href="/weapons">총기 통계</a>
    <b>가설 통계</b>
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
    <div class="row" style="margin-bottom:6px">
      <b>비교할 버전</b><span class="muted">(앱 버전별로 모든 세션 로그를 합산 · 최대 8개 · 클릭해 켜고 끔)</span>
    </div>
    <div id="vchips" class="row"></div>
    <div class="row" style="margin-top:10px">
      <b>기준 버전</b><select id="hver"></select>
      <span class="muted">버전 하나만 그리는 차트(일차 진행, 관통 × 폭발 성공률, 모듈 빈도)에 씁니다. 해당 차트 제목 옆에 표시됩니다.</span>
    </div>
    <p class="sub" style="margin:8px 0 0">플레이어 = 세이브(saveId — 저장 불러오기로 이어 한 세션을 합침, saveId가 없는 이전 로그는 세션) ·
      일차 = 상점 이벤트의 day (0일차 = 튜토리얼) · 습격은 습격 시작의 day(없으면 직전 일차)의 밤 · 1~6일차 고정, 7일차부터 무한 모드</p>
    <p class="sub" id="coverage" style="margin:6px 0 0"></p>
  </section>

  <div class="section">일차 진행 (기준 버전)</div>

  <section class="card wide">
    <div class="row" style="justify-content:space-between">
      <div><h3>n일차 낮·밤 플레이 타임과 이탈률<span class="basever"></span><span class="unitsw" data-unit="churn"></span></h3>
        <p class="sub" style="margin:0">위: 구간 플레이 시간(일시정지 제외) · 아래: 그 구간에서 끝난 플레이어 / 그 구간에 도달한 플레이어 ·
          낮 = 그날 첫 상점 이벤트 → 첫 습격 시작, 밤 = 첫 습격 시작 → 다음 날 첫 상점 이벤트</p></div>
      <div class="row">
        <select id="nightmode">
          <option value="all">밤 = 밤 전체(재도전·선택 화면 포함)</option>
          <option value="raid">밤 = 습격 중 시간만</option>
        </select>
        <label class="chk"><input type="checkbox" id="withended"> 이탈 구간 시간도 포함</label>
      </div>
    </div>
    <div class="legend" style="margin-top:8px"><span><i style="background:#c98500"></i>낮</span><span><i style="background:#3987e5"></i>밤</span></div>
    <div id="c-phase" class="chart"></div>
    <p class="sub" style="margin:6px 0 0">이탈 = 플레이어의 마지막 구간(그 뒤로 같은 세이브의 기록이 없음). 저장을 불러와 이어 했으면 이탈이 아닙니다.
      saveId가 없는 이전 로그는 세션이 끝난 구간을 이탈로 셉니다. 이탈 구간의 시간은 중간에 끊긴 값이라 기본으로 위 분포에서 뺍니다.</p>
  </section>

  <section class="card">
    <div class="row" style="justify-content:space-between">
      <div><h3>n일차 낮·밤 보유 달러<span class="basever"></span></h3>
        <p class="sub" style="margin:0">플레이어-구간 1개 = 1 · 낮 = 영업 시작(shop_opened)의 잔액</p></div>
      <select id="balmode">
        <option value="close">밤 = 영업 종료 요청 때 잔액(수동 종료한 날만)</option>
        <option value="next">밤 = 다음 날 영업 시작 잔액(밤을 마친 뒤)</option>
      </select>
    </div>
    <div id="c-balance" class="chart" style="margin-top:8px"></div>
    <p class="sub" style="margin:6px 0 0">게임이 습격 전후 잔액을 남기지 않아 밤 값은 선택한 근삿값입니다. 습격 시작에 잔액이 기록되면 정확한 값으로 바꿉니다.</p>
  </section>

  <section class="card">
    <h3>n일차 낮·밤 모듈 보유 개수<span class="basever"></span></h3>
    <p class="sub">플레이어-구간 1개 = 1 · 기본 해금 12종 + 그때까지 습격 모듈 상자로 얻은 잠긴 모듈(차지·도탄·물·열·회전·유도·중력) 종류 수 ·
      낮 = 전날 밤까지, 밤 = 그날 밤까지 · saveId가 없는 이전 로그는 세션 안에서만 셉니다</p>
    <div id="c-owned" class="chart"></div>
  </section>

  <div class="section">가설 통계</div>

  <section class="card">
    <div class="q">스테이지 선택 화면의 금고 내성 표기가 준비에 도움이 되는가?</div>
    <div class="row" style="justify-content:space-between">
      <h3>스테이지별 습격 총 관통</h3>
      <select id="vaultmetric">
        <option value="stat">관통 수치 (습격 시작 기록)</option>
        <option value="modules">관통 모듈 개수</option>
      </select>
    </div>
    <p class="sub">습격 1회 = 1 · x축은 스테이지(입장 비용 순)와 현재 씬의 금고 관통 내성(은행 3, 나머지 1 — 기획 대표 값 미정) ·
      관통 수치 = 총구 중 최댓값(방사 절반 반영, 차지 배율 제외) · 모듈 개수는 습격 시작 코드, 없으면 그 습격에서 가장 많이 쏜 구성으로 셈</p>
    <div id="c-vault" class="chart"></div>
  </section>

  <section class="card">
    <div class="q">튜토리얼 행동 가이드 상시 표시가 막힘을 줄이는가?</div>
    <div class="row" style="justify-content:space-between">
      <h3>튜토리얼 진행</h3>
      <div class="row"><span class="muted">시퀀스</span><select id="tutseq"></select></div>
    </div>
    <p class="sub">에디터 단계 넘기기(debugSkipped)가 있는 진행은 뺌 · 가이드 표시 정책 필드가 없어 정책 비교 대신 버전끼리 비교합니다</p>
    <div class="subhead">결과</div>
    <div id="t-tutorial"></div>
    <div class="subhead">단계별 퍼널<span class="unitsw" data-unit="funnel"></span></div>
    <p class="sub">그 단계를 시작한 진행 / 튜토리얼을 시작한 진행 · 건너뛰기는 뺌</p>
    <div id="c-funnel" class="chart scroll"></div>
    <div class="subhead">단계 소요 시간</div>
    <p class="sub">step_completed의 duration(실제 초)</p>
    <div id="c-steptime" class="chart scroll"></div>
  </section>

  <section class="card wide">
    <div class="row" style="justify-content:space-between">
      <div><div class="q">NPC 사격 테스트가 자발적인 모딩 수정을 유도하는가?</div>
        <h3>손님(NPC)별 주문당 수정 횟수</h3>
        <p class="sub" style="margin:0">수정 = 같은 주문에서 구성이 바뀐 작업대 퇴장 · 주문 1건 = 1 · 손님 ID는 게임 손님 에셋 이름 · 표본 2건 이상</p></div>
      <select id="modmetric">
        <option value="builds">전체 수정 횟수</option>
        <option value="buildsAfterTest">시험 사격 입력 뒤 수정 횟수</option>
      </select>
    </div>
    <div id="c-npc" class="chart scroll" style="margin-top:8px"></div>
  </section>

  <section class="card">
    <div class="row" style="justify-content:space-between">
      <div><div class="q">장비 보존·입장료 방식이 실패 후 재도전을 늘리는가?</div>
        <h3>습격 실패 뒤 재도전 비율<span class="unitsw" data-unit="retry"></span></h3>
        <p class="sub" style="margin:0">습격 실패 1회 = 1 · 재도전 = 같은 플레이어가 이후 다시 습격함(저장 불러오기 포함) · 색 = 재도전, 회색 = 재도전 안 함</p></div>
      <select id="retrymode">
        <option value="retried">아무 스테이지나 다시 습격</option>
        <option value="retriedSameStage">같은 스테이지 다시 습격</option>
      </select>
    </div>
    <div id="c-retry" class="chart" style="margin-top:8px"></div>
  </section>

  <section class="card">
    <div class="q">종결 총기가 2~3일차에 너무 일찍 완성되는가?</div>
    <div class="row" style="justify-content:space-between">
      <h3>종결 총기 달성 플레이어 비율<span class="unitsw" data-unit="endgame"></span></h3>
      <select id="endsource">
        <option value="builds">공방 빌드 (손님이 요구한 특성 제외)</option>
        <option value="allbuilds">공방 빌드 전체</option>
        <option value="raids">습격에 들고 간 총</option>
      </select>
    </div>
    <p class="sub">그 일차까지 한 번이라도 기준을 채운 총을 만든(들고 간) 플레이어 / 그 일차에 도달한 플레이어(낮·밤 구간 기준) ·
      손님 주문이 화염방사기·중력총 태그를 요구했으면 그 주문의 빌드는 발열·중력 조건 달성으로 세지 않음(손님 요구로 만든 총이라서) ·
      습격 총은 습격 시작 코드(새 빌드부터)라 이전 로그에는 거의 없음</p>
    <div class="rule" id="rule">
      <b>기준</b>
      관통 ≥ <input data-k="pen" type="number" min="0"> 그리고 폭발 ≥ <input data-k="exp" type="number" min="0">
      그리고 방사 ≥ <input data-k="spread" type="number" min="0"> 그리고 연사 ≥ <input data-k="rate" type="number" min="0">
      <b>또는</b> 발열 ≥ <input data-k="heat" type="number" min="0">
      <b>또는</b> 중력 ≥ <input data-k="grav" type="number" min="0">
      <span>(0 = 그 조건 끔)</span>
    </div>
    <div id="c-endgame" class="chart"></div>
  </section>

  <section class="card">
    <div class="q">전날 총에 따른 적 강화가 적정 난도를 유지하는가?</div>
    <h3>적 대응 시간 변화량 (전날 대비)</h3>
    <p class="sub">대응 시간 = 적 발견 → 처치(게임 초), 플레이어의 그날 습격 평균 · 변화량 = 그날 − 직전 습격 일차 · 0 위면 느려짐</p>
    <div id="c-response" class="chart"></div>
  </section>

  <section class="card">
    <div class="q">관통·폭발 모듈 하향이 과도한 성능과 쏠림을 줄이는가?</div>
    <h3>관통 × 폭발 모듈 수별 습격 성공률<span class="basever"></span></h3>
    <p class="sub">습격 총의 관통(가로)·폭발(세로) 모듈 개수 · 칸 색 = 습격 성공률, 숫자 = 습격 수 ·
      개수는 구간으로 묶음(관통 8+·폭발 11+는 종결 총기 기본 기준)</p>
    <div class="row" style="margin:6px 0"><span class="muted">성공률 0%</span><span class="ramp rate"></span><span class="muted">100%</span></div>
    <div id="c-penexp" class="cells"></div>
  </section>

  <div class="section">일반 통계</div>

  <section class="card">
    <div class="row" style="justify-content:space-between">
      <div><h3>일차별 수익 분포</h3>
        <p class="sub" style="margin:0">플레이어의 그날 수익 · 플레이어-일차 1개 = 1</p></div>
      <select id="revmode">
        <option value="sales">총기 판매 금액</option>
        <option value="all">판매 + 그날 밤 습격 보상</option>
      </select>
    </div>
    <div id="c-revenue" class="chart" style="margin-top:8px"></div>
  </section>

  <section class="card">
    <h3>일차별 NPC 조건 만족 횟수</h3>
    <p class="sub">플레이어가 그날 조건을 만족해 판매한 횟수(satisfied) · 플레이어-일차 1개 = 1</p>
    <div id="c-satisfied" class="chart"></div>
  </section>

  <section class="card">
    <h3>일차별 평균 총기 판매 금액</h3>
    <p class="sub">플레이어의 그날 판매 금액 평균 · 판매가 있는 플레이어-일차만</p>
    <div id="c-price" class="chart"></div>
  </section>

  <section class="card">
    <h3>주문별 작업대 체류 시간</h3>
    <p class="sub">한 주문에서 작업대에 머문 시간 합계(진입→퇴장, 실제 초) · 주문 1건 = 1</p>
    <div id="c-workbench" class="chart"></div>
  </section>

  <section class="card wide">
    <h3>일차별 야간 습격 총기 모듈 빈도<span class="basever"></span><span class="unitsw" data-unit="modules"></span></h3>
    <p class="sub">그 모듈이 들어간 총으로 나간 습격 / 그날 밤 총을 확인한 습격</p>
    <div class="row" style="margin:6px 0"><span class="muted">적음</span><span class="ramp"></span><span class="muted">많음</span></div>
    <div id="c-modules" class="cells"></div>
  </section>

  <div class="section">엔딩</div>

  <section class="card wide">
    <h3>엔딩 요약</h3>
    <p class="sub">은퇴 = Game / retire_completed($100,000 결제), 엔드 상자 = NightRaid / end_chest_opened · 플레이어 = 세이브 ·
      첫 엔딩 = 플레이어가 먼저 본 엔딩 · 계속하기 = 엔딩 화면에서 continue를 고른 비율 · 은퇴 재시도 = 같은 세이브에 은퇴가 2번 이상(은퇴 직전 자동저장을 불러와 다시 함) ·
      엔딩 로그를 넣은 빌드부터 쌓입니다</p>
    <div id="t-ending"></div>
  </section>

  <section class="card">
    <div class="row" style="justify-content:space-between">
      <h3>n일차까지 엔딩을 본 플레이어 비율<span class="unitsw" data-unit="endreach"></span></h3>
      <select id="endtype">
        <option value="any">엔딩 아무거나</option>
        <option value="retire">은퇴</option>
        <option value="end_chest">엔드 상자</option>
      </select>
    </div>
    <p class="sub">그 일차까지 엔딩을 본 플레이어 / 그 일차(낮·밤 구간 또는 상점 기록)에 도달한 플레이어</p>
    <div id="c-endreach" class="chart"></div>
  </section>

  <section class="card">
    <h3>첫 엔딩까지 누적 플레이 시간</h3>
    <p class="sub">플레이어가 그 엔딩을 처음 본 시점까지의 플레이 시간(같은 세이브의 이전 세션 합 + 그 세션 안, 일시정지 제외) ·
      같은 버전의 세션만 합칩니다 · 플레이어 1명 = 1</p>
    <div id="c-endtime" class="chart"></div>
  </section>

  <section class="card">
    <h3>엔드 상자 퍼널<span class="unitsw" data-unit="endfunnel"></span></h3>
    <p class="sub">습격을 한 번이라도 한 플레이어 중 각 단계에 도달한 플레이어 · 스캔 = 조건 확인, 명중 = 조건을 채운 탄이 코어에 맞음,
      열림 = END 화면 시작 · 단계마다 습격당 최대 1회 기록</p>
    <div id="c-endfunnel" class="chart"></div>
  </section>

  <section class="card">
    <h3>엔딩 화면 선택<span class="unitsw" data-unit="endchoice"></span></h3>
    <p class="sub">Game / ending_choice · 비율 = 그 엔딩 화면에서 고른 버튼 / 그 엔딩 화면 선택 수</p>
    <div id="c-endchoice" class="chart"></div>
    <div class="subhead">버튼을 누르기까지 걸린 시간</div>
    <p class="sub">panelSeconds(실시간) · 은퇴는 패널이 열린 때, 엔드 상자는 END 버튼이 다 나타난 때부터</p>
    <div id="c-endpanel" class="chart"></div>
  </section>

  <section class="card">
    <h3>은퇴 시점 자금</h3>
    <p class="sub">retire_completed 1회 = 1 · 결제 전·후 잔액(은퇴 비용 $100,000)과 남은 대출(outstandingLoans)</p>
    <div id="c-retirestate" class="chart"></div>
  </section>

  <section class="card">
    <h3>엔드 상자 조건과 명중 탄 수치</h3>
    <p class="sub">요구 = 스캔 때 확인한 상자 조건(requiredGravity·requiredHoming), 명중 = 조건을 채운 탄의 중력·유도 수치 · 기록 1개 = 1 ·
      명중 탄이 요구보다 크게 높으면 조건이 쉽거나 이미 강한 총으로 왔다는 뜻</p>
    <div id="c-chestnum" class="chart"></div>
  </section>

  <section class="card">
    <h3>엔드 상자 소요 시간</h3>
    <p class="sub">습격 시작 후 게임 초(duration) · 스캔→명중은 같은 플레이어·같은 일차의 스캔과 명중 차 · 흡수 = 명중→열림(absorbSeconds)</p>
    <div id="c-chesttime" class="chart"></div>
  </section>

  <section class="card">
    <h3>습격 종료 사유<span class="unitsw" data-unit="exit"></span></h3>
    <p class="sub">습격 결과 로그의 exitReason · 엔딩 종료 = END 화면에서 그만하기로 로비에 감(보상 미확정, 성공으로 셈) ·
      exitReason이 없는 이전 로그는 뺌</p>
    <div id="c-exit" class="chart"></div>
  </section>

  <div class="section">슬롯머신</div>

  <section class="card wide">
    <h3>슬롯머신 요약</h3>
    <p class="sub">회전 1회 = $500 · 당첨금 = 회전 시작 때 정해진 지급 예정 금액(plannedPrize) · 환급률 = 당첨금 ÷ 지출 ·
      이론 = 그 회전들의 확률표(처음 3회·마지막 판·일반) 기대 당첨금 ÷ 지출 · 이용 세션 = 한 번이라도 돌린 세션 / 상점 기록이 있는 세션 ·
      슬롯머신 로그(slot_machine_spin_started)를 넣은 빌드부터 쌓입니다</p>
    <div id="t-slot"></div>
  </section>

  <section class="card">
    <h3>세션당 슬롯머신 회전 횟수<span class="unitsw" data-unit="slotspins"></span></h3>
    <p class="sub">한 번이라도 돌린 세션만 · 세션 1개 = 1 · 안 돌린 세션 비율은 요약 표의 이용 세션으로 봅니다</p>
    <div id="c-slotspins" class="chart"></div>
  </section>

  <section class="card">
    <h3>플레이어별 손실 대비 이득 비율<span class="unitsw" data-unit="slotrtp"></span></h3>
    <p class="sub">플레이어(세이브)의 당첨금 합 ÷ 지출 합 · 100% 이상이면 본전 이상 · 플레이어 1명 = 1</p>
    <div id="c-slotrtp" class="chart"></div>
  </section>

  <section class="card">
    <h3>플레이어 슬롯머신 순이득</h3>
    <p class="sub">당첨금 − 지출 · 전체 = 플레이어의 모든 회전 합(플레이어 1명 = 1), n일차 = 그날 돌린 플레이어의 그날 합(플레이어-일차 1개 = 1) ·
      평균은 툴팁에 있습니다</p>
    <div id="c-slotnet" class="chart"></div>
  </section>

  <section class="card">
    <h3>일차별 슬롯머신 이용 플레이어 비율<span class="unitsw" data-unit="slotday"></span></h3>
    <p class="sub">그 일차에 한 번이라도 돌린 플레이어 / 그 일차(낮·밤 구간 또는 상점 기록)에 도달한 플레이어</p>
    <div id="c-slotday" class="chart"></div>
  </section>

  <section class="card">
    <h3>확률표별 실제 환급률</h3>
    <p class="sub">oddsPool별 당첨금 ÷ 지출 · 괄호 = 게임 가중치로 계산한 이론 환급률 · 처음 3회 = 저·중 당첨 ×5,
      마지막 판(결제 전 잔액 &lt; $1,000) = 저·중·폭발 ×5 · 회전 수가 적으면 방사($50,000) 한 번에 크게 흔들립니다</p>
    <div id="c-slotpool" class="chart"></div>
  </section>

  <section class="card">
    <h3>당첨 심볼 분포<span class="unitsw" data-unit="slotsym"></span></h3>
    <p class="sub">세 릴이 모두 같을 때만 당첨 · 회전 1회 = 1 · 툴팁의 이론 = 그 회전들의 확률표로 계산한 기대 비율</p>
    <div id="c-slotsym" class="chart"></div>
  </section>

  <section class="card">
    <h3>회전 직전 잔액<span class="unitsw" data-unit="slotbal"></span></h3>
    <p class="sub">결제 전 잔액(balanceBefore) · 회전 1회 = 1 · $1,000 미만은 마지막 판 확률표가 적용된 회전</p>
    <div id="c-slotbal" class="chart"></div>
  </section>

  <section class="card">
    <h3>플레이어별 회전 수 × 순이득</h3>
    <p class="sub">점 1개 = 플레이어 1명(모든 회전 합) · 오른쪽 아래로 갈수록 많이 돌려 많이 잃음 · 점선 = 이론 환급률(일반 98%)로 기대되는 순이득</p>
    <div id="c-slotscatter" class="chart"></div>
  </section>

  <section class="card wide">
    <details>
      <summary>집계 기준 — 게임이 남기는 로그 이벤트</summary>
<pre>일차       Shop 이벤트(order_created, workbench_*, weapon_sold 등)의 day. day가 없는 초기 빌드 로그는 일차 통계에서 빠짐
습격 일차  습격 시작 직전에 본 day (그날 밤 습격)
낮·밤      낮 = day=n인 첫 상점 이벤트 → 그날 첫 습격 시작, 밤 = 첫 습격 시작 → 다른 day의 첫 상점 이벤트.
           시간 = 실제 시각 차 − 일시정지(일시정지 → 다음 이벤트). 습격 중 시간 = 습격 시작 → 종료 합
이탈       플레이어(saveId)의 마지막 구간. saveId가 없는 이전 로그는 세션의 마지막 구간. 정상 종료 여부는 세션 요약의 endedCleanly,
           파산은 Game / bankrupt
플레이어   System / 씬 시작·새 게임 시작·저장 게임 시작의 saveId. 같은 주문(orderId)은 여러 세션에 기록돼도 하나로 합침
튜토리얼   Tutorial / step_started·step_completed(duration, debugSkipped)·tutorial_skipped·tutorial_ended(reason).
           끝 기록 없이 끝난 진행은 마지막으로 시작한 단계에서 멈춘 것으로 봄
수익       Shop / weapon_sold price 합 (+ 선택 시 습격 종료의 moduleCrateCredits + materialCrateCredits)
조건 만족  Shop / weapon_sold satisfied = true 횟수
종결 총기  공방 빌드: Shop / workbench_exited 코드의 파츠. 주문의 requestIds(order_created)에 tag/flamethrower·tag/gravity-gun이
           있으면 그 빌드는 발열·중력 조건에서 뺌. 습격 총: NightRaid / 습격 시작 코드. 도달 = 그 일차 이후 낮·밤 구간 또는 상점 기록
수정 횟수  같은 orderId의 workbench_exited 중 구성(weaponCode)이 직전과 달라진 횟수. 시험 사격 뒤 = 그 주문의 첫 attack_input 이후
손님 ID    order_created / order_restored의 profileId → 게임 Assets/Resources/CustomerOrderData.json guests의 name
체류 시간  같은 orderId의 workbench_entered → workbench_exited 시각 차 합계
습격 총    NightRaid / 습격 시작의 weaponCode. 없으면 그 습격의 weapon_fired 중 가장 많이 쏜 weaponAssemblyId를
           같은 세션에서 코드로 본 구성으로 풀어 씀. 둘 다 없으면 총 미확인
성공       습격 종료 이벤트의 survived
재도전     실패(survived = false)한 습격 뒤 같은 플레이어의 습격이 더 있음. 그 뒤 기록이 없으면 재도전 안 함으로 셈
대응 시간  같은 습격의 적 발견 → 적 처치(enemyId, time). 발견 없이 처치된 적은 뺌
은퇴       Game / retire_completed(결제 직후, 세이브의 첫 은퇴만 — 같은 세이브에 여러 번이면 자동저장을 불러와 재시도), retire_failed(reason)
엔드 상자  NightRaid / end_chest_scanned·end_chest_hit·end_chest_opened(습격당 각 1회). 엔딩 선택 = Game / ending_choice
누적 시간  같은 세이브의 세션별 플레이 시간(행 사이 시간 − 일시정지)을 세션 시작 순으로 더함. 버전이 바뀌면 새로 셈
종료 사유  습격 결과(습격 탈출 성공·습격 사망·습격 엔딩 종료)의 exitReason
슬롯머신   Shop / slot_machine_spin_started(레버를 당겨 결제가 성공한 순간 1회). 당첨금은 plannedPrize(지급 예정)이며
           회전 중 오브젝트가 꺼지면 실제 지급이 안 될 수 있음. 이론 환급률 = 게임 SlotMachineReels 가중치
           (꽝 709 / 저 200 / 중 70 / 폭발 20 / 방사 1, 처음 3회 저·중 ×5, 마지막 판 저·중·폭발 ×5)
박스플롯   상자 = 1~3사분위, 가운데 선 = 중앙값, 수염 = 1.5×IQR 안의 최솟값·최댓값, 점 = 그 밖의 값</pre>
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
const ENDPOINT = "/api/analytics/insights";
const PREF_STORE = "logkit.insights";
const MIN_NPC_ORDERS = 2;
const NEUTRAL = "#484f58";
const RULE_DEFAULT = { pen: 8, exp: 11, spread: 1, rate: 1, heat: 2, grav: 1 };
state.rule = Object.assign({}, RULE_DEFAULT, loadPrefs().rule || {});

// 무한 모드 시작 일차: 게임 DayManager의 고정 일차 목록(customerDays)이 1~6일차라 7일차부터다(Game / endless_started).
const ENDLESS_FROM = 7;
const dayLabel = (d) => d + "일차" + (d >= ENDLESS_FROM ? "·무한" : "");
const money = (x) => "$" + num(x, 0);
const part = (parts, name) => (parts && parts[name]) || 0;
const stageName = (scene) => (state.data.stages.find((s) => s.scene === scene) || {}).name || scene;
const stageVaultLabel = (scene) => {
  const st = state.data.stages.find((s) => s.scene === scene);
  return st ? st.name + " (내성 " + st.vaultPenetration + ")" : scene;
};

/** 선택한 버전들에 나오는 일차 목록(오름차순). */
function daysOf(vs, pick) {
  const set = new Set();
  vs.forEach((v) => pick(v).forEach((x) => { if (x.day != null) set.add(x.day); }));
  return [...set].sort((a, b) => a - b);
}

function quantile(sorted, q) {
  const pos = (sorted.length - 1) * q, lo = Math.floor(pos), hi = Math.ceil(pos);
  return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
}
/** 박스플롯 요약. 수염은 1.5×IQR 안의 극값, 그 밖은 이상값. */
function boxStats(values) {
  const s = values.filter((x) => x != null && isFinite(x)).sort((a, b) => a - b);
  if (!s.length) return null;
  const q1 = quantile(s, 0.25), med = quantile(s, 0.5), q3 = quantile(s, 0.75), iqr = q3 - q1;
  const inside = s.filter((x) => x >= q1 - 1.5 * iqr && x <= q3 + 1.5 * iqr);
  return { n: s.length, q1, med, q3, lo: inside[0], hi: inside[inside.length - 1], out: s.filter((x) => !inside.includes(x)),
    mean: s.reduce((a, b) => a + b, 0) / s.length };
}
/** min~max를 덮는 눈금(5개 안팎). */
function ticks(min, max) {
  if (max === min) { max = min + 1; }
  const raw = (max - min) / 4, p = Math.pow(10, Math.floor(Math.log10(raw)));
  const step = [1, 2, 2.5, 5, 10].map((m) => m * p).find((x) => x >= raw);
  const lo = Math.floor(min / step) * step, hi = Math.ceil(max / step) * step, out = [];
  for (let t = lo; t <= hi + step / 2; t += step) out.push(Math.round(t * 1e6) / 1e6);
  return out;
}
function boxTip(v, cat, b, fmt) {
  return v.version + " · " + cat + "\n표본 " + num(b.n, 0) + "\n최댓값(수염) " + fmt(b.hi) + "\n3사분위 " + fmt(b.q3) +
    "\n중앙값 " + fmt(b.med) + "\n1사분위 " + fmt(b.q1) + "\n최솟값(수염) " + fmt(b.lo) + "\n평균 " + fmt(b.mean) +
    (b.out.length ? "\n이상값 " + b.out.length + "개" : "");
}

/** 세로 묶음 박스플롯. valuesOf(v, cat) → 숫자 배열. zero면 0 기준선을 강조한다. */
function boxV(el, vs, cats, valuesOf, fmt, catLabel = (c) => String(c)) {
  const rows = vs.map((v) => ({ v, boxes: cats.map((c) => boxStats(valuesOf(v, c))) })).filter((r) => r.boxes.some(Boolean));
  if (!rows.length || !cats.length) { el.innerHTML = emptyBox("기록된 데이터가 없습니다."); return; }
  let min = Infinity, max = -Infinity;
  rows.forEach((r) => r.boxes.forEach((b) => { if (b) { min = Math.min(min, b.lo, ...b.out); max = Math.max(max, b.hi, ...b.out); } }));
  min = Math.min(0, min); max = Math.max(0, max);
  const tk = ticks(min, max); min = tk[0]; max = tk[tk.length - 1];
  const W = widthOf(el), H = 260, left = 56, top = 10, bottom = 28, plotW = W - left - 8, plotH = H - top - bottom;
  const sy = (y) => top + plotH - (y - min) / (max - min) * plotH;
  let s = "";
  tk.forEach((t) => {
    s += '<line x1="' + left + '" x2="' + (W - 8) + '" y1="' + sy(t) + '" y2="' + sy(t) + '" stroke="' + (t === 0 ? "var(--axis)" : "var(--grid)") + '"/>';
    s += '<text x="' + (left - 6) + '" y="' + (sy(t) + 4) + '" text-anchor="end">' + esc(fmt(t)) + "</text>";
  });
  const groupW = plotW / cats.length, gap = 4;
  const bw = Math.max(4, Math.min(26, (groupW * 0.8 - gap * (rows.length - 1)) / rows.length));
  const used = bw * rows.length + gap * (rows.length - 1);
  cats.forEach((cat, c) => {
    const gx = left + groupW * c + (groupW - used) / 2;
    rows.forEach((r, i) => {
      const b = r.boxes[c];
      if (!b) return;
      const x = gx + i * (bw + gap), cx = x + bw / 2, col = color(r.v);
      s += '<line x1="' + cx + '" x2="' + cx + '" y1="' + sy(b.hi) + '" y2="' + sy(b.q3) + '" stroke="' + col + '" stroke-width="1.5"/>';
      s += '<line x1="' + cx + '" x2="' + cx + '" y1="' + sy(b.q1) + '" y2="' + sy(b.lo) + '" stroke="' + col + '" stroke-width="1.5"/>';
      s += '<line x1="' + (cx - bw / 4) + '" x2="' + (cx + bw / 4) + '" y1="' + sy(b.hi) + '" y2="' + sy(b.hi) + '" stroke="' + col + '" stroke-width="1.5"/>';
      s += '<line x1="' + (cx - bw / 4) + '" x2="' + (cx + bw / 4) + '" y1="' + sy(b.lo) + '" y2="' + sy(b.lo) + '" stroke="' + col + '" stroke-width="1.5"/>';
      s += '<rect x="' + x + '" y="' + sy(b.q3) + '" width="' + bw + '" height="' + Math.max(1, sy(b.q1) - sy(b.q3)) +
        '" rx="2" fill="' + col + '" fill-opacity="0.35" stroke="' + col + '" stroke-width="1.5"/>';
      s += '<line x1="' + x + '" x2="' + (x + bw) + '" y1="' + sy(b.med) + '" y2="' + sy(b.med) + '" stroke="var(--text)" stroke-width="2"/>';
      b.out.forEach((o) => { s += '<circle cx="' + cx + '" cy="' + sy(o) + '" r="2.5" fill="' + col + '"/>'; });
      s += '<rect x="' + (x - gap / 2) + '" y="' + top + '" width="' + (bw + gap) + '" height="' + plotH +
        '" fill="transparent" data-tip="' + esc(boxTip(r.v, catLabel(cat), b, fmt)) + '"/>';
    });
    s += '<text x="' + (left + groupW * c + groupW / 2) + '" y="' + (H - 8) + '" text-anchor="middle">' + esc(catLabel(cat)) + "</text>";
  });
  el.innerHTML = legend(rows.map((r) => r.v)) + '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

/** 가로 묶음 박스플롯(항목이 많을 때). */
function boxH(el, vs, cats, valuesOf, fmt) {
  const rows = cats.map((c) => ({ c, boxes: vs.map((v) => boxStats(valuesOf(v, c))) })).filter((r) => r.boxes.some(Boolean));
  if (!rows.length) { el.innerHTML = emptyBox("기록된 데이터가 없습니다."); return; }
  let max = 0;
  rows.forEach((r) => r.boxes.forEach((b) => { if (b) max = Math.max(max, b.hi, ...b.out); }));
  const tk = ticks(0, Math.max(1, max)), top = 18;
  max = tk[tk.length - 1];
  const W = widthOf(el), labelW = 190, plotW = W - labelW - 16, bh = 10, gap = 3, pad = 10;
  const rowH = vs.length * bh + (vs.length - 1) * gap + pad, H = top + rows.length * rowH + 4;
  const sx = (x) => labelW + x / max * plotW;
  let s = "";
  tk.forEach((t) => {
    s += '<line x1="' + sx(t) + '" x2="' + sx(t) + '" y1="' + top + '" y2="' + (H - 2) + '" stroke="' + (t === 0 ? "var(--axis)" : "var(--grid)") + '"/>';
    s += '<text x="' + sx(t) + '" y="11" text-anchor="middle">' + esc(fmt(t)) + "</text>";
  });
  rows.forEach((r, k) => {
    const y0 = top + k * rowH + pad / 2;
    s += '<text class="lbl" x="' + (labelW - 8) + '" y="' + (y0 + (rowH - pad) / 2 + 4) + '" text-anchor="end">' + esc(r.c) + "</text>";
    r.boxes.forEach((b, i) => {
      if (!b) return;
      const v = vs[i], col = color(v), y = y0 + i * (bh + gap), cy = y + bh / 2;
      s += '<line x1="' + sx(b.lo) + '" x2="' + sx(b.hi) + '" y1="' + cy + '" y2="' + cy + '" stroke="' + col + '" stroke-width="1.5"/>';
      s += '<rect x="' + sx(b.q1) + '" y="' + y + '" width="' + Math.max(2, sx(b.q3) - sx(b.q1)) + '" height="' + bh +
        '" rx="2" fill="' + col + '" fill-opacity="0.35" stroke="' + col + '" stroke-width="1.5"/>';
      s += '<line x1="' + sx(b.med) + '" x2="' + sx(b.med) + '" y1="' + y + '" y2="' + (y + bh) + '" stroke="var(--text)" stroke-width="2"/>';
      b.out.forEach((o) => { s += '<circle cx="' + sx(o) + '" cy="' + cy + '" r="2.5" fill="' + col + '"/>'; });
      s += '<rect x="' + labelW + '" y="' + (y - gap / 2) + '" width="' + plotW + '" height="' + (bh + gap) +
        '" fill="transparent" data-tip="' + esc(boxTip(v, r.c, b, fmt)) + '"/>';
    });
  });
  el.innerHTML = legend(vs) + '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

/** 일차 × 버전 100% 누적 막대. partsOf(v, day) → { yes, total }. 아래(버전 색) = yes, 위(회색) = 나머지. */
function stacked100(el, vs, days, partsOf, yesName, noName, unitKey) {
  const rows = vs.map((v) => ({ v, cells: days.map((d) => partsOf(v, d)) })).filter((r) => r.cells.some((c) => c.total > 0));
  if (!rows.length || !days.length) { el.innerHTML = emptyBox("기록된 데이터가 없습니다."); return; }
  const W = widthOf(el), H = 250, left = 48, top = 10, bottom = 28, plotW = W - left - 8, plotH = H - top - bottom;
  // 표본 수 모드면 막대 높이 = 건수(같은 축), 아니면 칸마다 100%
  const count = !!unitKey && isCount(unitKey);
  let maxN = 0;
  rows.forEach((r) => r.cells.forEach((c) => { maxN = Math.max(maxN, c.total); }));
  const scaleMax = count ? countMax(maxN) : 1;
  let s = yAxis(W, top, plotH, left, scaleMax, count ? (x) => num(x, 0) : (x) => num(x * 100, 0) + "%");
  const groupW = plotW / days.length, gap = 2;
  const bw = Math.max(4, Math.min(28, (groupW * 0.8 - gap * (rows.length - 1)) / rows.length));
  const used = bw * rows.length + gap * (rows.length - 1);
  days.forEach((d, c) => {
    const gx = left + groupW * c + (groupW - used) / 2;
    rows.forEach((r, i) => {
      const cell = r.cells[c];
      if (!cell.total) return;
      const denom = count ? scaleMax : cell.total;
      const x = gx + i * (bw + gap), share = cell.yes / cell.total, hy = plotH * cell.yes / denom, hAll = plotH * cell.total / denom;
      const yTop = top + plotH - hy;
      if (cell.yes > 0) s += '<path d="' + barUp(x, yTop, bw, hy) + '" fill="' + color(r.v) + '"/>';
      if (cell.yes < cell.total) {
        const hn = hAll - hy - (cell.yes > 0 ? 2 : 0);
        s += '<path d="' + barUp(x, top + plotH - hAll, bw, hn) + '" fill="' + NEUTRAL + '"/>';
      }
      s += '<rect x="' + (x - gap / 2) + '" y="' + top + '" width="' + (bw + gap) + '" height="' + plotH + '" fill="transparent" data-tip="' +
        esc(r.v.version + " · " + dayLabel(d) + "\n" + yesName + " " + pct(share) + " (" + cell.yes + ")\n" + noName + " " +
          pct(1 - share) + " (" + (cell.total - cell.yes) + ")\n실패 " + cell.total + "회") + '"/>';
    });
    s += '<text x="' + (left + groupW * c + groupW / 2) + '" y="' + (H - 8) + '" text-anchor="middle">' + esc(dayLabel(d)) + "</text>";
  });
  const leg = '<div class="legend">' + rows.map((r) => '<span><i style="background:' + color(r.v) + '"></i>' + esc(r.v.version) + " " + esc(yesName) + "</span>").join("") +
    '<span><i style="background:' + NEUTRAL + '"></i>' + esc(noName) + "</span></div>";
  el.innerHTML = leg + '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

/** 칸 히트맵. cell(r, c) → { t(0~1, 색), text, tip } 또는 null. */
function cellmap(el, rowLabels, colLabels, cell, xTitle, yTitle) {
  if (!rowLabels.length || !colLabels.length) { el.innerHTML = emptyBox("기록된 데이터가 없습니다."); return; }
  const W = widthOf(el), labelW = 96, top = 22, bottom = yTitle ? 0 : 0;
  const cw = Math.max(26, Math.min(64, (W - labelW - 8) / colLabels.length)), ch = Math.min(34, Math.max(22, cw * 0.6));
  const H = top + rowLabels.length * ch + 20 + bottom;
  let s = "";
  colLabels.forEach((c, j) => { s += '<text x="' + (labelW + j * cw + cw / 2) + '" y="14" text-anchor="middle">' + esc(c) + "</text>"; });
  rowLabels.forEach((r, i) => {
    const y = top + i * ch;
    s += '<text class="lbl" x="' + (labelW - 8) + '" y="' + (y + ch / 2 + 4) + '" text-anchor="end">' + esc(r) + "</text>";
    colLabels.forEach((c, j) => {
      const v = cell(i, j);
      const x = labelW + j * cw;
      if (!v) { s += '<rect x="' + (x + 1) + '" y="' + (y + 1) + '" width="' + (cw - 2) + '" height="' + (ch - 2) + '" rx="3" fill="#0d1117" stroke="var(--grid)"/>'; return; }
      const rgbv = ramp(v.t), light = rgbv[0] * 0.299 + rgbv[1] * 0.587 + rgbv[2] * 0.114 > 140;
      s += '<rect x="' + (x + 1) + '" y="' + (y + 1) + '" width="' + (cw - 2) + '" height="' + (ch - 2) + '" rx="3" fill="rgb(' + rgbv.join(",") + ')" data-tip="' + esc(v.tip) + '"/>';
      if (cw >= 30) s += '<text x="' + (x + cw / 2) + '" y="' + (y + ch / 2 + 4) + '" text-anchor="middle" style="fill:' + (light ? "#0d1117" : "#e6edf3") + ';pointer-events:none">' + esc(v.text) + "</text>";
    });
  });
  s += '<text x="' + (labelW + colLabels.length * cw / 2) + '" y="' + (H - 4) + '" text-anchor="middle">' + esc(xTitle) + "</text>";
  el.innerHTML = (yTitle ? '<div class="muted" style="font-size:11px;margin-bottom:2px">세로: ' + esc(yTitle) + "</div>" : "") +
    '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

function heatVersion() {
  const vs = selectedVersions();
  const chosen = vs.find((v) => v.version === $("hver").value);
  return chosen || vs[vs.length - 1] || null;
}

// ─── 낮·밤 플레이 타임과 이탈 ───
const SLOTS = [{ night: false, name: "낮", color: "#c98500" }, { night: true, name: "밤", color: "#3987e5" }];
function renderPhase() {
  const el = $("c-phase"), v = heatVersion();
  if (!v) { el.innerHTML = emptyBox("버전을 선택하세요."); return; }
  if (!v.phases.length) { el.innerHTML = emptyBox(v.version + " — 일차 기록이 없습니다."); return; }
  const raidOnly = $("nightmode").value === "raid", withEnded = $("withended").checked;
  const days = [...new Set(v.phases.map((p) => p.day))].sort((a, b) => a - b);
  const timeOf = (p) => p.night && raidOnly ? p.raidSeconds : p.seconds;
  const cells = days.map((d) => SLOTS.map((slot) => {
    const list = v.phases.filter((p) => p.day === d && p.night === slot.night);
    const ended = list.filter((p) => p.ended);
    return {
      box: boxStats(list.filter((p) => withEnded || !p.ended).map(timeOf).filter((x) => x != null).map((x) => x / 60)),
      reached: new Set(list.map((p) => p.s)).size,
      ended: new Set(ended.map((p) => p.s)).size,
      afterFail: ended.filter((p) => p.lastRaid === "fail").length,
      crash: ended.filter((p) => !p.cleanEnd).length,
      bankrupt: ended.filter((p) => p.bankrupt).length,
    };
  }));
  let max = 0;
  cells.flat().forEach((c) => { if (c.box) max = Math.max(max, c.box.hi, ...c.box.out); });
  const tk = ticks(0, Math.max(1, max)), maxY = tk[tk.length - 1];
  const W = widthOf(el), left = 56, right = 8, top = 10, plotW = W - left - right;
  const timeH = 230, gapH = 34, rateH = 140, H = top + timeH + gapH + rateH + 26;
  const rateTop = top + timeH + gapH;
  // 아래 막대: 비율 모드면 이탈률(0~1), 표본 수 모드면 이탈 인원(가장 큰 값 기준 축)
  const churnCount = isCount("churn");
  const churnMax = churnCount ? countMax(Math.max(...cells.flat().map((c) => c.ended))) : 1;
  const sy = (y) => top + timeH - y / maxY * timeH, ry = (r) => rateTop + rateH - r / churnMax * rateH;
  const groupW = plotW / days.length, bw = Math.max(6, Math.min(30, groupW * 0.32)), gap = 4;
  const fmtMin = (x) => num(x, 1) + "분";
  const NL = String.fromCharCode(10);
  let s = "";
  tk.forEach((t) => {
    s += '<line x1="' + left + '" x2="' + (W - right) + '" y1="' + sy(t) + '" y2="' + sy(t) + '" stroke="' + (t === 0 ? "var(--axis)" : "var(--grid)") + '"/>';
    s += '<text x="' + (left - 6) + '" y="' + (sy(t) + 4) + '" text-anchor="end">' + esc(fmtMin(t)) + "</text>";
  });
  [0, 0.25, 0.5, 0.75, 1].map((t) => t * churnMax).forEach((t) => {
    s += '<line x1="' + left + '" x2="' + (W - right) + '" y1="' + ry(t) + '" y2="' + ry(t) + '" stroke="' + (t === 0 ? "var(--axis)" : "var(--grid)") + '"/>';
    s += '<text x="' + (left - 6) + '" y="' + (ry(t) + 4) + '" text-anchor="end">' + (churnCount ? num(t, 0) + "명" : (t * 100) + "%") + "</text>";
  });
  s += '<text x="' + left + '" y="' + (rateTop - 12) + '" class="lbl">' + (churnCount ? "이탈 인원" : "이탈률") + "</text>";
  days.forEach((d, c) => {
    const gx = left + groupW * c + (groupW - (bw * 2 + gap)) / 2;
    SLOTS.forEach((slot, i) => {
      const cell = cells[c][i], b = cell.box, x = gx + i * (bw + gap), cx = x + bw / 2, col = slot.color;
      const label = dayLabel(d) + " " + slot.name;
      if (b) {
        s += '<line x1="' + cx + '" x2="' + cx + '" y1="' + sy(b.hi) + '" y2="' + sy(b.lo) + '" stroke="' + col + '" stroke-width="1.5"/>';
        s += '<rect x="' + x + '" y="' + sy(b.q3) + '" width="' + bw + '" height="' + Math.max(1, sy(b.q1) - sy(b.q3)) +
          '" rx="2" fill="' + col + '" fill-opacity="0.35" stroke="' + col + '" stroke-width="1.5"/>';
        s += '<line x1="' + x + '" x2="' + (x + bw) + '" y1="' + sy(b.med) + '" y2="' + sy(b.med) + '" stroke="var(--text)" stroke-width="2"/>';
        b.out.forEach((o) => { s += '<circle cx="' + cx + '" cy="' + sy(o) + '" r="2.5" fill="' + col + '"/>'; });
        s += '<rect x="' + (x - gap / 2) + '" y="' + top + '" width="' + (bw + gap) + '" height="' + timeH + '" fill="transparent" data-tip="' +
          esc(boxTip({ version: v.version }, label + (slot.night && raidOnly ? " (습격 중)" : ""), b, fmtMin)) + '"/>';
      }
      if (cell.reached) {
        const r = cell.ended / cell.reached, val = churnCount ? cell.ended : r, h = rateH * val / churnMax;
        if (h > 0) s += '<path d="' + barUp(x, ry(val), bw, h) + '" fill="' + col + '"/>';
        if (bw >= 18) s += '<text x="' + cx + '" y="' + (ry(val) - 4) + '" text-anchor="middle">' + (churnCount ? cell.ended : num(r * 100, 0) + "%") + "</text>";
        const lines = [v.version + " · " + label, "이탈률 " + pct(r), "도달 " + cell.reached + "명 · 이탈 " + cell.ended + "명"];
        if (slot.night) lines.push("  그중 습격 실패 직후 " + cell.afterFail + "명");
        lines.push("  그중 비정상 종료 " + cell.crash + "명");
        lines.push("  그중 파산 " + cell.bankrupt + "명");
        s += '<rect x="' + (x - gap / 2) + '" y="' + rateTop + '" width="' + (bw + gap) + '" height="' + rateH +
          '" fill="transparent" data-tip="' + esc(lines.join(NL)) + '"/>';
      }
    });
    s += '<text x="' + (left + groupW * c + groupW / 2) + '" y="' + (H - 8) + '" text-anchor="middle">' + esc(dayLabel(d)) + "</text>";
  });
  el.innerHTML = '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

/** 단일 버전 n일차 낮·밤 박스플롯. valuesOf(day, night) → 숫자 배열. */
function dayNightBox(el, v, days, valuesOf, fmt) {
  const cells = days.map((d) => SLOTS.map((slot) => boxStats(valuesOf(d, slot.night))));
  if (!cells.flat().some(Boolean)) { el.innerHTML = emptyBox(v.version + " — 기록된 데이터가 없습니다."); return; }
  let min = 0, max = 0;
  cells.flat().forEach((b) => { if (b) { min = Math.min(min, b.lo, ...b.out); max = Math.max(max, b.hi, ...b.out); } });
  const tk = ticks(min, Math.max(min + 1, max)); min = tk[0]; max = tk[tk.length - 1];
  const W = widthOf(el), left = 64, right = 8, top = 10, plotH = 220, H = top + plotH + 28, plotW = W - left - right;
  const sy = (y) => top + plotH - (y - min) / (max - min) * plotH;
  const groupW = plotW / days.length, bw = Math.max(6, Math.min(30, groupW * 0.32)), gap = 4;
  let s = "";
  tk.forEach((t) => {
    s += '<line x1="' + left + '" x2="' + (W - right) + '" y1="' + sy(t) + '" y2="' + sy(t) + '" stroke="' + (t === 0 ? "var(--axis)" : "var(--grid)") + '"/>';
    s += '<text x="' + (left - 6) + '" y="' + (sy(t) + 4) + '" text-anchor="end">' + esc(fmt(t)) + "</text>";
  });
  days.forEach((d, c) => {
    const gx = left + groupW * c + (groupW - (bw * 2 + gap)) / 2;
    SLOTS.forEach((slot, i) => {
      const b = cells[c][i], x = gx + i * (bw + gap), cx = x + bw / 2, col = slot.color;
      if (!b) return;
      s += '<line x1="' + cx + '" x2="' + cx + '" y1="' + sy(b.hi) + '" y2="' + sy(b.lo) + '" stroke="' + col + '" stroke-width="1.5"/>';
      s += '<rect x="' + x + '" y="' + sy(b.q3) + '" width="' + bw + '" height="' + Math.max(1, sy(b.q1) - sy(b.q3)) +
        '" rx="2" fill="' + col + '" fill-opacity="0.35" stroke="' + col + '" stroke-width="1.5"/>';
      s += '<line x1="' + x + '" x2="' + (x + bw) + '" y1="' + sy(b.med) + '" y2="' + sy(b.med) + '" stroke="var(--text)" stroke-width="2"/>';
      b.out.forEach((o) => { s += '<circle cx="' + cx + '" cy="' + sy(o) + '" r="2.5" fill="' + col + '"/>'; });
      s += '<rect x="' + (x - gap / 2) + '" y="' + top + '" width="' + (bw + gap) + '" height="' + plotH + '" fill="transparent" data-tip="' +
        esc(boxTip({ version: v.version }, dayLabel(d) + " " + slot.name, b, fmt)) + '"/>';
    });
    s += '<text x="' + (left + groupW * c + groupW / 2) + '" y="' + (H - 8) + '" text-anchor="middle">' + esc(dayLabel(d)) + "</text>";
  });
  el.innerHTML = '<div class="legend"><span><i style="background:#c98500"></i>낮</span><span><i style="background:#3987e5"></i>밤</span></div>' +
    '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

// 기본 해금 모듈 수: 게임 Data/Modules의 initiallyLocked = 0인 모듈(19종 중 12종).
// 잠긴 모듈은 ModuleCatalog ID 10~16(차지·도탄·물·열·회전·유도·중력)이고 모듈 상자로만 해금된다.
const BASE_MODULES = 12, LOCKED_MODULES = new Set([10, 11, 12, 13, 14, 15, 16]);
function renderHoldings() {
  const v = heatVersion();
  if (!v) { $("c-balance").innerHTML = $("c-owned").innerHTML = emptyBox("버전을 선택하세요."); return; }
  const days = [...new Set(v.phases.map((p) => p.day))].sort((a, b) => a - b);
  const next = $("balmode").value === "next";
  const dayBalance = new Map(v.phases.filter((p) => !p.night && p.balance != null).map((p) => [p.s + ":" + p.day, p.balance]));
  dayNightBox($("c-balance"), v, days, (d, night) => v.phases.filter((p) => p.day === d && p.night === night)
    .map((p) => night && next ? dayBalance.get(p.s + ":" + (d + 1)) : p.balance).filter((x) => x != null), money);
  // 플레이어별 습격 보상 모듈(일차를 아는 습격만)
  const rewards = new Map();
  v.raids.forEach((r) => {
    if (r.day == null || !r.rewards) return;
    if (!rewards.has(r.s)) rewards.set(r.s, []);
    rewards.get(r.s).push(r);
  });
  const owned = (s, d, night) => {
    const set = new Set();
    (rewards.get(s) || []).forEach((r) => { if (night ? r.day <= d : r.day < d) r.rewards.forEach((m) => { if (LOCKED_MODULES.has(m)) set.add(m); }); });
    return BASE_MODULES + set.size;
  };
  dayNightBox($("c-owned"), v, days, (d, night) => [...new Set(v.phases.filter((p) => p.day === d && p.night === night).map((p) => p.s))]
    .map((s) => owned(s, d, night)), (x) => num(x, 0) + "종");
}

// ─── 가설 통계 ───
function renderVault(vs) {
  const stat = $("vaultmetric").value === "stat";
  const valueOf = (r) => stat ? r.penetration : r.parts ? part(r.parts, "관통") : null;
  const seen = new Set();
  vs.forEach((v) => v.raids.forEach((r) => { if (valueOf(r) != null) seen.add(r.stage); }));
  const order = state.data.stages.map((s) => s.scene).filter((s) => seen.has(s));
  [...seen].filter((s) => !order.includes(s)).sort().forEach((s) => order.push(s));
  boxV($("c-vault"), vs, order, (v, stage) => v.raids.filter((r) => r.stage === stage).map(valueOf).filter((x) => x != null),
    (x) => num(x, 1) + (stat ? "" : "개"), stageVaultLabel);
}

// ─── 튜토리얼 ───
const OUTCOMES = [["completed", "완료"], ["skipped", "건너뛰기"], ["abandoned", "중단(메뉴·파산)"], ["cancelled", "취소"],
  ["", "기록 없이 종료(앱 종료)"], ["error", "오류"], ["debug_ended", "디버그 종료"]];
function renderTutorial(vs) {
  const all = vs.flatMap((v) => v.tutorials);
  const seqs = [...new Set(all.map((t) => t.sequence))].sort();
  const sel = $("tutseq"), prev = sel.value || loadPrefs().tutseq;
  sel.innerHTML = seqs.map((s) => '<option value="' + esc(s) + '">' + esc(s || "(이름 없음)") + "</option>").join("");
  if (seqs.includes(prev)) sel.value = prev;
  const seq = sel.value;
  if (!seqs.length) {
    $("t-tutorial").innerHTML = emptyBox("튜토리얼 기록이 없습니다. 튜토리얼 로그(Tutorial / step_started 등)를 넣은 빌드부터 쌓입니다.");
    $("c-funnel").innerHTML = ""; $("c-steptime").innerHTML = "";
    return;
  }
  const runsOf = (v) => v.tutorials.filter((t) => t.sequence === seq && !t.debug);
  let rows = "";
  vs.forEach((v) => {
    const runs = runsOf(v);
    if (!runs.length) return;
    rows += "<tr><td>" + esc(v.version) + "</td><td>" + runs.length + "</td>" +
      OUTCOMES.map(([k]) => { const n = runs.filter((t) => t.outcome === k).length; return "<td>" + n + " <span class=\"muted\">" + pct(n / runs.length, 0) + "</span></td>"; }).join("") +
      "<td>" + v.tutorials.filter((t) => t.sequence === seq && t.debug).length + "</td></tr>";
  });
  $("t-tutorial").innerHTML = rows ? '<div class="tablewrap"><table><tr><th>버전</th><th>진행</th>' +
    OUTCOMES.map(([, n]) => "<th>" + esc(n) + "</th>").join("") + "<th>디버그 제외</th></tr>" + rows + "</table></div>" : emptyBox("이 시퀀스의 기록이 없습니다.");
  const steps = new Map();
  vs.forEach((v) => runsOf(v).forEach((t) => t.steps.forEach((s) => steps.set(s.index + "|" + s.id, s))));
  const keys = [...steps.values()].sort((a, b) => a.index - b.index || a.id.localeCompare(b.id)).map((s) => s.index + ". " + s.id);
  const keyOf = (s) => s.index + ". " + s.id;
  hbars($("c-funnel"), vs, keys, (v, key) => {
    const runs = runsOf(v).filter((t) => t.outcome !== "skipped");
    if (!runs.length) return null;
    const n = runs.filter((t) => t.steps.some((s) => keyOf(s) === key)).length;
    return shareBar("funnel", n, runs.length, "회");
  }, isCount("funnel") ? 0 : 1, false, 220);
  boxH($("c-steptime"), vs, keys, (v, key) => runsOf(v).flatMap((t) => t.steps.filter((s) => keyOf(s) === key && s.completed && s.duration != null).map((s) => s.duration)),
    (x) => num(x, 1) + "초");
}

function renderNpc(vs) {
  const field = $("modmetric").value;
  const counts = new Map();
  vs.forEach((v) => v.orders.forEach((o) => { if (o.npc) counts.set(o.npc, (counts.get(o.npc) || 0) + 1); }));
  const npcs = [...counts.keys()].filter((n) => vs.some((v) => v.orders.filter((o) => o.npc === n).length >= MIN_NPC_ORDERS)).sort();
  boxH($("c-npc"), vs, npcs, (v, npc) => {
    const list = v.orders.filter((o) => o.npc === npc);
    return list.length >= MIN_NPC_ORDERS ? list.map((o) => o[field]) : [];
  }, (x) => num(x, 1) + "회");
}

function renderRetry(vs) {
  const field = $("retrymode").value;
  const failed = (v) => v.raids.filter((r) => r.survived === false && r.day != null);
  const days = daysOf(vs, failed);
  stacked100($("c-retry"), vs, days, (v, d) => {
    const list = failed(v).filter((r) => r.day === d);
    return { yes: list.filter((r) => r[field]).length, total: list.length };
  }, "재도전", "재도전 안 함", "retry");
}

// 손님이 요구하면 그 특성의 총을 만들 수밖에 없어, 그 주문의 빌드는 해당 조건 달성에서 뺀다
const ORDER_FORCED = { heat: "tag/flamethrower", grav: "tag/gravity-gun" };
const ENDGAME_NAMES = { main: "관통·폭발 조합", heat: "발열", grav: "중력" };
/** 종결 기준 중 이 총이 채운 조건 목록. requests = 그 빌드를 만든 주문의 손님 요청 ID(없으면 빼지 않음). */
function endgameWhy(parts, requests) {
  const R = state.rule, p = (n) => part(parts, n), req = requests || [], out = [];
  if ((R.pen || R.exp || R.spread || R.rate) && p("관통") >= R.pen && p("폭발") >= R.exp && p("방사") >= R.spread && p("연사") >= R.rate) out.push("main");
  if (R.heat > 0 && p("발열") >= R.heat && !req.includes(ORDER_FORCED.heat)) out.push("heat");
  if (R.grav > 0 && p("중력") >= R.grav && !req.includes(ORDER_FORCED.grav)) out.push("grav");
  return out;
}
/** 일차(범주) × 비율 선 그래프. pointsOf(v) → [{ x: 일차, y: 0~1 또는 null, ... }]. count면 y는 건수이고 축을 건수로 잡는다. */
function dayLines(el, vs, days, pointsOf, tipOf, count) {
  const series = vs.map((v) => ({ v, pts: pointsOf(v).filter((p) => p.y != null) })).filter((s) => s.pts.length);
  if (!series.length || !days.length) { el.innerHTML = emptyBox("기록된 데이터가 없습니다."); return; }
  const W = widthOf(el), H = 250, left = 48, top = 10, bottom = 28, plotW = W - left - 16, plotH = H - top - bottom;
  const maxY = count ? countMax(Math.max(...series.flatMap((x) => x.pts.map((p) => p.y)))) : 1;
  const step = plotW / days.length, sx = (d) => left + step * days.indexOf(d) + step / 2, sy = (y) => top + plotH - y / maxY * plotH;
  let s = yAxis(W, top, plotH, left, maxY, count ? (x) => num(x, 0) + "명" : (x) => num(x * 100, 0) + "%");
  days.forEach((d) => { s += '<text x="' + sx(d) + '" y="' + (H - 8) + '" text-anchor="middle">' + esc(dayLabel(d)) + "</text>"; });
  series.forEach(({ v, pts }) => {
    s += '<polyline fill="none" stroke="' + color(v) + '" stroke-width="2" stroke-linejoin="round" points="' +
      pts.map((p) => sx(p.x).toFixed(1) + "," + sy(p.y).toFixed(1)).join(" ") + '"/>';
  });
  series.forEach(({ v, pts }) => pts.forEach((p) => {
    s += '<circle cx="' + sx(p.x) + '" cy="' + sy(p.y) + '" r="4" fill="' + color(v) + '" stroke="var(--panel)" stroke-width="2"/>';
    s += '<circle cx="' + sx(p.x) + '" cy="' + sy(p.y) + '" r="10" fill="transparent" data-tip="' + esc(tipOf(v, p)) + '"/>';
  }));
  el.innerHTML = legend(series.map((x) => x.v)) + '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

function renderEndgame(vs) {
  const source = $("endsource").value;
  const days = daysOf(vs, (v) => v.phases.concat(v.days));
  /** 플레이어별 [일차, 채운 조건] 목록. */
  const hitsOf = (v) => {
    const out = [];
    if (source === "raids") {
      v.raids.forEach((r) => { if (r.parts && r.day != null) out.push({ s: r.s, day: r.day, why: endgameWhy(r.parts) }); });
    } else {
      v.days.forEach((d) => d.builds.forEach((b, i) =>
        out.push({ s: d.s, day: d.day, why: endgameWhy(b, source === "builds" ? (d.buildRequests || [])[i] : null) })));
    }
    return out.filter((h) => h.why.length);
  };
  dayLines($("c-endgame"), vs, days, (v) => {
    // 도달 = 그 일차 이후 구간(낮·밤) 또는 상점 기록이 있는 플레이어
    const lastDay = new Map();
    v.phases.concat(v.days).forEach((x) => lastDay.set(x.s, Math.max(lastDay.get(x.s) ?? -1, x.day)));
    // 플레이어마다 처음 기준을 채운 일차와 그날 채운 조건
    const first = new Map();
    hitsOf(v).forEach((h) => {
      const f = first.get(h.s);
      if (!f || h.day < f.day) first.set(h.s, { day: h.day, why: new Set(h.why) });
      else if (h.day === f.day) h.why.forEach((w) => f.why.add(w));
    });
    return days.map((day) => {
      const reached = [...lastDay.entries()].filter(([, last]) => last >= day).map(([s]) => s);
      const hit = reached.filter((s) => first.has(s) && first.get(s).day <= day);
      const by = { main: 0, heat: 0, grav: 0 };
      hit.forEach((s) => first.get(s).why.forEach((w) => by[w]++));
      const y = !reached.length ? null : isCount("endgame") ? hit.length : hit.length / reached.length;
      return { x: day, y, rate: reached.length ? hit.length / reached.length : null, hits: hit.length, n: reached.length, by };
    });
  }, (v, p) => v.version + " · " + dayLabel(p.x) + "\n달성 " + pct(p.rate) + " (" + p.hits + " / " + p.n + "명)" +
    (p.hits ? "\n처음 채운 조건: " + Object.keys(ENDGAME_NAMES).filter((k) => p.by[k]).map((k) => ENDGAME_NAMES[k] + " " + p.by[k] + "명").join(", ") : ""), isCount("endgame"));
}

function renderResponse(vs) {
  const deltas = (v) => {
    const per = new Map();
    v.raids.forEach((r) => {
      if (r.day == null || r.responseMean == null || !r.responseCount) return;
      const key = r.s + ":" + r.day;
      const acc = per.get(key) || { s: r.s, day: r.day, sum: 0, n: 0 };
      acc.sum += r.responseMean * r.responseCount; acc.n += r.responseCount;
      per.set(key, acc);
    });
    const bySession = new Map();
    per.forEach((a) => { if (!bySession.has(a.s)) bySession.set(a.s, []); bySession.get(a.s).push(a); });
    const out = [];
    bySession.forEach((list) => {
      list.sort((a, b) => a.day - b.day);
      for (let i = 1; i < list.length; i++) out.push({ day: list[i].day, d: list[i].sum / list[i].n - list[i - 1].sum / list[i - 1].n });
    });
    return out;
  };
  const cache = new Map(vs.map((v) => [v.version, deltas(v)]));
  const days = [...new Set([...cache.values()].flat().map((x) => x.day))].sort((a, b) => a - b);
  boxV($("c-response"), vs, days, (v, d) => cache.get(v.version).filter((x) => x.day === d).map((x) => x.d),
    (x) => (x > 0 ? "+" : "") + num(x, 1) + "초", dayLabel);
}

function renderPenExp() {
  const v = heatVersion();
  const el = $("c-penexp");
  if (!v) { el.innerHTML = emptyBox("버전을 선택하세요."); return; }
  const raids = v.raids.filter((r) => r.parts && r.survived != null);
  if (!raids.length) { el.innerHTML = emptyBox(v.version + " — 총과 결과를 모두 아는 습격이 없습니다."); return; }
  // 구간 시작값. 마지막 구간이 종결 총기 기본 기준(관통 8, 폭발 11)부터 시작하게 묶는다
  const PEN_BINS = [0, 1, 2, 3, 5, 8], EXP_BINS = [0, 1, 2, 3, 5, 8, 11];
  const binOf = (bins, n) => bins.findLastIndex((b) => n >= b);
  const binLabel = (bins, k) => k === bins.length - 1 ? bins[k] + "+" : bins[k + 1] - bins[k] === 1 ? String(bins[k]) : bins[k] + "~" + (bins[k + 1] - 1);
  let maxP = 0, maxE = 0;
  raids.forEach((r) => { maxP = Math.max(maxP, binOf(PEN_BINS, part(r.parts, "관통"))); maxE = Math.max(maxE, binOf(EXP_BINS, part(r.parts, "폭발"))); });
  const cols = [...Array(maxP + 1).keys()], rows = [...Array(maxE + 1).keys()].reverse();
  cellmap(el, rows.map((e) => "폭발 " + binLabel(EXP_BINS, e)), cols.map((p) => binLabel(PEN_BINS, p)), (i, j) => {
    const e = rows[i], p = cols[j];
    const list = raids.filter((r) => binOf(PEN_BINS, part(r.parts, "관통")) === p && binOf(EXP_BINS, part(r.parts, "폭발")) === e);
    if (!list.length) return null;
    const ok = list.filter((r) => r.survived).length, rate = ok / list.length;
    return { t: rate, text: String(list.length),
      tip: v.version + "\n관통 " + binLabel(PEN_BINS, p) + " · 폭발 " + binLabel(EXP_BINS, e) + "\n성공률 " + pct(rate) + " (" + ok + " / " + list.length + "회)" };
  }, "관통 모듈 수", "");
}

// ─── 엔딩 ───
const ENDING_TYPES = [["retire", "은퇴"], ["end_chest", "엔드 상자"]];
const ENDING_KIND = { retire: "retire", end_chest: "chest_opened" };
/** 플레이어별 처음 본 엔딩 기록. type = retire·end_chest·any → Map(s → 기록). */
function firstEndings(v, type) {
  const kinds = type === "any" ? Object.values(ENDING_KIND) : [ENDING_KIND[type]];
  const first = new Map();
  v.endings.filter((e) => kinds.includes(e.kind)).forEach((e) => {
    const f = first.get(e.s);
    if (!f || e.play < f.play) first.set(e.s, e);
  });
  return first;
}

function renderEnding(vs) {
  const evs = vs.filter((v) => v.endings && v.endings.length);
  const ids = ["c-endreach", "c-endtime", "c-endfunnel", "c-endchoice", "c-endpanel", "c-retirestate", "c-chestnum", "c-chesttime", "c-exit"];
  if (!evs.length) {
    $("t-ending").innerHTML = emptyBox("엔딩 기록이 없습니다. 은퇴·엔드 상자 로그(retire_completed, end_chest_*, ending_choice)를 넣은 빌드부터 쌓입니다.");
    ids.forEach((id) => { $(id).innerHTML = ""; });
    return;
  }
  const of = (v, kind) => v.endings.filter((e) => e.kind === kind);
  const players = (list) => new Set(list.map((e) => e.s));

  // 요약 표
  let rows = "";
  evs.forEach((v) => {
    const retire = players(of(v, "retire")), chest = players(of(v, "chest_opened"));
    const any = new Set([...retire, ...chest]), both = [...retire].filter((s) => chest.has(s)).length;
    const first = [...firstEndings(v, "any").values()], firstRetire = first.filter((e) => e.kind === "retire").length;
    const choices = of(v, "choice"), cont = choices.filter((e) => e.choice === "continue").length;
    const retries = [...groupMap(of(v, "retire"), (e) => e.s).values()].filter((l) => l.length > 1).length;
    const failed = of(v, "retire_failed"), reasons = [...groupMap(failed, (e) => e.reason || "(없음)").entries()].map(([k, l]) => k + " " + l.length).join(", ");
    const endless = of(v, "retire").filter((e) => e.endless).length;
    const share = (n, total, unit) => n + (unit || "") + ' <span class="muted">' + pct(ratio(n, total), 0) + "</span>";
    rows += "<tr><td>" + esc(v.version) + "</td><td>" + v.players + "명</td><td>" + share(any.size, v.players, "명") + "</td><td>" +
      share(retire.size, v.players, "명") + "</td><td>" + share(chest.size, v.players, "명") + "</td><td>" + both + "명</td><td>" +
      (first.length ? firstRetire + " : " + (first.length - firstRetire) : "–") + "</td><td>" + share(endless, of(v, "retire").length, "회") +
      "</td><td>" + (choices.length ? share(cont, choices.length, "회") : "–") + "</td><td>" + retries + "명</td><td>" + failed.length + "회" +
      (reasons ? ' <span class="muted">' + esc(reasons) + "</span>" : "") + "</td></tr>";
  });
  $("t-ending").innerHTML = '<div class="tablewrap"><table><tr><th>버전</th><th>플레이어</th><th>엔딩 도달</th><th>은퇴</th><th>엔드 상자</th>' +
    "<th>둘 다</th><th>첫 엔딩 은퇴 : 상자</th><th>무한 모드 은퇴</th><th>엔딩 후 계속하기</th><th>은퇴 재시도</th><th>은퇴 실패</th></tr>" + rows + "</table></div>";

  // n일차까지 엔딩을 본 플레이어 비율
  const type = $("endtype").value, count = isCount("endreach");
  const reachDays = daysOf(evs, (v) => v.phases.concat(v.days));
  dayLines($("c-endreach"), evs, reachDays, (v) => {
    const lastDay = new Map();
    v.phases.concat(v.days).forEach((x) => lastDay.set(x.s, Math.max(lastDay.get(x.s) ?? -1, x.day)));
    const first = firstEndings(v, type);
    return reachDays.map((day) => {
      const reached = [...lastDay.entries()].filter(([, last]) => last >= day).map(([s]) => s);
      const hits = reached.filter((s) => first.has(s) && first.get(s).day != null && first.get(s).day <= day).length;
      return { x: day, y: reached.length ? (count ? hits : hits / reached.length) : null, hits, n: reached.length };
    });
  }, (v, p) => v.version + " · " + dayLabel(p.x) + "\n엔딩 " + pct(p.hits / p.n) + " (" + p.hits + " / " + p.n + "명)", count);

  // 첫 엔딩까지 누적 플레이 시간(분)
  boxV($("c-endtime"), evs, ENDING_TYPES.map(([k]) => k), (v, k) => [...firstEndings(v, k).values()].map((e) => e.play / 60),
    (x) => num(x, 0) + "분", (k) => ENDING_TYPES.find(([t]) => t === k)[1]);

  // 엔드 상자 퍼널
  const FUNNEL = [["chest_scanned", "상자 스캔"], ["chest_hit", "조건 탄 명중"], ["chest_opened", "상자 열림"]];
  const funnelKeys = FUNNEL.map(([, n]) => n).concat(["END 화면에서 로비로"]);
  hbars($("c-endfunnel"), evs, funnelKeys, (v, key) => {
    const raiders = players(v.raids).size;
    if (!raiders) return null;
    const i = funnelKeys.indexOf(key);
    const n = i < FUNNEL.length ? players(of(v, FUNNEL[i][0])).size
      : players(of(v, "choice").filter((e) => e.type === "end_chest" && e.choice === "lobby")).size;
    return shareBar("endfunnel", n, raiders, "명");
  }, isCount("endfunnel") ? 0 : 1, false, 140);

  // 엔딩 화면 선택
  const CHOICES = [["lobby", "로비로"], ["continue", "계속하기"]];
  const choiceKeys = ENDING_TYPES.flatMap(([, tn]) => CHOICES.map(([, cn]) => tn + " → " + cn));
  hbars($("c-endchoice"), evs, choiceKeys, (v, key) => {
    const i = choiceKeys.indexOf(key), t = ENDING_TYPES[Math.floor(i / 2)][0], c = CHOICES[i % 2][0];
    const list = of(v, "choice").filter((e) => e.type === t);
    return list.length ? shareBar("endchoice", list.filter((e) => e.choice === c).length, list.length, "회") : null;
  }, isCount("endchoice") ? 0 : 1, false, 140);
  boxH($("c-endpanel"), evs, choiceKeys, (v, key) => {
    const i = choiceKeys.indexOf(key), t = ENDING_TYPES[Math.floor(i / 2)][0], c = CHOICES[i % 2][0];
    return of(v, "choice").filter((e) => e.type === t && e.choice === c && e.panel != null).map((e) => e.panel);
  }, (x) => num(x, 1) + "초");

  // 은퇴 시점 자금
  const RETIRE_FIELDS = [["balance", "결제 전 잔액"], ["balanceAfter", "결제 후 잔액"], ["loans", "남은 대출"]];
  boxV($("c-retirestate"), evs, RETIRE_FIELDS.map(([k]) => k), (v, k) => of(v, "retire").filter((e) => e[k] != null).map((e) => e[k]),
    money, (k) => RETIRE_FIELDS.find(([f]) => f === k)[1]);

  // 엔드 상자 조건과 명중 탄 수치
  const NUMS = [["chest_scanned", "gravity", "요구 중력"], ["chest_hit", "gravity", "명중 탄 중력"],
    ["chest_scanned", "homing", "요구 유도"], ["chest_hit", "homing", "명중 탄 유도"]];
  boxV($("c-chestnum"), evs, NUMS.map((_, i) => i), (v, i) => of(v, NUMS[i][0]).filter((e) => e[NUMS[i][1]] != null).map((e) => e[NUMS[i][1]]),
    (x) => num(x, 1), (i) => NUMS[i][2]);

  // 엔드 상자 소요 시간
  const TIMES = ["습격 시작→스캔", "스캔→명중", "습격 시작→명중", "흡수(명중→열림)"];
  boxV($("c-chesttime"), evs, TIMES.map((_, i) => i), (v, i) => {
    if (i === 0) return of(v, "chest_scanned").filter((e) => e.raid != null).map((e) => e.raid);
    if (i === 2) return of(v, "chest_hit").filter((e) => e.raid != null).map((e) => e.raid);
    if (i === 3) return of(v, "chest_opened").filter((e) => e.absorb != null).map((e) => e.absorb);
    const scans = new Map(of(v, "chest_scanned").map((e) => [e.s + ":" + e.day, e.raid]));
    return of(v, "chest_hit").filter((e) => e.raid != null && scans.get(e.s + ":" + e.day) != null)
      .map((e) => e.raid - scans.get(e.s + ":" + e.day)).filter((x) => x >= 0);
  }, (x) => num(x, 0) + "초", (i) => TIMES[i]);

  // 습격 종료 사유
  const EXITS = [["extracted", "탈출 성공"], ["died", "사망"], ["ending_quit", "엔딩 종료"]];
  hbars($("c-exit"), evs, EXITS.map(([, n]) => n), (v, key) => {
    const list = v.raids.filter((r) => r.exit);
    if (!list.length) return null;
    const id = EXITS.find(([, n]) => n === key)[0];
    return shareBar("exit", list.filter((r) => r.exit === id).length, list.length, "회");
  }, isCount("exit") ? 0 : 1, false, 110);
}

// ─── 슬롯머신 ───
// 게임 SlotMachineReels.ChooseResult의 가중치. 저 = 기본·연장($1,000), 중 = 연사·관통($2,000), 폭발 $5,000, 방사 $50,000
const SLOT_POOLS = [["first_three", "처음 3회"], ["last_funds", "마지막 판"], ["ordinary", "일반"]];
const SLOT_SYMBOLS = [["none", "꽝", 0], ["기본", "기본", 1000], ["연장", "연장", 1000], ["연사", "연사", 2000], ["관통", "관통", 2000],
  ["폭발", "폭발", 5000], ["방사", "방사", 50000]];
function slotOdds(pool) {
  const intro = pool === "first_three", last = pool === "last_funds", m = intro || last ? 5 : 1;
  const w = { none: 709, low: 200 * m, mid: 70 * m, exp: 20 * (last ? 5 : 1), rad: 1 };
  const total = w.none + w.low + w.mid + w.exp + w.rad;
  const p = { none: w.none, 기본: w.low / 2, 연장: w.low / 2, 연사: w.mid / 2, 관통: w.mid / 2, 폭발: w.exp, 방사: w.rad };
  Object.keys(p).forEach((k) => { p[k] /= total; });
  return { p, ev: (w.low * 1000 + w.mid * 2000 + w.exp * 5000 + w.rad * 50000) / total };
}
const SLOT_ODDS = Object.fromEntries(SLOT_POOLS.map(([k]) => [k, slotOdds(k)]));
const spinEv = (x) => (SLOT_ODDS[x.pool] || SLOT_ODDS.ordinary).ev * x.price / 500;
const sumOf = (list, f) => list.reduce((a, x) => a + f(x), 0);
const signedMoney = (x) => (x > 0 ? "+" : x < 0 ? "−" : "") + money(Math.abs(x));
function groupMap(list, key) {
  const m = new Map();
  list.forEach((x) => { const k = key(x); if (!m.has(k)) m.set(k, []); m.get(k).push(x); });
  return m;
}
function median(list) { const s = list.slice().sort((a, b) => a - b); return s.length ? quantile(s, 0.5) : null; }

function renderSlot(vs) {
  const svs = vs.filter((v) => v.spins && v.spins.length);
  const ids = ["c-slotspins", "c-slotrtp", "c-slotnet", "c-slotday", "c-slotpool", "c-slotsym", "c-slotbal", "c-slotscatter"];
  if (!svs.length) {
    $("t-slot").innerHTML = emptyBox("슬롯머신 기록이 없습니다. 슬롯머신 로그(Shop / slot_machine_spin_started)를 넣은 빌드부터 쌓입니다.");
    ids.forEach((id) => { $(id).innerHTML = ""; });
    return;
  }
  const bySession = (v) => [...groupMap(v.spins, (x) => x.session).values()];
  const byPlayer = (v) => [...groupMap(v.spins, (x) => x.s).values()];
  const net = (list) => sumOf(list, (x) => x.prize - x.price);

  // 요약 표
  let rows = "";
  svs.forEach((v) => {
    const sp = v.spins, sess = bySession(v), players = byPlayer(v), counts = sess.map((l) => l.length);
    const cost = sumOf(sp, (x) => x.price), prize = sumOf(sp, (x) => x.prize), nets = players.map(net);
    const cell = (main, sub) => "<td>" + main + (sub ? ' <span class="muted">' + sub + "</span>" : "") + "</td>";
    rows += "<tr><td>" + esc(v.version) + "</td>" +
      cell(sess.length + " / " + v.shopSessions, pct(ratio(sess.length, v.shopSessions), 0)) +
      cell(num(sp.length, 0) + "회", "") +
      cell(num(avg(counts), 1) + "회", "중앙 " + num(median(counts), 1)) +
      cell(money(cost), "") + cell(money(prize), "") +
      cell(pct(ratio(prize, cost), 0), "이론 " + pct(ratio(sumOf(sp, spinEv), cost), 0)) +
      cell(signedMoney(avg(nets)), "중앙 " + signedMoney(median(nets))) +
      cell(nets.filter((x) => x > 0).length + " / " + players.length + "명", pct(ratio(nets.filter((x) => x > 0).length, players.length), 0)) +
      cell(pct(ratio(sp.filter((x) => x.prize > 0).length, sp.length), 0), "") +
      cell(pct(ratio(sp.filter((x) => x.night).length, sp.length), 0), "") + "</tr>";
  });
  $("t-slot").innerHTML = '<div class="tablewrap"><table><tr><th>버전</th><th>이용 세션</th><th>회전</th><th>세션당 회전</th><th>지출</th>' +
    "<th>당첨금</th><th>환급률</th><th>플레이어 평균 순이득</th><th>이득 본 플레이어</th><th>당첨률</th><th>밤 회전</th></tr>" + rows + "</table></div>";

  // 세션당 회전 횟수
  const SPIN_EDGES = [1, 2, 3, 4, 6, 11, 21, 51];
  const spinCats = SPIN_EDGES.map((e, i) => { const next = SPIN_EDGES[i + 1]; return !next ? e + "+" : next - e === 1 ? String(e) : e + "~" + (next - 1); });
  distribution($("c-slotspins"), svs, (v) => bySession(v).map((l) => l.length), spinCats,
    (n) => SPIN_EDGES.findLastIndex((e) => n >= e), "세션", "slotspins");

  // 플레이어 환급률
  const RTP_EDGES = [0, 0.5, 1, 1.5, 2, 4];
  const rtpCats = ["0% (전부 꽝)", "~50%", "50~100%", "100~150%", "150~200%", "200~400%", "400%+"];
  distribution($("c-slotrtp"), svs, (v) => byPlayer(v).map((l) => sumOf(l, (x) => x.prize) / sumOf(l, (x) => x.price)), rtpCats,
    (r) => r === 0 ? 0 : 1 + RTP_EDGES.slice(1).filter((e) => r >= e).length, "명", "slotrtp");

  // 순이득: 전체(플레이어) + n일차(플레이어-일차)
  const spinDays = daysOf(svs, (v) => v.spins);
  boxV($("c-slotnet"), svs, ["all"].concat(spinDays), (v, c) => c === "all" ? byPlayer(v).map(net)
    : [...groupMap(v.spins.filter((x) => x.day === c), (x) => x.s).values()].map(net), signedMoney,
    (c) => c === "all" ? "전체" : dayLabel(c));

  // 일차별 이용 플레이어 비율
  const count = isCount("slotday");
  const reachDays = daysOf(svs, (v) => v.phases.concat(v.days));
  dayLines($("c-slotday"), svs, reachDays, (v) => {
    const lastDay = new Map();
    v.phases.concat(v.days).forEach((x) => lastDay.set(x.s, Math.max(lastDay.get(x.s) ?? -1, x.day)));
    v.spins.forEach((x) => { if (x.day != null) lastDay.set(x.s, Math.max(lastDay.get(x.s) ?? -1, x.day)); });
    return reachDays.map((day) => {
      const reached = [...lastDay.values()].filter((last) => last >= day).length;
      const used = new Set(v.spins.filter((x) => x.day === day).map((x) => x.s)).size;
      const spins = v.spins.filter((x) => x.day === day).length;
      return { x: day, y: reached ? (count ? used : used / reached) : null, used, reached, spins };
    });
  }, (v, p) => v.version + " · " + dayLabel(p.x) + "\n이용 " + pct(p.used / p.reached) + " (" + p.used + " / " + p.reached + "명)\n회전 " + p.spins + "회", count);

  // 확률표별 환급률
  const poolKeys = SLOT_POOLS.map(([k, n]) => n + " (이론 " + pct(SLOT_ODDS[k].ev / 500, 0) + ")");
  let poolMax = 0;
  const poolCell = (v, i) => {
    const list = v.spins.filter((x) => (x.pool || "ordinary") === SLOT_POOLS[i][0]);
    if (!list.length) return null;
    const cost = sumOf(list, (x) => x.price), prize = sumOf(list, (x) => x.prize), r = prize / cost;
    return { value: r, label: pct(r, 0), tip: "실제 " + pct(r) + " · 이론 " + pct(ratio(sumOf(list, spinEv), cost)) + "\n회전 " + list.length + "회 · 지출 " +
      money(cost) + " · 당첨금 " + money(prize) };
  };
  svs.forEach((v) => SLOT_POOLS.forEach((_, i) => { const c = poolCell(v, i); if (c) poolMax = Math.max(poolMax, c.value); }));
  hbars($("c-slotpool"), svs, poolKeys, (v, key) => poolCell(v, poolKeys.indexOf(key)), Math.max(1, poolMax), false, 170);

  // 당첨 심볼
  const symKeys = SLOT_SYMBOLS.map(([, n, prize]) => prize ? n + " ($" + num(prize, 0) + ")" : n);
  hbars($("c-slotsym"), svs, symKeys, (v, key) => {
    const [id] = SLOT_SYMBOLS[symKeys.indexOf(key)], sp = v.spins;
    const n = sp.filter((x) => (x.symbol || "none") === id).length;
    const theory = sumOf(sp, (x) => (SLOT_ODDS[x.pool] || SLOT_ODDS.ordinary).p[id]) / sp.length;
    const bar = shareBar("slotsym", n, sp.length, "회", 1);
    bar.tip += "\n이론 " + pct(theory, 2);
    return bar;
  }, isCount("slotsym") ? 0 : 1, false, 130);

  // 회전 직전 잔액
  const BAL_EDGES = [0, 1000, 2000, 5000, 10000, 20000];
  const balCats = ["< $1,000", "$1,000~2,000", "$2,000~5,000", "$5,000~10,000", "$10,000~20,000", "$20,000+"];
  distribution($("c-slotbal"), svs, (v) => v.spins.filter((x) => x.before != null).map((x) => x.before), balCats,
    (b) => BAL_EDGES.findLastIndex((e) => b >= e), "회", "slotbal");

  // 회전 수 × 순이득
  scatter($("c-slotscatter"), svs, (v) => byPlayer(v).map((l) => ({ x: l.length, y: net(l), prize: sumOf(l, (x) => x.prize),
    cost: sumOf(l, (x) => x.price) })), (n) => -n * 500 * (1 - SLOT_ODDS.ordinary.ev / 500), (x) => num(x, 0) + "회", signedMoney,
    (v, p) => v.version + "\n회전 " + p.x + "회\n순이득 " + signedMoney(p.y) + "\n지출 " + money(p.cost) + " · 당첨금 " + money(p.prize) +
      "\n환급률 " + pct(ratio(p.prize, p.cost), 0));
}

/** 버전별 (x, y) 산점도. guide(x)를 주면 기대선(점선)을 그린다. y축은 0을 포함한다. */
function scatter(el, vs, pointsOf, guide, xFmt, yFmt, tipOf) {
  const series = vs.map((v) => ({ v, pts: pointsOf(v) })).filter((s) => s.pts.length);
  if (!series.length) { el.innerHTML = emptyBox("기록된 데이터가 없습니다."); return; }
  const all = series.flatMap((s) => s.pts);
  const maxX = niceMax(Math.max(1, ...all.map((p) => p.x)));
  const ty = ticks(Math.min(0, ...all.map((p) => p.y)), Math.max(0, ...all.map((p) => p.y)));
  const minY = ty[0], maxY = ty[ty.length - 1];
  const W = widthOf(el), H = 260, left = 64, top = 10, bottom = 28, plotW = W - left - 16, plotH = H - top - bottom;
  const sx = (x) => left + x / maxX * plotW, sy = (y) => top + plotH - (y - minY) / (maxY - minY) * plotH;
  let s = "";
  ty.forEach((t) => {
    s += '<line x1="' + left + '" x2="' + (W - 16) + '" y1="' + sy(t) + '" y2="' + sy(t) + '" stroke="' + (t === 0 ? "var(--axis)" : "var(--grid)") + '"/>';
    s += '<text x="' + (left - 6) + '" y="' + (sy(t) + 4) + '" text-anchor="end">' + esc(yFmt(t)) + "</text>";
  });
  for (let i = 0; i <= 4; i++) {
    const x = maxX * i / 4;
    s += '<text x="' + sx(x) + '" y="' + (H - 8) + '" text-anchor="middle">' + esc(xFmt(x)) + "</text>";
  }
  if (guide) {
    // 직선 기대선을 그림 영역 안에서 자른다
    const g0 = guide(0), g1 = guide(maxX), y1 = Math.max(minY, Math.min(maxY, g1));
    const x1 = g1 === g0 ? maxX : maxX * Math.min(1, (y1 - g0) / (g1 - g0));
    s += '<line x1="' + sx(0) + '" y1="' + sy(g0) + '" x2="' + sx(x1) + '" y2="' + sy(y1) + '" stroke="var(--muted)" stroke-dasharray="4 4"/>';
  }
  series.forEach(({ v, pts }) => pts.forEach((p) => {
    s += '<circle cx="' + sx(p.x) + '" cy="' + sy(p.y) + '" r="5" fill="' + color(v) + '" fill-opacity="0.85" stroke="var(--panel)" stroke-width="2"/>';
    s += '<circle cx="' + sx(p.x) + '" cy="' + sy(p.y) + '" r="10" fill="transparent" data-tip="' + esc(tipOf(v, p)) + '"/>';
  }));
  el.innerHTML = legend(series.map((x) => x.v)) + '<svg viewBox="0 0 ' + W + " " + H + '">' + s + "</svg>";
}

// ─── 일반 통계 ───
function renderGeneral(vs) {
  const days = daysOf(vs, (v) => v.days);
  const withRaid = $("revmode").value === "all";
  const credits = (v) => {
    const m = new Map();
    v.raids.forEach((r) => { if (r.day != null && r.credits != null) m.set(r.s + ":" + r.day, (m.get(r.s + ":" + r.day) || 0) + r.credits); });
    return m;
  };
  const creditCache = new Map(vs.map((v) => [v.version, credits(v)]));
  boxV($("c-revenue"), vs, days, (v, d) => v.days.filter((x) => x.day === d)
    .map((x) => x.salesSum + (withRaid ? creditCache.get(v.version).get(x.s + ":" + x.day) || 0 : 0)), money, dayLabel);
  boxV($("c-satisfied"), vs, days, (v, d) => v.days.filter((x) => x.day === d).map((x) => x.satisfied), (x) => num(x, 1) + "회", dayLabel);
  boxV($("c-price"), vs, days, (v, d) => v.days.filter((x) => x.day === d && x.priced > 0).map((x) => x.salesSum / x.priced), money, dayLabel);
  const orderDays = daysOf(vs, (v) => v.orders.filter((o) => o.workbenchSeconds != null));
  boxV($("c-workbench"), vs, orderDays, (v, d) => v.orders.filter((o) => o.day === d && o.workbenchSeconds != null).map((o) => o.workbenchSeconds),
    (x) => num(x, 0) + "초", dayLabel);
}

function renderModules() {
  const v = heatVersion();
  const el = $("c-modules");
  if (!v) { el.innerHTML = emptyBox("버전을 선택하세요."); return; }
  const raids = v.raids.filter((r) => r.parts && r.day != null);
  if (!raids.length) { el.innerHTML = emptyBox(v.version + " — 총을 확인한 습격이 없습니다."); return; }
  const days = [...new Set(raids.map((r) => r.day))].sort((a, b) => a - b);
  const freq = new Map();
  raids.forEach((r) => Object.keys(r.parts).forEach((m) => freq.set(m, (freq.get(m) || 0) + 1)));
  const mods = [...freq.keys()].sort((a, b) => freq.get(b) - freq.get(a) || a.localeCompare(b));
  const byDay = new Map(days.map((d) => [d, raids.filter((r) => r.day === d)]));
  const usedOf = (i, j) => byDay.get(days[j]).filter((r) => part(r.parts, mods[i]) > 0).length;
  const count = isCount("modules");
  let maxUsed = 1;
  mods.forEach((_, i) => days.forEach((_, j) => { maxUsed = Math.max(maxUsed, usedOf(i, j)); }));
  cellmap(el, mods, days.map(dayLabel), (i, j) => {
    const list = byDay.get(days[j]), used = usedOf(i, j);
    if (!used) return null;
    const share = used / list.length;
    return { t: count ? used / maxUsed : share, text: count ? String(used) : num(share * 100, 0) + "%",
      tip: v.version + " · " + dayLabel(days[j]) + " · " + mods[i] + "\n" + pct(share) + " (" + used + " / " + list.length + "회 습격)" };
  }, "습격 일차", "");
}

function renderCoverage(vs) {
  const txt = vs.map((v) => {
    const known = v.raids.filter((r) => r.parts).length;
    return v.version + ": 플레이어 " + v.players + "명(세이브 " + v.playersWithSave + ") · 습격 " + v.raids.length + "회 중 총 확인 " + known +
      "회 · 튜토리얼 " + v.tutorials.length + "회 · 주문 " + v.orders.length + "건";
  });
  $("coverage").textContent = txt.join("  |  ");
}

function renderHeatSelect(vs) {
  const sel = $("hver"), prev = sel.value || loadPrefs().hver;
  sel.innerHTML = vs.map((v) => '<option value="' + esc(v.version) + '">' + esc(v.version) + "</option>").join("");
  // 저장된 선택이 없으면 습격 총을 확인한 기록이 가장 많은 버전, 같으면 낮·밤 구간이 많은 버전
  const known = (v) => v.raids.filter((r) => r.parts).length * 100000 + v.phases.length;
  if (vs.some((v) => v.version === prev)) sel.value = prev;
  else if (vs.length) sel.value = vs.reduce((a, b) => known(b) > known(a) ? b : a).version;
  markBaseVersion();
}
/** 단일 버전 차트 제목 옆에 기준 버전을 적는다. */
function markBaseVersion() {
  const v = heatVersion();
  document.querySelectorAll(".basever").forEach((el) => { el.textContent = v ? "기준 " + v.version : ""; });
}

function render() {
  if (!state.data) return;
  const vs = selectedVersions();
  renderHeatSelect(vs);
  renderCoverage(vs);
  renderPhase();
  renderHoldings();
  renderVault(vs);
  renderTutorial(vs);
  renderNpc(vs);
  renderRetry(vs);
  renderEndgame(vs);
  renderResponse(vs);
  renderPenExp();
  renderGeneral(vs);
  renderModules();
  renderEnding(vs);
  renderSlot(vs);
}

document.querySelectorAll("#rule input").forEach((inp) => {
  inp.value = state.rule[inp.dataset.k];
  inp.onchange = () => {
    const n = Math.max(0, Math.floor(Number(inp.value) || 0));
    inp.value = n; state.rule[inp.dataset.k] = n;
    savePrefs({ rule: state.rule });
    if (state.data) renderEndgame(selectedVersions());
  };
});
$("withended").checked = !!loadPrefs().withended;
$("withended").onchange = () => { savePrefs({ withended: $("withended").checked }); renderPhase(); };
$("tutseq").onchange = () => { savePrefs({ tutseq: $("tutseq").value }); renderTutorial(selectedVersions()); };
for (const id of ["modmetric", "retrymode", "revmode", "nightmode", "vaultmetric", "balmode", "endsource", "endtype"]) {
  const saved = loadPrefs()[id];
  if (saved && [...$(id).options].some((o) => o.value === saved)) $(id).value = saved;
  $(id).onchange = () => { savePrefs({ [id]: $(id).value }); render(); };
}
$("hver").onchange = () => { savePrefs({ hver: $("hver").value }); markBaseVersion(); renderPhase(); renderHoldings(); renderPenExp(); renderModules(); };
initPage();
</script>
</body>
</html>
""";
}
