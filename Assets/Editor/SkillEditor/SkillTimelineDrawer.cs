// ★ 技能编辑器 · 时间轴控件 —— 把「段时机 / 出手特效时机 / 逐段效果号」三条数据画在同一条 0~1 横轴上
//
// 【它解决什么问题】改一个技能的「段时机」，过去的做法是在 JSON 里对着一串小数瞎猜，然后进 Play 模式
//   肉眼比对。三条数据分散在三个字段里、单位又都是归一化进度，人脑根本拼不出完整图景，
//   于是「刀光挥出去了但伤害早结算完了」这类错位只能靠手感发现，且发现时已经改不动动画了。
//   本控件把三条数据压到同一条横轴上，让「谁早谁晚」变成**一眼可见的几何关系**。
//
// 【★ 横轴单位为什么必须是归一化 0~1 而不是秒】
//   换成秒之后，美术把动画剪辑从 1 秒改成 2 秒，出手点会钉死在旧位置：
//   动作还没挥出去，伤害已经在 0.4 秒结算完毕，而**不会有任何报错**。
//   归一化进度则天然跟随动画长度缩放 —— 这正是 SegTimings / CastEffectTimings 用 0~1 的原因，
//   控件必须如实呈现这个单位，不许在显示层换算成秒（一换算就把"跟着缩放"这个性质弄丢了）。
//
// 【★ 为什么半透明是核心视觉语言】
//   SegTimings 里超出 SegCount 的点、CastEffectTimings 里超出 AttackEffects 数量的点，
//   都是「配了但不会生效」的废配置。它们必须在图上**看得见**（否则策划以为自己配上了）
//   又必须**一眼区别于生效点**（否则等于骗人）—— 半透明正好同时满足这两条。
//
// 【边界】本控件只画与拖拽，不负责校验、不负责清缓存。
//   改完 SegTimings / CastEffectTimings 后由调用方去清 SkillData 的缓存；
//   这里**只写 public 字段本身**，绝不碰 SkillData 的任何私有字段（_validCastEffectIds 等）。
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEditor;

namespace SkillEditorKit
{
    /// <summary>
    /// 时间轴控件的绘制与交互入口。纯 IMGUI，无 Odin 依赖。
    /// 数据源一律走 <see cref="SkillData"/> 的公开只读方法（GetSegmentCount / GetCastEffectCount 等），
    /// 字段名一律走 <see cref="SkillFieldMetaTable"/>，本文件不自带任何字段清单。
    /// </summary>
    public static class SkillTimelineDrawer
    {
        // ── 尺寸常量（GetHeight 必须与这里的排版逐项对账，否则外部预留高度会算错） ──
        private const float PadTop = 2f;
        private const float HeaderH = 15f;
        private const float TrackH = 22f;
        private const float TrackGap = 4f;
        private const float RulerH = 15f;
        private const float PadBottom = 2f;
        private const float ShrunkRowH = 22f;

        /// <summary>左侧轨道名栏宽度；窗口很窄时按比例收缩，避免把横轴挤成 0 宽。</summary>
        private const float GutterW = 92f;
        private const float GutterMinWidth = 160f;
        private const float GutterRatio = 0.35f;

        private const float TickW = 3f;          // tick 竖条宽度（px）
        private const float HitTolerance = 6f;   // tick 的水平命中半径（px）：太窄抓不住，太宽会误抓相邻点
        private const float SnapSteps = 1000f;   // 拖拽量化到千分位

        /// <summary>「配了但不生效」的点的透明度 —— 看得见，但一眼区别于实色。</summary>
        private const float InactiveAlpha = 0.35f;

