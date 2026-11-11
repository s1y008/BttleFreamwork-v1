# -*- coding: utf-8 -*-
"""
2026-09-24  语义级控制 Buff 施加：硬编码 BuffType -> Tags 反查
堵住的漏洞：ApplyStun/ApplySilence 硬编码 BuffType.Stunned/Silenced，
走 AddBuff(BuffType) -> GetData(type) -> 只拿 _buffDic 首个登记者，
导致 ① Tags 数据驱动对这条路径失效 ② 同位的第二个 Buff 永远摸不到。

改动三处：
  1. BuffData.cs   新增 _tagRepresentative + PickTagRepresentatives() + GetDataByTag()
  2. UnitHitReaction.cs  ApplyControlBuff(BuffType) -> ApplyControlBuff(string tag, int buffId, ...)
  3. ConfigManager.cs    新增 GetBuffDataByTag 转发
  4. CombatUnitController.cs 修正过时注释（AddBuff(type) -> AddBuff(buffId)）
"""
import io, sys

ROOT = r"E:\Project\BttleFreamwork-v1\Assets\Scripts"

def load(path):
    t = io.open(path, encoding="utf-8", newline="").read()
    return t.replace("\r\n", "\n"), ("\r\n" in t)

def save(path, text, crlf):
    if crlf:
        text = text.replace("\n", "\r\n")
    io.open(path, "w", encoding="utf-8", newline="").write(text)

def rep(path, pairs):
    text, crlf = load(path)
    for old, new in pairs:
        n = text.count(old)
        assert n == 1, "命中 %d 次（应为 1）: %s" % (n, old[:60])
        text = text.replace(old, new)
    save(path, text, crlf)
    # 写回后从磁盘重读复验
    back, _ = load(path)
    for old, new in pairs:
        assert new in back, "写回复验失败，新内容未落盘: %s" % new[:60]
    print("OK", path.split("\\")[-1])


# ============ 1. BuffData.cs ============
A = ROOT + r"\BattleFreamwork\BattleData\BuffData.cs"

pairs_A = []

pairs_A.append((
"""    private Dictionary<BuffType ,BuffData>  _buffDic = new Dictionary<BuffType,BuffData>();
""",
"""    private Dictionary<BuffType ,BuffData>  _buffDic = new Dictionary<BuffType,BuffData>();

    /// <summary>
    /// 行为标签 → 代表配置行（同标签多行时取 Id 最小者，保证「CSV 行序一动行为就变」不会发生）
    /// ★ 存在的意义：UnitHitReaction.ApplyStun / ApplySilence 这类**语义级施加**入口，
    ///   以前硬编码 BuffType.Stunned / Silenced —— 那会让 Tags 数据驱动对这条路径整体失效，
    ///   而且同标签的第二个 Buff 永远摸不到（GetData(BuffType) 只返回首个登记者）。
    ///   改由 Tags 反查后，「CSV 写什么就用什么」才真正闭环。
    /// </summary>
    private readonly Dictionary<string, BuffData> _tagRepresentative = new Dictionary<string, BuffData>();
"""))

pairs_A.append((
"""            _buffs.Add(buff.Id, buff);
        }
    }
""",
"""            _buffs.Add(buff.Id, buff);
        }

        PickTagRepresentatives();
    }
"""))

# GetDataByTag + PickTagRepresentatives 插到 GetData(int id) 之后、ParseTags 之前
pairs_A.append((
"""        return data;
    }

    /// <summary>
    /// 解析 Tags 列""",
"""        return data;
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
    /// 解析 Tags 列"""))

rep(A, pairs_A)


# ============ 2. UnitHitReaction.cs ============
B = ROOT + r"\BattleFreamwork\View\UnitHitReaction.cs"

pairs_B = []

old_block = """    /// <summary>
    /// 眩晕 —— 统一走 Buff 系统（免疫 / 叠加 / 驱散 / 时长都由 Buff 管理），由 Buff 事件驱动 FSM 切 Stun。
    /// 本方法只做「把这一下翻译成一个带时长的 Buff」。
    /// </summary>
    public void ApplyStun(CombatUnitController attacker, float duration)
    {
        ApplyControlBuff(BuffType.Stunned, attacker, duration);
    }

    /// <summary>沉默 —— 同 ApplyStun，目标是 Silenced + Silence 状态</summary>
    public void ApplySilence(CombatUnitController attacker, float duration)
    {
        ApplyControlBuff(BuffType.Silenced, attacker, duration);
    }

    /// <summary>
    /// 控制 Buff 落地：读配置 → 挂到本单位 BuffSystem → 用调用方的时长覆盖配置默认时长。
    /// 时长由 Buff 独占持有，FSM 侧（StunState / SilenceState）只负责每帧查询，不各自倒计时。
    /// </summary>
    private void ApplyControlBuff(BuffType type, CombatUnitController attacker, float duration)
    {
        if (attacker == null || m_Owner.IsDead || m_Owner.CurCombatUnit == null)
        {
            return;
        }

        Buff buff = m_Owner.CurCombatUnit.BuffSystem.AddBuff(type, attacker.GetInstanceID());
        if (buff != null && duration > 0f)
        {
            buff.RemainingTime = duration; // 用调用方时长覆盖配置默认时长
        }
    }
"""

