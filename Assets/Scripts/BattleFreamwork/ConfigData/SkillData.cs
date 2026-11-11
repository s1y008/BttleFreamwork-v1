using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;

 

public class SkillData {

    public int ID { get; set; }
    public string Name { get; set; }
    public string Icon { get; set; }
   
    public SkillTypes SkillType;
    /// <summary>
    /// 投技伤害优先等级
    ///0：敌人普通攻击   1：玩家普通攻击  2：敌人技能攻击  3：玩家攻击技能  4：敌人的霸体技能  5：玩家的投技技能或怒气技能 
    /// </summary>
    public int SkillThrowType { get; set; }
    public float CDTime { get; set; }
    public float AttackDist { get; set; }
    public SkillHitTypes HitType { get; set; }
    public SkillHitSharpTypes SkillHitSharpType { get; set; }
    public int HitNum { get; set; }
    public float AttackRadius { get; set; }
    public float AttackAngle { get; set; }
   public float Warning { get; set; }
    
    //public SkillEffectTypes Type { get; set; }

    /// <summary>
    /// ★ 命中效果表（CSV 列 13 "SkillEffectTypes"）—— **全技能唯一的受击表现真相源**。
    ///
    /// 【两级分隔】因为 CSV 的字段分隔符是英文逗号，段内、段间只能另找分隔符：
    ///   · 段与段之间用 <b>|</b>（竖线）
    ///   · 同一段内多个效果用 <b>;</b>（分号，与既有列保持一致）
    ///   例：<code>0|0;5|6</code> → 第1段掉血 / 第2段掉血+浮空 / 第3段只后退
    ///
    /// 【★ 没有 | 时的语义（2026-09-15 合并后新规）】整串视为「<b>第 1 段</b>」的全部表现，
    ///   <b>第 2 段起没有任何效果</b>。例："0;22" → 第 1 段掉血+受击表现，第 2 段起什么都不做。
    ///   想让每段表现一致，就重复写："0;22|0;22|0;22"。
    ///
    /// 【为什么取消「所有段共用」的回落】2026-09-15 之前是「SegEffectTypes 为空 → 回落 SkillEffectTypes，
    ///   所有段共用一套表现」。它有两个问题：
    ///   ① 策划只配了一份表现，却会被悄悄摊到所有段 —— 段数一改，手感跟着变，且无从察觉；
    ///   ② 「只让首段带硬直、后续段纯掉血」这种最常见的连招设计根本配不出来。
    ///   现在规则一句话：<b>效果归段，没写就是没有</b>。原 SegEffectTypes 列已删除，合并进本列。
    ///
    /// 【⚠ 0 是有效值】0 = ImmedDamage 立即伤害，是最常用的效果；
    ///   因此解析时**绝不能**把 0 当「无效」过滤掉，否则技能会莫名其妙不掉血。
    /// 【想让某段什么都不做】该段留空（如 <code>0||6</code> 的第 2 段）或填 -1
    ///   —— ExecuteEffect 的 switch 有 default 分支，未知枚举是安全的空操作。
    /// </summary>
    public List<int>[] EffectTypesBySegment;

    /// <summary>
    /// ★ 取第 segIdx 段（<b>1-based</b>）应当执行的效果类型列表 —— 命中效果的唯一入口。
    /// 【三条规则】
    ///   ① segIdx &lt;= 1 → 取第 1 组。段号 0（非段号入口，如 Buff Tick 调过来）也走这里，
    ///      与旧版「取不到逐段配置就用共用列表」的行为一致
    ///   ② segIdx 超出已配组数 → 返回 <b>null</b>，该段什么都不做。
    ///      刻意**不复用第 1 组** —— 静默复用会让「后面几段看起来和第一段一模一样」，
    ///      这种 bug 在画面上极难描述，宁可让策划看到"没效果"立刻来补配置
    ///   ③ 该组本身是空的（<code>0||6</code> 的中间段）→ 同样返回 null
    ///
    /// 【返回 null 的含义】调用方（Skill.HitTarget）据此直接 return —— 没有效果列表就不结算任何东西。
    /// </summary>
    public List<int> GetEffectTypesForSegment(int segIdx)
    {
        if (EffectTypesBySegment == null || EffectTypesBySegment.Length == 0) return null;

        // 段号 0（非段号入口）与第 1 段同义：段推进还没开始或不适用，取第一组的效果
        int idx = segIdx <= 1 ? 0 : segIdx - 1;
        if (idx >= EffectTypesBySegment.Length) return null;

        // 元素可能是 null（该段留空）→ 调用方视为「什么都不做」
        return EffectTypesBySegment[idx];
    }

    /// <summary>
    /// ★★ 受击特效 id 列表 —— **全工程唯一的受击特效真相源**
    /// （CSV 列 <b>14</b>，0-based；即表格里 1-based 的<b>第 15 列</b> "HitEffect"）。
    /// 分号分隔多 int，按段号顺序与伤害段对应：第 1 段命中播第 1 个，第 2 段播第 2 个……。
    ///
    /// 【★ 2026-09-28 改动】此前受击特效走列 24（表格第 25 列 HitEffectIds）。
    ///   该列已于 2026-09-28 整列删除（CSV 总列数 27 → 26，其后各列下标整体前移一位），受击特效统一收敛到本列。
    ///   （⚠ 定位列号一律以**当前 CSV 表头**为准：本列现在是 0-based 14 / 1-based 第 15 列。）
    ///
    /// 【取值规则】由 <c>Skill.GetHitEffectIdForSegment(int segIdx)</c> 实现，此处不重复判定。
    /// 【铁定约束】特效个数 &lt;= 生效攻击段数 —— 多配的那些永远没有段可以播，属配置错误，
    ///   加载期由 <c>ValidateHitEffectCount</c> 一次性 LogError 指名。
    /// 【取值必须走 <c>GetHitEffectIdList()</c>】空列会被 <c>CommonHelper.Str2IntList("")</c>
    ///   解析成**含一个 0 的 List**，直接读 Count 会把「没配」误判成「配了 1 个 id=0 的特效」。
    /// </summary>
    public List<int> HitEffect = new List<int>();
    public int Attack { get; set; }
    /// <summary>
    /// 暴击率0-1
    /// </summary>
    public float Crit { get; set; }
    public List<int> BuffID { get; set; }
    /// <summary>
    /// 后退距离
    /// </summary>
    public float BackDis { get; set; }
    /// <summary>
    /// 后退时间
    /// </summary>
    public float BackTime { get; set; }
    /// <summary>
    /// 击飞最大高度
    /// </summary>
    public float AriHeight { get; set; }
    /// <summary>
    /// 飞行时间
    /// </summary>
    public float AriTime { get; set; }

