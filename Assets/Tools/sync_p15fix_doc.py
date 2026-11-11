# -*- coding: utf-8 -*-
"""
同步两份主文档到 ★ v5 P15-fix 状态（2026-09-18 下午）

背景：
    P15 上午落地时把「受击打断攻击」也做了，因其对玩家与敌人**对称生效**而被否掉回退；
    同日又修掉了「死亡动画从未播放」这个迁移前就存在的缺陷。
    文档必须与代码一致。所有改动一律用「内容锚点 + 断言命中次数」做最小替换，
    避免历史上出现过的「行号区间替换吞掉 <tbody>」这类事故。

用法：
    python Assets/Tools/sync_p15fix_doc.py
"""
import io
import sys

TECH = 'Assets/战斗框架技术文档_段号驱动与状态机推进.html'
MAIN = 'Assets/战斗框架全链路与数据流转.html'

fails = []


def patch(path, tag, old, new, expect=1):
    s = io.open(path, encoding='utf-8').read()
    cnt = s.count(old)
    if cnt != expect:
        fails.append('%s [%s] 锚点命中 %d 次，期望 %d 次' % (path, tag, cnt, expect))
        return s
    io.open(path, 'w', encoding='utf-8').write(s.replace(old, new))
    print('  OK  %s [%s]' % (path.split('/')[-1], tag))
    return s.replace(old, new)


print('=== 技术文档：5.6 标题 / 打断回退 / 动画拓扑 ===')

patch(TECH, 'A 标题去掉打断',
      '<h3>5.6 受击表现接通 · Hure(22) + 受击动画 + 顿帧 + 打断 (v5 P15 · 2026-09-18)</h3>',
      '<h3>5.6 受击表现接通 · Hure(22) + 受击动画 + 顿帧 (v5 P15 · 2026-09-18；打断已于当日下午回退)</h3>')

patch(TECH, 'B ApplyHurt 去掉打断分支',
      """    <span class="com">// ① 打断：Attack → Idle 这条边本来就在转换表里，不需要新增状态</span>
    <span class="kw">if</span> (m_Owner.CurrentState == CombatState.Attack)
        m_Owner.<span class="fn">RequestState</span>(CombatState.Idle);

    m_Owner.<span class="fn">StartCoroutine</span>(<span class="fn">HitStopRoutine</span>());   <span class="com">// ② 顿帧：只冻受击方</span>
    m_View.<span class="fn">PlayHit</span>(hitType);                      <span class="com">// ③ 受击动作</span>""",
      """    m_Owner.<span class="fn">StartCoroutine</span>(<span class="fn">HitStopRoutine</span>());   <span class="com">// ① 顿帧：只冻受击方</span>
    m_View.<span class="fn">PlayHit</span>(hitType);                      <span class="com">// ② 受击动作</span>""")

patch(TECH, 'B2 追加不打断的注释',
      """<span class="com">// 顿帧令牌：连击时作废旧协程的「恢复速度」，避免第 1 段提前解冻</span>""",
      """<span class="com">// ★ 这里刻意不写「打断攻击」：本方法对玩家与敌人对称生效，
//   一旦在这里 RequestState(Idle)，敌人技能只要配了效果 22，玩家的三连斩也会被同样打断。
//   「挨一刀就被打断」是玩法规则（霸体 / 韧性），应落在 FSM / Buff 的显式判定上。</span>
<span class="com">// 顿帧令牌：连击时作废旧协程的「恢复速度」，避免第 1 段提前解冻</span>""")

