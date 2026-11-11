# -*- coding: utf-8 -*-
"""
删除 CombatStateMachine 的 _owner 字段与构造函数参数（2026-09-24）
- _owner 赋值一次、全类零读取，是 v5 P11 去掉回调 owner 形参后遗留的未完成清理。
- 状态的 CombatStateBase.Owner 不动：7 个子类读取 40 次，是真依赖。
"""
import io, os

ROOT = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork"
FILES = {
    "sm":   os.path.join(ROOT, "StateMachine", "CombatStateMachine.cs"),
    "ctl":  os.path.join(ROOT, "Controller", "CombatUnitController.cs"),
    "st":   os.path.join(ROOT, "StateMachine", "CombatState.cs"),
}
BAK_TAG = ".bak.2026-09-24-fsmowner"


def read(p):
    with io.open(p, "r", encoding="utf-8", newline="") as f:
        return f.read()


def write(p, s):
    with io.open(p, "w", encoding="utf-8", newline="") as f:
        f.write(s)


def rep(text, old, new, tag):
    cnt = text.count(old)
    assert cnt == 1, "[%s] 命中 %d 次，期望 1 次\n--- old ---\n%s" % (tag, cnt, old)
    return text.replace(old, new)


# ---------- 载入（保留原始换行符判断） ----------
raw = {}
crlf = {}
for k, p in FILES.items():
    t = read(p)
    crlf[k] = "\r\n" in t
    raw[k] = t.replace("\r\n", "\n") if crlf[k] else t
print("换行符：", {k: ("CRLF" if v else "LF") for k, v in crlf.items()})

# ---------- CombatStateMachine.cs ----------
t = raw["sm"]

t = rep(t,
"""/// 【_owner 现在还有用吗】v5 P11 之后三个回调都不再接收 owner 参数，_owner 也就不再当「二传手」。
///   刻意保留它的两个理由：① 状态机本身该知道自己在服务谁（将来加转换日志 / 调试输出直接可用）；
///   ② 免得把 owner 概念从状态机里彻底摘掉，将来要加 owner 相关逻辑时又得回头改构造函数。
///   若确认长期不用，可以连同构造函数参数一起删除。
""",
"""/// ★ 2026-09-24 收尾清理：构造函数参数 owner 与 _owner 字段已删除。
///   v5 P11 去掉三个回调的 owner 形参后，这个字段在全类中**零读取**，
///   成了一个赋值一次、再没人看的改造残留（它当时保留的两条理由均已不成立：
///   需要 owner 的从来是状态，不是状态机）。
///   现在 owner 只有一条路径：InitStateMachine 里 new XxxState(this) → CombatStateBase.Owner，
///   那才是 7 个子类实际读取的那一份（CombatStates.cs 内 40 处）。
""",
    "sm:类注释")

t = rep(t,
"""    private readonly CombatUnitController _owner;
    private readonly Dictionary<CombatState, ICombatState>""",
"""    private readonly Dictionary<CombatState, ICombatState>""",
    "sm:删 _owner 字段")

t = rep(t,
"""    public CombatStateMachine(CombatUnitController owner)
    {
        _owner = owner;
        InitDefaultTransitions();
    }
""",
"""    /// <summary>
    /// 构造只做一件事：装载转换表。
    ///
    /// ⚠ 此处原本还有一个 <c>CombatUnitController owner</c> 参数（对应被删掉的 _owner 字段），
    ///   2026-09-24 删除：三个回调自 v5 P11 起不再接收 owner 实参，状态机便再也用不上它，
    ///   全类零读取。留着只会让人误以为状态机需要知道自己在服务谁。
    ///   谁要用 owner 谁自己注入 —— 是状态，不是状态机。
    /// </summary>
    public CombatStateMachine()
    {
        InitDefaultTransitions();
    }
""",
    "sm:构造函数去参")

raw["sm"] = t

# ---------- CombatUnitController.cs ----------
t = raw["ctl"]
t = rep(t,
"""        m_StateMachine = new CombatStateMachine(this);
""",
"""        // ★ 2026-09-24：状态机不再接收 owner —— 它那边的 _owner 赋值后全类零读取，已连同构造参数删除。
        //   owner 只往下传给紧接着这 7 个状态；它们才是真正持有并读取 owner 的一方。
        m_StateMachine = new CombatStateMachine();
""",
    "ctl:调用点去参")
raw["ctl"] = t

# ---------- CombatState.cs 注释同步 ----------
t = raw["st"]
t = rep(t,
"""///   就是把同一个恒等对象重复传一遍（唯一调用点 CombatStateMachine.Tick 传的永远是 _owner）。
""",
"""///   就是把同一个恒等对象重复传一遍（改造前唯一调用点 CombatStateMachine.Tick 传的永远是 _owner；
///   而那个 _owner 已于 2026-09-24 连同构造函数参数一起删除 —— 状态机根本不需要知道自己在服务谁）。
""",
    "st:注释同步")
raw["st"] = t

# ---------- 写回 ----------
for k, p in FILES.items():
    s = raw[k]
    if crlf[k]:
        s = s.replace("\n", "\r\n")
    write(p, s)

# ---------- 从磁盘重读复验 ----------
print("\n=== 复验 ===")
for k, p in FILES.items():
    t = read(p).replace("\r\n", "\n")
    hits = t.count("_owner")
    print("%-4s %-55s _owner 残留出现处：%d" % (k, os.path.basename(p), hits))

sm = read(FILES["sm"]).replace("\r\n", "\n")
assert "_owner" not in sm, "CombatStateMachine.cs 仍残留 _owner 引用"
assert "CombatStateMachine()" in sm, "构造函数未改为无参"
assert "CombatUnitController owner" not in sm, "构造参数未删干净"

ctl = read(FILES["ctl"]).replace("\r\n", "\n")
assert "new CombatStateMachine();" in ctl, "调用点未改为无参"
assert "new CombatStateMachine(this)" not in ctl, "调用点仍有旧写法"

print("\n全部断言通过")
