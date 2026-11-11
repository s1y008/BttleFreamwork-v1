# -*- coding: utf-8 -*-
"""
把 SkillEditorWindow.DrawSegmentEffects 从「纯文本框」升级成
「可视化 chips + 下拉添加 + 未实现标红」，原文本框保留为「高级·文本编辑」（默认折叠）。

纪律（项目铁律 7）：
  · 改 .cs 一律走 Python 脚本：old 命中数断言 == 1 + 幂等（new 已存在即跳过）
    + 写回后重读复验 + 延迟 2 秒二次复验（磁盘曾静默回退）。
  · 改前先物理备份 .bak.<标签>，标签写「改动后状态」。
"""
import io, os, sys, time

PATH = r"E:\Project\BttleFreamwork-v1\Assets\Editor\SkillEditor\SkillEditorWindow.cs"
BAK_TAG = "2026-10-13-segeffects-chips"

# ── 改动 ①：新增折叠状态字段 ──────────────────────────────────
EDITS = []

EDITS.append({
    "name": "state-field",
    "marker": "_effectTextOpen",
    "old": "        private Vector2 _formScroll;\n",
    "new": (
        "        private Vector2 _formScroll;\n"
        "        /// <summary>「逐段效果号 · 高级文本编辑」的折叠状态 —— 默认折叠。</summary>\n"
        "        private bool _effectTextOpen;\n"
    ),
})

# ── 改动 ②：重写 DrawSegmentEffects ───────────────────────────
OLD_METHOD = '''        /// <summary>
        /// 逐段效果号 —— 显示成「第一段 / 第二段 ...」，并显式写出「第 N 段无效果」。
        /// ⚠ 空段必须显示出来，不能留白（SkillData.cs:77 注释：空段 = 什么都不做）。
        /// </summary>
        private void DrawSegmentEffects(SkillData data, SkillFieldMeta meta, List<int>[] segs)
        {
            EditorGUILayout.LabelField(meta.Label, GUILayout.Width(90));
            EditorGUI.indentLevel++;
            if (segs == null || segs.Length == 0)
            {
                EditorGUILayout.LabelField("（整条都没配 —— 所有段都不会有任何效果）");
            }
            else
            {
                for (int s = 0; s < segs.Length; s++)
                {
                    string body;
                    if (segs[s] == null || segs[s].Count == 0)
                    {
                        body = "无效果";
                    }
                    else
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        for (int i = 0; i < segs[s].Count; i++)
                        {
                            if (i > 0) sb.Append(';');
                            sb.Append(segs[s][i]);
                        }
                        body = sb.ToString();
                    }
                    // 段号超出段数 ⇒ GetEffectTypesForSegment 返回 null，这一组永远不会被读到
                    string rowLabel = "第 " + (s + 1) + " 段";
                    if (s >= data.SegCount) rowLabel += "（超出段数·不生效）";
                    else if (segs[s] == null || segs[s].Count == 0) rowLabel += "（空·不结算）";

                    string next = EditorGUILayout.TextField(rowLabel, body);
                    if (next != body)
                    {
                        List<int> p = ParseIntList(next);
                        List<int>[] copy = CloneSegments(segs);
                        copy[s] = p;
                        SkillEditorModel.Set(data, meta, copy);
                    }
                }
            }
            EditorGUI.indentLevel--;
        }
'''