patch(TECH, 'C 动画拓扑补 Die 修复',
      """参数：Die / Run / SkillId  →  + Hit(Trigger) + HitType(Int) + Move(Bool)

[SM] Base Layer
  Any   → Hit(SM)          [Hit]        ← 打断其它状态（跑动 / 施法 / 受击中）
  idle  → Hit(SM)          [Hit]        ← ★ 插在 idle 转换列表的**第一位**
  idle  → Run_F / Down / FloatHit / Die  （既有 4 条无条件出边，本次未动）""",
      """参数：Die / Run / SkillId  →  + Hit(Trigger) + HitType(Int) + Move(Bool)   （共 6 个）

[SM] Base Layer
  Any   → Die              [Die==true]  ★ P15-fix：死亡入口，排 AnyState 首位且禁止自转
  Any   → Hit(SM)          [Hit]        ← 受击可覆盖当前动画（跑动 / 施法 / 受击中）
  idle  → Hit(SM)          [Hit]        ← ★ 插在 idle 转换列表的**第一位**
  idle  → Die              [Die==true]  ★ P15-fix：原为无条件脏边，现补条件
  idle  → Run_F / Down / FloatHit       （既有无条件出边，本次未动）
  Die   → idle             [Die==false] ★ P15-fix：不拦会形成死亡无限循环""")

patch(TECH, 'D checklist 改「为什么不做打断」',
      """    <li><b>打断不新增状态</b>：受击表现<b>不进 FSM</b>（9-17 已定论：做成状态会摧毁 AttackState 的 8 个跨帧字段）。
      打断只需 <code>RequestState(Idle)</code> —— <code>Attack &rarr; Idle</code> 这条边本来就在转换表里。</li>"""
      if False else
      """    <li><b>打断不新增状态</b>：受击表现<b>不进 FSM</b>（9-17 已定论：做成状态会摧毁 AttackState 的 8 个跨帧字段）。
      打断只需 <code>RequestState(Idle)</code> —— <code>Attack → Idle</code> 这条边本来就在转换表里。</li>""",
      """    <li><b>打断为什么最终回退</b>：实现过一版（受击且处在 Attack ⇒ <code>RequestState(Idle)</code>），同日撤销 ——
      这条管线<b>对玩家与敌人对称生效</b>，CSV 里有 37 个技能配了效果 22，敌人一旦使用它们，
      玩家的三连斩会被同样打断。而「挨一刀就被打断」是<b>玩法规则</b>，应由受击方的霸体 / 韧性决定，
      不该藏在通用受击表现里生效。将来要做，落点在 FSM 或 Buff 的显式判定。</li>""")

patch(TECH, 'E callout 决策①改不打断',
      """    ① 受击<b>允许打断</b>敌人攻击；② 第 1 段<b>不补配 Hure</b>""",
      """    ① 受击<b>不打断</b>敌人攻击（实现后因对称性否掉，同日回退）；② 第 1 段<b>不补配 Hure</b>""")

print('=== 技术文档：新增 5.7 死亡链路修复 ===')