    /// <summary>
    /// ★ 出手特效 id 列表(挂在施法者武器挂点上)—— **全工程唯一的出手特效来源**(CSV 列 22 "AttackEffectID")。
    /// 分号分隔多 int,例 "10001;10002;10003"。
    ///
    /// 【★ 2026-09-15 去重】此前还有一个语义完全相同的 CastEffectIds 列(**2026-09-15 当时的**列 24),已整列删除;
    ///   读取一律走 GetCastEffectIdList(),它同时处理「空串被解析成 [0]」这个坑,不要直接读本字段的 Count。
    /// 【播出时机】由 CastEffectTimings 按下标一一配对提供;没配时机则一件都不播(见该字段注释)。
    /// 【数量可大于段数】特效已与伤害段解耦:3 段伤害播 4 个特效是允许的,只要时机表同样有 4 条。
    /// </summary>
    public List<int> AttackEffects { get; set; }

    // ============== ★ v4 段号驱动新增字段 ==============

    /// <summary>
    /// ★ 段时机表(归一化 0~1),按段数填入对应时机点
    /// 例:{0.3, 0.6, 0.9} 表示 30% / 60% / 90% 处各触发一段伤害
    /// 由 AttackState.OnUpdate 每帧比对 info.normalizedTime >= SegTimings[i] 推进段号
    ///
    /// ⚠ 这一列**只负责"在什么时间点结算",不负责"一共几段"** —— 段数由 SegCount 决定。
    ///   （2026-09-15 修正：原先段数是拿 SegTimings.Length 顶替的，见 SegCount 注释）
    /// </summary>
    public float[] SegTimings;

    /// <summary>
    /// ★ v5 P6 新增：**本技能的伤害总段数**（int，CSV 列 26，1-based 语义：1 = 单段，3 = 三段）。
    ///
    /// 【为什么要单独开一列，而不能用 SegTimings.Length 顶替】
    /// 旧实现是 `_segCount = SegTimings.Length`，把「时机点个数」当成了「伤害段数」。这两个概念本来就不等价：
    ///   ① 想让第 3 段打出两倍伤害：需要 3 段伤害，但时机表可能要多填一个点做判定，两者天然可能不等；
    ///   ② 时机表是 float 列表，容易因多敲一个分号、末尾残留分隔符而多出一个元素，
    ///      于是"段数"被一个**手滑**悄悄改掉，且不报任何错；
    ///   ③ 段数是**玩法数值**（策划按手感定：这一刀是 1 段还是 3 段），
    ///      时机表是**表现参数**（美术按动画节奏定）——两类数据混在一列，改任何一方都会误伤另一方；
    ///   ④ 现在有了独立的特效时间轴（CastEffectTimings），一个技能里"段数"和"各条时间轴的条目数"
    ///      本来就可以互不相等，更不该让其中一列去充当另一列的长度来源。
    ///
    /// 【取值与语义】
    ///   0  = 这个技能不走分段伤害（等价于旧版"没配 SegTimings"），AttackState 不会触发任何一段；
    ///   n  = 一次施法结算 n 段伤害，段号 1 ~ n 依次传给 TriggerSegmentHit。
    ///
    /// 【与 SegTimings 的关系 —— 实际生效段数取两者的较小值】
    ///   SegCount > SegTimings.Length → 后面的段没有时机点可用，只能按时机点数量结算（加载时告警）
    ///   SegCount < SegTimings.Length → 多出来的时机点被忽略（加载时告警）
    ///   真实生效值由 GetSegmentCount() 统一给出，调用方一律不要自己算。
    /// </summary>
    public int SegCount { get; set; }

    // ============== ★ 2026-10-10 弹道投递新增字段（投掷物方案 P1）==============

    /// <summary>
    /// ★ 命中投递方式 —— 与效果列表（EffectTypesBySegment）正交的维度。
    ///   Instant(0，缺省) = 段时机点一到立即结算：全部既有技能的行为，逐行不变；
    ///   Projectile(1)    = 段时机点生成投掷物，结算延迟到弹着时刻、原点改为弹着点。
    /// 【为什么不是第 24 个效果号】效果号回答「命中之后发生什么」，投递方式回答「命中发生在哪、什么时候」；
    ///   做成效果号会把「发射 / 结算」拆成两次效果执行，效果归段的 | ; 配置语义随之破坏。
    /// ⚠ JSON 缺键时 Newtonsoft 保持 C# 默认值 0 ⇒ 全表既有技能零改动零迁移
    ///   （JsonConfigLoader.LoadRows 直构，缺键走 CLR 默认值）。
    /// </summary>
    public SkillDeliveryType DeliveryType { get; set; }

    /// <summary>
    /// ★ 投掷物配置 id（ProjectileData.json 的 ID 列）。仅 DeliveryType=Projectile 时生效；0=未配。
    /// 加载期由 SkillDataManager.ValidateProjectileBindings 校验指向真实弹道行。
    /// </summary>
    public int ProjectileId { get; set; }

    /// <summary>
    /// ★ v5 P6：**本次施法真正会结算几段伤害** —— 全工程唯一的段数真相源。
    ///
    /// 【为什么必须收敛成一个方法】"段数"这个数字被四处使用（AttackState 游标上限、
    ///   受击特效索引边界、配置校验），若各写各的 `SegTimings.Length`，
    ///   将来规则一变就要改 N 处，且极易漏改。这里集中定义一次，其它地方只准调它。
    ///
    /// 【返回 0 的两种情况】
    ///   ① SegCount <= 0 —— 策划明确不分段（或没配这一列）
    ///   ② SegTimings 为空 —— 配了段数却没有时机点，没有时机就无法推进，只能 0（加载时会告警点名）
    ///
    /// 【为什么取 min 而不是直接返回 SegCount】_segTimings[_firedSegIdx] 是按下标直接取值的，
    ///   让段数超过时机点个数会 IndexOutOfRangeException。宁可少打一段并告警，也不能让战斗崩溃。
    /// </summary>
    public int GetSegmentCount()
    {
        if (SegCount <= 0) return 0;

        //int timingCount = (SegTimings != null) ? SegTimings.Length : 0;
        //如果数组不为空就返回该数组的长度，否则返回0
        int timingCount = SegTimings?.Length ?? 0;
        if (timingCount <= 0) return 0;

        return Mathf.Min(SegCount, timingCount);
    }

