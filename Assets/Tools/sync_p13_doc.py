# -*- coding: utf-8 -*-
"""
把 v5 P13（UnitView / UnitHitReaction 抽离）同步进两份文档。

为什么用脚本 + 内容锚点而不是按行号区间替换：
  2026-09 多次踩坑 —— 按行号区间会吃掉 <pre><code> 包装、行数一变就整体错位，
  已经误删过 <tbody>。本脚本对每处替换都断言命中次数，不对就直接崩，不会静默写坏。
"""
import io
import os

MAIN = 'E:/Project/BttleFreamwork-v1/Assets/战斗框架技术文档_段号驱动与状态机推进.html'
OLD = 'E:/Project/BttleFreamwork-v1/Assets/战斗框架全链路与数据流转.html'


def rd(p):
    return io.open(p, encoding='utf-8').read()


def wr(p, s):
    io.open(p, 'w', encoding='utf-8', newline='').write(s)


def rep(s, old, new, tag, expect=1):
    n = s.count(old)
    assert n == expect, '[%s] 命中 %d 次，期望 %d' % (tag, n, expect)
    return s.replace(old, new)


# ══════════════════ 主文档 ══════════════════
main = rd(MAIN)

# 1) AttackState 日志里的动画进度读点：改为 UnitView.StateInfo（必须与代码现状一致）
main = rep(main,
           't={GetCurrentAnimatorStateInfo().normalizedTime:F3}',
           't={(m_View != null ? m_View.StateInfo.normalizedTime : 0f):F3}',
           'main/TriggerSegmentHit 日志')

# 2) GetCurrentAnimatorStateInfo 方法块：标注已迁往 UnitView
main = rep(main,
           '<span class="com" data-page-node-id="fcY8mbFsoMEujtmbUYExUd">// ★ 当前 Animator 状态信息 — 状态机推进用</span>',
           '<span class="com" data-page-node-id="fcY8mbFsoMEujtmbUYExUd">// ★ 当前 Animator 状态信息 — 状态机推进用</span>\n'
           '<span class="com">// ★ v5 P13：本方法与 SetMoveAnim / ResetSkillId 一并迁往 UnitView，改名为 View.StateInfo；</span>\n'
           '<span class="com">//   CombatUnitController 至此不再有一行代码触碰 Animator，画面端改写必须经 View。</span>',
           'main/GetCurrentAnimatorStateInfo 标注')

