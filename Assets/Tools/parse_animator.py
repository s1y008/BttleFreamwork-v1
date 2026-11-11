# -*- coding: utf-8 -*-
"""
解析 Unity AnimatorController (.controller, YAML)，还原：
  1. 参数定义（名字 / 类型 / 默认值）
  2. 树形拓扑（支持子状态机，输出层级）
  3. 每个状态的出边转换（目标 + 条件 + ExitTime）
  4. 每个状态绑定的动画片段（按 guid 反查 .meta 得到 FBX 文件名）
  5. 诊断：哪些状态 / 子状态机没有任何入边

用法: python Assets/Tools/parse_animator.py <controller路径> [更多路径...]

为什么需要它：Animator Controller 里状态与转换只用 fileID 互指，且支持嵌套子状态机，
肉眼无法还原拓扑。只有把 fileID 解引用成人名，才能回答诸如
「敌人的受击动画到底有没有转换能进入」这类决定方案可行性的问题。
"""
import io
import os
import re
import sys

MODE = {'1': 'If', '2': 'IfNot', '3': 'Greater', '4': 'Less',
        '6': 'Equals', '7': 'NotEqual'}
# ⚠ Unity 的 AnimatorControllerParameterType 是**跳跃枚举**，不是 0/1/2/3 连续值：
#   Float=1, Int=3, Bool=4, Trigger=9。
#   2026-09-18 修正：此前写成 4=Trigger / 9=Bool（当成连续下标），
#   导致把 GBL 的 Die/Run（Bool）误报成 Trigger、把新建的 Hit（Trigger）误报成 Bool。
#   参数类型判错会直接误导「SetTrigger 能不能生效」的判断，属必须修的解析缺陷。
TYPE = {'1': 'Float', '3': 'Int', '4': 'Bool', '9': 'Trigger'}


def load_guid_names(root='Assets'):
    """扫 .meta 建立 guid -> 文件名，用于把 m_Motion 的 guid 还原成动画名"""
    m = {}
    for dp, dn, fn in os.walk(root):
        if '.codely-cli' in dp or 'Library' in dp:
            continue
        for f in fn:
            if not f.endswith('.meta'):
                continue
            p = os.path.join(dp, f)
            try:
                t = io.open(p, encoding='utf-8', errors='ignore').read()
            except Exception:
                continue
            g = re.search(r'^guid:\s*([0-9a-f]{32})', t, re.M)
            if g:
                m[g.group(1)] = f[:-5]
    return m


def split_blocks(text):
    """按 '--- !u!<classID> &<fileID>' 切块"""
    seq = re.findall(r'^--- !u!(\d+) &(-?\d+)[ \t]*$', text, re.M)
    chunks = re.split(r'^--- !u!\d+ &-?\d+[ \t]*$', text, flags=re.M)
    return [(seq[k][0], seq[k][1], chunks[k + 1]) for k in range(len(seq))]


