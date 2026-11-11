# -*- coding: utf-8 -*-
"""
修复 SkillEditorWindow.cs：移除本工程 Odin dll 里不存在的 SirenixEditorGUI 调用。

纪律来源（用户 2026-09 起多次踩坑）：
  ① 单处 old 必须与必须命中 1 次，命中数不对就整体失败，不许猜测式替换
  ② 写回后必须重读复验（Windows 上曾出现过磁盘静默回退）
  ③ 幂等：new 已存在即跳过，重复执行不产生副作用
"""
import io
import time

P = r"E:\Project\BttleFreamwork-v1\Assets\Editor\SkillEditor\SkillEditorWindow.cs"

old1 = "            SirenixEditorGUI.HorizontalLineSeparator(new Color(0.25f, 0.26f, 0.3f), 1f);"
new1 = "            DrawSeparator();"

old2 = "        private static List<int> ParseIntList(string txt)"
new2 = (
    "        /// <summary>\n"
    "        /// 一条细分隔线。\n"
    "        /// ⚠ 刻意不用 Odin 的 SirenixEditorGUI —— 本工程这份 Odin dll 里没有这个类型\n"
    "        ///   （2026-10-12 编译实锤：error CS0103）。Odin 在这里能用的只有窗口类与菜单树，\n"
    "        ///   界面绘制 API 一律用标准 UnityEditor 的。\n"
    "        /// </summary>\n"
    "        private static void DrawSeparator()\n"
    "        {\n"
    "            Rect r = EditorGUILayout.GetControlRect(false, 1f);\n"
    "            EditorGUI.DrawRect(r, new Color(0.38f, 0.39f, 0.43f, 0.7f));\n"
    "        }\n"
    "\n"
    "        private static List<int> ParseIntList(string txt)"
)

src = io.open(P, encoding="utf-8").read()

# 只查「成员调用」形态（名字紧跟一个点），注释正文里提到该类型名是安全的、不算残留
if new1 in src and "private static void DrawSeparator()" in src and "SirenixEditorGUI." not in src:
    print("[skip] 目标状态已存在，本次无改动（幂等）")
else:
    n1 = src.count(old1)
    n2 = src.count(old2)
    assert n1 == 1, "old1 命中 %d 次，预期 1 次，拒绝替换" % n1
    assert n2 == 1, "old2 命中 %d 次，预期 1 次，拒绝替换" % n2

    out = src.replace(old1, new1).replace(old2, new2)
    assert "SirenixEditorGUI." not in out, "替换后仍有 SirenixEditorGUI 成员调用"
    io.open(P, "w", encoding="utf-8", newline="").write(out)
    print("[done] 已写入")

time.sleep(2)

verify = io.open(P, encoding="utf-8").read()
print("复验 old1 残留次数 :", verify.count(old1), "(应为 0)")
print("复验 new1 存在     :", verify.count(new1), "(应为 1)")
print("复验 new2 存在     :", verify.count("private static void DrawSeparator()"), "(应为 1)")
print("复验 SirenixEditorGUI 调用是否清零 :", "SirenixEditorGUI." not in verify, "(应为 True)")
