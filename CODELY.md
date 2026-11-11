# CODELY.md — BttleFreamwork-v1（战斗框架 / Battle Framework Prototype）

> Generated 2026-10-10 from live Unity Editor state (via Unity tools) + file analysis.
> Canonical project-context file: this file at **project root** (an older `Assets/CODELY.md` also exists — do not confuse them).

---

## 0. ★ 全局沟通规则（用户指令，2026-10-13 起，优先级高于本文件其余约定）

1. **所有回答一律使用中文**。
2. **向用户提问 / 请求选择时一律使用中文**——包括 `ask_user` 的问题、选项标签（label）、选项说明、输入提示（placeholder）。
3. **思考过程（thinking）同样使用中文**。

---

## 1. Project Overview

**One sentence:** A 3D action-combat (ARPG-style) **battle framework prototype** — data-driven skills with per-segment hit timelines, a 7-state combat FSM, a tag-driven buff system with zero-GC object pooling, strategy-pattern skill effects, hit reactions, auto-fight AI and a virtual joystick.

| Item | Value |
|---|---|
| Unity version | **2023.2.20f1c1** (ceda3f59bbc9), installed at `C:/Program Files/Unity/Hub/Editor/2023.2.20f1c1` |
| Render pipeline | **Built-in** (no URP/HDRP packages) |
| Language / namespaces | C#, gameplay code in `namespace BattleFramework.Core` |
| Target platform | Editor / standalone prototype — **no build pipeline or build scripts exist** |
| Scene count | 1 (`SampleScene.unity`) |
| Documentation language | **Chinese** (design docs, code comments, config field docs) |

---

## 2. Scene & Runtime Entry Point

- **Only scene:** `Assets/Scenes/SampleScene.unity` (currently open; loaded first).
- **Bootstrap:** scene root object **`GameStart`** → `Assets/Scripts/GameStart.cs`:
  - `CreadHero()` — `Resources.Load("Model/Player/NYT")` → instantiate at origin → `AddComponent<WarrorController>()` → `CurCombatUnit = new Warrior(1001)`.
  - `CreadNPC()` — `Resources.Load("Model/Enemy/GBL")` → at `(0,0,3)` → `AddComponent<AttackNpcController>()` → `CurCombatUnit = new AttackNpc(10003001)`.
- Scene also contains an editor-placed `NYT` hero model instance (Bip001 rig) and `baodian xiao` (hit-VFX marker with `guangci`/`wave`/`474` children); runtime spawning happens through `GameStart`.
- Combat units live on **Layer 6**.

---

## 3. Architecture (layered, from `战斗框架抽象方案.md`)

```
配置层   SkillData / BufferData / EffectData / WarriorData / AttackNpcData (JSON in Resources/Config)
   ↓ JsonConfigLoader / ConfigManager (Newtonsoft)
数据层   BaseUnit → CombatUnit(HP/属性/死亡事件) → Warrior(1001) / AttackNpc(10003001)（纯数值+技能列表）
   ↓ 被持有
控制层   MoveBaseController → CombatUnitController（索敌/施法/FSM桥接/受击Facade）
                                 → WarrorController(玩家·摇杆) / AttackNpcController(NPC·自动战斗)
   ↓ 编排
行为层   CombatStateMachine(ICombatState ×7状态) + UnitView(画面) + UnitHitReaction(受击编排)
         + UnitBuffComponent(Buff事件→FSM仲裁) + BuffSystemPooled(数值真相源)
   ↓ 结算
技能层   Skill.SkillHit(segIdx) → SkillHitInternal(按HitType/ShapeType选目标) → HitTarget
         → ExecuteEffect → ISkillEffect 策略 + SkillEffectRegistry
```

