# -*- coding: utf-8 -*-
"""
2026-10-12 · 补齐「补齐效果组到 N 段」按钮
【为什么补】冒烟实锤：用户已在编辑器里把 10002 段数改成 4（未保存），效果组仍为 3。
  联动只在「下一次改段数」时触发 ⇒ 存量不一致没有入口修复，必须给一键补齐。
"""
import io
import os

PATH = r"E:\Project\BttleFreamwork-v1\Assets\Editor\SkillEditor\SkillEditorWindow.cs"
BAK = PATH + ".bak.2026-10-12-fillbtn"
src = io.open(PATH, encoding="utf-8").read()

if not os.path.exists(BAK):
    io.open(BAK, "w", encoding="utf-8").write(src)
    print("已备份 →", BAK)

OLD = """            if (segs > 0 && group > segs && GUILayout.Button("截断效果组到 " + segs + " 段", GUILayout.Width(140)))
            {
                TrimEffectGroups(data, segs);
            }
"""
NEW = """            // 存量不一致的修复入口：段数已被改过（或旧数据本来就不齐）时，联动不会自动补，必须给按钮
            if (segs > 0 && group < segs && GUILayout.Button("补齐效果组到 " + segs + " 段", GUILayout.Width(150)))
            {
                SyncSegmentEffects(data);
            }
            if (segs > 0 && group > segs && GUILayout.Button("截断效果组到 " + segs + " 段", GUILayout.Width(140)))
            {
                TrimEffectGroups(data, segs);
            }
"""

if NEW in src:
    print("[幂等·跳过] 补齐按钮已存在")
else:
    assert src.count(OLD) == 1, f"命中 {src.count(OLD)} 次（期望 1）"
    out = src.replace(OLD, NEW, 1)
    io.open(PATH, "w", encoding="utf-8").write(out)
    print("[已替换] 补齐按钮")

v = io.open(PATH, encoding="utf-8").read()
print("复验 补齐按钮 :", "补齐效果组到 " in v)
print("复验 旧串残留 :", OLD in v)