        // 下面几个颜色不在 TimelinePalette 里（契约层不归我改），故本地定义。
        // 全部为固定值：深浅皮肤下轨道底色都由 TrackBg 铺满，所以对比度是恒定的，无需按皮肤再调一遍。
        private static readonly Color GridLine = new Color(0.62f, 0.64f, 0.68f, 0.35f);
        private static readonly Color DisorderLine = new Color(0.95f, 0.25f, 0.25f, 0.85f);
        private static readonly Color BlockText = new Color(1f, 1f, 1f, 0.92f);
        private static readonly Color HoverRing = new Color(1f, 1f, 1f, 0.85f);

        private static readonly string[] RulerLabels = { "0", "0.25", "0.5", "0.75", "1.0" };

        // ── 拖拽状态（跨帧保持）──
        // IMGUI 无状态，拖拽只能靠静态字段记住「抓的是哪条轨道的第几个点」；
        // 用 hotControl 做归属校验：被 Esc / 窗口失焦夺走后状态必须自愈，否则下次 hover 会误判成拖拽中。
        private static int _activeId = -1;
        private static int _dragIndex = -1;
        private static TimelineTrackId _dragTrack = TimelineTrackId.Segment;

        private static GUIStyle _miniLabel;

        /// <summary>
        /// 绘制整个时间轴。
        /// </summary>
        /// <param name="rect">外部给定的绘制区域</param>
        /// <param name="data">当前技能；null 时画占位行，不抛异常</param>
        /// <param name="playheadNorm">播放头 0~1；&lt; 0 表示不在播放，不画播放头</param>
        /// <param name="interactive">是否允许拖拽调整时机点</param>
        /// <param name="shrinkZero">无段数据时收缩成一行提示，而不是画三条空轨道</param>
        /// <returns>true = 本次交互修改了数据</returns>
        public static bool Draw(Rect rect, SkillData data, float playheadNorm, bool interactive, bool shrinkZero)
        {
            Event evt = Event.current;
            if (evt == null) interactive = false;   // 不在 OnGUI 上下文里就退化为纯绘制

            if (data == null)
            {
                DrawPlaceholder(rect);
                return false;
            }

            bool hasSegData = data.SegCount > 0 && data.SegTimings != null && data.SegTimings.Length > 0;
            if (shrinkZero && !hasSegData)
            {
                DrawShrunkHint(rect);
                return false;
            }

            // ── 排版：先算几何，事件阶段与绘制阶段共用同一套坐标 ──
            float gutter = rect.width >= GutterMinWidth ? GutterW : rect.width * GutterRatio;
            float axisX = rect.x + gutter;
            float axisW = rect.width - gutter;

            float y = rect.y + PadTop;
            Rect headerRect = new Rect(rect.x, y, rect.width, HeaderH);
            y += HeaderH;

            Rect segRow = new Rect(rect.x, y, rect.width, TrackH);
            y += TrackH + TrackGap;
            Rect castRow = new Rect(rect.x, y, rect.width, TrackH);
            y += TrackH + TrackGap;
            Rect effRow = new Rect(rect.x, y, rect.width, TrackH);
            y += TrackH + TrackGap;
            Rect rulerRect = new Rect(rect.x, y, rect.width, RulerH);

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            bool changed = HandleEvents(evt, data, controlId, segRow, castRow, axisX, axisW, interactive);

            bool showPlayhead = playheadNorm >= 0f;
            float playhead = showPlayhead ? Mathf.Clamp01(playheadNorm) : 0f;

            // ── 绘制 ──
            DrawHeader(headerRect);
            DrawTimingTrack(segRow, axisX, axisW, data, TimelineTrackId.Segment, playhead, showPlayhead, interactive);
            DrawTimingTrack(castRow, axisX, axisW, data, TimelineTrackId.CastEffect, playhead, showPlayhead, interactive);
            DrawEffectTypeTrack(effRow, axisX, axisW, data, playhead, showPlayhead);
            DrawRuler(rulerRect, axisX, axisW);

            if (showPlayhead) DrawPlayhead(axisX, axisW, playhead, segRow.y, rulerRect.yMax);

            return changed;
        }

