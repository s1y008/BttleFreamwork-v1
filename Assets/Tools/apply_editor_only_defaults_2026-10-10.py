# -*- coding: utf-8 -*-
"""
B 档：把 InitDefaultTransitions 降到 Editor（运行时不再有内置兜底表）

改动清单
  CombatStateMachine.cs
    1. 合并重复 summary（上次脚本留下的两个连续 /// <summary>，会触发 CS1587）
    2. 构造注释：说明运行时/Editor 差异
    3. ApplyTransitionTableAsset 注释：去掉「没配资产则代码表是唯一真相」
    4. 校验失败文案：改为「将无任何合法转换」
    5. LogDisabledOnce 文案同步
    6. CanTransitTo 注释：规则唯一来源改为资产
    7. ★ InitDefaultTransitions 用 #if UNITY_EDITOR 包裹（运行时为空方法）
  CombatTransitionTable.cs
    8. LogMissingOnce：Warning → Error，文案同步
  CombatTransitionMatrixWindow.cs
    9. 顶部提示文案同步

纪律：每处 old 命中必须 == 1；写回后重读复验；延迟 3 秒二次复验；幂等。
"""
import io, sys, time

ROOT = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork"
FSM = ROOT + r"\StateMachine\CombatStateMachine.cs"
TABLE = ROOT + r"\StateMachine\CombatTransitionTable.cs"
WIN = ROOT + r"\Editor\CombatTransitionMatrixWindow.cs"

STEPS = []

# ── 1) 合并重复 summary ────────────────────────────────────────────────
STEPS.append((FSM, "1-summary", """    /// <summary>
    /// 构造只做一件事：装载转换表。
    ///
    /// ⚠ 此处原本还有一个 <c>CombatUnitController owner</c> 参数（对应被删掉的 _owner 字段），
    ///   2026-09-24 删除：三个回调自 v5 P11 起不再接收 owner 实参，状态机便再也用不上它，
    ///   全类零读取。留着只会让人误以为状态机需要知道自己在服务谁。
    ///   谁要用 owner 谁自己注入 —— 是状态，不是状态机。
    /// </summary>
    /// <summary>
    /// 构造：装载转换表。
    ///
    /// <param name="applyAssetTable">
""", """    /// <summary>
    /// 构造：装载转换表。
    ///
    /// ⚠ 此处原本还有一个 <c>CombatUnitController owner</c> 参数（对应被删掉的 _owner 字段），
    ///   2026-09-24 删除：三个回调自 v5 P11 起不再接收 owner 实参，状态机便再也用不上它，
    ///   全类零读取。留着只会让人误以为状态机需要知道自己在服务谁。
    ///   谁要用 owner 谁自己注入 —— 是状态，不是状态机。
    ///
    /// <param name="applyAssetTable">
"""))

# ── 2) 构造体注释 ──────────────────────────────────────────────────────
STEPS.append((FSM, "2-ctor", """    public CombatStateMachine(bool applyAssetTable = true)
    {
        InitDefaultTransitions();
""", """    public CombatStateMachine(bool applyAssetTable = true)
    {
        // ★ 仅 Editor 下会真正装载内置表（见 InitDefaultTransitions 的 #if UNITY_EDITOR 包裹）；
        //   运行时（打包后）这里是空方法，转换关系完全来自下面的资产覆盖。
        InitDefaultTransitions();
"""))

# ── 3) ApplyTransitionTableAsset 注释 ──────────────────────────────────
STEPS.append((FSM, "3-comment", """    ///   整体覆盖保证：配了资产，资产就是唯一真相；没配资产，代码表就是唯一真相。
    ///
    /// 【为什么失败时保留内置表而不是直接抛异常】
    ///   状态机在 Controller.Start() 里构造，抛异常会让整个单位初始化失败、连锁 NRE。
    ///   但「保留内置表」必须伴随逐条 LogError 指名 —— 静默兜底是工程铁律明令禁止的。
""", """    ///   整体覆盖保证：配了资产，资产就是唯一真相。
    ///
    /// 【为什么失败时不抛异常】
    ///   状态机在 Controller.Start() 里构造，抛异常会让整个单位初始化失败、连锁 NRE。
    ///   ⇒ 改为「保留现状 + 逐条 LogError 指名」：行为可见地坏掉、日志直接指向根因，
    ///     而不是静默兜底。
    ///   ★ 注意运行时没有内置表可退（见 InitDefaultTransitions 的 #if UNITY_EDITOR），
    ///     所以这里失败的后果是「该状态机无任何合法转换」，必须靠日志立刻发现。
"""))

