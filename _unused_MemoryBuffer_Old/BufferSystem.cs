using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BattleFramework.Core
{
    /// <summary>
    /// 缓冲区状态标志位枚举
    /// </summary>
    [Flags]
    public enum BufferFlags : uint
    {
        None = 0,
        /// <summary>已分配</summary>
        Allocated = 1 << 0,
        /// <summary>已锁定（不可释放）</summary>
        Locked = 1 << 1,
        /// <summary>脏数据（需要同步）</summary>
        Dirty = 1 << 2,
        /// <summary>正在读取</summary>
        Reading = 1 << 3,
        /// <summary>正在写入</summary>
        Writing = 1 << 4,
        /// <summary>共享模式</summary>
        Shared = 1 << 5,
        /// <summary>固定内存（pinned）</summary>
        Pinned = 1 << 6,
        /// <summary>压缩模式</summary>
        Compressed = 1 << 7,
        /// <summary>加密模式</summary>
        Encrypted = 1 << 8,
        /// <summary>可回收</summary>
        Recyclable = 1 << 9,
        /// <summary>池化管理</summary>
        Pooled = 1 << 10,
        /// <summary>临时缓冲区</summary>
        Temporary = 1 << 11,
        /// <summary>持久化缓冲区</summary>
        Persistent = 1 << 12,
        /// <summary>异步操作中</summary>
        AsyncOperation = 1 << 13,
        /// <summary>错误状态</summary>
        Error = 1 << 14,
        /// <summary>已释放</summary>
        Disposed = 1 << 15
    }

    /// <summary>
    /// 高效的基于位枚举的缓冲区系统
    /// </summary>
    public class BufferSystem : IDisposable
    {
        private byte[] _data;
        private BufferFlags _flags;
        private int _capacity;
        private int _length;
        private int _position;
        private GCHandle _pinnedHandle;
        private readonly object _lockObject = new object();

        /// <summary>
        /// 获取缓冲区容量
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// 获取或设置数据长度
        /// </summary>
        public int Length
        {
            get => _length;
            set
            {
                if (value < 0 || value > _capacity)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _length = value;
            }
        }

        /// <summary>
        /// 获取或设置当前位置
        /// </summary>
        public int Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _length)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        /// <summary>
        /// 获取缓冲区标志
        /// </summary>
        public BufferFlags Flags => _flags;

        /// <summary>
        /// 构造函数
        /// </summary>
        public BufferSystem(int capacity, BufferFlags initialFlags = BufferFlags.None)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _capacity = capacity;
            _data = new byte[capacity];
            _flags = initialFlags | BufferFlags.Allocated;
            _length = 0;
            _position = 0;
        }

        #region 标志位操作

        /// <summary>
        /// 设置标志位
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFlag(BufferFlags flag)
        {
            _flags |= flag;
        }

        /// <summary>
        /// 清除标志位
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearFlag(BufferFlags flag)
        {
            _flags &= ~flag;
        }

        /// <summary>
        /// 切换标志位
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ToggleFlag(BufferFlags flag)
        {
            _flags ^= flag;
        }

        /// <summary>
        /// 检查标志位是否设置
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasFlag(BufferFlags flag)
        {
            return (_flags & flag) == flag;
        }

        /// <summary>
        /// 检查是否有任一标志位
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasAnyFlag(BufferFlags flags)
        {
            return (_flags & flags) != 0;
        }

        /// <summary>
        /// 批量设置标志位
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFlags(BufferFlags flags)
        {
            _flags |= flags;
        }

        /// <summary>
        /// 批量清除标志位
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearFlags(BufferFlags flags)
        {
            _flags &= ~flags;
        }

        #endregion

        #region 缓冲区操作

        /// <summary>
        /// 写入数据
        /// </summary>
        public int Write(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException();

            lock (_lockObject)
            {
                if (HasFlag(BufferFlags.Locked))
                    throw new InvalidOperationException("Buffer is locked");

                SetFlag(BufferFlags.Writing);
                try
                {
                    int available = _capacity - _position;
                    int bytesToWrite = Math.Min(count, available);

                    Buffer.BlockCopy(buffer, offset, _data, _position, bytesToWrite);
                    _position += bytesToWrite;
                    _length = Math.Max(_length, _position);

                    SetFlag(BufferFlags.Dirty);
                    return bytesToWrite;
                }
                finally
                {
                    ClearFlag(BufferFlags.Writing);
                }
            }
        }

        /// <summary>
        /// 读取数据
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException();

            lock (_lockObject)
            {
                SetFlag(BufferFlags.Reading);
                try
                {
                    int available = _length - _position;
                    int bytesToRead = Math.Min(count, available);

                    Buffer.BlockCopy(_data, _position, buffer, offset, bytesToRead);
                    _position += bytesToRead;

                    return bytesToRead;
                }
                finally
                {
                    ClearFlag(BufferFlags.Reading);
                }
            }
        }

        /// <summary>
        /// 清空缓冲区
        /// </summary>
        public void Clear()
        {
            ThrowIfDisposed();

            lock (_lockObject)
            {
                if (HasFlag(BufferFlags.Locked))
                    throw new InvalidOperationException("Buffer is locked");

                Array.Clear(_data, 0, _data.Length);
                _length = 0;
                _position = 0;
                ClearFlag(BufferFlags.Dirty);
            }
        }

        /// <summary>
        /// 锁定缓冲区（防止修改和释放）
        /// </summary>
        public void Lock()
        {
            SetFlag(BufferFlags.Locked);
        }

        /// <summary>
        /// 解锁缓冲区
        /// </summary>
        public void Unlock()
        {
            ClearFlag(BufferFlags.Locked);
        }

        /// <summary>
        /// 固定内存（用于非托管操作）
        /// </summary>
        public IntPtr Pin()
        {
            ThrowIfDisposed();

            lock (_lockObject)
            {
                if (HasFlag(BufferFlags.Pinned))
                    throw new InvalidOperationException("Buffer is already pinned");

                _pinnedHandle = GCHandle.Alloc(_data, GCHandleType.Pinned);
                SetFlag(BufferFlags.Pinned);
                return _pinnedHandle.AddrOfPinnedObject();
            }
        }

        /// <summary>
        /// 取消固定
        /// </summary>
        public void Unpin()
        {
            lock (_lockObject)
            {
                if (HasFlag(BufferFlags.Pinned))
                {
                    _pinnedHandle.Free();
                    ClearFlag(BufferFlags.Pinned);
                }
            }
        }

        /// <summary>
        /// 获取底层数组的Span（零拷贝访问）
        /// </summary>
        public Span<byte> AsSpan()
        {
            ThrowIfDisposed();
            return new Span<byte>(_data, 0, _length);
        }

        /// <summary>
        /// 获取底层数组的Memory（零拷贝访问）
        /// </summary>
        public Memory<byte> AsMemory()
        {
            ThrowIfDisposed();
            return new Memory<byte>(_data, 0, _length);
        }

        #endregion

        #region IDisposable

        private void ThrowIfDisposed()
        {
            if (HasFlag(BufferFlags.Disposed))
                throw new ObjectDisposedException(nameof(BufferSystem));
        }

        public void Dispose()
        {
            if (HasFlag(BufferFlags.Disposed))
                return;

            lock (_lockObject)
            {
                if (HasFlag(BufferFlags.Pinned))
                    Unpin();

                _data = null;
                SetFlag(BufferFlags.Disposed);
            }
        }

        #endregion
    }
}
