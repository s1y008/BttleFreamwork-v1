using UnityEngine;

/// <summary>
/// 内建效果注册入口 —— 经 RuntimeInitializeOnLoadMethod 自动执行，无需改动 GameStart。
/// 将来新增效果：写一个新的 ISkillEffect 实现类，然后在下面追加一行 Register(new XxxEffect())，
/// 并**把该效果号从 SkillEffectRegistry 的 KnownStubIds 里删掉**（否则配错也静默，违反铁律 5）。
/// </summary>
public static class SkillEffectSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterBuiltinEffects()
    {
        // ★ 先 Clear 再注册：Domain Reload 关闭时，若只做索引器赋值，
        //   旧实例会覆盖掉新注册的实例（改了效果类逻辑却跑的还是旧对象）。零成本防御。
        SkillEffectRegistry.Clear();

        SkillEffectRegistry.Register(new ImmedDamageEffect());   // 0  立即伤害
        SkillEffectRegistry.Register(new AddBuffEffect());       // 2  添加Buff
        SkillEffectRegistry.Register(new SneerEffect());         // 3  嘲讽
        SkillEffectRegistry.Register(new HureEffect());          // 22 受击表现

        // ★ 加载期一次性汇总「被 CSV 配置但未实现」的效果号（只报一次，不刷屏）
        SkillEffectRegistry.LogConfiguredEffectSummary();
    }
}
