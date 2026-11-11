# -*- coding: utf-8 -*-
"""
2026-09-24 Buff 主键改造（Type 位 -> Id）
1A: HardControlMask 提升为 BuffType.HardControl + 加载期类别契约双向校验
方案2: BuffSystemPooled 增加 AddBuff(int) 入口 / 索引按位分解 / 移除后重算位图
"""
import io, os, sys

ROOT = r"E:\Project\BttleFreamwork-v1\Assets\Scripts"

def load(path):
    with io.open(path, "r", encoding="utf-8", newline="") as f:
        raw = f.read()
    crlf = "\r\n" in raw
    return raw.replace("\r\n", "\n"), crlf

def save(path, text, crlf):
    out = text.replace("\n", "\r\n") if crlf else text
    with io.open(path, "w", encoding="utf-8", newline="") as f:
        f.write(out)

def rep(text, old, new, tag):
    n = text.count(old)
    assert n == 1, "ANCHOR MISS[%s]: count=%d\n%s" % (tag, n, old[:120])
    return text.replace(old, new, 1)

# ============================ 1. BuffType.cs ============================
p = os.path.join(ROOT, r"BattleFreamwork\BufferSystem\BuffType.cs")
t, crlf = load(p)

t = rep(t,
"""    /// <summary>
    /// Buff类型位枚举 - 使用位标记实现高效的Buff检测
    /// </summary>""",
"""    /// <summary>
    /// Buff类型位枚举 - 使用位标记实现高效的Buff检测
    /// ★ 2026-09-24 起语义放宽：Type 由「一种Buff占一个位」改为「标签位图」——
    ///   ① CSV 的 BuffType 列允许写多个枚举名的组合（用 ';' 分隔）；
    ///   ② 同一个位可以被多个 BuffId 共享（例如 id=2001/2002 都是 Stunned 位，只是数值不同）；
    ///   ⇒ Buff 的数量不再受 64 位上限约束，新增 Buff 只需新增 Id，不必抢枚举位。
    /// ⚠ 由此：「单位身上有没有某个Buff」必须按 Id 判定（BuffSystemPooled.AddBuff(int)），
    ///   位图只用于 O(1) 的类别级查询（是否被控制 / 是否中毒 / 能否移动）。
    /// </summary>""",
"bufftype-doc")

t = rep(t,
"""        /// <summary>移动限制</summary>
        AllMovementImpair = Frozen | Stunned | Slowed | Petrified | Sleeping,""",
"""        /// <summary>移动限制</summary>
        AllMovementImpair = Frozen | Stunned | Slowed | Petrified | Sleeping,

        /// <summary>
        /// 硬控：不可移动 / 不可攻击 / 不可施法（眩晕/冰冻/石化/睡眠）—— FSM 依此切 Stun 态
        /// ★ 2026-09-24 从 UnitBuffComponent 的私有常量提升为公有组合标记：
        ///   新增硬控种类时只改这一处，FSM 仲裁 / 免疫 / 驱散全部自动生效
        /// </summary>
        HardControl = Stunned | Frozen | Petrified | Sleeping,""",
"bufftype-hardcontrol")

save(p, t, crlf)
print("OK BuffType.cs")

# ============================ 2. BuffData.cs ============================
p = os.path.join(ROOT, r"BattleFreamwork\BattleData\BuffData.cs")
t, crlf = load(p)

t = rep(t,
"""            buff.Id = int.Parse(str[0]);
            buff.Type= (BuffType)Enum.Parse(typeof(BuffType), str[1]);
            buff.Name = str[2];
            buff.Category = (BuffCategory)Enum.Parse(typeof(BuffCategory), str[3]);""",
"""            buff.Id = int.Parse(str[0]);

            //BuffType列支持组合标签：CSV里用';'分隔多个枚举名（引擎Enum.Parse用','，与CSV列分隔符冲突）
            //解析失败不再抛异常：报错指名后跳过该行
            BuffType parsedType;
            if (!Enum.TryParse(str[1].Trim().Replace(';', ','), out parsedType))
            {
                Debug.LogError("[BuffData] ID=" + str[0] + " BuffType列无法解析为枚举: '" + str[1] + "'，跳过该行");
                continue;
            }
            buff.Type = parsedType;
            buff.Name = str[2];

            BuffCategory parsedCategory;
            if (!Enum.TryParse(str[3].Trim(), out parsedCategory))
            {
                Debug.LogError("[BuffData] ID=" + buff.Id + " BuffCategory列无法解析: '" + str[3] + "'，跳过该行");
                continue;
            }
            buff.Category = parsedCategory;""",
"buffdata-parse")

