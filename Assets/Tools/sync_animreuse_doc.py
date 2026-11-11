# -*- coding: utf-8 -*-
"""
同步「UnitView 改为复用宿主基类已缓存的 Animator」到两份文档。
规则（Assets/.workbuddy/memory/MEMORY.md 六.4 / 六.11）：内容锚点 + 断言命中次数 + patch 幂等。
"""
import io, os, sys

MAIN = 'Assets/战斗框架技术文档_段号驱动与状态机推进.html'
OLD = 'Assets/战斗框架全链路与数据流转.html'


def patch(path, anchor, new, times=1):
    s = io.open(path, encoding='utf-8').read()
    tag = os.path.basename(path)
    if new in s:
        print('  SKIP %-45s 已应用过，跳过' % tag)
        return
    n = s.count(anchor)
    if n != times:
        print('  !! 断言失败 %s：锚点命中 %d 次（期望 %d）' % (tag, n, times))
        print('     锚点前 70 字：', repr(anchor[:70]))
        sys.exit(1)
    before = len(s.encode('utf-8'))
    s = s.replace(anchor, new)
    io.open(path, 'w', encoding='utf-8', newline='').write(s)
    print('  OK %-47s %8d -> %8d bytes' % (tag, before, os.path.getsize(path)))


# ══════════ 1. 主文档 5.5.1：new UnitView(this) → new UnitView(this, m_Animator) ══════════
print('[1] 主文档 5.5.1 代码块')
patch(
    MAIN,
    '''<span class="com">// 改后：构造即绑定，依赖直接进 readonly 字段</span>
<span class="kw">if</span> (m_View == <span class="kw">null</span>)        m_View = <span class="kw">new</span> <span class="cls">UnitView</span>(<span class="kw">this</span>);
<span class="kw">if</span> (m_HitReaction == <span class="kw">null</span>) m_HitReaction = <span class="kw">new</span> <span class="cls">UnitHitReaction</span>(<span class="kw">this</span>, m_View);''',
    '''<span class="com">// 改后：构造即绑定，依赖直接进 readonly 字段</span>
<span class="com">// Animator 沿用宿主基类 MoveBaseController.Awake() 已取好的引用，View 不再自己查一次</span>
<span class="kw">if</span> (m_View == <span class="kw">null</span>)        m_View = <span class="kw">new</span> <span class="cls">UnitView</span>(<span class="kw">this</span>, m_Animator);
<span class="kw">if</span> (m_HitReaction == <span class="kw">null</span>) m_HitReaction = <span class="kw">new</span> <span class="cls">UnitHitReaction</span>(<span class="kw">this</span>, m_View);''')

# ══════════ 2. 主文档 5.5.1：补一条要点说明为何复用 ══════════
print('[2] 主文档 checklist 增补复用要点')
patch(
    MAIN,
    '''    <li><b>创建时机完全可控</b> —— 同步发生在 <code>Start()</code> 中、状态机注册之前，不再依赖 Unity 何时回调 <code>Awake</code>。</li>''',
    '''    <li><b>创建时机完全可控</b> —— 同步发生在 <code>Start()</code> 中、状态机注册之前，不再依赖 Unity 何时回调 <code>Awake</code>。</li>
    <li><b>Animator 不重复获取</b> —— 宿主基类 <code>MoveBaseController.Awake()</code> 早已取好 <code>m_Animator</code>，
      View 直接沿用该引用。省一次查询倒在其次，<b>真正要杜绝的是真相源分裂</b>：若 Animator 将来改挂到子节点模型上，
      基类的取法会跟着调整，而 View 自己那句 GetComponent 不会 —— 于是出现「Controller 认为有、View 认为没有」的偏差。</li>''')

# ══════════ 3. 旧文档：UnitView 构造签名同步 ══════════
print('[3] 旧文档 UnitView 构造签名')
patch(
    OLD,
    r'''public UnitView(CombatUnitController owner)
{
    m_Owner    = owner;
    m_Animator = owner.GetComponent<Animator>();   // 构造即就绪，无「创建了还没初始化」的中间态
}''',
    r'''public UnitView(CombatUnitController owner, Animator animator)
{
    m_Owner    = owner;
    m_Animator = animator;   // 复用宿主基类 Awake 里已取好的引用，View 不再自己 GetComponent
}''')

# ══════════ 4. 旧文档 pts：增补持有/复用说明 ══════════
print('[4] 旧文档 pts 增补')
patch(
    OLD,
    '''<b>持有方式（P13-fix）</b>：这两个类<b>不是 MonoBehaviour</b>，由 Controller 在 <code>Start()</code> 里 <code>new UnitView(this)</code>''',
    '''<b>持有方式（P13-fix）</b>：这两个类<b>不是 MonoBehaviour</b>，由 Controller 在 <code>Start()</code> 里 <code>new UnitView(this, m_Animator)</code>''')

print('\n全部同步完成')
