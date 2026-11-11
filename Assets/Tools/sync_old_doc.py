# -*- coding: utf-8 -*-
"""
把《战斗框架技术文档_段号驱动与状态机推进.html》(2026-09-12 方案稿)
按 2026-09-15 的代码现状做定点同步。

⚠ 全部用「内容锚点」定位，不用行号 —— 上一次用行号时，前面的替换改变了行数，
导致后面的区块整体错位（<pre><code> 包装被吃掉、个别行残留）。教训已记录在日志里。
"""
import io, os, sys

SRC = r"E:\Project\BttleFreamwork-v1\Assets\战斗框架技术文档_段号驱动与状态机推进.html"
DST = SRC + ".new.html"

text = io.open(SRC, encoding="utf-8", newline="").read()
miss = []


def block(start, end, new_inner, keep_end=True):
    """把 start 与 end 之间的内容换成 new_inner（start / end 自身保留）"""
    global text
    i = text.find(start)
    if i < 0:
        miss.append("START: " + start[:70])
        return
    j = text.find(end, i + len(start))
    if j < 0:
        miss.append("END: " + end[:70])
        return
    tail = end if keep_end else ""
    text = text[:i + len(start)] + new_inner + tail + text[j + len(end):]


def rep(old, new, expect=1):
    global text
    n = text.count(old)
    if n != expect:
        miss.append("REP x%d: %s" % (n, old[:70]))
    text = text.replace(old, new)


# ---------- 顶部现状横幅 ----------
BANNER = u'''
  <div class="callout-box" style="border-left:3px solid var(--warn);margin-bottom:26px">
    <b>⚠ 现状同步（2026-09-15）</b>：本文是 <b>2026-09-12 的方案稿</b>，落地之后实现又演进过多轮。
    <b>第 5 节代码、第 6 节 CSV 字段已按 2026-09-15 现状同步</b>；工作量对比、落地步骤按原文保留，作为决策记录。
    <br>与原文的关键差异：
    <br>① <code>_leftIdle</code> / <code>_castFired</code> 两个 bool → <code>_phase</code> 枚举 + <code>_castCursor</code> 特效游标；
    <br>② <code>PlayCastEffect()</code> 无参版 → <code>PlayCastEffect(int effectIndex)</code>，时机改由 CSV <code>CastEffectTimings</code> 显式给出（不配就不播）；
    <br>③ <code>CastEffectIds</code> 列已删除，出手特效 id 的唯一来源是 <code>AttackEffectID</code>（列 22）；
    <br>④ <code>CombatUnitController.GetWeaponAnchor()</code> 已删除，绑点由 EffectManager 通过 EffectDot + BindName 自行解析；
    <br>⑤ 特效三问（播几个 / 何时播 / 播哪个 id）已全部下沉到 <code>SkillData</code>。
    <br>完整最新链路见 <a href="战斗框架全链路与数据流转.html">《战斗框架全链路与数据流转》</a>。
  </div>'''

