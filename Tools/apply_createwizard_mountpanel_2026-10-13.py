# -*- coding: utf-8 -*-
"""
给 SkillEditorWindow.cs 接入两块新 UI：
  ① 顶部「＋ 新建技能」按钮（打开 SkillCreateWizard）
  ② 右面板「挂载到单位」卡片（SkillUnitMountPanel，插在 DrawDirtyFooter 之前）
  ③ 新增 SelectSkillById / FindMenuItem —— 供向导创建后自动选中新建技能

纪律：
  · old 命中数断言 == 1（多了说明锚点不唯一，少了说明文件已被别人改过）
  · 幂等：新内容已存在就跳过，不重复插入
  · 写回后立即重读复验，延迟 2 秒再复验一次（项目曾出现磁盘静默回退）
"""
import io
import os
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TARGET = os.path.join(ROOT, "Assets", "Editor", "SkillEditor", "SkillEditorWindow.cs")
BACKUP = TARGET + ".bak.2026-10-13-createwizard-mountpanel"


def read_text(path):
    with io.open(path, "rb") as f:
        raw = f.read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    return raw.decode("utf-8-sig" if bom else "utf-8"), bom


def write_text(path, text, bom):
    data = text.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    with io.open(path, "wb") as f:
        f.write(data)


def replace_once(text, old, new, tag):
    n = text.count(old)
    if n != 1:
        raise SystemExit("[FAIL] %s：old 命中 %d 次（要求 == 1），已中止，未写文件。\n---old---\n%s" % (tag, n, old))
    return text.replace(old, new, 1)


# ── ① 字段 ────────────────────────────────────────────────────
FIELD_ANCHOR = "        private readonly HashSet<FieldGroup> _foldouts = new HashSet<FieldGroup>();\n"
FIELD_NEW = (
    "        /// <summary>「挂载到单位」卡片的自持状态（搜索词 / 滚动位置 / 可见行缓存）。</summary>\n"
    "        private SkillUnitMountPanel _mountPanel = new SkillUnitMountPanel();\n"
    "        private readonly HashSet<FieldGroup> _foldouts = new HashSet<FieldGroup>();\n"
)

# ── ② DrawEditors 顶部按钮 ────────────────────────────────────
BTN_ANCHOR = "            // 选中项以 tree 为准（BuildMenuTree 之后 Selection 才可用）\n"
BTN_NEW = "            DrawCreateButton();\n" + BTN_ANCHOR

# ── ③ 挂载卡片插在 DrawDirtyFooter 之前 ───────────────────────
CARD_ANCHOR = "            DrawGroups(data);\n            DrawDirtyFooter();\n"
CARD_NEW = "            DrawGroups(data);\n            DrawMountPanel(data);\n            DrawDirtyFooter();\n"