# ── 4) 校验失败文案 ────────────────────────────────────────────────────
STEPS.append((FSM, "4-errmsg", """            // fail-fast 指名：逐条报错，并明确告知「沿用代码内置表」，不静默降级
            StringBuilder sb = new StringBuilder();
            sb.Append("[TransitionTable] 资产「").Append(table.name).Append("」校验失败，")
              .Append("本状态机沿用 CombatStateMachine.InitDefaultTransitions 内置表。问题如下：");
""", """            // fail-fast 指名：逐条报错，不静默降级
            StringBuilder sb = new StringBuilder();
            sb.Append("[TransitionTable] 资产「").Append(table.name).Append("」校验失败，")
              .Append("本状态机将无任何合法转换（单位会卡死在当前状态）。问题如下：");
"""))

# ── 5) LogDisabledOnce 文案 ────────────────────────────────────────────
STEPS.append((FSM, "5-disabled", """            + "」的 Enabled 为 false，运行时沿用代码内置转换表。");
""", """            + "」的 Enabled 为 false，运行时将无任何合法转换（单位会卡死在当前状态）。");
"""))

# ── 6) CanTransitTo 注释 ───────────────────────────────────────────────
STEPS.append((FSM, "6-cantransit", """    /// ③ 查白名单——HashSet O(1) 判定，规则唯一来源见 InitDefaultTransitions。
""", """    /// ③ 查白名单——HashSet O(1) 判定。
    ///   ★ 规则唯一来源 = Resources/Config/CombatTransitionTable 资产；
    ///     仅 Editor 下且资产缺失时，才回落到 InitDefaultTransitions 内置表。
"""))

# ── 7) ★ InitDefaultTransitions 降到 Editor ────────────────────────────
STEPS.append((FSM, "7-ifdef-head", """    /// <summary>
    /// 默认转换表：定义各状态之间的合法转换。
    /// Dead 为终态（无出边），受控状态结束后回到 Idle。
    /// </summary>
    private void InitDefaultTransitions()
    {
""", """#if UNITY_EDITOR
    /// <summary>
    /// 默认转换表：定义各状态之间的合法转换。
    /// Dead 为终态（无出边），受控状态结束后回到 Idle。
    ///
    /// ★ 2026-10-10 B 档：本方法**只在 Editor 下编译**。
    ///   【为什么还在】Editor 需要一份「代码基线」，用于两件事：
    ///     ① 新建转换表资产时用它填充，保证「资产起点 == 当前线上行为」（迁移零差异）；
    ///     ② 矩阵窗口的「重置为代码基线」。
    ///   【为什么运行时不再保留】运行时留着它 = 同时存在两份真相源：
    ///     改了它不生效（被资产覆盖）且**不报错** —— 这是静默失效，
    ///     正是 v4 根因 A「转换表只定义允许怎么走，不保证运行时真这么走」的翻版。
    ///   现在运行时转换关系的唯一来源是 Resources/Config/CombatTransitionTable 资产，
    ///   缺失时由 CombatTransitionTable.LogMissingOnce 报 **Error** 指名，不再有兜底表。
    /// </summary>
    private void InitDefaultTransitions()
    {
"""))

STEPS.append((FSM, "7-ifdef-tail", """        // Dead 为终态，无出边
    }
""", """        // Dead 为终态，无出边
    }
#else
    /// <summary>
    /// 运行时（打包后）的空实现：转换关系的唯一来源是转换表资产，代码里不再内置任何边。
    /// 保留同名方法只是为了让构造函数的调用点在两种编译条件下都能编译通过。
    /// </summary>
    private void InitDefaultTransitions() { }
#endif
"""))

