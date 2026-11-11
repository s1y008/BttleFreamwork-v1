

## Codely Structured Memories

### User

### Feedback
- [2026-09-08 08:35:42] 本项目存在其他AI并发修改：2026-09-07会话中实测，分析阶段读到的StringConst(305行)/OnDizz/OnSilence/WarrorController.OnDead在数十分钟内被外部AI改动（OnDizz/OnSilence被改为走Buff系统、StringConst被精简到31行）。**Why:** 用户曾让另一个AI修复整个战斗系统，其改动与我的编辑可能交错发生。**How to apply:** 在本项目做任何编辑前，必须用最新read_file重读目标区域核对old_string，不可信任同会话早前的读取结果；两次读取内容不一致时直接以最新为准，编辑后建议grep验证关键接线未被覆盖。

### Project
- [2026-10-23 15:09:32] [已过时，留档] 本条原记录 2026-09-08 时的框架状态与四个漏洞（OnSkillCase 反射链路缺失/OnHeal·AddBuff 空桩/GetSkill(10003) 硬编码/调试日志），绝大部分已在后续会话解决：反射路径已删除，攻击链已改为 AttackState 段号驱动（TriggerSegmentHit → SkillHit(segIdx)），配置已迁移 JSON。**Why:** 避免未来会话按此旧账误修（『下一步修漏洞①②』等指令已作废）。**How to apply:** 当前框架状态一律以项目根 CODELY.md（§11 Known Issues）与《战斗框架抽象方案.md》为准。




- [2026-10-23 15:09:32] [已失效 2026-09-28] 旧『CRLF 铁律』不再适用：Resources/Config/ 下 5 个 CSV 已全部迁移为 JSON（JsonConfigLoader + Newtonsoft 反序列化，键名=领域类成员名），依赖 Split('\r') 的脆弱 CSV 解析器（BuffMsg/EffectMsg）已随迁移删除，CSV 原件（含 .meta）封存于项目根 ConfigCsvBackup_2026-09-28/。**Why:** BuffMsg 已重写为 JSON 结构化加载，CRLF 约束失去载体。**How to apply:** 不再需要为 Resources/Config 写 \r\n；仅当未来重引入 CSV 解析时，先做 \r\n 归一化再 split（SkillData 旧写法有先例）。


### Reference