MAIN_NEW_SECTION = u'''
  <h3>5.5 UnitView / UnitHitReaction · 受击表现抽离 (v5 P13 · 2026-09-17)</h3>
  <p>此前受击链路的实现全写在 <code>CombatUnitController</code> 里：<code>OnDamaged</code> 同时负责扣血、
     播特效、请求死亡状态、改仇恨四个毫不相干的职责；三个击退入口做的事完全一样却有着三套不同的防御判断。
     现在按「单位认识谁」拆成两个组件，Controller 保留同名方法作为<b>对外契约（Facade）</b>，
     所以 <code>Skill.ImmedDamage</code>、<code>UnitBuffComponent</code> 的调用点<b>一行都没改</b>。</p>

  <table>
    <thead><tr><th>组件</th><th>回答的问题</th><th>不能做什么</th></tr></thead>
    <tbody>
      <tr>
        <td><b>UnitView</b></td>
        <td>「看起来发生了什么」—— Animator 参数读写、动画状态查询、特效创建、死亡表现</td>
        <td>不持有血量/攻击力等业务数据，不做任何判断</td>
      </tr>
      <tr>
        <td><b>UnitHitReaction</b></td>
        <td>「受这一下要播什么、切什么状态」—— 扣血 → 受击特效 → 死亡判定 → 仇恨 → 控制态</td>
        <td>不让 View 或 FSM 反向依赖它</td>
      </tr>
      <tr>
        <td><b>Controller</b></td>
        <td>血量真相源、索敌决策、FSM 桥接</td>
        <td>不再直接摸 Animator</td>
      </tr>
    </tbody>
  </table>

  <div class="code-header">
    <span class="lang">C#</span>
    <span>CombatUnitController.cs · 三个 Facade 的现状</span>
  </div>
<pre><code><span class="com">// ★ v5 P13：保留签名 = 外部零改动；返回 HitOutcome 让调用方知道「有没有打死」</span>
<span class="kw">public virtual</span> <span class="cls">UnitHitReaction</span>.HitOutcome <span class="fn">OnDamaged</span>(<span class="cls">CombatUnitController</span> attactker, <span class="kw">float</span> hp, <span class="kw">int</span> hitEffectId)
{
    <span class="kw">if</span> (m_HitReaction == <span class="kw">null</span>) <span class="kw">return</span> <span class="cls">UnitHitReaction</span>.HitOutcome.Ignored;
    <span class="kw">return</span> m_HitReaction.<span class="fn">ApplyHit</span>(attactker, (<span class="kw">int</span>)hp, hitEffectId);
}

<span class="com">// 三个语义化击退入口统一转发到同一个实现（它们此前仅有防御判断的差异）</span>
<span class="kw">public virtual void</span> <span class="fn">OnHitOut</span>(...)       { m_HitReaction.<span class="fn">ApplyKnockback</span>(attacker, time); }
<span class="kw">public virtual void</span> <span class="fn">OnAirHit</span>(...)        { m_HitReaction.<span class="fn">ApplyKnockback</span>(attacker, time); }
<span class="kw">public virtual void</span> <span class="fn">OnPlayerAirHit</span>(...)  { m_HitReaction.<span class="fn">ApplyKnockback</span>(attacker, time); }

<span class="com">// 眩晕 / 沉默：仍然只翻译成一个带时长的 Buff，由 Buff 事件驱动 FSM</span>
<span class="kw">public virtual void</span> <span class="fn">OnDizz</span>(...)    { m_HitReaction.<span class="fn">ApplyStun</span>(attacker, time); }
<span class="kw">public virtual void</span> <span class="fn">OnSilence</span>(...) { m_HitReaction.<span class="fn">ApplySilence</span>(attacker, time); }</code></pre>

  <div class="code-header">
    <span class="lang">C#</span>
    <span>UnitHitReaction.cs · 受击管线（顺序不可乱）</span>
  </div>
<pre><code><span class="kw">public</span> HitOutcome <span class="fn">ApplyHit</span>(<span class="cls">CombatUnitController</span> attacker, <span class="kw">int</span> damage, <span class="kw">int</span> hitEffectId)
{
    <span class="kw">if</span> (attacker == <span class="kw">null</span> || m_Owner.IsDead || m_Owner.CanbeAttack &gt; <span class="num">0</span>) <span class="kw">return</span> HitOutcome.Ignored;

    m_Owner.Hp = <span class="cls">Mathf</span>.Max(<span class="num">0</span>, m_Owner.Hp - damage);              <span class="com">// ① 先扣血</span>
    <span class="kw">if</span> (m_Owner.Hp &lt;= <span class="num">0</span>) { m_Owner.<span class="fn">RequestState</span>(CombatState.Dead); <span class="kw">return</span> HitOutcome.Dead; } <span class="com">// ② 死了不再加火花</span>
    <span class="kw">if</span> (hitEffectId &gt; <span class="num">0</span>) m_View.<span class="fn">PlayEffect</span>(hitEffectId);                    <span class="com">// ③ 再出画面</span>
    <span class="kw">if</span> (m_Owner.m_Target == <span class="kw">null</span>) m_Owner.m_Target = attacker;            <span class="com">// ④ 死者不需要仇恨</span>
    <span class="kw">return</span> HitOutcome.Normal;
}</code></pre>

  <p><b>顺带清理（均经 <code>scan_refs.py</code> 全量扫描确认为零引用，非抽样判断）：</b>
     <code>OnHurt</code>、<code>IsUseAttSkill</code>、<code>EffectGo</code>、<code>_IsHitPoint</code>、
     <code>_SubHpCount</code>、<code>count</code> 共 6 个死成员删除。</p>

  <p><b>验证结论：</b>同一场景术前 / 术后各跑一轮 Play，<code>[HitEffectCalc]</code> 与
     <code>[HitEffect]</code> 计数严格 1:1（各 32 次），三段 <code>[AttackState]</code>、
     三个 <code>[Cast]</code>、<code>[SegEffect]</code> 均等价；<code>Parameter 'Move' does not exist</code>
     告警术前同样存在（10 条），非本次引入。</p>
'''

main = rep(main,
           '<span class="kw">public</span> <span class="cls">List</span>&lt;<span class="kw">int</span>&gt;[] EffectTypesBySegment;\n</code></pre>\n</section>',
           '<span class="kw">public</span> <span class="cls">List</span>&lt;<span class="kw">int</span>&gt;[] EffectTypesBySegment;\n</code></pre>\n' + MAIN_NEW_SECTION + '\n</section>',
           'main/插入 5.5 小节')

wr(MAIN, main)
print('主文档已同步：%d 字符' % len(main))

# ══════════════════ 旧文档 ══════════════════
old = rd(OLD)

