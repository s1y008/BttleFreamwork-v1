# -*- coding: utf-8 -*-
"""
2026-09-24 索敌球形检测：Fractional A 方案
- FindTargetForMove_MinDistance / _Random / _MinHp 三个方法改用共用缓冲区 + OverlapSphereNonAlloc
- FindTargetInRadius_MinDistance（309 行所在）按用户决定**不动实现**，仅清理过期的 region 注释
"""
import io

P = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\Controller\CombatUnitController.cs"


def read(p):
    with io.open(p, "r", encoding="utf-8", newline="") as f:
        return f.read()


def write(p, s):
    with io.open(p, "w", encoding="utf-8", newline="") as f:
        f.write(s)


def rep(text, old, new, tag):
    """幂等：新串已在 → 跳过；否则要求 old 恰好命中 1 次"""
    if new in text:
        print("  · %s：已是新版，跳过" % tag)
        return text
    cnt = text.count(old)
    assert cnt == 1, "[%s] old 命中 %d 次，期望 1\n--- old ---\n%s" % (tag, cnt, old)
    return text.replace(old, new)


raw = read(P)
crlf = "\r\n" in raw
t = raw.replace("\r\n", "\n") if crlf else raw
print("换行符:", "CRLF" if crlf else "LF")

# ══════════ 块 1：插入共用缓冲区 + 辅助检测 ══════════
t = rep(t,
"    #region 查找最近攻击目标\n",
"""    //═══════════════ 球形索敌的共用缓冲区（2026-09-24）═══════════════
    // ★ 为什么不用 Physics.OverlapSphere：它每次调用都在引擎内部 new 一个 Collider[]，
    //   这些数组活不过一帧就成垃圾 ⇒ 每帧索敌 = 每帧一次堆分配，堆到 GC 来收。
    //   OverlapSphereNonAlloc 把结果填进调用方给的数组，容量固定、反复复用 ⇒ 零分配。
    //
    // ★★ 为什么敢让所有单位共用这一份（安全性论证）：
    //   四个 FindTargetInRadius_XXX 都是「同步执行、当场消费完」—— 循环里只挑出**一个**
    //   CombatUnitController 返回，不返回数组、也不跨帧持有其中的 Collider。
    //   Unity 游戏逻辑只有主线程，不存在两次调用交错的可能 ⇒ 共用安全。
    //   ⚠ 若将来某个方法要把结果存起来跨帧用，就必须改回独享数组 —— 届时会读到别人写进去的东西。
    //
    // ⚠⚠ 容量超限属于「静默降级」（违反项目铁律 5）：目标数超过容量时 Unity 会把多出来的
    //   部分**静默丢弃**，既不报错也不给提示。索敌丢目标的表现是「有些敌人永远不被选中」，
    //   而且无从排查 ⇒ 下面做一次性 LogError 指名（不是静默吞掉）。
    private const int TARGET_BUFFER_SIZE = 64;
    private static readonly Collider[] s_TargetBuffer = new Collider[TARGET_BUFFER_SIZE];
    private static bool s_BufferOverflowWarned;

    /// <summary>
    /// 球形检测：结果写入共用缓冲区，返回**实际命中数**（&lt;= TARGET_BUFFER_SIZE）。
    /// ⚠ 调用方必须按返回值 count 遍历 —— 缓冲区 count 之后的位置仍保留着上一次的 Collider 引用，
    ///   按 s_TargetBuffer.Length 走会把陈旧对象当成本次命中。
    /// </summary>
    private static int OverlapTargets(Vector3 center, float radius, int layer)
    {
        int count = Physics.OverlapSphereNonAlloc(center, radius, s_TargetBuffer, layer);
        if (count >= TARGET_BUFFER_SIZE && !s_BufferOverflowWarned)
        {
            s_BufferOverflowWarned = true;
            Debug.LogError("[CombatUnitController] 球形索敌缓冲区已满（容量 " + TARGET_BUFFER_SIZE
                + "，检测半径 " + radius + "）：超出容量的目标会被 Unity **静默丢弃**，"
                + "表现为某些单位永远索不到敌 —— 请调大 TARGET_BUFFER_SIZE。此提示只报一次。");
        }
        return count;
    }

    #region 查找最近攻击目标
""",
    "插入缓冲区与辅助方法")

# ══════════ 块 2：MinDistance —— 只清过期注释，实现不动 ══════════
t = rep(t,
"""        #region foeach遍历碰撞数组会造成内存垃圾，顾此方法放弃，改用下边的for循环遍历
       
        foreach (Collider c in colliders)
""",
"""        // ⚠ 2026-09-24 清理：此处原本有一条 region 注释说「foreach 遍历碰撞数组会造成内存垃圾，
        //   顾此方法放弃，改用下边的 for 循环遍历」，但方法体实际仍是 foreach ——
        //   说明那次改造当年只做了一半（真正改成 for 的是下面的 FindTargetForMove_MinDistance）。
        //   而且这条经验本身已过期：Unity 2019.3 之后 foreach 遍历数组不再产生额外 GC
        //   （本工程是 2023.2.20f1c1 ⇒ 现在这么写没有问题）。
        //   ★ 本方法真正的分配源是上面那行 Physics.OverlapSphere（每次调用 new 一个 Collider[]），
        //     按 2026-09-24 的决定暂不改动 —— 它只在 ScaleEnemy 分支丢失目标时才走到。
        foreach (Collider c in colliders)
""",
    "MinDistance 注释清理")

t = rep(t,
"""        }
           
        #endregion
     
        return selectedUnit;
""",
"""        }

        return selectedUnit;
""",
    "MinDistance 移除endregion")