        /// <summary>外部排版要用：给定宽度下需要多高。</summary>
        /// <param name="hasData">false = 走 shrinkZero 的单行提示形态</param>
        public static float GetHeight(bool hasData)
        {
            if (!hasData) return PadTop + ShrunkRowH + PadBottom;
            // 标题 + 三条轨道（每条后面都跟一个间隙，含最后一条到刻度之间）+ 刻度 + 上下留白
            return PadTop + HeaderH + (TrackH + TrackGap) * 3 + RulerH + PadBottom;
        }

        /// <summary>把 0~1 的归一化值转成易读文案，例如 "0.33 (33%)"。供 Scene / 调试复用。</summary>
        public static string FormatNorm(float v)
        {
            // 百分比单独 clamp：越界值在数字部分要如实暴露（那才是 bug），但百分比不该出现 105%
            int pct = Mathf.RoundToInt(Mathf.Clamp01(v) * 100f);
            return v.ToString("0.###", CultureInfo.InvariantCulture)
                   + " (" + pct.ToString(CultureInfo.InvariantCulture) + "%)";
        }

        // ══════════════════════════════ 事件阶段 ══════════════════════════════

        private static bool HandleEvents(Event evt, SkillData data, int controlId,
                                         Rect segRow, Rect castRow, float axisX, float axisW, bool interactive)
        {
            // 自愈：热控制已被外部夺走（Esc、窗口失焦）却没走 MouseUp ⇒ 状态必须清掉
            if (_dragIndex >= 0 && GUIUtility.hotControl != _activeId) ClearDrag();

            if (!interactive || evt == null || axisW <= 0f) return false;

            Vector2 mp = evt.mousePosition;
            bool changed = false;

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                int hit = -1;
                TimelineTrackId hitTrack = TimelineTrackId.Segment;

                // 先判落在哪一行，再在该行内做水平命中；行不相交，故两行不会互相抢事件
                if (segRow.Contains(mp))
                {
                    hit = HitTestTick(data.SegTimings, axisX, axisW, mp.x);
                    hitTrack = TimelineTrackId.Segment;
                }
                else if (castRow.Contains(mp))
                {
                    hit = HitTestTick(data.CastEffectTimings, axisX, axisW, mp.x);
                    hitTrack = TimelineTrackId.CastEffect;
                }

                if (hit >= 0)
                {
                    _activeId = controlId;
                    _dragTrack = hitTrack;
                    _dragIndex = hit;
                    GUIUtility.hotControl = controlId;
                    evt.Use();
                }
            }
            else if (evt.type == EventType.MouseDrag && _dragIndex >= 0 && GUIUtility.hotControl == controlId)
            {
                float[] arr = GetTimingArray(data, _dragTrack);
                if (arr != null && _dragIndex < arr.Length)
                {
                    float nv = Mathf.Clamp01((mp.x - axisX) / axisW);
                    // 量化到千分位：否则拖一下就得到 0.30000012，JSON 里全是噪音且 diff 天天打架
                    nv = Mathf.Round(nv * SnapSteps) / SnapSteps;

                    if (!Mathf.Approximately(arr[_dragIndex], nv))
                    {
                        // 只写 public 字段本身；缓存由调用方负责清
                        arr[_dragIndex] = nv;
                        GUI.changed = true;
                        changed = true;
                    }
                    evt.Use();
                }
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
            {
                ClearDrag();
                GUIUtility.hotControl = 0;
                evt.Use();
            }

            return changed;
        }

