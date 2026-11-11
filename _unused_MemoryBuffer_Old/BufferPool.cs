using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BattleFramework.Core
{
    /// <summary>
    /// 缓冲区池 - 高效的内存复用系统
    /// </summary>
    public class BufferPool : IDisposable
    {
        private readonly ConcurrentBag<BufferSystem>[] _pools;
        private readonly int[] _poolSizes;
        private readonly int _maxPoolSize;
        private int _totalAllocated;
        private int _totalPooled;
        private bool _disposed;

        /// <summary>
        /// 获取已分配的总缓冲区数量
        /// </summary>
        public int TotalAllocated => _totalAllocated;

        /// <summary>
        /// 获取池中缓冲区数量
        /// </summary>
        public int TotalPooled => _totalPooled;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="maxPoolSize">每个大小级别的最大池容量</param>
        public BufferPool(int maxPoolSize = 100)
        {
            _maxPoolSize = maxPoolSize;

            // 预定义常用的缓冲区大小: 1KB, 4KB, 16KB, 64KB, 256KB, 1MB, 4MB
            _poolSizes = new int[] { 1024, 4096, 16384, 65536, 262144, 1048576, 4194304 };
            _pools = new ConcurrentBag<BufferSystem>[_poolSizes.Length];

            for (int i = 0; i < _pools.Length; i++)
            {
                _pools[i] = new ConcurrentBag<BufferSystem>();
            }
        }

        /// <summary>
        /// 从池中租用缓冲区
        /// </summary>
        public BufferSystem Rent(int minimumSize)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BufferPool));

            if (minimumSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(minimumSize));

            // 查找合适的池
            int poolIndex = GetPoolIndex(minimumSize);

            if (poolIndex >= 0 && _pools[poolIndex].TryTake(out BufferSystem buffer))
            {
                Interlocked.Decrement(ref _totalPooled);

                // 重置缓冲区状态
                buffer.Clear();
                buffer.ClearFlags(BufferFlags.Dirty | BufferFlags.Error);
                buffer.SetFlag(BufferFlags.Pooled);

                return buffer;
            }

            // 池中没有可用缓冲区，创建新的
            int actualSize = poolIndex >= 0 ? _poolSizes[poolIndex] : GetNextPowerOfTwo(minimumSize);
            var newBuffer = new BufferSystem(actualSize, BufferFlags.Pooled);

            Interlocked.Increment(ref _totalAllocated);
            return newBuffer;
        }

        /// <summary>
        /// 归还缓冲区到池中
        /// </summary>
        public void Return(BufferSystem buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (_disposed)
            {
                buffer.Dispose();
                return;
            }

            // 检查缓冲区状态
            if (buffer.HasFlag(BufferFlags.Disposed))
                return;

            if (buffer.HasFlag(BufferFlags.Error))
            {
                // 有错误的缓冲区直接丢弃
                buffer.Dispose();
                Interlocked.Decrement(ref _totalAllocated);
                return;
            }

            // 查找对应的池
            int poolIndex = GetPoolIndex(buffer.Capacity);

            if (poolIndex >= 0 && _pools[poolIndex].Count < _maxPoolSize)
            {
                // 清理并归还到池中
                buffer.Clear();
                buffer.ClearFlags(BufferFlags.Locked | BufferFlags.Dirty | BufferFlags.Reading |
                                 BufferFlags.Writing | BufferFlags.AsyncOperation);

                if (buffer.HasFlag(BufferFlags.Pinned))
                    buffer.Unpin();

                _pools[poolIndex].Add(buffer);
                Interlocked.Increment(ref _totalPooled);
            }
            else
            {
                // 池已满或大小不匹配，直接释放
                buffer.Dispose();
                Interlocked.Decrement(ref _totalAllocated);
            }
        }

        /// <summary>
        /// 租用临时缓冲区（使用using模式自动归还）
        /// </summary>
        public PooledBuffer RentPooled(int minimumSize)
        {
            var buffer = Rent(minimumSize);
            return new PooledBuffer(this, buffer);
        }

        /// <summary>
        /// 清空所有池
        /// </summary>
        public void Clear()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BufferPool));

            for (int i = 0; i < _pools.Length; i++)
            {
                while (_pools[i].TryTake(out BufferSystem buffer))
                {
                    buffer.Dispose();
                    Interlocked.Decrement(ref _totalAllocated);
                    Interlocked.Decrement(ref _totalPooled);
                }
            }
        }

        /// <summary>
        /// 修剪池（移除部分缓冲区以释放内存）
        /// </summary>
        public int Trim(int targetCount = 0)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BufferPool));

            int trimmed = 0;

            for (int i = 0; i < _pools.Length; i++)
            {
                int currentCount = _pools[i].Count;
                int toTrim = Math.Max(0, currentCount - targetCount);

                for (int j = 0; j < toTrim; j++)
                {
                    if (_pools[i].TryTake(out BufferSystem buffer))
                    {
                        buffer.Dispose();
                        Interlocked.Decrement(ref _totalAllocated);
                        Interlocked.Decrement(ref _totalPooled);
                        trimmed++;
                    }
                }
            }

            return trimmed;
        }

        /// <summary>
        /// 获取池索引
        /// </summary>
        private int GetPoolIndex(int size)
        {
            for (int i = 0; i < _poolSizes.Length; i++)
            {
                if (size <= _poolSizes[i])
                    return i;
            }
            return -1; // 超出预定义大小
        }

        /// <summary>
        /// 获取下一个2的幂次方
        /// </summary>
        private int GetNextPowerOfTwo(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;
            return value;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Clear();
        }
    }

    /// <summary>
    /// 池化缓冲区包装器 - 支持using自动归还
    /// </summary>
    public struct PooledBuffer : IDisposable
    {
        private readonly BufferPool _pool;
        private BufferSystem _buffer;
        private bool _disposed;

        internal PooledBuffer(BufferPool pool, BufferSystem buffer)
        {
            _pool = pool;
            _buffer = buffer;
            _disposed = false;
        }

        /// <summary>
        /// 获取缓冲区
        /// </summary>
        public BufferSystem Buffer => _buffer ?? throw new ObjectDisposedException(nameof(PooledBuffer));

        public void Dispose()
        {
            if (_disposed || _buffer == null)
                return;

            _disposed = true;
            _pool.Return(_buffer);
            _buffer = null;
        }
    }
}