t = rep(t,
"""            //重复键拒绝加载，防止运行时字典互相覆盖
            if (_buffs.ContainsKey(buff.Id))
            {
                Debug.LogError("[BuffData] 重复BuffId: " + buff.Id + "，跳过该行");
                continue;
            }
            if (_buffDic.ContainsKey(buff.Type))
            {
                Debug.LogError("[BuffData] 重复BuffType: " + buff.Type + "，跳过该行");
                continue;
            }

            _buffDic.Add(buff.Type, buff);
            _buffs.Add(buff.Id, buff);""",
"""            //★ Id 是唯一主键（Buff的精确识别只认Id，不再认枚举位）
            if (_buffs.ContainsKey(buff.Id))
            {
                Debug.LogError("[BuffData] 重复BuffId: " + buff.Id + "，跳过该行");
                continue;
            }

            //★ BuffType 降级为标签索引：允许多个Id共享同一个（组合）标签位——这是突破64位上限的前提。
            //  按Type反查只登记首个（AddBuff(BuffType) 兼容路径用它），新Buff一律走 AddBuff(int buffId)
            if (_buffDic.ContainsKey(buff.Type))
            {
                Debug.LogWarning("[BuffData] BuffType '" + buff.Type + "' 已由 ID=" + _buffDic[buff.Type].Id
                    + " 登记，ID=" + buff.Id + " 不再进Type索引（按Id施加不受影响）");
            }
            else
            {
                _buffDic.Add(buff.Type, buff);
            }

            //类别↔标签位契约校验：新增Type忘了归类 → 开机就报，不等到「免疫没生效」才发现
            ValidateCategoryContract(buff);

            _buffs.Add(buff.Id, buff);""",
"buffdata-index")

t = rep(t,
"""    /// <summary>
    /// 根据ID获取指定的数据
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public BuffData GetData(int id)
    {
        BuffData data;
        if (!_buffs.TryGetValue(id, out data))
        {
            Debug.LogError("[BuffData] 不存在的BuffId: " + id);
            return null;
        }
        return data;
    }
}""",
"""    /// <summary>
    /// 根据ID获取指定的数据（★ 推荐入口：Id 是 Buff 的主键）
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public BuffData GetData(int id)
    {
        BuffData data;
        if (!_buffs.TryGetValue(id, out data))
        {
            Debug.LogError("[BuffData] 不存在的BuffId: " + id);
            return null;
        }
        return data;
    }

    /// <summary>
    /// 类别↔标签位 双向契约校验（加载期一次性，零运行时开销）
    /// 目的：新增 BuffType 时若忘了登记进对应组合标记，开机即报错指名，
    ///       而不是等「免疫控制没生效 / 驱散驱不掉 / FSM不切硬控」时才被发现
    ///       （CSV的BuffCategory 与 枚举的组合标记 是两套真相源，必须同源）
    /// 处理：LogError 但仍加载该行 —— 归类缺失只影响免疫/驱散/FSM仲裁，
    ///       不影响该Buff自身数值生效，直接跳过行会让问题更难查
    /// </summary>
    private void ValidateCategoryContract(BuffData buff)
    {
        bool inControl  = (buff.Type & BuffType.AllControlEffects) != 0;
        bool inPositive = (buff.Type & BuffType.AllPositive) != 0;
        bool inNegative = (buff.Type & BuffType.AllNegative) != 0;

        //正向：声明了某类别，就必须带上该类别的标签位
        if (buff.Category == BuffCategory.Control && !inControl)
        {
            Debug.LogError("[BuffData] ID=" + buff.Id + " (" + buff.Type + ") 类别=Control，但 "
                + buff.Type + " 不在 BuffType.AllControlEffects 内 → 免疫控制 / 控制类驱散 / FSM硬控 都不会对它生效，"
                + "请把该类型登记进 BuffType.AllControlEffects");
        }
        if (buff.Category == BuffCategory.Positive && !inPositive)
        {
            Debug.LogError("[BuffData] ID=" + buff.Id + " (" + buff.Type + ") 类别=Positive，但 "
                + buff.Type + " 不在 BuffType.AllPositive 内 → 增益查询/统计会漏掉它，请把该类型登记进 BuffType.AllPositive");
        }
        if (buff.Category == BuffCategory.Negative && !inNegative)
        {
            Debug.LogError("[BuffData] ID=" + buff.Id + " (" + buff.Type + ") 类别=Negative，但 "
                + buff.Type + " 不在 BuffType.AllNegative 内 → 无敌免疫 / 减益驱散 都不会对它生效，请把该类型登记进 BuffType.AllNegative");
        }

        //反向：只查 Positive。AllPositive 是纯增益位（bit0~15），与任何控制/减益位无交集，不会误报；
        //      Control / Negative 的反向不查 —— AllNegative 历史上就含 Stunned/Silenced 等控制位，查了必然误报
        if (buff.Category != BuffCategory.Positive && inPositive)
        {
            Debug.LogError("[BuffData] ID=" + buff.Id + " 类别=" + buff.Category + "，但 " + buff.Type
                + " 命中了 BuffType.AllPositive（增益位）→ 会被当成增益统计/驱散，请核对 CSV 的 BuffCategory 列");
        }
    }
}""",
"buffdata-validate")

