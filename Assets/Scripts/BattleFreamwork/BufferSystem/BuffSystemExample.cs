using UnityEngine;
using BattleFramework.Core;

/// <summary>
/// Buff系统使用示例
/// 将此脚本挂载到任意GameObject上查看效果
/// </summary>
public class BuffSystemExample : MonoBehaviour
{
    private ClassObjectPool<Buff> _globalPool = new ClassObjectPool<Buff>(maxcount: 200);
    private BuffSystemPooled _playerBuffSystem;
    private int _buffIdCounter = 1000;
  
    void Start()
    {
       

        // 2. 创建玩家Buff系统
        _playerBuffSystem = new BuffSystemPooled(ownerId: 1, buffPool: _globalPool);
        Debug.Log("玩家Buff系统已创建");

        // 3. 订阅事件
        _playerBuffSystem.OnBuffAdded += OnBuffAdded;
        _playerBuffSystem.OnBuffRemoved += OnBuffRemoved;
        _playerBuffSystem.OnBuffTick += OnBuffTick;

        // 4. 演示：添加各种Buff
        DemoAddBuffs();
    }

    void Update()
    {
        // 每帧更新Buff系统
        _playerBuffSystem.Update(Time.deltaTime);

        // 按键测试
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            AddAttackBuff();
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            AddPoisonBuff();
        }
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            AddStunBuff();
        }
        if (Input.GetKeyDown(KeyCode.C))
        {
            DispelDebuffs();
        }
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ShowBuffInfo();
        }
    }

    void DemoAddBuffs()
    {
        Debug.Log("=== 演示开始 ===");

        // 添加攻击力提升Buff
        _playerBuffSystem.AddBuff(BuffType.AttackBoost, _buffIdCounter++);

        // 添加中毒Buff（持续伤害）
        _playerBuffSystem.AddBuff(BuffType.Poisoned, _buffIdCounter++);
    }

    void AddAttackBuff()
    {
        _playerBuffSystem.AddBuff( BuffType.AttackBoost, _buffIdCounter++);
        Debug.Log("添加了攻击Buff（按1）");
    }

    void AddPoisonBuff()
    {
        _playerBuffSystem.AddBuff(BuffType.Poisoned, _buffIdCounter++);
        Debug.Log("添加了中毒Buff（按2）");
    }

    void AddStunBuff()
    {
        _playerBuffSystem.AddBuff(BuffType.Stunned, _buffIdCounter++);
        Debug.Log("添加了眩晕Buff（按3）");
    }

    void DispelDebuffs()
    {
        int count = _playerBuffSystem.DispelNegativeBuffs(count: 10);
        Debug.Log($"驱散了 {count} 个减益效果（按C）");
    }

    void ShowBuffInfo()
    {
        Debug.Log("=== 当前Buff状态 ===");
        Debug.Log(_playerBuffSystem.GetDebugInfo());
        Debug.Log($"可以移动: {_playerBuffSystem.CanMove()}");
        Debug.Log($"可以攻击: {_playerBuffSystem.CanAttack()}");
        Debug.Log($"可以施法: {_playerBuffSystem.CanCast()}");
        Debug.Log($"被控制: {_playerBuffSystem.IsControlled()}");
    }

    void OnBuffAdded(Buff buff)
    {
        Debug.Log($"<color=green>✓ 添加Buff: {buff._BuffData.Name} ({buff._BuffData.Type})</color>");
    }

    void OnBuffRemoved(Buff buff)
    {
        Debug.Log($"<color=red>✗ 移除Buff: {buff._BuffData.Name} ({buff._BuffData.Type})</color>");
    }

    void OnBuffTick(Buff buff)
    {
        Debug.Log($"<color=yellow>⚡ Buff触发: {buff._BuffData.Name} - 伤害 {buff._BuffData.EffectValue}</color>");
    }

    void OnDestroy()
    {
        // 取消事件订阅
        if (_playerBuffSystem != null)
        {
            _playerBuffSystem.OnBuffAdded -= OnBuffAdded;
            _playerBuffSystem.OnBuffRemoved -= OnBuffRemoved;
            _playerBuffSystem.OnBuffTick -= OnBuffTick;
        }
    }

    void OnGUI()
    {
        // 防止 Start 未执行时 OnGUI 提前调用导致空引用
        if (_playerBuffSystem == null || _globalPool == null)
            return;

        GUILayout.BeginArea(new Rect(10, 10, 400, 300));

        GUILayout.Label("=== Buff系统示例 ===");
        GUILayout.Label($"激活Buff数量: {_playerBuffSystem.ActiveBuffCount}");
        GUILayout.Space(10);

        GUILayout.Label("按键操作:");
        GUILayout.Label("  [1] 添加攻击Buff");
        GUILayout.Label("  [2] 添加中毒Buff");
        GUILayout.Label("  [3] 添加眩晕Buff");
        GUILayout.Label("  [C] 驱散减益");
        GUILayout.Label("  [空格] 显示详细信息");
        GUILayout.Space(10);

        GUILayout.Label("状态:");
        GUILayout.Label($"  可以移动: {_playerBuffSystem.CanMove()}");
        GUILayout.Label($"  可以攻击: {_playerBuffSystem.CanAttack()}");
        GUILayout.Label($"  可以施法: {_playerBuffSystem.CanCast()}");

        GUILayout.EndArea();
    }
}