SEC57 = """
  <h3>5.7 死亡链路修复 · Die 是 Bool，死亡动画从未播放 (v5 P15-fix · 2026-09-18)</h3>

  <p>上一节用 <code>parse_animator.py</code> 复验拓扑时顺带发现：<b>敌人的死亡动画从来没播过</b>。
     这不是一处写错，而是<b>三个病根叠在一起</b>，且都不是本次 P15 引入的（迁移前就存在）：</p>

  <table>
    <thead>
      <tr><th>#</th><th>病根</th><th>为什么致命</th><th>怎么治</th></tr>
    </thead>
    <tbody>
      <tr><td>①</td><td><code>Die</code> 参数是 <b>Bool</b>（<code>m_Type: 4</code>），<br>
          而 <code>PlayDeath()</code> 写的是 <code>SetTrigger("Die")</code></td>
          <td>对 Bool 参数设触发器<b>既不生效也不报错</b> —— 静默失效，
              日志里什么都看不出来</td>
          <td>新增 <code>WriteDeathParam()</code>：<b>先查参数真实类型再写值</b></td></tr>
      <tr><td>②</td><td><code>idle &rarr; Die</code> 这条边<b>没有条件</b>，<br>却排在 idle 转换列表的<b>末位</b></td>
          <td>前面 Run_F / Down / FloatHit 三条同样无条件且能命中 ⇒
              这条边<b>从未生效过</b>，死亡没有入口</td>
          <td>给它补条件 <code>[Die == true]</code>，<br>并新增 <b>Any State → Die</b> 作真正的入口</td></tr>
      <tr><td>③</td><td><code>Die &rarr; idle</code> 有一条 <code>ExitTime</code> 回边</td>
          <td>就算真进了死亡态，播完也会被拉回待机；<br>与 ② 的新入口叠加 ⇒
              <b>「死亡 → 回待机 → 再死亡」无限循环</b></td>
          <td>补条件 <code>[Die == false]</code>：死了就不再回待机</td></tr>
    </tbody>
  </table>

  <div class="code-header">
    <span class="lang">C#</span>
    <span>UnitView.cs · 按参数<b>真实类型</b>写死亡（★ 本次修复核心）</span>
  </div>
<pre><code><span class="kw">private void</span> <span class="fn">WriteDeathParam</span>()
{
    <span class="cls">AnimatorControllerParameter</span> p = <span class="fn">FindParameter</span>(<span class="cls">BattleConst</span>.ani_Die);
    <span class="kw">if</span> (p == <span class="kw">null</span>)      <span class="com">// 玩家 NYT.controller 根本没有 Die 参数</span>
    {
        <span class="kw">if</span> (!_deathChecked) { _deathChecked = <span class="kw">true</span>;
            <span class="cls">Debug</span>.<span class="fn">LogWarning</span>(<span class="str">$"[UnitView] {m_Owner.name} 没有 Die 参数 —— 无死亡动画"</span>); }
        <span class="kw">return</span>;
    }
    _deathChecked = <span class="kw">true</span>;

    <span class="kw">switch</span> (p.type)
    {
        <span class="kw">case</span> <span class="cls">AnimatorControllerParameterType</span>.Bool:     m_Animator.<span class="fn">SetBool</span>(ani_Die, <span class="kw">true</span>); <span class="kw">break</span>;
        <span class="kw">case</span> <span class="cls">AnimatorControllerParameterType</span>.Trigger:  m_Animator.<span class="fn">SetTrigger</span>(ani_Die);      <span class="kw">break</span>;
        <span class="kw">default</span>: <span class="cls">Debug</span>.<span class="fn">LogError</span>(<span class="str">$"Die 参数类型是 {p.type}，代码只支持 Bool / Trigger"</span>); <span class="kw">return</span>;
    }
    <span class="cls">Debug</span>.<span class="fn">Log</span>(<span class="str">$"[Death] {m_Owner.name} 写 Die({p.type}) = 死亡"</span>);
}</code></pre>

  <div class="code-header">
    <span class="lang">Animator</span>
    <span>GBL.controller · 死亡通路（P15-fix 后；<code>parse_animator.py</code> 可复验）</span>
  </div>
<pre><code>[SM] Base Layer
  Any   → Die       [Die==true]   ★ 真正的死亡入口；排在 AnyState 列表首位、canTransitionToSelf=false
  idle  → Die       [Die==true]   ★ 原无条件脏边，已补条件
  Die   → idle      [Die==false]  ★ 拦掉「死亡 → 回待机」的回边

  ⚠ canTransitionToSelf 必须为 false：Any State 转换默认允许「目标就是当前状态」，
    不关掉的话死亡动画会在每一帧被重新进入，永远重播第一段。</code></pre>

  <ul class="checklist">
    <li><b>为什么做类型自适应而不是统一改一种</b>：敌人 <code>GBL.controller</code> 与玩家
      <code>NYT.controller</code> 是两套历史资产 —— 改资产要动 YAML 与全部旧分支，改代码只动一处。
      类型查询的成本是<b>每个单位一次</b>（有缓存），可忽略。</li>
    <li><b>Bool 而不是 Trigger 的好处</b>：Bool 是<b>持续为真</b>的状态量，单位只要还存在，
      Animator 就一直知道「它是死的」—— 这正是 <code>[Die == false]</code> 这个出边条件能成立的前提
      （Trigger 用完即清，回边条件会立刻为真）。</li>
    <li><b>为什么不动 idle 上 Run_F / Down / FloatHit 那几条脏边</b>：它们今天仍然会抢在 idle 的所有后续转换之前，
      但有 Any State 这条入口兜着，删除它们属于改变既有行为，不在本次范围。</li>
  </ul>

  <p><b>验证结论（Play 实测）：</b>新增运行时探针 <code>DeathLinkProbe</code>（<code>#if UNITY_EDITOR</code>，不进正式包），
     走<b>真实路径</b> <code>CombatUnitController.SetDead(true) → View.PlayDeath()</code>，
     之后每 0.4 秒采样一次 Animator 当前状态：8 次采样 <code>inDie=True</code> 全部成立、
     <code>Die=True</code>、最终结果 <code>[DeathDiag] 结论 PASS —— GBL(Clone) 进入并停留在死亡状态</code>。
     同轮 <code>[HitReact]</code> 51 条对应 <code>[SegEffect] 效果=[0,22]</code> 53 条 —— 差额 2 正是
     <b>死亡之后</b>的两段（ApplyHurt 首行 <code>IsDead</code> 直接 return），契约未被破坏；
     <code>[HitEffectCalc]</code> ↔ <code>[HitEffect]</code> 78:78 仍 1:1，0 Error 0 Warning。</p>

  <p class="callout-box"><b>顺带修掉的工具缺陷</b>：<code>parse_animator.py</code> 此前把 <code>m_Type</code>
     当连续下标 ⇒ <code>4</code> 被报成 Trigger（实际是 Bool），正是这个误报掩盖了本节的病根 ①。
     参数类型映射是<b>跳跃枚举</b>：<code>1=Float / 3=Int / 4=Bool / 9=Trigger</code>。</p>

</section>"""