save(p, t, crlf)
print("OK BuffData.cs")

# ============================ 3. BuffSystemPooled.cs ============================
p = os.path.join(ROOT, r"BattleFreamwork\BufferSystem\BuffSystemPooled.cs")
t, crlf = load(p)

old_add = t[t.index("        /// <summary>\n        /// 添加一个BUFF\n"):t.index("        /// <summary>\n        /// 移除Buff（按类型，仅支持单一BuffType）\n")]
new_add = """        /// <summary>
        /// 添加一个BUFF（按BuffType —— 兼容入口）
        /// ⚠ 2026-09-24 起 BuffType 只是标签位图，不再是Buff主键：同一个标签位可被多个BuffId共享，
        ///   本入口按「整值精确匹配」查配置，取到的是首个登记该Type的配置行。
        ///   新增Buff请改用 AddBuff(int buffId)，不要再抢枚举位。
        /// </summary>
        /// <param name="type">BUFF类型/标签位（可以是组合位，CSV里用';'分隔）</param>
        /// <param name="casterId">施加者ID</param>
        public Buff AddBuff(BuffType type, int casterId)
        {
            if (type == BuffType.None)
            {
                Debug.LogError("[BuffSystem] AddBuff 不接受空类型 BuffType.None");
                return null;
            }

            var buffData = ConfigManager.GetBuffData(type);
            if (buffData == null)
            {
                Debug.LogError("[BuffSystem] 找不到 BuffType=" + type + " 对应的配置行"
                    + "（BufferData.csv 的 BuffType 列整值需与该枚举值一致）");
                return null;
            }
            return ApplyBuff(buffData, casterId);
        }

        /// <summary>
        /// 添加一个BUFF（按配置Id —— ★ 推荐入口，Buff数量不受枚举64位上限限制）
        /// </summary>
        /// <param name="buffId">BufferData.csv 的 ID 列</param>
        /// <param name="casterId">施加者ID</param>
        public Buff AddBuff(int buffId, int casterId)
        {
            var buffData = ConfigManager.GetBuffData(buffId);
            if (buffData == null)
            {
                Debug.LogError("[BuffSystem] 不存在的BuffId: " + buffId);
                return null;
            }
            return ApplyBuff(buffData, casterId);
        }

        /// <summary>
        /// Buff落地唯一实现：免疫检查 → 按Id叠加/刷新 → 出池 → 登记索引 → 置位
        /// 两个公开入口都收敛到这里，保证「按Id施加」与「按Type施加」行为完全一致
        /// </summary>
        private Buff ApplyBuff(BuffData buffData, int casterId)
        {
            if (buffData.Type == BuffType.None)
            {
                Debug.LogError("[BuffSystem] BuffId=" + buffData.Id + " 的 BuffType 为空，拒绝施加");
                return null;
            }

            // 检查免疫（标签位图：命中任一免疫类别即免疫）
            if (IsImmuneTo(buffData.Type))
                return null;

            // ★ 叠加/刷新按 Id 判定（原按Type：两个共享同一位的Buff会互相刷新，张冠李戴）
            Buff existing;
            if (_buffById.TryGetValue(buffData.Id, out existing))
            {
                // 可叠加
                if (existing._BuffData.Stackable && existing.StackCount < existing._BuffData.MaxStack)
                {
                    existing.StackCount++;
                    existing.RemainingTime = existing._BuffData.Duration; // 刷新持续时间
                    OnBuffUpdated?.Invoke(existing);
                    return existing;
                }

                // 刷新持续时间（同一个Buff）
                if (existing._BuffData.Duration > existing.RemainingTime)
                {
                    existing.RemainingTime = existing._BuffData.Duration;
                    OnBuffUpdated?.Invoke(existing);
                    return existing;
                }

                return null;
            }

            // 从池中获取Buff对象
            var buff = _buffPool.Spawn(true);
            buff._BuffData = buffData;
            buff.RemainingTime = buffData.Duration;//剩余时间=持续时间
            buff.StackCount = 1;//堆叠层数1
            buff.NextTickTime = _currentTime + buffData.TickInterval;//下一次触发时间=当前时间+间隔时间
            buff.CasterId = casterId;
            buff.IsActive = true;
            buff.CreatedTime = _currentTime;//自系统进入活跃期起的秒数

            // 添加到管理列表
            _activeBuffs.Add(buff);
            _buffById[buffData.Id] = buff;
            IndexByTypeBits(buff, true);

            // 置位：标签位图（组合位整体置上，IsImmuneTo / HasAnyBuff 用「命中任一位」语义，天然兼容）
            _buffFlags |= buffData.Type;

            OnBuffAdded?.Invoke(buff);
            return buff;
        }


"""
t = rep(t, old_add, new_add, "bsp-addbuff")

