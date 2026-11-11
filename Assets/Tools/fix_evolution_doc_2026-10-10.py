# -*- coding: utf-8 -*-
"""
修复《战斗框架演化全史_2026-10-10.html》三处标签问题：
  1) 第 04 节首个 .evo 块缺少收尾 </div>（div 配平差 1）
  2) 第 07 节 P14「正确边界」行：误写 </strong> 应为 </code>
  3) 第 12 节收尾契约行：外层 <strong> 未闭合 + 拼接产生的语病

工程纪律：old 命中数断言 == 1；写回后从磁盘重读复验；幂等（new 已存在即跳过）。
"""
import io
import os
import sys

P = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                 "战斗框架演化全史_2026-10-10.html")


def patch(text, old, new, tag):
    if new in text:
        print("[SKIP] %s : new 已存在，判定为已修复" % tag)
        return text, False
    n = text.count(old)
    assert n == 1, "[FAIL] %s : old 命中数 == %d（期望 1）" % (tag, n)
    print("[ OK ] %s : 命中 1 处，已替换" % tag)
    return text.replace(old, new), True


raw = io.open(P, "r", encoding="utf-8").read()

# 1) 第 04 节首个 .evo 块收尾
old1 = """        <p class="callout warn" style="margin-top:10px;">⚠ 这个「自然重置」的假设在下一轮就被证伪了。</p>
      </div>
    </div>
  </div>

  <div class="evo">
    <div class="evo-head">
      <span class="evo-title">v2：SetSkillCaster 每次都重置"""
new1 = """        <p class="callout warn" style="margin-top:10px;">⚠ 这个「自然重置」的假设在下一轮就被证伪了。</p></div>
      </div>
    </div>
  </div>
  </div>

  <div class="evo">
    <div class="evo-head">
      <span class="evo-title">v2：SetSkillCaster 每次都重置"""

# 2) 第 07 节： </strong> -> </code>
old2 = "<code>SilenceState.OnExit → Owner.IsSilenced = false</strong>，漏掉会让单位永远放不出技能）"
new2 = "<code>SilenceState.OnExit → Owner.IsSilenced = false</code>，漏掉会让单位永远放不出技能）"

# 3) 第 12 节：补闭合 + 修语病
old3 = "<p><strong>原框架为什么不清空（五层原因，用户追问的原因）：① 原框架姿态是"
new3 = "<p><strong>原框架为什么不清空（五层原因，用户追问后整理）：</strong>① 原框架姿态是"

text = raw
for old, new, tag in ((old1, new1, "FIX-1 第04节 evo 收尾"),
                      (old2, new2, "FIX-2 第07节 code 闭合"),
                      (old3, new3, "FIX-3 第12节 strong 闭合")):
    text, _ = patch(text, old, new, tag)

if text != raw:
    io.open(P, "w", encoding="utf-8", newline="").write(text)
    print("[WRITE] 已写回磁盘")
else:
    print("[NOOP ] 无改动")

# ---------- 写回复验 ----------
verify = io.open(P, "r", encoding="utf-8").read()
for probe, tag in ((new1, "FIX-1"), (new2, "FIX-2"), (new3, "FIX-3")):
    assert probe in verify, "[VERIFY-FAIL] %s 不在磁盘内容中" % tag
    print("[VERIFY] %s 已在磁盘中" % tag)

# div 配平自查
import re
opens = len(re.findall(r"<div\b", verify))
closes = len(re.findall(r"</div>", verify))
print("[CHECK ] <div> = %d, </div> = %d, 差 = %d" % (opens, closes, opens - closes))
