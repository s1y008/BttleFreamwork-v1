# -*- coding: utf-8 -*-
"""
T3：Skill.cs 接线
  ① ExecuteEffect 方法体：23 分支 switch → 注册表 + 三态
  ② 删除 4 个已迁走的方法体（ImmedDamage / OnHure / AddBuff / Sneer）

铁律：大文件多处块替换一律走脚本 —— 用「括号平衡扫描」定位方法边界，
每块命中数断言 ==1，写回后从磁盘重读复验，失败从备份还原。CRLF 保持。
"""
import io, sys, os, shutil, re

P = r"E:\Project\BttleFreamwork-v1\Assets\Scripts\BattleFreamwork\Skill.cs"
BAK = P + ".bak.2026-09-24-abstr"

src = io.open(P, encoding="utf-8", newline="").read().replace("\r\n", "\n")
orig = src


def find_block(text, sig):
    """定位一个方法：返回 (start_idx, end_idx)，含其上方紧邻的 /// 注释块。"""
    cnt = text.count(sig)
    if cnt != 1:
        raise AssertionError("签名命中 %d 次（期望 1）：%s" % (cnt, sig))
    i = text.index(sig)

    # 向前吸收紧邻的 /// 注释行（以及其上的空行不吸收）
    head = text[:i]
    hlines = head.split("\n")
    k = len(hlines) - 2          # i 位于某行行首，hlines 最后一个元素是 ''
    while k >= 0 and hlines[k].strip().startswith("///"):
        k -= 1
    start_line = k + 1
    start = sum(len(x) + 1 for x in hlines[:start_line])

    # 向后括号平衡
    p = text.index("{", i)
    depth = 0
    end = None
    while p < len(text):
        ch = text[p]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                end = p + 1
                break
        p += 1
    if end is None:
        raise AssertionError("括号未闭合：%s" % sig)
    return start, end


NEW_EXEC = """    private void ExecuteEffect(SkillEffectTypes effectType, CombatUnitController target)
    {
        // ★ 23 分支 switch 已删：效果实现全部迁往 ISkillEffect 策略类（见 SkillEffect/ 目录）。
        //   ★ 未注册的效果号 → **三态**（用户 2026-09-24 拍板，见方案 4.7）：
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
        }

        var ctx = new SkillEffectContext
        {
            Caster   = m_SkillCaseter,
            Target   = target,
            Skill    = this,
            BaseData = BaseData,
            SegIdx   = m_SegIdx
        };
        effect.Execute(ctx);
    }"""

# ① ExecuteEffect 替换
s, e = find_block(src, "    private void ExecuteEffect(SkillEffectTypes effectType, CombatUnitController target)")
print("[①] ExecuteEffect 原块 %d 字符 → 新块 %d 字符" % (e - s, len(NEW_EXEC)))
src = src[:s] + NEW_EXEC + src[e:]

# ② 删除 4 个已迁走的方法体
for sig, name in [
    ("    public void ImmedDamage(CombatUnitController target)", "ImmedDamage"),
    ("    private void OnHure(CombatUnitController target)", "OnHure"),
    ("    public void AddBuff(CombatUnitController target)", "AddBuff"),
    ("    public void Sneer(CombatUnitController target)", "Sneer"),
]:
    s, e = find_block(src, sig)
    print("[②] 删除 %-12s 块 %d 字符（含上方注释）" % (name, e - s))
    src = src[:s] + src[e:]

# 清理删除后残留的连续空行（≥3 行 → 2 行）
src = re.sub(r"\n{3,}", "\n\n", src)

io.open(P, "w", encoding="utf-8", newline="").write(src.replace("\n", "\r\n"))

# ── 写回复验 ──
chk = io.open(P, encoding="utf-8", newline="").read().replace("\r\n", "\n")
if NEW_EXEC not in chk:
    print("[FAIL] ExecuteEffect 新块不在磁盘中")
    shutil.copyfile(BAK, P)
    print("[ROLLBACK] 已还原")
    sys.exit(1)

for sig, name in [
    ("    public void ImmedDamage(CombatUnitController target)", "ImmedDamage"),
    ("    private void OnHure(CombatUnitController target)", "OnHure"),
    ("    public void AddBuff(CombatUnitController target)", "AddBuff"),
    ("    public void Sneer(CombatUnitController target)", "Sneer"),
]:
    if chk.count(sig) != 0:
        print("[FAIL] %s 仍残留" % name)
        shutil.copyfile(BAK, P)
        print("[ROLLBACK] 已还原")
        sys.exit(1)

print("[VERIFY] T3 全部落地")
print("  switch 关键字残留 =", chk.count("switch (effectType)"))
print("  case SkillEffectTypes 残留 =", chk.count("case SkillEffectTypes"))
print("  行数 =", chk.count("\n"), " CRLF =", io.open(P, encoding="utf-8", newline="").read().count("\r\n"))