s = io.open(TECH, encoding='utf-8').read()
anchor = """     <code>Assets/Tools/parse_animator.py</code> 复验拓扑可 diff。</p>

</section>"""
if s.count(anchor) != 1:
    fails.append('技术文档 5.7 插入锚点命中 %d 次' % s.count(anchor))
else:
    s = s.replace(anchor, """     <code>Assets/Tools/parse_animator.py</code> 复验拓扑可 diff。</p>
""" + SEC57)
    io.open(TECH, 'w', encoding='utf-8').write(s)
    print('  OK  技术文档 [新增 5.7]')

print('=== 主文档：STEP 04-P15 去打断 + 加 STEP 04-P15fix ===')

patch(MAIN, 'F 步骤标题去打断',
      'title:"v5 P15 · 受击表现接通（Hure=22 + 受击动画 + 顿帧 + 打断）"',
      'title:"v5 P15 · 受击表现接通（Hure=22 + 受击动画 + 顿帧；打断已回退）"')

patch(MAIN, 'G 代码里的打断分支',
      """    // 打断：Attack → Idle 这条边本来就在转换表里，不需要新增状态
    if (m_Owner.CurrentState == CombatState.Attack) m_Owner.RequestState(CombatState.Idle);

    m_Owner.StartCoroutine(HitStopRoutine());   // 顿帧：只冻受击方""",
      """    // ★ 刻意不打断攻击：本方法对玩家与敌人对称生效，一旦在这里 RequestState(Idle)，
    //   敌人技能配了效果 22 时玩家的三连斩也会被同样打断 —— 那是玩法规则，归 FSM / Buff
    m_Owner.StartCoroutine(HitStopRoutine());   // 顿帧：只冻受击方""")

patch(MAIN, 'H pts 打断不新增状态 → 回退说明',
      '"<b>打断不新增状态</b>：受击表现<b>不进 FSM</b>（做成状态会摧毁 AttackState 的 8 个跨帧字段）。打断只需 <code>RequestState(Idle)</code>，<code>Attack → Idle</code> 本就在转换表里。"',
      '"<b>打断已于同日回退</b>：实现过一版后撤销 —— 这条管线<b>对玩家与敌人对称</b>，敌人技能一旦配效果 22，玩家三连斩会被同样打断；而「挨打即打断」是玩法规则（霸体 / 韧性），应落在 FSM / Buff 的显式判定，不能藏在通用受击表现里。"')

patch(MAIN, 'I pts 拍板改不打断',
      '"<b>拍板取舍</b>：受击<b>允许打断</b>攻击；',
      '"<b>拍板取舍</b>：受击<b>不打断</b>攻击（对称性否掉后回退）；')

patch(MAIN, 'J out 字段去掉 RequestState(Idle)',
      'out:"RequestState(Idle)、Animator Hit/HitType、顿帧（speed=0→1）"',
      'out:"Animator Hit/HitType、顿帧（speed=0→1）"')

