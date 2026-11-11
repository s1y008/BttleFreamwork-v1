# -*- coding: utf-8 -*-
"""
状态同步改造 S1：把 AttackState 的三处 Animator 依赖收敛到 ICastClock。

★ 铁律遵循（环境坑五.1）：改 .cs 大文件一律走脚本
  ① 每块 old 命中数 == 1 断言      ② 写回后重读复验
  ③ 幂等（new 已在则跳过）          ④ 复验断言忽略注释行
"""
import io
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # = Assets
TARGET = os.path.join(ROOT, "Scripts", "BattleFreamwork", "StateMachine", "CombatStates.cs")

BLOCKS = [
    # ── B1 构造函数：注入时钟 ────────────────────────────────────────────
    (
        "public class AttackState : CombatStateBase\n"
        "{\n"
        "    public AttackState(CombatUnitController owner) : base(owner) { }\n",

        "public class AttackState : CombatStateBase\n"
        "{\n"
        "    /// ★ 施法时钟 —— 状态同步改造的第一个可替换边界（2026-09-28）。\n"
        "    ///   默认注入 <c>AnimatorCastClock</c>（进度读本机 Animator），与改造前**逐行等价**；\n"
        "    ///   联网 / 本地服务器模式改传 <c>LogicCastClock</c>，本文件的逻辑一行都不用动。\n"
        "    ///   详见 <c>Net/CastClock.cs</c>。\n"
        "    private readonly ICastClock _clock;\n"
        "\n"
        "    public AttackState(CombatUnitController owner, ICastClock clock = null) : base(owner)\n"
        "    {\n"
        "        _clock = clock ?? new AnimatorCastClock(owner);\n"
        "    }\n",
    ),
    # ── B2 OnEnter 归零时钟 ──────────────────────────────────────────────
    (
        "        _phase = AttackPhase.WaitingForLeave;\n"
        "        _firedSegIdx = 0;\n",

        "        _clock.Reset();\n"
        "\n"
        "        _phase = AttackPhase.WaitingForLeave;\n"
        "        _firedSegIdx = 0;\n",
    ),
    # ── B3 OnUpdate 推进时钟 ─────────────────────────────────────────────
    (
        "    public override void OnUpdate(float deltaTime)\n"
        "    {\n"
        "        // ① 起手守卫：等 Animator 真正离开待机，才开始推进本轮技能（实现见 TryEnterPlaying）\n",

        "    public override void OnUpdate(float deltaTime)\n"
        "    {\n"
        "        // 逻辑时钟按帧累加进度（AnimatorCastClock 是空实现 ⇒ 单机零影响）\n"
        "        _clock.Tick(deltaTime);\n"
        "\n"
        "        // ① 起手守卫：等 Animator 真正离开待机，才开始推进本轮技能（实现见 TryEnterPlaying）\n",
    ),
    # ── B4 注释微调 ──────────────────────────────────────────────────────
    (
        "        // ⑫ ★ v5 P1 性能优化点：整个 Playing 分支每帧只读一次动画机状态并缓存复用\n",

        "        // ⑫ ★ v5 P1 性能优化点：整个 Playing 分支每帧只取一次进度值并复用（改造后仍是每帧一次）\n",
    ),
    # ── B5 进度来源替换 ──────────────────────────────────────────────────
    (
        "        AnimatorStateInfo info = Owner.View.StateInfo;\n",

        "        // ★ 2026-09-28（状态同步 S1）：进度改由 ICastClock 提供，不再直接读 Animator。\n"
        "        //   单机下 AnimatorCastClock.GetProgress() 与原先的 info.normalizedTime 逐值等价。\n"
        "        float progress = _clock.GetProgress();\n",
    ),
    # ── B6/B7 两个推进调用改传 progress ──────────────────────────────────
    (
        "        AdvanceCastEffect(info);\n",
        "        AdvanceCastEffect(progress);\n",
    ),
    (
        "        AdvanceSegment(info);\n",
        "        AdvanceSegment(progress);\n",
    ),
    # ── B8/B9 两个 Advance 的形参 ────────────────────────────────────────
    (
        "    private void AdvanceCastEffect(AnimatorStateInfo info)\n",
        "    private void AdvanceCastEffect(float progress)\n",
    ),
    (
        "        if (castTiming < 0f || info.normalizedTime < castTiming)\n",
        "        if (castTiming < 0f || progress < castTiming)\n",
    ),
    (
        "    private void AdvanceSegment(AnimatorStateInfo info)\n",
        "    private void AdvanceSegment(float progress)\n",
    ),
    (
        "        if (info.normalizedTime < _segTimings[_firedSegIdx])\n",
        "        if (progress < _segTimings[_firedSegIdx])\n",
    ),
    # ── B10/B11 起手守卫与结束判定 ───────────────────────────────────────
    (
        "        if (!Owner.View.IsInIdleState)\n"
        "        {\n"
        "            _phase = AttackPhase.Playing;\n",

        "        if (_clock.IsCasting())\n"
        "        {\n"
        "            _phase = AttackPhase.Playing;\n",
    ),
    (
        "        // 仍停在技能动画（或仍在过渡中）⇒ 没播完，本帧不结束\n"
        "        if (!Owner.View.IsInIdleState)\n"
        "            return false;\n",

        "        // 仍停在技能动画（或仍在过渡中）⇒ 没播完，本帧不结束\n"
        "        if (_clock.IsCasting())\n"
        "            return false;\n",
    ),
]