# ---------- 5.1 AttackState ----------
ATTACK = u'''
<span class="kw">private enum</span> <span class="cls">AttackPhase</span> { <span class="cls">WaitingForLeave</span>, <span class="cls">Playing</span> }   <span class="com">// v5 P3：一维枚举取代 bool 二维判断</span>
<span class="kw">private const float</span> LEAVE_IDLE_GUARD_SECONDS = <span class="num">5f</span>;

<span class="kw">private</span> <span class="cls">AttackPhase</span> _phase;   <span class="com">// WaitingForLeave → Playing</span>
<span class="kw">private float</span> _elapsed;
<span class="kw">private</span> <span class="cls">Skill</span> _currentSkill;
<span class="kw">private int</span> _firedSegIdx;    <span class="com">// 已触发段数 == 下一段在 _segTimings 里的下标</span>
<span class="kw">private int</span> _segCount;       <span class="com">// = BaseData.GetSegmentCount()（CSV SegCount 列）</span>
<span class="kw">private float</span>[] _segTimings;
<span class="kw">private int</span> _castCursor;     <span class="com">// 特效轨道游标：已播数量 == 下一个待播下标（0-based）</span>
<span class="kw">private int</span> _castCount;      <span class="com">// = BaseData.GetCastEffectCount()</span>

<span class="kw">public</span> <span class="cls">CombatState</span> StateType { <span class="kw">get</span> { <span class="kw">return</span> <span class="cls">CombatState</span>.Attack; } }

<span class="kw">public void</span> <span class="fn">OnEnter</span>(<span class="cls">CombatUnitController</span> owner, <span class="kw">object</span> context)
{
    _phase = <span class="cls">AttackPhase</span>.WaitingForLeave;
    _elapsed = <span class="num">0f</span>; _firedSegIdx = <span class="num">0</span>; _castCursor = <span class="num">0</span>; _castCount = <span class="num">0</span>;
    _currentSkill = <span class="kw">null</span>; _segCount = <span class="num">0</span>; _segTimings = <span class="kw">null</span>;

    <span class="com">// 从 RequestState 上下文拿 skillId，加载技能</span>
    <span class="kw">if</span> (context <span class="kw">is int</span> skillId && skillId &gt; <span class="num">0</span>)
    {
        _currentSkill = owner.CurCombatUnit.<span class="fn">GetSkill</span>(skillId);
        <span class="kw">if</span> (_currentSkill != <span class="kw">null</span>)
        {
            _currentSkill.<span class="fn">SetSkillCaster</span>(owner, owner.m_Target);

            <span class="com">// v5 P6：段数来自 CSV 的 SegCount 列，不再用 SegTimings.Length 顶替</span>
            _segCount = _currentSkill.BaseData.<span class="fn">GetSegmentCount</span>();
            _segTimings = _currentSkill.BaseData.SegTimings;

            <span class="com">// v5 P8：特效条目数 = min(有效特效 id 数, 时机数)，与段数无关</span>
            _castCount = _currentSkill.BaseData.<span class="fn">GetCastEffectCount</span>();
        }
    }
}

<span class="kw">public void</span> <span class="fn">OnUpdate</span>(<span class="cls">CombatUnitController</span> owner, <span class="kw">float</span> deltaTime)
{
    <span class="kw">if</span> (_phase == <span class="cls">AttackPhase</span>.WaitingForLeave)
    {
        <span class="kw">if</span> (!owner.IsAnimatorInIdleState) _phase = <span class="cls">AttackPhase</span>.Playing;
        <span class="kw">else</span> {
            _elapsed += deltaTime;
            <span class="kw">if</span> (_elapsed &gt;= LEAVE_IDLE_GUARD_SECONDS) { owner.<span class="fn">RequestState</span>(<span class="cls">CombatState</span>.Idle); <span class="kw">return</span>; }
        }
    }
    <span class="kw">else</span> {
        <span class="kw">if</span> (owner.IsAnimatorInIdleState) { owner.<span class="fn">RequestState</span>(<span class="cls">CombatState</span>.Idle); <span class="kw">return</span>; }

        <span class="com">// v5 P1：每帧只读一次 Animator 状态，两条轨道共用（native call 2 → 1）</span>
        <span class="cls">AnimatorStateInfo</span> info = owner.<span class="fn">GetCurrentAnimatorStateInfo</span>();

        <span class="com">// ① 特效轨道：时机来自 CSV 的 CastEffectTimings，与伤害段无关</span>
        <span class="kw">if</span> (_currentSkill != <span class="kw">null</span> && _castCursor &lt; _castCount)
        {
            <span class="kw">while</span> (_castCursor &lt; _castCount)   <span class="com">// 同一帧跨多个时机点 → 全部补播</span>
            {
                <span class="kw">float</span> castTiming = _currentSkill.BaseData.<span class="fn">GetCastEffectTiming</span>(_castCursor);
                <span class="kw">if</span> (castTiming &lt; <span class="num">0f</span> || info.normalizedTime &lt; castTiming) <span class="kw">break</span>;
                _castCursor++;
                _currentSkill.<span class="fn">PlayCastEffect</span>(_castCursor);   <span class="com">// 显式传特效序号（1-based）</span>
            }
        }

        <span class="com">// ② 伤害段轨道：时机来自 CSV 的 SegTimings</span>
        <span class="kw">if</span> (_currentSkill != <span class="kw">null</span> && _segTimings != <span class="kw">null</span> && _firedSegIdx &lt; _segCount)
        {
            <span class="kw">if</span> (info.normalizedTime &gt;= _segTimings[_firedSegIdx])
            {
                _firedSegIdx++;
                owner.<span class="fn">TriggerSegmentHit</span>(_currentSkill, _firedSegIdx);
            }
        }
    }
    owner.<span class="fn">DoAutoFight</span>();
}

<span class="kw">public void</span> <span class="fn">OnExit</span>(<span class="cls">CombatUnitController</span> owner)
{
    _phase = <span class="cls">AttackPhase</span>.WaitingForLeave;
    _currentSkill = <span class="kw">null</span>;
    _segTimings = <span class="kw">null</span>;
}
'''

