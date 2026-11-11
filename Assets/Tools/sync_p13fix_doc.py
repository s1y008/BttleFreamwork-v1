# -*- coding: utf-8 -*-
"""
同步「UnitView / UnitHitReaction 由挂载组件改为 Controller 持有对象」到两份文档。
规则（遵循 Assets/.workbuddy/memory/MEMORY.md 六.4）：
  · 一律用**内容锚点**替换，绝不用行号区间
  · 每次替换前断言命中次数 == 1，写回前打印前后字节数
"""
import io, os, sys

MAIN = 'Assets/战斗框架技术文档_段号驱动与状态机推进.html'
OLD = 'Assets/战斗框架全链路与数据流转.html'


def patch(path, anchor, new, times=1):
    s = io.open(path, encoding='utf-8').read()
    tag = os.path.basename(path)
    # 幂等：目标文本若已在文件中，说明这一步此前已应用过（重复跑会双倍插入）
    if new in s:
        print('  SKIP %-45s 已应用过（检测到目标文本），跳过' % tag)
        return
    n = s.count(anchor)
    if n != times:
        print('  !! 断言失败 %s：锚点命中 %d 次（期望 %d）' % (tag, n, times))
        print('     锚点前 60 字：', anchor[:60].replace('\n', '\\n'))
        sys.exit(1)
    before = len(s.encode('utf-8'))
    s = s.replace(anchor, new)
    io.open(path, 'w', encoding='utf-8', newline='').write(s)
    after = os.path.getsize(path)
    print('  OK %-46s %8d -> %8d bytes' % (tag, before, after))


# ═══════════════ 1. 主文档：5.5 节后插入「持有方式」小节 ═══════════════
print('[1] 主文档 5.5 后插入 5.5.1')
anchor_main = '''  </table>

  <div class="code-header">
    <span class="lang">C#</span>
    <span>CombatUnitController.cs · 三个 Facade 的现状</span>
  </div>'''

new_main = '''  </table>

  <h4>5.5.1 持有方式：为什么不用 AddComponent</h4>
  <p>这两个类<b>不继承 MonoBehaviour</b> —— 它们是 Controller 在 <code>Start()</code> 里 <code>new</code> 出来的普通 C# 对象，
     经 <code>View</code> / <code>HitReaction</code> 两个只读属性向外暴露。状态机依旧写 <code>Owner.View.Xxx</code>，
     <b>所有既有调用点一字未改</b>，变的只有「对象从哪来」。</p>

  <div class="code-header">
    <span class="lang">C#</span>
    <span>CombatUnitController.cs · 由「挂载组件」改为「持有对象」</span>
  </div>
<pre><code><span class="com">// 改前：往单位 GameObject 上挂两个组件，再靠 Setup 补依赖</span>
<span class="com">//   m_View        = gameObject.AddComponent&lt;UnitView&gt;();</span>
<span class="com">//   m_HitReaction = gameObject.AddComponent&lt;UnitHitReaction&gt;();</span>
<span class="com">//   m_HitReaction.Setup(this, m_View);   ← 漏掉这一步，编译期毫无痕迹</span>

<span class="com">// 改后：构造即绑定，依赖直接进 readonly 字段</span>
<span class="kw">if</span> (m_View == <span class="kw">null</span>)        m_View = <span class="kw">new</span> <span class="cls">UnitView</span>(<span class="kw">this</span>);
<span class="kw">if</span> (m_HitReaction == <span class="kw">null</span>) m_HitReaction = <span class="kw">new</span> <span class="cls">UnitHitReaction</span>(<span class="kw">this</span>, m_View);</code></pre>

  <ul class="checklist">
    <li><b>消灭「实例存在但依赖未注入」的窗口</b> —— <code>UnitView(owner)</code> 在构造时就取回 Animator，
      <code>UnitHitReaction(owner, view)</code> 在构造时就收下依赖，两个字段都是 <code>readonly</code>。
      原先 <code>AddComponent + Setup</code> 两步式里，漏掉第二步要到 ApplyHit 才炸。</li>
    <li><b>Inspector 与 prefab 保持干净</b> —— 单位身上不再多两个「看得见却无从配置」的组件，
      也不存在「组件删了但 GUID 还留在场景里 → Missing Script」的隐患（已核验两份 GUID 在全部场景 / 预制体中零引用）。</li>
    <li><b>创建时机完全可控</b> —— 同步发生在 <code>Start()</code> 中、状态机注册之前，不再依赖 Unity 何时回调 <code>Awake</code>。</li>
  </ul>

  <div class="callout-box">
    <b>代价要说清楚</b>：这两类对象无法在 Inspector 里展开调试 —— 想确认 Animator 有没有取到，
    只能在 <code>UnitView</code> 里临时加日志。将来若需要响应 Unity 消息（例如 <code>OnAnimatorIK</code>），
    得由 Controller 手动转发一层。<em class="callout">就这两个类目前的职责而言，都没有用到 MonoBehaviour 的任何能力，
    所以这属于白赚 —— 去掉的是一个从未使用的基类。</em>
  </div>

  <div class="code-header">
    <span class="lang">C#</span>
    <span>CombatUnitController.cs · 三个 Facade 的现状</span>
  </div>'''

