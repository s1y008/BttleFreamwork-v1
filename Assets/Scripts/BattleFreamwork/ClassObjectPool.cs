using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 类对象池  
/// </summary>
/// <typeparam name="T"></typeparam>
public class ClassObjectPool<T> where T :class, new()  
{
    /// <summary>
    /// 池   item
    /// </summary>
    protected Stack<T> m_Pool = new Stack<T>();//Test
    /// <summary>
    /// 最大对象个数，<=0表示不限个数
    /// </summary>
    protected int m_MaxCount = 0;   //500 500, 
    /// <summary>
    /// 没有回收的对象个数
    /// </summary>
    protected int m_NoRecycleCount = 0;

#if UNITY_EDITOR
    /// <summary>
    /// 编辑器专用：池内对象登记，用于检测重复回收
    /// </summary>
    private readonly HashSet<T> m_EditorInPool = new HashSet<T>();
#endif

    public ClassObjectPool(int maxcount)
    {
        m_MaxCount = maxcount;
        for (int i = 0; i < maxcount; i++)
        {
            T item = new T();
            m_Pool.Push(item);
#if UNITY_EDITOR
            m_EditorInPool.Add(item);
#endif
            //m_Pool.Push(Activator.CreateInstance<T>());//反射创建一个类
        }
    }

    /// <summary>
    /// 从池里面取类对象
    /// </summary>
    /// <param name="creatIfPoolEmpty">如果为空是否new出来</param>
    /// <returns></returns>
    public T Spawn(bool creatIfPoolEmpty)
    {
        if (m_Pool.Count > 0)
        {
            T rtn = m_Pool.Pop();
#if UNITY_EDITOR
            m_EditorInPool.Remove(rtn);
#endif
            if (rtn == null)
            {
                if (creatIfPoolEmpty)
                    rtn = new T();
            }
            m_NoRecycleCount++;
            return rtn;
        }
        else
        {
            if (creatIfPoolEmpty)
            {
                T rtn = new T();
                m_NoRecycleCount++;
                return rtn;
            }
        }

        return null;
    }

    /// <summary>
    /// 回收类对象
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public bool Recycle(T obj)
    {
        if (obj == null)
            return false;

#if UNITY_EDITOR
        //重复回收检测：同一对象两次入池会让池中出现重复引用，Spawn会返回同一对象造成数据错乱
        if (m_EditorInPool.Contains(obj))
        {
            Debug.LogError("[ClassObjectPool] 检测到重复回收：" + typeof(T).Name);
            return false;
        }
#endif

        //池满直接拒绝：不Push、计数不动
        //(原实现先减计数再判满导致计数漂移；obj=null改不了调用方引用，属无效代码已删除)
        if (m_MaxCount > 0 && m_Pool.Count >= m_MaxCount)
        {
            return false;
        }

#if UNITY_EDITOR
        m_EditorInPool.Add(obj);
#endif
        m_NoRecycleCount--;
        m_Pool.Push(obj);
        return true;
    }
}