# ---------- 5.3 PlayCastEffect ----------
PLAYCAST = u'''
<span class="com">// ★ v5 P8：Skill 只负责「拿到 id → 交给 EffectManager」</span>
<span class="com">//   「播几个 / 何时播 / 播哪个 id」三问已全部下沉到 BaseData（SkillData）</span>
<span class="kw">public void</span> <span class="fn">PlayCastEffect</span>(<span class="kw">int</span> effectIndex)
{
    <span class="kw">if</span> (m_SkillCaseter == <span class="kw">null</span>) <span class="kw">return</span>;

    <span class="com">// 1-based；无效 / 越界 / 该位为空 一律返回 0</span>
    <span class="kw">int</span> castEffectId = BaseData.<span class="fn">GetCastEffectId</span>(effectIndex);
    <span class="kw">if</span> (castEffectId &lt;= <span class="num">0</span>) <span class="kw">return</span>;   <span class="com">// 不退化到播第 1 个</span>

    <span class="com">// 绑点由 EffectManager 通过施法者身上的 EffectDot 按 BindName 自行解析</span>
    <span class="cls">EffectManager</span>.Instance.<span class="fn">CreateEffect</span>(castEffectId, m_SkillCaseter.transform);
'''

# ---------- 5.4 数据类字段 ----------
FIELDS = u'''
<span class="com">// ★ 段时机表(归一化 0~1)，CSV 列 23</span>
<span class="kw">public float</span>[] SegTimings;

<span class="com">// ★ 出手特效 id 的唯一来源 —— CSV 列 22 "AttackEffectID"</span>
<span class="com">//   （原 CastEffectIds 列语义重复，2026-09-15 已删除，特效 id 不再有两个来源）</span>
<span class="kw">public</span> <span class="cls">List</span>&lt;<span class="kw">int</span>&gt; AttackEffects { <span class="kw">get</span>; <span class="kw">set</span>; }

<span class="com">// ★ 出手特效的独立时间轴 —— CSV 列 25，与 AttackEffects 按下标配对</span>
<span class="com">//   留空 = 该技能不出手特效（没有任何隐式推算）</span>
<span class="kw">public float</span>[] CastEffectTimings;

<span class="com">// ★ 伤害总段数 —— CSV 列 26，段数的唯一真相源</span>
<span class="kw">public int</span> SegCount { <span class="kw">get</span>; <span class="kw">set</span>; }

<span class="com">// ★ 逐段敌人表现 —— CSV 列 27，| 分段、; 分效（例 0|0;5|6）</span>
<span class="kw">public</span> <span class="cls">List</span>&lt;<span class="cls">List</span>&lt;<span class="kw">int</span>&gt;&gt; SegEffectTypes;
'''

# ---------- CSV 示例 ----------
CSVSAMPLE = u'''
<span class="com"># 真实文件共 28 列，此处只列与段号 / 特效相关的 6 列（列号不连续）</span>
<span class="com">#   [22]AttackEffectID  [23]SegTimings  [24]HitEffectIds</span>
<span class="com">#   [25]CastEffectTimings  [26]SegCount  [27]SegEffectTypes</span>
SkillId, AttackEffectID,     SegTimings,   HitEffectIds,      CastEffectTimings,    SegCount, SegEffectTypes
10002,   10001;10002;10001,  0.3;0.6;0.9,  10002;10001;10002, 0.25;0.55;0.85,       3,
10003,   10001;10002;10003,  0.3;0.6;0.9,  10002;10001;10002, 0.05;0.28;0.58;0.88,  3,        0|0;5|6
10004,   10001;10002;10003,  ,             ,                  ,                    0,
'''