    /// <summary>
    /// ★ v5 P5 新增:出手特效**独立时间轴**(归一化 0~1),与 AttackEffects **按下标一一配对**。
    ///
    /// 【为什么要单独一列】旧规则把刀光时机写死成 SegTimings[i] - 0.05,等于规定
    /// 「特效必须依附于某个伤害段、且必须比它早 5%」。现实中的需求根本不是这个规律:
    /// 起手要有蓄力光环(早得多)、收招要有残影(晚得多)、段数 3 段却想播 5 个特效 —— 全都做不到。
    /// 抽出这一列后,特效拥有了**自己的时间点**,与伤害段彻底解耦。
    ///
    /// 【配对规则】CastEffectTimings[i] 就是 AttackEffects[i] 的播出时机,两者**必须成对出现**:
    ///   实际播放条数 = min(AttackEffects 数量, CastEffectTimings 数量),多出来的那些会被丢弃。
    ///   例:AttackEffects = 10001;10002;10003      CastEffectTimings = 0.05;0.28;0.58
    ///      → 10001 在 5% 播(起手光环)、10002 在 28% 播、10003 在 58% 播,与伤害段位置无关
    ///   例:一个特效播多次 —— AttackEffects = 10001;10001;10001    CastEffectTimings = 0.1;0.5;0.8
    ///
    /// 【必须升序】调度是按下标顺序推进的游标,写成 0.5;0.2 会导致两个特效扎堆在同一帧播出。
    ///   加载时会做升序校验并在 Editor 打警告,但**不自动排序** —— 静默改数据会让策划的意图失真。
    ///
    /// 【留空行为 —— 重要】为 null 时该技能**一件出手特效都不播**(GetCastEffectCount 返回 0)。
    ///   这是刻意设计:宁可「不播」,也不要偷偷按某个魔数推算出「看着播了、位置却不对」的结果。
    ///   没有刀光一眼可见,而时机错位要对着设计稿逐帧比对才能发现 —— 前者的排查成本低得多。
    /// </summary>
    public float[] CastEffectTimings;

    /// <summary>
    /// ★ v5 P5 配置陷阱工具：取出手特效 id 列表，**「有效」的定义统一为「至少有一个 &gt;0 的元素」**。
    ///
    /// 【★ 2026-09-15 去重】原先这里有两个字段：CastEffectIds（v4 新加）与 AttackEffects（策划原列），
    ///   两者语义完全重合 —— 都是「出手特效 id 集合」，只是列名不同。重复字段的代价在这一列上很直接：
    ///     ① 策划填 AttackEffectID、程序读 CastEffectIds → 配了却没特效，且看代码查不出原因；
    ///     ② 兜底写法「CastEffectIds 无效则回落 AttackEffects」让「哪个才是真相源」变得不可判定，
    ///        调试时必须在两个字段之间反复横跳；
    ///     ③ 每次新增特效相关能力（如本次的 CastEffectTimings）都要再决定一次「配给哪一列」。
    ///   现合并为**唯一来源 AttackEffects**（CSV 列 22 "AttackEffectID"），CastEffectIds 列已删除。
    ///   迁移规则：原 AttackEffectID 有值的原样保留；为空的用 CastEffectIds 补位（避免删列丢数据）。
    ///
    /// 【为什么不能写 Count &gt; 0】CommonHelper.Str2IntList("") 对空串的处理是:
    ///   "".Split(';') → 长度 1 的数组 [""],再 Str2Int("") → 0,于是返回**含一个 0 的 List**,
    ///   而不是直觉中的空 List。后果:CSV 没配 AttackEffectID 的技能,字段实际是 [0] 而不是 [],
    ///   Count 会被误判为 1 —— 「没有特效」被读成「有一个 id=0 的特效」。本项目 232 行都是这种情况。
    ///
    /// 【约定】id &lt;= 0 一律视为未配置:既不存在 0 号特效,也让「空着」和「填 0」得到一致处理。
    /// 【返回 null】表示没配任何有效特效 id,调用方据此判定「没有出手特效」。
    /// </summary>
    // 缓存：有效出手特效列表。
    // 配置对象全程只读（LoadSkillData 之后不再改动），且多个 Skill 实例共享同一个 SkillData，
    // 所以这里可以放心缓存：AttackState 的 while 补播循环每次都要问「有没有特效」，
    // 不缓存的话每播一个特效都要把整个列表遍历一遍。
    private List<int> _validCastEffectIds;
    private bool _validCastEffectIdsResolved;

    public List<int> GetCastEffectIdList()
    {
        if (_validCastEffectIdsResolved) return _validCastEffectIds;

        if (AttackEffects != null)
        {
            for (int i = 0; i < AttackEffects.Count; i++)
            {
                if (AttackEffects[i] > 0)
                {
                    _validCastEffectIds = AttackEffects;
                    _validCastEffectIdsResolved = true;
                    return _validCastEffectIds;
                }
            }
        }

        // 没找到任何有效 id —— 连 false 结果也要缓存，否则每次都白遍历一遍
        _validCastEffectIds = null;
        _validCastEffectIdsResolved = true;
        return null;
    }

