using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using BattleFramework.Core;

/// <summary>
/// Buff 契约自检（Editor 菜单：Tools/Diag/BuffContractCheck）
/// 用途：每次改完 BufferData.csv 或 BuffType 枚举后跑一次，实锤四件事：
///   ① Id 主键可用（AddBuff(int) 取到正确配置行）
///   ② 按 Type 施加的兼容入口仍可用
///   ③ 移除 Buff 后标签位图按存活Buff重算（同位Buff不会被误清位）
///   ④ 免疫链路是否需要补 CSV 行（ControlImmune 缺行时会明确提示）
/// 幂等：只读配置 + 用临时 BuffSystem 实例，不写任何资产、不影响场景。
/// </summary>
public static class BuffContractCheck
{
    [MenuItem("Tools/Diag/BuffContractCheck")]
    public static void Run()
    {
        Debug.Log("========== [BuffContractCheck] begin ==========");

        // ① 配置加载：Id 是主键
        for (int id = 1001; id <= 1006; id++)
        {
            BuffData d = ConfigManager.GetBuffData(id);
            Debug.Log("[BuffContractCheck] id=" + id + " -> "
                + (d == null ? "NULL（CSV 缺该行）"
                             : d.Name + " | Type=" + d.Type + " | Cat=" + d.Category
                               + " | Tags=[" + string.Join(";", d.Tags) + "]"));
        }

        // ② 按 Id 施加 + 位图表现
        BuffSystemPooled sys = new BuffSystemPooled(9999);
        Buff b1 = sys.AddBuff(1005, 1);   // Stunned（CSV 类别=Control）
        Debug.Log("[BuffContractCheck] AddBuff(1005) -> " + (b1 == null ? "null" : b1._BuffData.Name)
            + " | flags=" + sys.ActiveBuffFlags
            + " | HasAnyHardControl=" + sys.HasAnyBuff(BuffType.HardControl)
            + " | IsControlled=" + sys.IsControlled());

        // ③ 移除后重算位图：再加一个共享控制位的Buff，移除它不应把 Stunned 位清掉
        Buff b2 = sys.AddBuff(1006, 1);   // Silenced
        Debug.Log("[BuffContractCheck] +AddBuff(1006) -> " + (b2 == null ? "null" : b2._BuffData.Name)
            + " | flags=" + sys.ActiveBuffFlags);
        sys.RemoveBuffById(1006);
        Debug.Log("[BuffContractCheck] -RemoveBuffById(1006) -> flags=" + sys.ActiveBuffFlags
            + " | Stunned仍在=" + sys.HasBuff(BuffType.Stunned)
            + "（true=重算生效；若仍用 &=~Type 清位，这里会变成 false，即仍生效的Buff被误清）");

        // ④ 按 Type 的兼容入口
        Buff b3 = sys.AddBuff(BuffType.Poisoned, 1);
        Debug.Log("[BuffContractCheck] AddBuff(BuffType.Poisoned) -> "
            + (b3 == null ? "null（CSV 无该 Type 行）" : "id=" + b3._BuffData.Id + " " + b3._BuffData.Name));

        // ⑤ Tags（行为标签）验证：★ 1B 数据驱动归类的核心
        //   关键判据：沉默不能被当成硬控（不定身、仍可移动）
        BuffSystemPooled sys3 = new BuffSystemPooled(9997);

        sys3.AddBuff(1005, 1);   // Tags = Control;HardControl;MovementImpair
        Debug.Log("[BuffContractCheck] ★Tags 挂1005(眩晕) -> HardControl="
            + sys3.HasBuffWithTag(BuffTags.HardControl) + " | MovementImpair="
            + sys3.HasBuffWithTag(BuffTags.MovementImpair) + " | CanMove=" + sys3.CanMove()
            + "（CanMove 应 False：眩晕定身）");
        sys3.ClearAllBuffs();

        sys3.AddBuff(1006, 1);   // Tags = Control;Silence
        Debug.Log("[BuffContractCheck] ★Tags 挂1006(沉默) -> Silence="
            + sys3.HasBuffWithTag(BuffTags.Silence) + " | HardControl="
            + sys3.HasBuffWithTag(BuffTags.HardControl) + "（必须 False：沉默不定身）"
            + " | CanMove=" + sys3.CanMove() + "（必须 True：沉默仍可移动）");
        sys3.ClearAllBuffs();

        sys3.AddBuff(1003, 1);   // Tags = DoT
        Debug.Log("[BuffContractCheck] ★Tags 挂1003(流血) -> DoT="
            + sys3.HasBuffWithTag(BuffTags.DoT) + " | CanMove=" + sys3.CanMove() + "（应 True）");
        sys3.ClearAllBuffs();

        // ⑤·补 全表数据驱动验证：把 BufferData.json 每一行都施加一遍，
        //      校验「Tags 里声明了什么行为」与「系统判定的行为」是否一致
        //      ★ 这一段对新加的配置行自动生效 —— 验证 1B「新增Buff只改配置不改代码」就靠它
        //      ★ 2026-09-28 CSV→JSON：改经 ConfigManager 全表口遍历，不再手工解析文本
        List<BuffData> allBuffRows = ConfigManager.GetAllBuffData();
        for (int i = 0; i < allBuffRows.Count; i++)
        {
            BuffData row = allBuffRows[i];
            if (row == null) continue;

            bool tagHard = row.HasTag(BuffTags.HardControl);
            bool tagMove = row.HasTag(BuffTags.MovementImpair);
            bool tagSilence = row.HasTag(BuffTags.Silence);

            BuffSystemPooled s = new BuffSystemPooled(9000 + row.Id);
            if (s.AddBuff(row.Id, 1) == null)
            {
                Debug.LogError("[BuffContractCheck] id=" + row.Id + " 施加失败（被免疫或配置缺失）");
                s.ClearAllBuffs();
                continue;
            }

            bool okHard = s.HasBuffWithTag(BuffTags.HardControl) == tagHard;
            bool okMove = s.CanMove() == !tagMove;
            bool okSil  = s.HasBuffWithTag(BuffTags.Silence) == tagSilence;
            Debug.Log("[BuffContractCheck] ◆数据驱动 id=" + row.Id + " Tags=[" + string.Join(";", row.Tags) + "]"
                + " → 硬控判定" + (okHard ? "一致" : "★不一致")
                + " / 移动判定" + (okMove ? "一致" : "★不一致")
                + " / 沉默判定" + (okSil ? "一致" : "★不一致"));
            s.ClearAllBuffs();
        }

        // ⑥ 归类契约反向验证：造一条「类别=Control 但类型没登记进 AllControlEffects」的假数据，
        //    反射调用加载期同一份校验 → 应当报错指名
        //    （证明：以后新增 BuffType 而忘了归类，开机就会被这条 Error 抓住，而不是等到免疫失效才发现）
        object msg = typeof(ConfigManager)
            .GetField("_buffMsg", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .GetValue(null);
        System.Reflection.MethodInfo validate = typeof(BuffMsg).GetMethod("ValidateCategoryContract",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        BuffData fake = new BuffData();
        fake.Id = 9999;
        fake.Type = BuffType.Berserk;           // 增益位，不在 AllControlEffects 内
        fake.Category = BuffCategory.Control;   // 却声明为控制类
        validate.Invoke(msg, new object[] { fake });
        Debug.Log("[BuffContractCheck] 反向验证已执行 → 上面应出现一条 [BuffData] ID=9999 ... AllControlEffects 的报错，出现即代表归类契约生效");

        // ⑦ 免疫链路可达性：没有 ControlImmune 配置行就无法实测，明确指名
        BuffData immuneRow = ConfigManager.GetBuffData(BuffType.ControlImmune);
        if (immuneRow == null)
        {
            Debug.LogWarning("[BuffContractCheck] BufferData.csv 没有 ControlImmune 配置行 → "
                + "「免疫控制」链路无法实测。补一行即可验证，例如：1007,ControlImmune,免疫控制,Positive,-1,1,0,0,0,0,0,,,,免疫一切控制");
        }
        else
        {
            BuffSystemPooled sys2 = new BuffSystemPooled(9998);
            sys2.AddBuff(immuneRow.Id, 1);
            Buff blocked = sys2.AddBuff(1005, 1);   // 应被免疫
            Debug.Log("[BuffContractCheck] 免疫验证：挂上 ControlImmune 后 AddBuff(1005 Stunned) -> "
                + (blocked == null ? "null（被免疫 ✓）" : "加上了 ✗，检查 IsImmuneTo 与 AllControlEffects"));
            sys2.ClearAllBuffs();
        }

        // ⑧ ★ 语义级施加（ApplyStun / ApplySilence）的 Tags 反查 —— 2026-09-24 修复的核心
        //    修复前这两个入口硬编码 BuffType.Stunned / Silenced 走 AddBuff(BuffType)，
        //    导致 ① Tags 数据驱动对这条路径整体失效 ② 同位的第二个 Buff 永远摸不到。
        //    现在改为 ConfigManager.GetBuffDataByTag(tag) —— 全程不碰枚举位。
        BuffData stunRow = ConfigManager.GetBuffDataByTag(BuffTags.HardControl);
        BuffData silRow  = ConfigManager.GetBuffDataByTag(BuffTags.Silence);
        Debug.Log("[BuffContractCheck] ◆语义路由 ApplyStun    -> GetBuffDataByTag(HardControl) = "
            + (stunRow == null ? "NULL（无任何行声明该标签 ⇒ ApplyStun 会 fail-fast 报 Error 指名）"
                               : "id=" + stunRow.Id + " " + stunRow.Name + " Type=" + stunRow.Type));
        Debug.Log("[BuffContractCheck] ◆语义路由 ApplySilence -> GetBuffDataByTag(Silence) = "
            + (silRow == null ? "NULL（无任何行声明该标签 ⇒ ApplySilence 会 fail-fast 报 Error 指名）"
                              : "id=" + silRow.Id + " " + silRow.Name + " Type=" + silRow.Type));

        // ⑧·补 关键判据：拿到的这一行**是不是真的靠 Tags 命中**（而不是碰巧 Type 带着对应枚举位）
        if (stunRow != null)
        {
            bool byBit = (BuffType.HardControl & stunRow.Type) != 0;
            Debug.Log("[BuffContractCheck] ◆路由真相 id=" + stunRow.Id + " 命中来源="
                + (byBit ? "BuffType 位（枚举靠得住，但换一个不带该位的行就立刻失效）"
                         : "★Tags 列（完全不依赖枚举位 —— 这才证明漏洞已堵）")
                + " | 行内 Tags=[" + string.Join(";", stunRow.Tags) + "]");
        }

        // ⑧·补2 未知标签必须返回 null ⇒ ApplyControlBuff 走 fail-fast 分支报 Error，不静默降级
        BuffData bogus = ConfigManager.GetBuffDataByTag("__NoSuchTag__");
        Debug.Log("[BuffContractCheck] ◆未知标签 -> "
            + (bogus == null ? "null ✓（ApplyControlBuff 会 LogError 指名）"
                             : "竟然查到了 id=" + bogus.Id + " ✗"));

        // ⑧·补3 端到端：模拟 ApplyControlBuff 的两步（按 Tags 查行 → 按 Id 施加）
        if (stunRow != null)
        {
            BuffSystemPooled sys4 = new BuffSystemPooled(9996);
            Buff applied = sys4.AddBuff(stunRow.Id, 1);
            Debug.Log("[BuffContractCheck] ◆端到端 ApplyStun 模拟 -> AddBuff(id=" + stunRow.Id + ") = "
                + (applied == null ? "null ✗" : applied._BuffData.Name + " ✓")
                + " | HardControlTag=" + sys4.HasBuffWithTag(BuffTags.HardControl)
                + " | CanMove=" + sys4.CanMove() + "（应 False）");
            sys4.ClearAllBuffs();
        }

        sys.ClearAllBuffs();
        Debug.Log("========== [BuffContractCheck] end ==========");
    }
}
