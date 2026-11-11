# -*- coding: utf-8 -*-
"""2026-09-24 给 BuffContractCheck 加 Tags 验证段（中文原文字面量，避免 \\u 转义出错）"""
import io

P = r"E:\Project\BttleFreamwork-v1\Assets\Editor\BuffDiagnostics\BuffContractCheck.cs"

with io.open(P, "r", encoding="utf-8", newline="") as f:
    raw = f.read()
crlf = "\r\n" in raw
t = raw.replace("\r\n", "\n")

def rep(text, old, new, tag):
    n = text.count(old)
    assert n == 1, "ANCHOR MISS[%s]: count=%d\n%s" % (tag, n, old[:150])
    return text.replace(old, new, 1)

# 1) ① 段日志带上 Tags
t = rep(t,
'''                + (d == null ? "NULL（CSV 缺该行）" : d.Name + " | Type=" + d.Type + " | Cat=" + d.Category));''',
'''                + (d == null ? "NULL（CSV 缺该行）"
                             : d.Name + " | Type=" + d.Type + " | Cat=" + d.Category
                               + " | Tags=[" + string.Join(";", d.Tags) + "]"));''',
"probe-1")

# 2) 原「⑥ 免疫」先改成「⑦ 免疫」（先改后面的，避免编号撞车）
t = rep(t,
"        // ⑥ 免疫链路可达性",
"        // ⑦ 免疫链路可达性",
"probe-2")

# 3) 原「⑤ 归类契约反向验证」改成「⑥」
t = rep(t,
"        // ⑤ 归类契约反向验证",
"        // ⑥ 归类契约反向验证",
"probe-3")

# 4) 在「⑥ 归类契约反向验证」之前插入新的「⑤ Tags 验证」段
t = rep(t,
"        // ⑥ 归类契约反向验证",
'''        // ⑤ Tags（行为标签）验证：★ 1B 数据驱动归类的核心
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

        // ⑥ 归类契约反向验证''',
"probe-4")

with io.open(P, "w", encoding="utf-8", newline="") as f:
    f.write(t.replace("\n", "\r\n") if crlf else t)
print("OK BuffContractCheck.cs")