    /// <summary>
    /// ★ v5 P8：**取第 effectIndex 个（1-based）出手特效的 id** —— 「这个序号能不能播、能的话播哪个」由数据类一次回答。
    ///
    /// 【为什么放在 SkillData 而不是 Skill】职责边界的问题：
    ///   · SkillData 是**配置查询层** —— 它拥有 CSV 原始数据，也只有它知道「什么算有效配置」
    ///     （比如空列会被 Str2IntList 解析成含一个 0 的 List，这条脏数据的处理规则必须跟着数据走）；
    ///   · Skill 是**运行时行为层** —— 它该关心的是「拿到 id 之后挂到哪个挂点上、什么时候创建」。
    ///   反过来把取值的三个判断写在 Skill 里，效果等于让调用方替数据类擦屁股：
    ///   任何一个后续新增的调用点（Buff 出手、连携追打、AI 预览）都得把这三行抄一遍，
    ///   漏抄一行就是「配了特效却播不出来」或「下标越界」。
    ///
    /// 【本方法一次性吃掉三个判断】
    ///   ① 整个列表无效（没配 / 全是 0）→ 0
    ///   ② 序号越界（配了 3 个却要第 4 个）→ 0
    ///   ③ 指定位置的 id &lt;= 0（该位空着）→ 0
    ///   调用方只需要 `if (id &lt;= 0) return;`，不可能写错。
    ///
    /// 【返回 0 而不是 -1】与本项目特效 id 的既有约定一致：id &lt;= 0 一律代表「没有」，不存在 0 号特效。
    /// 【刻意不退化到播第 1 个】越界就跳过 —— 静默复用第 1 个特效会让几次出手看起来一模一样，
    ///   这种 bug 在画面上很难描述清楚，宁可少播一次。
    /// </summary>
    /// <param name="effectIndex">1-based 特效序号，取值范围 1 ~ GetCastEffectIdList().Count</param>
    /// <returns>特效 id；<b>0 表示该序号没有可用特效</b></returns>
    public int GetCastEffectId(int effectIndex)
    {
        List<int> list = GetCastEffectIdList();
        if (list == null) return 0;
        if (effectIndex <= 0 || effectIndex > list.Count) return 0;

        int id = list[effectIndex - 1];
        return id > 0 ? id : 0;
    }

    /// <summary>
    /// ★ 有效出手特效 id 的数量 —— 取 {@link GetCastEffectIdList} 的长度,没配则为 0。
    /// 单独开一个方法是为了让「什么是有效配置」只有一个真相源,
    /// 避免 ValidateEffectTimings / GetCastEffectCount / PlayCastEffect 三处各判一套。
    /// </summary>
    public int GetValidCastEffectIdCount()
    {
        List<int> list = GetCastEffectIdList();
        return list != null ? list.Count : 0;
    }

    /// <summary>
    /// ★ v5 P8：本技能一共要播几个出手特效（从 Skill 下沉，语义不变）。
    ///
    /// 【取两者较小值】一个特效要能播，必须同时满足「有特效 id」和「有播出时机」两个条件，
    ///   所以真正会播的条目数 = min(有效特效 id 数, CastEffectTimings 数量)。
    ///     配了 4 个 id 却只写 3 个时机 → 第 4 个不知道什么时候播，跳过
    ///     写了 4 个时机却只有 3 个 id → 第 4 个不知道播什么，同样跳过
    ///
    /// 【为什么取交集而不是各自兜底】任何隐式补位都会掩盖配置错误 —— 少写一个数字，
    ///   结果却是「它照样播了，只是位置不太对」，这类 bug 极难定位。宁缺毋滥。
    ///
    /// 【为什么放在 SkillData】它与 GetCastEffectId 是同一套判断的两半：
    ///   前者回答「第 n 个播什么」，本方法回答「一共能播几个」，
    ///   两边都必须遵守「空列 = 含一个 0 的 List」这条脏数据规则。
    ///   分家就会出现 Skill 判一套、SkillData 判另一套的经典错位。
    /// </summary>
    public int GetCastEffectCount()
    {
        int idCount = GetValidCastEffectIdCount();
        int timingCount = CastEffectTimings != null ? CastEffectTimings.Length : 0;

        // 没配时机表 → timingCount 为 0 → 结果必为 0 → 整个特效轨道不播
        return Mathf.Min(idCount, timingCount);
    }

    /// <summary>
    /// ★ v5 P8：取第 idx 个（<b>0-based</b>）出手特效的播出时机，归一化 0~1（从 Skill 下沉，语义不变）。
    /// </summary>
    public float GetCastEffectTiming(int idx)
    {
        if (idx < 0) return -1f;
        if (CastEffectTimings == null || idx >= CastEffectTimings.Length) return -1f;

        float timing = CastEffectTimings[idx];
        return timing >= 0f ? timing : -1f;
    }

    /// <summary>
    /// ★ 受击特效「有效 id」列表 —— 与 <c>GetCastEffectIdList()</c> 同构，规则也一致。
    ///
    /// 【为什么必须封装】<c>CommonHelper.Str2IntList("")</c> 对空串返回**含一个 0 的 List**，
    ///   直接读 <c>HitEffect.Count</c> 会把「这一列空着」误判成「配了 1 个 id=0 的特效」。
    ///   本方法把这个脏数据规则吃掉一次，调用方只看得到「有」或「没有」。
    ///
    /// 【缓存】配置对象加载后全程只读，多个 Skill 实例共享同一份 SkillData，可安全缓存。
    /// 【返回 null】整列没配或全是 0 ⇒ 该技能没有受击特效，调用方据此判定不播。
    /// </summary>
    private List<int> _validHitEffectIds;
    private bool _validHitEffectIdsResolved;

    public List<int> GetHitEffectIdList()
    {
        if (_validHitEffectIdsResolved) return _validHitEffectIds;

        if (HitEffect != null)
        {
            for (int i = 0; i < HitEffect.Count; i++)
            {
                if (HitEffect[i] > 0)
                {
                    _validHitEffectIds = HitEffect;
                    _validHitEffectIdsResolved = true;
                    return _validHitEffectIds;
                }
            }
        }

        // 连 false 结果也要缓存，否则每次都白遍历一遍
        _validHitEffectIds = null;
        _validHitEffectIdsResolved = true;
        return null;
    }

}

public class SkillDataManager
{
    private Dictionary<int, SkillData> m_SkillDataDic = null;

   
  
    /// <summary>
    /// 读取技能数据
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public SkillData GetData(int key)
    {
        if (m_SkillDataDic==null)
        {
            LoadSkillData();
        }
        return m_SkillDataDic.ContainsKey(key) ? m_SkillDataDic[key] : null;
    }
    
    public List<SkillData> GetAll()
    {
        if (m_SkillDataDic==null)
        {
            LoadSkillData();
        }
        return new List<SkillData>(m_SkillDataDic.Values);
    }