def read_text():
    with io.open(TARGET, "r", encoding="utf-8", newline="") as f:
        raw = f.read()
    crlf = raw.count("\r\n")
    return raw.replace("\r\n", "\n"), crlf > 0


def write_text(txt, crlf):
    if crlf:
        txt = txt.replace("\n", "\r\n")
    with io.open(TARGET, "w", encoding="utf-8", newline="") as f:
        f.write(txt)


def code_lines(txt):
    """复验用：去掉注释行，避免命中自己写的说明文字。"""
    out = []
    for ln in txt.split("\n"):
        s = ln.strip()
        if s.startswith("//") or s.startswith("*") or s.startswith("///") or s.startswith("/*"):
            continue
        out.append(ln)
    return "\n".join(out)


def main():
    if not os.path.exists(TARGET):
        print("✗ 目标不存在: %s" % TARGET)
        return 1

    txt, crlf = read_text()
    print("读取: %s  字符=%d  CRLF=%s" % (TARGET, len(txt), crlf))

    # ① 逐块断言（幂等）
    # ⚠⚠ **插入型替换的陷阱**（同类第 2 次踩到，务必保留此注释）：
    #   当 new 里**包含** old（本脚本 B2/B3 就是在 old 前面插一行）时，
    #   old 在改完之后**仍然命中 1 次** —— 若按「old 命中 1 就执行」判断，复跑会**无限重复插入**。
    #   （上一次就是这个 defect 让 _clock.Reset() 被插了两次。）
    #   正确判据：**先看 new 是否已存在**，存在即跳过；不存在才要求 old 恰好命中 1 次。
    todo = []
    for i, (old, new) in enumerate(BLOCKS):
        if txt.count(new) > 0:
            print("  · 第 %d 块已改过，跳过（幂等）" % (i + 1))
            continue
        n = txt.count(old)
        if n == 1:
            todo.append((old, new))
            continue
        print("  ✗ 第 %d 块：期望命中 1 次，实际 %d 次" % (i + 1, n))
        print("    old 片段: %r" % old[:90])
        return 2

    if not todo:
        print("✓ 全部已就位，无需改动")
        return 0

    # ② 写入
    for old, new in todo:
        txt = txt.replace(old, new, 1)
    write_text(txt, crlf)

    # ③ 重读复验（忽略注释行）
    back, _ = read_text()
    code = code_lines(back)
    ok = True

    must_gone = [
        "AnimatorStateInfo info = Owner.View.StateInfo;",
        "info.normalizedTime",
        "AdvanceCastEffect(info);",
        "AdvanceSegment(info);",
        "private void AdvanceCastEffect(AnimatorStateInfo info)",
        "private void AdvanceSegment(AnimatorStateInfo info)",
        "if (!Owner.View.IsInIdleState)",
    ]
    for m in must_gone:
        if m in code:
            print("  ✗ 仍残留: %r" % m)
            ok = False

    must_have = [
        "private readonly ICastClock _clock;",
        "_clock = clock ?? new AnimatorCastClock(owner);",
        "_clock.Reset();",
        "_clock.Tick(deltaTime);",
        "float progress = _clock.GetProgress();",
        "AdvanceCastEffect(progress);",
        "AdvanceSegment(progress);",
        "if (_clock.IsCasting())",
    ]
    for m in must_have:
        if m not in code:
            print("  ✗ 缺失: %r" % m)
            ok = False

    if code.count("if (_clock.IsCasting())") != 2:
        print("  ✗ IsCasting 判定应为 2 处，实际 %d" % code.count("if (_clock.IsCasting())"))
        ok = False

    # ⚠ 插入型块必须验证「恰好一次」，否则重复插入不会被上面的残留检查发现
    for one in ["_clock.Reset();", "_clock.Tick(deltaTime);", "float progress = _clock.GetProgress();"]:
        c = code.count(one)
        if c != 1:
            print("  ✗ %s 应恰好 1 处，实际 %d（重复插入？）" % (one, c))
            ok = False

    if not ok:
        print("✗✗ 复验未通过")
        return 3

    print("✓ 改造完成：%d 块写入，复验通过（残留项 0，必需项 %d 项齐全）" % (len(todo), len(must_have)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
