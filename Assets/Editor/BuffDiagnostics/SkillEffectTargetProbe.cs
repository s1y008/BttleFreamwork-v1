using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// Skill 效果「目标来源」探针（Editor 菜单：Tools/Diag/SkillEffectTargetProbe）
///
/// 用途：实锤技能效果作用于「ExecuteEffect 传入的 target」（即 ctx.Target），
///       而不是 Skill 的字段 m_SkillTarget。
///
/// ★ 2026-09-24 阶段一（ISkillEffect 策略化）之后，本探针改为**直接调用注册表里的效果类**
///   —— 与游戏运行时走的是同一份代码，比改造前直接调 Skill 的方法更贴近真实。
///
/// 对照构造：把 Skill 的 m_SkillTarget 字段设成 null / 别的单位，而 ctx.Target 始终给 victim。
///   ⇒ 若效果仍作用于 victim，证明它读的是 ctx.Target（字段对它没有影响）。
///
/// ⚠ 必须在 Play 模式下运行（需要场景里真实的 CombatUnitController 实例 + 已注册的效果表）。
/// 幂等：只操作临时 new 出来的 Skill 实例；结束时清空 victim/caster 身上的 Buff，不污染场景。
///
/// ★ 实测背景：CSV 里配了效果 2 的技能其 BuffID 列（0-based 17）**全部为空**
///   ⇒ 探针必须自己注入 BuffID，否则 AddBuff 的 for 循环跑 0 次。
/// </summary>
public static class SkillEffectTargetProbe
{
    const BindingFlags FL = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    // 探针专用：CSV 的 BuffID 列是空的，这里注入一个确实存在的 Buff（1001 增攻，无控制副作用）
    const int PROBE_BUFF_ID = 1001;
    const int PROBE_SKILL_ID = 10026;   // 列 13 = "0;2"，确实是带 AddBuff 效果的技能

    [MenuItem("Tools/Diag/SkillEffectTargetProbe")]
    static void Run()
    {
        Debug.Log("========== [SkillTargetProbe] 开始 ==========");

        if (!EditorApplication.isPlaying)
        {
            Debug.LogError("[SkillTargetProbe] 必须在 Play 模式下运行（需要真实单位实例 + 已注册的效果表）");
            return;
        }

        var units = UnityEngine.Object.FindObjectsOfType<CombatUnitController>();
        var alive = new List<CombatUnitController>();
        foreach (var u in units)
        {
            if (u != null && u.CurCombatUnit != null && !u.IsDead)
                alive.Add(u);
        }

        if (alive.Count < 2)
        {
            Debug.LogError($"[SkillTargetProbe] 场景里可用单位不足 2 个（找到 {alive.Count} 个），无法构造对照实验");
            return;
        }

        var caster = alive[0];
        var victim = alive[1];
        Debug.Log($"[SkillTargetProbe] caster={caster.name}  victim={victim.name}");

        // ── 实验 A：m_SkillTarget = null，效果 2（AddBuff）作用于 ctx.Target = victim
        victim.CurCombatUnit.BuffSystem.ClearAllBuffs();
        int aBefore = victim.CurCombatUnit.BuffSystem.ActiveBuffCount;
        RunEffect(2, caster, victim, victim, null);
        int aAfter = victim.CurCombatUnit.BuffSystem.ActiveBuffCount;
        Debug.Log($"◆实验A m_SkillTarget=null，AddBuff 效果 ctx.Target=victim：victim Buff {aBefore} → {aAfter} ⇒ "
                  + (aAfter > aBefore ? "★生效 —— 读的是 ctx.Target（若读字段则为 0）" : "✗未生效 —— 仍在看字段"));

        // ── 实验 B：m_SkillTarget = caster（≠ victim），看 Buff 加到谁身上
        victim.CurCombatUnit.BuffSystem.ClearAllBuffs();
        caster.CurCombatUnit.BuffSystem.ClearAllBuffs();
        int bVBefore = victim.CurCombatUnit.BuffSystem.ActiveBuffCount;
        int bCBefore = caster.CurCombatUnit.BuffSystem.ActiveBuffCount;
        RunEffect(2, caster, victim, victim, caster);
        int bVAfter = victim.CurCombatUnit.BuffSystem.ActiveBuffCount;
        int bCAfter = caster.CurCombatUnit.BuffSystem.ActiveBuffCount;
        Debug.Log($"◆实验B m_SkillTarget=caster，AddBuff 效果 ctx.Target=victim：victim {bVBefore}→{bVAfter} / caster {bCBefore}→{bCAfter} ⇒ "
                  + (bVAfter > bVBefore && bCAfter == bCBefore ? "★只作用于 victim（正确）" : "✗目标来源仍不对"));

        // ── 实验 C：效果 3（Sneer）作用于 ctx.Target = victim
        victim.m_Target = null;
        RunEffect(3, caster, victim, victim, null);
        string got = victim.m_Target == null ? "null" : victim.m_Target.name;
        Debug.Log($"◆实验C m_SkillTarget=null，Sneer 效果 ctx.Target=victim：victim.m_Target={got} ⇒ "
                  + (victim.m_Target == caster ? "★嘲讽作用于 victim（正确）" : "✗嘲讽未作用于 victim"));

        // ── 实验 D：效果 0（ImmedDamage）判空 —— ctx.Target = null 必须不抛异常
        try
        {
            RunEffect(0, caster, victim, null, victim);
            Debug.Log("◆实验D ImmedDamage 效果 ctx.Target=null：未抛异常 ⇒ ★判空生效（改造前此处会 NRE）");
        }
        catch (Exception e)
        {
            Debug.LogError($"◆实验D ImmedDamage 效果 ctx.Target=null：抛出异常 ⇒ ✗判空未生效 —— {e.GetType().Name}: {e.Message}");
        }

        // 收尾：清掉探针造成的 Buff，不污染场景
        victim.CurCombatUnit.BuffSystem.ClearAllBuffs();
        caster.CurCombatUnit.BuffSystem.ClearAllBuffs();
        Debug.Log("========== [SkillTargetProbe] 结束（已清理临时 Buff） ==========");
    }

