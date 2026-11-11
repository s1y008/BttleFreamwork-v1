# -*- coding: utf-8 -*-
"""
上一轮 FIX-1 多补了一个 </div>（div 519 / /div 520，差 -1）。
本轮回退：把行内多出的那个 </div> 去掉，只保留独立补的那一个。
"""
import io
import os
import re

P = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                 "战斗框架演化全史_2026-10-10.html")

old = u"⚠ 这个「自然重置」的假设在下一轮就被证伪了。</p></div>"
new = u"⚠ 这个「自然重置」的假设在下一轮就被证伪了。</p>"

raw = io.open(P, "r", encoding="utf-8").read()
if new in raw and old not in raw:
    print("[SKIP] 已修复")
else:
    n = raw.count(old)
    assert n == 1, "[FAIL] 命中数 == %d（期望 1）" % n
    raw = raw.replace(old, new)
    io.open(P, "w", encoding="utf-8", newline="").write(raw)
    print("[ OK ] 已回退行内多余的 </div>")

v = io.open(P, "r", encoding="utf-8").read()
assert new in v, "[VERIFY-FAIL]"
o = len(re.findall(r"<div\b", v))
c = len(re.findall(r"</div>", v))
print("[CHECK ] <div> = %d, </div> = %d, 差 = %d" % (o, c, o - c))
assert o == c, "[FAIL] div 仍不配平"
print("[VERIFY] div 配平 OK")
