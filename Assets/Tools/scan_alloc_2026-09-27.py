# -*- coding: utf-8 -*-
"""
下游命中结算链路 —— 托管分配静态审计器（只读）

背景：2026-09-27 做完方案 F（SkillCastContext）后，命中结算的「上半段」已归零，
      但下游 OnDamaged -> UnitHitReaction / UnitBuffComponent / BuffSystemPooled / UnitView
      这一层还没做过分配审计。本脚本按「Release 下是否真的编译进去」来分类，
      避免把 #if UNITY_EDITOR 里的诊断日志误报成真机分配。

用法：python Tools/scan_alloc_2026-09-27.py
输出：按文件分组的候选分配点，标记 [EDITOR]（真机不编译）/ [RELEASE]（真机生效）
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = os.path.join(ROOT, "Scripts")

# ── 下游链路文件（命中结算从进门到出门经过的全部代码）──────────────────────
TARGETS = [
    "Scripts/BattleFreamwork/Skill.cs",
    "Scripts/BattleFreamwork/SkillEffect/ISkillEffect.cs",
    "Scripts/BattleFreamwork/SkillEffect/SkillCastContext.cs",
    "Scripts/BattleFreamwork/SkillEffect/SkillEffectRegistry.cs",
    "Scripts/BattleFreamwork/SkillEffect/Effects/ImmedDamageEffect.cs",
    "Scripts/BattleFreamwork/SkillEffect/Effects/HureEffect.cs",
    "Scripts/BattleFreamwork/SkillEffect/Effects/AddBuffEffect.cs",
    "Scripts/BattleFreamwork/SkillEffect/Effects/SneerEffect.cs",
    "Scripts/BattleFreamwork/Controller/CombatUnitController.cs",
    "Scripts/BattleFreamwork/Controller/IHitReceiver.cs",
    "Scripts/BattleFreamwork/Controller/UnitBuffComponent.cs",
    "Scripts/BattleFreamwork/View/UnitHitReaction.cs",
    "Scripts/BattleFreamwork/View/UnitView.cs",
    "Scripts/BattleFreamwork/BufferSystem/BuffSystemPooled.cs",
    "Scripts/BattleFreamwork/BufferSystem/Buff.cs",
    "Scripts/BattleFreamwork/ClassObjectPool.cs",
    "Scripts/BattleFreamwork/StateMachine/CombatStateMachine.cs",
    "Scripts/BattleFreamwork/StateMachine/CombatStates.cs",
    "Scripts/BattleFreamwork/StateMachine/CombatState.cs",
    "Scripts/BattleFreamwork/ConfigData/SkillData.cs",
    "Scripts/ConfigData/EffectManager.cs",
]

# ── 分配模式 ────────────────────────────────────────────────────────────────
# (正则, 说明, 严重度)  严重度: HIGH=每次调用都分配 MEDIUM=条件性/首次 LOW=需人工判
PATTERNS = [
    (re.compile(r"\bnew\s+[A-Za-z_][A-Za-z0-9_<>\[\],\s]*\s*[\(\{\[]"), "new 对象/集合", "MED"),
    (re.compile(r"\bnew\s+[A-Za-z_][A-Za-z0-9_<>]*\s*\["), "new 数组", "MED"),
    (re.compile(r'"\s*\$"|\$\\?"'), "字符串插值 $", "MED"),
    (re.compile(r"\.AsReadOnly\s*\("), "AsReadOnly() 每次包装", "MED"),
    (re.compile(r"\.To(?:List|Array)\s*\(|\.Where\s*\(|\.Select\s*\(|\.OrderBy\w*\s*\(|\.Any\s*\(|\.First\w*\s*\(|\.Cast<|\.OfType<"), "LINQ", "MED"),
    (re.compile(r"params\s+object\s*\[\s*\]"), "params object[] 装箱", "MED"),
    (re.compile(r"StartCoroutine\s*\("), "StartCoroutine", "MED"),
    (re.compile(r"\.parameters\b"), "Animator.parameters（Unity 每次返回新数组）", "MED"),
    (re.compile(r"\bResources\.Load\b"), "Resources.Load", "LOW"),
    (re.compile(r"\bObject\.Instantiate\b|\bInstantiate\s*\("), "Instantiate", "LOW"),
    (re.compile(r"\bGetComponents?\s*[<\(]|\bGetComponentsIn\w+\s*[<\(]|\.Find\s*\(\s*\"|FindObjectsOfType"), "Unity 查询 API", "LOW"),
    (re.compile(r"\bDebug\.Log\w*\s*\("), "Debug.Log（字符串拼接分配）", "LOW"),
    (re.compile(r"\bstring\s*\+\s*|\+\s*\"|\"\s*\+"), "字符串 + 拼接", "LOW"),
]

# 注释/字符串前缀：命中在注释里的不算
def is_comment_line(line):
    s = line.strip()
    return s.startswith("//") or s.startswith("///") or s.startswith("*") or s.startswith("/*")


def strip_string_literals(line):
    """去掉字符串字面量内容，避免把注释里的示例代码当真"""
    return re.sub(r'"(?:[^"\\]|\\.)*"', '""', line)


def scan_file(rel):
    path = os.path.join(ROOT, rel)
    if not os.path.exists(path):
        return None
    with io.open(path, "r", encoding="utf-8", errors="replace") as f:
        lines = f.read().replace("\r\n", "\n").split("\n")

    # 预处理指令栈：判断当前行在 Release（非 UNITY_EDITOR）下是否编译
    # stack 元素为 bool：True=该分支在 Release 下生效
    stack = [True]
    editor_depth = 0      # 处于 UNITY_EDITOR 分支内的深度
    results = []

    for idx, raw in enumerate(lines, 1):
        line = raw.strip()

        # ── 预处理指令 ──
        if line.startswith("#"):
            if line.startswith("#if"):
                cond = line[3:].strip()
                # 判断这个分支在 Release 下是否为真
                # UNITY_EDITOR 在真机为假；!UNITY_EDITOR 在真机为真
                neg = cond.startswith("!")
                has_editor = "UNITY_EDITOR" in cond
                release_true = (not has_editor) or neg
                parent = stack[-1]
                stack.append(parent and release_true)
                continue
            elif line.startswith("#elif"):
                if len(stack) > 1:
                    stack.pop()
                cond = line[5:].strip()
                neg = cond.startswith("!")
                has_editor = "UNITY_EDITOR" in cond
                release_true = (not has_editor) or neg
                parent = stack[-1]
                stack.append(parent and release_true)
                continue
            elif line.startswith("#else"):
                if len(stack) > 1:
                    v = stack.pop()
                    parent = stack[-1]
                    # else 取反：这里简化为「与父一致且上个分支为假」
                    stack.append(parent and not (v and parent) if parent else False)
                continue
            elif line.startswith("#endif"):
                if len(stack) > 1:
                    stack.pop()
                continue
            continue

        in_release = stack[-1] if stack else True
        if is_comment_line(raw):
            continue
        code = strip_string_literals(raw)

        for pat, desc, sev in PATTERNS:
            m = pat.search(code)
            if m:
                results.append((idx, sev, desc, raw.strip()[:110], in_release))

    return results


def main():
    print("=" * 78)
    print("下游命中结算链路 · 托管分配静态审计")
    print("=" * 78)

    total_release = 0
    total_editor = 0
    per_file = []

    for rel in TARGETS:
        res = scan_file(rel)
        if res is None:
            print("  [缺失] %s" % rel)
            continue
        rel_hits = [r for r in res if r[4]]
        edt_hits = [r for r in res if not r[4]]
        total_release += len(rel_hits)
        total_editor += len(edt_hits)
        per_file.append((rel, rel_hits, edt_hits))

    for rel, rel_hits, edt_hits in per_file:
        if not rel_hits and not edt_hits:
            continue
        print("\n" + "-" * 78)
        print("%s   [RELEASE %d / EDITOR-ONLY %d]" % (rel, len(rel_hits), len(edt_hits)))
        for ln, sev, desc, txt, inrel in rel_hits:
            print("  [RELEASE] %4d %-4s %-38s | %s" % (ln, sev, desc, txt))
        if len(edt_hits) <= 6:
            for ln, sev, desc, txt, inrel in edt_hits:
                print("  [EDITOR ] %4d %-4s %-38s | %s" % (ln, sev, desc, txt))
        else:
            print("  [EDITOR ] 另有 %d 处（诊断日志，真机不编译，略）" % len(edt_hits))

    print("\n" + "=" * 78)
    print("汇总：RELEASE 生效 %d 处 / EDITOR-ONLY %d 处（后者在真机不参与分配）"
          % (total_release, total_editor))
    print("=" * 78)


if __name__ == "__main__":
    main()
