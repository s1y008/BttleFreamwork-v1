# -*- coding: utf-8 -*-
"""
2026-09-24 1B：BufferData.csv 增 Tags 列（行为标签，数据驱动归类）
仅追加第 15 列（index 14），不动任何现有列的值与顺序。
"""
import io

P = r"E:\Project\BttleFreamwork-v1\Assets\Resources\Config\BufferData.csv"

with io.open(P, "r", encoding="utf-8", newline="") as f:
    raw = f.read()

lines = raw.replace("\r\n", "\n").split("\n")

assert lines[0].startswith("ID,BuffType"), "表头变了: " + lines[0]
assert "Tags" not in lines[0], "Tags 列已存在，别重复追加"

# 表头追加 Tags
lines[0] = lines[0] + ",Tags"

# 按 ID 指定行为标签（★ 只写「行为」标签，类别仍由 BuffCategory 列负责，两套不重叠）
TAGS = {
    "1001": "",                                     # 增攻：纯增益，无行为标签
    "1002": "",                                     # 加速：纯增益
    "1003": "DoT",                                  # 流血：持续伤害
    "1004": "DoT",                                  # 中毒：持续伤害
    "1005": "Control;HardControl;MovementImpair",   # 眩晕：控制 + 硬控 + 限制移动
    "1006": "Control;Silence",                      # 沉默：控制 + 沉默（不剥夺移动）
}

hit = set()
for i in range(1, len(lines)):
    if not lines[i].strip():
        continue
    bid = lines[i].split(",")[0]
    assert bid in TAGS, "未预期的 BuffId，请补进 TAGS 映射: " + bid
    lines[i] = lines[i] + "," + TAGS[bid]
    hit.add(bid)

assert hit == set(TAGS), "有行没被处理: " + str(set(TAGS) - hit)

out = "\r\n".join(lines)
with io.open(P, "w", encoding="utf-8", newline="") as f:
    f.write(out)

print("OK BufferData.csv")
for l in lines:
    print(l)