        /// <summary>水平 ±HitTolerance 内最近的 tick；没有则返回 -1。</summary>
        private static int HitTestTick(float[] timings, float axisX, float axisW, float mouseX)
        {
            if (timings == null || axisW <= 0f) return -1;

            int best = -1;
            float bestDist = HitTolerance;
            for (int i = 0; i < timings.Length; i++)
            {
                float x = axisX + axisW * Mathf.Clamp01(timings[i]);
                float d = Mathf.Abs(x - mouseX);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }

        private static void ClearDrag()
        {
            _dragIndex = -1;
            _activeId = -1;
        }

        // ══════════════════════════════ 绘制阶段 ══════════════════════════════

        private static void DrawHeader(Rect r)
        {
            EditorGUI.DrawRect(r, TimelinePalette.TrackBg);
            GUIStyle st = MiniLabel();

            st.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(r.x + 6f, r.y, r.width * 0.5f - 6f, r.height), "施法时间轴", st);

            // 单位提示必须常驻：这是本控件最容易被人误读成"秒"的地方，写在标题右侧每帧提醒
            st.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(r.x + r.width * 0.5f, r.y, r.width * 0.5f - 6f, r.height),
                      "单位：归一化 0~1（动画进度）", st);
        }

        private static void DrawTimingTrack(Rect rowRect, float axisX, float axisW, SkillData data,
                                            TimelineTrackId trackId, float playhead, bool showPlayhead,
                                            bool interactive)
        {
            EditorGUI.DrawRect(rowRect, TimelinePalette.TrackBg);

            SkillFieldMeta meta = MetaOf(trackId);
            GUIStyle st = MiniLabel();
            st.alignment = TextAnchor.MiddleLeft;

            Rect labelRect = new Rect(rowRect.x + 5f, rowRect.y, axisX - rowRect.x - 6f, rowRect.height);
            // 轨道名挂上契约层的 tooltip：悬停即可看到「★ 归一化 0~1，不是秒」这类字段级说明
            GUI.Label(labelRect, new GUIContent(TrackName(meta, trackId), meta != null ? meta.Tooltip : string.Empty), st);

            Rect axisRect = new Rect(axisX, rowRect.y, axisW, rowRect.height);
            DrawGrid(axisRect, axisX, axisW);

            float[] timings = GetTimingArray(data, trackId);
            int activeCount = GetActiveCount(data, trackId);

            if (timings == null || timings.Length == 0)
            {
                st.alignment = TextAnchor.MiddleCenter;
                GUI.Label(axisRect, EmptyText(trackId), st);
                return;
            }

            Vector2 mp = MousePos();
            bool rowHover = interactive && rowRect.Contains(mp);

            // 顺序错乱先画线（画在 tick 底下）：调度是按下标推进的游标，倒序会让两点扎堆在同一帧
            for (int i = 1; i < timings.Length; i++)
            {
                if (timings[i] >= timings[i - 1]) continue;

                float xa = axisX + axisW * Mathf.Clamp01(timings[i - 1]);
                float xb = axisX + axisW * Mathf.Clamp01(timings[i]);
                float lineY = axisRect.center.y;
                EditorGUI.DrawRect(new Rect(xb, lineY - 1f, Mathf.Max(xa - xb, 1f), 2f), DisorderLine);
            }

            int hoverIndex = -1;
            float hoverX = 0f;
            string hoverTip = string.Empty;

            for (int i = 0; i < timings.Length; i++)
            {
                float v = timings[i];
                float x = axisX + axisW * Mathf.Clamp01(v);
                bool active = i < activeCount;
                bool fired = showPlayhead && v <= playhead;

                Color c = fired ? TimelinePalette.Fired : TimelinePalette.Pending;
                if (!active) c = WithAlpha(c, InactiveAlpha);

                Rect tickRect = new Rect(x - TickW * 0.5f, axisRect.y + 3f, TickW, axisRect.height - 6f);
                EditorGUI.DrawRect(tickRect, c);

                // 高亮：hover 到 ±6px 内的 tick，或正被拖拽的那个（拖拽时鼠标常跑出轨道，必须继续高亮）
                bool dragging = _dragIndex == i && _dragTrack == trackId;
                bool hovered = dragging || (rowHover && Mathf.Abs(mp.x - x) <= HitTolerance);
                if (!hovered) continue;

                DrawOutline(new Rect(tickRect.x - 2f, tickRect.y - 2f, tickRect.width + 4f, tickRect.height + 4f), HoverRing);
                hoverIndex = i;
                hoverX = x;
                hoverTip = TickTip(data, trackId, i, active, fired, v);
            }

            // 半透明图例：只有真的存在废配置时才占地方，否则是噪音
            if (activeCount < timings.Length)
            {
                st.alignment = TextAnchor.MiddleRight;
                GUI.Label(new Rect(axisRect.xMax - 74f, axisRect.y, 70f, axisRect.height), "半透明 = 不生效", st);
            }

            // 悬停文案放在最后画，保证压在所有 tick 之上
            if (hoverIndex >= 0) DrawHoverTip(axisRect, hoverX, hoverTip);
        }

