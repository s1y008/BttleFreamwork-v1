using System;
using System.Collections;
using System.Collections.Generic;
using BattleFramework.Core;
using UnityEngine;

public class BuffMsg
{
    private Dictionary<int,BuffData> _buffs = new Dictionary<int, BuffData>();
    

    private Dictionary<BuffType ,BuffData>  _buffDic = new Dictionary<BuffType,BuffData>();

    /// <summary>
    /// 行为标签 → 代表配置行（同标签多行时取 Id 最小者，保证「CSV 行序一动行为就变」不会发生）
    /// ★ 存在的意义：UnitHitReaction.ApplyStun / ApplySilence 这类**语义级施加**入口，
    ///   以前硬编码 BuffType.Stunned / Silenced —— 那会让 Tags 数据驱动对这条路径整体失效，
    ///   而且同标签的第二个 Buff 永远摸不到（GetData(BuffType) 只返回首个登记者）。
    ///   改由 Tags 反查后，「CSV 写什么就用什么」才真正闭环。
    /// </summary>
    private readonly Dictionary<string, BuffData> _tagRepresentative = new Dictionary<string, BuffData>();

    public BuffMsg()
    {
        // ★ 2026-09-28 CSV→JSON：配置源切换为 BufferData.json（键名与 BuffData 公开成员一一对应；
        //   Type/Category 沿用 CSV 的枚举名字符串风格，Newtonsoft 字符串→枚举直接支持）。
        //   旧 Split('\r') 的 CRLF 脆弱性、裸 float.Parse 的崩表风险随 CSV 解析器一并移除；
        //   单行脏数据仍走「报错跳行」语义（JsonConfigLoader 的 Error 处理器）。
        List<BuffData> rows = JsonConfigLoader.LoadRows<BuffData>("BufferData");
        if (rows == null)
        {
            Debug.LogError("[BuffData] BufferData.json 加载失败 —— Buff 配置表为空，AddBuff 将全部取不到配置行");
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            BuffData buff = rows[i];
            if (buff == null)
            {
                continue;
            }

            //Duration只允许-1(永久)或>0(秒)：脏数据拒绝加载，防止出现"永不过期的0秒Buff"
            if (buff.Duration != -1 && buff.Duration <= 0)
            {
                Debug.LogError("[BuffData] ID=" + buff.Id + " Duration=" + buff.Duration + " 非法（应为-1或>0），跳过该行");
                continue;
            }

            //★ Id 是唯一主键（Buff的精确识别只认Id，不再认枚举位）
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

            _buffs.Add(buff.Id, buff);
        }

        PickTagRepresentatives();
    }
    
    
    
    /// <summary>
    /// 根据枚举获取对应的数据
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public BuffData GetData(BuffType type)
    {
        //索引器直取改为TryGetValue：未配置返回null由调用方判空，不再抛KeyNotFoundException
        BuffData data;
        if (!_buffDic.TryGetValue(type, out data))
        {
            Debug.LogError("[BuffData] 未配置的BuffType: " + type);
            return null;
        }
        return data;
    }
    
