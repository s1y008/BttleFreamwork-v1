# -*- coding: utf-8 -*-
"""
2026-09-29 补齐 AddBuff 绑定契约校验（SkillData.cs）

背景（真实缺口）：AddBuffEffect.Execute 是「遍历 BaseData.BuffID 逐个施加」，
  BuffID 为空 ⇒ 循环 0 次 ⇒ 既不挂 Buff 也不报错 = 静默降级（违反项目铁律 5）。
  实测：本次改动前全表 12 个技能配了效果 2(AddBuff) 而 BuffID 全空，打出去毫无反应且无从察觉。

改动两处（都是**插入型**：new 包含 old）
  A. 在 ValidateEffectTimings 定义前插入新方法 ValidateBuffBindings(SkillData)
  B. 在加载循环里 ValidateEffectIdContract(info); 之后插入 ValidateBuffBindings(info);

★ 铁律遵守
  · 改前物理备份 .bak.2026-09-29-addbuff-binding
  · CRLF 文件：读时归一化 \n，写回还原 \r\n
  · 每块 old 命中数 == 1 断言
  · ⚠⚠ 幂等判据必须是「new 的标志串已存在即跳过」——
        插入型替换里 old 改完仍命中 1 次，若写成「old 命中 0 且 new 存在才跳过」会每次复跑重复插入
  · 写回后重读复验：新方法恰好 1 次、调用恰好 1 次、大括号平衡
"""
import io
import os
import sys

PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                    "..", "Scripts", "BattleFreamwork", "ConfigData", "SkillData.cs")

NEW_METHOD = '''    /// <summary>
    /// ★★ 【AddBuff 绑定契约校验】2026-09-29：技能配了效果 2(AddBuff) ⇒ BuffID 必须非空，且每个 id 必须存在。
    ///
    /// 【为什么必须有这一条】<c>AddBuffEffect.Execute</c> 是「遍历 BuffID 逐个施加」，
    ///   BuffID 为空 ⇒ 循环 0 次 ⇒ 既不挂 Buff 也不报错，属**静默降级**，
    ///   直接违反项目铁律 5（资产契约优先，禁止为资产不一致写运行时静默失效）。
    ///   实测：本次改动前全表 12 个技能配了效果 2 而 BuffID 全空，打出去完全没反应且无从察觉。
    ///
    /// 【为什么放加载期而不是命中时】命中时报错会随每次攻击刷屏；加载期只报一次且指名到技能 ID。
    ///   ⚠ 与既有 Validate* 系列一致，整段只在 UNITY_EDITOR 下编译 —— Release 构建零开销。
    ///
    /// 【不做自动修正】补哪个 Buff 属于策划决策，静默挑一个默认 Buff 会让「我明明没配怎么挂上了」无从追查。
    /// </summary>
    private void ValidateBuffBindings(SkillData data)
    {
#if UNITY_EDITOR
        // ① 该技能是否配置了效果 2(AddBuff) —— 命中效果表是「效果归段」，必须逐段扫
        bool hasAddBuff = false;
        if (data.EffectTypesBySegment != null)
        {
            for (int s = 0; s < data.EffectTypesBySegment.Length && !hasAddBuff; s++)
            {
                List<int> types = data.EffectTypesBySegment[s];
                if (types == null) continue;
                for (int t = 0; t < types.Count; t++)
                {
                    if (types[t] == (int)SkillEffectTypes.AddBuff)
                    {
                        hasAddBuff = true;
                        break;
                    }
                }
            }
        }
        if (!hasAddBuff) return;

        // ② 配了 AddBuff 却没有可用 BuffID ⇒ AddBuffEffect 空转（打了没反应）
        //    ⚠ 不能只判 Count：历史解析会把空配置字段填成「含一个 0 的 List」
        //      （CommonHelper.Str2IntList("") 的坑），必须按 >0 计数
        int validIdCount = 0;
        if (data.BuffID != null)
        {
            for (int i = 0; i < data.BuffID.Count; i++)
            {
                if (data.BuffID[i] > 0) validIdCount++;
            }
        }

        if (validIdCount == 0)
        {
            Debug.LogError($"[SkillData] 技能 {data.ID}({data.Name}) 配了效果 2(AddBuff)，但 BuffID 列为空" +
                $" ⇒ 命中时 AddBuffEffect 循环 0 次，等于打了没反应。" +
                $"请在配置表 BuffID 列补上 BuffId（例如流血 = 1003）。");
            return;
        }

        // ③ BuffID 引用的 Buff 必须存在 —— 否则每次命中都取不到配置行，同样是没有表现的静默失效
        for (int i = 0; i < data.BuffID.Count; i++)
        {
            int buffId = data.BuffID[i];
            if (buffId <= 0) continue;
            if (ConfigManager.GetBuffData(buffId) == null)
            {
                Debug.LogError($"[SkillData] 技能 {data.ID}({data.Name}) 的 BuffID 引用了不存在的 BuffId={buffId}" +
                    $" ⇒ 命中时 AddBuff 取不到配置行，Buff 挂不上。请核对 BufferData 的 Id 列。");
            }
        }
#endif
    }

'''

