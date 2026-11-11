# -*- coding: utf-8 -*-
"""
修复「一次按键 = 无限连击」。

根因：m_CurSelectedSkill 被选中后常驻非空 → AutoFightUpdate 每帧 TryCastSkill → 无限起手。
方案（用户 2026-09-29 拍板）：
  ① 作用范围只改玩家 —— Controller 加 protected virtual OnSkillCastFinished() 空实现，
     由 WarrorController override 清空技能；敌人行为零变化。
  ② 结束时机 = 段数打完即收招，不等动画播完；切 Idle 由 IdleState.OnEnter 自动
     View.ResetSkillId()（= 置空 Animator 的 SkillId），无需本处碰 Animator。

铁律：old 命中数 == 1 断言；幂等判据「new 已存在即跳过」；写回复验；CRLF/BOM 保真。
"""
import io, os, sys

ROOT = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork"
F_STATES = os.path.join(ROOT, "StateMachine", "CombatStates.cs")
F_CTRL = os.path.join(ROOT, "Controller", "CombatUnitController.cs")
F_WARRIOR = os.path.join(ROOT, "Controller", "WarrorController.cs")

ok = True


def read(path):
    raw = io.open(path, "rb").read()
    return (raw.decode("utf-8-sig" if raw[:3] == b"\xef\xbb\xbf" else "utf-8").replace("\r\n", "\n"),
            b"\r\n" in raw, raw[:3] == b"\xef\xbb\xbf")


def write(path, txt, crlf, bom):
    out = txt.replace("\n", "\r\n") if crlf else txt
    data = out.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    io.open(path, "wb").write(data)


def apply(path, blocks, label):
    global ok
    txt, crlf, bom = read(path)
    changed = False
    print("===== %s =====" % os.path.basename(path))
    for old, new, desc in blocks:
        if new in txt:
            print("  - 跳过（已存在）: %s" % desc)
            continue
        n = txt.count(old)
        if n != 1:
            print("  ! 失败：old 命中 %d 次（期望 1）: %s" % (n, desc))
            ok = False
            continue
        txt = txt.replace(old, new, 1)
        changed = True
        print("  + 已替换: %s" % desc)
    if changed:
        write(path, txt, crlf, bom)
        back, _, _ = read(path)
        for old, new, desc in blocks:
            if back.count(new) != 1:
                print("  ! 复验失败：%s 出现 %d 次（期望 1）" % (desc, back.count(new)))
                ok = False
        print("  ✔ 已写回并通过复验")
    else:
        print("  - 无改动（全部幂等跳过）")
    print()


