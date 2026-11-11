# -*- coding: utf-8 -*-
"""2026-09-24 给 BuffContractCheck 增加第⑧段：语义级施加入口（ApplyStun/ApplySilence）的 Tags 反查验证"""
import io

path = r"E:\Project\BttleFreamwork-v1\Assets\Editor\BuffDiagnostics\BuffContractCheck.cs"
t = io.open(path, encoding="utf-8", newline="").read()
crlf = "\r\n" in t
text = t.replace("\r\n", "\n")

old = """        sys.ClearAllBuffs();
        Debug.Log("========== [BuffContractCheck] end ==========");"""

new = """        // ⑧ ★ 语义级施加（ApplyStun / ApplySilence）的 Tags 反查 —— 2026-09-24 修复的核心
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
        Debug.Log("========== [BuffContractCheck] end ==========");"""

n = text.count(old)
assert n == 1, "锚点命中 %d 次（应为 1）" % n
text = text.replace(old, new)

if crlf:
    text = text.replace("\n", "\r\n")
io.open(path, "w", encoding="utf-8", newline="").write(text)

back = io.open(path, encoding="utf-8", newline="").read().replace("\r\n", "\n")
assert "◆语义路由" in back and "◆路由真相" in back and "◆端到端" in back, "写回复验失败"
print("OK BuffContractCheck.cs — 已插入第⑧段（Lines=%d, crlf=%s）" % (back.count("\n") + 1, "\r\n" in io.open(path, encoding="utf-8", newline="").read()))