# ---------- CSV 字段说明表 ----------
CSVROWS = u'''
      <tr><td><code>SegCount</code></td><td>int</td><td><b>列 26 · 伤害总段数（策划显式配置）</b><br>段数的唯一真相源，<code>GetSegmentCount()</code> 取 <code>min(SegCount, SegTimings.Length)</code> 兜底。<b>不再是冗余字段</b> —— 2026-09-15 起「时机点个数」与「伤害段数」正式解耦</td></tr>
      <tr><td><code>SegTimings</code></td><td>float[]</td><td>列 23 · 每段触发的归一化时机(0~1)，分号分隔<br>例 <code>0.3;0.6;0.9</code> 表示 30% / 60% / 90% 处各触发一段</td></tr>
      <tr><td><code>AttackEffectID</code></td><td>List&lt;int&gt;</td><td><b>列 22 · 出手特效 id 的唯一来源</b>（原 CastEffectIds 列已删除）<br>按特效序号索引，与 CastEffectTimings 按下标配对</td></tr>
      <tr><td><code>CastEffectTimings</code></td><td>float[]</td><td>列 25 · 出手特效的<b>独立时机轴</b>，与伤害段无关<br>留空则该技能不出手特效；实际播出条数 = min(特效 id 数, 时机数)</td></tr>
      <tr><td><code>HitEffectIds</code></td><td>List&lt;int&gt;</td><td>列 24 · 受击特效 id，由<b>受击方</b> OnDamaged 按攻击者的 SegIdx 索引</td></tr>
      <tr><td><code>SegEffectTypes</code></td><td>List&lt;List&lt;int&gt;&gt;</td><td>列 27 · 逐段敌人表现，<code>|</code> 分段、<code>;</code> 分效<br>例 <code>0|0;5|6</code> = 第1段仅掉血 / 第2段掉血+浮空 / 第3段仅后退</td></tr>
'''

# ================= 执行替换 =================
# 5.1
block(u'<pre data-page-node-id="sOyfncXbIdM5JqEOAERVqn"><code data-page-node-id="YrFHNXtroNJAowIaZJap3p">',
      u'</code></pre>', ATTACK)
# 5.3
block(u'<pre data-page-node-id="ZXvlH2I8QPWJofQl0qp6UD"><code data-page-node-id="XxBgMZIItdNqAwmPaQTq5u">',
      u'</code></pre>', PLAYCAST)
# 5.4
block(u'<pre data-page-node-id="mSzDTu47VM3XQiqRorMT52"><code data-page-node-id="ghWAxZACQOcaOLvGmTxeIw">',
      u'</code></pre>', FIELDS)
# CSV 示例
block(u'<pre data-page-node-id="zAyV85doCB8Mb0TyTNo6r4"><code data-page-node-id="frmq2CpUhf2523zT3Mga9D">',
      u'</code></pre>', CSVSAMPLE)
# CSV 字段说明表 tbody
block(u'<tbody data-page-node-id="1eZqERmk1SXL3YfvBCSCxX">', u'</tbody>', CSVROWS)

# 标题 / 正文文案
rep(u'<span data-page-node-id="Uyq2E6tGPMP85IsXbibObb">Skill.cs · 新增 PlayCastEffect 方法</span>',
    u'<span data-page-node-id="Uyq2E6tGPMP85IsXbibObb">Skill.cs · PlayCastEffect（2026-09-15 现状）</span>')
rep(u'<span data-page-node-id="xZmBjtGcDqGY5xxjtOV4U8">数据类 · 加 SegTimings + CastEffectIds 字段</span>',
    u'<span data-page-node-id="xZmBjtGcDqGY5xxjtOV4U8">数据类 · 段号与出手特效相关字段（2026-09-15 现状）</span>')
rep(u'skill.m_SegIdx = segIdx;', u'skill.SegIdx = segIdx;')
rep(u'<span data-page-node-id="GOrxsYQxjdSzuamxhhVv7t">BufferData.csv 字段扩展</span>',
    u'<span data-page-node-id="GOrxsYQxjdSzuamxhhVv7t">SkillData.csv · 段号 / 特效相关列（共 28 列）</span>')
rep(u'在技能配置表(假设为 <code data-page-node-id="1EKqnQESeZ8YCLoRPAtOUO">BufferData.csv</code> / <code data-page-node-id="mbEEsLcv0OoupBMazdXmKG">SkillData.csv</code>)中新增两个字段:',
    u'当前 <code data-page-node-id="mbEEsLcv0OoupBMazdXmKG">SkillData.csv</code> <b>共 28 列</b>，与段号 / 出手特效相关的列如下（2026-09-15 现状；<code>CastEffectIds</code> 列已删除，其后各列整体左移一位）:')
rep(u'<code data-page-node-id="sEz5xNOhR2mLr3wXuE1rUZ">CastEffectIds</code> / <code data-page-node-id="iG0BoVVa1CmUNRgsUqCDH4">HitEffectIds</code> 用分号分隔多个 int',
    u'<code data-page-node-id="sEz5xNOhR2mLr3wXuE1rUZ">AttackEffectID</code> / <code data-page-node-id="iG0BoVVa1CmUNRgsUqCDH4">HitEffectIds</code> 用分号分隔多个 int')