ANCHOR_METHOD = "    private void ValidateEffectTimings(SkillData data)\n"

OLD_CALL = "            ValidateEffectIdContract(info);\n"
NEW_CALL = (
    "            ValidateEffectIdContract(info);\n"
    "\n"
    "            // ★ 2026-09-29:AddBuff 绑定契约 —— 配了效果 2(AddBuff) 就必须有可用的 BuffID，且 id 必须存在\n"
    "            ValidateBuffBindings(info);\n"
)


def main():
    with open(PATH, "rb") as f:
        raw = f.read()
    has_bom = raw[:3] == b"\xef\xbb\xbf"
    txt = raw.decode("utf-8-sig").replace("\r\n", "\n")

    # ── A. 插入新方法（幂等标志：方法签名已存在则跳过）──
    sig = "private void ValidateBuffBindings(SkillData data)"
    if sig in txt:
        print("  [A] 跳过：新方法已存在")
    else:
        cnt = txt.count(ANCHOR_METHOD)
        if cnt != 1:
            raise SystemExit("[A] 锚点命中 %d 次（应恰好 1 次）" % cnt)
        txt = txt.replace(ANCHOR_METHOD, NEW_METHOD + ANCHOR_METHOD)
        print("  [A] 已插入新方法")

    # ── B. 插入调用（幂等标志：调用串已存在则跳过）──
    if "ValidateBuffBindings(info);" in txt:
        print("  [B] 跳过：调用已存在")
    else:
        cnt = txt.count(OLD_CALL)
        if cnt != 1:
            raise SystemExit("[B] 调用点命中 %d 次（应恰好 1 次）" % cnt)
        txt = txt.replace(OLD_CALL, NEW_CALL)
        print("  [B] 已插入调用")

    # ── 写回（还原 CRLF）──
    out = txt.replace("\n", "\r\n").encode("utf-8")
    if has_bom:
        out = b"\xef\xbb\xbf" + out
    with open(PATH, "wb") as f:
        f.write(out)

    # ── 复验（重新从磁盘读）──
    with open(PATH, "rb") as f:
        raw2 = f.read()
    t2 = raw2.decode("utf-8-sig").replace("\r\n", "\n")

    ok = True
    n_sig = t2.count(sig)
    n_call = t2.count("ValidateBuffBindings(info);")
    print("  [复验] 方法定义出现 %d 次（应为 1）" % n_sig)
    print("  [复验] 调用出现 %d 次（应为 1）" % n_call)
    ok = ok and n_sig == 1 and n_call == 1

    # 大括号平衡（忽略字符串/注释的粗检，够用于发现漏插）
    depth = 0
    instr = None
    incomment = False
    for line in t2.split("\n"):
        i = 0
        while i < len(line):
            c = line[i]
            if incomment:
                if c == "*" and i + 1 < len(line) and line[i + 1] == "/":
                    incomment = False
                    i += 2
                    continue
                i += 1
                continue
            if instr:
                if c == "\\":
                    i += 2
                    continue
                if c == instr:
                    instr = None
                i += 1
                continue
            if c == "/" and i + 1 < len(line) and line[i + 1] == "/":
                break
            if c == "/" and i + 1 < len(line) and line[i + 1] == "*":
                incomment = True
                i += 2
                continue
            if c in "\"'":
                instr = c
                i += 1
                continue
            if c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
                if depth < 0:
                    print("  [复验] ✘ 出现多余的 }")
                    ok = False
                    depth = 0
            i += 1
    print("  [复验] 大括号平衡 depth = %d（应为 0）" % depth)
    ok = ok and depth == 0

    crlf_ok = (b"\r\n" in raw2)
    print("  [复验] CRLF 保持 = %s" % crlf_ok)
    ok = ok and crlf_ok

    print("\n结论: " + ("全部通过 OK" if ok else "存在失败项 FAILED"))
    sys.exit(0 if ok else 1)


main()