class Ctrl(object):
    def __init__(self, path):
        self.path = path
        self.text = io.open(path, encoding='utf-8', errors='ignore').read()
        self.blocks = split_blocks(self.text)
        self.name = {}
        self.motion = {}
        self.params = []
        self.states = {}     # fid -> {'trans': [...], 'motion_guid': ...}
        self.sms = {}        # fid -> {'states': [], 'subs': [], 'default': fid}
        self.trans = {}      # fid -> dict
        self.guids = load_guid_names()
        self._parse()

    def _parse(self):
        for cid, fid, b in self.blocks:
            m = re.search(r'^  m_Name:[ \t]*(.*)$', b, re.M)
            if m:
                self.name[fid] = m.group(1).strip()

        # ---- 参数（缩进 2 的列表项）----
        lines = self.text.split('\n')
        in_p, cur = False, None
        for ln in lines:
            ind = len(ln) - len(ln.lstrip(' '))
            s = ln.strip()
            if s.startswith('m_AnimatorParameters:'):
                in_p = True
                continue
            if not in_p:
                continue
            if ind == 0:
                break
            if s.startswith('m_AnimatorLayers:'):
                break
            if s.startswith('- '):
                s = s[2:].strip()
                if s.startswith('m_Name:'):
                    cur = {'name': s.split(':', 1)[1].strip(), 'type': '?', 'def': ''}
                    self.params.append(cur)
                continue
            if cur is None:
                continue
            if s.startswith('m_Type:'):
                cur['type'] = TYPE.get(s.split(':', 1)[1].strip(), s.split(':', 1)[1].strip())
            elif s.startswith('m_DefaultFloat:') and cur['type'] == 'Float':
                cur['def'] = s.split(':', 1)[1].strip()
            elif s.startswith('m_DefaultInt:') and cur['type'] == 'Int':
                cur['def'] = s.split(':', 1)[1].strip()
            elif s.startswith('m_DefaultBool:') and cur['type'] == 'Bool':
                cur['def'] = 'true' if s.split(':', 1)[1].strip() == '1' else 'false'

        # ---- 状态 ----
        for cid, fid, b in self.blocks:
            if cid != '1102':
                continue
            d = {'trans': [], 'guid': None, 'speed': '1'}
            tr = re.search(r'^  m_Transitions:\s*$(.*?)(?=^  m_\w)', b, re.M | re.S)
            if tr:
                d['trans'] = re.findall(r'fileID:\s*(-?\d+)', tr.group(1))
            mo = re.search(r'm_Motion:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32})', b)
            if mo:
                d['guid'] = mo.group(2)
            sp = re.search(r'^  m_Speed:\s*([\d.-]+)', b, re.M)
            if sp:
                d['speed'] = sp.group(1)
            self.states[fid] = d

        # ---- 状态机 ----
        for cid, fid, b in self.blocks:
            if cid != '1107':
                continue
            d = {'states': [], 'subs': [], 'default': None}
            d['states'] = re.findall(r'^\s*- serializedVersion:.*?m_State:\s*\{fileID:\s*(-?\d+)\}',
                                     b, re.M | re.S)
            if not d['states']:
                d['states'] = re.findall(r'm_State:\s*\{fileID:\s*(-?\d+)\}', b)
            d['subs'] = re.findall(r'm_StateMachine:\s*\{fileID:\s*(-?\d+)\}', b)
            df = re.search(r'm_DefaultState:\s*\{fileID:\s*(-?\d+)\}', b)
            if df:
                d['default'] = df.group(1)
            anym = re.search(r'm_AnyState:\s*\{fileID:\s*(-?\d+)\}', b)
            d['any'] = anym.group(1) if anym else None
            # Entry 分支（1109）：从子状态机入口直接按条件分流到具体状态
            ent = re.search(r'^  m_EntryTransitions:\s*$(.*?)(?=^  m_\w)', b, re.M | re.S)
            d['entry'] = re.findall(r'fileID:\s*(-?\d+)', ent.group(1)) if ent else []
            anyt = re.search(r'^  m_AnyStateTransitions:\s*$(.*?)(?=^  m_\w)', b, re.M | re.S)
            d['anytrans'] = re.findall(r'fileID:\s*(-?\d+)', anyt.group(1)) if anyt else []
            self.sms[fid] = d

        # ---- 转换 ----
        # 1101 = AnimatorStateTransition（状态 → 目标，带 ExitTime / Duration）
        # 1109 = AnimatorTransition（子状态机 Entry → 目标，无 ExitTime，纯条件驱动）
        # ⚠ 2026-09-18 补：只认 1101 会把 Entry 分支整条漏掉，
        #   表现为「新建的 LightHit 明明有入边却被诊断成孤儿」。
        for cid, fid, b in self.blocks:
            if cid not in ('1101', '1109'):
                continue
            t = {'dst': None, 'dst_sm': None, 'conds': [], 'et': '0', 'ex': '0', 'dur': '0'}
            d = re.search(r'm_DstState:\s*\{fileID:\s*(-?\d+)\}', b)
            # ⚠ fileID: 0 表示「这一字段没指向任何东西」，必须当 None。
            #   此前 '0' 被当成合法 id，去查名字得到 '?'，于是「idle → Hit(子状态机)」被打印成「→ ?」。
            if d and d.group(1) != '0':
                t['dst'] = d.group(1)
            ds = re.search(r'm_DstStateMachine:\s*\{fileID:\s*(-?\d+)\}', b)
            if ds and ds.group(1) != '0':
                t['dst_sm'] = ds.group(1)
            et = re.search(r'm_HasExitTime:\s*(\d+)', b)
            if et:
                t['et'] = et.group(1)
            ex = re.search(r'm_ExitTime:\s*([\d.-]+)', b)
            if ex:
                t['ex'] = ex.group(1)
            du = re.search(r'm_TransitionDuration:\s*([\d.-]+)', b)
            if du:
                t['dur'] = du.group(1)
            # 条件：三字段一组。Unity 写盘顺序可能是 Mode→Event→Treshold，
            # 也可能是 Event→Mode→Treshold，两种都要认，否则会把带条件误报成「无条件」。
            cond_block = re.search(r'^  m_Conditions:.*?(?=^  m_\w)', b, re.M | re.S)
            cb = cond_block.group(0) if cond_block else b
            ev = re.findall(r'm_ConditionEvent:\s*(\S+)', cb)
            md = re.findall(r'm_ConditionMode:\s*(\d+)', cb)
            th = re.findall(r'm_EventTreshold:\s*([\d.eE+-]+)', cb)
            for k in range(min(len(ev), len(md))):
                t['conds'].append((ev[k], md[k], th[k] if k < len(th) else '0'))
            self.trans[fid] = t

    # ---------- 输出 ----------
    def tdesc(self, t):
        if t['conds']:
            return ' & '.join('%s %s%s' % (c[0], MODE.get(c[1], c[1]),
                                           '' if c[2] in ('0', '0.0') else ' ' + c[2])
                              for c in t['conds'])
        if t['et'] == '1':
            return 'ExitTime=%s' % t['ex']
        return '无条件(立即)'

    def clip_of(self, fid):
        g = self.states.get(fid, {}).get('guid')
        if not g:
            return '-'
        return self.guids.get(g, g[:8] + '...')

    def target_name(self, t):
        """把一条转换的目标解引用成人名（子状态机加前缀标记）"""
        if t['dst']:
            return self.name.get(t['dst'], '?')
        if t['dst_sm']:
            return '<进入子状态机 %s>' % self.name.get(t['dst_sm'], '?')
        return '(Exit)'

    def dump(self, smid, ind=0):
        sm = self.sms.get(smid)
        if not sm:
            return
        pad = '  ' * ind
        print('%s[SM] %s' % (pad, self.name.get(smid, '?')))
        for tid in sm.get('entry', []):
            t = self.trans.get(tid)
            if t:
                print('%s  Entry -> %-18s %s' % (pad, self.target_name(t), self.tdesc(t)))
        for tid in sm.get('anytrans', []):
            t = self.trans.get(tid)
            if t:
                print('%s  Any   -> %-18s %s' % (pad, self.target_name(t), self.tdesc(t)))
        for s in sm['states']:
            tag = ' <默认>' if sm['default'] == s else ''
            print('%s  - %-16s speed=%-4s clip=%s%s' %
                  (pad, self.name.get(s, '?'), self.states.get(s, {}).get('speed', '1'),
                   self.clip_of(s), tag))
            for tid in self.states.get(s, {}).get('trans', []):
                t = self.trans.get(tid)
                if not t:
                    continue
                print('%s      -> %-18s %s' % (pad, self.target_name(t), self.tdesc(t)))
        for sub in sm['subs']:
            self.dump(sub, ind + 1)

    def report(self):
        print('=== %s ===' % self.path)
        print('--- 参数 (%d) ---' % len(self.params))
        for p in self.params:
            d = ('  默认=%s' % p['def']) if p['def'] else ''
            print('   %-14s %-8s%s' % (p['name'], p['type'], d))
        print('--- 拓扑 ---')
        root = None
        m = re.search(r'm_AnimatorLayers:(.*?)(?=^--- !u!)', self.text, re.S | re.M)
        if m:
            rs = re.findall(r'm_StateMachine:\s*\{fileID:\s*(-?\d+)\}', m.group(1))
            if rs:
                root = rs[0]
        if root:
            self.dump(root)
        else:
            for fid in self.sms:
                self.dump(fid)
        # 诊断
        reachable = set()
        for t in self.trans.values():
            if t['dst']:
                reachable.add(t['dst'])
            if t['dst_sm']:
                reachable.add(t['dst_sm'])
        print('--- 诊断 ---')
        # 子状态机的「默认状态」不需要入边 —— 它是兜底入口，无入边也进得去。
        # 不区分会把 Hit 子树的默认状态 ContiHit 误报成孤儿，掩盖真正的断链。
        defaults = set(v['default'] for v in self.sms.values() if v.get('default'))
        orphans = [f for f in self.states if f not in reachable and f not in defaults]
        soft = [f for f in self.states if f not in reachable and f in defaults]
        # ⚠ 子状态机「进得去」有两种方式：有转换直接指向它，**或**有转换指向它内部的某个状态。
        #   只按前者判定会把 Skill 子状态机误报成无入边（idle → Skill20014 是指向内部状态的）。
        orphans_sm = [f for f in self.sms if f not in reachable and f != root
                      and not any(s in reachable for s in self.sms[f].get('states', []))]
        if soft:
            print('   ○ 无入边但为所在子状态机的默认状态（兜底入口，正常）:')
            for f in sorted(soft, key=lambda f: self.name.get(f, '?')):
                print('        - %s' % self.name.get(f, '?'))
        if orphans:
            print('   ★ 无入边状态（没有转换能到达，只能靠 Animator.Play 直接播）:')
            for f in sorted(orphans, key=lambda f: self.name.get(f, '?')):
                print('        - %s' % self.name.get(f, '?'))
        if orphans_sm:
            print('   ★★ 无入边子状态机（整棵子树都进不去）:')
            for f in sorted(orphans_sm, key=lambda f: self.name.get(f, '?')):
                n = len(self.sms[f]['states'])
                print('        - %s（内含 %d 个状态）' % (self.name.get(f, '?'), n))
        if not orphans and not orphans_sm:
            print('   所有状态与子状态机均有入边')
        print()


if __name__ == '__main__':
    if len(sys.argv) < 2:
        print('用法: python Assets/Tools/parse_animator.py <controller路径> [更多路径...]')
        sys.exit(1)
    for p in sys.argv[1:]:
        Ctrl(p).report()
