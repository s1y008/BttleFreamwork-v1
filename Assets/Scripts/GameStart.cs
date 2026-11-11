using System.Collections.Generic;
using BattleFramework.Core;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;


public class GameStart : MonoBehaviour
{
  
    NavMeshAgent  m_Agent;

    /// <summary>
    /// Buff对象池
    /// 声明为字段但不在初始化器里new：字段初始化器在编辑器反序列化时也会执行，白建200个对象
    /// </summary>
    ClassObjectPool<Buff>  m_BuffPool = null;
    
    void Start()
    {
        CreadHero();
        CreadNPC();

      // m_Agent = GetComponent<NavMeshAgent>();
      // m_Agent.SetDestination(Vector3.forward*20);

      #region 测试代码测试的是BUFFER
     /*
      //运行时才创建池
      if (m_BuffPool == null)
      {
          m_BuffPool = new ClassObjectPool<Buff>(200);
      }

      GameObject player = GameObject.CreatePrimitive(PrimitiveType.Cube);

      TestPlayer temp=  player.AddComponent<TestPlayer>();
      temp.CreadBuff(m_BuffPool);
      
      
      
      GameObject enemy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
    
      TestEnemy go=  enemy.AddComponent<TestEnemy>();
      go.CreadBuff(m_BuffPool);

      //给玩家脚本中的敌人引用赋值
      temp.enemy = go;
      */

      #endregion
     

    }
        
    


    private void CreadNPC()
    {
     
        GameObject go = Resources.Load<GameObject>("Model/Enemy/GBL");
        GameObject hero=  GameObject.Instantiate(go, new Vector3(0, 0, 3), Quaternion.identity);
        AttackNpcController warrorController= hero.AddComponent<AttackNpcController>();

        AttackNpc herodata = new AttackNpc(10003001);
        warrorController.CurCombatUnit=herodata;
    }

    void CreadHero()
    {
        GameObject go = Resources.Load<GameObject>("Model/Player/NYT");
      GameObject hero=  GameObject.Instantiate(go, new Vector3(0, 0, 0), Quaternion.identity);
      WarrorController warrorController= hero.AddComponent<WarrorController>();

      Warrior herodata = new Warrior(1001);
      warrorController.CurCombatUnit=herodata;


    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
