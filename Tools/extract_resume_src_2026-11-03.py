# -*- coding: utf-8 -*-
"""从 STAR 演化全史 HTML 抽取「面试叙事」原始素材（纯文本），供 B 版简历改写使用。"""
import re, io, os

SRC = r"E:/Project/BttleFreamwork-v1/Assets/战斗框架演化全史_STAR版_2026-11-03.html"
OUT = r"E:/Project/BttleFreamwork-v1/Tools/_resume_src.txt"

s = io.open(SRC, encoding="utf-8").read()

def clean(frag):
    frag = re.sub(r"<br\s*/?>", "\n", frag, flags=re.I)
    frag = re.sub(r"</li>", "\n", frag, flags=re.I)
    frag = re.sub(r"<[^>]+>", "", frag)
    frag = (frag.replace("&lt;", "<").replace("&gt;", ">")
                .replace("&amp;", "&").replace("&nbsp;", " ")
                .replace("&quot;", '"'))
    lines = [l.strip() for l in frag.splitlines()]
    return "\n".join(l for l in lines if l)

lines = []
lines.append("===== STAR 卡全表 =====")
for m in re.finditer(r'<div class="star-card[^"]*" id="(p\d\d)">(.*?)(?=<div class="star-card|\n</section>)', s, re.S):
    cid, body = m.group(1), m.group(2)
    t = re.search(r'class="star-title">(.*?)</span>', body, re.S)
    d = re.search(r'class="star-date">(.*?)</span>', body, re.S)
    lines.append("\n### %s  %s  %s" % (cid, clean(t.group(1)) if t else "?", clean(d.group(1)) if d else ""))
    for rm in re.finditer(r'<div class="star-row"><div class="star-k [^"]*">(.*?)</div><div class="star-v">(.*?)</div></div>', body, re.S):
        k, v = clean(rm.group(1)), clean(rm.group(2))
        lines.append("  [%s] %s" % (k, v.replace("\n", " / ")))

for title, start_mark, end_mark in [
    ("质量基线", "质量基线（当前实测）", "产出物全量统计"),
    ("问题解法索引", "问题 → 解法索引", "当前架构"),
    ("当前架构", "八层职责与核心契约", "七条设计原则的诞生"),
    ("工程纪律", "工程纪律", "回退 / 否决清单"),
    ("回退否决清单", "回退 / 否决清单", "遗留待办与下一步"),
]:
    a = s.find(start_mark)
    b = s.find(end_mark, a + 1)
    lines.append("\n\n===== %s =====" % title)
    if a >= 0 and b > a:
        txt = clean(s[a:b])
        for l in txt.splitlines():
            if l:
                lines.append("  " + l)

txt = "\n".join(lines)
io.open(OUT, "w", encoding="utf-8").write(txt)
print("写出:", OUT)
print("字符数:", len(txt), " 行数:", len(lines))
print("抽样（前 8 行）:")
for l in lines[:8]:
    print("  ", l[:140])
