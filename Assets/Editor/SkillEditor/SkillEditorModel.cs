// ★ 技能编辑器 · 数据读写层 —— 窗口与 SkillData 之间**唯一的读写通道**
//
// 【它的职责】三件事：① 把 ConfigManager 里的技能表缓存成「按 ID 升序」的只读视图；
//   ② 按 SkillFieldMetaTable 的契约用反射读写单个字段（Property / Field 两条路径）；
//   ③ 把改动**最小 diff** 地写回 SkillData.json，并保证不覆盖别人在编辑器持有期间的改动。
//
// 【为什么读写必须收口】SkillData 的 28 个成员里 Property 与 Field 混用，
//   反射时 PropertyInfo / FieldInfo 是两个不同类型，取错只会拿到 null 且**不报错**——
//   这类「改了没生效」的 bug 在界面上完全看不见。把两条路径收在一处，只在这里判一次形，
//   上层（主窗口 / 时间轴 / Gizmo）就永远不可能取错。
//
// 【为什么不整表 ToString() 重写】JArray 会保留每个 JObject 的**键插入顺序**，
//   只改涉改动的键即可让其余行、其余键一字不动；若把整个数组用 Newtonsoft 重新序列化，
//   键顺序会被统一重排（按 C# 声明序），一次保存产生上千行 diff，
//   code review 时真正的改动会被彻底淹没。这也是本项目「配置 diff 必须可读」的硬要求。
//
// 【★ 一次性特效缓存这个坑】SkillData 里 _validCastEffectIds / _validHitEffectIds 是「解析一次就锁死」的缓存，
//   改完 AttackEffects / HitEffect 后若不清，表现是「改了 A、播的还是 B，且不报任何错」。
//   本轮不碰运行时代码，所以在**本层用反射把两个 Resolved 布尔置回 false** —— 这是唯一的补救点。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace SkillEditorKit
{
    /// <summary>
    /// 技能编辑器的数据源：内存对象读写 + 脏标记 + 最小 diff 落盘。
    /// 全部静态方法，无任何 Unity 对象持有 —— 域重载（Enter Play Mode / 编译）后静态字段会归零，
    /// 所以调用方进入窗口时必须先调 <see cref="Refresh"/>，这也是它作为「生命周期入口」的原因。
    /// </summary>
    public static class SkillEditorModel
    {
        // ── 配置定位 ────────────────────────────────────────────
        private const string ConfigName = "SkillData";
        private const string FileName = ConfigName + ".json";
        private const string AssetPath = "Assets/Resources/Config/" + FileName;

        /// <summary>主键的 JSON 键名。SkillData 的成员名是全大写 ID，不是 Id —— 写错会拿到 null。</summary>
        private const string IdKey = "ID";

        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // ── 运行时缓存（Refresh 时整体重建）────────────────────
        private static readonly List<SkillData> _rows = new List<SkillData>();
        private static readonly Dictionary<int, SkillData> _byId = new Dictionary<int, SkillData>();

        /// <summary>Property 形态成员的反射句柄。按 MemberName 索引，避免每帧 GetProperty。</summary>
        private static readonly Dictionary<string, PropertyInfo> _propertyCache = new Dictionary<string, PropertyInfo>();

        /// <summary>Field 形态成员的反射句柄。与上一张表互斥，由 meta.Kind 决定查哪张。</summary>
        private static readonly Dictionary<string, FieldInfo> _fieldCache = new Dictionary<string, FieldInfo>();

        /// <summary>被改过的技能 id。</summary>
        private static readonly HashSet<int> _dirty = new HashSet<int>();

        /// <summary>
        /// 本次会话里「新建但尚未落盘」的技能 id。
        ///
        /// 【为什么必须单独记一份】新建的技能在 SkillData.json 里还没有对应的 JObject，
        ///   Save 时 FindRowById 必然返回 null。靠这个集合才能把「找不到行」分成两种：
        ///   ① 是新建 ⇒ 追加一行；② 不是新建 ⇒ 数据不一致，LogError 指名（铁律 5）。
        ///   若没有这份集合，二者只能一律报错，新建功能就彻底不可用了。
        /// </summary>
        private static readonly HashSet<int> _newIds = new HashSet<int>();

        /// <summary>
        /// 新建技能的起始 ID。既有技能落在 10001 ~ 20109（236 行，2026-10-13 统计），
        ///   10000 是空号；从 10000 起扫既能保证不与既有行相撞，也留出了「9xxx 以下」给历史号段。
        /// </summary>
        private const int MinNewId = 10000;

        /// <summary>首次 Set 前留的原始值快照：skillId → (成员名 → 原始值)。Revert 与「算 diff」都靠它。</summary>
        private static readonly Dictionary<int, Dictionary<string, object>> _snapshots =
            new Dictionary<int, Dictionary<string, object>>();

        /// <summary>本次载入时磁盘文件的 hash —— 保存前据此判断文件是否被外部改动过。</summary>
        private static string _loadedHash;

        /// <summary>本次载入时磁盘文件的字节数。仅用于报错时给出可定位的线索（"原来 x 字节，现在 y 字节"）。</summary>
        private static int _loadedByteCount;

        /// <summary>SkillData 上两个「一次性特效缓存」的 resolved 标志位。</summary>
        private static FieldInfo _castResolvedFlag;

        private static FieldInfo _hitResolvedFlag;

        private static bool _initialized;

        // ══════════════════════════════════════════════════════
        // 生命周期
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 进入窗口 / 重新载入时调用：重建技能表缓存、反射句柄、文件基线。
        /// ⚠ 会**丢弃全部未保存改动**（脏标记与快照一并清空）—— 想留着先 Save。
        /// </summary>
        public static void Refresh()
        {
            BuildRowCache();
            BuildMemberCache();
            CacheEffectResolvedFlags();

            // 刷新即视为「重新载入」：旧脏标记与旧快照已失去意义，必须清掉，
            // 否则 Revert 会把新载入的数据覆盖成上一次会话的值
            _dirty.Clear();
            _snapshots.Clear();

            // ★ 新建（未落盘）的技能同样一并丢弃 —— 原因见 CreateBlank / CreateClone 的 XML 注释：
            //   ConfigManager.GetAllSkillData() 每次返回的是「新建的副本 List」，把新建对象 Add 进去
            //   进不了运行时的字典，Refresh 重建缓存后它们已不在表里；
            //   此时若还留着 _newIds，IsNew 会对着一个「不在缓存里」的 id 说真话 —— 那是假信息
            _newIds.Clear();

            RefreshFileBaseline();
            _initialized = true;
        }

        /// <summary>全部技能数据（只读视图），已按 ID 升序。</summary>
        public static IReadOnlyList<SkillData> All()
        {
            EnsureLoaded();
            return _rows;
        }

        /// <summary>按技能号取数据；找不到返回 null。</summary>
        public static SkillData ById(int id)
        {
            EnsureLoaded();
            SkillData data;
            return _byId.TryGetValue(id, out data) ? data : null;
        }

        // ══════════════════════════════════════════════════════
        // 字段读写
        // ══════════════════════════════════════════════════════

        /// <summary>读一个字段的值（返回 object，调用方负责类型转换）；读不到返回 null。</summary>
        public static object Get(SkillData data, SkillFieldMeta meta)
        {
            EnsureLoaded();

            if (data == null)
            {
                Debug.LogError("[SkillEditor] 读取失败：SkillData 为 null。");
                return null;
            }
            if (meta == null || string.IsNullOrEmpty(meta.MemberName))
            {
                Debug.LogError("[SkillEditor] 读取失败：字段元数据为 null 或缺少 MemberName。");
                return null;
            }

            PropertyInfo property;
            if (_propertyCache.TryGetValue(meta.MemberName, out property))
            {
                return property.GetValue(data);
            }

            FieldInfo field;
            if (_fieldCache.TryGetValue(meta.MemberName, out field))
            {
                return field.GetValue(data);
            }

            // 契约表与 SkillData 已经对不上 —— 必须点名，否则上层只会拿到一个无声的 null
            Debug.LogError("[SkillEditor] 读取失败：SkillData 上找不到成员「" + meta.MemberName
                + "」（契约表与运行时类不一致，请核对 SkillFieldMetaTable 与 SkillData.cs）。");
            return null;
        }

        /// <summary>
        /// 写一个字段的值。
        /// </summary>
        /// <returns>true = 确实写入了（值有变化）；false = 值没变、字段只读、类型不符或写入失败。</returns>
        public static bool Set(SkillData data, SkillFieldMeta meta, object value)
        {
            EnsureLoaded();

            if (data == null)
            {
                Debug.LogError("[SkillEditor] 写入失败：SkillData 为 null。");
                return false;
            }
            if (meta == null || string.IsNullOrEmpty(meta.MemberName))
            {
                Debug.LogError("[SkillEditor] 写入失败（技能 " + data.ID + "）：字段元数据为 null 或缺少 MemberName。");
                return false;
            }

            // ① 只读字段一律拒绝 —— ID 是主键，改了会让 JSON 行与内存字典错位
            if (meta.ReadOnly)
            {
                Debug.LogWarning("[SkillEditor] 已拒绝写入只读字段：技能 " + data.ID + "(" + data.Name
                    + ") 的「" + meta.Label + "(" + meta.MemberName + ")」不允许在编辑器中修改。");
                return false;
            }

            // ② 类型校验：界面给的多半是 int / float / string，目标可能是枚举或数值，
            //    不做强转会把「填错了」变成「改了没生效」
            Type memberType = MemberTypeOf(meta);
            if (memberType == null)
            {
                Debug.LogError("[SkillEditor] 写入失败（技能 " + data.ID + "）：SkillData 上找不到成员「"
                    + meta.MemberName + "」（契约表与运行时类不一致）。");
                return false;
            }

            object coerced;
            if (!TryCoerceValue(value, memberType, out coerced))
            {
                Debug.LogWarning("[SkillEditor] 已拒绝写入：技能 " + data.ID + "(" + data.Name + ") 的「"
                    + meta.Label + "(" + meta.MemberName + ")」需要 " + memberType.Name + " 类型，"
                    + "但传入的是 " + (value == null ? "null" : value.GetType().Name) + "。");
                return false;
            }

            // ★ 特效缓存失效必须放在「值没变」的早退分支**之前**：
            //   UI 完全可能就地改 List<int> 再把同一个引用传回来，此时下面按内容比较会判定「值没变」，
            //   若在这里才失效就已经晚了 —— 结果正是「改了 A、播的还是 B」
            InvalidateEffectCacheIfNeeded(data, meta);

            // ③ 首次改动前先留快照（深拷贝），Revert 与「算最小 diff」共用这一份
            EnsureSnapshot(data);

            object current = Get(data, meta);
            if (ValuesEqual(current, coerced)) return false;

            // ④ 真正落笔
            try
            {
                WriteRaw(data, meta, coerced);
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillEditor] 写入异常：技能 " + data.ID + "(" + data.Name + ") 的「"
                    + meta.Label + "(" + meta.MemberName + ")」—— " + ex.Message);
                return false;
            }

            _dirty.Add(data.ID);
            return true;
        }

        // ══════════════════════════════════════════════════════
        // 脏标记
        // ══════════════════════════════════════════════════════

        /// <summary>该技能是否有未保存改动。</summary>
        public static bool IsDirty(int skillId)
        {
            EnsureLoaded();
            return _dirty.Contains(skillId);
        }

        /// <summary>是否有任意未保存改动。</summary>
        public static bool HasAnyDirty()
        {
            EnsureLoaded();
            return _dirty.Count > 0;
        }

        /// <summary>有哪些技能被改过（升序）。</summary>
        public static List<int> DirtyIds()
        {
            EnsureLoaded();
            List<int> ids = new List<int>(_dirty);
            ids.Sort();
            return ids;
        }

        /// <summary>放弃某技能的全部改动，把内存对象恢复成最初载入时的值。</summary>
        public static void Revert(int skillId)
        {
            EnsureLoaded();

            Dictionary<string, object> snapshot;
            if (!_snapshots.TryGetValue(skillId, out snapshot))
            {
                // 没留过快照 = 这个技能从未被改过，清掉脏标记即可
                _dirty.Remove(skillId);
                return;
            }

            SkillData data = ById(skillId);
            if (data == null)
            {
                Debug.LogError("[SkillEditor] 放弃改动失败：技能 " + skillId + " 已不在当前技能表中，无法回滚。");
                _snapshots.Remove(skillId);
                _dirty.Remove(skillId);
                return;
            }

            IReadOnlyList<SkillFieldMeta> metas = SkillFieldMetaTable.All;
            for (int i = 0; i < metas.Count; i++)
            {
                SkillFieldMeta meta = metas[i];
                object original;
                if (!snapshot.TryGetValue(meta.MemberName, out original)) continue;

                try
                {
                    // 再拷一份：快照本体要留着，不能被下一次 Set 的就地改动带走
                    WriteRaw(data, meta, CloneValue(original));
                }
                catch (Exception ex)
                {
                    Debug.LogError("[SkillEditor] 回滚异常：技能 " + data.ID + "(" + data.Name + ") 的「"
                        + meta.Label + "(" + meta.MemberName + ")」—— " + ex.Message);
                }
            }

            // 特效列表被回滚 ⇒ 缓存同样失效
            InvalidateEffectCache(data);

            _snapshots.Remove(skillId);
            _dirty.Remove(skillId);
        }

        // ══════════════════════════════════════════════════════
        // 新建技能（复制 / 空白模板）
        // ══════════════════════════════════════════════════════
        //
        // 【★ 一个必须先讲清楚的前提 —— 新建只活在「编辑器内存」里】
        //   ConfigManager.GetAllSkillData() → SkillDataManager.GetAll() 的实现是
        //   `return new List<SkillData>(m_SkillDataDic.Values);` —— 每次调用都**新建一份 List 副本**，
        //   不是运行时字典的可写引用。所以：
        //     · 把新建的 SkillData Add 进那个 List，运行时完全看不到（副本随调用结束即弃）；
        //     · 本层的 _rows / _byId 是编辑器自己的缓存，新建项挂进去后界面立刻可见、可编辑；
        //     · 但一旦调用 Refresh()（重编译 / 进 Play / 重开窗口），缓存按运行时数据重建，
        //       **未落盘的新建技能会丢失**。
        //   ⇒ 这是运行时的既有设计，本轮不改运行时代码（铁律：不动 Assets/Scripts/**）。
        //   ⇒ UI 必须在用户新建后提示「未保存，Refresh 后会丢失」，并在退出前引导 Save。
        //   落盘（Save）之后 SkillData.json 里有了这一行，Refresh 自然能重新载入，就不再丢。

        /// <summary>
        /// 取一个当前未被占用的技能 ID —— 从 <see cref="MinNewId"/>（10000）起的最小空号。
        /// </summary>
        /// <returns>可用 ID；号段被占满（正常不会发生）返回 -1。</returns>
        public static int NextFreeId()
        {
            EnsureLoaded();

            // 逐个试号：表里只有几百行，命中一个空号最多几百次字典查询，
            // 比维护一张「已用号段」表更不容易出错（号段表一旦漏同步，就会给出重复号）
            for (int id = MinNewId; id < int.MaxValue; id++)
            {
                if (!_byId.ContainsKey(id)) return id;
            }

            Debug.LogError("[SkillEditor] 取号失败：" + MinNewId + " 起的技能号段已被占满，无可用 ID。");
            return -1;
        }

        /// <summary>该 ID 是否已被占用（含本次会话新建、尚未落盘的）。</summary>
        public static bool IsIdTaken(int id)
        {
            EnsureLoaded();
            return _byId.ContainsKey(id);
        }

        /// <summary>
        /// 深拷贝一个既有技能，产出一条新技能。
        /// </summary>
        /// <param name="sourceId">被复制的源技能 ID。</param>
        /// <param name="newId">新技能 ID，必须未被占用且 ≥ <see cref="MinNewId"/>。</param>
        /// <param name="newName">新技能名称。</param>
        /// <returns>新建的 SkillData；失败（源不存在 / ID 冲突 / 名称为空）返回 null 并 LogError 指名。</returns>
        ///
        /// 【★ 为什么必须深拷贝】SkillData 里有六个引用型成员：
        ///   SegTimings(float[]) / CastEffectTimings(float[]) / EffectTypesBySegment(List&lt;int&gt;[]) /
        ///   HitEffect / AttackEffects / BuffID(List&lt;int&gt;)。
        ///   浅拷贝会让新技能与源技能**共享同一份数组 / 同一份 List** ——
        ///   界面上改新技能的第 2 段时机，源技能跟着一起变，且不报任何错（正是铁律 5 禁止的形态）。
        ///   所以这里走 CloneValue 逐字段复制，且对 List&lt;int&gt;[] 这种「数组套列表」要复制到第二层。
        public static SkillData CreateClone(int sourceId, int newId, string newName)
        {
            EnsureLoaded();

            SkillData source = ById(sourceId);
            if (source == null)
            {
                Debug.LogError("[SkillEditor] 新建失败：找不到要复制的源技能 " + sourceId
                    + "（请先确认该技能在当前技能表中）。");
                return null;
            }
            if (!ValidateNewId(newId)) return null;
            if (!ValidateNewName(newId, newName)) return null;

            SkillData copy = new SkillData();
            CopyAllMembers(source, copy);

            if (!ApplyIdentity(copy, newId, newName)) return null;
            return RegisterNew(copy);
        }

        /// <summary>
        /// 新建一条空白技能（默认模板）。
        /// </summary>
        /// <param name="newId">新技能 ID，必须未被占用且 ≥ <see cref="MinNewId"/>。</param>
        /// <param name="newName">新技能名称。</param>
        /// <returns>新建的 SkillData；失败返回 null 并 LogError 指名。</returns>
        ///
        /// 【★ 每个默认值为什么是它 —— 逐条给理由，将来有人要改，先看这里】
        ///   SegCount = 1：段数。0 表示「不分段 ⇒ 一次伤害都不结算」，空白模板必须能打出一刀，故取 1。
        ///   SegTimings = { 0.5f }：段时机是**归一化 0~1 的动画进度**（不是秒），
        ///                 取 0.5 = 动画走到一半时结算，是「挥到中途命中」最不容易出错的缺省。
        ///   CastEffectTimings = new float[0]：出手特效时机**按下标与 AttackEffects 一一配对**，
        ///                 空数组 ⇒ 一件特效都不播（空白模板本来也没配 AttackEffects，二者自洽）。
        ///                 注意不能填 null —— 空数组与 null 在后续 Count 判断上语义不同，空数组更安全。
        ///   EffectTypesBySegment = { {0} }：0 = ImmedDamage 立即伤害。
        ///                 ★ 不含 0 的段**一点血都不掉**（GetEffectTypesForSegment 返回该段列表，
        ///                   没有 ImmedDamage 就没有伤害结算），这是最容易踩的坑，故必填。
        ///   HitNum = -1：命中数量。-1 = 不限（Skill.cs:734 只在 > -1 时才限制数量）；
        ///                 ★ 填 0 会「一个目标都打不到」，绝不能当默认值。
        ///   AttackDist = 5：进入该距离才发起攻击。取 5 是近战手感的中位（全表 0~20）。
        ///   AttackRadius = 3：圆形 / 扇形半径。比 AttackDist 小一点，保证「能进入攻击距离」时范围不溢出。
        ///   AttackAngle = 180：扇形角度。180 = 正前方半圆，是扇形最常用的缺省（全表 0~360）。
        ///   SkillHitSharpType = Fan(2)：与上面 AttackAngle=180 配套；取 Fan 而不是 Circle，
        ///                 是为了让 AttackAngle 这一列在模板里就是「看得见效果」的，方便策划理解两者关系。
        ///   HitType = ScaleEnemy(1)：敌方范围。与 Fan 配套，最常见的小怪群伤语义。
        ///   CDTime = 1：冷却 1 秒，避免模板技能变成 0 CD 的连发（那会让试技能时看不清单次表现）。
        ///   Attack = 10：攻击力。给一个非零值，否则打出去 0 伤害会让人误以为流程坏了。
        ///   DeliveryType = Instant(0)：缺省即即时，全表既有技能也都是 0；
        ///                 ★ 若取 Projectile(1) 而 ProjectileId 未配，加载期会被指名报错，故模板不碰弹道。
        ///   ProjectileId = 0：0 = 未配，与 Instant 自洽。
        ///   其余（Icon / SkillType / SkillThrowType / Crit / 击退浮空 / BuffID / AttackEffects）留 CLR 默认值。
        ///   HitEffect 由 SkillData 的字段初始化器给出空 List，不再重复赋值。
        public static SkillData CreateBlank(int newId, string newName)
        {
            EnsureLoaded();

            if (!ValidateNewId(newId)) return null;
            if (!ValidateNewName(newId, newName)) return null;

            SkillData data = new SkillData();
            if (!ApplyBlankTemplate(data)) return null;
            if (!ApplyIdentity(data, newId, newName)) return null;
            return RegisterNew(data);
        }

        /// <summary>该技能是不是「本次会话新建、尚未落盘」。</summary>
        public static bool IsNew(int skillId)
        {
            EnsureLoaded();
            return _newIds.Contains(skillId);
        }

        /// <summary>
        /// 撤销一次新建 —— 把这条技能从编辑器缓存里摘掉。
        ///
        /// 【为什么只许撤未落盘的】已落盘的技能在磁盘上有实体行，
        ///   这里只能摘内存；一旦允许，界面会显示「技能消失了」，而磁盘上那行还在 ——
        ///   下一次 Refresh 它又冒出来，属于典型的静默不一致，故一律拒绝并指名。
        /// </summary>
        public static void DiscardNew(int skillId)
        {
            EnsureLoaded();

            if (!_newIds.Contains(skillId))
            {
                Debug.LogError("[SkillEditor] 撤销新建失败：技能 " + skillId + " 不是本次会话新建的技能"
                    + "（已落盘或本就存在于表中）。撤销只会摘掉内存行、不会删磁盘行，"
                    + "用它撤销已存在的技能会让界面与配置对不上，已拒绝。");
                return;
            }

            _newIds.Remove(skillId);
            _dirty.Remove(skillId);
            _snapshots.Remove(skillId);
            _byId.Remove(skillId);

            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].ID == skillId)
                {
                    _rows.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>新 ID 的合法性校验：≥ MinNewId 且未被占用。冲突一律指名，不静默换号。</summary>
        private static bool ValidateNewId(int newId)
        {
            if (newId < MinNewId)
            {
                Debug.LogError("[SkillEditor] 新建失败：新技能 ID " + newId + " < " + MinNewId
                    + "。该号段留给既有技能，请改用 NextFreeId() 取号。");
                return false;
            }
            if (_byId.ContainsKey(newId))
            {
                Debug.LogError("[SkillEditor] 新建失败：技能 ID " + newId
                    + " 已被占用（同 ID 行已存在或本次已新建过），请换一个号。");
                return false;
            }
            return true;
        }

        private static bool ValidateNewName(int newId, string newName)
        {
            // 名称留空会让 JSON 行变成 "Name": ""，界面上是一行看不出是哪个技能的空白项 ——
            // 属于「配了却看不出配了什么」，直接拦住
            if (string.IsNullOrEmpty(newName))
            {
                Debug.LogError("[SkillEditor] 新建失败（技能 " + newId + "）：新技能名称为空，"
                    + "请先在界面上填一个名字。");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 逐字段深拷贝：遍历契约表而不是手写 28 个字段 ——
        ///   将来 SkillData 加列，只要 SkillFieldMetaTable 同步了，这里自动跟着生效，不会漏拷。
        /// </summary>
        private static void CopyAllMembers(SkillData source, SkillData target)
        {
            IReadOnlyList<SkillFieldMeta> metas = SkillFieldMetaTable.All;
            for (int i = 0; i < metas.Count; i++)
            {
                SkillFieldMeta meta = metas[i];

                // CloneValue 对「数组套 List」会复制到第二层，对标量 / 枚举原样返回 —— 正是深拷贝所需
                object value = CloneValue(Get(source, meta));

                try
                {
                    WriteRaw(target, meta, value);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[SkillEditor] 复制异常（源技能 " + source.ID + " → 新技能）：字段「"
                        + meta.Label + "(" + meta.MemberName + ")」—— " + ex.Message);
                }
            }
        }

        /// <summary>写入空白模板的默认值。任一字段写不进去就整单失败（fail-fast，不许静默少设一个默认值）。</summary>
        private static bool ApplyBlankTemplate(SkillData data)
        {
            if (!SetRawByName(data, "SegCount", 1)) return false;

            // 段时机是归一化进度，0.5 = 动画走到一半结算
            float[] segTimings = new float[1];
            segTimings[0] = 0.5f;
            if (!SetRawByName(data, "SegTimings", segTimings)) return false;

            // 空数组（不是 null）：与「没配 AttackEffects」自洽，且 Count 判断上更安全
            if (!SetRawByName(data, "CastEffectTimings", new float[0])) return false;

            // 0 = ImmedDamage 立即伤害；不含 0 的段不掉血，这是模板必须带它的唯一理由
            List<int>[] segments = new List<int>[1];
            segments[0] = new List<int>();
            segments[0].Add(0);
            if (!SetRawByName(data, "EffectTypesBySegment", segments)) return false;

            if (!SetRawByName(data, "HitNum", -1)) return false;         // -1 = 不限命中数
            if (!SetRawByName(data, "AttackDist", 5f)) return false;
            if (!SetRawByName(data, "AttackRadius", 3f)) return false;
            if (!SetRawByName(data, "AttackAngle", 180f)) return false;
            if (!SetRawByName(data, "SkillHitSharpType", SkillHitSharpTypes.Fan)) return false;
            if (!SetRawByName(data, "HitType", SkillHitTypes.ScaleEnemy)) return false;
            if (!SetRawByName(data, "CDTime", 1f)) return false;
            if (!SetRawByName(data, "Attack", 10)) return false;
            if (!SetRawByName(data, "DeliveryType", SkillDeliveryType.Instant)) return false;
            return SetRawByName(data, "ProjectileId", 0);
        }

        /// <summary>
        /// 写入 ID 与 Name。ID 是契约表里的只读字段，只能走 WriteRaw 绕过只读检查 ——
        ///   这是新建场景唯一需要它的地方（Revert 是另一处）。
        /// </summary>
        private static bool ApplyIdentity(SkillData data, int newId, string newName)
        {
            SkillFieldMeta idMeta = SkillFieldMetaTable.Find(IdKey);
            SkillFieldMeta nameMeta = SkillFieldMetaTable.Find("Name");
            if (idMeta == null || nameMeta == null)
            {
                Debug.LogError("[SkillEditor] 新建失败：契约表里找不到 " + IdKey
                    + " / Name 字段，无法为新技能写入主键与名称。");
                return false;
            }

            try
            {
                WriteRaw(data, idMeta, newId);
                WriteRaw(data, nameMeta, newName);
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillEditor] 新建失败（技能 " + newId + "）：写入 " + IdKey
                    + " / Name 异常 —— " + ex.Message);
                return false;
            }
            return true;
        }

        /// <summary>按成员名写入（绕过只读与类型校验）。契约表里查不到该成员 ⇒ 指名报错并让整单失败。</summary>
        private static bool SetRawByName(SkillData data, string memberName, object value)
        {
            SkillFieldMeta meta = SkillFieldMetaTable.Find(memberName);
            if (meta == null)
            {
                Debug.LogError("[SkillEditor] 写入失败：契约表里找不到字段「" + memberName
                    + "」（SkillFieldMetaTable 与 SkillData 不一致）。");
                return false;
            }

            try
            {
                WriteRaw(data, meta, value);
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillEditor] 写入失败：字段「" + meta.Label + "(" + memberName
                    + ")」—— " + ex.Message);
                return false;
            }
            return true;
        }

        /// <summary>把新建对象挂进 _rows（按 ID 升序插位）/ _byId，并标为「新建 + 脏」。</summary>
        private static SkillData RegisterNew(SkillData data)
        {
            InsertRowSorted(data);

            if (_byId.ContainsKey(data.ID))
            {
                // ValidateNewId 已经挡过一轮；这里再挡一次，防止并发路径绕过校验写进重复主键
                Debug.LogError("[SkillEditor] 新建失败：技能 ID " + data.ID + " 重复，新行未被索引。");
                _rows.Remove(data);
                return null;
            }

            _byId.Add(data.ID, data);
            _newIds.Add(data.ID);

            // ★ 新建行必须进 _dirty：SaveAll 只遍历 DirtyIds()，不标脏的新建技能永远不会被落盘
            _dirty.Add(data.ID);
            return data;
        }

        /// <summary>按 ID 升序插位 —— 界面列表的稳定性依赖 _rows 有序（见 BuildRowCache 的 Sort）。</summary>
        private static void InsertRowSorted(SkillData data)
        {
            int index = _rows.Count;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].ID > data.ID)
                {
                    index = i;
                    break;
                }
            }
            _rows.Insert(index, data);
        }

        // ══════════════════════════════════════════════════════
        // 落盘
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 保存某技能：物理备份 → 校验磁盘未被外部改动 → 只改涉改动的键 → 清脏标记。
        /// </summary>
        /// <returns>true = 成功；false = 文件被外部改动过 / 找不到行 / 写入失败。</returns>
        public static bool Save(int skillId, bool createBackup = true)
        {
            EnsureLoaded();

            string path = AbsolutePath;
            if (!File.Exists(path))
            {
                Debug.LogError("[SkillEditor] 保存失败（技能 " + skillId + "）：找不到配置文件 " + path + "。");
                return false;
            }

            SkillData data = ById(skillId);
            if (data == null)
            {
                Debug.LogError("[SkillEditor] 保存失败：技能 " + skillId + " 不存在于当前技能表中。");
                return false;
            }

            // ① 物理备份 —— 沿用工程既有的 .bak.<时间戳> 命名习惯（见 GBLAnimatorBuilder.cs.bak.*）
            if (createBackup) CreateBackup(path);

            // ② 外部改动校验：编辑器可能开了很久，期间策划/别人可能直接改了文件，
            //    直接覆写等于静默吞掉别人的改动 —— 这类冲突必须在写入前拦住并点名
            int currentBytes = SafeFileLength(path);
            string currentHash = SafeHashFile(path);
            if (_loadedHash == null)
            {
                Debug.LogError("[SkillEditor] 保存失败（技能 " + skillId + "）：无法读取本次载入时的文件基线，"
                    + "为避免覆盖他人改动，已终止写入。请重新 Refresh() 后重试。");
                return false;
            }
            if (!string.Equals(currentHash, _loadedHash, StringComparison.Ordinal))
            {
                Debug.LogError("[SkillEditor] 保存失败：技能 " + skillId + "(" + data.Name + ") —— "
                    + FileName + " 在编辑器持有期间被外部改动过（载入时 " + _loadedByteCount
                    + " 字节 / hash " + _loadedHash.Substring(0, 8) + "，现在是 " + currentBytes
                    + " 字节）。已终止写入，请先 Refresh() 重新载入后再改。");
                return false;
            }

            string text = ReadTextUtf8(path);

            if (text == null)
            {
                Debug.LogError("[SkillEditor] 保存失败（技能 " + skillId + "）：无法读取 " + path + "。");
                return false;
            }

            JArray array;
            try
            {
                array = JArray.Parse(text);
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillEditor] 保存失败（技能 " + skillId + "）：" + FileName
                    + " 不是合法的 JSON 数组 —— " + ex.Message);
                return false;
            }

            JObject row = FindRowById(array, skillId);
            if (row == null)
            {
                // ★ 磁盘上还没有这一行 —— 只有「本次会话新建」才允许走追加路径。
                //   非新建技能走到这里 = 内存与磁盘已经对不上（数据不一致），必须指名并终止
                if (!IsNew(skillId))
                {
                    Debug.LogError("[SkillEditor] 保存失败：" + FileName + " 中找不到 " + IdKey + " = "
                        + skillId + " 的行，且该技能不是本次会话新建的（内存与磁盘不一致），"
                        + "无法定位要更新的对象。请 Refresh() 后重试。");
                    return false;
                }

                row = BuildNewRow(array, data);
                if (row == null) return false;

                // 追加到末尾：既有行的相对顺序一字不动，diff 只有新增的这一块
                array.Add(row);
            }
            else
            {
                List<SkillFieldMeta> changed = CollectChangedMembers(data);
                if (changed.Count == 0)
                {
                    // 没有真正发生变化的键（例如改了又改回来）⇒ 不碰文件，只把脏标记与快照清掉
                    _dirty.Remove(data.ID);
                    _snapshots.Remove(data.ID);
                    return true;
                }

                // ③ 只改涉改动的键 —— JObject 保留原有键顺序，替换已有键时位置不变，
                //    其余行与其余键一字不动
                for (int i = 0; i < changed.Count; i++)
                {
                    SkillFieldMeta meta = changed[i];
                    JToken token;
                    if (!TryToJToken(Get(data, meta), out token))
                    {
                        Debug.LogError("[SkillEditor] 保存失败（技能 " + skillId + "）：字段「"
                            + meta.Label + "(" + meta.MemberName + ")」无法序列化为 JSON，已终止写入。");
                        return false;
                    }
                    row[meta.MemberName] = token;
                }
            }

            string output = array.ToString(Formatting.Indented);

            // Newtonsoft 的缩进换行取的是 Environment.NewLine；万一原文件用的是另一种风格，
            // 一次保存会把整个文件的换行全部改掉 ⇒ 统一回原文件风格，diff 才是干净的
            output = NormalizeNewLines(output, DetectNewLine(text));

            if (!WriteTextRobust(path, output))
            {
                Debug.LogError("[SkillEditor] 保存失败（技能 " + skillId + "）：写入 " + path + " 失败，"
                    + "改动仍留在内存中（可通过备份文件 " + path + ".bak.* 手动核对）。");
                return false;
            }

            // 让 Unity 重新导入，否则 Resources.Load 拿到的仍是旧文本，
            // 下一次保存会因为「基线不一致」被误判成外部改动
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);

            // ④ 落盘成功 ⇒ 磁盘与内存已一致，基线随之刷新，脏标记与快照清掉
            _loadedHash = SafeHashFile(path);
            _loadedByteCount = SafeFileLength(path);
            _dirty.Remove(skillId);
            _snapshots.Remove(skillId);

            // ★ 落盘成功 ⇒ 磁盘上已有这一行，不再是「新建」。
            //   必须在这里摘掉：留着的话第二次 Save 会再走一次追加分支，产出一行重复 ID
            _newIds.Remove(skillId);
            return true;
        }

        /// <summary>全部脏技能一次性保存。</summary>
        /// <returns>成功保存的技能个数。</returns>
        public static int SaveAll()
        {
            EnsureLoaded();

            // 先拷一份：Save 过程中会修改 _dirty，边遍历边改集合会出问题
            List<int> ids = DirtyIds();
            int saved = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                // 整批只留一份备份：备份名时间戳精确到秒，逐个备份会在同一秒内互相覆盖，
                // 反而只剩最后一个技能的旧版本，比只留一份更糟
                if (Save(ids[i], i == 0)) saved++;
            }
            return saved;
        }

        // ══════════════════════════════════════════════════════
        // 一次性特效缓存
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 清掉 SkillData 的两个「一次性特效缓存」，让下一次取值重新解析 AttackEffects / HitEffect。
        ///
        /// 【为什么必须有这一步】SkillData 里 _validCastEffectIds / _validHitEffectIds 是「算一次就锁死」的缓存
        ///   （见 _validCastEffectIdsResolved / _validHitEffectIdsResolved 两个 bool）。
        ///   配置对象在编辑器里是**长期存活**的，改完特效 id 后若不清缓存，
        ///   GetCastEffectIdList() 直接返回旧列表 ⇒ 表现是「改了 A、播的还是 B，且不报任何错」。
        ///   本轮不碰运行时代码，所以在这里用反射把两个 bool 置回 false —— 私有字段可写，代价为零。
        /// </summary>
        public static void InvalidateEffectCache(SkillData data)
        {
            if (data == null) return;

            CacheEffectResolvedFlags();
            ResetResolvedFlag(data, _castResolvedFlag, "_validCastEffectIdsResolved");
            ResetResolvedFlag(data, _hitResolvedFlag, "_validHitEffectIdsResolved");
        }

        /// <summary>
        /// 只有特效类字段才需要失效缓存。判据取自契约表的 <see cref="SkillFieldMeta.Reference"/>：
        ///   RefTable.CastEffect ↔ AttackEffects、RefTable.HitEffect ↔ HitEffect，
        ///   这样将来加了新特效列，只要契约表里标了引用，本层就自动跟着生效，不用改两处。
        /// </summary>
        private static void InvalidateEffectCacheIfNeeded(SkillData data, SkillFieldMeta meta)
        {
            if (meta.Reference == RefTable.CastEffect || meta.Reference == RefTable.HitEffect)
            {
                InvalidateEffectCache(data);
            }
        }

        private static void ResetResolvedFlag(SkillData data, FieldInfo flag, string fieldName)
        {
            if (flag == null)
            {
                Debug.LogError("[SkillEditor] 技能 " + data.ID + "：无法清理一次性特效缓存 —— "
                    + "SkillData 上找不到私有字段 " + fieldName
                    + "（运行时类可能被改过名，请同步本文件的反射名）。");
                return;
            }
            if (flag.FieldType != typeof(bool))
            {
                Debug.LogError("[SkillEditor] 技能 " + data.ID + "：无法清理一次性特效缓存 —— "
                    + "SkillData 的字段 " + fieldName + " 类型不是 bool（实际是 "
                    + flag.FieldType.Name + "）。");
                return;
            }

            try
            {
                flag.SetValue(data, false);
            }
            catch (Exception ex)
            {
                // ⚠ 吞掉异常等于放过一个「改了没生效」的坑，必须点名
                Debug.LogError("[SkillEditor] 技能 " + data.ID + "：清理一次性特效缓存失败（"
                    + fieldName + "）—— " + ex.Message);
            }
        }

        private static void CacheEffectResolvedFlags()
        {
            if (_castResolvedFlag != null && _hitResolvedFlag != null) return;

            Type type = typeof(SkillData);
            _castResolvedFlag = type.GetField("_validCastEffectIdsResolved", PrivateInstance);
            _hitResolvedFlag = type.GetField("_validHitEffectIdsResolved", PrivateInstance);
        }

        // ══════════════════════════════════════════════════════
        // 内部：缓存构建
        // ══════════════════════════════════════════════════════

        /// <summary>静态字段在域重载后会归零 —— 所有入口先过这一道，保证不会拿到空缓存。</summary>
        private static void EnsureLoaded()
        {
            if (!_initialized) Refresh();
        }

        private static void BuildRowCache()
        {
            _rows.Clear();
            _byId.Clear();

            List<SkillData> source = ConfigManager.GetAllSkillData();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    SkillData data = source[i];
                    if (data == null) continue;
                    _rows.Add(data);
                }
            }

            // 界面列表要稳定可预期 ⇒ 按 ID 升序，不依赖 JSON 文件里的行序
            _rows.Sort(CompareById);

            for (int i = 0; i < _rows.Count; i++)
            {
                SkillData data = _rows[i];
                if (_byId.ContainsKey(data.ID))
                {
                    Debug.LogError("[SkillEditor] 技能表出现重复 ID，后者未被索引：" + data.ID);
                    continue;
                }
                _byId.Add(data.ID, data);
            }
        }

        private static int CompareById(SkillData a, SkillData b)
        {
            return a.ID.CompareTo(b.ID);
        }

        /// <summary>
        /// 一次性把 28 个成员的反射句柄建好。
        /// 【为什么必须预建】GetProperty / GetField 内部要扫成员表并做字符串匹配，
        ///   而右面板是每帧刷新的热点；放到 Refresh 里只做一次，之后全是字典查表。
        /// </summary>
        private static void BuildMemberCache()
        {
            _propertyCache.Clear();
            _fieldCache.Clear();

            Type type = typeof(SkillData);
            IReadOnlyList<SkillFieldMeta> metas = SkillFieldMetaTable.All;

            for (int i = 0; i < metas.Count; i++)
            {
                SkillFieldMeta meta = metas[i];
                if (meta == null || string.IsNullOrEmpty(meta.MemberName)) continue;

                if (meta.Kind == MemberKind.Property)
                {
                    PropertyInfo property = type.GetProperty(meta.MemberName, PublicInstance);
                    if (property == null || !property.CanRead)
                    {
                        Debug.LogError("[SkillEditor] 契约破损：SkillData 上没有可读的 Property「"
                            + meta.MemberName + "」，该字段在编辑器中不可用。");
                        continue;
                    }
                    _propertyCache[meta.MemberName] = property;
                }
                else
                {
                    FieldInfo field = type.GetField(meta.MemberName, PublicInstance);
                    if (field == null)
                    {
                        Debug.LogError("[SkillEditor] 契约破损：SkillData 上没有 Field「"
                        + meta.MemberName + "」，该字段在编辑器中不可用。");
                        continue;
                    }
                    _fieldCache[meta.MemberName] = field;
                }
            }
        }

        private static Type MemberTypeOf(SkillFieldMeta meta)
        {
            PropertyInfo property;
            if (_propertyCache.TryGetValue(meta.MemberName, out property)) return property.PropertyType;

            FieldInfo field;
            if (_fieldCache.TryGetValue(meta.MemberName, out field)) return field.FieldType;

            return null;
        }

        /// <summary>绕开只读/类型检查直接写入 —— 只给 Revert 用（只读的 ID 也要能回滚）。</summary>
        private static void WriteRaw(SkillData data, SkillFieldMeta meta, object value)
        {
            PropertyInfo property;
            if (_propertyCache.TryGetValue(meta.MemberName, out property))
            {
                property.SetValue(data, value);
                return;
            }

            FieldInfo field;
            if (_fieldCache.TryGetValue(meta.MemberName, out field))
            {
                field.SetValue(data, value);
            }
        }

        // ══════════════════════════════════════════════════════
        // 内部：快照
        // ══════════════════════════════════════════════════════

        /// <summary>首次改动前把 28 个字段的原始值整体快照下来（深拷贝）。</summary>
        private static void EnsureSnapshot(SkillData data)
        {
            if (_snapshots.ContainsKey(data.ID)) return;

            IReadOnlyList<SkillFieldMeta> metas = SkillFieldMetaTable.All;
            Dictionary<string, object> snapshot = new Dictionary<string, object>(metas.Count);
            for (int i = 0; i < metas.Count; i++)
            {
                SkillFieldMeta meta = metas[i];
                // ★ 必须深拷贝：HitEffect / AttackEffects 是 List<int>、SegTimings 是 float[]，
                //   只存引用的话，UI 一就地改动，快照跟着一起变，Revert 就成了空操作
                snapshot[meta.MemberName] = CloneValue(Get(data, meta));
            }
            _snapshots[data.ID] = snapshot;
        }

        /// <summary>
        /// 深拷贝一个字段值。只处理本工程实际用到的三种形态：标量、数组、List。
        /// </summary>
        private static object CloneValue(object value)
        {
            if (value == null) return null;

            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal) return value;

            if (type.IsArray)
            {
                Array source = (Array)value;
                Array copy = (Array)source.Clone();

                Type element = type.GetElementType();
                bool elementIsScalar = element != null &&
                                       (element.IsPrimitive || element.IsEnum || element == typeof(string));
                if (!elementIsScalar)
                {
                    // 元素是引用类型（如 List<int>[]）⇒ 只 Clone 一层是不够的，内层的 List 仍共享
                    for (int i = 0; i < copy.Length; i++)
                    {
                        copy.SetValue(CloneValue(copy.GetValue(i)), i);
                    }
                }
                return copy;
            }

            IList list = value as IList;
            if (list != null)
            {
                object copy = Activator.CreateInstance(type);
                IList copyList = (IList)copy;
                for (int i = 0; i < list.Count; i++) copyList.Add(CloneValue(list[i]));
                return copy;
            }

            return value;
        }

        /// <summary>
        /// 值比较。集合类型必须**逐元素比**：界面常常新建一个内容相同的 List 传回来，
        /// 若按引用比会误判成「值变了」，把没改的键也写进 JSON，产生多余 diff。
        /// </summary>
        private static bool ValuesEqual(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;

            // string 也实现 IEnumerable，但它要按值比，所以先排除
            IEnumerable sequenceA = (a is string) ? null : (a as IEnumerable);
            IEnumerable sequenceB = (b is string) ? null : (b as IEnumerable);
            if (sequenceA != null && sequenceB != null)
            {
                IEnumerator iteratorA = sequenceA.GetEnumerator();
                IEnumerator iteratorB = sequenceB.GetEnumerator();
                try
                {
                    return CompareSequences(iteratorA, iteratorB);
                }
                finally
                {
                    IDisposable disposableA = iteratorA as IDisposable;
                    if (disposableA != null) disposableA.Dispose();

                    IDisposable disposableB = iteratorB as IDisposable;
                    if (disposableB != null) disposableB.Dispose();
                }
            }

            return a.Equals(b);
        }

        private static bool CompareSequences(IEnumerator a, IEnumerator b)
        {
            while (true)
            {
                bool nextA = a.MoveNext();
                bool nextB = b.MoveNext();
                if (nextA != nextB) return false;   // 长度不同
                if (!nextA) return true;            // 同时走到末尾
                if (!ValuesEqual(a.Current, b.Current)) return false;
            }
        }

        /// <summary>对比快照，算出这个技能**真正改动了哪些键** —— 保存时只写这些键。</summary>
        private static List<SkillFieldMeta> CollectChangedMembers(SkillData data)
        {
            List<SkillFieldMeta> changed = new List<SkillFieldMeta>();

            Dictionary<string, object> snapshot;
            if (!_snapshots.TryGetValue(data.ID, out snapshot)) return changed;   // 没快照 = 没改过

            IReadOnlyList<SkillFieldMeta> metas = SkillFieldMetaTable.All;
            for (int i = 0; i < metas.Count; i++)
            {
                SkillFieldMeta meta = metas[i];
                object original;
                if (!snapshot.TryGetValue(meta.MemberName, out original)) continue;
                if (!ValuesEqual(original, Get(data, meta))) changed.Add(meta);
            }
            return changed;
        }

        // ══════════════════════════════════════════════════════
        // 内部：文件读写
        // ══════════════════════════════════════════════════════

        /// <summary>SkillData.json 在磁盘上的绝对路径（Resources 目录不随平台变，故取自 Application.dataPath）。</summary>
        private static string AbsolutePath
        {
            get { return Path.Combine(Application.dataPath, "Resources/Config/" + FileName); }
        }

        /// <summary>记下「本次载入时」的文件指纹，供保存前做外部改动校验。</summary>
        private static void RefreshFileBaseline()
        {
            _loadedHash = SafeHashFile(AbsolutePath);
            _loadedByteCount = SafeFileLength(AbsolutePath);

            if (_loadedHash == null)
            {
                Debug.LogError("[SkillEditor] 无法读取 " + AbsolutePath
                    + " —— 技能表可能为空，且保存功能将被禁用（缺少校验基线）。");
            }
        }

        private static void CreateBackup(string path)
        {
            // 时间戳精确到秒即可；同秒内连续保存会互相覆盖，但那属于同一轮编辑，留最新一份足够
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string backup = path + ".bak." + stamp;
            try
            {
                File.Copy(path, backup, true);
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillEditor] 备份失败：无法把 " + path + " 复制为 " + backup
                    + " —— " + ex.Message + "（备份失败不阻断保存，但请手工留一份再继续）");
            }
        }

        /// <summary>
        /// 把 UFT-8 文本写回磁盘。
        /// 【为什么要分两步】本工程历史上出现过「Windows 上直接覆写已存在文件被拒绝」的情况
        ///   （只读属性 / 杀软 / 其它进程持有句柄）。先直接写，失败则退回「临时文件 + File.Copy 覆盖」。
        /// </summary>
        private static bool WriteTextRobust(string path, string text)
        {
            try
            {
                File.WriteAllText(path, text, Utf8NoBom);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SkillEditor] 直接写入 " + path + " 失败（" + ex.Message
                    + "），改用临时文件覆盖。");
            }

            string tmp = path + ".tmp." + DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            try
            {
                File.WriteAllText(tmp, text, Utf8NoBom);
                File.Copy(tmp, path, true);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillEditor] 写入 " + path + " 失败：" + ex.Message);
                return false;
            }
            finally
            {
                TryDelete(tmp);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // 临时文件删不掉不影响结果，下次写入会覆盖同名文件；不在这里刷屏
            }
        }

        /// <summary>
        /// 读取文本并按 UTF-8 解码。
        /// ⚠ 必须手动剥掉 BOM：UTF8Encoding.GetString 会把 BOM 解成 U+FEFF 留在串首，
        ///   带着它 JArray.Parse 会直接失败。这里统一剥掉，写入时也不加 BOM（Utf8NoBom）。
        /// </summary>
        private static string ReadTextUtf8(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                string text = Encoding.UTF8.GetString(bytes);
                if (text.Length > 0 && text[0] == '\uFEFF') return text.Substring(1);
                return text;
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillEditor] 读取文件失败：" + path + " —— " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 用**原始字节**算 hash 而不是先解码成字符串 —— 换行风格、BOM、不可见字符的差异都要能抓到，
        /// 编码往返一次就会把这些差异悄悄抹掉，校验形同虚设。
        /// </summary>
        private static string SafeHashFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] bytes = File.ReadAllBytes(path);
                return HashBytes(bytes);
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkillEditor] 计算文件 hash 失败：" + path + " —— " + ex.Message);
                return null;
            }
        }

        private static string HashBytes(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }

        private static int SafeFileLength(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                return info.Exists ? (int)info.Length : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>按 <c>ID</c> 找到那一行；找不到返回 null（调用方负责 LogError 指名）。</summary>
        private static JObject FindRowById(JArray array, int skillId)
        {
            for (int i = 0; i < array.Count; i++)
            {
                JObject row = array[i] as JObject;
                if (row == null) continue;

                JToken token;
                if (!row.TryGetValue(IdKey, out token)) continue;
                if (token.Type != JTokenType.Integer) continue;

                if (token.Value<int>() == skillId) return row;
            }
            return null;
        }

        /// <summary>
        /// 为新建技能造一个**键齐全**的 JObject —— 键顺序沿用 array[0]，array[0] 漏掉的键按契约表补齐。
        ///
        /// 【★ 为什么键顺序必须抄 array[0]】JObject 保留键的插入顺序，
        ///   若按 C# 声明序（契约表顺序）造行，新行的键序会和既有 236 行完全不同 ——
        ///   一旦将来需要全表重写，diff 会有上千行，真正的改动被彻底淹没（本项目「最小 diff」硬要求）。
        ///   ⇒ 抄第一行 = 新行与既有行**长得一模一样**，只有新增的这一块出现在 diff 里。
        ///
        /// 【★ 为什么要补齐】现在 array[0]（ID=10001）没有 DeliveryType / ProjectileId 两个键
        ///   （2026-10-10 弹道方案后加的，只写进了 ID=10201 那一行）。
        ///   只抄 array[0] 会让新行缺这两个键 —— 虽然 Newtonsoft 缺键走 CLR 默认值 0 不会崩，
        ///   但「新行与其它行的键集合不一致」本身就是隐患（将来有人按键集合做迁移会漏掉新行），
        ///   故一律补齐，且补齐的键排在末尾（与 ID=10201 那行的实际位置一致）。
        /// </summary>
        private static JObject BuildNewRow(JArray array, SkillData data)
        {
            JObject template = (array.Count > 0) ? (array[0] as JObject) : null;
            if (template == null)
            {
                Debug.LogError("[SkillEditor] 新建行失败（技能 " + data.ID + "）：" + FileName
                    + " 是空数组或第一行不是 JSON 对象，无法沿用既有键顺序。");
                return null;
            }

            // ① 键顺序 = 第一行的既有键序 + 契约表里「第一行没有」的键
            List<string> keyOrder = new List<string>(SkillFieldMetaTable.All.Count + 2);

            // 用显式枚举器而不是 foreach：JProperty 是引用类型（不会装箱），
            // 且能明确 Dispose —— 与 ValuesEqual 里的写法保持一致
            IEnumerator<JProperty> props = template.Properties().GetEnumerator();
            try
            {
                while (props.MoveNext()) keyOrder.Add(props.Current.Name);
            }
            finally
            {
                props.Dispose();
            }

            IReadOnlyList<SkillFieldMeta> metas = SkillFieldMetaTable.All;
            for (int i = 0; i < metas.Count; i++)
            {
                string member = metas[i].MemberName;
                if (!keyOrder.Contains(member)) keyOrder.Add(member);
            }

            // ② 逐键取值写入。JSON 里有、契约表里没有的键 = 契约破损，必须指名（不许静默少写）
            JObject row = new JObject();
            for (int i = 0; i < keyOrder.Count; i++)
            {
                string key = keyOrder[i];
                SkillFieldMeta meta = SkillFieldMetaTable.Find(key);
                if (meta == null)
                {
                    Debug.LogError("[SkillEditor] 新建行失败（技能 " + data.ID + "）：" + FileName
                        + " 的第一行含键「" + key + "」，但 SkillFieldMetaTable 里没有同名字段"
                        + "（配置列与契约表不一致），已终止写入。");
                    return null;
                }

                JToken token;
                if (!TryToJToken(Get(data, meta), out token))
                {
                    Debug.LogError("[SkillEditor] 新建行失败（技能 " + data.ID + "）：字段「"
                        + meta.Label + "(" + meta.MemberName + ")」无法序列化为 JSON，已终止写入。");
                    return null;
                }
                row[key] = token;
            }
            return row;
        }

        private static bool TryToJToken(object value, out JToken token)
        {
            token = null;
            try
            {
                // null 必须显式转成 JSON 的 null：JToken.FromObject(null) 返回的是 null 引用，
                // 赋给 JObject 索引器会被当成「删除该键」，语义完全相反
                token = (value == null) ? JValue.CreateNull() : JToken.FromObject(value);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>猜原文件用的是 CRLF 还是 LF —— 没换行的文件就跟着当前平台走。</summary>
        private static string DetectNewLine(string text)
        {
            int index = text.IndexOf('\n');
            if (index < 0) return Environment.NewLine;
            return (index > 0 && text[index - 1] == '\r') ? "\r\n" : "\n";
        }

        private static string NormalizeNewLines(string text, string newLine)
        {
            if (string.Equals(text, string.Empty, StringComparison.Ordinal)) return text;
            if (string.Equals(newLine, Environment.NewLine, StringComparison.Ordinal)) return text;

            // 先统一成 LF 再换成目标风格，避免在已经是 \r\n 的文本上二次替换出 \r\r\n
            return text.Replace("\r\n", "\n").Replace("\n", newLine);
        }

        // ══════════════════════════════════════════════════════
        // 内部：类型适配
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把界面传来的值转成成员的真实类型。
        /// 【为什么不能直接用 SetValue】反射的 SetValue 对 1 → float、0 → SkillTypes 这类转换会抛
        ///   ArgumentException，而右面板里「int 输入框改 float 字段」是家常便饭；
        ///   在这里统一转一次，上层就只管传界面上的原始值。
        /// </summary>
        private static bool TryCoerceValue(object value, Type target, out object result)
        {
            result = null;

            // 已经是目标类型（枚举、List<int>、float[]、List<int>[] 都走这里）⇒ 直接放行，
            // 别往下走 Convert.ChangeType —— 它对集合类型只会抛异常
            if (target.IsInstanceOfType(value))
            {
                result = value;
                return true;
            }

            if (value == null)
            {
                // 引用类型允许置 null（如 SegTimings = null 表示"不配时机"）；
                // 值类型不能为 null，拒绝并让调用方看到警告
                return !target.IsValueType;
            }

            if (target.IsEnum)
            {
                try
                {
                    string text = value as string;
                    result = (text != null)
                        ? Enum.Parse(target, text, true)
                        : Enum.ToObject(target, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            if (target == typeof(string))
            {
                IFormattable formattable = value as IFormattable;
                result = (formattable != null)
                    ? formattable.ToString(null, CultureInfo.InvariantCulture)
                    : value.ToString();
                return true;
            }

            if ((value is IConvertible) && typeof(IConvertible).IsAssignableFrom(target))
            {
                try
                {
                    result = Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }
    }
}
