using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// JSON 配置装载统一入口（2026-09-28 CSV→JSON 迁移）。
///
/// 【它收口什么】全部配置表的 Resources 读取 + Newtonsoft 反序列化 + 「单行失败跳行」语义。
///   旧 CSV 解析器对脏行的处理是 LogError 后 continue（其余行照常加载）；
///   Newtonsoft 默认行为是单行坏数据炸整表 —— 这里经 Error 处理器恢复旧行为：
///   配置表局部脏数据报错指名，但不让整个游戏加载失败。
///
/// 【键名契约】JSON 顶层是行数组；每个对象的键名与领域类的公开成员名一一对应
///   （SkillData / AttackNpcData / WarriorData / EffectData / BuffData）。
///   枚举既接受 int 也接受枚举名字符串（Newtonsoft 两种都能反序列化）。
///
/// 【文件位置】Resources/Config/&lt;configName&gt;.json（TextAsset，UTF-8 无 BOM）。
///   ⚠ 同目录下不允许再存在同名 .csv —— Resources 按无扩展名键加载，同名双资产属于未定义行为。
/// </summary>
public static class JsonConfigLoader
{
    /// <summary>单行失败：LogError 指名 + Handled=true ⇒ Newtonsoft 跳过该行、继续后面的行（迁移自旧 CSV 解析器语义）。</summary>
    private static readonly JsonSerializerSettings s_RowErrorSettings = new JsonSerializerSettings
    {
        Error = (sender, args) =>
        {
            Debug.LogError("[JsonConfig] 配置行解析失败，已跳过该行: " + args.ErrorContext.Error.Message
                + "（path=" + args.ErrorContext.Path + "）");
            args.ErrorContext.Handled = true;
        }
    };

    /// <summary>
    /// 读取 Resources/Config/&lt;configName&gt;.json 并反序列化为 List&lt;T&gt;。
    /// 返回 null 表示整表不可用（文件缺失 / 顶层结构损坏）—— 已 LogError 指名，调用方按「空表」处理，不静默降级。
    /// </summary>
    public static List<T> LoadRows<T>(string configName)
    {
        TextAsset asset = Resources.Load<TextAsset>("Config/" + configName);
        if (asset == null)
        {
            Debug.LogError("[JsonConfig] 找不到配置文件 Resources/Config/" + configName + ".json");
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<List<T>>(asset.text, s_RowErrorSettings);
        }
        catch (Exception ex)
        {
            // 单行错误已被 Error 处理器吃掉；走到这里说明顶层结构损坏（非单行问题）
            Debug.LogError("[JsonConfig] " + configName + ".json 整体反序列化失败: " + ex.Message);
            return null;
        }
    }
}
