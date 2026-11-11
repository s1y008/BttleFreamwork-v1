# -*- coding: utf-8 -*-
"""
修正「新建技能后自动选中」的 Odin 调用。

背景：第一版写的是 MenuTree.Selection.Select(item) —— 2026-10-13 编译实锤
  error CS1061: 'OdinMenuTreeSelection' does not contain a definition for 'Select'
用 System.Reflection.Metadata 只读扒了 Sirenix.OdinInspector.Editor.dll 的元数据确认：
  OdinMenuTreeSelection 实现的是 IList<OdinMenuItem>，可用成员只有 Add / Clear / Remove /
  IndexOf / Contains / Insert —— 选中就是「清空后加进去」。
  （OdinMenuItem 自己有 Select(1)，但参数语义未在元数据里标明，不用它。）

纪律：old 命中 == 1 + 幂等 + 写回后重读复验 + 延迟 2 秒二次复验。
"""
import io
import os
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TARGET = os.path.join(ROOT, "Assets", "Editor", "SkillEditor", "SkillEditorWindow.cs")

OLD = "            // MenuTree.Selection.Select(item);  // TEMP-DISABLED-FOR-API-PROBE\n"
NEW = (
    "            // ⚠ OdinMenuTreeSelection 是 IList<OdinMenuItem>：只有 Add / Clear / Remove，\n"
    "            //   **没有 Select 方法**（2026-10-13 编译实锤 error CS1061）。\n"
    "            //   所以「选中」= 先清空再把目标节点加进去。\n"
    "            MenuTree.Selection.Clear();\n"
    "            MenuTree.Selection.Add(item);\n"
)


def read_text(path):
    with io.open(path, "rb") as f:
        raw = f.read()
    return raw.decode("utf-8-sig" if raw.startswith(b"\xef\xbb\xbf") else "utf-8")


def main():
    text = read_text(TARGET)

    if "MenuTree.Selection.Add(item);" in text:
        print("[SKIP] 已经是修正后的写法（幂等），跳过。")
        return

    n = text.count(OLD)
    if n != 1:
        raise SystemExit("[FAIL] old 命中 %d 次（要求 == 1），已中止：\n%s" % (n, OLD))

    text = text.replace(OLD, NEW, 1)
    with io.open(TARGET, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    print("[WRITE] 已写回 SkillEditorWindow.cs")

    check = read_text(TARGET)
    for marker in ("MenuTree.Selection.Clear();", "MenuTree.Selection.Add(item);"):
        if marker not in check:
            raise SystemExit("[FAIL] 复验失败，缺少：%s" % marker)
    print("[VERIFY-1] 两处 marker 命中。")

    time.sleep(2)
    check2 = read_text(TARGET)
    if check2 != check:
        raise SystemExit("[FAIL] 二次复验不一致 —— 磁盘回退了，请人工处理！")
    print("[VERIFY-2] 2 秒后内容一致。")


if __name__ == "__main__":
    main()
