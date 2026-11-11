# -*- coding: utf-8 -*-
"""
2026-10-12 · 技能编辑器：段数联动（SegCount → 逐段效果表）

【背景】用户要求：编辑器里改「伤害段数」时，逐段效果号要对应添加。
【运行时依据（已核实，勿凭印象改）】
  1) SkillData.GetEffectTypesForSegment 规则②：段号超出已配组数返回 null，
     且**刻意不复用第 1 组** ⇒ 只改 SegCount 不加组，新段什么都不做。
  2) SkillData.ValidateEffectTypes②：组数 != SegCount 时加载期指名告警。
  3) SkillData.GetSegmentCount：生效段数 = min(SegCount, SegTimings.Length)
     ⇒ 只补组不补时机点，新段依然不会结算 —— 所以额外给「按段数均分时机点」按钮（人工触发，不自动写）。

【纪律】改 .cs 一律走脚本：命中数 == 1 断言 + 写回后重读复验 + 延迟二次复验 + 幂等。
"""
import io
import os
import time

PATH = r"E:\Project\BttleFreamwork-v1\Assets\Editor\SkillEditor\SkillEditorWindow.cs"
BAK = PATH + ".bak.2026-10-12-segcountsync"

src = io.open(PATH, encoding="utf-8").read()

# ── 备份（本工程无 git，破坏性改动前必须物理备份）──────────────
if not os.path.exists(BAK):
    io.open(BAK, "w", encoding="utf-8").write(src)
    print("已备份 →", BAK)
else:
    print("备份已存在，跳过 →", BAK)

# ══════════════════════════════════════════════════════════
# 替换块
# ══════════════════════════════════════════════════════════

# ① 字段写入后：段数变化时联动同步逐段效果表
OLD_1 = """            if (EditorGUI.EndChangeCheck() && next != null)
            {
                SkillEditorModel.Set(data, meta, next);
                SkillEditorModel.InvalidateEffectCache(data);
            }
"""
NEW_1 = """            if (EditorGUI.EndChangeCheck() && next != null)
            {
                SkillEditorModel.Set(data, meta, next);
                SkillEditorModel.InvalidateEffectCache(data);

                // ★ 段数是「上游」：改它必须同步逐段效果表的组数。
                //   否则 SkillData.GetEffectTypesForSegment 规则②（超出组数返回 null、不复用第 1 组）
                //   会让新加的段「什么都不做」，且加载期 ValidateEffectTypes② 指名告警。
                if (meta.MemberName == "SegCount") SyncSegmentEffects(data);
            }
"""

