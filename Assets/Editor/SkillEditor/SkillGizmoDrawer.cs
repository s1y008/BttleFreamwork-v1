// ★ 技能编辑器 · Scene 视图可视化层（Gizmo 绘制）
//
// 【它解决什么问题】策划配「攻击距离 2.5 / 半径 3 / 扇形 120°」时，以前只能靠脑补，
//   必须进 Play 让角色走过去才能验证判定范围。本文件把这几个数字**直接画在角色脚下**，
//   改数字 → Scene 视图当场变化，不再需要跑一遍游戏。
//
// 【为什么用 Handles 而不是 Gizmos】
//   Gizmos 只在 OnDrawGizmos / [DrawGizmo] 的时机生效，而主窗口的刷新方式是挂在
//   SceneView.duringSceneGui 上 —— 那个时机 Gizmos 已经不收数据了，画不出来。
//   Handles 在两种挂载方式下都有效，而且自带 DrawSolidArc（半透明填充），
//   正好用来表达「命中范围」这种**有面积**的概念，这是 Gizmos 做不到的。
//
// 【★ 字段一律走契约表】
//   成员名字符串只在向 SkillFieldMetaTable.Find 查询时出现**一次**，
//   之后：取值走 meta.MemberName + meta.Kind 反射，界面文字走 meta.Label。
//   这样以后 SkillData 改名 / 换形态（Property ↔ Field），只需要改 SkillFieldMeta.cs 一处。
//
// 【职责边界（重要）】
//   · 不注册任何 SceneView / EditorApplication 回调 —— 刷新时机由调用方决定，本类是纯函数式绘制
//   · 不含任何弹窗 / 面板 / GUI 逻辑 —— 只做 Scene 空间绘制
//   · 只读 SkillData，绝不回写（运行时代码一个字都不改）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SkillEditorKit
{
    /// <summary>
    /// 绘制开关。默认全开 —— 教学场景下「看到全部信息」比「画面干净」重要得多。
    /// </summary>
    public sealed class DrawOptions
    {
        /// <summary>进攻距离环（AttackDist，黄橙）。</summary>
        public bool ShowAttackDist = true;

        /// <summary>命中范围本体：圆 / 扇形 / 前方（青绿）。</summary>
        public bool ShowRange = true;

        /// <summary>警示距离环（Warning，红色虚线）。</summary>
        public bool ShowWarning = true;

        /// <summary>在图形旁写出数值文字。教学场景下这一项最关键：看得到数字比看得到图形重要。</summary>
        public bool ShowLabels = true;
    }

    /// <summary>
    /// 技能作用范围的 Scene 视图绘制器。
    /// 静态、无状态（除反射缓存与探针对象），可安全地在 duringSceneGui / DrawGizmo 里反复调用。
    /// </summary>
    public static class SkillGizmoDrawer
    {
        // ─────────────────────────────────────────────────────────────
        // 契约表查询键
        //
        // 【为什么这些字符串只出现在这里】它们是**查询键**，不是业务常量：
        //   拿到的 SkillFieldMeta 里同时带着 MemberName（反射用）和 Label（界面用）。
        //   后续所有代码都只用 meta.XXX，这样「硬写 AttackDist」这件事在全文件只发生一次。
        // ─────────────────────────────────────────────────────────────
        private const string KeyAttackDist = "AttackDist";
        private const string KeyAttackRadius = "AttackRadius";
        private const string KeyAttackAngle = "AttackAngle";
        private const string KeyWarning = "Warning";
        private const string KeyHitSharpType = "SkillHitSharpType";

        // ── 配色 ──────────────────────────────────────────────────
        // 与 TimelinePalette 同一套思路：避开 Unity 主题色，保证深浅两种皮肤下都可读。
        private static readonly Color AttackDistColor = new Color(0.937f, 0.624f, 0.153f); // #EF9F27 黄橙
        private static readonly Color WarningColor = new Color(0.886f, 0.294f, 0.290f);    // #E24B4A 红
        private static readonly Color RangeColor = new Color(0.114f, 0.620f, 0.459f);      // #1D9E75 青绿

        private const float FillAlpha = 0.15f;    // 半透明填充的 alpha：再高就会把角色和地形糊住
        private const float DotScreenSize = 4f;    // 虚线每一小段的屏幕像素长度
        private const float ArrowHeadRatio = 0.25f;// 箭头头部相对杆长的比例
        private const float ArrowHeadMax = 0.6f;   // 箭头头部长度上限：半径 20m 的技能不能长出两把镰刀
        private const float ArrowHeadSpread = 25f;// 箭头两条斜线与杆的反夹角（度）
        private const float LabelLift = 0.06f;     // 文字抬高一点，避免和它描述的线糊在一起
        private const float LabelOutset = 0.35f;   // 文字沿半径往外推的距离
        private const float ProbeDistance = 3f;    // 无选中物体时，锚点落在 Scene 相机前方几米
        private const float MinPlanarLength = 1e-6f;// 水平投影长度的退化阈值

        private const string ProbeName = "SkillGizmoProbe (Hidden)";

        /// <summary>opts 传 null 时的兜底，避免调用方少写一个 new 就 NRE。</summary>
        private static readonly DrawOptions DefaultOptions = new DrawOptions();

        /// <summary>
        /// 反射缓存。键 = meta.MemberName。
        /// 【为什么必须缓存】Draw 是每帧回调里跑的，反射查找（GetProperty/GetField + GetValue）
        ///   每帧来一遍会在 Profiler 里显眼；而且反射取错形态（把 Property 当 Field）会**静默返回 null**，
        ///   所以必须严格按契约表的 MemberKind 二选一，不能偷懒"两种都试一遍"。
        /// </summary>
        private static readonly Dictionary<string, MemberInfo> MemberCache =
            new Dictionary<string, MemberInfo>();

        /// <summary>文字样式缓存 —— GUIStyle 每帧 new 会造成明显的 GC 压力。</summary>
        private static GUIStyle _labelStyle;

        /// <summary>
        /// 「无选中物体」时的临时锚点。
        /// 【为什么是字段而不是局部变量】ResolveAnchor 返回的是它，方法一结束就销毁等于没画；
        ///   所以全局只保留**这一个**探针并复用，一旦出现真实选中就立刻销毁它 —— 这就是"及时销毁"。
        /// </summary>
        private static GameObject _probe;

        // ─────────────────────────────────────────────────────────────
        // 公开 API
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 在 Scene 视图绘制某个技能的作用范围。
        /// </summary>
        /// <param name="anchor">锚点（挂 Gizmo 的那个 Transform，通常是施法者）。</param>
        /// <param name="data">要可视化的技能；null 时直接返回，不抛异常。</param>
        /// <param name="opts">绘制开关；null 时按"全开"处理。</param>
        public static void Draw(Transform anchor, SkillData data, DrawOptions opts)
        {
            // 【为什么静默 return 而不是抛异常】调用方是 duringSceneGui 这类每帧回调，
            //   抛一次异常等于每帧刷一条红日志，控制台瞬间被淹没，真正的错误反而看不见。
            //   缺 data 就无数值可画，缺 anchor 就不知道画在哪 —— 两者都是"没得画"，不是"画错了"。
            if (anchor == null || data == null) return;
            if (opts == null) opts = DefaultOptions;

            // ★★ 防污染（第一步：进门存状态）
            //   Handles.color / Handles.matrix 都是**全局静态**的。同一个 duringSceneGui 上
            //   可能还挂着团队里其它同事的绘制代码（时间轴、弹道预览等），
            //   不还回去会把它们一起染成我们的颜色，或者把它们的坐标系拧歪。
            Color colorBefore = Handles.color;
            Matrix4x4 matrixBefore = Handles.matrix;

            try
            {
                // ★ 统一的「贴地坐标系」：原点 = 锚点，局部 +Z = 锚点正前方的**水平投影**。
                // 【为什么不直接用 anchor.rotation】角色有俯仰/翻滚时，直接用会让圆盘跟着歪掉，
                //   而战斗判定里的"范围"其实是水平面内的概念 —— 贴地才是策划想看到的样子。
                //   同时保留 forward 的水平投影，扇形中轴与前方箭头才对得上角色的正面。
                Vector3 forward = Flatten(anchor.forward);
                Handles.matrix = Matrix4x4.TRS(anchor.position, Quaternion.LookRotation(forward, Vector3.up), Vector3.one);

                float attackDist;
                float attackRadius;
                float attackAngle;
                float warning;
                SkillHitSharpTypes shape;

                // 读取失败时 TryRead* 会把 out 置成默认值（0 / None），
                // 与"策划没配"走同一条路径，被下面每个 Draw 方法里的 <= 0 守卫拦掉。
                TryReadFloat(data, MetaAttackDist, out attackDist);
                TryReadFloat(data, MetaAttackRadius, out attackRadius);
                TryReadFloat(data, MetaAttackAngle, out attackAngle);
                TryReadFloat(data, MetaWarning, out warning);
                TryReadShape(data, MetaHitSharpType, out shape);

                // 绘制顺序：先铺实心填充（最底层）→ 再画环线 → 最后压文字。
                // 反过来的话半透明填充会盖住环线，边界看起来发虚。
                if (opts.ShowRange) DrawRange(shape, attackRadius, attackAngle, opts.ShowLabels);
                if (opts.ShowWarning) DrawWarningRing(warning, opts.ShowLabels);
                if (opts.ShowAttackDist) DrawAttackRing(attackDist, opts.ShowLabels);
                if (opts.ShowLabels) DrawLegend(data, shape, attackDist, attackRadius, attackAngle, warning);
            }
            finally
            {
                // ★★ 防污染（第二步：出门还原）
                //   放在 finally 里：即使中间某个 Handles 调用抛异常（比如矩阵被外部破坏），
                //   也不会把脏状态留给后面的绘制代码。
                Handles.matrix = matrixBefore;
                Handles.color = colorBefore;
            }
        }

        /// <summary>
        /// 从 Scene 里找一个合适的锚点 Transform。
        /// 优先 <see cref="Selection.activeGameObject"/>；没有则退化到 Scene 相机前方 3 米的临时点。
        /// </summary>
        /// <returns>找到的锚点；连 Scene 视图都不存在时返回 null（调用方需自行判空）。</returns>
        public static Transform ResolveAnchor()
        {
            // ① 首选：当前选中的物体。绝大多数情况下策划选的就是场景里的角色/怪物。
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                // 真身出现了，临时探针就该退场 —— 这就是"及时销毁"，避免 Hidden 对象 lingering。
                ReleaseProbe();
                return selected.transform;
            }

            // ② 退化方案：Scene 相机前方 ProbeDistance 米处。
            //   目的不是"画得准"，而是让 Gizmo **还有东西可看** —— 整个消失会让人以为编辑器坏了。
            Camera camera = Camera.current;
            if (camera == null)
            {
                // duringSceneGui 期间 Camera.current 就是 Scene 相机；但本方法也可能被
                // 工具栏按钮等在其它时机调用，所以补一条 SceneView 兜底，并且处理它为 null 的情况。
                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView != null) camera = sceneView.camera;
            }
            if (camera == null) return null;

            EnsureProbe();
            _probe.transform.position = camera.transform.position + camera.transform.forward * ProbeDistance;

            // 朝向也跟着相机走：扇形与「前方」这两类的轴向取自 anchor.forward，
            // 让它们正对镜头，策划一眼就能看出扇形张角有多大。
            _probe.transform.rotation = Quaternion.LookRotation(Flatten(camera.transform.forward), Vector3.up);
            return _probe.transform;
        }

        // ─────────────────────────────────────────────────────────────
        // 四种攻击范围
        // ─────────────────────────────────────────────────────────────

        /// <summary>按 SkillHitSharpType 分派到对应的画法。</summary>
        private static void DrawRange(SkillHitSharpTypes shape, float radius, float angleDegrees, bool showLabel)
        {
            switch (shape)
            {
                case SkillHitSharpTypes.Circle:
                    DrawCircleRange(radius, showLabel);
                    break;

                case SkillHitSharpTypes.Fan:
                    DrawFanRange(radius, angleDegrees, showLabel);
                    break;

                // None（对目标）与 Forward 共用同一支箭头：
                //   None 语义上是"只打选中的那个目标"，没有面状范围，
                //   画一支指向正前方的箭头用来表达"施法朝向"，比什么都不画更好教。
                case SkillHitSharpTypes.Forward:
                case SkillHitSharpTypes.None:
                default:
                    DrawForwardRange(radius, showLabel);
                    break;
            }
        }

        /// <summary>圆形：青绿半透明实心（360° 扇形）+ 边界线。</summary>
        private static void DrawCircleRange(float radius, bool showLabel)
        {
            // 【为什么不画半径 0 的圆】半径 0 的圆盘在 Scene 视图里就是一个亮点，
            //   既没有信息量又像是渲染错误，还容易和锚点本身混淆。
            if (radius <= 0f) return;

            // 实心填充：用 360° 的 SolidArc 而不是 SolidDisc —— 与扇形走同一条 API，
            //   保证"圆"和"扇形"的视觉密度一致（alpha 相同），策划对比两者时不会被渲染差异误导。
            Handles.color = new Color(RangeColor.r, RangeColor.g, RangeColor.b, FillAlpha);
            Handles.DrawSolidArc(Vector3.zero, Vector3.up, Vector3.forward, 360f, radius);

            // 边界线必须是不透明的，否则半透明的边界看不清半径到底到哪
            Handles.color = RangeColor;
            Handles.DrawWireDisc(Vector3.zero, Vector3.up, radius);

            if (!showLabel) return;
            // 标注放 180°（角色身后）：同心图形上 0° / 90° 已分别被进攻距离和警示环占用，
            //   三个数字各占一侧才读得清，不会叠成一团。
            Handles.Label(PointOnRing(180f, radius) + Vector3.up * LabelLift,
                string.Format("圆形 r={0}m", Meters(radius)),
                GetLabelStyle(RangeColor));
        }

        /// <summary>扇形：以 anchor.forward 为中轴、左右各 angle/2 的半透明实心楔形 + 描边。</summary>
        private static void DrawFanRange(float radius, float angleDegrees, bool showLabel)
        {
            if (radius <= 0f) return;

            // 夹到 0~360：配置表里 AttackAngle 的上限是 360，但 JSON 手改可能越界，
            //   超过 360 会让 DrawSolidArc 自己绕圈重叠，视觉上完全读不出角度。
            float angle = Mathf.Clamp(angleDegrees, 0f, 360f);

            // 起始边 = 中轴（局部 +Z）反向旋转半个张角，这样扫过 angle 后正好左右对称
            Vector3 from = Quaternion.AngleAxis(-angle * 0.5f, Vector3.up) * Vector3.forward;

            Handles.color = new Color(RangeColor.r, RangeColor.g, RangeColor.b, FillAlpha);
            Handles.DrawSolidArc(Vector3.zero, Vector3.up, from, angle, radius);

            Handles.color = RangeColor;
            Handles.DrawWireArc(Vector3.zero, Vector3.up, from, angle, radius);

            if (!showLabel) return;
            // 标注放在角平分线**外侧**（局部 +Z 再外推），压不到弧线上
            Handles.Label(Vector3.forward * (radius + LabelOutset) + Vector3.up * LabelLift,
                string.Format("扇形 r={0}m {1}°", Meters(radius), angle.ToString("0", CultureInfo.InvariantCulture)),
                GetLabelStyle(RangeColor));
        }

        /// <summary>前方 / 对目标：沿 anchor.forward 的一条杆 + 两条短斜线箭头。</summary>
        private static void DrawForwardRange(float radius, bool showLabel)
        {
            if (radius <= 0f) return;

            Handles.color = RangeColor;
            Vector3 tip = new Vector3(0f, 0f, radius);
            Handles.DrawLine(Vector3.zero, tip);

            // 箭头头部：从尖端朝局部 -Z 反折两条短斜线。
            // 长度随半径缩放但设上限，否则大范围技能的箭头会粗得盖住范围本身。
            float headLength = Mathf.Min(radius * ArrowHeadRatio, ArrowHeadMax);
            Quaternion spreadLeft = Quaternion.AngleAxis(ArrowHeadSpread, Vector3.up);
            Quaternion spreadRight = Quaternion.AngleAxis(-ArrowHeadSpread, Vector3.up);
            Handles.DrawLine(tip, tip + spreadLeft * Vector3.back * headLength);
            Handles.DrawLine(tip, tip + spreadRight * Vector3.back * headLength);

            if (!showLabel) return;
            Handles.Label(tip + Vector3.forward * LabelOutset + Vector3.up * LabelLift,
                string.Format("前方 {0}m", Meters(radius)),
                GetLabelStyle(RangeColor));
        }

        // ─────────────────────────────────────────────────────────────
        // 两个距离环
        // ─────────────────────────────────────────────────────────────

        /// <summary>进攻距离环：黄橙一整圈。</summary>
        private static void DrawAttackRing(float attackDist, bool showLabel)
        {
            if (attackDist <= 0f) return;

            Handles.color = AttackDistColor;
            Handles.DrawWireDisc(Vector3.zero, Vector3.up, attackDist);

            if (!showLabel) return;
            // 标注放 0°（角色正前方）—— 这是策划最关心的方向：站这么远就能打到
            Handles.Label(PointOnRing(0f, attackDist) + Vector3.up * LabelLift,
                string.Format("{0} {1}", MetaAttackDist.Label, Meters(attackDist)),
                GetLabelStyle(AttackDistColor));
        }

        /// <summary>警示环：红色，用分段 DrawDottedLine 近似虚线（Handles 没有 DrawDottedDisc）。</summary>
        private static void DrawWarningRing(float warning, bool showLabel)
        {
            if (warning <= 0f) return;

            Handles.color = WarningColor;
            DrawDottedRing(warning);

            if (!showLabel) return;
            // 标注放 90°（角色右手边），避开 0° 的进攻距离文字
            Handles.Label(PointOnRing(90f, warning) + Vector3.up * LabelLift,
                string.Format("{0} {1}", MetaWarning.Label, Meters(warning)),
                GetLabelStyle(WarningColor));
        }

        /// <summary>
        /// 用若干段虚线拼出一整圈。
        /// 【为什么要分段】Handles.DrawDottedLine 只能画两点之间的直线段，没有现成的"虚线圆"。
        ///   段数按半径自适应：小圈切太碎纯属浪费，大圈不够密会退化成多边形、一眼看出是假的。
        /// </summary>
        private static void DrawDottedRing(float radius)
        {
            int segments = Mathf.Clamp(Mathf.CeilToInt(radius * 12f), 24, 120);
            float step = 360f / segments;

            Vector3 previous = PointOnRing(0f, radius);
            for (int i = 1; i <= segments; i++)
            {
                Vector3 current = PointOnRing(step * i, radius);
                Handles.DrawDottedLine(previous, current, DotScreenSize);
                previous = current;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 文字
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 锚点处的总览图例 —— 把四个数字一次性摆在脚边。
        /// 【为什么还要这一条】单看图形只能看出"大不大"，看不出"是哪个数在起作用"；
        ///   教学场景下把数值集中列出来，策划才知道该去改哪一列。
        /// </summary>
        private static void DrawLegend(SkillData data, SkillHitSharpTypes shape,
                                       float attackDist, float radius, float angleDegrees, float warning)
        {
            // 技能名可能为空（新建未命名的行），给个占位，避免出现"技能名： "这种读不懂的空标签
            string skillName = string.IsNullOrEmpty(data.Name) ? "(未命名技能)" : data.Name;

            string text = string.Format(
                "{0}｜范围类型：{1}\n攻击距离 {2}／半径 {3}／扇形角度 {4}°／警示距离 {5}",
                skillName,
                ShapeName(shape),
                ValueOrUnset(attackDist),
                ValueOrUnset(radius),
                angleDegrees > 0f ? angleDegrees.ToString("0", CultureInfo.InvariantCulture) + "°" : "未配",
                ValueOrUnset(warning));

            Handles.Label(Vector3.up * LabelLift, text, GetLabelStyle(Color.white));
        }

        /// <summary>SkillHitSharpTypes 的中文显示名（枚举里只有英文，界面要给人看）。</summary>
        private static string ShapeName(SkillHitSharpTypes shape)
        {
            switch (shape)
            {
                case SkillHitSharpTypes.Circle: return "圆形";
                case SkillHitSharpTypes.Fan: return "扇形";
                case SkillHitSharpTypes.Forward: return "前方";
                default: return "对目标"; // None，以及任何将来新增但这里没跟上的枚举值
            }
        }

        /// <summary>缓存复用同一个 GUIStyle，只改文字颜色以匹配它所描述的图形。</summary>
        private static GUIStyle GetLabelStyle(Color color)
        {
            // GUIStyle 要在 GUI 上下文里创建；duringSceneGui 与 DrawGizmo 都满足，
            // 缓存后不再重复 new（否则每帧几个 GUIStyle 会在 Profiler 里显眼）。
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(EditorStyles.boldLabel);
                _labelStyle.alignment = TextAnchor.MiddleCenter;
                _labelStyle.wordWrap = false;
            }

            _labelStyle.normal.textColor = color;
            return _labelStyle;
        }

        /// <summary>统一 1 位小数 + 显式单位「m」：策划看的是相对大小，两位小数只是噪音。</summary>
        private static string Meters(float value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture) + "m";
        }

        /// <summary>未配（&lt;= 0）时显示"未配"，别让空数字变成一个孤零零的 "0.0m"。</summary>
        private static string ValueOrUnset(float value)
        {
            return value > 0f ? Meters(value) : "未配";
        }

        // ─────────────────────────────────────────────────────────────
        // 几何工具
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 贴地圆环上某一点（局部坐标）。
        /// </summary>
        /// <param name="degrees">从角色正前方（局部 +Z）起算、朝右手边（局部 +X）为正的角度。</param>
        private static Vector3 PointOnRing(float degrees, float radius)
        {
            return Quaternion.AngleAxis(degrees, Vector3.up) * (Vector3.forward * radius);
        }

        /// <summary>
        /// 取方向在水平面（XZ）上的投影。
        /// 【为什么要兜底】相机或物体正对上下时水平投影长度趋近 0，
        ///   直接 Normalize 会产出 NaN，一旦写进 Handles.matrix 整个 Scene 视图会黑掉 —— 必须挡住。
        /// </summary>
        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < MinPlanarLength) return Vector3.forward;
            return direction.normalized;
        }

        // ─────────────────────────────────────────────────────────────
        // 契约表 → 反射取值
        // ─────────────────────────────────────────────────────────────

        // 契约表是编译期固定的清单，查一次缓存即可，不必每帧线性扫描 28 行。
        private static readonly SkillFieldMeta MetaAttackDist = Require(KeyAttackDist);
        private static readonly SkillFieldMeta MetaAttackRadius = Require(KeyAttackRadius);
        private static readonly SkillFieldMeta MetaAttackAngle = Require(KeyAttackAngle);
        private static readonly SkillFieldMeta MetaWarning = Require(KeyWarning);
        private static readonly SkillFieldMeta MetaHitSharpType = Require(KeyHitSharpType);

        /// <summary>
        /// 向契约表取元数据。
        /// 【为什么不静默】找不到说明 SkillFieldMetaTable 与 SkillData 已经不同步，
        ///   这是必须修的契约错误；只跳过会让策划以为"这列不支持可视化"。
        /// </summary>
        private static SkillFieldMeta Require(string memberName)
        {
            SkillFieldMeta meta = SkillFieldMetaTable.Find(memberName);
            if (meta == null)
            {
                Debug.LogError(string.Format(
                    "[SkillGizmoDrawer] 契约表 SkillFieldMetaTable 里找不到成员“{0}”，该字段无法可视化。请同步 SkillFieldMeta.cs。",
                    memberName));
            }
            return meta;
        }

        /// <summary>按契约表描述的形态（Property / Field）读取 float 成员。</summary>
        private static bool TryReadFloat(SkillData data, SkillFieldMeta meta, out float value)
        {
            value = 0f;

            object raw;
            if (!TryReadMember(data, meta, out raw)) return false;

            // 用 Convert 而不是硬转型：成员将来若从 float 改成 int，这里不会抛 InvalidCastException
            value = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>按契约表描述的形态读取枚举成员。</summary>
        private static bool TryReadShape(SkillData data, SkillFieldMeta meta, out SkillHitSharpTypes value)
        {
            value = SkillHitSharpTypes.None;

            object raw;
            if (!TryReadMember(data, meta, out raw)) return false;

            // 同样走 Convert：既能吃装箱后的枚举，也能吃配置里直接写的 int
            value = (SkillHitSharpTypes)Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>统一的反射读取入口 —— 形态由 meta.Kind 决定，不猜。</summary>
        private static bool TryReadMember(SkillData data, SkillFieldMeta meta, out object value)
        {
            value = null;

            // meta 为 null 只可能是 Require 已经报过契约错误的情况，这里不再重复刷日志
            if (meta == null) return false;

            MemberInfo member = GetMember(meta);
            if (member == null) return false;

            PropertyInfo property = member as PropertyInfo;
            if (property != null)
            {
                value = property.GetValue(data, null);
                return true;
            }

            FieldInfo field = member as FieldInfo;
            if (field != null)
            {
                value = field.GetValue(data);
                return true;
            }

            return false;
        }

        /// <summary>带缓存地取 MemberInfo。</summary>
        private static MemberInfo GetMember(SkillFieldMeta meta)
        {
            MemberInfo cached;
            if (MemberCache.TryGetValue(meta.MemberName, out cached)) return cached;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type owner = typeof(SkillData);

            MemberInfo member = meta.Kind == MemberKind.Property
                ? (MemberInfo)owner.GetProperty(meta.MemberName, flags)
                : (MemberInfo)owner.GetField(meta.MemberName, flags);

            // ⚠ 连同 null 一起缓存：否则每帧都会重查并重复刷同一条 Error，控制台会被淹没。
            MemberCache[meta.MemberName] = member;

            if (member == null)
            {
                Debug.LogError(string.Format(
                    "[SkillGizmoDrawer] 契约表登记的“{0}”（Kind={1}）在 SkillData 上不存在，该字段不参与绘制。",
                    meta.MemberName, meta.Kind));
            }

            return member;
        }

        // ─────────────────────────────────────────────────────────────
        // 临时探针的生命周期
        // ─────────────────────────────────────────────────────────────

        /// <summary>确保探针存在（全局只会有这一个）。</summary>
        private static void EnsureProbe()
        {
            if (_probe != null) return;

            // HideAndDontSave：不进场景、不被序列化保存、切场景不会被一起存盘；
            // HideInHierarchy：连层级面板里都不显示，避免策划误以为场景里多了个东西。
            // 【为什么只 new 一次】就算标了 HideFlags，每次调用都 new 一个也是泄漏 ——
            //   它不会被 GC，只能在 Destroy 时释放。
            _probe = new GameObject(ProbeName);
            _probe.hideFlags = HideFlags.HideAndDontSave | HideFlags.HideInHierarchy;
        }

        /// <summary>销毁探针并清空引用。</summary>
        private static void ReleaseProbe()
        {
            if (_probe == null) return;

            // 用 DestroyImmediate：这是编辑器侧的隐藏辅助对象，不走运行时销毁队列，
            // 保证调用方下一次 ResolveAnchor 时不会拿到已销毁的对象。
            UnityEngine.Object.DestroyImmediate(_probe);
            _probe = null;
        }

        /// <summary>
        /// 域重载（改完代码回到 Unity）前主动销毁探针。
        /// 【为什么需要这一行】HideAndDontSave 对象不会被自动回收，静态字段又会在重载时清空，
        ///   于是 native 对象再也找不到人引用它 —— 反复编译就会悄悄堆积一堆看不见的 GameObject。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void RegisterProbeCleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseProbe;
        }
    }
}