        private static void DrawEffectTypeTrack(Rect rowRect, float axisX, float axisW, SkillData data,
                                                float playhead, bool showPlayhead)
        {
            EditorGUI.DrawRect(rowRect, TimelinePalette.TrackBg);

            SkillFieldMeta meta = MetaOf(TimelineTrackId.EffectType);
            GUIStyle st = MiniLabel();
            st.alignment = TextAnchor.MiddleLeft;

            Rect labelRect = new Rect(rowRect.x + 5f, rowRect.y, axisX - rowRect.x - 6f, rowRect.height);
            GUI.Label(labelRect, new GUIContent(TrackName(meta, TimelineTrackId.EffectType),
                                                meta != null ? meta.Tooltip : string.Empty), st);

            Rect axisRect = new Rect(axisX, rowRect.y, axisW, rowRect.height);
            DrawGrid(axisRect, axisX, axisW);

            List<int>[] bySeg = data.EffectTypesBySegment;
            int segCount = data.GetSegmentCount();
            int cfgCount = bySeg != null ? bySeg.Length : 0;

            // 取较大值：配置比段数多出来的那些组同样要画出来（半透明），否则"多配了"这件事永远看不见
            int blockCount = Mathf.Max(segCount, cfgCount);
            if (blockCount <= 0)
            {
                st.alignment = TextAnchor.MiddleCenter;
                GUI.Label(axisRect, "未配逐段效果号（EffectTypesBySegment 为空）", st);
                return;
            }

            float[] timings = data.SegTimings;
            float bw = axisW / blockCount;   // 按段数等分：这一轨没有时间点，段序本身就是横轴
            Color oldText = st.normal.textColor;

            for (int i = 0; i < blockCount; i++)
            {
                float x0 = axisX + bw * i;
                float x1 = (i == blockCount - 1) ? axisX + axisW : axisX + bw * (i + 1);
                Rect block = new Rect(x0 + 1f, axisRect.y + 3f, Mathf.Max(x1 - x0 - 2f, 1f), axisRect.height - 6f);

                bool active = i < segCount;

                // 是否已结算：优先用该段自己的时机点（这才是本控件要对比的东西），
                // 取不到时机点时才回落到色块右端
                bool fired = false;
                if (showPlayhead)
                {
                    float at = (timings != null && i < timings.Length)
                        ? timings[i]
                        : (i + 1f) / blockCount;
                    fired = playhead >= Mathf.Clamp01(at);
                }

                Color c = fired ? TimelinePalette.Fired : TimelinePalette.Pending;
                if (!active) c = WithAlpha(c, InactiveAlpha);
                EditorGUI.DrawRect(block, c);

                st.alignment = TextAnchor.MiddleCenter;
                st.normal.textColor = BlockText;   // 色块底色是实色，白字在深浅皮肤下都压得住
                GUI.Label(block, SegmentEffectText(bySeg, i), st);
            }

            st.normal.textColor = oldText;
        }

