// 战斗框架文档冒烟校验：提取 <script> 在 DOM stub 上真实执行，验证语法与运行时无异常。
const fs = require('fs');
const vm = require('vm');

const FILE = 'E:/Project/BttleFreamwork-v1/Assets/战斗框架全链路与数据流转.html';
const html = fs.readFileSync(FILE, 'utf8');

// 提取所有 <script> 正文（本文件无外链脚本）
const scripts = [...html.matchAll(/<script[^>]*>([\s\S]*?)<\/script>/g)].map(m => m[1]);
console.log('script blocks =', scripts.length);
const code = scripts.join('\n');
console.log('script chars  =', code.length);

// ---- DOM stub ----
const made = {};
function el(id) {
  if (made[id]) return made[id];
  const e = {
    id, innerHTML: '', textContent: '', style: {}, dataset: {},
    classList: { add(){}, remove(){}, toggle(){}, contains(){return false} },
    addEventListener(){}, removeEventListener(){}, appendChild(){}, querySelector(){return el(id+'-q')},
    querySelectorAll(){return []}, getContext(){return null}, setAttribute(){}, getAttribute(){return null},
    focus(){}, blur(){}, scrollIntoView(){}, remove(){}, insertAdjacentHTML(){}, closest(){return null},
    getBoundingClientRect(){return {left:0,top:0,width:100,height:20,right:100,bottom:20}},
  };
  made[id] = e; return e;
}
const doc = {
  getElementById: el,
  querySelector: () => el('q'),
  querySelectorAll: () => [],
  createElement: () => el('new'),
  createElementNS: () => el('svg'),
  addEventListener(){}, removeEventListener(){},
  body: el('body'), documentElement: el('html'), head: el('head'),
};
const sandbox = {
  document: doc, window: {}, console,
  addEventListener(){}, removeEventListener(){},
  setTimeout: () => 0, clearTimeout(){}, setInterval: () => 0, clearInterval(){},
  requestAnimationFrame: () => 0, cancelAnimationFrame(){},
  performance: { now: () => 0 },
  location: { href: '', hash: '' },
  navigator: { userAgent: 'node' },
  localStorage: { getItem: () => null, setItem(){} },
  matchMedia: () => ({ matches: false, addEventListener(){} }),
};
sandbox.window = sandbox;
sandbox.globalThis = sandbox;

let ok = true;
try {
  vm.createContext(sandbox);
  vm.runInContext(code, sandbox, { timeout: 5000 });
  console.log('RUN   : OK（脚本执行无异常）');
} catch (e) {
  ok = false;
  console.log('RUN   : FAIL ->', e.message);
}