# ───────────────────────── 1. CombatStates.cs（AttackState） ─────────────────────────
BLOCKS_STATES = [
    # ① 字段区：新增两个标志
    (
        "    /// <summary>本次施法是否已报过延迟 —— 每次施法最多一条告警，避免掉一次帧刷一屏。</summary>\n"
        "    private bool _castLagWarned;\n",
        "    /// <summary>本次施法是否已报过延迟 —— 每次施法最多一条告警，避免掉一次帧刷一屏。</summary>\n"
        "    private bool _castLagWarned;\n"
        "\n"
        "    // ══════════════ ★ 2026-09-29 收尾标志（修复「一次按键 = 无限连击」）══════════════\n"
        "    //   起因：技能选中后 m_CurSelectedSkill 常驻非空，AutoFightUpdate 每帧拿它再 TryCastSkill，\n"
        "    //        表现为「按一次 A 就一直打」。收尾时清空它即可，但收尾本身有两条独立路径，\n"
        "    //        需要两个标志分别表达「谁到了」和「是否已经收过」\n"
        "\n"
        "    /// <summary>\n"
        "    /// 伤害段是否已全部打完（_firedSegIdx 触及 _segCount）。\n"
        "    ///   段打完即可收招，不必等 Animator 把收招动画播完 —— 玩家诉求：打完最后一段就能再出手。\n"
        "    ///   ⚠ _segCount == 0 的技能（全表 233 个）永远置不了 true，靠「动画播完」那条路兜底。\n"
        "    /// </summary>\n"
        "    private bool _segmentsDone;\n"
        "\n"
        "    /// <summary>\n"
        "    /// 本次施法是否已完成收尾（OnSkillCastFinished + 切 Idle）。\n"
        "    ///   两条结束路径谁先到谁触发，本标志保证收尾**只做一次**（否则回调会被调用两遍）。\n"
        "    /// </summary>\n"
        "    private bool _castFinished;\n",
        "字段：新增 _segmentsDone / _castFinished"
    ),

    # ② OnEnter 重置（锚点三行连写，OnExit 里没有 _segCount = 0 ⇒ 唯一）
    (
        "        _currentSkill = null;\n"
        "        _segCount = 0;\n"
        "        _segTimings = null;\n",
        "        _currentSkill = null;\n"
        "        _segCount = 0;\n"
        "        _segTimings = null;\n"
        "        _segmentsDone = false;\n"
        "        _castFinished = false;\n",
        "OnEnter：重置两个收尾标志"
    ),

    # ③ OnUpdate 增加第 ⑤ 步
    (
        "        // ④ 伤害段推进（实现见 AdvanceSegment）\n"
        "        AdvanceSegment(progress);\n"
        "    }\n",
        "        // ④ 伤害段推进（实现见 AdvanceSegment）\n"
        "        AdvanceSegment(progress);\n"
        "\n"
        "        // ⑤ 段数打完即收招（实现见 TryFinishBySegments）\n"
        "        //    ★ 2026-09-29：不等 Animator 播完收招动画，打完最后一段就放玩家出手。\n"
        "        //    返回 true = 已收招，调用方必须立刻 return。\n"
        "        if (TryFinishBySegments())\n"
        "            return;\n"
        "    }\n",
        "OnUpdate：新增 ⑤ 段数打完即收招"
    ),

    # ④ TryFinishSkill 改走统一出口
    (
        "        // 请求切回 Idle；IdleState.OnEnter 负责 NavStop + ResetSkillId\n"
        "        // （段索引归零已迁到 AttackState.OnEnter，见 v5 P10）\n"
        "        Owner.RequestState(CombatState.Idle);\n"
        "        return true;\n"
        "    }\n",
        "        // 收招走统一出口（段数打完那条路也会走它，保证只做一次）\n"
        "        FinishCast();\n"
        "        return true;\n"
        "    }\n",
        "TryFinishSkill：改调 FinishCast"
    ),

    # ⑤ AdvanceSegment 末尾打标记
    (
        "        Owner.TriggerSegmentHit(_currentSkill, _firedSegIdx);\n"
        "    }\n",
        "        Owner.TriggerSegmentHit(_currentSkill, _firedSegIdx);\n"
        "\n"
        "        // ★ 2026-09-29：最后一段已触发 ⇒ 伤害部分结束，可以收招了。\n"
        "        //   ⚠ 这里只打标记，真正的收招在 OnUpdate ⑤ 处统一做 —— 出手特效可能配在\n"
        "        //     最后一段之后（独立时间轴），要等它们播完再退，否则 OnExit 的\n"
        "        //     「仍有 N 个出手特效未播出」fail-fast 会把「我们抢跑」误报成「配置错」。\n"
        "        if (_firedSegIdx >= _segCount)\n"
        "        {\n"
        "            _segmentsDone = true;\n"
        "        }\n"
        "    }\n",
        "AdvanceSegment：段打完置 _segmentsDone"
    ),

    # ⑥ 新增 TryFinishBySegments + FinishCast（插在 TryFinishSkill 方法之后）
    (
        "        // 收招走统一出口（段数打完那条路也会走它，保证只做一次）\n"
        "        FinishCast();\n"
        "        return true;\n"
        "    }\n",
        "        // 收招走统一出口（段数打完那条路也会走它，保证只做一次）\n"
        "        FinishCast();\n"
        "        return true;\n"
        "    }\n"
        "\n"
        "    /// <summary>\n"
        "    /// ⑤ 段数打完即收招 —— ★ 2026-09-29：玩家「一次按键 = 一次释放」的关键一环。\n"
        "    ///\n"
        "    /// 【为什么不等动画播完】原先唯一的结束条件是「Animator 自己播完技能动画」，\n"
        "    ///   但技能动画往往比最后一段伤害长（收招摆造型），这段时间玩家被锁在 Attack 状态里。\n"
        "    ///\n"
        "    /// 【为什么还要等出手特效播完】CastEffectTimings 是**独立于伤害段**的时间轴，\n"
        "    ///   允许特效配在最后一段之后（例如收招残影 0.95）；此时若直接退，\n"
        "    ///   OnExit 那条 fail-fast 会误报 —— 那不是配置错，是我们抢跑。\n"
        "    ///\n"
        "    /// 【返回 true】已收招，调用方必须立刻 return。\n"
        "    /// </summary>\n"
        "    private bool TryFinishBySegments()\n"
        "    {\n"
        "        if (!_segmentsDone)            return false;   // 段还没打完\n"
        "        if (_castCursor < _castCount)  return false;   // 还有刀光没播，等下一帧\n"
        "        FinishCast();\n"
        "        return true;\n"
        "    }\n"
        "\n"
        "    /// <summary>\n"
        "    /// 一次技能释放的**唯一收尾出口** —— 两条结束路径（段数打完 / 动画播完）都会走到这里。\n"
        "    ///\n"
        "    /// 做两件事：\n"
        "    ///   ① 回调 Owner.OnSkillCastFinished()：告诉 Controller「这次释放结束了」。\n"
        "    ///      ★ 基类默认什么都不做（敌人靠持续持有技能实现连续攻击，清空会打断 AI）；\n"
        "    ///        玩家在 WarrorController 里 override 清空 m_CurSelectedSkill ——\n"
        "    ///        否则 AutoFightUpdate 每帧拿残留技能再 TryCastSkill ⇒ 一次按键 = 无限连击。\n"
        "    ///   ② 切回 Idle：IdleState.OnEnter 会 NavStop + ResetSkillId，\n"
        "    ///      「置空 Animator 的 SkillId」这一步由它负责，本处不再直接碰 Animator。\n"
        "    ///\n"
        "    /// ⚠ _castFinished 保证只收一次：两条路径可能在同一轮里先后到达。\n"
        "    /// </summary>\n"
        "    private void FinishCast()\n"
        "    {\n"
        "        if (_castFinished)\n"
        "            return;\n"
        "        _castFinished = true;\n"
        "\n"
        "        Owner.OnSkillCastFinished();\n"
        "        Owner.RequestState(CombatState.Idle);\n"
        "    }\n",
        "新增 TryFinishBySegments + FinishCast"
    ),
]