# 落地步骤 / 验收表
rep(u'<li data-page-node-id="k3h5G8bhG6XMU1D2bm5LRD"><b data-page-node-id="0kaXW2gRGqfFufqYQktBbE">CSV 扩展</b> — 在技能数据 CSV 中加 <code data-page-node-id="D65rGk5G4FvDCFAE0x1rzF">SegTimings</code> / <code data-page-node-id="LqyDEUXO6STpNXWVw9NgUE">CastEffectIds</code> 字段(若解析器不支持多值,先扩展解析器);挑 1~2 个测试技能配好数据</li>',
    u'<li data-page-node-id="k3h5G8bhG6XMU1D2bm5LRD"><b data-page-node-id="0kaXW2gRGqfFufqYQktBbE">CSV 扩展</b> — 在技能数据 CSV 中加 <code data-page-node-id="D65rGk5G4FvDCFAE0x1rzF">SegTimings</code> / <code data-page-node-id="LqyDEUXO6STpNXWVw9NgUE">AttackEffectID</code> / <code>CastEffectTimings</code> / <code>SegCount</code> / <code>SegEffectTypes</code> 字段(若解析器不支持多值,先扩展解析器);挑 1~2 个测试技能配好数据</li>')
rep(u'<li data-page-node-id="NlmBHF5ORjcckHybtIkNDS"><b data-page-node-id="eHS354JYSpz3VjAtGD2F3n">Skill 加 PlayCastEffect</b>(5.3 节)— 编译验证;在测试技能上挂刀光 prefab 到武器挂点,Play 验证刀光在挥刀过程中正确出现</li>',
    u'<li data-page-node-id="NlmBHF5ORjcckHybtIkNDS"><b data-page-node-id="eHS354JYSpz3VjAtGD2F3n">Skill 加 PlayCastEffect</b>(5.3 节)— 编译验证;Play 验证 <code>[Cast]</code> 日志按 CastEffectTimings 逐个播出特效（绑点由 EffectManager 经 EffectDot 解析，无需在代码里找武器挂点）</li>')
rep(u'<td data-page-node-id="VCSsBua9JqY6GfsBErIxQa">CSV 解析无报错,Console 中 SegTimings 数组长度 = SegCount</td>',
    u'<td data-page-node-id="VCSsBua9JqY6GfsBErIxQa">CSV 解析无报错，<code>GetSegmentCount()</code> = min(SegCount, SegTimings.Length)</td>')
rep(u'<td data-page-node-id="tiFgkXTfjkEV0AcH30kkLk">刀光在挥刀过程中出现,跟随武器运动</td><td data-page-node-id="Dx5XKLPfWmfTNV5zzuncmt">删除 PlayCastEffect 调用,仅保留日志</td>',
    u'<td data-page-node-id="tiFgkXTfjkEV0AcH30kkLk"><code>[Cast]</code> 日志按 CSV 的 CastEffectTimings 播出对应 effectId</td><td data-page-node-id="Dx5XKLPfWmfTNV5zzuncmt">恢复 .bak 文件</td>')
rep(u'<td data-page-node-id="5hgvgvC25zmMr3o1dQntJb">_firedSegIdx / _castFired 归零</td>',
    u'<td data-page-node-id="5hgvgvC25zmMr3o1dQntJb"><code>_phase</code> 归位，<code>_firedSegIdx</code> / <code>_castCursor</code> / <code>_castCount</code> 归零</td>')

# SVG 流程图里的过时标签
rep(u'_leftIdle?</text>', u'离开 Idle?</text>')
rep(u'★ 关键: 刀光和火花在同一段号触发点,但挂到不同物体 — 刀光挂武器跟随运动,火花挂敌人受击点',
    u'★ 关键: 刀光走 CastEffectTimings 独立时间轴,火花走 SegTimings 段号 — 绑点由 EffectManager 按 BindName 解析')
rep(u'★ AttackState.OnUpdate 每帧检查 normalizedTime,跨过 SegTimings[i] 即触发第 i+1 段',
    u'★ AttackState.OnUpdate 每帧读一次 normalizedTime,分别跨过 CastEffectTimings / SegTimings 即触发特效与段伤害')

# 顶部横幅
i = text.find(u'</header>')
if i < 0:
    miss.append(u'找不到 </header>')
else:
    text = text[:i + len(u'</header>')] + BANNER + text[i + len(u'</header>'):]

if miss:
    sys.stderr.write(u"\n".join(u"MISS " + m for m in miss) + u"\n")

io.open(DST, "w", encoding="utf-8", newline="").write(text)
print("written:", DST, os.path.getsize(DST), "bytes")
print("misses:", len(miss))
