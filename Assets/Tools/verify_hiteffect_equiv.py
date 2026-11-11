# -*- coding: utf-8 -*-
"""受击特效 id 取值逻辑「全表等价性」验证。

【用途】
  证明「受击方反查 attacker.m_CurSkill（改造前）」与「攻击方 Skill 按段算好后传参（v5 P10 起）」
  对**全表每一个技能的每一个段号**都取到相同的特效 id。
  受击特效链路在 Play 日志里原本不可观测（v5 P10 才补的 [HitEffectCalc]/[HitEffect]），
  所以静态全表比对是比肉眼/单例实测更硬的证据。

【为什么这么比就够】
  两条路径的输入完全相同（同一个 Skill 的同一份 BaseData.HitEffectIds、同一个段号），
  差异只可能出在边界判断上，所以把段号从 -1 扫到 SegCount+2 即可覆盖全部边界分支。

【关键脏数据规则】
  CommonHelper.Str2IntList("") 会把空串解析成**含一个 0 的 List**（而非空表）——
  本脚本按此规则模拟，否则会得出假差异。

用法：直接跑，无需参数。输出「差异数 = 0」即证明等价。
"""
import os

CSV = os.path.normpath(os.path.join(
    os.path.dirname(os.path.abspath(__file__)), '..', 'Resources', 'Config', 'SkillData.csv'))


def str2int(s):
    s = (s or '').strip()
    try:
        return int(s)
    except Exception:
        return 0


def str2intlist(s):
    # 模拟 CommonHelper.Str2IntList：按 ';' 切分，逐项 Str2Int（空串 -> 0）
    return [str2int(x) for x in ('' if s is None else s).split(';')]


def old_id(seg_idx, ids):
    """改造前：OnDamaged 内反查 attacker.m_CurSkill 取受击特效 id"""
    if seg_idx > 0 and ids is not None and seg_idx <= len(ids):
        return ids[seg_idx - 1]
    return 0


def new_id(seg_idx, ids):
    """改造后：Skill.GetHitEffectIdForSegment(segIdx)"""
    if ids is None or len(ids) == 0 or seg_idx <= 0 or seg_idx > len(ids):
        return 0
    return ids[seg_idx - 1]


def main():
    lines = open(CSV, encoding='utf-8-sig').read().splitlines()
    hdr = lines[0].split(',')
    idx = {h: i for i, h in enumerate(hdr)}
    i_id, i_hit, i_seg = idx['ID'], idx['HitEffectIds'], idx['SegCount']

    print('CSV             =', CSV)
    print('CSV 列数        =', len(hdr))
    print('HitEffectIds 列 =', i_hit, '(0-based)')
    print('SegCount 列     =', i_seg, '(0-based)')
    print()

    diff, withval, cell_total, nrow = [], [], 0, 0

    for ln in lines[1:]:
        if not ln.strip():
            continue
        p = ln.split(',')
        if not p[0].strip():
            continue
        nrow += 1
        ids = str2intlist(p[i_hit]) if len(p) > i_hit else []
        segcount = str2int(p[i_seg]) if len(p) > i_seg else 0

        # 段号边界全扫：-1 / 0 / 1..SegCount / SegCount+1 / SegCount+2
        for seg in range(-1, segcount + 3):
            cell_total += 1
            a, b = old_id(seg, ids), new_id(seg, ids)
            if a != b:
                diff.append((p[0], seg, ids, a, b))

        valid = [x for x in ids if x > 0]
        if valid:
            withval.append((p[0], ids, segcount, valid))

    print('技能行数            =', nrow)
    print('比对单元格数        =', cell_total)
    print('差异数              =', len(diff))
    for d in diff[:30]:
        print('   DIFF id=%s seg=%s ids=%s old=%s new=%s' % d)
    print()
    print('HitEffectIds 真配了 >0 值的技能数 =', len(withval))
    for w in withval:
        print('   id=%-7s ids=%-24s SegCount=%-3s 有效值=%s' % w)

    print()
    print('RESULT =', 'EQUIVALENT' if not diff else 'DIFF FOUND')


if __name__ == '__main__':
    main()