t = rep(t,
"""        public bool RemoveBuff(BuffType buffType)
        {
            if (!HasBuff(buffType)) return false;

            int removeCount = 0;
            _pendingRemove.Clear();

            //反向遍历原地收集（替代原Where().ToList()，零GC），循环结束后统一清算
            for (int i = _activeBuffs.Count - 1; i >= 0; i--)
            {
                if (_activeBuffs[i]._BuffData.Type == buffType)
                {""",
"""        public bool RemoveBuff(BuffType buffType)
        {
            if (buffType == BuffType.None) return false;
            //标签位图语义：命中任一位即算命中（原 HasBuff 是「全部位都有」，对组合位会漏删）
            if (!HasAnyBuff(buffType)) return false;

            int removeCount = 0;
            _pendingRemove.Clear();

            //反向遍历原地收集（替代原Where().ToList()，零GC），循环结束后统一清算
            for (int i = _activeBuffs.Count - 1; i >= 0; i--)
            {
                if ((_activeBuffs[i]._BuffData.Type & buffType) != 0)
                {""",
"bsp-removebuff")

t = rep(t,
"""            buff.IsActive = false;
            _activeBuffs.Remove(buff);
            _buffById.Remove(buff._BuffData.Id);

            if (_buffsByType.TryGetValue(buff._BuffData.Type, out var list))
            {
                list.Remove(buff);
                if (list.Count == 0)
                {
                    _buffsByType.Remove(buff._BuffData.Type);
                    // 清除标志位
                    _buffFlags &= ~buff._BuffData.Type;
                }
            }
""",
"""            buff.IsActive = false;
            _activeBuffs.Remove(buff);
            _buffById.Remove(buff._BuffData.Id);
            IndexByTypeBits(buff, false);

            //★ 标签位可被多个Buff共享 ⇒ 不能用 &= ~Type 清位（会把仍生效的同位Buff一起清掉）
            //  改为按当前存活Buff全量重算：Buff数量是十量级，移除是低频操作，开销可忽略
            RecomputeFlags();
""",
"bsp-removeimmediate")

