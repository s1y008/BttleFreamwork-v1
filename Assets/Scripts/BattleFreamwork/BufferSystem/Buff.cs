using System;
using System.Collections.Generic;
using UnityEngine;


namespace BattleFramework.Core
{
   
    /// <summary>
    /// Buff数据 - 单个Buff的完整信息
    /// </summary>
    public class Buff
    {
       
        /// <summary>剩余时间（秒）</summary>
        public float RemainingTime { get; set; }

        /// <summary>当前叠加层数</summary>
        public int StackCount { get; set; } = 1;


        /// <summary>下次触发时间</summary>        
        public float NextTickTime { get; set; }

        /// <summary>施加者ID</summary>
        public int CasterId { get; set; }


        /// <summary>是否激活</summary>
        public bool IsActive { get; set; }

        /// <summary>创建时间戳</summary>
        public float CreatedTime { get; set; }
            
        /// <summary>
        /// buff对应的配置文件数据
        /// </summary>
        public BuffData  _BuffData { get; set; }
        public Buff()
        {
            //新建即未激活：由BuffSystemPooled.AddBuff显式置true（池化语义：出池才是激活态）
            IsActive = false;
            StackCount = 1;
            RemainingTime = -1;
        }

        /// <summary>
        /// 重置当前脚本的数据，为了回收到对象池
        /// </summary>
        public void Recycle()
        {
            RemainingTime = -1;
            StackCount = 1;
            NextTickTime = 0;
            CasterId = 0;
            IsActive = false;
            CreatedTime = 0;
            _BuffData = null;
        }
        /// <summary>
        /// 是否已过期
        /// </summary>
        public bool IsExpired()
        {
            //已被回收的Buff（_BuffData为null）一律视为已过期
            //事件回调返回后外部持有的引用即失效，防止失效引用被误判
            if (_BuffData == null)
                return true;
            //如果持续时间小于0，则表示当前的BUFF永久不会过期
            if (_BuffData.Duration < 0) 
                return false; 
            //判断剩余时间小于=0  则表示当前BUFF失效
            return RemainingTime <= 0;
        }

        /// <summary>
        /// 是否需要触发Tick效果
        /// </summary>
        public bool ShouldTick(float currentTime)   //20    8.50  2  =8.52  8.52.30
        {
            
            if (_BuffData == null || _BuffData.TickInterval <= 0) 
                return false;
            return currentTime >= NextTickTime;
        }
    }
}
