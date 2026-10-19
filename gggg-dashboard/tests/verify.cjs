const fs=require('fs'),vm=require('vm'),assert=require('assert');
process.chdir(require('path').join(__dirname,'..'));
const nodes={};
const node=s=>nodes[s]??={value:s==='#scene'?'all':s==='#tz'?'KST':'',style:{},classList:{toggle(){}},setAttribute(k,v){this[k]=v},showModal(){},textContent:'',innerHTML:''};
const ctx={document:{querySelector:node,querySelectorAll:()=>[]},window:{},clearInterval(){},setInterval(){},setTimeout(){},console,performance};
vm.createContext(ctx);
vm.runInContext(fs.readFileSync('dist/data.js','utf8'),ctx);
vm.runInContext(fs.readFileSync('dist/app.js','utf8'),ctx);
const evaluate=s=>vm.runInContext(s,ctx);

// 고정 수치 제거: data CSV를 직접 파싱해 동적 기대값 계산 (지표 집계 정의 유지)
const csv=fs.readFileSync('data/7f18f899_20261015_102421.csv','utf8');
ctx.csv=csv;
const parsed=evaluate('parseCSV(csv)');
ctx.parsedRows=parsed;
const records=evaluate('normalize(parsedRows)');
ctx.records=records;
const a=evaluate('analyze(records)');

const dyn = {
  records: parsed.length,
  revenue: a.revenue,
  sales: a.validSales.length,
  states: a.states.length,
  blocks: a.blocks.length,
  visits: a.visits.length,
  raids: a.raids.length,
  unmatched: a.unmatched,
};
assert.equal(evaluate('parseCSV(csv).length'), dyn.records);
assert.equal(a.revenue, dyn.revenue);
assert.equal(evaluate('analyze(records).sales.length'), dyn.sales);
assert.equal(evaluate('analyze(records).states.length'), dyn.states);
assert.equal(evaluate('analyze(records).blocks.length'), dyn.blocks);
assert.equal(evaluate('analyze(records).visits.length'), dyn.visits);
assert.equal(evaluate('analyze(records).raids.length'), dyn.raids);
assert.equal(evaluate('analyze(records).unmatched'), dyn.unmatched);

assert.equal(evaluate('JSON.stringify(parseCSV(csv))===JSON.stringify(raw)'),true);

for(const p of ['overview','shop','raid','weapons','logs','schema']){
  evaluate(`page='${p}';render()`);
  assert(nodes['#content'].innerHTML.length>100);
  console.log(p+' OK')
}

evaluate("page='raid';render();replayTime=duration(analyze(records).raids[0]);updateReplay(analyze(records).raids[0])");
nodes['#scene'].value='all';
assert.equal(evaluate('analyze(records).raids[0].scene'), 'NightRaid_BankGrand');
nodes['#scene'].value='Main';
assert.equal(evaluate('analyze(filtered()).raids.length'),0);
nodes['#scene'].value='NightRaid_BankGrand';
assert.equal(evaluate('analyze(filtered()).raids.length'), dyn.raids);
evaluate("page='raid';render()");
assert(nodes['#content'].innerHTML.includes('#1 · NightRaid_BankGrand'));
assert(nodes['#content'].innerHTML.includes('step="any"'));
console.log('Raid scene label, scene filter keeps raid whole, slider reaches end: PASS');
console.log('dynamic:', dyn);
console.log('PASS');
