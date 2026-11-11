# -*- coding: utf-8 -*-
"""
给 Assets/Editor/SkillEditor/SkillEditorModel.cs 增加「新建技能」能力：

  A1. 新增 _newIds 集合（本次会话新建、尚未落盘的技能 id）
  A2. Refresh 一并清 _newIds
  A3. 新增公开 API：NextFreeId / IsIdTaken / CreateClone / CreateBlank / IsNew / DiscardNew
  A4. Save 支持「FindRowById 返回 null 且 IsNew ⇒ 追加新行（沿用 array[0] 键序 + 补齐缺键）」
  A5. 新增内部辅助：BuildNewRow / CopyAllMembers / ApplyBlankTemplate / RegisterNew / ...

纪律（项目铁律 7）：
  · 改 .cs 一律走 Python 脚本：old 命中数断言 == 1 + 幂等 + 写回后重读复验 + 延迟 2 秒二次复验。
  · 幂等判据 = new 已存在即跳过。
"""
import io, os, sys, time

PATH = r"E:\Project\BttleFreamwork-v1\Assets\Editor\SkillEditor\SkillEditorModel.cs"

# ── 编辑 1：新增 _newIds 字段 ────────────────────────────────
OLD_1 = """        /// <summary>被改过的技能 id。</summary>
        private static readonly HashSet<int> _dirty = new HashSet<int>();
"""

NEW_1 = """        /// <summary>被改过的技能 id。</summary>
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
"""

# ── 编辑 2：Refresh 清 _newIds ───────────────────────────────
OLD_2 = """            _dirty.Clear();
            _snapshots.Clear();

            RefreshFileBaseline();
"""

NEW_2 = """            _dirty.Clear();
            _snapshots.Clear();

            // ★ 新建（未落盘）的技能同样一并丢弃 —— 原因见 CreateBlank / CreateClone 的 XML 注释：
            //   ConfigManager.GetAllSkillData() 每次返回的是「新建的副本 List」，把新建对象 Add 进去
            //   进不了运行时的字典，Refresh 重建缓存后它们已不在表里；
            //   此时若还留着 _newIds，IsNew 会对着一个「不在缓存里」的 id 说真话 —— 那是假信息
            _newIds.Clear();

            RefreshFileBaseline();
"""

# ── 编辑 3：新增「新建技能」公开 API 区块 ─────────────────────
ANCHOR_3 = """        // ══════════════════════════════════════════════════════
        // 落盘
        // ══════════════════════════════════════════════════════
"""

NEW_3 = """        // ══════════════════════════════════════════════════════
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

""" + ANCHOR_3

# ── 编辑 4：Save 支持追加新行 ────────────────────────────────
OLD_4 = """            List<SkillFieldMeta> changed = CollectChangedMembers(data);
            if (changed.Count == 0)
            {
                // 没有真正发生变化的键（例如改了又改回来）⇒ 不碰文件，只把脏标记与快照清掉
                _dirty.Remove(data.ID);
                _snapshots.Remove(data.ID);
                return true;
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
                Debug.LogError("[SkillEditor] 保存失败：" + FileName + " 中找不到 " + IdKey + " = "
                    + skillId + " 的行，无法定位要更新的对象。");
                return false;
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
"""

NEW_4 = """            JArray array;
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
"""

# ── 编辑 5：落盘成功后从「新建」集合移除 ──────────────────────
OLD_5 = """            _loadedHash = SafeHashFile(path);
            _loadedByteCount = SafeFileLength(path);
            _dirty.Remove(skillId);
            _snapshots.Remove(skillId);
            return true;
"""

NEW_5 = """            _loadedHash = SafeHashFile(path);
            _loadedByteCount = SafeFileLength(path);
            _dirty.Remove(skillId);
            _snapshots.Remove(skillId);

            // ★ 落盘成功 ⇒ 磁盘上已有这一行，不再是「新建」。
            //   必须在这里摘掉：留着的话第二次 Save 会再走一次追加分支，产出一行重复 ID
            _newIds.Remove(skillId);
            return true;
"""

# ── 编辑 6：新增 BuildNewRow ─────────────────────────────────
ANCHOR_6 = """        private static bool TryToJToken(object value, out JToken token)
"""

NEW_6 = """        /// <summary>
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

""" + ANCHOR_6

EDITS = [
    ("_newIds 字段",           OLD_1, NEW_1, "_newIds"),
    ("Refresh 清 _newIds",     OLD_2, NEW_2, "_newIds.Clear();"),
    ("新建技能公开 API",        ANCHOR_3, NEW_3, "public static SkillData CreateBlank("),
    ("Save 追加新行",          OLD_4, NEW_4, "row = BuildNewRow(array, data);"),
    ("落盘后摘新建标记",        OLD_5, NEW_5, "_newIds.Remove(skillId);"),
    ("BuildNewRow",           ANCHOR_6, NEW_6, "private static JObject BuildNewRow("),
]


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

    # 幂等：六步全做过就跳过
    done = 0
    for name, old, new, marker in EDITS:
        if marker in src:
            done += 1
    if done == len(EDITS):
        print("SKIP 六步均已存在 —— 幂等跳过")
        return 0
    if done != 0:
        print("FAIL 部分已存在（%d/%d），脚本不处理半成品状态" % (done, len(EDITS)))
        return 1

    # old 命中数断言 == 1
    for name, old, new, marker in EDITS:
        cnt = src.count(old)
        if cnt != 1:
            print("FAIL [%s] old 命中 %d 次（要求 == 1）" % (name, cnt))
            return 1
    print("CHECK old 命中数 == 1：6/6 OK")

    out = src
    for name, old, new, marker in EDITS:
        out = out.replace(old, new, 1)

    stamp = time.strftime("%Y-%m-%d-createapi")
    bak = PATH + ".bak." + stamp
    if not os.path.exists(bak):
        write_text(bak, src)
        print("BACKUP " + bak)

    write_text(PATH, out)

    # 复验
    again = read_text(PATH)
    ok = True
    for name, old, new, marker in EDITS:
        if marker not in again:
            print("VERIFY-1 FAIL 缺少标记 [%s]: %s" % (name, marker))
            ok = False
    print("VERIFY-1 %s | len=%d" % ("OK" if ok else "FAIL", len(again)))
    if not ok:
        return 1

    time.sleep(2)
    again2 = read_text(PATH)
    ok2 = all(marker in again2 for name, old, new, marker in EDITS)
    print("VERIFY-2 %s | len=%d" % ("OK" if ok2 else "FAIL", len(again2)))
    return 0 if ok2 else 1


if __name__ == "__main__":
    sys.exit(main())
