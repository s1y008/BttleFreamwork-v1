using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 技能效果注册表 —— 效果号 → 策略实现的唯一真相源（取代 Skill.cs 里 23 分支 switch）。
///
/// ★ 未注册的效果号走**三态**（用户 2026-09-24 拍板，见方案 4.7）：
///   ① 已知空桩（<see cref="IsKnownStub"/> 返回 true，含 CSV 的 -1）→ 静默跳过，
///      与旧 switch default 的「安全空操作」逐字等价；
///   ② 未知效果号 → 由调用方 LogError 指名，绝不静默降级（项目铁律 5）；
///   ③ <see cref="LogConfiguredEffectSummary"/> 在加载期一次性汇总「被配置但未实现」的清单。
/// </summary>
public static class SkillEffectRegistry
{
    private static readonly Dictionary<int, ISkillEffect> s_Effects = new Dictionary<int, ISkillEffect>();

    /// <summary>
    /// 已知空桩白名单 —— 这些效果号在 Skill.cs 里原本就是**空方法体**（打出去没反应），
    /// 未注册时静默跳过，与旧 switch default 逐字等价。
    ///
    /// 来源：SkillEffectTypes 共 23 个值（0~22），已实现 4 个（0 / 2 / 3 / 22），其余 19 个在此列。
    ///
    /// ⚠⚠ 填完某个桩并 <see cref="Register"/> 之后，**必须把它从本集合里删掉** ——
    ///    否则将来配置错（比如该效果类被误删）也会被静默跳过，违反铁律 5。
    /// </summary>
    private static readonly HashSet<int> s_KnownStubIds = new HashSet<int>
    {
        1,  4,  5,  6,  7,  8,  9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21
    };

    /// <summary>注册一个效果实现。索引器赋值 → 同 EffectId 重复注册以最后一次为准（幂等）。</summary>
    public static void Register(ISkillEffect effect)
    {
        if (effect == null)
        {
            Debug.LogError("[SkillEffect] Register 收到 null —— 拒绝注册。");
            return;
        }
        s_Effects[effect.EffectId] = effect;
    }

    /// <summary>
    /// 清空注册表。★ 必须在 Register 之前调用一次 ——
    /// Domain Reload 关闭时，若只做索引器赋值，旧实例会覆盖掉新注册的实例
    /// （表现：改了效果类的逻辑，跑的还是旧对象）。
    /// </summary>
    public static void Clear()
    {
        s_Effects.Clear();
    }

    public static bool TryGet(int effectId, out ISkillEffect effect)
    {
        return s_Effects.TryGetValue(effectId, out effect);
    }

    /// <summary>
    /// 是否为「已知空桩」 —— 命中时应当静默跳过（与旧 switch default 等价）。
    /// ⚠ 负值（CSV 用 -1 表示"该段什么都不做"）一律视为安全空操作，必须静默。
    /// </summary>
    public static bool IsKnownStub(int effectId)
    {
        if (effectId < 0) return true;
        return s_KnownStubIds.Contains(effectId);
    }

    /// <summary>
    /// ★ 加载期一次性汇总：把「CSV 配置了但尚未实现」的效果号聚合成**一份清单**，只报一次。
    ///
    /// 背景（2026-09-24 实测）：CSV 列 13 用到 21 种效果号，其中 **17 种是空桩却出现在约 175 个技能段**。
    /// 一律报错会刷屏（光 7/4/5 三个号就 109 个技能），一律静默则等于给失效盖章（撞铁律 5），
    /// 所以用「一份清单」折中：既暴露问题，又不刷屏。
    /// </summary>
    public static void LogConfiguredEffectSummary()
    {
        List<SkillData> all = ConfigManager.GetAllSkillData();
        if (all == null || all.Count == 0)
        {
            Debug.LogWarning("[SkillEffect] 加载期汇总：取不到技能配置（ConfigManager.GetAllSkillData 为空），跳过。");
            return;
        }

        // 效果号 → 出现的段次数 / 涉及技能数（同一技能内的重复段只算一次技能数）
        Dictionary<int, int> segCount = new Dictionary<int, int>();
        Dictionary<int, int> skillCount = new Dictionary<int, int>();
        HashSet<int> seenInThisSkill = new HashSet<int>();

        for (int i = 0; i < all.Count; i++)
        {
            SkillData data = all[i];
            if (data == null || data.EffectTypesBySegment == null) continue;

            seenInThisSkill.Clear();
            for (int s = 0; s < data.EffectTypesBySegment.Length; s++)
            {
                List<int> types = data.EffectTypesBySegment[s];
                if (types == null) continue;

                for (int t = 0; t < types.Count; t++)
                {
                    int id = types[t];
                    if (id < 0) continue;                       // -1 = 该段什么都不做，不参与统计

                    int c;
                    segCount.TryGetValue(id, out c);
                    segCount[id] = c + 1;

                    if (seenInThisSkill.Add(id))
                    {
                        skillCount.TryGetValue(id, out c);
                        skillCount[id] = c + 1;
                    }
                }
            }
        }

        List<int> stubIds = new List<int>();      // 已知空桩
        List<int> unknownIds = new List<int>();   // 既未注册也不在白名单 —— 真错误

        foreach (KeyValuePair<int, int> kv in skillCount)
        {
            int id = kv.Key;
            if (s_Effects.ContainsKey(id)) continue;            // 已实现，不报
            if (s_KnownStubIds.Contains(id)) stubIds.Add(id);
            else unknownIds.Add(id);
        }

        // 数值序（不是字符串序 —— 否则会排成 1,10,11,2…）
        stubIds.Sort();
        unknownIds.Sort();

        List<string> stubs = new List<string>();
        List<string> unknown = new List<string>();
        for (int i = 0; i < stubIds.Count; i++) stubs.Add(Describe(stubIds[i], skillCount, segCount));
        for (int i = 0; i < unknownIds.Count; i++) unknown.Add(Describe(unknownIds[i], skillCount, segCount));

        if (stubs.Count > 0)
        {
            Debug.LogWarning("[SkillEffect] 加载期汇总：以下效果号被 CSV 配置但**尚未实现**（空桩，命中时静默跳过，"
                + "打出去不会有表现）—— " + string.Join(" ", stubs)
                + "。属已知待填桩（方案 6.4），**不是本次重构引入**，填完记得从 KnownStubIds 里删掉。");
        }

        if (unknown.Count > 0)
        {
            Debug.LogError("[SkillEffect] 加载期汇总：以下效果号既未注册、也不在已知空桩白名单内 —— "
                + string.Join(" ", unknown)
                + "。要么补 Register，要么加进 KnownStubIds，要么修 CSV —— 不能静默跳过。");
        }
    }

    /// <summary>把效果号格式化成「4(40个技能/40段)」的形式。</summary>
    private static string Describe(int id, Dictionary<int, int> skillCount, Dictionary<int, int> segCount)
    {
        int skills, segs;
        skillCount.TryGetValue(id, out skills);
        segCount.TryGetValue(id, out segs);
        return id + "(" + skills + "个技能/" + segs + "段)";
    }
}
