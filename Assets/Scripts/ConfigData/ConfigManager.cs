using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BattleFramework.Core;

public static class ConfigManager 
{
   private static EffectMsg _effectMsg=new EffectMsg();
   public static EffectData GetEffectData(int id)
   {
       return _effectMsg.GetData(id);
   }
   private static SkillDataManager  _SkillDataManager = new SkillDataManager();
   public static SkillData GetSkillData(int id)
   {
       return _SkillDataManager.GetData(id);
   }

   /// <summary>
   /// ★ 2026-10-10 弹道投递（投掷物方案 P1）：投掷物配置行 —— 懒加载同款（首查触发 ProjectileDataManager 内部加载）。
   /// </summary>
   private static ProjectileDataManager _ProjectileDataManager = new ProjectileDataManager();
   public static ProjectileData GetProjectileData(int id)
   {
       return _ProjectileDataManager.GetData(id);
   }

   /// <summary>
   /// 全部技能配置（只读遍历口）。
   /// 用途：SkillEffectRegistry 的加载期汇总要统计「CSV 配置了哪些效果号」，需遍历全表。
   /// ⚠ 首次调用会触发 CSV 加载（懒加载在 SkillDataManager 内部）。
   /// </summary>
   public static List<SkillData> GetAllSkillData()
   {
       return _SkillDataManager.GetAll();
   }
   
   private static AttackNpcDataManager _AttackNpcDataManager = new AttackNpcDataManager();
   public static AttackNpcData GetAttackNpcData(int id)
   {
       return _AttackNpcDataManager.GetData(id);
   }
   
   private static WarriorDataManager _WarriorDataManager = new WarriorDataManager();

   public static WarriorData GetWarriorData(int id)
   {
       return _WarriorDataManager.GetData(id);
   }
   
   
   private static  BuffMsg _buffMsg = new BuffMsg();

   public static BuffData GetBuffData(int id)
   {
       return _buffMsg.GetData(id);
   }
   public static BuffData GetBuffData(BuffType type)
   {
       return _buffMsg.GetData(type);
   }

   /// <summary>
   /// 按行为标签取代表配置行（BufferData.json 的 Tags 字段，见 BuffTags 常量）。
   /// 无匹配返回 null —— 调用方负责 LogError 指名，不做静默降级。
   /// </summary>
   public static BuffData GetBuffDataByTag(string tag)
   {
       return _buffMsg.GetDataByTag(tag);
   }

   /// <summary>
   /// 全部 Buff 配置行（只读遍历口）—— 供 Editor 探针（BuffContractCheck）/ 迁移验证工具使用。
   /// ⚠ 首次调用会触发 BufferData.json 加载（懒加载在 BuffMsg 内部）。
   /// </summary>
   public static List<BuffData> GetAllBuffData()
   {
       return _buffMsg.GetAll();
   }
   
}