        private static void DrawRuler(Rect rulerRect, float axisX, float axisW)
        {
            // 刻度区也铺 TrackBg：这样刻度线与文字在深浅皮肤下都落在同一块已知底色上，对比度恒定
            EditorGUI.DrawRect(rulerRect, TimelinePalette.TrackBg);
            DrawGrid(rulerRect, axisX, axisW);

            GUIStyle st = MiniLabel();
            for (int i = 0; i < RulerLabels.Length; i++)
            {
                float x = axisX + axisW * (i * 0.25f);

                // 首尾两个刻度贴边，居中会有一半跑到轨道外，故改成左 / 右对齐
                if (i == 0)
                {
                    st.alignment = TextAnchor.MiddleLeft;
                    GUI.Label(new Rect(x, rulerRect.y, 22f, rulerRect.height), RulerLabels[i], st);
                }
                else if (i == RulerLabels.Length - 1)
                {
                    st.alignment = TextAnchor.MiddleRight;
                    GUI.Label(new Rect(x - 22f, rulerRect.y, 22f, rulerRect.height), RulerLabels[i], st);
                }
                else
                {
                    st.alignment = TextAnchor.MiddleCenter;
                    GUI.Label(new Rect(x - 16f, rulerRect.y, 32f, rulerRect.height), RulerLabels[i], st);
                }
            }
        }

        private static void DrawPlayhead(float axisX, float axisW, float playhead, float top, float bottom)
        {
            float x = axisX + axisW * playhead;
            EditorGUI.DrawRect(new Rect(x - 1f, top, 2f, bottom - top), TimelinePalette.Playhead);
        }

        private static void DrawGrid(Rect band, float axisX, float axisW)
        {
            for (int i = 0; i < RulerLabels.Length; i++)
            {
                float x = axisX + axisW * (i * 0.25f);
                // 末尾那条正好落在右边界上，往回缩 1px，否则整条线画到轨道外面看不见
                float px = Mathf.Min(x, band.xMax - 1f);
                EditorGUI.DrawRect(new Rect(px, band.y, 1f, band.height), GridLine);
            }
        }

        private static void DrawOutline(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1f, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }

        private static void DrawHoverTip(Rect axisRect, float tickX, string tip)
        {
            GUIStyle st = MiniLabel();
            st.alignment = TextAnchor.MiddleLeft;

            // 估算宽度：miniLabel 约 6px/字符，够用即可，真正防溢出靠下面的左右翻转
            float w = Mathf.Min(tip.Length * 6.2f + 8f, axisRect.width);
            float x = tickX + 6f;
            if (x + w > axisRect.xMax) x = tickX - 6f - w;      // 右边放不下就翻到左边
            x = Mathf.Clamp(x, axisRect.x, Mathf.Max(axisRect.xMax - w, axisRect.x));

            EditorGUI.DrawRect(new Rect(x, axisRect.y, w, axisRect.height), TimelinePalette.TrackBg);
            GUI.Label(new Rect(x + 4f, axisRect.y, w - 8f, axisRect.height), tip, st);
        }

        private static void DrawPlaceholder(Rect rect)
        {
            EditorGUI.DrawRect(rect, TimelinePalette.TrackBg);
            GUIStyle st = MiniLabel();
            st.alignment = TextAnchor.MiddleCenter;
            GUI.Label(rect, "未选择技能", st);
        }