t = rep(t,
"""            finally
            {
                _draining = false;
            }
        }

      
        #endregion""",
"""            finally
            {
                _draining = false;
            }
        }

        /// <summary>
        /// 按「单个标签位」登记 / 注销索引：组合位会分解成多个位分别登记，
        /// 这样 GetBuffs(Stunned) 能取到所有带 Stunned 位的Buff（不论它还带别的什么位）
        /// </summary>
        private void IndexByTypeBits(Buff buff, bool add)
        {
            ulong bits = (ulong)buff._BuffData.Type;
            while (bits != 0UL)
            {
                ulong low = bits & (~bits + 1UL);   //取最低置位（等价 bits & -bits）
                BuffType bit = (BuffType)low;

                if (add)
                {
                    List<Buff> list;
                    if (!_buffsByType.TryGetValue(bit, out list))
                    {
                        list = new List<Buff>();
                        _buffsByType[bit] = list;
                    }
                    list.Add(buff);
                }
                else
                {
                    List<Buff> list;
                    if (_buffsByType.TryGetValue(bit, out list))
                    {
                        list.Remove(buff);
                        if (list.Count == 0)
                            _buffsByType.Remove(bit);
                    }
                }

                bits &= bits - 1UL;                 //清掉最低置位
            }
        }

        /// <summary>
        /// 按当前存活Buff重算标签位图（移除Buff后调用）
        /// </summary>
        private void RecomputeFlags()
        {
            BuffType flags = BuffType.None;
            for (int i = 0; i < _activeBuffs.Count; i++)
            {
                flags |= _activeBuffs[i]._BuffData.Type;
            }
            _buffFlags = flags;
        }

        #endregion""",
"bsp-indexbits")

# 字段注释同步
t = rep(t,
"""        private readonly Dictionary<int, Buff> _buffById;
        private readonly Dictionary<BuffType, List<Buff>> _buffsByType;""",
"""        /// <summary>★ 主键索引：BuffId → Buff（精确识别、叠加/刷新的唯一依据）</summary>
        private readonly Dictionary<int, Buff> _buffById;
        /// <summary>标签索引：单个标签位 → 带该位的Buff列表（组合位已按位分解）</summary>
        private readonly Dictionary<BuffType, List<Buff>> _buffsByType;""",
"bsp-fields")

save(p, t, crlf)
print("OK BuffSystemPooled.cs")

# ============================ 4. UnitBuffComponent.cs ============================
p = os.path.join(ROOT, r"BattleFreamwork\Controller\UnitBuffComponent.cs")
t, crlf = load(p)

t = rep(t,
"""    /// <summary>硬控Buff位（眩晕/冰冻/石化/睡眠：不可移动、不可攻击、不可施法）</summary>
    private const BuffType HardControlMask = BuffType.Stunned | BuffType.Frozen | BuffType.Petrified | BuffType.Sleeping;""",
"""    // ★ 硬控位已提升为公有组合标记 BuffType.HardControl（见 BuffType.cs「组合标记」区）：
    //   新增硬控种类时只改那一处枚举，FSM仲裁 / 免疫 / 驱散全部自动生效，
    //   不再需要在本文件再抄一份掩码（抄两份 = 两套真相源，加类型时必漏一处）""",
"ubc-mask")

t = rep(t, "m_Unit.BuffSystem.HasAnyBuff(HardControlMask)", "m_Unit.BuffSystem.HasAnyBuff(BuffType.HardControl)", "ubc-use1")
t = rep(t, "return (HardControlMask & type) != 0 || type == BuffType.Silenced;",
        "return (BuffType.HardControl & type) != 0 || type == BuffType.Silenced;", "ubc-use2")

save(p, t, crlf)
print("OK UnitBuffComponent.cs")

# ============================ 5. CombatUnitController.cs ============================
p = os.path.join(ROOT, r"BattleFreamwork\Controller\CombatUnitController.cs")
t, crlf = load(p)

t = rep(t,
"""        //按配置Id读取Buff数据（缺失时GetData已打日志并返回null，不再抛异常）
        BuffData buffData = ConfigManager.GetBuffData(buffid);
        if (buffData == null)
        {
            return;
        }

        CurCombatUnit.BuffSystem.AddBuff(buffData.Type, attacker.GetInstanceID());""",
"""        //★ 走 Id 入口：BuffType 已降级为标签位图（同一个位可被多个BuffId共享），
        //  先转成 Type 再施加会张冠李戴（CSV里两个Id共用Stunned位时，永远只能加到第一个）。
        //  BuffId 不存在时 AddBuff 内部已打日志并返回 null。
        CurCombatUnit.BuffSystem.AddBuff(buffid, attacker.GetInstanceID());""",
"cuc-onaddbuff")

save(p, t, crlf)
print("OK CombatUnitController.cs")

print("ALL DONE")
