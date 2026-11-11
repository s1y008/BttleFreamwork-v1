# -*- coding: utf-8 -*-
"""收尾：修掉因 _owner 删除而失效的命名注释，并把断言改成「排除注释行」后再复验。"""
import io, os, re

ROOT = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork"
FILES = {
    "sm":  os.path.join(ROOT, "StateMachine", "CombatStateMachine.cs"),
    "ctl": os.path.join(ROOT, "Controller", "CombatUnitController.cs"),
    "st":  os.path.join(ROOT, "StateMachine", "CombatState.cs"),
}


def read(p):
    with io.open(p, "r", encoding="utf-8", newline="") as f:
        return f.read()


def write(p, s):
    with io.open(p, "w", encoding="utf-8", newline="") as f:
        f.write(s)


def rep(text, old, new, tag):
    cnt = text.count(old)
    assert cnt == 1, "[%s] 命中 %d 次，期望 1\n--- old ---\n%s" % (tag, cnt, old)
    return text.replace(old, new)


raw, crlf = {}, {}
for k, p in FILES.items():
    t = read(p)
    crlf[k] = "\r\n" in t
    raw[k] = t.replace("\r\n", "\n") if crlf[k] else t

t = raw["st"]
_st_old = """/// 【⚠ 命名】字段用 PascalCase 的 Owner 而非 _owner：_owner 是 CombatStateMachine 的
///   私有实现细节，而这是给所有子类使用的受保护成员，命名上要能区分开。
"""
_st_new = """/// 【⚠ 命名】字段用 PascalCase 的 Owner 而非 _owner：这是给所有子类使用的受保护成员，
///   与「本类的私有实现细节」在命名上要能一眼区分开（原对照方是状态机里的 _owner 字段，
///   已于 2026-09-24 删除，辨别 Owner 归属的理由依然成立 —— 它属于状态，不属于状态机）。
"""
if _st_new in t:
    print("st: 命名注释已是新版，跳过（幂等）")
else:
    t = rep(t, _st_old, _st_new, "st:命名注释")
raw["st"] = t

for k, p in FILES.items():
    s = raw[k]
    if crlf[k]:
        s = s.replace("\n", "\r\n")
    write(p, s)


def code_lines_has(text, needle):
    """忽略注释行与 summary 行，只看真正的代码行"""
    for i, line in enumerate(text.split("\n")):
        s = line.strip()
        if s.startswith("//") or s.startswith("///") or s.startswith("*"):
            continue
        if needle in s:
            return i + 1
    return 0


print("=== 复验（仅检查非注释行）===")
ok = True
for k, p in FILES.items():
    t = read(p).replace("\r\n", "\n")
    ln = code_lines_has(t, "_owner")
    print("%-4s %-30s 非注释行出现 _owner：%s" % (k, os.path.basename(p), ("第 %d 行" % ln) if ln else "无"))
    if ln:
        ok = False

sm = read(FILES["sm"]).replace("\r\n", "\n")
assert "public CombatStateMachine()" in sm, "sm: 未找到无参构造函数"
assert code_lines_has(sm, "CombatUnitController owner") == 0, "sm: 构造参数仍在（非注释行）"
assert "private readonly Dictionary<CombatState" in sm, "sm: _states 字段丢失"

ctl = read(FILES["ctl"]).replace("\r\n", "\n")
assert "new CombatStateMachine();" in ctl, "ctl: 调用点未去参"
assert "new CombatStateMachine(this)" not in ctl, "ctl: 旧写法仍在"

st = read(FILES["st"]).replace("\r\n", "\n")
assert "protected readonly CombatUnitController Owner;" in st, "st: 状态 Owner 字段被误删"
assert "protected CombatStateBase(CombatUnitController owner)" in st, "st: 状态构造注入被误删"

assert ok, "仍有非注释行的 _owner 引用"
print("\n全部断言通过")