    /// <summary>
    /// 从注册表取效果类并执行 —— 与游戏运行时同一份代码路径。
    /// </summary>
    /// <param name="target">ctx.Target（本次命中目标）</param>
    /// <param name="skillTargetField">Skill.m_SkillTarget 字段值，仅作对照（效果类不该读它）</param>
    static void RunEffect(int effectId, CombatUnitController caster, CombatUnitController victim,
                          CombatUnitController target, CombatUnitController skillTargetField)
    {
        ISkillEffect effect;
        if (!SkillEffectRegistry.TryGet(effectId, out effect))
        {
            Debug.LogError($"[SkillTargetProbe] 效果号 {effectId} 未注册（注册表未初始化？）—— 实验不成立");
            return;
        }

        var skill = new Skill(PROBE_SKILL_ID);
        SetField(skill, "m_SkillCaseter", caster);
        SetField(skill, "m_SkillTarget", skillTargetField);
        skill.BaseData.BuffID = new List<int> { PROBE_BUFF_ID };

        // ★ 2026-09-28：上下文改为只传 skill，去掉重复抄写的三个参数。
        //   但段号必须仍为 1（与旧版 new SkillCastContext(caster, skill, skill.BaseData, 1) 等价）——
        //   SegIdx 的 setter 自 v5 P16 起是 private，这里沿用本文件既有的反射手法直写字段。
        //   ⚠ 顺序敏感：SkillCastContext 在**构造瞬间**对段号取快照，必须在 new 之前写。
        SetField(skill, "m_SegIdx", 1);

        var cast = new SkillCastContext(skill);
        effect.Execute(in cast, target);
    }

    static void SetField(object obj, string name, object value)
    {
        var f = obj.GetType().GetField(name, FL);
        if (f == null)
        {
            Debug.LogError($"[SkillTargetProbe] 反射找不到字段 {name}（改名了？）—— 实验结论不成立");
            return;
        }
        f.SetValue(obj, value);
    }
}