NEW_METHOD = '''        /// <summary>
        /// 逐段效果号 —— 每段一行：段号标签 + 已选效果号 chips + 「＋」下拉添加。
        /// ⚠ 空段必须显示出来，不能留白（SkillData.cs:77 注释：空段 = 什么都不做）。
        ///
        /// 【为什么必须是 chips + 下拉，而不是一个裸文本框】
        ///   SkillEffectTypes 共 23 个值，其中 19 个目前是空桩（SkillEffectRegistry.s_KnownStubIds）。
        ///   手填数字等于让策划在 23 个数字里盲选，选中空桩后的表现是「配了却什么都没发生」——
        ///   这属于铁律 5 明令禁止的静默降级。
        ///
        /// 【为什么未实现的一律标红】不是装饰，而是把「这个号目前打出去不会有任何反应」
        ///   这条信息从「进 Play 才发现」前置到「配置当场就能看见」。
        ///
        /// 【为什么还留着文本框】批量粘贴 / 整串改写时打字比点下拉快；
        ///   折叠起来不占视线，但老路径一条都不删。
        /// </summary>
        private void DrawSegmentEffects(SkillData data, SkillFieldMeta meta, List<int>[] segs)
        {
            // 取不到 meta 就是契约破损，必须指名 —— 静默跳过会让「改了没生效」无从查起
            if (meta == null)
            {
                Debug.LogError("[技能编辑器] DrawSegmentEffects 收到空的字段元数据，无法绘制 —— "
                               + "请检查 SkillFieldMetaTable 是否仍含 EffectTypesBySegment。");
                return;
            }

            EditorGUILayout.LabelField(meta.Label, GUILayout.Width(90));
            EditorGUI.indentLevel++;
            if (segs == null || segs.Length == 0)
            {
                EditorGUILayout.LabelField("（整条都没配 —— 所有段都不会有任何效果）");
            }
            else
            {
                for (int s = 0; s < segs.Length; s++)
                {
                    DrawOneSegmentEffectRow(data, meta, segs, s);
                }
            }

            // 高级 · 文本编辑：与 chips 完全同源，只是换一种输入方式
            _effectTextOpen = EditorGUILayout.Foldout(_effectTextOpen, "高级 · 文本编辑", true);
            if (_effectTextOpen && segs != null)
            {
                for (int s = 0; s < segs.Length; s++)
                {
                    DrawSegmentEffectTextField(data, meta, segs, s);
                }
            }
            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 一段 = 一行：段号标签 + 已选效果 chip 一排 + 行尾「＋」。
        /// 段号标签上的两个标注（超出段数 / 空段）是「为什么这一行不起作用」的唯一线索，不许省。
        /// </summary>
        private void DrawOneSegmentEffectRow(SkillData data, SkillFieldMeta meta, List<int>[] segs, int s)
        {
            List<int> picked = segs[s];

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(SegmentRowLabel(data, segs, s), EditorStyles.miniLabel,
                GUILayout.Width(RowLabelWidth));

            if (picked != null)
            {
                for (int i = 0; i < picked.Count; i++)
                {
                    DrawEffectChip(data, meta, segs, s, picked[i]);
                }
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("＋", GUILayout.Width(22)))
            {
                ShowEffectPicker(data, meta, segs, s);
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 段号标签 —— 「第 N 段」+ 两个不生效原因标注。
        /// ① 超出段数：GetEffectTypesForSegment 返回 null，这一组永远不会被读到。
        /// ② 空段：SkillData.cs:77 注释，空段 = 什么都不做。
        /// </summary>
        private static string SegmentRowLabel(SkillData data, List<int>[] segs, int s)
        {
            string label = "第 " + (s + 1) + " 段";
            if (s >= data.SegCount) label += "（超出段数·不生效）";
            else if (segs[s] == null || segs[s].Count == 0) label += "（空·不结算）";
            return label;
        }

        /// <summary>
        /// 单个效果号 chip —— 未实现 / 越界的一律染红，悬停给出原因，点一下即删。
        ///
        /// 【为什么用 GUI.backgroundColor 而不是自绘 DrawRect】
        ///   本工程这份 Odin dll 里没有 SirenixEditorGUI（2026-10-12 编译实锤 CS0103），
        ///   一律只用标准 UnityEditor API。backgroundColor 对 EditorStyles.miniButton 的底图是
        ///   正片叠底，深浅皮肤下都还能看清文字，比自绘 rect 更不容易踩对比度的坑。
        /// </summary>
        private void DrawEffectChip(SkillData data, SkillFieldMeta meta, List<int>[] segs, int s, int value)
        {
            bool valid = value >= 0 && value < SkillEffectTypeCatalog.Count;
            if (!valid) ReportBadEffectValue(data, s, value);

            bool stub = valid && SkillEffectTypeCatalog.IsStub(value);
            string label = valid ? SkillEffectTypeCatalog.OptionLabel(value) : (value + " 未知（越界）");

            Color old = GUI.backgroundColor;
            if (stub || !valid) GUI.backgroundColor = StubColor;

            GUIContent content = ChipContent(value, label, stub);
            float w = EditorStyles.miniButton.CalcSize(content).x + 10f;
            bool clicked = GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(w));

            GUI.backgroundColor = old;

            if (clicked)
            {
                ApplyEffectToggle(data, meta, segs, s, value, true);
            }
        }

        /// <summary>
        /// chip 的 GUIContent 缓存 —— 23 个号的文本是静态的，没必要每帧 new 一个（零 GC 铁律）。
        /// 空桩集合 s_KnownStubIds 是编译期常量，域重载时静态字段会一起清掉，缓存不会腐化。
        /// </summary>
        private static GUIContent ChipContent(int value, string label, bool stub)
        {
            if (value < 0 || value >= _chipContents.Length)
            {
                return new GUIContent(label, BadValueTip);
            }
            GUIContent cached = _chipContents[value];
            if (cached == null)
            {
                cached = new GUIContent(label, stub ? StubTip : "");
                _chipContents[value] = cached;
            }
            return cached;
        }

        /// <summary>
        /// 「＋」的下拉：已实现的在前，未实现的压在「── 未实现 ──」分隔条下面。
        ///
        /// 【为什么必须分组】GenericMenu 改不了条目颜色，只靠 OptionLabel 自带的「（未实现）」
        ///   后缀在 23 条里扫一眼仍容易漏 —— 分组之后「往下越过分隔条 = 进去就是空桩」，
        ///   和后缀一起构成双保险。
        /// 【为什么点已勾选项 = 取消】下拉是这一组效果号的开关，与「点 chip = 删除」是同一套语义。
        /// </summary>
        private void ShowEffectPicker(SkillData data, SkillFieldMeta meta, List<int>[] segs, int s)
        {
            GenericMenu menu = new GenericMenu();
            List<int> picked = segs[s];

            for (int v = 0; v < SkillEffectTypeCatalog.Count; v++)
            {
                if (SkillEffectTypeCatalog.IsStub(v)) continue;
                menu.AddItem(new GUIContent(SkillEffectTypeCatalog.OptionLabel(v)),
                    ContainsValue(picked, v), OnPickEffect,
                    new EffectPick(data, meta, segs, s, v));
            }

            menu.AddSeparator("");
            menu.AddDisabledItem(new GUIContent("── 未实现 ──"));
            menu.AddSeparator("");

            for (int v = 0; v < SkillEffectTypeCatalog.Count; v++)
            {
                if (!SkillEffectTypeCatalog.IsStub(v)) continue;
                menu.AddItem(new GUIContent(SkillEffectTypeCatalog.OptionLabel(v)),
                    ContainsValue(picked, v), OnPickEffect,
                    new EffectPick(data, meta, segs, s, v));
            }

            menu.ShowAsContext();
        }

        private void OnPickEffect(object userData)
        {
            EffectPick p = userData as EffectPick;
            if (p == null)
            {
                Debug.LogError("[技能编辑器] 效果号下拉回调收到空的上下文，本次选择已丢弃 —— 这是编辑器内部错误。");
                return;
            }
            // toggle：已选中再点一次 = 取消，与点 chip 删除一致
            ApplyEffectToggle(p.Data, p.Meta, p.Segs, p.Seg, p.Value, ContainsValue(p.Segs[p.Seg], p.Value));
        }

        /// <summary>
        /// 增删一个效果号 —— 一律走 CloneSegments 写时拷贝。
        /// ⚠ 不能就地改 segs：SkillEditorModel 的快照是深拷贝，就地改会让「原始值」跟着变，
        ///   脏标记与最小 diff 全部失效（表现为「改了却保存不进去」）。
        /// </summary>
        private void ApplyEffectToggle(SkillData data, SkillFieldMeta meta, List<int>[] segs, int s,
                                       int value, bool remove)
        {
            if (meta == null)
            {
                Debug.LogError("[技能编辑器] 技能 " + data.ID + "：逐段效果号缺少字段元数据，改动未生效。");
                return;
            }

            List<int>[] copy = CloneSegments(segs);
            if (copy[s] == null) copy[s] = new List<int>();
            bool had = ContainsValue(copy[s], value);

            if (remove)
            {
                if (!had) return;
                copy[s].Remove(value);
            }
            else
            {
                if (had) return;
                copy[s].Add(value);
            }

            SkillEditorModel.Set(data, meta, copy);
            Repaint();
        }

        /// <summary>高级 · 文本编辑：与旧版同样的「; 分隔」写法，只是空段显示空串而不是「无效果」。</summary>
        private void DrawSegmentEffectTextField(SkillData data, SkillFieldMeta meta, List<int>[] segs, int s)
        {
            string body = JoinIntList(segs[s]);
            string next = EditorGUILayout.TextField(SegmentRowLabel(data, segs, s), body);
            if (next == body) return;

            List<int>[] copy = CloneSegments(segs);
            copy[s] = ParseIntList(next);
            SkillEditorModel.Set(data, meta, copy);
        }

        /// <summary>越界效果号只报一次 —— OnGUI 每秒跑几十帧，同一次报错刷屏等于没人看。</summary>
        private void ReportBadEffectValue(SkillData data, int seg, int value)
        {
            long key = ((long)data.ID << 32) | (uint)value;
            if (_reportedBadEffect.Contains(key)) return;
            _reportedBadEffect.Add(key);

            Debug.LogError("[技能编辑器] 技能 " + data.ID + " 第 " + (seg + 1) + " 段：效果号 "
                + value + " 超出合法范围 0~" + (SkillEffectTypeCatalog.Count - 1)
                + " —— 运行时不会有任何效果，请改配置或补 SkillEffectTypes 枚举。");
        }

        private static bool ContainsValue(List<int> list, int value)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
            return false;
        }

        private static string JoinIntList(List<int> list)
        {
            if (list == null || list.Count == 0) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(list[i]);
            }
            return sb.ToString();
        }

        /// <summary>下拉回调的上下文 —— GenericMenu 的 userData 只能带一个 object。</summary>
        private sealed class EffectPick
        {
            public SkillData Data;
            public SkillFieldMeta Meta;
            public List<int>[] Segs;
            public int Seg;
            public int Value;

            public EffectPick(SkillData data, SkillFieldMeta meta, List<int>[] segs, int seg, int value)
            {
                Data = data;
                Meta = meta;
                Segs = segs;
                Seg = seg;
                Value = value;
            }
        }
'''

