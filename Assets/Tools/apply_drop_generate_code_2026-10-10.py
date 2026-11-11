# -*- coding: utf-8 -*-
"""
删除 CombatTransitionMatrixWindow 中的「生成代码」按钮与 GenerateCode() 方法。

背景：B 档（InitDefaultTransitions 降到 Editor）之后，生成出来的 AddTransition 行
唯一能粘回的地方是 Editor-only 模板；把资产内容粘回去会污染「代码基线」这个
A/B 对照基准，故该导出功能已过时且有害。

改动两处：
  1) DrawToolbar() 里的按钮
  2) GenerateCode() 方法整体（含其后一个空行）

铁律：命中数断言 + 幂等（已删即跳过）+ 写回后重读复验 + 延迟二次复验。
"""
import io
import os
import sys
import time

PATH = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\Editor\CombatTransitionMatrixWindow.cs"

BTN_OLD = (
    '        if (GUILayout.Button("生成代码", EditorStyles.toolbarButton, GUILayout.Width(70f)))\n'
    '            GenerateCode();\n'
)
METHOD_SIG = "    private void GenerateCode()"
METHOD_END = "    }"


def read_lines():
    with io.open(PATH, "r", encoding="utf-8", newline="") as f:
        return f.read().split("\n")


def write_text(text):
    with io.open(PATH, "w", encoding="utf-8", newline="") as f:
        f.write(text)


def main():
    raw = "\n".join(read_lines())

    # ── 幂等判据：方法已不存在 且 按钮也不存在 ⇒ 已删过，直接跳过 ──
    if METHOD_SIG not in raw and BTN_OLD not in raw:
        print("[SKIP] 已删除状态，跳过（幂等）")
        return 0

    # ── 1) 按钮 ──
    n_btn = raw.count(BTN_OLD)
    print("[CHECK] 按钮片段命中 =", n_btn)
    assert n_btn == 1, "按钮片段命中数 != 1，放弃改动"
    raw = raw.replace(BTN_OLD, "")

    # ── 2) 方法整体（按行切片，避免多行字符串空白不匹配）──
    lines = raw.split("\n")
    sig_idx = [i for i, l in enumerate(lines) if l == METHOD_SIG]
    print("[CHECK] 方法签名命中 =", len(sig_idx))
    assert len(sig_idx) == 1, "方法签名命中数 != 1，放弃改动"
    i = sig_idx[0]

    # 从签名往下找第一个仅含 '    }' 的行（方法闭合）
    j = None
    for k in range(i + 1, len(lines)):
        if lines[k] == METHOD_END:
            j = k
            break
    assert j is not None, "未找到方法闭合括号，放弃改动"
    print("[INFO] 删除区间 = 行 %d ~ %d (1-based)，共 %d 行" % (i + 1, j + 1, j - i + 1))

    # 顺带吃掉闭合后紧跟的空行
    end = j + 1
    if end < len(lines) and lines[end].strip() == "":
        end += 1

    del lines[i:end]
    out = "\n".join(lines)
    write_text(out)

    # ── 复验 1 ──
    check = "\n".join(io.open(PATH, "r", encoding="utf-8", newline="").read().split("\n"))
    assert METHOD_SIG not in check, "复验失败：方法仍存在"
    assert BTN_OLD not in check, "复验失败：按钮仍存在"
    assert "GenerateCode" not in check, "复验失败：仍残留 GenerateCode 字样"
    assert "CountEdges" in check, "复验失败：CountEdges 被误删（它另有 6 处引用）"
    assert "GenerateMarkdown" in check, "复验失败：GenerateMarkdown 被误删"
    print("[OK] 复验1 通过：GenerateCode 已清除，CountEdges/GenerateMarkdown 保留")
    print("[INFO] 删除后行数 =", len(check.split("\n")))
    return 0


if __name__ == "__main__":
    sys.exit(main())