# 1) AttackState 代码注释补 P13
old = rep(old,
          '''    //   攻击本身的推进与结算全部在本方法上方，与本行无关；切回 Idle 后由 IdleState 恢复决策。
}`,''',
          '''    //   攻击本身的推进与结算全部在本方法上方，与本行无关；切回 Idle 后由 IdleState 恢复决策。
    //
    // ★ v5 P13：本状态用到的画面端读写全部改走 Owner.View ——
    //   Owner.View.IsInIdleState（原 IsAnimatorInIdleState）、Owner.View.StateInfo（原 GetCurrentAnimatorStateInfo）、
    //   Owner.View.ResetSkillId / SetMoveAnim（原 Owner 同名方法）。
    //   CombatUnitController 至此不再有一行代码触碰 Animator。
}`,''',
          'old/AttackState 注释补 P13')

# 2) pts 追加一条 P13 说明
old = rep(old,
          '''不受影响</b>。"],''',
          '''不受影响</b>。","<b>v5 P13 画面端分层</b>：状态里的动画读写全部走 <code>Owner.View</code>；受击管线搬进 <code>UnitHitReaction</code>，画面出口搬进 <code>UnitView</code>，Controller 只留 Facade。"],''',
          'old/pts 追加 P13')

# 3) 插入一张 STEP 卡片
STEP = u'''{n:"STEP 04-P13",title:"v5 P13 · 受击表现抽离为 View / HitReaction",file:"UnitView.cs、UnitHitReaction.cs（新增）",
code:`// UnitHitReaction：一次受击从进门到出门的编排者
public HitOutcome ApplyHit(CombatUnitController attacker, int damage, int hitEffectId)
{
    if (attacker == null || m_Owner.IsDead || m_Owner.CanbeAttack > 0) return HitOutcome.Ignored;

    m_Owner.Hp = Mathf.Max(0, m_Owner.Hp - damage);                                  // ① 先扣血
    if (m_Owner.Hp <= 0) { m_Owner.RequestState(CombatState.Dead); return HitOutcome.Dead; }  // ② 死者不加火花
    if (hitEffectId > 0) m_View.PlayEffect(hitEffectId);                             // ③ 再出画面
    if (m_Owner.m_Target == null) m_Owner.m_Target = attacker;                       // ④ 死了就不需要仇恨
    return HitOutcome.Normal;
}

// UnitView：画面端唯一出口（原 Controller.OnAttackEffect 搬来，日志标签原样保留）
public void PlayEffect(int effectId)
{
    Debug.Log(\\$"[HitEffect] {gameObject.name} 播放特效 effectId={effectId}");
    EffectManager.Instance.CreateEffect(effectId, transform);
}

// Controller 侧降为 Facade —— 签名不变，外部调用点一行没改
public virtual UnitHitReaction.HitOutcome OnDamaged(CombatUnitController attactker, float hp, int hitEffectId)
{
    if (m_HitReaction == null) return UnitHitReaction.HitOutcome.Ignored;
    return m_HitReaction.ApplyHit(attactker, (int)hp, hitEffectId);
}`,
pts:["<b>三层分工</b>：UnitView 回答「怎么让它看起来发生了」；UnitHitReaction 决定「受这一下要播什么、切什么状态」；Controller 留数据与决策。","<b>Facade 的意义</b>：<code>OnDamaged</code> / <code>OnHitOut</code> / <code>OnAirHit</code> 保留原签名转发，<code>Skill.ImmedDamage</code> 与 <code>UnitBuffComponent</code> 的 DoT 结算<b>零改动</b>，将来二十个技能效果桩仍只认识 Controller 这一层。","<b>三个击退入口收敛</b>：OnHitOut / OnAirHit / OnPlayerAirHit 此前做的事完全一样，只有防御判断多少不同 —— 这是复制粘贴漂移；全量扫描确认此刻外部零调用，趁早统一到 ApplyKnockback。","<b>KnockbackDuration 改为受控写入</b>：<code>private set</code> + <code>SetKnockbackDuration()</code>，把「谁能写」锁死在贴着 RequestState 的那个唯一正确位置。","<b>顺带删死代码</b>：OnHurt / IsUseAttSkill / EffectGo / _IsHitPoint / _SubHpCount / count，均经全量扫描确认零引用。","<b>验证</b>：术前术后各一轮 Play，<code>[HitEffectCalc]</code> 与 <code>[HitEffect]</code> 各 32 次严格 1:1，三段日志逐条等价。"],
in:"hitEffectId（攻击方按段算好）、damage、控制时长",out:"UnitView.PlayEffect、RequestState(Dead / Knockback / Stun / Silence)、m_Target"},

'''

old = rep(old, '{n:"STEP 04b"', STEP + '{n:"STEP 04b"', 'old/插入 STEP 04-P13 卡片')

wr(OLD, old)
print('旧文档已同步：%d 字符' % len(old))
print('OK')