EDITS.append({
    "name": "draw-segment-effects",
    "marker": "DrawOneSegmentEffectRow",
    "old": OLD_METHOD,
    "new": NEW_METHOD,
})

# ── 改动 ③：chips 用的静态常量（放在 DrawSeparator 前后，紧邻小工具区）──
EDITS.append({
    "name": "chip-consts",
    "marker": "_chipContents",
    "old": "        private static List<int> ParseIntList(string txt)\n",
    "new": (
        "        /// <summary>未实现 / 越界效果号的 chip 底色。</summary>\n"
        "        private static readonly Color StubColor = new Color(0.95f, 0.34f, 0.32f);\n"
        "        private const string StubTip = \"该效果号尚未实现，配了不会有任何反应\";\n"
        "        private const string BadValueTip = \"效果号超出 SkillEffectTypes 的合法范围，配了不会有任何反应\";\n"
        "        private const float RowLabelWidth = 190f;\n"
        "\n"
        "        /// <summary>效果号 chip 的 GUIContent 缓存（下标 = 效果号）。</summary>\n"
        "        private static readonly GUIContent[] _chipContents = new GUIContent[SkillEffectTypeCatalog.Count];\n"
        "\n"
        "        /// <summary>已报过警的越界效果号（技能 ID << 32 | 值），避免每帧刷屏。</summary>\n"
        "        private static readonly HashSet<long> _reportedBadEffect = new HashSet<long>();\n"
        "\n"
        "        private static List<int> ParseIntList(string txt)\n"
    ),
})


