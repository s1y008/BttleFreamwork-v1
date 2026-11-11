# -*- coding: utf-8 -*-
"""
2026-09-28 删除 SkillData.csv 第 25 列（1-based）= 下标 24 = HitEffectIds。

背景：受击特效已于 2026-09-28 统一收敛到列 14（1-based 第 15 列 HitEffect），
      原第 25 列 HitEffectIds 已无任何读取者，属死配置，整列删除。

影响：删列后其后所有列下标整体前移一位
      旧 idx25 CastEffectTimings -> 新 idx24（1-based 第 25 列）
      旧 idx26 SegCount          -> 新 idx25（1-based 第 26 列）
      总列数 27 -> 26

安全约束（踩坑总结）：
  1. 二进制读写：文件有 UTF-8 BOM 且为 CRLF，文本模式会破坏 BOM/换行
  2. 幂等：表头里已无 HitEffectIds 则直接跳过（防重复执行）
  3. 每行列数必须等于表头列数，不等则中止（不静默降级）
  4. 表头第 25 列必须是 HitEffectIds，否则中止
"""
import io
import os
import sys

SRC = r"E:\Project\BttleFreamwork-v1\Assets\Resources\Config\SkillData.csv"
I_DROP = 24  # 0-based，1-based 第 25 列

if not os.path.exists(SRC):
    raise SystemExit("源文件不存在: %s" % SRC)

with open(SRC, "rb") as f:
    raw = f.read()

# 统一按 \n 切分后再去掉尾部 \r，写回时统一补 \r\n（原文件是 CRLF）
has_crlf = b"\r\n" in raw
norm = raw.replace(b"\r\n", b"\n")
lines = norm.split(b"\n")

# 去掉末尾空行，记录原尾部是否带换行（保持原样）
trailing = b""
if lines and lines[-1] == b"":
    lines = lines[:-1]
    trailing = b"\r\n" if has_crlf else b"\n"

header = lines[0].split(b",")
print("原表头列数 =", len(header))
print("表头第25列(1-based) =", header[I_DROP].decode("utf-8"))

# ---- 幂等：已删过就退出 ----
if header[I_DROP] != b"HitEffectIds":
    print("[SKIP] 表头第25列已不是 HitEffectIds（可能已删除），脚本跳过，不做任何修改")
    sys.exit(0)

# ---- 校验所有行列数一致 ----
bad = []
for i, ln in enumerate(lines):
    cols = ln.split(b",")
    if len(cols) != len(header):
        bad.append((i + 1, len(cols)))
if bad:
    raise SystemExit("列数异常行（行号, 实际列数）前10: %r，已中止，未写入任何内容" % bad[:10])

# ---- 执行删列 ----
out = []
for ln in lines:
    cols = ln.split(b",")
    del cols[I_DROP]
    out.append(b",".join(cols))

new_raw = (b"\r\n" if has_crlf else b"\n").join(out) + trailing

# ---- 写回（二进制，保留 BOM 原样）----
with open(SRC, "wb") as f:
    f.write(new_raw)

# ---- 写回复验：重读磁盘 ----
with open(SRC, "rb") as f:
    chk = f.read()
chk_lines = chk.replace(b"\r\n", b"\n").split(b"\n")
chk_lines = [x for x in chk_lines if x != b""]
chk_header = chk_lines[0].split(b",")

assert len(chk_header) == len(header) - 1, "复验失败：表头列数 %d != %d" % (len(chk_header), len(header) - 1)
assert b"HitEffectIds" not in chk_header, "复验失败：表头仍含 HitEffectIds"
assert chk_header[24].decode("utf-8") == "CastEffectTimings", "复验失败：新第25列=%r" % chk_header[24]
assert chk_header[25].decode("utf-8") == "SegCount", "复验失败：新第26列=%r" % chk_header[25]

bad2 = [(i + 1, len(x.split(b","))) for i, x in enumerate(chk_lines) if len(x.split(b",")) != len(chk_header)]
assert not bad2, "复验失败：列数不齐 %r" % bad2[:5]

print("OK 列数 %d -> %d" % (len(header), len(chk_header)))
print("新表头 =", chk_header.decode("utf-8") if False else ",".join(c.decode("utf-8") for c in chk_header))
print("数据行数 =", len(chk_lines) - 1)
print("BOM 保留 =", chk.startswith(b"\xef\xbb\xbf"))
print("CRLF 保留 =", b"\r\n" in chk)
