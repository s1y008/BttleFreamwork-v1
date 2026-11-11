# -*- coding: utf-8 -*-
"""
2026-09-24：把用户拍板的三项架构决策固化进《战斗框架抽象方案.md》
  ① 未注册效果号 → 三态 + 加载期一次性汇总
  ② 阶段一 → 完整策略模式（不做轻量委托表）
  ③ 阶段二 IHitReceiver → 现在就提接口预埋，与阶段一连续实施
  顺带落实评审提出的小修：Register 前先 Clear()

铁律：多处块替换走脚本 + 命中断言 + 写回复验。文档为纯 LF。
"""
import io, sys, os, shutil

P = r"E:\Project\BttleFreamwork-v1\战斗框架抽象方案.md"
BAK = P + ".bak.2026-09-24-decisions"

src = io.open(P, encoding="utf-8", newline="").read()

pairs = []

# ── ① 4.6 表格：Sneer 行由「保持现状」改为「已拍板并落地」
pairs.append((
"| Sneer(3) | 只作用于 `m_SkillTarget` 一人 | **保持现状**（同源问题，尚未拍板，见下） |",
"| **Sneer(3)** | 只作用于 `m_SkillTarget` 一人 | **作用于每个命中目标**（同源问题已一并拍板并落地，见 4.6.2） |",
))

# ── ② 接口注释：静默 → 三态
pairs.append((
"/// 未注册的效果号 = 尚未实现的效果桩，ExecuteEffect 静默跳过（与旧 switch default 等价）。",
"""/// 未注册的效果号 → **三态处理**（★ 2026-09-24 用户拍板，见 4.7）：
///   ① KnownStubIds 白名单内的已知空桩 → 静默跳过（与旧 switch default 逐字等价）；
///   ② 未知效果号 → LogError 指名（技能 / 段号 / 效果号），绝不静默降级（项目铁律 5）；
///   ③ 加载期按效果号聚合出「被 CSV 配置但未实现」的清单，只 Warning 一次 —— 不刷屏。""",
))

# ── ③ ExecuteEffect 体：三态实现
pairs.append((
"""    // ★ 23 分支 switch 已删：效果实现全部迁往 ISkillEffect 策略类（见 SkillEffect/ 目录）。
    //   未注册的效果号 = 尚未实现的效果桩 → 静默跳过，与旧 switch default 的「安全空操作」语义逐字等价
    //   （CSV 列 13 的 -1 / 未知值依赖这一契约，不可改成报错）。
    if (!SkillEffectRegistry.TryGet((int)effectType, out ISkillEffect effect))
    {
        return;
    }""",
"""    // ★ 23 分支 switch 已删：效果实现全部迁往 ISkillEffect 策略类（见 SkillEffect/ 目录）。
    //   ★ 未注册的效果号 → **三态**（用户 2026-09-24 拍板，见 4.7）：
    //     ① 已知空桩（KnownStubIds 白名单内）→ 静默跳过，与旧 switch default 逐字等价
    //       （CSV 列 13 的 -1 / 空段依赖这一契约，不可改成报错）；
    //     ② 未知效果号 → LogError 指名，绝不静默 —— 这个数字只可能来自「CSV 配错」
    //       或「忘了 Register」，两种都是必须暴露的错误，不能伪装成"这个效果没配"。
    if (!SkillEffectRegistry.TryGet((int)effectType, out ISkillEffect effect))
    {
        if (SkillEffectRegistry.IsKnownStub((int)effectType))
        {
            return;                       // ① 已知空桩：安全空操作
        }

        Debug.LogError($"[SkillEffect] 效果号 {(int)effectType}（技能 {BaseData.ID} 第{m_SegIdx}段）未注册："
                     + "要么它是空桩请加进 KnownStubIds，要么忘了 Register —— 不能静默跳过");
        return;                           // ② 已指名报错
    }""",
))

# ── ④ T3 取舍说明：补三态带来的限定
pairs.append((
"> T3 的取舍说明：也可以先逐 case 切换、每步验证 —— 但空桩 case 调用的是空方法体，注册表 TryGet 失败 return 与之逐字等价，故一次性替换是安全的，只需一次回归。",
"""> T3 的取舍说明：也可以先逐 case 切换、每步验证 —— 但空桩 case 调用的是空方法体，
> 注册表 TryGet 失败且**命中 KnownStubIds 白名单**时 return 与之逐字等价，故一次性替换是安全的，只需一次回归。
>
> ⚠ 加了三态之后有一处不同于旧 behavior：**未知效果号会新增一条 LogError**。
>   但 CSV 现有 21 种效果号全部落在「已注册(0/2/3/22) + 已知空桩(其余 17 种)」之内 ⇒ 实际等价，不会误报。""",
))

# ── ⑤ Setup：Register 前先 Clear（评审提出的小修）
pairs.append((
"""    private static void RegisterBuiltinEffects()
    {
        SkillEffectRegistry.Register(new ImmedDamageEffect());   // 0  立即伤害""",
"""    private static void RegisterBuiltinEffects()
    {
        // ★ 先 Clear 再注册：Domain Reload 关闭时，若只做索引器赋值，
        //   旧实例会覆盖掉新注册的实例（改了效果类逻辑却跑的还是旧对象）。零成本防御。
        SkillEffectRegistry.Clear();

        SkillEffectRegistry.Register(new ImmedDamageEffect());   // 0  立即伤害""",
))