**Attack chain:** `AutoFightUpdate` → `GetCanUseSkill` → `SelectTartgetBySkill` → `TryCastSkill` → `StarSKill` (sic) → `AttackState.OnUpdate` dual timeline (cast FX `CastEffectTimings` + damage segments `SegTimings`) → `TriggerSegmentHit` → `Skill.SkillHit(segIdx)` → per-target `HitTarget` → `ExecuteEffect` → receiver `IHitReceiver.OnXxx` → `UnitHitReaction.ApplyXxx` (扣血 → 死亡 → 特效 → 仇恨 → 控制态).

**Delivery dimension (投掷物/弹道, 2026-10-10):** `SkillData.DeliveryType` (0=Instant default, 1=Projectile) is orthogonal to the effect list. Projectile path: at segment timing `SkillHit` branches to `ProjectileManager.Spawn` (does NOT write SegIdx); on impact `Skill.SkillHitAt(segIdx, impactPos, dir)` atomically writes segment + impact-origin then runs the original `SkillHitInternal`. Components: `BattleFreamwork/Projectile/` (`ProjectileManager` pooled + driver GO, `Projectile` straight/homing), config `BattleFreamwork/ConfigData/ProjectileData.cs` + `Resources/Config/ProjectileData.json`; `EffectManager.CreateEffectAt` for unbound impact FX; cast gate in `TryCastSkill` (skill with unresolved projectile cannot re-cast — protects `m_SkillTarget` from mid-flight rewrite). Reference skill: **10201 魔法球** (see `战斗框架修改记录_2026-10-10_投掷物投递层.md`).