apply(F_STATES, BLOCKS_STATES, "CombatStates.cs")

# ───────────────────────── 2. CombatUnitController.cs ─────────────────────────
BLOCKS_CTRL = [
    (
        "    public bool RequestState(CombatState state)\n"
        "    {\n"
        "        if (m_StateMachine == null)\n"
        "        {\n"
        "            return false;\n"
        "        }\n"
        "        return m_StateMachine.ChangeState(state);\n"
        "    }\n",
        "    public bool RequestState(CombatState state)\n"
        "    {\n"
        "        if (m_StateMachine == null)\n"
        "        {\n"
        "            return false;\n"
        "        }\n"
        "        return m_StateMachine.ChangeState(state);\n"
        "    }\n"
        "\n"
        "    /// <summary>\n"
        "    /// 一次技能释放结束的回调 —— 由 AttackState 收尾时调用\n"
        "    ///   （两条结束路径：「段数打完」与「动画播完」，谁先到谁触发，且只触发一次）。\n"
        "    ///\n"
        "    /// ★ 基类默认是**空实现**：敌人靠「持续持有 m_CurSelectedSkill」实现连续攻击，\n"
        "    ///   在这里清空会打断它的 AI 节奏，所以什么都不做。\n"
        "    ///   玩家需要「一次按键 = 一次释放」，在 WarrorController 里 override 清空 ——\n"
        "    ///   否则 AutoFightUpdate 每帧都会拿残留的技能再 TryCastSkill，表现为无限连击。\n"
        "    ///\n"
        "    /// 【为什么是回调而不是给 CurSelectedSkill 加 setter】\n"
        "    ///   CurSelectedSkill 刻意只读 ——「写入权留在 Controller」。开 setter 等于放行\n"
        "    ///   「谁都能改当前技能」，将来必定出现第二处误清空。留一个有名字的时机钩子，\n"
        "    ///   调用方一眼看得出「为什么在这里改」。\n"
        "    /// </summary>\n"
        "    protected virtual void OnSkillCastFinished()\n"
        "    {\n"
        "    }\n",
        "RequestState 后新增 OnSkillCastFinished 虚方法"
    ),
]

apply(F_CTRL, BLOCKS_CTRL, "CombatUnitController.cs")

# ───────────────────────── 3. WarrorController.cs ─────────────────────────
BLOCKS_WARRIOR = [
    (
        "        if (m_Target == null)\n"
        "        {\n"
        "            // ★ v5 P13：目标是空 → 回待机动画。画面改写一律经 View（Controller 不再直接持有 Animator）\n"
        "            View.ResetSkillId();\n"
        "        }\n"
        "      \n"
        "    }\n",
        "        if (m_Target == null)\n"
        "        {\n"
        "            // ★ v5 P13：目标是空 → 回待机动画。画面改写一律经 View（Controller 不再直接持有 Animator）\n"
        "            View.ResetSkillId();\n"
        "        }\n"
        "      \n"
        "    }\n"
        "\n"
        "    /// <summary>\n"
        "    /// ★ 2026-09-29：玩家「一次按键 = 一次释放」。\n"
        "    ///\n"
        "    /// 【问题】m_CurSelectedSkill 一旦被选中就常驻非空（按 A 只负责选中，不负责释放），\n"
        "    ///   于是 AutoFightUpdate 每帧都满足 `m_CurSelectedSkill != null && m_Target != null`，\n"
        "    ///   每帧 TryCastSkill → StarSKill → RequestState(Attack) ⇒ **按一次 A 就无限连击**。\n"
        "    ///\n"
        "    /// 【修复】AttackState 收尾时（段数打完或动画播完，谁先到谁触发）回调到这里，\n"
        "    ///   清空当前技能 ⇒ 下一帧 `m_CurSelectedSkill == null`，必须再按一次 A 才能出手。\n"
        "    ///\n"
        "    /// 【为什么不给基类加 setter】见 CombatUnitController.OnSkillCastFinished 的注释。\n"
        "    /// </summary>\n"
        "    protected override void OnSkillCastFinished()\n"
        "    {\n"
        "        m_CurSelectedSkill = null;\n"
        "    }\n",
        "WarrorController：override OnSkillCastFinished 清空技能"
    ),
]

apply(F_WARRIOR, BLOCKS_WARRIOR, "WarrorController.cs")

print("=== 总结 ===")
print("全部成功" if ok else "存在失败项，请检查上面标记 ! 的行")
sys.exit(0 if ok else 1)