    /// <summary>
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
    /// 按行为标签取「代表配置行」—— 语义级施加入口（ApplyStun / ApplySilence）的唯一数据来源。
    /// ★ 取不到返回 null，由调用方 LogError 指名，**不做静默降级**：
    ///   没有配置行声明该标签 = 资产漏配，就该当场报错，而不是「打了没反应」。
    /// </summary>
    /// <param name="tag">行为标签，见 BuffTags 常量</param>
    public BuffData GetDataByTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return null;
        }
        BuffData data;
        return _tagRepresentative.TryGetValue(tag, out data) ? data : null;
    }

    /// <summary>
    /// 扫全表为每个行为标签选出代表行（Id 最小者），同一标签多行时 LogWarning 指名。
    /// 只在构造期跑一次 ⇒ 运行时 GetDataByTag 是 O(1) 字典查询。
    /// ⚠ 按 Id 升序遍历：保证「谁当代表」是确定的，与 CSV 行序无关。
    /// </summary>
    private void PickTagRepresentatives()
    {
        List<int> ids = new List<int>(_buffs.Keys);
        ids.Sort();

        for (int k = 0; k < ids.Count; k++)
        {
            BuffData buff = _buffs[ids[k]];
            if (buff.Tags == null)
            {
                continue;
            }
            for (int i = 0; i < buff.Tags.Length; i++)
            {
                string tag = buff.Tags[i];
                if (string.IsNullOrEmpty(tag))
                {
                    continue;
                }
                BuffData existing;
                if (!_tagRepresentative.TryGetValue(tag, out existing))
                {
                    _tagRepresentative.Add(tag, buff);
                    continue;
                }
                // 已有同类代表 ⇒ 语义级入口区分不了二者，报警让策划显式传 buffId
                Debug.LogWarning("[BuffData] 行为标签 '" + tag + "' 有多个配置行（ID="
                    + existing.Id + " / ID=" + buff.Id + "）⇒ 语义级施加入口（ApplyStun/ApplySilence）"
                    + "固定取 Id 最小的 ID=" + existing.Id + "，ID=" + buff.Id + " 摸不到。"
                    + "若二者需要区分，请调用方显式传 buffId");
            }
        }
    }

    /// <summary>
    /// 全部 Buff 配置行（按字典载入序）—— 供 Editor 探针 / 迁移验证工具全表遍历。
    /// ⚠ 首次访问 GetData 系列方法会触发 BufferData.json 懒加载。
    /// </summary>
    public List<BuffData> GetAll()
    {
        return new List<BuffData>(_buffs.Values);
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
        bool inHard     = (buff.Type & BuffType.HardControl) != 0;

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

        //★ 两套控制口径的漂移防护：HardControl 必须是 AllControlEffects 的子集。
        //  AllControlEffects = 免疫口径（ControlImmune 免疫哪些）；HardControl = FSM投影口径（哪些会切 Stun 态）。
        //  若某个硬控位不在免疫口径内 ⇒ 它无法被「免疫控制」免疫，属于静默失效，必须报错。
        if ((BuffType.HardControl & ~BuffType.AllControlEffects) != 0)
        {
            Debug.LogError("[BuffData] BuffType.HardControl 含有不在 AllControlEffects 内的位 → "
                + "这些硬控无法被 ControlImmune 免疫，请把缺失的位补进 BuffType.AllControlEffects");
        }

        //★ Tags（行为标签）契约：控制类必须声明行为标签（Control / HardControl / Silence 至少其一）。
        //  没声明 ⇒ 只能退回 BuffType 位判定（过渡期兜底），报 Error 指名 —— 等将来删掉位兜底，
        //  这些行会立刻失效，所以现在就必须点名
        if (buff.Category == BuffCategory.Control
            && !buff.HasTag(BuffTags.Control)
            && !buff.HasTag(BuffTags.HardControl)
            && !buff.HasTag(BuffTags.Silence))
        {
            Debug.LogError("[BuffData] ID=" + buff.Id + " 类别=Control，但 Tags 列没有声明任何行为标签"
                + "（Control / HardControl / Silence）→ 目前只能退回 BuffType 位判定，请在 Tags 列补上，"
                + "例如：Control;HardControl;MovementImpair");
        }

        //★ 软控提示：是控制类、但既不是硬控也不是沉默 ⇒ 免疫/驱散生效，却不会驱动 FSM 切任何控制态。
        //  这一条专门防「新增了一个控制Buff，以为它会定身，结果只有数值没有状态」。
        if (buff.Category == BuffCategory.Control && !inHard && (buff.Type & BuffType.Silenced) == 0)
        {
            Debug.LogWarning("[BuffData] ID=" + buff.Id + " (" + buff.Type + ") 是控制类，但既不在 BuffType.HardControl 内、"
                + "也不含 Silenced 位 ⇒ 免疫/驱散对它有效，但 UnitBuffComponent 不会为它切 Stun/Silence 状态。"
                + "如果它应当定身，请把该类型登记进 BuffType.HardControl");
        }
    }
}
public class BuffData 
{
    /// <summary>Buff类型</summary>
    public BuffType Type { get; set; }

    /// <summary>Buff ID（用于精确识别）</summary>
    public int Id { get; set; }

    /// <summary>Buff名称</summary>
    public string Name { get; set; }

    /// <summary>Buff描述</summary>
    public string Description { get; set; }

    /// <summary>Buff类别</summary>
    public BuffCategory Category { get; set; }

    /// <summary>持续时间（秒），-1表示永久</summary>
    public float Duration { get; set; }

 /// <summary>最大叠加层数</summary>
    public int MaxStack { get; set; }

    /// <summary>效果值（如攻击力+50）</summary>
    public float EffectValue { get; set; }

    /// <summary>效果百分比（如攻击力+20%）</summary>
    public float EffectPercent { get; set; }

    /// <summary>间隔触发时间（持续伤害等）</summary>
    public float TickInterval { get; set; }

    /// <summary>是否可驱散</summary>
    public bool Dispellable { get; set; }

    /// <summary>是否可叠加</summary>
    public bool Stackable { get; set; }

    /// <summary>图标路径</summary>
    public string IconName { get; set; }

    /// <summary>特效路径</summary>
    public string EffectName { get; set; }

    /// <summary>
    /// 行为标签（来自 CSV 的 Tags 列，';' 分隔）—— ★ 行为归类的真相源
    /// 恒非 null（未配置时是空数组）；非空时一切行为判定（硬控/沉默/移动限制/免疫/DoT）以它为准
    /// </summary>
    public string[] Tags { get; set; } = new string[0];

    /// <summary>是否带有指定行为标签（大小写不敏感）</summary>
    public bool HasTag(string tag)
    {
        if (string.IsNullOrEmpty(tag) || Tags == null)
        {
            return false;
        }
        for (int i = 0; i < Tags.Length; i++)
        {
            if (string.Equals(Tags[i], tag, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

}