# ② 新增两个方法：段数联动 + 三方核对条
OLD_2 = """        // ── 底部：未保存清单 ────────────────────────────────────
        private void DrawDirtyFooter()
"""
NEW_2 = '''        // ── 段数联动（2026-10-12 用户要求）────────────────────
        /// <summary>
        /// ★ 改「伤害段数」时把逐段效果表补齐到同样组数。
        ///
        /// 【为什么必须联动】GetEffectTypesForSegment 规则②：段号超出已配组数返回 null，
        ///   且刻意**不复用第 1 组** ⇒ 只改 SegCount 不加组，新段就是什么都不做。
        ///
        /// 【为什么新段默认填 ImmedDamage(0) 而不是留空】ValidateEffectTypes③：某段效果不含 0
        ///   就不结算伤害。留空虽不报错，但学员会看到「加了段却没掉血」这种最难描述的画面；
        ///   0 是段的基础语义（先掉血，再往上叠别的）。这是**可见、可撤销**的默认值，不是运行时静默降级。
        ///
        /// 【为什么不截断多余的组】段数调小时截断会丢配置且不可复原。
        ///   保留 + 界面标注「不生效」+ 一键截断按钮，比自动删数据安全。
        /// </summary>
        private void SyncSegmentEffects(SkillData data)
        {
            int want = data.SegCount;
            if (want <= 0) return;   // 段数归零 = 不分段；逐段表留着也不会被执行，不动它（ValidateEffectTypes① 会告警点名）

            SkillFieldMeta meta = SkillFieldMetaTable.Find("EffectTypesBySegment");
            if (meta == null)
            {
                Debug.LogError("[技能编辑器] 契约表缺 EffectTypesBySegment，无法同步段数 —— 请检查 SkillFieldMetaTable");
                return;
            }

            List<int>[] cur = SkillEditorModel.Get(data, meta) as List<int>[];
            if (cur != null && cur.Length >= want) return;   // 已经够了（长度超出段数的情况交给界面标注，不自动删）

            List<int>[] next = new List<int>[want];
            int added = 0;
            for (int i = 0; i < want; i++)
            {
                bool hasOld = cur != null && i < cur.Length && cur[i] != null && cur[i].Count > 0;
                if (hasOld)
                {
                    next[i] = new List<int>(cur[i]);
                }
                else
                {
                    next[i] = new List<int> { 0 };   // 新增段：默认先结算一次伤害
                    added++;
                }
            }

            if (SkillEditorModel.Set(data, meta, next))
            {
                Debug.Log($"[技能编辑器] 技能 {data.ID}：段数 → {want}，逐段效果表已补齐到 {want} 组" +
                          (added > 0 ? $"（新增 {added} 组，默认 ImmedDamage(0)，可再改）" : ""));
                Repaint();
            }
        }

        /// <summary>
        /// 段数 / 时机点 / 效果组 的三方核对条 —— 这三列不一致是「加了段却没伤害」的头号原因。
        ///
        /// ⚠ 为什么时机点不自动补：生成时机是策划意图（均分只是其中一种写法），
        ///   自动写会让「我明明配过 0.3/0.6/0.9」被悄悄覆盖 —— 那是数据失真，比少一个按钮严重得多。
        ///   所以只给按钮，由人点，且点完会打日志留下痕。
        /// </summary>
        private void DrawSegmentTools(SkillData data)
        {
            int segs = data.SegCount;
            int timing = data.SegTimings != null ? data.SegTimings.Length : 0;
            int group = data.EffectTypesBySegment != null ? data.EffectTypesBySegment.Length : 0;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"段数 {segs} · 时机点 {timing} · 效果组 {group}",
                EditorStyles.miniLabel, GUILayout.Width(210));

            if (segs > 0 && timing != segs && GUILayout.Button("按段数均分时机点", GUILayout.Width(130)))
            {
                EvenSplitTimings(data, segs);
            }
            if (segs > 0 && group > segs && GUILayout.Button("截断效果组到 " + segs + " 段", GUILayout.Width(140)))
            {
                TrimEffectGroups(data, segs);
            }
            if (segs > 0 && timing == segs && group == segs)
            {
                EditorGUILayout.LabelField("三列一致 ✓", EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (segs > 0 && timing == 0)
            {
                EditorGUILayout.HelpBox("段数 > 0 但一个时机点都没有 —— 没有时机就无法推进，实际结算 0 段。点上面按钮补齐。",
                    MessageType.Error);
            }
        }

        /// <summary>
        /// 按段数均分时机点 —— 取 0~0.9 而不是 0~1：
        /// 末段留 10% 给收招（动画播完走 TryFinishSkill），贴到 1.0 有被 OnExit 打断、段被吞掉的风险。
        /// 现表 10002 就是 0.3/0.6/0.9，正好是这个算法的 3 段结果 —— 与既有配置风格一致。
        /// </summary>
        private void EvenSplitTimings(SkillData data, int n)
        {
            SkillFieldMeta meta = SkillFieldMetaTable.Find("SegTimings");
            if (meta == null)
            {
                Debug.LogError("[技能编辑器] 契约表缺 SegTimings，无法均分 —— 请检查 SkillFieldMetaTable");
                return;
            }

            float[] arr = new float[n];
            for (int i = 0; i < n; i++) arr[i] = (i + 1) * 0.9f / n;

            string before = data.SegTimings != null ? string.Join(";", data.SegTimings) : "(空)";
            if (SkillEditorModel.Set(data, meta, arr))
            {
                SkillEditorModel.InvalidateEffectCache(data);
                Debug.Log($"[技能编辑器] 技能 {data.ID}：段时机 {before} → {string.Join(";", arr)}（按 {n} 段均分 0~0.9）");
                Repaint();
            }
        }

        /// <summary>把多出来的效果组截掉 —— 只在人点按钮时发生，且会打日志留痕。</summary>
        private void TrimEffectGroups(SkillData data, int n)
        {
            SkillFieldMeta meta = SkillFieldMetaTable.Find("EffectTypesBySegment");
            if (meta == null)
            {
                Debug.LogError("[技能编辑器] 契约表缺 EffectTypesBySegment，无法截断 —— 请检查 SkillFieldMetaTable");
                return;
            }

            List<int>[] cur = SkillEditorModel.Get(data, meta) as List<int>[];
            if (cur == null || cur.Length <= n) return;

            List<int>[] next = new List<int>[n];
            for (int i = 0; i < n; i++) next[i] = cur[i] != null ? new List<int>(cur[i]) : null;

            if (SkillEditorModel.Set(data, meta, next))
            {
                Debug.Log($"[技能编辑器] 技能 {data.ID}：逐段效果表 {cur.Length} 组 → {n} 组（截断）");
                Repaint();
            }
        }

        // ── 底部：未保存清单 ────────────────────────────────────
        private void DrawDirtyFooter()
'''