# ── ④ 新方法（插在 DrawHeader 之前）───────────────────────────
METHOD_ANCHOR = (
    "        // ── 顶部工具条 ──────────────────────────────────────────\n"
    "        private void DrawHeader(SkillData data)\n"
)
METHOD_NEW = '''        // ── 顶部「＋ 新建技能」按钮 ──────────────────────────
        /// <summary>
        /// 画在**自绘区开头**而不是 Odin 工具条（OdinMenuEditorWindow.DrawToolbar）里，理由有三：
        ///   ① Odin 工具条那条横向空间要与左树搜索框共享，窗口变窄时按钮会被挤没；
        ///   ② 这里与下方「技能 id + 保存 / 放弃改动」那一横条是同一套视觉，风格连续；
        ///   ③ Odin 纪律只验证了「继承 OdinMenuEditorWindow / [MenuItem] / MenuTree」这三处，
        ///      自绘区 100% 用标准 UnityEditor 控件，不引入任何未验证的 Odin 绘制 API。
        /// </summary>
        private void DrawCreateButton()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("＋ 新建技能", GUILayout.Width(110)))
            {
                SkillCreateWizard.Open(this);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            DrawSeparator();
        }

        /// <summary>「挂载到单位」卡片 —— 具体绘制收在 SkillUnitMountPanel 里，本窗口只负责挂上去。</summary>
        private void DrawMountPanel(SkillData data)
        {
            // 域重载后非序列化字段可能已被清掉，这里补一次，比在 OnEnable 里兜更可靠
            if (_mountPanel == null) _mountPanel = new SkillUnitMountPanel();
            _mountPanel.Draw(data);
        }

        // ── 向导新建后的自动选中 ──────────────────────────────
        /// <summary>
        /// 向导新建技能后调用：刷新左树并自动选中，让用户直接在右侧继续编辑。
        ///
        /// 【为什么必须同时改 MenuTree.Selection】DrawEditors 每帧都以
        ///   MenuTree.Selection.SelectedValue 为准回写 _selected，只给 _selected 赋值
        ///   会在下一帧被树里的选中值盖掉，表现为「新建完右侧仍是一片空白」。
        /// </summary>
        public void SelectSkillById(int skillId)
        {
            SkillData d = SkillEditorModel.ById(skillId);
            if (d == null)
            {
                Debug.LogError("[技能编辑器] 新建技能 " + skillId + " 后仍取不到数据 —— 无法自动选中，"
                               + "请检查 SkillEditorModel 的 CreateClone / CreateBlank 是否把新技能注册进了缓存表。");
                return;
            }

            ForceMenuTreeRebuild();
            _selected = d;

            if (MenuTree == null) return;

            OdinMenuItem item = FindMenuItem(d);
            if (item == null)
            {
                Debug.LogError("[技能编辑器] 左树里找不到刚创建的技能 " + skillId
                               + " —— 请检查 BuildMenuTree 的节点加入逻辑。");
                return;
            }
            MenuTree.Selection.Select(item);
            Repaint();
        }

        /// <summary>
        /// 在 Odin 左树里找出 value 是该技能的节点。
        /// ⚠ Odin 只给了 EnumerateTree() 这个 IEnumerable 接口，没有下标访问；
        ///   这里用显式 MoveNext（不是 foreach）走一遍，递归由 EnumerateTree 内部完成。
        ///   这是「点一次按钮」的路径，不是每帧绘制，不给 GC 添负担。
        /// </summary>
        private OdinMenuItem FindMenuItem(SkillData data)
        {
            if (MenuTree == null || data == null) return null;

            IEnumerator<OdinMenuItem> it = MenuTree.EnumerateTree().GetEnumerator();
            while (it.MoveNext())
            {
                OdinMenuItem item = it.Current;
                if (item != null && ReferenceEquals(item.Value, data)) return item;
            }
            return null;
        }

''' + METHOD_ANCHOR


def main():
    text, bom = read_text(TARGET)

    # 幂等：已经改过就直接退出
    if "private SkillUnitMountPanel _mountPanel" in text and "DrawCreateButton" in text and "SelectSkillById" in text:
        print("[SKIP] 三处改动都已在文件里，跳过（幂等）。")
        return

    if not os.path.exists(BACKUP):
        with io.open(BACKUP, "wb") as f:
            f.write(open(TARGET, "rb").read())
        print("[BACKUP] 已备份 → %s" % os.path.basename(BACKUP))
    else:
        print("[BACKUP] 备份已存在，不覆盖：%s" % os.path.basename(BACKUP))

    before = text

    if "private SkillUnitMountPanel _mountPanel" not in text:
        text = replace_once(text, FIELD_ANCHOR, FIELD_NEW, "①字段")
    if "DrawCreateButton();" not in text:
        text = replace_once(text, BTN_ANCHOR, BTN_NEW, "②按钮调用")
    if "DrawMountPanel(data);" not in text:
        text = replace_once(text, CARD_ANCHOR, CARD_NEW, "③挂载卡片调用")
    if "public void SelectSkillById" not in text:
        text = replace_once(text, METHOD_ANCHOR, METHOD_NEW, "④新方法")

    if text == before:
        raise SystemExit("[FAIL] 没有任何改动被应用，已中止。")

    write_text(TARGET, text, bom)
    print("[WRITE] 已写回 %s" % os.path.basename(TARGET))

    # 复验 1
    check, _ = read_text(TARGET)
    for marker in ("private SkillUnitMountPanel _mountPanel", "DrawCreateButton();",
                   "DrawMountPanel(data);", "public void SelectSkillById",
                   "private OdinMenuItem FindMenuItem"):
        if marker not in check:
            raise SystemExit("[FAIL] 复验失败，缺少：%s" % marker)
    print("[VERIFY-1] 五处 marker 全部命中。")

    # 复验 2：延迟 2 秒再看一次（防磁盘静默回退）
    time.sleep(2)
    check2, _ = read_text(TARGET)
    if check2 != check:
        raise SystemExit("[FAIL] 二次复验与首次内容不一致 —— 磁盘回退了，请人工处理！")
    print("[VERIFY-2] 2 秒后内容一致，改动稳定。行数 %d → %d" %
          (before.count("\n") + 1, check2.count("\n") + 1))


if __name__ == "__main__":
    main()