        private static void DrawShrunkHint(Rect rect)
        {
            EditorGUI.DrawRect(rect, TimelinePalette.TrackBg);

            // 用 Pending（橙）画左侧竖条 + 文字：这是"数据缺失"而不是"数据为空"，配色上要跟空轨道拉开
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), TimelinePalette.Pending);

            GUIStyle st = MiniLabel();
            st.alignment = TextAnchor.MiddleCenter;
            Color old = st.normal.textColor;
            st.normal.textColor = TimelinePalette.Pending;
            GUI.Label(new Rect(rect.x + 8f, rect.y, rect.width - 12f, rect.height),
                      "该技能没有段数据（SegCount = 0 或 SegTimings 为空）—— 伤害不会分段结算", st);
            st.normal.textColor = old;
        }

        // ══════════════════════════════ 取数与文案 ══════════════════════════════

        private static float[] GetTimingArray(SkillData data, TimelineTrackId trackId)
        {
            return trackId == TimelineTrackId.Segment ? data.SegTimings : data.CastEffectTimings;
        }

        /// <summary>真正生效的条目数：段的真相源是 GetSegmentCount()，特效的真相源是 GetCastEffectCount()。</summary>
        private static int GetActiveCount(SkillData data, TimelineTrackId trackId)
        {
            return trackId == TimelineTrackId.Segment ? data.GetSegmentCount() : data.GetCastEffectCount();
        }

        private static SkillFieldMeta MetaOf(TimelineTrackId trackId)
        {
            switch (trackId)
            {
                case TimelineTrackId.Segment: return SkillFieldMetaTable.Find("SegTimings");
                case TimelineTrackId.CastEffect: return SkillFieldMetaTable.Find("CastEffectTimings");
                default: return SkillFieldMetaTable.Find("EffectTypesBySegment");
            }
        }

        private static string TrackName(SkillFieldMeta meta, TimelineTrackId trackId)
        {
            // 契约层给了中文名就直接用，找不到再退回枚举名 —— 宁可难看也不静默留白
            return meta != null ? meta.Label : trackId.ToString();
        }

        private static string EmptyText(TimelineTrackId trackId)
        {
            return trackId == TimelineTrackId.Segment
                ? "未配段时机（SegTimings 为空）"
                : "未配出手特效时机（CastEffectTimings 为空）";
        }

        private static string TickTip(SkillData data, TimelineTrackId trackId, int index,
                                      bool active, bool fired, float value)
        {
            string pos = FormatNorm(value);
            string state = fired ? "已过" : "未到";

            if (trackId == TimelineTrackId.Segment)
            {
                if (active) return "第 " + (index + 1) + " 段 · " + pos + " · " + state;

                // 半透明的含义必须就地讲清，否则使用者只会看到"点了但没变化"
                return "第 " + (index + 1) + " 个时机点 · " + pos
                       + " · 超出 SegCount(" + data.SegCount + ")，配了但不生效";
            }

            if (active)
            {
                int id = data.GetCastEffectId(index + 1);   // 1-based，与 AttackEffects 按下标配对
                return "出手特效 " + (index + 1) + " · id " + id + " · " + pos + " · " + state;
            }

            return "出手特效时机 " + (index + 1) + " · " + pos
                   + " · 没有配对的 AttackEffects id，不会播出";
        }

        /// <summary>某一段的效果号文案；空段写「空」而不是留白 —— 留白会被读成"渲染没跟上"。</summary>
        private static string SegmentEffectText(List<int>[] bySeg, int index)
        {
            if (bySeg == null || index >= bySeg.Length) return "空";

            List<int> list = bySeg[index];
            if (list == null || list.Count == 0) return "空";

            return string.Join(";", list);   // 与 CSV/JSON 里的书写格式一致，所见即所配
        }

        // ══════════════════════════════ 小工具 ══════════════════════════════

        private static GUIStyle MiniLabel()
        {
            // EditorStyles 只能在 OnGUI 上下文里取，故延迟到首次绘制时才建
            if (_miniLabel == null)
            {
                _miniLabel = new GUIStyle(EditorStyles.miniLabel);
                _miniLabel.normal.textColor = TimelinePalette.Label;
                _miniLabel.clipping = TextClipping.Clip;
            }
            return _miniLabel;
        }

        private static Vector2 MousePos()
        {
            Event e = Event.current;
            // 不在 OnGUI 里时返回一个必然落在所有轨道之外的坐标，让 hover 判定自然为假
            return e != null ? e.mousePosition : new Vector2(-10000f, -10000f);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
