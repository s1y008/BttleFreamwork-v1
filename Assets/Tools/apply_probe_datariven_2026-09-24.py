# -*- coding: utf-8 -*-
"""给 BuffContractCheck 加「全表数据驱动验证」段：CSV 每一行都施加一遍，
校验『Tags 声明的行为』与『系统判定出的行为』是否一致（对未来新增行自动生效）"""
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

t = rep(t,
"        // ⑥ 归类契约反向验证",
'''        // ⑤·补 全表数据驱动验证：把 CSV 每一行都施加一遍，
        //      校验「Tags 里声明了什么行为」与「系统判定的行为」是否一致
        //      ★ 这一段对新加的 CSV 行自动生效 —— 验证 1B「新增Buff只改CSV不改代码」就靠它
        TextAsset csv = Resources.Load<TextAsset>("Config/BufferData");
        if (csv != null)
        {
            string[] rows = csv.text.Replace("\\r\\n", "\\n").Split('\\n');
            for (int i = 1; i < rows.Length; i++)
            {
                if (string.IsNullOrEmpty(rows[i].Trim())) continue;
                string[] col = rows[i].Split(',');
                if (col.Length < 15) { Debug.LogError("[BuffContractCheck] 第" + (i + 1) + "行列数不足15，缺 Tags 列"); continue; }
                int bid;
                if (!int.TryParse(col[0], out bid)) continue;

                bool tagHard = false, tagMove = false, tagSilence = false;
                string[] tags = col[14].Split(';');
                for (int k = 0; k < tags.Length; k++)
                {
                    string tg = tags[k].Trim();
                    if (tg == BuffTags.HardControl) tagHard = true;
                    if (tg == BuffTags.MovementImpair) tagMove = true;
                    if (tg == BuffTags.Silence) tagSilence = true;
                }

                BuffSystemPooled s = new BuffSystemPooled(9000 + bid);
                if (s.AddBuff(bid, 1) == null)
                {
                    Debug.LogError("[BuffContractCheck] id=" + bid + " 施加失败（被免疫或配置缺失）");
                    s.ClearAllBuffs();
                    continue;
                }

                bool okHard = s.HasBuffWithTag(BuffTags.HardControl) == tagHard;
                bool okMove = s.CanMove() == !tagMove;
                bool okSil  = s.HasBuffWithTag(BuffTags.Silence) == tagSilence;
                Debug.Log("[BuffContractCheck] ◆数据驱动 id=" + bid + " Tags=[" + col[14] + "]"
                    + " → 硬控判定" + (okHard ? "一致" : "★不一致")
                    + " / 移动判定" + (okMove ? "一致" : "★不一致")
                    + " / 沉默判定" + (okSil ? "一致" : "★不一致"));
                s.ClearAllBuffs();
            }
        }

        // ⑥ 归类契约反向验证''',
"probe-datadriven")

with io.open(P, "w", encoding="utf-8", newline="") as f:
    f.write(t.replace("\n", "\r\n") if crlf else t)
print("OK BuffContractCheck.cs (data-driven section added)")