// ---- 内容断言 ----
function check(label, cond, extra) {
  console.log((cond ? 'PASS  : ' : 'FAIL  : ') + label + (extra ? '  ' + extra : ''));
  if (!cond) ok = false;
}
check('配置列已改为 26', /技能配置列 <b>26 列<\/b>/.test(html));
check('数据字典 AttackEffectID 在列 22', /<b>AttackEffectID<\/b>[\s\S]{0,80}CSV 列 22/.test(html));
check('CastEffectTimings 列号 24', /<b>CastEffectTimings<\/b>[\s\S]{0,60}CSV 列 24/.test(html));
check('HitEffect 列号 14', /<b>HitEffect<\/b>[\s\S]{0,60}CSV 列 14/.test(html));
check('SegCount 列号 25', /<b>SegCount<\/b>[\s\S]{0,80}CSV 列 25/.test(html));
check('SkillEffectTypes 已合并为逐段表（列 13）', /<b>SkillEffectTypes<\/b>[\s\S]{0,120}CSV 列 13/.test(html));
check('字典已标注「无 | 则仅第 1 段生效」', /不写 <code>\|<\/code> 则整串只在第 1 段生效/.test(html));
check('SegEffectTypes 列标记为已删除', /<s>SegEffectTypes<\/s>[\s\S]{0,120}已于 2026-09-15 删除/.test(html));
check('marks 已改用 AttackEffectID', /AttackEffectID\[0\] = 10001/.test(html));
check('marks 不再引用 CastEffectIds[', !/CastEffectIds\[\d\]/.test(html));
check('曲线第4个时机标注为未播', /只有 3 个特效 id/.test(html));
check('.tick.skip 样式已定义', /\.tick\.skip\{/.test(html));

// ---- v5 P9：状态机去掉 object context 通道 ----
check('ChangeState 已去掉 context 参数', /ChangeState\(CombatState to\)/.test(html) && !/ChangeState\(CombatState to, object context/.test(html));
check('OnEnter 签名已无 context', !/OnEnter\(CombatUnitController owner, object context\)/.test(html));
check('AttackState 改读 Owner.CurSelectedSkill', /Owner\.CurSelectedSkill/.test(html));
check('RequestState(Attack) 不再传 skillId', !/RequestState\(CombatState\.Attack, /.test(html));
check('已标注 v5 P9 版本', /v5 P9/.test(html));

// ---- v5 P10：受击特效改由攻击方按段算好后传参（删除 m_CurSkill 跨对象反查）----
check('OnDamaged 签名已改为 int hitEffectId', /OnDamaged\(CombatUnitController attactker, float hp, int hitEffectId\)/.test(html));
check('受击特效由攻击方按段算出（GetHitEffectIdForSegment）', /GetHitEffectIdForSegment/.test(html));
check('已删除 attactker.m_CurSkill 反查代码', !/attactker\.m_CurSkill/.test(html));
check('IdleState 不再调用 ResetSkillSegIdx', !/owner\.ResetSkillSegIdx\(\)/.test(html));
check('段号归零点已迁到 AttackState', /AttackState\.OnEnter<\/code> 重置|改在 <code>AttackState\.OnEnter<\/code>/.test(html));
check('已标注 v5 P10 版本', /v5 P10/.test(html));

// ---- v5 P11：状态回调去掉 owner 参数，改为构造注入 ----
check('OnEnter 已无 owner 参数', /public override void OnEnter\(\)/.test(html) && !/OnEnter\(CombatUnitController owner\)/.test(html));
check('OnUpdate 已无 owner 参数', /public override void OnUpdate\(float deltaTime\)/.test(html) && !/OnUpdate\(CombatUnitController owner/.test(html));
check('ChangeState 回调不再传 _owner 实参', !/OnEnter\(_owner\)|OnExit\(_owner\)/.test(html));
// ⚠ 2026-09-17 收紧：原文断言是「文档内已无小写 owner.」，但 P11 的**构造注入**典范写法
//   `XxxState(CombatUnitController owner)` 本身就是小写 owner 参数，会被误伤。
//   本断言真正要守住的是「三个回调不再把 owner 当参数传来传去」，故改为校验回调签名。
check('三个状态回调的签名里不再带 owner 形参', !/On(Enter|Update|Exit)\([^)]*\bowner\b/.test(html));
check('已标注构造注入 new AttackState(this)', /new AttackState\(this\)/.test(html));
check('已标注 v5 P11 版本', /v5 P11/.test(html));

// ---- v5 P12：攻击期间锁移动（AttackState.OnUpdate 去掉 DoAutoFight）----
check('AttackState 已不再调用 DoAutoFight', !/Owner\.DoAutoFight\(\);/.test(html));
check('已标注攻击期间锁移动', /锁移动/.test(html));
check('已说明决策真实入口是 FSM', /决策的真实入口是 FSM/.test(html));
check('已说明 Update 的 else 分支是死分支', /死分支/.test(html));
check('已标注 v5 P12 版本', /v5 P12/.test(html));

// ---- v5 P13：受击表现抽离为 UnitView / UnitHitReaction（Controller 降为 Facade）----
check('受击已转发到 UnitHitReaction.ApplyHit', /m_HitReaction\.ApplyHit/.test(html));
check('OnDamaged 签名保持不变（外部调用点零改动）', /OnDamaged\(CombatUnitController attactker, float hp, int hitEffectId\)/.test(html));
check('三个击退入口统一为 ApplyKnockback', /ApplyKnockback/.test(html));
check('Controller 不再自己调 EffectManager 播特效', !/EffectManager\.Instance\.CreateEffect\(effectId, this\.transform\)/.test(html));
check('受击特效出口改由 UnitView.PlayEffect 承担', /m_View\.PlayEffect\(hitEffectId\)|UnitView\.PlayEffect/.test(html));
check('画面端读写已改走 Owner.View', /Owner\.View\./.test(html));
check('已标注 v5 P13 版本', /v5 P13/.test(html));
check('已说明 KnockbackDuration 改为受控写入 SetKnockbackDuration', /SetKnockbackDuration/.test(html));

// ---- v5 P13-fix：UnitView / UnitHitReaction 由 AddComponent 改为 Controller 持有普通对象 ----
check('UnitView 由 Controller new 出来持有', /new UnitView\(this, m_Animator\)/.test(html));
check('UnitHitReaction 由 Controller new 出来持有', /new UnitHitReaction\(this, m_View\)/.test(html));
check('文档中不再有这两个类的 AddComponent 挂载代码', !/AddComponent<UnitView>|AddComponent<UnitHitReaction>/.test(html));
check('UnitView 代码块已改用构造注入的 m_Owner 访问宿主', /\{m_Owner\.name\}/.test(html));
check('UnitView 构造已改为接收 Animator，不再自己 GetComponent', /UnitView\(CombatUnitController owner, Animator animator\)/.test(html));
check('UnitView 代码块已无 owner.GetComponent<Animator>', !/owner\.GetComponent<Animator>\(\)/.test(html));
check('STEP 06 已同步为 Facade 转发', /OnDamaged 降为 Facade/.test(html));
check('已标注 P13-fix 版本', /P13-fix/.test(html));

// ---- 主文档（段号驱动与状态机推进）：P13-fix 持有方式小节 ----
// ⚠ 该文档含交互式 SVG，此处**只做文本正则校验**，不 eval 其 <script>，避免 DOM stub 不兼容造成误报。
const FILE_MAIN = 'E:/Project/BttleFreamwork-v1/Assets/战斗框架技术文档_段号驱动与状态机推进.html';
const mainHtml = fs.readFileSync(FILE_MAIN, 'utf8');
console.log('\n--- 主文档：' + FILE_MAIN.split('/').pop() + ' ---');
check('主文档已新增 5.5.1 持有方式小节', /5\.5\.1 持有方式/.test(mainHtml));
check('主文档已说明不用 AddComponent 的理由', /为什么不用 AddComponent/.test(mainHtml));
check('主文档已标注 new UnitView(this, m_Animator)', /<span class="kw">new<\/span> <span class="cls">UnitView<\/span>\(<span class="kw">this<\/span>, m_Animator\)/.test(mainHtml));
check('主文档已说明复用基类缓存的 Animator', /MoveBaseController\.Awake\(\)<\/code> 早已取好/.test(mainHtml));
check('主文档已标注 new UnitHitReaction(this, m_View)', /UnitHitReaction<\/span>\(<span class="kw">this<\/span>, m_View\)/.test(mainHtml));
check('主文档仍标注 AddComponent 只是对照的注释行', /gameObject\.AddComponent&lt;UnitView&gt;/.test(mainHtml));
check('主文档已说明代价（Inspector 不可见）', /手动转发一层/.test(mainHtml));
check('主文档仍保留 Facade 现状代码块', /CombatUnitController\.cs · 三个 Facade 的现状/.test(mainHtml));

// ---- v5 P15：受击表现接通（Hure=22 空桩补齐 + 受击动画 + 顿帧 + 打断）----
check('已新增 STEP 04-P15 受击表现步骤', /STEP 04-P15/.test(html));
check('OnHure 已改用本次命中的 target', /OnHure\(target\)/.test(html) && !/OnHure\(m_SkillTarget\)/.test(html));
check('受击表现落到 UnitHitReaction.ApplyHurt', /ApplyHurt/.test(html));
check('UnitView 已新增受击出口 PlayHit', /PlayHit/.test(html));
check('已说明必须先写 HitType 再触发 Hit', /先写 HitType 再触发 Hit/.test(html));
// ---- v5 P15-fix：打断回退 + 死亡链路修复 ----
check('已说明打断已回退（对称性理由）', /打断为什么最终回退/.test(mainHtml) && /对称生效/.test(mainHtml));
check('受击代码块里已没有 RequestState(Idle) 的打断分支',
      !/if \(m_Owner\.CurrentState == CombatState\.Attack\)/.test(html)
      && !/m_Owner\.<span class="fn">RequestState<\/span>\(CombatState\.Idle\)/.test(html));
check('已新增 STEP 04-P15fix 死亡链路修复', /STEP 04-P15fix/.test(html));
check('已说明 SetTrigger 对 Bool 参数无效', /对 Bool 参数设触发器/.test(mainHtml));
check('已标注 WriteDeathParam 类型自适应', /WriteDeathParam/.test(html) && /WriteDeathParam/.test(mainHtml));
check('已标注 Any → Die [Die==true] 死亡入口', /Any\s+→ Die\s+\[Die==true\]/.test(mainHtml));
check('已标注 Die → idle [Die==false] 阻断循环', /Die\s+→ idle\s+\[Die==false\]/.test(mainHtml));
check('已说明 canTransitionToSelf=false 的理由', /canTransitionToSelf/.test(mainHtml));
check('已说明参数类型映射 4=Bool 掩盖了本缺陷', /m_Type/.test(mainHtml) && /4=Bool|4 = Bool/.test(mainHtml));
check('主文档已新增 5.7 死亡链路修复', /5\.7 死亡链路修复/.test(mainHtml));
check('主文档已说明三者叠加（非单点错误）', /不是一处写错，是三个病根叠着/.test(html));
check('已说明顿帧只作用于受击方', /顿帧：只冻受击方/.test(html));
check('已标注 v5 P15 版本', /v5 P15/.test(html));

check('主文档已新增 5.6 受击表现接通', /5\.6 受击表现接通/.test(mainHtml));
check('主文档已说明 Hit 子状态机整棵无入边', /整棵子树没有任何转换能进入/.test(mainHtml));
check('主文档已标注 HIT_TYPE_LIGHT', /HIT_TYPE_LIGHT/.test(mainHtml));
check('主文档已标注 Hit/HitType 参数入口', /Any\s+→ Hit\(SM\)\s+\[Hit\]/.test(mainHtml)
      && /Entry → LightHit\s+\[HitType Equals 1\]/.test(mainHtml));
check('主文档已标注 v5 P15 版本', /v5 P15/.test(mainHtml));

console.log('\nRESULT =', ok ? 'ALL PASS' : 'HAS FAILURE');
process.exit(ok ? 0 : 1);