new_block = """    /// <summary>
    /// 眩晕 —— 统一走 Buff 系统（免疫 / 叠加 / 驱散 / 时长都由 Buff 管理），由 Buff 事件驱动 FSM 切 Stun。
    /// 本方法只做「把这一下翻译成一个带时长的 Buff」。
    ///
    /// ★ 2026-09-24：施加对象不再硬编码 BuffType.Stunned，改由 CSV 的 **Tags 列**反查
    ///   （取声明了 HardControl 标签、Id 最小的那一行）。原写法的两个坑：
    ///   ① AddBuff(BuffType) 只拿 GetData(type) 的首个登记者 ⇒ 同位的第二个 Buff 永远摸不到；
    ///   ② 新 Buff 哪怕在 CSV 写了 HardControl 标签，走这条路径也进不来 ⇒ Tags 数据驱动在此断裂。
    /// </summary>
    /// <param name="buffId">要挂的具体 Buff 配置行；0 = 按 HardControl 标签取代表行</param>
    public void ApplyStun(CombatUnitController attacker, float duration, int buffId = 0)
    {
        ApplyControlBuff(BuffTags.HardControl, buffId, attacker, duration);
    }

    /// <summary>沉默 —— 同 ApplyStun，按 Silence 标签取代表行，FSM 切 Silence 状态</summary>
    /// <param name="buffId">要挂的具体 Buff 配置行；0 = 按 Silence 标签取代表行</param>
    public void ApplySilence(CombatUnitController attacker, float duration, int buffId = 0)
    {
        ApplyControlBuff(BuffTags.Silence, buffId, attacker, duration);
    }

    /// <summary>
    /// 控制 Buff 落地：① 确定挂哪一行配置 → ② 挂到本单位 BuffSystem → ③ 用调用方时长覆盖配置默认时长。
    /// 时长由 Buff 独占持有，FSM 侧（StunState / SilenceState）只负责每帧查询，不各自倒计时。
    ///
    /// ★ 查询优先级：显式 buffId ＞ Tags 反查。**全程不碰 BuffType 枚举** ——
    ///   这是「新增一个控制 Buff 只需在 CSV 写一行」能真正闭环的最后一环。
    /// </summary>
    private void ApplyControlBuff(string tag, int buffId, CombatUnitController attacker, float duration)
    {
        if (attacker == null || m_Owner.IsDead || m_Owner.CurCombatUnit == null)
        {
            return;
        }

        BuffData buffData = buffId > 0
            ? ConfigManager.GetBuffData(buffId)
            : ConfigManager.GetBuffDataByTag(tag);

        if (buffData == null)
        {
            // fail-fast：资产漏配就该当场报错指名，不许变成「打了没反应」的隐性 bug
            Debug.LogError("[UnitHitReaction] 控制Buff施加失败 —— "
                + (buffId > 0
                    ? "找不到 BuffId=" + buffId + " 对应的配置行"
                    : "没有任何配置行声明了行为标签 '" + tag + "'")
                + "，请检查 BufferData.csv（owner=" + m_Owner.name + "）");
            return;
        }

        Buff buff = m_Owner.CurCombatUnit.BuffSystem.AddBuff(buffData.Id, attacker.GetInstanceID());
        if (buff != null && duration > 0f)
        {
            buff.RemainingTime = duration; // 用调用方时长覆盖配置默认时长
        }
    }
"""

pairs_B.append((old_block, new_block))
rep(B, pairs_B)


# ============ 3. ConfigManager.cs ============
C = ROOT + r"\ConfigData\ConfigManager.cs"
rep(C, [(
"""   public static BuffData GetBuffData(BuffType type)
   {
       return _buffMsg.GetData(type);
   }
""",
"""   public static BuffData GetBuffData(BuffType type)
   {
       return _buffMsg.GetData(type);
   }

   /// <summary>
   /// 按行为标签取代表配置行（BufferData.csv 的 Tags 列，见 BuffTags 常量）。
   /// 无匹配返回 null —— 调用方负责 LogError 指名，不做静默降级。
   /// </summary>
   public static BuffData GetBuffDataByTag(string tag)
   {
       return _buffMsg.GetDataByTag(tag);
   }
""")])


# ============ 4. CombatUnitController.cs 注释漂移修正 ============
D = ROOT + r"\BattleFreamwork\Controller\CombatUnitController.cs"
text_d, crlf_d = load(D)
old_comment = "        //（OnAddBuff内部：GetBuffData判空 → BuffSystem.AddBuff(type, attacker.GetInstanceID())）"
new_comment = "        //（OnAddBuff内部：GetBuffData判空 → BuffSystem.AddBuff(buffid, attacker.GetInstanceID())）"
n = text_d.count(old_comment)
if n == 1:
    save(D, text_d.replace(old_comment, new_comment), crlf_d)
    print("OK CombatUnitController.cs (注释已修正)")
else:
    print("SKIP CombatUnitController.cs —— 注释命中 %d 次，未改动" % n)

print("\n==== 全部完成 ====")