# ── 8) LogMissingOnce: Warning → Error ─────────────────────────────────
STEPS.append((TABLE, "8-missing", """    /// <summary>资产缺失时只报一次，避免每个单位构造都刷一条日志。</summary>
    public static void LogMissingOnce()
    {
        if (s_MissingLogged) return;
        s_MissingLogged = true;
        Debug.LogWarning("[TransitionTable] 未找到转换表资产 Resources/" + RESOURCE_PATH
            + "，运行时沿用 CombatStateMachine.InitDefaultTransitions 内置表。"
            + "如需改用资产配置，请在菜单 Tools → Battle Framework → Transition Matrix 中创建并保存。");
    }
""", """    /// <summary>
    /// 资产缺失时只报一次，避免每个单位构造都刷一条日志。
    ///
    /// ★ 这是 **Error 不是 Warning**：运行时没有内置表可退
    ///   （CombatStateMachine.InitDefaultTransitions 只在 Editor 下编译），
    ///   所以资产缺失 = 该状态机无任何合法转换 = 所有单位卡死在当前状态。必须一眼看到。
    /// </summary>
    public static void LogMissingOnce()
    {
        if (s_MissingLogged) return;
        s_MissingLogged = true;
        Debug.LogError("[TransitionTable] 未找到转换表资产 Resources/" + RESOURCE_PATH
            + "，运行时状态机将无任何合法转换（所有单位会卡死在当前状态）。"
            + "请在菜单 Tools → Battle Framework → Transition Matrix 中创建并保存转换表资产。");
    }
"""))

# ── 9) 窗口提示文案 ────────────────────────────────────────────────────
STEPS.append((WIN, "9-hint", """                "运行时没有可用资产，状态机沿用 InitDefaultTransitions 内置表。"
                + "要让它读配置，点「新建资产」→「保存到资产」（默认路径 "
""", """                "运行时没有可用资产 ⇒ 状态机将无任何合法转换（所有单位卡死在当前状态）。"
                + "立即点「新建资产」→「保存到资产」（默认路径 "
"""))


def main():
    # 幂等判定：关键标记已存在即跳过
    fsm_text = io.open(FSM, encoding="utf-8").read()
    if "#if UNITY_EDITOR\n    /// <summary>\n    /// 默认转换表" in fsm_text:
        print("SKIP: B 档 (#if UNITY_EDITOR 包裹) 已存在。")
        return 0

    # 按文件分组执行
    files = {}
    for path, name, old, new in STEPS:
        files.setdefault(path, []).append((name, old, new))

    for path, items in files.items():
        text = io.open(path, encoding="utf-8").read()
        cur = text
        for name, old, new in items:
            cnt = cur.count(old)
            if cnt != 1:
                print("FAIL: [%s] %s 命中 %d 次（要求 == 1）" % (path.split("\\")[-1], name, cnt))
                return 1
            cur = cur.replace(old, new, 1)
            print("OK  : %-16s %s" % (path.split("\\")[-1], name))
        io.open(path, "w", encoding="utf-8", newline="").write(cur)

    print("WRITE OK")

    # 复验 1
    time.sleep(1)
    ok = True
    for path, items in files.items():
        again = io.open(path, encoding="utf-8").read()
        for name, old, new in items:
            if new not in again:
                print("VERIFY FAIL: %s / %s" % (path, name))
                ok = False
    if not ok:
        return 1
    print("VERIFY OK (第一次)")

    # 复验 2（防磁盘静默回退）
    time.sleep(3)
    fsm2 = io.open(FSM, encoding="utf-8").read()
    tbl2 = io.open(TABLE, encoding="utf-8").read()
    checks = [
        ("FSM #if 头", "#if UNITY_EDITOR\n    /// <summary>\n    /// 默认转换表" in fsm2),
        ("FSM #else 空实现", "private void InitDefaultTransitions() { }" in fsm2),
        ("FSM #endif", "#endif" in fsm2),
        ("TABLE LogError", "Debug.LogError(\"[TransitionTable] 未找到转换表资产" in tbl2),
    ]
    for label, good in checks:
        print(("OK  : " if good else "FAIL: ") + label)
        if not good:
            ok = False
    if not ok:
        return 1
    print("VERIFY OK (第二次，延迟 3 秒)")
    print("FSM lines =", fsm2.count("\n") + 1)
    return 0


if __name__ == "__main__":
    sys.exit(main())
