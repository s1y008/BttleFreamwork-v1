using System.Collections;
using System.Collections.Generic;
using BattleFramework.Core;
using UnityEngine;

public class TestEnemy : MonoBehaviour
{
    BuffSystemPooled  buff;

    public float hp = 5000;
    
    public TestPlayer player;

    /// <summary>
    /// 伤害见血
    /// </summary>
    /// <param name="demage"></param>
    public void ChangHP(float demage)
    {
        hp -= demage;
        Debug.LogError(hp+"::::::"+gameObject.name);
    }
    
    
    public void CreadBuff(ClassObjectPool<Buff> buffPool)
    {
        buff = new BuffSystemPooled(2, buffPool);
    }
    // Update is called once per frame
    void Update()
    {
        //敌人身上的Buff同样需要驱动（过期与Tick），否则挂上后永不过期
        if (buff != null)
        {
            buff.Update(Time.deltaTime);
        }
    }
}