    /// <summary>
    /// ★ v5 P5 配置防呆:校验 CastEffectTimings 与 AttackEffects(CSV 列 22)的配对关系。
    ///
    /// 【为什么只在 Editor 检查】这属于配置阶段就能发现的问题,不该拖到真机运行;
    /// Release 构建整段 #if 掉,零开销。也不做自动修正(比如自动排序),
    /// 因为静默改写数据会让策划"我明明配了 A 顺序,怎么播出是 B 顺序"而查不到原因。
    ///
    /// 【检查三项】
    ///   ① 配了特效 id 却没配时机 → 一件都不播,最容易发生的疏漏,必须显式点名
    ///   ② 时机必须升序 —— 调度是顺序推进的游标,0.5;0.2 会让第 2 个在第 1 个播完后立刻播出(同帧扎堆)
    ///   ③ 数量不等 → 实际只播 min(两者) 条,多出来的被丢弃，需要告知是哪一侧配多了
    /// </summary>
    /// <summary>
    /// ★★ 【SkillDataManager 配置防呆】铁定约束校验：受击特效个数 **不得大于** 生效攻击段数（<c>GetSegmentCount()</c>）。
    ///
    /// 【为什么只在生效段数 &gt; 0 时检查】全表 233 个技能 <c>SegCount=0</c>（策划尚未补配），
    ///   它们本来就不结算任何一段，配几个特效都不会播 —— 若一并报警，233 条噪音会把
    ///   真正需要修的那几条彻底埋掉（与「告警不能被噪音淹没」是同一条原则）。
    ///
    /// 【不做自动修正】是裁掉多配的、还是给技能补段，属于策划决策；
    ///   静默截断会让「我明明配了 3 个怎么只播 2 个」无从追查。
    ///
    /// 【违规后果】多配的那些永远取不到（段号最大只到段数），等于配置浪费而非运行时错误，
    ///   所以这里只报一次，不改变取值行为。
    /// </summary>
    private void ValidateHitEffectCount(SkillData data)
    {
#if UNITY_EDITOR
        int segCount = data.GetSegmentCount();
        if (segCount <= 0) return;

        List<int> ids = data.GetHitEffectIdList();
        int n = (ids != null) ? ids.Count : 0;
        if (n <= segCount) return;

        Debug.LogError($"[SkillData] 技能 {data.ID} 违反受击特效约束：第 15 列(HitEffect) 配了 {n} 个受击特效，" +
            $"但生效攻击段数只有 {segCount} —— 第 {segCount + 1} 个及其后的特效永远播不到。" +
            $"请删掉多配的，或给该技能补上对应段数（列 SegCount / SegTimings）。");
#endif
    }

    /// <summary>
    /// ★★ 【AddBuff 绑定契约校验】2026-09-29：技能配了效果 2(AddBuff) ⇒ BuffID 必须非空，且每个 id 必须存在。
    ///
    /// 【为什么必须有这一条】<c>AddBuffEffect.Execute</c> 是「遍历 BuffID 逐个施加」，
    ///   BuffID 为空 ⇒ 循环 0 次 ⇒ 既不挂 Buff 也不报错，属**静默降级**，
    ///   直接违反项目铁律 5（资产契约优先，禁止为资产不一致写运行时静默失效）。
    ///   实测：本次改动前全表 12 个技能配了效果 2 而 BuffID 全空，打出去完全没反应且无从察觉。
    ///
    /// 【为什么放加载期而不是命中时】命中时报错会随每次攻击刷屏；加载期只报一次且指名到技能 ID。
    ///   ⚠ 与既有 Validate* 系列一致，整段只在 UNITY_EDITOR 下编译 —— Release 构建零开销。
    ///
    /// 【不做自动修正】补哪个 Buff 属于策划决策，静默挑一个默认 Buff 会让「我明明没配怎么挂上了」无从追查。
    /// </summary>
    private void ValidateBuffBindings(SkillData data)
    {
#if UNITY_EDITOR
        // ① 该技能是否配置了效果 2(AddBuff) —— 命中效果表是「效果归段」，必须逐段扫
        bool hasAddBuff = false;
        if (data.EffectTypesBySegment != null)
        {
            for (int s = 0; s < data.EffectTypesBySegment.Length && !hasAddBuff; s++)
            {
                List<int> types = data.EffectTypesBySegment[s];
                if (types == null) continue;
                for (int t = 0; t < types.Count; t++)
                {
                    if (types[t] == (int)SkillEffectTypes.AddBuff)
                    {
                        hasAddBuff = true;
                        break;
                    }
                }
            }
        }
        if (!hasAddBuff) return;

        // ② 配了 AddBuff 却没有可用 BuffID ⇒ AddBuffEffect 空转（打了没反应）
        //    ⚠ 不能只判 Count：历史解析会把空配置字段填成「含一个 0 的 List」
        //      （CommonHelper.Str2IntList("") 的坑），必须按 >0 计数
        int validIdCount = 0;
        if (data.BuffID != null)
        {
            for (int i = 0; i < data.BuffID.Count; i++)
            {
                if (data.BuffID[i] > 0) validIdCount++;
            }
        }

        if (validIdCount == 0)
        {
            Debug.LogError($"[SkillData] 技能 {data.ID}({data.Name}) 配了效果 2(AddBuff)，但 BuffID 列为空" +
                $" ⇒ 命中时 AddBuffEffect 循环 0 次，等于打了没反应。" +
                $"请在配置表 BuffID 列补上 BuffId（例如流血 = 1003）。");
            return;
        }

        // ③ BuffID 引用的 Buff 必须存在 —— 否则每次命中都取不到配置行，同样是没有表现的静默失效
        for (int i = 0; i < data.BuffID.Count; i++)
        {
            int buffId = data.BuffID[i];
            if (buffId <= 0) continue;
            if (ConfigManager.GetBuffData(buffId) == null)
            {
                Debug.LogError($"[SkillData] 技能 {data.ID}({data.Name}) 的 BuffID 引用了不存在的 BuffId={buffId}" +
                    $" ⇒ 命中时 AddBuff 取不到配置行，Buff 挂不上。请核对 BufferData 的 Id 列。");
            }
        }
#endif
    }

