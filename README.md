# BttleFreamwork-v1

基于 Unity 的**数据驱动战斗框架**。核心思路：把「数据」「决策」「结算」「表现」四层拆开，每件事只有一个真相源。

> 本项目为教学与工程实践用框架，玩家角色、敌人、技能、特效均由配置表驱动，不依赖硬编码。

---

## 技术栈

| 项 | 版本 |
|---|---|
| Unity | 2023.2.20f1c1 |
| 序列化 | Newtonsoft.Json 3.2.2 |
| 编辑器 UI | Odin Inspector（商业插件，见下方「运行前置」） |
| 动画 | Unity Animator（Animancer 仅作可选扩展） |

---

## ⚠ 运行前置：两个付费插件不在本仓库

本仓库为公开仓库，**不含以下商业插件**（其授权协议禁止公开分发），因此**克隆后直接打开会编译失败，这是预期行为**：

| 缺失目录 | 插件 | 说明 |
|---|---|---|
| `Assets/Plugins/Sirenix/` | [Odin Inspector](https://odininspector.com/) | 技能编辑器与 Inspector 增强依赖 |
| `Assets/Plugins/Animancer/` | [Animancer](https://kybernetik.com.au/animancer/) | 可选动画方案，框架主体不依赖 |

**恢复步骤**：自行购买并导入上述插件，放回对应目录即可。若只关心框架逻辑，可先移除 `Assets/Editor/SkillEditor/`（依赖 Odin），运行时不依赖这两个插件。

---

## 架构分层

```
输入 / AI 决策
    ↓
CombatUnitController  ── 决策层：状态切换、技能选择、Facade 转发（不碰 Animator）
    ↓
CombatStateMachine ── 七状态：Idle / Move / Attack / Stun / Silence / Knockback / Dead
    ↓                 转换表持久化为 ScriptableObject 资产，可编辑
AttackState.OnUpdate ── clock.GetProgress() 驱动，归一化时间轴 0~1
    ↓
Skill.SkillHit(segIdx) ── 按段取效果号 → SkillEffectRegistry
    ↓
UnitHitReaction ── 受击编排：ApplyHit → HitOutcome{Ignored, Normal, Dead}
    ↓
UnitView ── 表现层唯一出口（动画 / 特效 / 飘字）
```

### 关键设计约束

- **段号 1-based**，只在 `Skill` 内部流转；对外唯一入口 `Skill.SkillHit(int segIdx)`。
- **时间轴归一化**：`SegTimings` / `CastEffectTimings` 存 0~1 动画进度，不是绝对秒。
- **效果号按段分发**：段数超过效果组数时返回 `null`，**不复用第一组**（避免误伤）。
- **收尾三条路径**：段数打完 / 动画播完 / 被打断，由 `_castFinished` 保证只回调一次。
- **零 GC**：`readonly struct` + `in` 传参、`OverlapSphereNonAlloc`、对象池、禁用 `foreach` 装箱与 LINQ。
- **fail-fast**：配置缺失一律 `LogError` 指名，**禁止为数据不一致写运行时静默降级**。

---

## 目录结构

```
Assets/
├── Scripts/
│   ├── BattleFreamwork/
│   │   ├── Controller/      决策层与 Facade（CombatUnitController、WarrorController）
│   │   ├── StateMachine/    七状态实现 + 转换表
│   │   ├── SkillEffect/     效果接口、注册表、各效果实现、投射上下文
│   │   ├── Projectile/      弹道系统（XZ 扫掠判定，防高速隧穿）
│   │   ├── View/            UnitView、UnitHitReaction
│   │   └── Net/             ICastClock 等联网接缝
│   └── ConfigData/          JSON 加载器与数据类
├── Editor/SkillEditor/      技能编辑器（契约 / 模型 / 时间轴 / Gizmo / 新建向导 / 单位挂载）
├── Resources/Config/        5 张配置表（SkillData 236 条、ProjectileData 等）
└── ArtResources/            模型、特效、贴图

Tools/                        数据统计、改造校验、HTML 冒烟脚本
ConfigCsvBackup_2026-09-28/   CSV → JSON 迁移前的原始快照
```

---

## 配置层

5 张 JSON 表由 `JsonConfigLoader` 加载，**键名与 C# 公开成员名一一对应**（非列号）。

| 表 | 内容 |
|---|---|
| `SkillData.json` | 236 条技能：段数、归一化时机点、逐段效果号、命中特效、弹道绑定 |
| `ProjectileData.json` | 弹道参数：速度、追踪转速、命中半径、寿命、特效 id |
| `EffectData.json` | 特效资源映射 |
| `WarriorData.json` | 21 个玩家单位及其 `SkillArray` |
| `AttackNpcData.json` | 1460 个 NPC 及其 `SkillArray` |

⚠ **在 `SkillData.json` 新增一行，不代表任何单位会使用该技能** —— 必须同时写进对应单位的 `SkillArray`。

---

## 技能编辑器

菜单：`Tools / BattleFramework / Skill Editor`

能力：
- 按「已配时间轴 / 全部技能」分组的技能树，实时编辑 28 个字段
- 归一化时间轴可视化（逐段时机、出手特效时机、效果号轨道）
- 效果号可视化 chips，未实现的效果号标红提示
- 新建技能（复制现有 / 空白模板，深拷贝保证不污染源技能）
- 挂载到 Warrior / AttackNpc（写 `SkillArray`）
- Scene 视图 Gizmo：攻击距离环、命中范围、朝向箭头

⚠ 保存只写 `SkillData.json`（挂载另写单位表）；配置在进入 Play 时加载，**改完需重进 Play 才生效**。

---

## 已知限制

- 玩家按键释放的技能在 `WarrorController` 中固定为单一技能 id，切换技能需改代码或走调试入口。
- `EffectTypesBySegment` 中部分效果号仍为「已注册空桩」（配了不报错但不产生行为），编辑器已标注。
- 部分技能 `SegCount = 0`，依赖「动画播完」收尾路径兜底，不会结算伤害段。
- 弹道的 `PierceCount`（穿透）与 `Ballistic`（抛物线）为预留字段，尚未实现。

---

## License

代码部分用于教学与工程实践。`Assets/ArtResources/` 下的美术资源版权归原权利人所有，请勿直接商用。