STEP_LAST = """code:`// ① Skill.cs：效果 22（Hure）此前是空桩 —— 这是「敌人挨打毫无反应」的根因"""
s = io.open(MAIN, encoding='utf-8').read()
NEW_STEP = """{n:"STEP 04-P15fix",title:"v5 P15-fix · 死亡链路修复（Die 是 Bool，死亡动画从未播放）",file:"CombatUnitController.SetDead → UnitView.PlayDeath → GBL.controller",
code:`// ① 根因 —— 敌人 GBL.controller 的 Die 参数是 Bool(m_Type:4)，代码却写 SetTrigger：
//    SetTrigger("Die");   // ✗ 对 Bool 参数设触发器：不报错、不生效，纯静默失效
m_Animator.SetBool(BattleConst.ani_Die, true);   // ✓ 先查类型再写值

// ② UnitView.WriteDeathParam：类型自适应 + 参数不存在只告警一次（玩家 NYT 没有 Die 参数）
AnimatorControllerParameter p = FindParameter(BattleConst.ani_Die);
if (p == null) { /* 只告警一次，不刷屏 */ return; }
switch (p.type) {
    case Bool:    m_Animator.SetBool(ani_Die, true); break;
    case Trigger: m_Animator.SetTrigger(ani_Die);    break;
    default:      Debug.LogError("Die 参数类型不支持"); return;
}

// ③ GBL.controller：死亡既无入口、又有回边 —— 三处一起改
//    Any   → Die     [Die==true]   ★ 真入口，排 AnyState 首位 + canTransitionToSelf=false
//    idle  → Die     [Die==true]   ★ 原无条件脏边，补条件
//    Die   → idle    [Die==false]  ★ 不拦会形成「死亡 → 回待机 → 再死亡」无限循环`,
pts:["<b>不是一处写错，是三个病根叠着</b>：① Die 是 <code>Bool</code> 而代码写 <code>SetTrigger</code>；② <code>idle → Die</code> 无条件却排在列表末位，被 Run_F 抢先导致<b>从未生效</b>；③ <code>Die → idle</code> 有回边，播完就被拉回待机。缺哪个都播不出来。","<b>为什么类型自适应而不是改资产</b>：两套控制器是历史资产，改 YAML 要同步所有旧分支；改代码只动一处，且查询成本是每个单位<b>一次</b>（有缓存）。","<b>为什么必须拦回边</b>：只加 Any State 入口而不加 <code>[Die == false]</code>，会立刻变成死亡无限循环 —— 这是本次改动<b>最反直觉的一步</b>。","<b>为什么 <code>canTransitionToSelf = false</code></b>：Any State 转换默认允许目标就是当前状态，不关掉的话死亡动画每帧被重新进入，永远重播第一段。","<b>验证</b>：新增运行时探针 <code>DeathLinkProbe</code>（<code>#if UNITY_EDITOR</code>）走真实路径 <code>SetDead(true)</code>，8 次采样 <code>inDie=True</code> 全部成立，<code>[DeathDiag] 结论 PASS</code>；<code>[Death]</code> 日志确认真写的是 <code>Die(Bool)</code>。","<b>顺带修工具</b>：<code>parse_animator.py</code> 把参数类型当连续下标，<code>4</code> 被误报成 Trigger（应为 Bool）—— 正是它掩盖了这个缺陷。"],
in:"IsDead=true",out:"Animator Die(Bool)=true、卸载 CapsuleCollider / CharacterController"},

"""
if s.count(STEP_LAST) != 1:
    fails.append('主文档 STEP 插入锚点命中 %d 次' % s.count(STEP_LAST))
else:
    s = s.replace(STEP_LAST, NEW_STEP + STEP_LAST)
    io.open(MAIN, 'w', encoding='utf-8').write(s)
    print('  OK  主文档 [新增 STEP 04-P15fix]')

print()
if fails:
    print('RESULT = HAS FAILURE')
    for f in fails:
        print('  -', f)
    sys.exit(1)
print('RESULT = ALL PATCHED')