def read_text(p):
    with io.open(p, "r", encoding="utf-8-sig") as f:
        return f.read()


def write_text(p, s):
    with io.open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(s)


def main():
    if not os.path.exists(PATH):
        print("FAIL 文件不存在: " + PATH)
        return 1

    src = read_text(PATH)
    original = src
    changed = False

    for e in EDITS:
        # ⚠ 幂等判据必须看**原始**文本：改动 ② 的代码里含有改动 ③ 的标记，
        #   拿逐步修改后的 src 去判会把它误判成「已存在」而跳过。
        if e["marker"] in original:
            print("SKIP [%s] 已存在 %s —— 幂等跳过" % (e["name"], e["marker"]))
            continue
        cnt = src.count(e["old"])
        if cnt != 1:
            print("FAIL [%s] old 命中 %d 次（要求 == 1）" % (e["name"], cnt))
            return 1
        src = src.replace(e["old"], e["new"], 1)
        changed = True
        print("APPLIED [%s]" % e["name"])

    if not changed:
        print("NO-OP 全部改动已存在")
        return 0

    bak = PATH + ".bak." + BAK_TAG
    if not os.path.exists(bak):
        write_text(bak, original)
        print("BACKUP -> " + bak)

    write_text(PATH, src)

    # 复验 ①：写回后立刻重读
    again = read_text(PATH)
    ok = True
    for e in EDITS:
        if e["marker"] not in again:
            print("VERIFY-1 FAIL 缺少 " + e["marker"])
            ok = False
    # 静态常量必须真落盘（_chipContents 的声明与缓存方法里的引用同名，此处只认声明）
    if "private static readonly GUIContent[] _chipContents" not in again:
        print("VERIFY-1 FAIL 缺少 _chipContents 声明")
        ok = False
    # 旧形态必须已经消失
    if "body = \"无效果\";" in again:
        print("VERIFY-1 FAIL 旧的「无效果」文本框仍在")
        ok = False
    print("VERIFY-1 %s | len=%d" % ("OK" if ok else "FAIL", len(again)))
    if not ok:
        return 1

    # 复验 ②：延迟 2 秒再读一次（磁盘曾静默回退）
    time.sleep(2)
    again2 = read_text(PATH)
    ok2 = again2 == again
    print("VERIFY-2 %s | len=%d" % ("OK" if ok2 else "FAIL", len(again2)))
    return 0 if ok2 else 1


if __name__ == "__main__":
    sys.exit(main())
