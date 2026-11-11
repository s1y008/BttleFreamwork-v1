# -*- coding: utf-8 -*-
"""
补「新建技能未落盘」的主窗口提示
——————————————————————————————
为什么必须补：
    SkillEditorModel 的新建项只存在于编辑器内存的 _rows / _byId 缓存里
   （ConfigManager.GetAllSkillData() 没有接纳它的入口）。一旦 Refresh()
    被触发（重编译 / 进 Play / 重载配置），缓存按磁盘数据重建，那条技能连同
    用户已经填好的字段一起消失，且**不报任何错** —— 标准的静默丢失。

    向导窗口已经提示过两次（SkillCreateWizard.cs:211 / :213），但用户回到主窗口后
    就看不见了。主窗口的「未保存改动」清单是拦住它的最后一道闸，
    这里必须把「新建·未落盘」和「改了没存」区分开。

铁律：
    ① old 命中数必须 == 1
    ② 幂等 —— new 已存在即跳过
    ③ 写回后重读复验
    ④ 延迟 2 秒二次复验（防磁盘静默回退）
"""
import io
import os
import shutil
import sys
import time

TARGET = r"E:\Project\BttleFreamwork-v1\Assets\Editor\SkillEditor\SkillEditorWindow.cs"
BAK_TAG = "newnotice"

# ── old / new ────────────────────────────────────────────────────────────────
OLD = '''            EditorGUILayout.LabelField("未保存改动（" + ids.Count + " 个技能）", EditorStyles.boldLabel);
            for (int i = 0; i < ids.Count && i < 12; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("技能 " + ids[i]);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("保存", GUILayout.Width(50)))
                {
                    SkillData d = SkillEditorModel.ById(ids[i]);
                    if (d != null) DoSave(d);
                }
                EditorGUILayout.EndHorizontal();
            }
            if (ids.Count > 12) EditorGUILayout.LabelField("... 还有 " + (ids.Count - 12) + " 个");'''

NEW = '''            EditorGUILayout.LabelField("未保存改动（" + ids.Count + " 个技能）", EditorStyles.boldLabel);
            bool anyNew = false;
            for (int i = 0; i < ids.Count && i < 12; i++)
            {
                int id = ids[i];

                // ★ 区分「改了没存」与「新建未落盘」：
                //   后者不在磁盘上，Refresh() 重建缓存时会连同用户填好的字段一起消失且不报错，
                //   必须在清单里单独标红，否则用户以为它还在、继续往下配。
                bool isNew = SkillEditorModel.IsNew(id);
                if (isNew) anyNew = true;

                EditorGUILayout.BeginHorizontal();
                Color prevColor = GUI.color;
                if (isNew) GUI.color = StubColor;
                EditorGUILayout.LabelField(isNew ? ("技能 " + id + "（新建·未落盘）") : ("技能 " + id));
                GUI.color = prevColor;
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("保存", GUILayout.Width(50)))
                {
                    SkillData d = SkillEditorModel.ById(id);
                    if (d != null) DoSave(d);
                }
                EditorGUILayout.EndHorizontal();
            }
            if (ids.Count > 12) EditorGUILayout.LabelField("... 还有 " + (ids.Count - 12) + " 个");

            // ★ 最后一道闸：新建技能只活在编辑器内存里。
            //   重编译 / 进 Play / 点「重载配置」都会按磁盘数据重建缓存 ⇒ 它会被静默丢掉。
            //   这句话必须在「全部保存」按钮之前说清楚，否则用户点了保存也没意识到紧迫性。
            if (anyNew)
            {
                EditorGUILayout.HelpBox("带「新建·未落盘」标记的技能目前只存在于编辑器内存，"
                    + "重编译 / 进入 Play / 重载配置都会丢失。请先点「保存」把它写进 SkillData.json。",
                    MessageType.Warning);
            }'''


def read_text(path):
    with io.open(path, "r", encoding="utf-8-sig", newline="") as f:
        return f.read()


def write_text(path, text):
    with io.open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text)


def main():
    if not os.path.isfile(TARGET):
        print("[FAIL] 目标文件不存在: %s" % TARGET)
        return 1

    src = read_text(TARGET)
    print("[INFO] 原始大小: %d 字符" % len(src))

    # ── 幂等判据：new 已存在即跳过 ──────────────────────────────────────────
    if "（新建·未落盘）" in src:
        print("[SKIP] 已经补过了（检测到「（新建·未落盘）」），不重复写入。")
        return 0

    # ── old 命中数必须 == 1 ────────────────────────────────────────────────
    hits = src.count(OLD)
    print("[INFO] old 命中数: %d" % hits)
    if hits != 1:
        print("[FAIL] old 命中数 %d != 1 —— 拒绝改写。请先人工核对 DrawDirtyFooter 的内容。" % hits)
        return 1

    # ── 备份（标签写「改动后状态」） ────────────────────────────────────────
    bak = TARGET + ".bak.2026-10-14-" + BAK_TAG
    if not os.path.isfile(bak):
        shutil.copy2(TARGET, bak)
        print("[BAK ] 已备份 -> %s" % bak)
    else:
        print("[BAK ] 备份已存在，不覆盖: %s" % bak)

    out = src.replace(OLD, NEW, 1)
    if out == src:
        print("[FAIL] 替换未生效")
        return 1

    write_text(TARGET, out)
    print("[OK  ] 已写入，新大小: %d 字符（+%d）" % (len(out), len(out) - len(src)))

    # ── 写回后重读复验 ─────────────────────────────────────────────────────
    chk = read_text(TARGET)
    ok = ("（新建·未落盘）" in chk) and ("bool anyNew = false;" in chk) and (chk.count(OLD) == 0)
    print("[CHK1] 复验：%s" % ("通过" if ok else "失败"))
    if not ok:
        print("[FAIL] 第一次复验未通过，请人工检查文件")
        return 1

    # ── 延迟 2 秒二次复验（防磁盘静默回退） ────────────────────────────────
    time.sleep(2)
    chk2 = read_text(TARGET)
    ok2 = ("（新建·未落盘）" in chk2) and len(chk2) == len(chk)
    print("[CHK2] 延迟复验：%s（大小 %d）" % ("通过" if ok2 else "失败", len(chk2)))
    if not ok2:
        print("[FAIL] 二次复验未通过 —— 可能发生磁盘静默回退")
        return 1

    print("[DONE] 完成")
    return 0


if __name__ == "__main__":
    sys.exit(main())