# ══════════ 块 3：ForMove ══════════
t = rep(t,
"""        Collider[] colliders = Physics.OverlapSphere(transform.position, CurCombatUnit.MoveSpring, layer);
        for (int i = 0; i < colliders.Length; i++)
        {
            CombatUnitController unit = colliders[i].gameObject.GetComponent<CombatUnitController>();
""",
"""        // ★ 2026-09-24：改用共用缓冲区 + NonAlloc（原理见上方 OverlapTargets 注释），
        //   注意必须按 count 遍历，不能按 s_TargetBuffer.Length。
        int count = OverlapTargets(transform.position, CurCombatUnit.MoveSpring, layer);
        for (int i = 0; i < count; i++)
        {
            CombatUnitController unit = s_TargetBuffer[i].gameObject.GetComponent<CombatUnitController>();
""",
    "ForMove 改 NonAlloc")

# ══════════ 块 4：Random ══════════
t = rep(t,
"""        Collider[] colliders = Physics.OverlapSphere(transform.position, CurCombatUnit.Spring, layer);
        List<object> allUnit = new List<object>();

        for (int i = 0; i < colliders.Length; i++)
        {
            CombatUnitController unit = colliders[i].gameObject.GetComponent<CombatUnitController>();
""",
"""        // ★ 2026-09-24：改用共用缓冲区 + NonAlloc（原理见上方 OverlapTargets 注释）。
        // ⚠ 这里还剩一处分配：allUnit 每次 new List<object>()。可以比照 s_TargetBuffer 复用
        //   （调用前 Clear 即可，安全前提与上方缓冲区相同），本次按「一次一事」未动。
        int count = OverlapTargets(transform.position, CurCombatUnit.Spring, layer);
        List<object> allUnit = new List<object>();

        for (int i = 0; i < count; i++)
        {
            CombatUnitController unit = s_TargetBuffer[i].gameObject.GetComponent<CombatUnitController>();
""",
    "Random 改 NonAlloc")

# ══════════ 块 5：MinHp ══════════
t = rep(t,
"""        Collider[] colliders = Physics.OverlapSphere(transform.position, CurCombatUnit.Spring, layer);

        for (int i = 0; i < colliders.Length; i++)
        {
            CombatUnitController unit = colliders[i].gameObject.GetComponent<CombatUnitController>();
""",
"""        // ★ 2026-09-24：改用共用缓冲区 + NonAlloc（原理见上方 OverlapTargets 注释）。
        int count = OverlapTargets(transform.position, CurCombatUnit.Spring, layer);

        for (int i = 0; i < count; i++)
        {
            CombatUnitController unit = s_TargetBuffer[i].gameObject.GetComponent<CombatUnitController>();
""",
    "MinHp 改 NonAlloc")

# ══════════ 写回 ══════════
if crlf:
    t = t.replace("\n", "\r\n")
write(P, t)

# ══════════ 复验 ══════════
c = read(P).replace("\r\n", "\n")
print("\n=== 复验 ===")


def code_only(text):
    return "\n".join(l for l in text.split("\n")
                     if not l.strip().startswith("//") and not l.strip().startswith("///")
                     and not l.strip().startswith("*"))


body = code_only(c)
assert "private const int TARGET_BUFFER_SIZE = 64;" in body, "容量常量缺失"
assert "private static readonly Collider[] s_TargetBuffer" in body, "缓冲区字段缺失"
assert c.count("private static int OverlapTargets(") == 1, "辅助方法数量异常"
assert "Physics.OverlapSphereNonAlloc(center, radius, s_TargetBuffer, layer)" in body, "NonAlloc 调用缺失"
print("缓冲区 + 辅助方法 ✓")

assert "region foeach遍历碰撞数组" not in c, "过期 region 注释仍在"
assert "foeach遍历" not in c, "过期注释文本仍在"
# MinDistance 内部必须仍然保留 OverlapSphere 与 foreach（用户决定不动）
# ⚠ 边界锚点必须带 `public virtual` 前缀：我们自己写的注释里也提过 FindTargetForMove_MinDistance
#   这个方法名，只按方法名找会命中注释、把切片提前截断（与「复验要忽略注释」同一个坑）。
NEXT_MARK = "\n    public virtual CombatUnitController FindTargetForMove_MinDistance"
seg = c[c.index("FindTargetInRadius_MinDistance(int layer, bool includeCanbeAttack"):]
seg = seg[:seg.index(NEXT_MARK)]
assert "Physics.OverlapSphere(transform.position, CurCombatUnit.Spring, layer);" in seg, "MinDistance 的检测被误改"
assert "foreach (Collider c in colliders)" in seg, "MinDistance 的 foreach 被误改"
print("MinDistance：实现未动，仅注释更新 ✓")

for name in ("FindTargetForMove_MinDistance", "FindTargetInRadius_Random", "FindTargetInRadius_MinHp"):
    i = c.index(name + "(")
    s2 = c[i:c.index("\n    }", i)]
    assert "Physics.OverlapSphere(" not in s2, name + " 仍在使用 OverlapSphere"
    assert "OverlapTargets(" in s2, name + " 未改用 OverlapTargets"
    assert "colliders.Length" not in s2, name + " 仍按 Length 遍历"
    assert "for (int i = 0; i < count; i++)" in s2, name + " 未按 count 遍历"
    print(name + " ✓")

print("\n剩余 Physics.OverlapSphere 处数（应为 1 = MinDistance +Skill.cs 的 3 处不在此文件）:",
      c.count("Physics.OverlapSphere("))
print("全部断言通过")
