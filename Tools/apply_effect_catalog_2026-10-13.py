# -*- coding: utf-8 -*-
"""
给 Assets/Editor/SkillEditor/SkillFieldMeta.cs 追加 SkillEffectTypeCatalog（效果号中文目录）。

纪律（项目铁律 7）：
  · 改 .cs 一律走 Python 脚本：old 命中数断言 == 1 + 幂等 + 写回后重读复验。
  · 幂等判据 = new 已存在即跳过。
"""
import io, os, sys, time

PATH = r"E:\Project\BttleFreamwork-v1\Assets\Editor\SkillEditor\SkillFieldMeta.cs"

ANCHOR = "    public static class TimelinePalette"

NEW_BLOCK = '''    /// <summary>
    /// ★ 效果号（SkillEffectTypes）的中文目录 —— 界面下拉与「未实现」标注的**唯一真相源**。
    ///
    /// 【为什么中文名必须手工维护一份】
    ///   Skill.cs 里的中文只写在**注释**里（如 `HitOut = 5, //浮空`），反射拿不到；
    ///   而「5 是浮空、10 是拉近」这类语义只有人知道 —— 这恰恰是契约层存在的理由。
    ///
    /// 【为什么「是否未实现」不在本表写死】
    ///   KnownStubIds 会随着填桩不断减少，在这里抄一份就会**立刻腐化**成假信息。
    ///   所以一律实时问 SkillEffectRegistry.IsKnownStub（运行时的唯一真相源）。
    ///   ⇒ 填完某个桩却忘了从 KnownStubIds 里删，界面会一直标红 —— 那正是**它该报的错**。
    ///
    /// 【为什么标红是硬要求】
    ///   19 个效果号目前是空桩（打出去没反应）。若不标红，策划选中「5 浮空」后
    ///   会得到「配了却什么都没发生」—— 属于铁律 5 明令禁止的静默降级。
    /// </summary>
    public static class SkillEffectTypeCatalog
    {
        /// <summary>效果号 → 中文名。下标 = SkillEffectTypes 的数值（0~22）。</summary>
        private static readonly string[] _labels = new string[]
        {
            "立即伤害",       // 0  ImmedDamage
            "立即治疗",       // 1  ImmedHeal
            "添加Buff",       // 2  AddBuff
            "嘲讽",           // 3  Sneer
            "僵直",           // 4  Rigor
            "浮空",           // 5  HitOut
            "受击后退",       // 6  HitBack
            "击飞(抛物线)",   // 7  HitOutDown
            "受击倒地",       // 8  HitDown
            "眩晕",           // 9  Dizz
            "拉近",           // 10 ForwardMove
            "持续后退受击",   // 11 OneUnitHitBack
            "设置单位可用",   // 12 OnSetUnitEnable
            "暴力抓取",       // 13 ViolenceToGrab
            "浮空受击",       // 14 AirHit
            "倒地持续受击",   // 15 DownHit
            "单位移动",       // 16 OnUnitMove
            "移动到目标并弹跳", // 17 OnUnitMoveTargetAndJump
            "移动到目标",     // 18 OnUnitMoveTarget
            "僵直(单体)",     // 19 Frozen
            "测试旋转",       // 20 SetRotate
            "浮空受击(玩家)", // 21 AirHitForPlayer
            "受击表现"        // 22 Hure
        };

        /// <summary>效果号总数（SkillEffectTypes 共 23 个值：0~22）。</summary>
        public static int Count { get { return _labels.Length; } }

        /// <summary>
        /// 取中文名。越界返回空串 —— 越界的效果号是配置错误，
        /// 由 <see cref="SkillEffectRegistry"/> 在运行时报错指名，本类不越俎代庖。
        /// </summary>
        public static string LabelOf(int value)
        {
            if (value < 0 || value >= _labels.Length) return "";
            return _labels[value];
        }

        /// <summary>下拉里显示的完整文本：「5 浮空」；未实现则追加「（未实现）」。</summary>
        public static string OptionLabel(int value)
        {
            string name = LabelOf(value);
            if (string.IsNullOrEmpty(name)) name = "未知";
            return value + " " + name + (IsStub(value) ? "（未实现）" : "");
        }

        /// <summary>
        /// 该效果号当前是不是**空桩**（配了也不会有任何反应）。
        /// 实时问 SkillEffectRegistry —— 不在此处维护副本，避免 KnownStubIds 变动后信息腐化。
        /// </summary>
        public static bool IsStub(int value)
        {
            return SkillEffectRegistry.IsKnownStub(value);
        }
    }

'''

def read_text(p):
    with io.open(p, "r", encoding="utf-8-sig") as f:
        return f.read()

def write_text(p, s):
    with io.open(p, "w", encoding="utf-8", newline="\n") as f:
        f.write(s)

def main():
    if not os.path.exists(PATH):
        print("FAIL 文件不存在: " + PATH)
        return 1

    src = read_text(PATH)

    # 幂等：已经加过就跳过
    if "class SkillEffectTypeCatalog" in src:
        print("SKIP 已存在 SkillEffectTypeCatalog —— 幂等跳过")
        return 0

    cnt = src.count(ANCHOR)
    if cnt != 1:
        print("FAIL 锚点命中 %d 次（要求 == 1）: %s" % (cnt, ANCHOR))
        return 1

    out = src.replace(ANCHOR, NEW_BLOCK + ANCHOR, 1)

    # 备份（时间戳按「改动后状态」命名）
    stamp = time.strftime("%Y-%m-%d-effectcatalog")
    bak = PATH + ".bak." + stamp
    if not os.path.exists(bak):
        write_text(bak, src)

    write_text(PATH, out)

    # 复验
    again = read_text(PATH)
    ok = ("class SkillEffectTypeCatalog" in again) and again.count(ANCHOR) == 1
    print("VERIFY-1 %s | len=%d" % ("OK" if ok else "FAIL", len(again)))
    if not ok:
        return 1

    # 延迟二次复验（磁盘曾静默回退）
    time.sleep(2)
    again2 = read_text(PATH)
    ok2 = "class SkillEffectTypeCatalog" in again2
    print("VERIFY-2 %s | len=%d" % ("OK" if ok2 else "FAIL", len(again2)))
    return 0 if ok2 else 1

if __name__ == "__main__":
    sys.exit(main())
