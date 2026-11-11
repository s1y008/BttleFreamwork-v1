using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BattleFramework.Core
{
    /// <summary>
    /// 游戏Buff系统 - 使用对象池优化，零GC版本
    /// </summary>
    public class BuffSystemPooled 
    {
        private readonly List<Buff> _activeBuffs;
        private BuffType _buffFlags;
        /// <summary>
        /// 受害者ID
        /// </summary>
        private readonly int _ownerId;
        private float _currentTime;
        /// <summary>★ 主键索引：BuffId → Buff（精确识别、叠加/刷新的唯一依据）</summary>
        private readonly Dictionary<int, Buff> _buffById;
        /// <summary>标签索引：单个标签位 → 带该位的Buff列表（组合位已按位分解）</summary>
        private readonly Dictionary<BuffType, List<Buff>> _buffsByType;
        
        private readonly ClassObjectPool<Buff> _buffPool;

        /// <summary>行为标签计数：标签 → 当前带该标签的存活Buff数量（O(1) 查询，随存活Buff重算）</summary>
        private readonly Dictionary<string, int> _tagCounts = new Dictionary<string, int>();

        /// <summary>
        /// 延后移除队列：迭代/清算期间收到的移除请求先入队，由最外层持有者统一清算
        /// 防止事件回调重入导致遍历跳帧、重复移除、重复回收；非迭代期恒为空
        /// </summary>
        private readonly List<Buff> _pendingRemove = new List<Buff>();

        /// <summary>
        /// 是否处于遍历中（Update主循环期间为true）
        /// </summary>
        private bool _iterating;

        /// <summary>
        /// 是否处于清算移除中（DrainPendingRemoves期间为true）
        /// </summary>
        private bool _draining;

        /// <summary>
        /// 获取当前激活的Buff标志位
        /// </summary>
        public BuffType ActiveBuffFlags => _buffFlags;

        /// <summary>
        /// 获取激活的Buff数量
        /// </summary>
        public int ActiveBuffCount => _activeBuffs.Count;

        /// <summary>
        /// 获取所有激活的Buff（只读）
        /// </summary>
        public IReadOnlyList<Buff> ActiveBuffs => _activeBuffs.AsReadOnly();

        /// <summary>
        /// Buff添加事件
        /// </summary>
        public event Action<Buff> OnBuffAdded;

        /// <summary>
        /// Buff移除事件
        /// </summary>
        public event Action<Buff> OnBuffRemoved;

        /// <summary>
        /// Buff更新事件（叠加、刷新等）
        /// </summary>
        public event Action<Buff> OnBuffUpdated;

        /// <summary>
        /// Buff触发事件（持续伤害等）
        /// </summary>
        public event Action<Buff> OnBuffTick;

        /// <summary>
        /// buff 系统的构造函数
        /// </summary>
        /// <param name="ownerId"></param>
        /// <param name="buffPool"></param>
        public BuffSystemPooled(int ownerId, ClassObjectPool<Buff> buffPool = null)
        {
            _ownerId = ownerId;
            _activeBuffs = new List<Buff>();
            _buffById = new Dictionary<int, Buff>();
            _buffsByType = new Dictionary<BuffType, List<Buff>>();
            _buffFlags = BuffType.None;
            _currentTime = 0f;
            //未传池时兜底内部池，避免AddBuff里空引用
            _buffPool = buffPool ?? new ClassObjectPool<Buff>(32);
        }

        #region Buff标志位快速检查

        /// <summary>
        /// ★ 是否存在带指定**行为标签**的Buff（数据驱动判定的首选入口）
        /// </summary>
        /// <param name="tag">见 BuffTags 常量（Control / HardControl / Silence / MovementImpair / DoT）</param>
        public bool HasBuffWithTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }
            int count;
            return _tagCounts.TryGetValue(tag, out count) && count > 0;
        }

        /// <summary>
        /// 检查是否有指定Buff（位运算，O(1)复杂度）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasBuff(BuffType buffType)
        {
            return (_buffFlags & buffType) == buffType;
        }

        /// <summary>
        /// 检查是否有任一Buff
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasAnyBuff(BuffType buffTypes)    //我现在有  睡眠  石化   石化
        {
            return (_buffFlags & buffTypes) != 0;
        }

        /// <summary>
        /// 检查是否有所有指定Buff
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasAllBuffs(BuffType buffTypes)
        {
            return (_buffFlags & buffTypes) == buffTypes;
        }

        /// <summary>
        /// 检查是否有增益效果
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasPositiveBuff()
        {
            return HasAnyBuff(BuffType.AllPositive);
        }

        /// <summary>
        /// 检查是否有减益效果
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasNegativeBuff()
        {
            return HasAnyBuff(BuffType.AllNegative);
        }

        /// <summary>
        /// 检查是否被控制
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsControlled()
        {
            return HasAnyBuff(BuffType.AllControlEffects);
        }

        /// <summary>
        /// 检查是否可以移动
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanMove()
        {
            //★ 标签优先（数据驱动），未声明 Tags 的行退回位判定
            return !HasBuffWithTag(BuffTags.MovementImpair) && !HasAnyBuff(BuffType.AllMovementImpair);
        }

        /// <summary>
        /// 检查是否可以攻击
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanAttack()
        {
            return !HasAnyBuff(BuffType.Disarmed | BuffType.Stunned | BuffType.Frozen | BuffType.Sleeping);
        }

        /// <summary>
        /// 检查是否可以施法
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanCast()
        {
            return !HasAnyBuff(BuffType.Silenced | BuffType.Stunned | BuffType.Frozen | BuffType.Sleeping);
        }

        /// <summary>
        /// 检查是否无敌
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInvincible()
        {
            return HasBuff(BuffType.Invincible);
        }

        #endregion

        #region Buff管理 - 零GC版本

        /// <summary>
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

            // 检查免疫（★ 标签优先：目标带 Control 标签即算控制类）
            if (IsImmuneTo(buffData))
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

            //★ 事件回调里（SyncControlStateFromBuffs）会查 HasBuffWithTag，故必须在 OnBuffAdded 之前刷新
            RecomputeTagCounts();

            OnBuffAdded?.Invoke(buff);
            return buff;
        }


        /// <summary>
        /// 移除Buff（按类型，仅支持单一BuffType）
        /// </summary>
        public bool RemoveBuff(BuffType buffType)
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
                {
                    _pendingRemove.Add(_activeBuffs[i]);
                    removeCount++;
                }
            }

            DrainPendingRemoves();
            return removeCount > 0;
        }

        /// <summary>
        /// 移除Buff（按ID）
        /// </summary>
        public bool RemoveBuffById(int buffId)
        {
            if (_buffById.TryGetValue(buffId, out var buff))
            {
                RemoveBuffInternal(buff);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 驱散Buff（移除可驱散的减益）
        /// </summary>
        public int DispelNegativeBuffs(int count = 1)
        {
            int dispelled = 0;
            _pendingRemove.Clear();

            //正向遍历原地收集（与原Take(count)顺序语义一致），循环结束后统一清算
            for (int i = 0; i < _activeBuffs.Count && dispelled < count; i++)
            {
                var b = _activeBuffs[i];
                if (b._BuffData.Dispellable && b._BuffData.Category == BuffCategory.Negative)
                {
                    _pendingRemove.Add(b);
                    dispelled++;
                }
            }

            DrainPendingRemoves();
            return dispelled;
        }

        /// <summary>
        /// 驱散所有Buff
        /// </summary>
        public int DispelAllBuffs()
        {
            int dispelled = 0;
            _pendingRemove.Clear();

            for (int i = 0; i < _activeBuffs.Count; i++)
            {
                var b = _activeBuffs[i];
                if (b._BuffData.Dispellable)
                {
                    _pendingRemove.Add(b);
                    dispelled++;
                }
            }

            DrainPendingRemoves();
            return dispelled;
        }

        /// <summary>
        /// 清除所有Buff（强制）
        /// </summary>
        public void ClearAllBuffs()
        {
            _pendingRemove.Clear();

            for (int i = 0; i < _activeBuffs.Count; i++)
            {
                _pendingRemove.Add(_activeBuffs[i]);
            }

            DrainPendingRemoves();
        }

        /// <summary>
        /// 入口：执行移除
        /// 迭代/清算期间改为入队延后处理，防止事件重入导致遍历集合被中途修改
        /// </summary>
        private void RemoveBuffInternal(Buff buff)
        {
            //已失效的Buff直接忽略：防事件重入导致重复移除、重复回收（双重归还池会让Spawn返回同一对象）
            if (buff == null || !buff.IsActive)
                return;

            if (_iterating || _draining)
            {
                if (!_pendingRemove.Contains(buff))
                    _pendingRemove.Add(buff);
                return;
            }

            RemoveBuffImmediate(buff);
        }

        /// <summary>
        /// 实际执行移除（仅在非迭代/非清算状态下调用）
        /// </summary>
        private void RemoveBuffImmediate(Buff buff)
        {
            buff.IsActive = false;
            _activeBuffs.Remove(buff);
            _buffById.Remove(buff._BuffData.Id);
            IndexByTypeBits(buff, false);

            //★ 标签位可被多个Buff共享 ⇒ 不能用 &= ~Type 清位（会把仍生效的同位Buff一起清掉）
            //  改为按当前存活Buff全量重算：Buff数量是十量级，移除是低频操作，开销可忽略
            RecomputeFlags();
            //★ 同上：行为标签计数也按存活Buff重算（放在 OnBuffRemoved 之前，回调里要查 HasBuffWithTag）
            RecomputeTagCounts();

            //事件在Recycle之前发出：回调内可以正常读取Buff数据
            //注意：回调返回后该引用即失效（_BuffData被置null），跨帧保存请复制所需字段
            OnBuffRemoved?.Invoke(buff);

            buff.Recycle();
            // 归还到池中
            _buffPool.Recycle(buff);
        }

        /// <summary>
        /// 统一清算延后移除队列
        /// </summary>
        private void DrainPendingRemoves()
        {
            //迭代中或清算中：本次不处理，由最外层持有者的循环继续清算（防止遍历中途集合被改）
            if (_iterating || _draining)
                return;

            _draining = true;
            try
            {
                //从队尾逐个清算，期间新入队的项会被继续处理
                while (_pendingRemove.Count > 0)
                {
                    var buff = _pendingRemove[_pendingRemove.Count - 1];
                    _pendingRemove.RemoveAt(_pendingRemove.Count - 1);
                    RemoveBuffImmediate(buff);
                }
            }
            finally
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
        /// 按当前存活Buff重算「行为标签计数」
        /// 与 RecomputeFlags 同理：标签可被多个Buff共享，不能做增量减一，全量重算才不会错
        /// </summary>
        private void RecomputeTagCounts()
        {
            _tagCounts.Clear();
            for (int i = 0; i < _activeBuffs.Count; i++)
            {
                string[] tags = _activeBuffs[i]._BuffData.Tags;
                if (tags == null)
                {
                    continue;
                }
                for (int j = 0; j < tags.Length; j++)
                {
                    int count;
                    _tagCounts.TryGetValue(tags[j], out count);
                    _tagCounts[tags[j]] = count + 1;
                }
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

        #endregion

        #region 更新和Tick

        /// <summary>
        /// 更新Buff系统（每帧调用）
        /// </summary>
        public void Update(float deltaTime)
        {
            //时钟常走：跟随真实流逝时间，CreatedTime等时间戳语义才正确
            _currentTime += deltaTime;

            if (_activeBuffs.Count <= 0)
            {
                //无激活Buff时归零：此时没有任何Buff引用旧时钟，安全
                //同时消解长时间对局下float累加的精度漂移问题
                _currentTime = 0f;
                return;
            }

            _iterating = true;
            try
            {
                // 反向遍历；过期Buff只入队，循环结束后统一清算（Tick事件回调里的移除请求同样入队）
                for (int i = _activeBuffs.Count - 1; i >= 0; i--)
                {
                    var buff = _activeBuffs[i];

                    // 更新持续时间
                    if (buff._BuffData.Duration > 0)
                    {
                        buff.RemainingTime -= deltaTime;

                        // 检查过期
                        if (buff.IsExpired())
                        {
                            _pendingRemove.Add(buff);
                            continue;
                        }
                    }

                    // 检查Tick效果（回调期间新增的Buff本帧不处理，下帧生效）
                    if (buff.ShouldTick(_currentTime))
                    {
                        buff.NextTickTime = _currentTime + buff._BuffData.TickInterval;
                        OnBuffTick?.Invoke(buff);
                    }
                }
            }
            finally
            {
                _iterating = false;
            }

            DrainPendingRemoves();
        }

        #endregion

        #region 查询

        /// <summary>
        /// 获取指定类型的所有Buff
        /// </summary>
        public List<Buff> GetBuffs(BuffType buffType)
        {
            if (_buffsByType.TryGetValue(buffType, out var buffs))
                return new List<Buff>(buffs);
            return new List<Buff>();
        }

        /// <summary>
        /// 获取指定类别的所有Buff（填充式查询：调用方传入复用List，热路径零GC）
        /// </summary>
        public void GetBuffsByCategory(BuffCategory category, List<Buff> result)
        {
            if (result == null)
                return;
            result.Clear();
            for (int i = 0; i < _activeBuffs.Count; i++)
            {
                if (_activeBuffs[i]._BuffData.Category == category)
                    result.Add(_activeBuffs[i]);
            }
        }

        /// <summary>
        /// 获取指定类别的所有Buff
        /// 注意：每次调用都会产生List分配，热路径请使用填充式重载
        /// </summary>
        public List<Buff> GetBuffsByCategory(BuffCategory category)
        {
            var result = new List<Buff>();
            GetBuffsByCategory(category, result);
            return result;
        }

        /// <summary>
        /// 获取Buff（按ID）
        /// </summary>
        public Buff GetBuffById(int buffId)
        {
            _buffById.TryGetValue(buffId, out var buff);
            return buff;
        }

        /// <summary>
        /// 获取Buff叠加层数
        /// </summary>
        public int GetBuffStackCount(BuffType buffType)
        {
            if (_buffsByType.TryGetValue(buffType, out var buffs) && buffs.Count > 0)
                return buffs[0].StackCount;
            return 0;
        }

        /// <summary>
        /// 获取Buff剩余时间
        /// </summary>
        public float GetBuffRemainingTime(BuffType buffType)
        {
            if (_buffsByType.TryGetValue(buffType, out var buffs) && buffs.Count > 0)
                return buffs[0].RemainingTime;
            return 0f;
        }

        /// <summary>
        /// 计算属性加成（值）
        /// </summary>
        public float CalculateStatBonus(BuffType buffType)
        {
            float bonus = 0f;
            if (_buffsByType.TryGetValue(buffType, out var buffs))
            {
                foreach (var buff in buffs)
                {
                    bonus += buff._BuffData.EffectValue * buff.StackCount;
                }
            }
            return bonus;
        }

        /// <summary>
        /// 计算属性加成（百分比）
        /// </summary>
        public float CalculateStatMultiplier(BuffType buffType)
        {
            float multiplier = 1f;
            if (_buffsByType.TryGetValue(buffType, out var buffs))
            {
                foreach (var buff in buffs)
                {
                    multiplier *= (1f + buff._BuffData.EffectPercent * buff.StackCount);
                }
            }
            return multiplier;
        }

        #endregion

        #region 免疫检查

        /// <summary>
        /// 检查是否免疫指定Buff
        /// </summary>
        private bool IsImmuneTo(BuffData data)   //中毒
        {
            // 免疫控制：自身带免疫（标签或位 —— 将来 CSV 补 ControlImmune 行后即可走标签）
            if (HasBuffWithTag(BuffTags.ImmuneControl) || HasBuff(BuffType.ControlImmune))
            {
                if (IsControlTagged(data))
                    return true;
            }

            // 无敌免疫所有减益
            if (HasBuff(BuffType.Invincible))
            {
                if (data.Category == BuffCategory.Negative || (data.Type & BuffType.AllNegative) != 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 该Buff是否属于「控制类」（免疫控制的判定口径）
        /// ★ Tags 优先（数据驱动：CSV 写 Control 即可，不必抢枚举位）；
        ///   该行没声明 Tags 时退回 BuffType 位判定 —— 加载期已对这类行报 Error 指名
        /// </summary>
        private static bool IsControlTagged(BuffData data)
        {
            if (data.Tags != null && data.Tags.Length > 0)
            {
                return data.HasTag(BuffTags.Control)
                    || data.HasTag(BuffTags.HardControl)
                    || data.HasTag(BuffTags.Silence);
            }
            return (data.Type & BuffType.AllControlEffects) != 0;
        }

        #endregion

        #region 调试

        /// <summary>
        /// 获取调试信息
        /// </summary>
        public string GetDebugInfo()
        {
            var info = $"BuffSystemPooled [Owner: {_ownerId}]\n";
            info += $"Active Buffs: {ActiveBuffCount}\n";
            info += $"Flags: {_buffFlags}\n";
         
            info += "--- Buff List ---\n";

           

            return info;
        }

        #endregion
    }
}