# ③ 逐段效果行：标注「超出段数 / 空段」
OLD_3 = """                    string next = EditorGUILayout.TextField("第 " + (s + 1) + " 段", body);
"""
NEW_3 = """                    // 段号超出段数 ⇒ GetEffectTypesForSegment 返回 null，这一组永远不会被读到
                    string rowLabel = "第 " + (s + 1) + " 段";
                    if (s >= data.SegCount) rowLabel += "（超出段数·不生效）";
                    else if (segs[s] == null || segs[s].Count == 0) rowLabel += "（空·不结算）";

                    string next = EditorGUILayout.TextField(rowLabel, body);
"""

# ④ 时间轴下方挂三方核对条
OLD_4 = """            EditorGUILayout.Space(4);
        }

        // ── 六张分组卡 ──────────────────────────────────────────
"""
NEW_4 = """            EditorGUILayout.Space(4);
            DrawSegmentTools(data);
        }

        // ── 六张分组卡 ──────────────────────────────────────────
"""

# ⑤ 校验面板：补「组数 vs 段数」（对齐 ValidateEffectTypes②，同样只在 >1 组时校验）
OLD_5 = """            // 弹道绑定（SkillDataManager.ValidateProjectileBindings 的对应项）
            if (data.DeliveryType == SkillDeliveryType.Projectile && data.ProjectileId <= 0)
                errs.Add("投递方式为「弹道」，但 ProjectileId 为 0，运行时会拿不到弹道配置。");
"""
NEW_5 = """            // 弹道绑定（SkillDataManager.ValidateProjectileBindings 的对应项）
            if (data.DeliveryType == SkillDeliveryType.Projectile && data.ProjectileId <= 0)
                errs.Add("投递方式为「弹道」，但 ProjectileId 为 0，运行时会拿不到弹道配置。");

            // 逐段效果组数 vs 段数（对齐 SkillData.ValidateEffectTypes②）
            // ⚠ 只有 1 组时是「没写 |、全部效果集中在第 1 段」的合法常规写法，不做段数校验
            int groupCount = data.EffectTypesBySegment != null ? data.EffectTypesBySegment.Length : 0;
            if (groupCount > 1 && data.SegCount > 0 && groupCount != data.SegCount)
            {
                warns.Add("逐段效果有 " + groupCount + " 组，但 SegCount=" + data.SegCount + " —— " +
                          (groupCount > data.SegCount
                              ? "多出的组用不上（可在时间轴下方一键截断）"
                              : "少掉的段没有任何效果（GetEffectTypesForSegment 返回 null，不复用第 1 组）"));
            }
"""

BLOCKS = [
    ("① 段数联动挂钩", OLD_1, NEW_1),
    ("② 联动/核对/均分/截断四个方法", OLD_2, NEW_2),
    ("③ 逐段行标注状态", OLD_3, NEW_3),
    ("④ 挂三方核对条", OLD_4, NEW_4),
    ("⑤ 校验面板补组数校验", OLD_5, NEW_5),
]

out = src
for name, old, new in BLOCKS:
    hit = out.count(old)
    if new in out:
        print(f"[幂等·跳过] {name}：新内容已存在")
        continue
    assert hit == 1, f"[断言失败] {name}：命中 {hit} 次（期望 1）"
    out = out.replace(old, new, 1)
    print(f"[已替换] {name}")

if out == src:
    print("无变化（全部幂等跳过）")
else:
    # Windows 上偶发拒绝覆写已存在文件 → 失败则走临时文件 + PowerShell Copy-Item
    try:
        io.open(PATH, "w", encoding="utf-8").write(out)
    except Exception as e:
        tmp = PATH + ".tmp"
        io.open(tmp, "w", encoding="utf-8").write(out)
        print("直接覆写失败，已写临时文件，请用 PowerShell 覆盖：", e)
        raise

# ── 复验 ──────────────────────────────────────────────────
verify = io.open(PATH, encoding="utf-8").read()
checks = [
    ("SyncSegmentEffects 定义", "private void SyncSegmentEffects(SkillData data)"),
    ("DrawSegmentTools 定义", "private void DrawSegmentTools(SkillData data)"),
    ("EvenSplitTimings 定义", "private void EvenSplitTimings(SkillData data, int n)"),
    ("TrimEffectGroups 定义", "private void TrimEffectGroups(SkillData data, int n)"),
    ("段数挂钩", 'if (meta.MemberName == "SegCount") SyncSegmentEffects(data);'),
    ("核对条挂载", "DrawSegmentTools(data);"),
    ("组数校验", "多出的组用不上（可在时间轴下方一键截断）"),
]
print("\n=== 写回后复验 ===")
ok = True
for label, needle in checks:
    hit = needle in verify
    ok = ok and hit
    print(f"  {label:24s} {'✓' if hit else '✗ 缺失'}")

print("\n=== 旧串是否残留 ===")
for name, old, _ in BLOCKS:
    print(f"  {name:34s} 残留={old in verify}")

print("\n总判定:", "PASS" if ok else "FAIL")