patch(MAIN, anchor_main, new_main)

# ═══════════════ 2. 旧文档：UnitView.PlayEffect 代码同步 ═══════════════
print('[2] 旧文档 UnitView.PlayEffect 同步')
anchor_play = r'''// UnitView：画面端唯一出口（原 Controller.OnAttackEffect 搬来，日志标签原样保留）
public void PlayEffect(int effectId)
{
    Debug.Log(\$"[HitEffect] {gameObject.name} 播放特效 effectId={effectId}");
    EffectManager.Instance.CreateEffect(effectId, transform);
}'''

new_play = r'''// UnitView：画面端唯一出口（原 Controller.OnAttackEffect 搬来，日志标签原样保留）
// ⚠ 普通 C# 类：由 Controller 持有，故一切 GameObject 访问都要经构造注入的 m_Owner
public UnitView(CombatUnitController owner)
{
    m_Owner    = owner;
    m_Animator = owner.GetComponent<Animator>();   // 构造即就绪，无「创建了还没初始化」的中间态
}

public void PlayEffect(int effectId)
{
    Debug.Log(\$"[HitEffect] {m_Owner.name} 播放特效 effectId={effectId}");
    EffectManager.Instance.CreateEffect(effectId, m_Owner.transform);
}'''

patch(OLD, anchor_play, new_play)

# ═══════════════ 3. 旧文档：STEP 06 改成 P13 后的 Facade 现状 ═══════════════
print('[3] 旧文档 STEP 06 同步为 Facade')
anchor_step6 = r'''public virtual void OnDamaged(CombatUnitController attactker, float hp, int hitEffectId)
{
    if (attactker == null || IsDead || CanbeAttack > 0) return;   // 三道门
    Hp = Mathf.Max(0, Hp - (int)(hp));                            // setter 内触发事件

#if UNITY_EDITOR
    Debug.Log($"[Combat] {gameObject.name} 受击 dmg={hp} HP={Hp}");   // v5 P0：零 GC
#endif

    if (Hp <= 0) { RequestState(CombatState.Dead); return; }

    // v5 P10：受击特效 id 由攻击方按段算好后**直接传入**（0 = 该段无特效）
    // 不再反查 attacker.m_CurSkill.SegIdx —— 受击方无需知道攻击者的任何内部状态
    if (hitEffectId > 0) OnAttackEffect(hitEffectId);

    if (m_Target == null) m_Target = attactker;                   // 反锁攻击者'''

new_step6 = r'''// ★ v5 P13：OnDamaged 降为 Facade —— 下列逻辑整体迁往 UnitHitReaction.ApplyHit（详见 STEP 04-P13）
public virtual UnitHitReaction.HitOutcome OnDamaged(CombatUnitController attactker, float hp, int hitEffectId)
{
    if (m_HitReaction == null) return UnitHitReaction.HitOutcome.Ignored;
    return m_HitReaction.ApplyHit(attactker, (int)hp, hitEffectId);
}

// 迁移前本方法亲自做的四件事，现由 ApplyHit 按同样顺序执行：
//   ① 扣血 Hp = Max(0, Hp - damage)                  setter 内触发事件
//   ② 死后 return —— 死者身上不再加一朵火花
//   ③ 受击特效：id 由攻击方按段算好后直接传入（0 = 该段无特效），径直交给 UnitView.PlayEffect
//   ④ m_Target == null 时反锁攻击者 —— 死者不需要仇恨，排在死亡分支之后天然被跳过'''

patch(OLD, anchor_step6, new_step6)

# ═══════════════ 4. 旧文档：STEP 04-P13 增补一条要点 ═══════════════
print('[4] 旧文档 STEP 04-P13 增补持有方式要点')
anchor_pts = '''<b>验证</b>：术前术后各一轮 Play，<code>[HitEffectCalc]</code> 与 <code>[HitEffect]</code> 各 32 次严格 1:1，三段日志逐条等价。"],'''

new_pts = '''<b>验证</b>：术前术后各一轮 Play，<code>[HitEffectCalc]</code> 与 <code>[HitEffect]</code> 各 32 次严格 1:1，三段日志逐条等价。","<b>持有方式（P13-fix）</b>：这两个类<b>不是 MonoBehaviour</b>，由 Controller 在 <code>Start()</code> 里 <code>new UnitView(this)</code> / <code>new UnitHitReaction(this, m_View)</code> 持有 —— 依赖进 <code>readonly</code> 字段，消灭「漏调 Setup → 远处空引用」这一失败模式；单位 GameObject 上不再多两个无从配置的组件，其 GUID 在全部场景 / 预制体中经核验为零引用。"],'''

patch(OLD, anchor_pts, new_pts)

print('\n全部同步完成')