    private void ValidateEffectTimings(SkillData data)
    {
#if UNITY_EDITOR
        int timingCount = data.CastEffectTimings != null ? data.CastEffectTimings.Length : 0;

        // ⚠ 必须用「有效 id 数」而不是 Count：空配置字段实际是含一个 0 的 List（见 GetValidCastEffectIdCount）
        int idCount = data.GetValidCastEffectIdCount();

        // ① 真的配了特效却没配时机 —— 结果是默默不播,必须点名
        if (idCount > 0 && timingCount == 0)
        {
            Debug.LogWarning($"[SkillData] 技能 {data.ID} 配了 {idCount} 个出手特效但没有 CastEffectTimings," +
                             $"出手特效将完全不播出 —— 请在 CSV 列 \"CastEffectTimings\" 补上对应的时机点");
            return;
        }

        if (timingCount < 2) return;

        // ② 升序校验
        for (int i = 1; i < data.CastEffectTimings.Length; i++)
        {
            if (data.CastEffectTimings[i] < data.CastEffectTimings[i - 1])
            {
                Debug.LogWarning($"[SkillData] 技能 {data.ID} 的 CastEffectTimings 不是升序:" +
                                 $"第 {i + 1} 个 {data.CastEffectTimings[i]} < 前一个 {data.CastEffectTimings[i - 1]}," +
                                 $"会导致多个特效扎堆播出");
                break;   // 一条足够定位,不刷屏
            }
        }

        // ③ 数量比对 —— 实际只播 min(idCount, timingCount) 条
        if (timingCount != idCount)
        {
            string side = timingCount > idCount ? "时机多于特效 id" : "特效 id 多于时机";
            Debug.LogWarning($"[SkillData] 技能 {data.ID}:CastEffectTimings {timingCount} 个 vs 特效 id {idCount} 个" +
                             $"({side}),实际只播前 {Mathf.Min(idCount, timingCount)} 条");
        }
#endif
    }

    /// <summary>
    /// ★ v5 P6 配置防呆:校验「伤害段数 SegCount」与「时机点个数 SegTimings」是否匹配。
    ///
    /// 【为什么必须校验】这两列现在是各自独立的：段数由策划按手感填整数，时机点由美术按动画节奏填浮点。
    ///   一旦填歪，结果是"技能看着放了却没伤害"或"少打一段"，画面上极难定位 —— 必须在加载时点名。
    ///
    /// 【检查四项】
    ///   ① 配了时机却没配段数(SegCount=0) → 一段都不结算，等同于技能无伤害（最容易发生）
    ///   ② 配了段数却没配时机(SegTimings 空) → 无时机可推进，同样一段都不结算
    ///   ③ 段数 > 时机点数 → 多出来的段没有时机点，实际按时机点个数结算
    ///   ④ 段数 < 时机点数 → 多出来的时机点被忽略
    ///   ※ 只在 Editor 检查：属于配置阶段问题，Release 整段 #if 掉，零运行时开销。
    /// </summary>
    private void ValidateSegCount(SkillData data)
    {
#if UNITY_EDITOR
        int segCount = data.SegCount;
        int timingCount = (data.SegTimings != null) ? data.SegTimings.Length : 0;

        // ① 有时机没段数 → 完全不打伤害
        if (timingCount > 0 && segCount <= 0)
        {
            Debug.LogWarning($"[SkillData] 技能 {data.ID} 配了 {timingCount} 个 SegTimings 但 SegCount=0," +
                             $"本次施法将**一段伤害都不结算** —— 请在 CSV 列 \"SegCount\" 填上总段数");
            return;
        }

        // ② 有段数没时机 → 无法推进
        if (segCount > 0 && timingCount <= 0)
        {
            Debug.LogWarning($"[SkillData] 技能 {data.ID} 配了 SegCount={segCount} 但没有 SegTimings," +
                             $"没有时机点就无法推进段号，技能无伤害 —— 请在 CSV 列 \"SegTimings\" 补上时机点");
            return;
        }

        // ③④ 数量不等
        if (segCount > 0 && timingCount > 0 && segCount != timingCount)
        {
            string side = segCount > timingCount ? "段数多于时机点（多出的段无时机可触发）" : "段数少于时机点（多出的时机点被忽略）";
            Debug.LogWarning($"[SkillData] 技能 {data.ID}:SegCount={segCount} vs SegTimings {timingCount} 个，" +
                             $"{side}，实际只结算 {Mathf.Min(segCount, timingCount)} 段");
        }
#endif
    }

    /// <summary>
    /// ★ 配置防呆：命中效果表（CSV 列 13）与段数的对应关系。
    ///
    /// 【检查三项】
    ///   ① 配了逐段效果但 SegCount=0 → 该技能根本不分段，这张表永远不会被执行（死配置）
    ///   ② 组数与 SegCount 不等 → 多出来的组用不上 / 少掉的段回落共用列表，需点名
    ///   ③ 某段的效果里没有 0（ImmedDamage）→ 该段**不结算伤害**，只播表现。
    ///      这通常是刻意设计（如"第3段只击退"），所以措辞上说明"若是有意为之请忽略"
    /// </summary>
    private void ValidateEffectTypes(SkillData data)
    {
#if UNITY_EDITOR
        if (data.EffectTypesBySegment == null) return;

        int groupCount = data.EffectTypesBySegment.Length;

        // 只有 1 组 = 没写 |，语义是「全部效果集中在第 1 段」，是合法常规写法，不做段数校验
        if (groupCount <= 1) return;

        // ① 不分段却配了逐段表
        if (data.SegCount <= 0)
        {
            Debug.LogWarning($"[SkillData] 技能 {data.ID} 的 SkillEffectTypes 配了 {groupCount} 段（含 |）但 SegCount=0，" +
                             $"该技能不分段，第 2 段起永远不会被执行 —— 请先补 CSV 列 \"SegCount\"");
            return;
        }

        // ② 组数与段数不等
        if (groupCount != data.SegCount)
        {
            string side = groupCount > data.SegCount ? "段数多于 SegCount（多出的组用不上）" : "段数少于 SegCount（少掉的段没有任何效果）";
            Debug.LogWarning($"[SkillData] 技能 {data.ID}:SkillEffectTypes {groupCount} 段 vs SegCount={data.SegCount}，" +
                             $"{side}");
        }

        // ③ 某段不含 ImmedDamage(0) → 该段不掉血（可能是刻意设计，措辞保留余地）
        for (int i = 0; i < groupCount; i++)
        {
            List<int> segList = data.EffectTypesBySegment[i];
            if (segList == null || segList.Count == 0) continue;

            bool hasDamage = false;
            for (int j = 0; j < segList.Count; j++)
            {
                if (segList[j] == 0) { hasDamage = true; break; }
            }
            if (!hasDamage)
            {
                Debug.LogWarning($"[SkillData] 技能 {data.ID} 第 {i + 1} 段的效果表不含 ImmedDamage(0)，" +
                                 $"该段**不结算伤害**、只播表现 —— 若是有意为之请忽略");
            }
        }
#endif
    }