### Key classes (all under `Assets/Scripts/`)
| Area | Files |
|---|---|
| State machine | `BattleFreamwork/StateMachine/` — `CombatStateMachine`, `CombatState`, `CombatStates`, `CombatTransitionTable` (ScriptableObject) |
| Controllers | `BattleFreamwork/Controller/` — `CombatUnitController` (base), `WarrorController` (hero, *misspelling intentional — do not rename*), `AttackNpcController`, `MoveBaseController`, `IHitReceiver`, `UnitBuffComponent` |
| Data units | `BattleFreamwork/BattleData/` — `BaseUnit`, `CombatUnit`, `Warrior`, `AttackNpc`, `BuffData` |
| Buffs | `BattleFreamwork/BufferSystem/` — `Buff`, `BuffSystemPooled` (ulong bit-enum flags, O(1) checks), `BuffType` (Tags column drives control-buff classification), `ClassObjectPool<T>` (zero-GC pooling) |
| Skills & effects | `BattleFreamwork/Skill.cs`, `SkillEffect/` — `ISkillEffect`, `SkillEffectRegistry`, `SkillEffectSetup`, `SkillCastContext`, `Effects/{ImmedDamageEffect, AddBuffEffect, HureEffect, SneerEffect}` |
| Projectiles (投掷物) | `BattleFreamwork/Projectile/` — `ProjectileManager` (pooled, own driver GO), `Projectile` (straight/homing, XZ-plane hit test); config class `BattleFreamwork/ConfigData/ProjectileData.cs` |
| View | `BattleFreamwork/View/` — `UnitView` (only layer touching Animator), `UnitAnimationClipSet`, `UnitHitReaction` |
| Config loaders | `Scripts/ConfigData/` — `ConfigManager`, `JsonConfigLoader`, `EffectData`, `EffectManager`; per-table classes in `BattleFreamwork/ConfigData/` (`SkillData`, `WarriorData`, `AttackNpcData`) |
| Misc | `BattleConst`, `CommonHelper` (`Str2Int`/`Str2Float`/`RollCrit`), `EffectDot`, `VirtualJoystick` (+ `RockerUI.prefab`), `Net/CastClock` (cast counter/clock), `Debug/{DeathLinkProbe, GcAllocProbe}` |
| Editor tools | `Scripts/BattleFreamwork/Editor/CombatTransitionMatrixWindow` (FSM matrix), `Assets/Editor/` (`GBLAnimatorBuilder` builds GBL's AnimatorController in code; `AnimationCollector`, `BuffDiagnostics`, `EffectSnapshot`) |

**Skill effect enum (`SkillEffectTypes`):** 23 effects defined; only **4 implemented** as strategies — `ImmedDamage=0`, `AddBuff=2`, `Sneer=3`, `Hure=22` (受击); the other ~19 are empty stubs awaiting implementation (see `战斗框架抽象方案.md` §4.4 for the full stub table).

**Reference test skill:** **10003「三连斩」** — 3 segments (`SegTimings 0.3;0.6;0.9`), per-segment effect lists, single-target. Hero skills: 10002–10006 + `Super10006`.

---

## 4. Directory Map

```
Assets/
├── Scripts/                 # GameStart.cs, TestPlayer/TestEnemy + BattleFreamwork/, ConfigData/
├── Resources/               # Runtime-loaded: Config/*.json, Model/{Player/NYT.prefab, Enemy/GBL.prefab},
│                            #   NYT.controller, Effect/, RockerUI.prefab, 摇杆 textures
├── ArtResources/            # AnimatorController/ Effect/ Model/ Textures/
├── Editor/                  # GBLAnimatorBuilder + AnimationCollector/BuffDiagnostics/EffectSnapshot
├── Tools/                   # ~120 dated Python maintenance scripts (see §6 conventions)
├── Plugins/Animancer/       # Animancer library (3 asmdefs). NOT used by framework yet —
│                            #   mentioned only as future option in UnitAnimationClipSet.cs; playback is Mecanim
├── _p18_unused/, _planA_unused/, _diagnose/   # quarantined retired code (kept, not deleted)
└── *.md / *.html            # Chinese framework docs + their .bak copies (see §7)
Root:
├── 战斗框架抽象方案.md        # ★ canonical abstraction design doc (接口抽象方案, 1251 lines)
├── design.md                # skill 10003 enemy-reaction design (done & verified 2026-09-18)
├── plan.md, review.md       # planning/review docs
├── BuffSystem.Tests/        # xUnit sources, OUTSIDE Assets (not Unity-compiled, see §9)
├── GameData/BufferData.xlsx # designer-side spreadsheet source for buff table
├── ConfigCsvBackup_2026-09-28/  # frozen backup of pre-JSON CSV configs
├── 备份的包/                 # backed-up packages
└── _unused_editor/, _unused_MemoryBuffer_Old/   # quarantined retired editor/old-buffer code
```

---

## 5. Config & Data Assets

- **Live configs are JSON** in `Assets/Resources/Config/`: `SkillData.json`, `BufferData.json`, `EffectData.json`, `WarriorData.json`, `AttackNpcData.json`, `ProjectileData.json` (投掷物弹道参数) (+ `CombatTransitionTable.asset` ScriptableObject for FSM transitions).
- Migrated **CSV → JSON around 2026-09-28/29** (old CSVs frozen in `ConfigCsvBackup_2026-09-28/`; `verify_json_vs_csv_2026-09-29.py` in Tools verified equivalence).
- ⚠ **Docs and code comments still describe the old 27-column CSV layout** (column 13 = effect list, 23/24 = SegTimings/HitEffectIds, etc.). Treat all CSV-column references as **historical**; the JSON schema is authoritative.
- Segmented-effect syntax retained in JSON values: `|` splits per-segment, `;` splits within a segment (e.g. `0|0;22|0;22` = seg1 [damage], seg2 [damage+hit], seg3 [damage+hit]).
- Historical hazard (CSV era): old parsers used `Split('\r')` + CRLF line endings — a LF-converted CSV silently loaded 0 rows. JSON loaders replaced them, but **if CSV parsing is ever reintroduced, keep CRLF**.

---

## 6. Development Conventions — CRITICAL

1. **★ Concurrent AI editing:** Other AI agents have made (and may still make) parallel edits to this project in the same session window. Before ANY edit: **re-read the target region with a fresh `read_file`** (never trust an earlier read), match `old_string` exactly, and after editing **grep to verify key wiring was not overwritten** by another agent.
2. **`.bak` backup convention:** Risky edits historically leave a copy `<file>.bak.YYYY-MM-DD-<tag>` next to the original (both code and docs; hundreds exist). Preserve the convention for significant changes; never bulk-delete `.bak` files without asking.
3. **Quarantine over deletion:** Retired code moves to `_unused_*` / `_p18_unused` / `_planA_unused` / `_diagnose` folders rather than being deleted. Root also keeps `_unused_editor/`, `_unused_MemoryBuffer_Old/`.
4. **`Assets/Tools/` is an AI-maintenance archive:** ~120 one-shot Python scripts named `apply_*` / `verify_*` / `migrate_*` / `scan_*` with dates (used for bulk refactors & equivalence checks), plus `_run_*.txt` play-mode logs. They are historical artifacts — do not "clean them up", but prefer fresh scripts + Codely Unity tools for new work.
5. **Comments/docs in Chinese**; keep new comments in the codebase's Chinese style, explaining *why* (this codebase documents reasoning heavily — e.g. boundary notes in `UnitAnimationClipSet.cs`).
6. **Naming:** Classes/components `PascalCase`; assets mixed; typo `WarrorController` and `StarSKill`/`SelectTartget` are **established API — do not rename**.
7. **Editor-only code** lives in `Assets/Editor/`, `Assets/Scripts/BattleFreamwork/Editor/`, and `[AddComponentMenu("BattleFramework/...")]` groups collection tooling (excluded from builds).
8. **No `*.asmdef` for project code** — gameplay compiles into `Assembly-CSharp`. Only Animancer ships asmdefs (Animancer / Examples / FSM).
9. **Verification culture:** every meaningful change was historically followed by play-mode probes (`Debug/DeathLinkProbe`, `GcAllocProbe`), log scans, and equivalence scripts before declaring done.

---

## 7. Key Documentation (root & Assets/)

- `战斗框架抽象方案.md` — ★ canonical architecture/abstraction plan (IHitReceiver / ISkillEffect strategy migration; 23-effect stub table; read this first for design intent). `.bak` copies mark each evolution.
- `design.md` — completed design for skill 10003 enemy hit reactions (3-layer nested Animator FSM, `Hit` sub-state machine).
- `Assets/战斗框架技术文档.md` + `战斗框架技术文档_段号驱动与状态机推进.html` + `战斗框架全链路与数据流转.html` — framework technical docs (segment-index-driven skills & FSM), each with `.bak.<date>-<tag>` snapshots.
- `战斗框架演化全史_2026-10-10.html` — evolution history.
- `BattleFreamwork/BufferSystem/BuffSystem_使用说明.md` — buff system manual (CSV-era but still describes mechanics).

---

## 8. Packages & Dependencies (`Packages/manifest.json`)

| Package | Note |
|---|---|
| `cn.tuanjie.codely.bridge` 1.0.86 | Codely ↔ Unity bridge (required for Unity builtin tools) |
| `cn.tuanjie.ai.generators` | **local** `file:../.codely-cli/extensions/TJGenerators/Packages/cn.tuanjie.ai.generators` — TJGenerators AI asset generation |
| `com.besty.unity-skills` | **local, on external drive `H:/实训资料/AI相关资料/Unity-Skills-1.9.4/SkillsForUnity`** — ⚠ breaks if H: is absent |
| `com.unity.ai.navigation` 2.0.6 | NavMesh (scene currently has no baked NavMesh data) |
| `com.unity.nuget.newtonsoft-json` 3.2.2 | JSON config parsing |
| `com.unity.feature.development` 1.0.2, `collab-proxy` 2.12.4, `timeline` 1.8.6, `ugui` 2.0.0, `visualscripting` 1.8.0 + standard modules | Dev/feature set |

---

## 9. Building, Running & Testing

- **Run:** open `SampleScene.unity` → **Play**. With Codely: use `exec_runtime_script` (auto-enters Play) or `unity_editor play` / `stop`; drive gameplay through the game's own API (`WarrorController`, `Skill`, FSM), not simulated raw input.
- **No build scripts** exist (no `Build/` folder, no `PerformBuild` method). A batchmode build would have to be created first, e.g.:
  ```bash
  "C:/Program Files/Unity/Hub/Editor/2023.2.20f1c1/Editor/Unity.exe" -batchmode -quit \
    -projectPath . -executeMethod <YourBuildScript>.PerformBuild -logFile
  ```
- **No Unity Test Framework setup** (no test asmdefs; only Animancer ships asmdefs).
- **`BuffSystem.Tests/`** (project root, outside `Assets/`): xUnit sources (`BuffPoolTests.cs`, `BuffSystemTests.cs`, referencing `BattleFramework.Core`) — **not compiled by Unity and not wired to any csproj**; treat as standalone reference tests (run with `dotnet test` only after creating a csproj) or historical.
- **De facto verification:** Play-mode probes + `Debug.Log` evidence via `exec_runtime_script`; Editor console via `unity_console` (clear → act → read); GC/alloc scans via `Assets/Tools/scan_*.py` against `_run_*.txt` logs.

---

## 10. Version Control

- **No `.git` / VCS detected.** Project history is carried by manual `.bak.YYYY-MM-DD-tag` copies, `ConfigCsvBackup_*`, and `备份的包/` — do not delete these.
- If VCS is initialized, commit: `Assets/`, `Packages/manifest.json`, `ProjectSettings/`, root `*.md` docs, `.codelyignore`. Ignore: `Library/`, `Temp/`, `obj/`, `Logs/`, `UserSettings/`, `.idea/`, `Library/` equivalents in `.codely-cli/`, `sln`/`csproj` (Unity-generated).

---

## 11. Known Issues / TODO

- **19 of 23 skill effects are empty stubs** (only ImmedDamage/AddBuff/Sneer/Hure implemented) — next steps are documented in `战斗框架抽象方案.md`.
- Docs/comments referencing the old 27-column CSV are stale vs. current JSON schema.
- `com.besty.unity-skills` depends on an external `H:` drive path — fragile across machines.
- Two oddly-named stray files at root (`日期：2026-09-18　审查范围：A`, `审查依据：工程既定边界（受击表现不进`) are artifacts with truncated names — ignore them.
- Scene has an editor-placed `NYT` while `GameStart` spawns a runtime copy — possible duplicate hero at runtime; verify before relying on scene placement.
- Historical open items (from 2026-09-09 review; verify current state before acting): CD system not wired; `WarrorController.AutoFightUpdate` had hardcoded test skill 10003; legacy `m_CrossFade/RunAttB` branch issues for NPC chase.

## Codely Structured Memories

### User
- [2026-10-23 15:09:32] 用户正以本战斗框架作为面试作品求职（2026-10 面试时被面试官质疑"很少听到有人用 MVC 做战斗"）。已与用户对齐正名口径：框架不自称 MVC，统一表述为"数据驱动的分层战斗框架——配置/数值/决策/表现四层分离 + ISkillEffect 策略注册表 + Buff 标签数据驱动 + ICastClock 逻辑时钟预埋（为状态同步准备）"，与 GAS（GameplayEffect/GameplayTag 同构处）/ET 谱系对标；理由是 MVC 为请求-响应模型、不适合每帧 tick 的战斗模拟。**Why:** 面试口径不一致会让用户在反复准备中自相矛盾。**How to apply:** 未来帮用户准备面试、写简历或表述该框架时，一律沿用该口径，避免再说"MVC 做战斗"；可引用的工程亮点：零 GC 施法上下文（[CastCtx] 计数器实锤）、Buff 对象池+防重入、配置迁移 34493 字段等价性验证 diffs=0。
### Feedback

### Project

### Reference