# ── ⑥ 新增 4.7 节（三项决策）
pairs.append((
"---\n\n## 5. 可行性评估",
"""---

### 4.7 ★ 三项架构决策已拍板（2026-09-24）

| # | 议题 | 决策 |
|---|---|---|
| 1 | 未注册的效果号如何处理 | **三态 + 加载期一次性汇总** |
| 2 | 阶段一的实施形态 | **完整策略模式**（不做轻量委托表） |
| 3 | 阶段二 `IHitReceiver` 的时机 | **现在就提接口预埋**，与阶段一连续实施（阶段三仍延后） |

#### ① 三态 + 加载期一次性汇总

背景数据（2026-09-24 实测，来自 `SkillData.csv` 列 13）：CSV 共用到 **21 种效果号**，
其中 **17 种是空桩，却出现在约 175 个技能段里** —— 这些技能现在打出去就是静默失效。

| 效果号 | 涉及技能数 | 状态 |
|---|---|---|
| 0 / 22 / 2 | 218 / 37 / 12 | ✅ 已实现 |
| **7 / 4 / 16 / 5** | **54 / 40 / 38 / 15** | ❌ 空桩，**高频引用** |
| 11 / 9 / 10 / 6 / 18 / 19 | 6 / 4 / 4 / 2 / 2 / 2 | ❌ 空桩，低频 |
| 1 / 8 / 12 / 13 / 14 / 17 / 20 / 21 | 各 1 | ❌ 空桩，个例 |
| 3 | 0 | ✅ 已实现但零配置 |

因此三态的判据：

- **已知空桩**（`KnownStubIds` 白名单内）→ 静默跳过，与旧 switch default 逐字等价；
- **未知效果号**（既没注册、也不在白名单）→ `LogError` 指名（技能 / 段号 / 效果号）；
- **加载期一次性汇总**：配置加载后按效果号聚合统计「被 CSV 配置但未实现」的清单，只 Warning 一次。

> 这样既不刷屏（若一律报错，光 7/4/5 三个号就涉及 109 个技能），也不违反铁律 5（禁止静默降级）：
> 175 个技能段的失效会以**一份清单**的形式暴露出来，而不是每次命中都报、也不是永远看不见。

#### ② 完整策略模式

原本的取舍是"取决于打算填几个桩"，现在有数了 —— **已有 147 个技能段在引用 4 个高频空桩**，
填桩不再是假设而是确定要做的事，一次到位比「先委托表、再升级」便宜。

#### ③ 阶段二现在做

`CombatUnitController` 目前是唯一实现类，此时提接口属预埋。用户选择在阶段一之后紧接着做，
不与阶段三混在一起；**阶段三 `IHitShape` 仍建议延后**（等真的有新形状需求）。

#### 实施时一并处理的小修（无需再拍板）

`SkillEffectRegistry.Register` 前先 `Clear()` —— Domain Reload 关闭时索引器赋值会让旧实例覆盖新实例。

---

## 5. 可行性评估""",
))

# ── ⑦ 阶段二标题标注已拍板
pairs.append((
"### 6.2 阶段二：受击侧 `IHitReceiver` 接口提取",
"### 6.2 阶段二：受击侧 `IHitReceiver` 接口提取（★ 已拍板 2026-09-24：与阶段一连续实施，不延后）",
))

# ── ⑧ 风险表新增第 12 项
pairs.append((
"| 11 | 同类问题漏改：`Sneer` 与 `AddBuff` 同源 | ✅ **已拍板并一并落地**（4.6.2）：`Sneer(CombatUnitController target)` + 探针实测通过；`SkillEffectContext` 因此**取消 `PrimaryTarget` 字段** |",
"""| 11 | 同类问题漏改：`Sneer` 与 `AddBuff` 同源 | ✅ **已拍板并一并落地**（4.6.2）：`Sneer(CombatUnitController target)` + 探针实测通过；`SkillEffectContext` 因此**取消 `PrimaryTarget` 字段** |
| 12 | ★ **17 个空桩被约 175 个技能段引用**（当前即静默失效） | 不在本次重构范围（填桩属 6.4）；但按 4.7-① 的三态方案，加载期会一次性汇总出清单，避免「打了没反应」被误当成重构引入的漂移 |""",
))

for i, (old, new) in enumerate(pairs, 1):
    cnt = src.count(old)
    if cnt != 1:
        print("[FAIL] 第 %d 块命中 %d 次（期望 1）" % (i, cnt))
        print("--- old 前 120 字 ---")
        print(repr(old[:120]))
        sys.exit(1)
    src = src.replace(old, new, 1)
    print("[OK] 第 %d 块已替换" % i)

io.open(P, "w", encoding="utf-8", newline="").write(src)

chk = io.open(P, encoding="utf-8", newline="").read()
for i, (old, new) in enumerate(pairs, 1):
    if new not in chk:
        print("[FAIL] 复验失败：第 %d 块不在磁盘中" % i)
        if os.path.exists(BAK):
            shutil.copyfile(BAK, P)
            print("[ROLLBACK] 已还原")
        sys.exit(1)

print("[VERIFY] 全部 %d 块已落盘" % len(pairs))
print("代码块 =", chk.count("```"), "（偶数）")
print("LF =", chk.count("\n"), "CRLF =", chk.count("\r\n"))
for k in ("4.7 ★", "KnownStubIds", "SkillEffectRegistry.Clear", "已拍板 2026-09-24：与阶段一连续实施"):
    print("  %-45s %d" % (k, chk.count(k)))