    /// <summary>
    /// ★ v5 P20 配置防呆：**特效 id 契约校验**（2026-09-19「敌人身上冒出玩家刀光」事故的直接产物）。
    ///
    /// 【为什么必须有这条】那次事故里，10003 的出手特效（列 22）与受击特效（列 14）第三段
    ///   **都配了 2003**，而 <c>EffectManager</c> 的池化 key 是 **ResName 而不是 id**、
    ///   且每个 key 只保留一个实例 ⇒ 同一个 GameObject 被「施法者武器挂点 Atk3」与
    ///   「受击者身上」两个业务来回抢用，父节点瞬移，表现为敌人身上冒出玩家的刀光。
    ///   ⚠ 这个坑**查时序永远查不出来** —— 出手时机 #1 0.140 / #2 0.330 / #3 0.600 三轮误差 ≤0.008，
    ///   与 CSV 逐条吻合，调度完全正确。它只能在配置层面用静态检查发现。
    ///
    /// 【检查两项】
    ///   ① 列 22 与列 14 出现**相同 id** → 池化单实例必然搬家，必须把其中一处改成别的 id
    ///   ② 列 22 / 列 14 引用了 <c>EffectData.csv</c> 中**不存在**的 id → 日志照打、画面零表现（死配置）
    ///
    /// 【只在 Editor】Release 构建整段 #if 掉，零开销。
    /// 【不做自动修正】改哪个 id、换成什么资源是策划决策，静默改写会让「我明明配了 A 怎么变成 B」无法追查。
    /// </summary>
    private void ValidateEffectIdContract(SkillData data)
    {
#if UNITY_EDITOR
        // 用 GetCastEffectIdList() 而不是直接读 AttackEffects ——
        // 它已处理「空列被 Str2IntList 解析成含一个 0 的 List」这条脏数据规则，避免把 0 当真 id 误报。
        List<int> castIds = data.GetCastEffectIdList();
        List<int> hitIds = data.GetHitEffectIdList();

        if ((castIds == null || castIds.Count == 0) && (hitIds == null || hitIds.Count == 0))
            return;

        // ① 出手 ↔ 受击 撞车（本次事故的真凶形态）
        if (castIds != null && hitIds != null)
        {
            for (int i = 0; i < castIds.Count; i++)
            {
                int id = castIds[i];
                if (id <= 0) continue;
                if (!hitIds.Contains(id)) continue;

                Debug.LogError($"[SkillData] 技能 {data.ID} 的出手特效(列22)与受击特效(列14/表格第15列)都用了 id={id} —— " +
                    $"EffectManager 池化按 ResName 只保留一个实例，该实例会在「施法者挂点」与「受击者身上」之间" +
                    $"来回搬家（表现：敌人身上冒出施法者的特效）。请把其中一处改成不同的 id。");
            }
        }

        // ② 悬空引用：配了 id 但 EffectData 里没有这一行
        ReportMissingEffectId(data, castIds, "出手特效(列22)");
        ReportMissingEffectId(data, hitIds, "受击特效(列14/表格第15列)");
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// 逐个检查特效 id 在 <c>EffectData.csv</c> 中是否存在，不存在则 LogError 指名。
    /// ⚠ 这类死配置最阴险：调用照常发生、日志照常打印（[HitEffect] 播放 10001），
    ///   但 <c>GetEffectData</c> 返回 null ⇒ **画面上什么都不会出现**，且没有任何报错。
    /// </summary>
    private static void ReportMissingEffectId(SkillData data, List<int> ids, string fieldName)
    {
        if (ids == null) return;

        for (int i = 0; i < ids.Count; i++)
        {
            int id = ids[i];
            if (id <= 0) continue;
            if (ConfigManager.GetEffectData(id) != null) continue;

            Debug.LogError($"[SkillData] 技能 {data.ID} 的{fieldName}引用了 id={id}，但 EffectData.csv 中不存在该行 —— " +
                $"调用与日志照常发生，但**画面上不会出现任何东西**（死配置）。请补 EffectData 行，或把该位改成 0。");
        }
    }
#endif

    /// <summary>
    /// ★ 2026-10-10 弹道投递契约校验（投掷物方案 P1）—— 与既有 Validate* 系列同一条纪律：
    ///   Editor-only、加载期一次性、不做自动修正（静默改写数据会让策划意图失真）。
    /// 检查五项：
    ///   ① 配了弹道投递但 ProjectileId 未配 / ProjectileData 无此行 —— 打出去没有弹体，死配置，LogError；
    ///   ② 弹道行 Speed / MaxRange / LifeTime ≤ 0 —— 永远飞不到判定条件，LogError；
    ///      （顺带点名 TrajectoryType=Lob —— 抛物线预留未实现，运行时按直线飞）
    ///   ③ FxId / ImpactFxId 引用 EffectData 中不存在的 id —— 调用照常、日志照打、画面零表现，LogError；
    ///   ④ FxId 与该技能出手特效(AttackEffects)/受击特效(HitEffect)撞 id —— EffectManager 池化按
    ///      ResName 单实例，撞车会让实例在「施法者挂点 ↔ 弹体」之间搬家（P20 事故形态），LogError；
    ///   ⑤ DeliveryType=Projectile 且 HitType 为 SelectedFriend / Self —— 治疗弹/自体弹语义特殊，
    ///      MVP 未实现，点名警告而非静默空转。
    /// </summary>
    private void ValidateProjectileBindings(SkillData data)
    {
#if UNITY_EDITOR
        if (data.DeliveryType != SkillDeliveryType.Projectile) return;

        // ① 弹道行必须存在
        ProjectileData proj = ConfigManager.GetProjectileData(data.ProjectileId);
        if (proj == null)
        {
            Debug.LogError($"[SkillData] 技能 {data.ID}({data.Name}) DeliveryType=Projectile 但 ProjectileId={data.ProjectileId} " +
                $"在 ProjectileData.json 中查无此行 ⇒ 段触发后无弹可出、本段结算被丢弃。" +
                $"请补 ProjectileData 行，或把 DeliveryType 改回 0(Instant)。");
            return;
        }

        // ② 飞行参数必须能到达判定条件
        if (proj.Speed <= 0f || proj.MaxRange <= 0f || proj.LifeTime <= 0f)
        {
            Debug.LogError($"[SkillData] 技能 {data.ID}({data.Name}) 引用的投掷物 {proj.ID}({proj.Name}) " +
                $"Speed={proj.Speed} / MaxRange={proj.MaxRange} / LifeTime={proj.LifeTime} 存在 ≤0 —— " +
                $"弹道无法飞行 / 无法自爆，本段永远不结算。");
        }
        if (proj.TrajectoryType == ProjectileTrajectoryType.Lob)
        {
            Debug.LogWarning($"[SkillData] 技能 {data.ID} 引用的投掷物 {proj.ID}({proj.Name}) " +
                $"TrajectoryType=Lob(抛物线) —— 尚未实现，运行时按直线弹飞行。");
        }

        // ③④ 特效 id 契约（悬空引用 / 撞车检查）
        ValidateProjectileEffectId(data, proj.FxId, "飞行特效", true);
        ValidateProjectileEffectId(data, proj.ImpactFxId, "弹着点特效", false);

        // ⑤ 治疗弹 / 自体弹 —— MVP 未实现
        if (data.HitType == SkillHitTypes.SelectedFriend || data.HitType == SkillHitTypes.Self)
        {
            Debug.LogWarning($"[SkillData] 技能 {data.ID}({data.Name}) DeliveryType=Projectile 且 " +
                $"HitType={data.HitType}（友方/自身）—— 治疗弹与自体弹当前未实现，弹体照飞但结算语义未定义。");
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// ③④ 两条共用的逐 id 检查。撞车检查只对**飞行特效**做 ——
    /// 它与出手特效同处「施法者端」生命周期（都挂在人/弹上跟随移动），最易互抢单实例；
    /// 弹着点特效播在无人认领的世界坐标上，不存在抢用对象。
    /// 复用 SkillData 的既有有效列表封装（GetCastEffectIdList / GetHitEffectIdList），
    /// 不吃「空列被解析成含一个 0 的 List」的坑。
    /// </summary>
    private static void ValidateProjectileEffectId(SkillData data, int fxId, string field, bool checkCollision)
    {
        if (fxId <= 0) return;

        // ③ 悬空引用 —— 死配置最阴险：日志照打、画面零表现
        if (ConfigManager.GetEffectData(fxId) == null)
        {
            Debug.LogError($"[SkillData] 技能 {data.ID} 的投掷物{field} id={fxId} 在 EffectData.json 中不存在 —— " +
                $"调用照常、画面零表现（死配置）。请补 EffectData 行，或把该 id 改成 0。");
            return;
        }

        if (!checkCollision) return;

        // ④ 撞车检查
        List<int> castIds = data.GetCastEffectIdList();
        if (castIds != null && castIds.Contains(fxId))
        {
            Debug.LogError($"[SkillData] 技能 {data.ID} 的投掷物飞行特效 id={fxId} 与出手特效(AttackEffects)撞 id —— " +
                $"EffectManager 池化按 ResName 只留一个实例，该实例会在「施法者挂点 ↔ 弹体」之间搬家" +
                $"（表现：弹飞到一半特效瞬移回手上）。请换成不同的 id。");
        }

        List<int> hitIds = data.GetHitEffectIdList();
        if (hitIds != null && hitIds.Contains(fxId))
        {
            Debug.LogError($"[SkillData] 技能 {data.ID} 的投掷物飞行特效 id={fxId} 与受击特效(HitEffect)撞 id —— " +
                $"同上，单实例会被两个业务互抢。请换成不同的 id。");
        }
    }
#endif

    public void LoadSkillData()
    {
        m_SkillDataDic = new Dictionary<int, SkillData>();

        // ★ 2026-09-28 CSV→JSON：配置源切换为 SkillData.json（键名与 SkillData 公开成员一一对应，
        //   枚举接受 int / 名字字符串两种写法）。加载与「单行失败跳行」语义统一收口在 JsonConfigLoader。
        //   旧 CSV 按字符切分的三处脆弱点（Length-1 上界静默丢尾行、裸 Parse 崩表、空串解析成 [0]）随切换一并消失。
        List<SkillData> rows = JsonConfigLoader.LoadRows<SkillData>("SkillData");
        if (rows == null)
        {
            return; // 整表不可用：保持空字典，LoadRows 已 LogError 指名，不静默降级
        }

        for (int i = 0; i < rows.Count; i++)
        {
            SkillData info = rows[i];
            if (info == null)
            {
                continue;
            }

            // ★ v5 P5:配置防呆 —— 特效时机必须升序,否则多个特效会扎堆在同一帧播出
            ValidateEffectTimings(info);

            // ★ v5 P6:配置防呆 —— 段数(SegCount)与时机点个数(SegTimings)必须对得上
            ValidateSegCount(info);

            // ★ 2026-09-28:受击特效铁定约束 —— 特效个数不得大于生效攻击段数
            ValidateHitEffectCount(info);

            // ★ 配置防呆 —— 命中效果表(列 13)的分段数应与 SegCount 一致
            ValidateEffectTypes(info);

            // ★ v5 P20:配置防呆 —— 特效 id 契约：出手(列22)与受击(列14)不得撞车，
            //   且引用的 id 必须存在于 EffectData（否则日志照打、画面零表现）
            ValidateEffectIdContract(info);

            // ★ 2026-09-29:AddBuff 绑定契约 —— 配了效果 2(AddBuff) 就必须有可用的 BuffID，且 id 必须存在
            ValidateBuffBindings(info);

            // ★ 2026-10-10 弹道投递契约 —— DeliveryType=Projectile ⇒ ProjectileId 必须指向真实弹道行，
            //   弹道参数合法，且飞行特效不与出手/受击特效撞车（P20 池化搬家的直接复用）
            ValidateProjectileBindings(info);

            if (!m_SkillDataDic.ContainsKey(info.ID))
            {
                m_SkillDataDic.Add(info.ID, info);
            }
            else
            {
                Debug.LogError("[SkillData] 重复技能ID已跳过: " + info.ID);
            }
        }
    }
}


