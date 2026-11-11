using System;
using System.Threading.Tasks;
using Xunit;
using BattleFramework.Core;

namespace BattleFramework.Tests
{
    public class BufferPoolTests
    {
        #region 构造函数测试

        [Fact]
        public void Constructor_CreatesPoolWithDefaultMaxSize()
        {
            // Arrange & Act
            var pool = new BufferPool();

            // Assert
            Assert.Equal(0, pool.TotalAllocated);
            Assert.Equal(0, pool.TotalPooled);
        }

        [Fact]
        public void Constructor_CreatesPoolWithCustomMaxSize()
        {
            // Arrange & Act
            var pool = new BufferPool(50);

            // Assert
            Assert.Equal(0, pool.TotalAllocated);
            Assert.Equal(0, pool.TotalPooled);
        }

        #endregion

        #region 租用测试

        [Fact]
        public void Rent_CreatesNewBuffer_WhenPoolEmpty()
        {
            // Arrange
            var pool = new BufferPool();

            // Act
            var buffer = pool.Rent(1024);

            // Assert
            Assert.NotNull(buffer);
            Assert.True(buffer.Capacity >= 1024);
            Assert.True(buffer.HasFlag(BufferFlags.Pooled));
            Assert.Equal(1, pool.TotalAllocated);
            Assert.Equal(0, pool.TotalPooled);

            // Cleanup
            pool.Return(buffer);
            pool.Dispose();
        }

        [Fact]
        public void Rent_ReusesBufferFromPool()
        {
            // Arrange
            var pool = new BufferPool();
            var buffer1 = pool.Rent(1024);
            pool.Return(buffer1);

            // Act
            var buffer2 = pool.Rent(1024);

            // Assert
            Assert.NotNull(buffer2);
            Assert.Equal(1, pool.TotalAllocated);
            Assert.Equal(0, pool.TotalPooled);

            // Cleanup
            pool.Return(buffer2);
            pool.Dispose();
        }

        [Fact]
        public void Rent_WithZeroSize_ThrowsException()
        {
            // Arrange
            var pool = new BufferPool();

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Rent(0));

            // Cleanup
            pool.Dispose();
        }

        [Fact]
        public void Rent_WithNegativeSize_ThrowsException()
        {
            // Arrange
            var pool = new BufferPool();

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Rent(-100));

            // Cleanup
            pool.Dispose();
        }

        [Fact]
        public void Rent_AllocatesAppropriateSize()
        {
            // Arrange
            var pool = new BufferPool();

            // Act & Assert - 测试各个大小级别
            var buffer1kb = pool.Rent(1024);
            Assert.Equal(1024, buffer1kb.Capacity);

            var buffer4kb = pool.Rent(4096);
            Assert.Equal(4096, buffer4kb.Capacity);

            var buffer16kb = pool.Rent(16384);
            Assert.Equal(16384, buffer16kb.Capacity);

            // Cleanup
            pool.Return(buffer1kb);
            pool.Return(buffer4kb);
            pool.Return(buffer16kb);
            pool.Dispose();
        }

        [Fact]
        public void Rent_RoundsUpToNextSize()
        {
            // Arrange
            var pool = new BufferPool();

            // Act - 请求1025字节，应该分配4KB
            var buffer = pool.Rent(1025);

            // Assert
            Assert.True(buffer.Capacity >= 1025);
            Assert.Equal(4096, buffer.Capacity);

            // Cleanup
            pool.Return(buffer);
            pool.Dispose();
        }

        #endregion

        #region 归还测试

        [Fact]
        public void Return_AddsBufferToPool()
        {
            // Arrange
            var pool = new BufferPool();
            var buffer = pool.Rent(1024);

            // Act
            pool.Return(buffer);

            // Assert
            Assert.Equal(1, pool.TotalAllocated);
            Assert.Equal(1, pool.TotalPooled);

            // Cleanup
            pool.Dispose();
        }

        [Fact]
        public void Return_NullBuffer_ThrowsException()
        {
            // Arrange
            var pool = new BufferPool();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => pool.Return(null));

            // Cleanup
            pool.Dispose();
        }

        [Fact]
        public void Return_DisposedBuffer_DoesNotAddToPool()
        {
            // Arrange
            var pool = new BufferPool();
            var buffer = pool.Rent(1024);
            buffer.Dispose();

            // Act
            pool.Return(buffer);

            // Assert
            Assert.Equal(1, pool.TotalAllocated);
            Assert.Equal(0, pool.TotalPooled);

            // Cleanup
            pool.Dispose();
        }

        [Fact]
        public void Return_BufferWithError_DisposesBuffer()
        {
            // Arrange
            var pool = new BufferPool();
            var buffer = pool.Rent(1024);
            buffer.SetFlag(BufferFlags.Error);

            // Act
            pool.Return(buffer);

            // Assert
            Assert.Equal(0, pool.TotalAllocated);
            Assert.Equal(0, pool.TotalPooled);

            // Cleanup
            pool.Dispose();
        }

        [Fact]
        public void Return_ClearsBufferState()
        {
            // Arrange
            var pool = new BufferPool();
            var buffer = pool.Rent(1024);
            buffer.Write(new byte[] { 1, 2, 3 }, 0, 3);
            buffer.SetFlag(BufferFlags.Locked);
            buffer.SetFlag(BufferFlags.Dirty);

            // Act
            pool.Return(buffer);
            var buffer2 = pool.Rent(1024);

            // Assert
            Assert.Equal(0, buffer2.Length);
            Assert.Equal(0, buffer2.Position);
            Assert.False(buffer2.HasFlag(BufferFlags.Locked));
            Assert.False(buffer2.HasFlag(BufferFlags.Dirty));

            // Cleanup
            pool.Return(buffer2);
            pool.Dispose();
        }

        [Fact]
        public void Return_UnpinsPinnedBuffer()
        {
            // Arrange
            var pool = new BufferPool();
            var buffer = pool.Rent(1024);
            buffer.Pin();

            // Act
            pool.Return(buffer);
            var buffer2 = pool.Rent(1024);

            // Assert
            Assert.False(buffer2.HasFlag(BufferFlags.Pinned));

            // Cleanup
            pool.Return(buffer2);
            pool.Dispose();
        }

        [Fact]
        public void Return_WhenPoolFull_DisposesBuffer()
        {
            // Arrange
            var pool = new BufferPool(maxPoolSize: 2);
            var buffer1 = pool.Rent(1024);
            var buffer2 = pool.Rent(1024);
            var buffer3 = pool.Rent(1024);

            pool.Return(buffer1);
            pool.Return(buffer2);

            // Act - 第三个归还应该被丢弃
            pool.Return(buffer3);

            // Assert
            Assert.Equal(2, pool.TotalAllocated);
            Assert.Equal(2, pool.TotalPooled);

            // Cleanup
            pool.Dispose();
        }

        #endregion

        #region PooledBuffer测试

        [Fact]
        public void RentPooled_ReturnsPooledBuffer()
        {
            // Arrange
            var pool = new BufferPool();

            // Act
            using (var pooled = pool.RentPooled(1024))
            {
                // Assert
                Assert.NotNull(pooled.Buffer);
                Assert.True(pooled.Buffer.Capacity >= 1024);
                Assert.Equal(1, pool.TotalAllocated);
                Assert.Equal(0, pool.TotalPooled);
            }

            // Assert - 应该自动归还
            Assert.Equal(1, pool.TotalAllocated);
            Assert.Equal(1, pool.TotalPooled);

            // Cleanup
            pool.Dispose();
        }

        [Fact]
        public void RentPooled_AutoReturnsOnDispose()
        {
            // Arrange
            var pool = new BufferPool();
            PooledBuffer pooled;

            // Act
            using (pooled = pool.RentPooled(1024))
            {
                pooled.Buffer.Write(new byte[] { 1, 2, 3 }, 0, 3);
            }

            // Assert
            Assert.Equal(1, pool.TotalPooled);

            // Cleanup
            pool.Dispose();
        }

        #endregion

        #region 清空和修剪测试

        [Fact]
        public void Clear_RemovesAllBuffers()
        {
            // Arrange
            var pool = new BufferPool();
            var buffer1 = pool.Rent(1024);
            var buffer2 = pool.Rent(4096);
            var buffer3 = pool.Rent(16384);

            pool.Return(buffer1);
            pool.Return(buffer2);
            pool.Return(buffer3);

            // Act
            pool.Clear();

            // Assert
            Assert.Equal(0, pool.TotalAllocated);
            Assert.Equal(0, pool.TotalPooled);

            // Cleanup
            pool.Dispose();
        }

        [Fact]
        public void Trim_RemovesExcessBuffers()
        {
            // Arrange
            var pool = new BufferPool();
            var buffers = new BufferSystem[10];
            for (int i = 0; i < 10; i++)
            {
                buffers[i] = pool.Rent(1024);
            }

            foreach (var buffer in buffers)
            {
                pool.Return(buffer);
            }

            // Act
            int trimmed = pool.Trim(targetCount: 3);

            // Assert
            Assert.True(trimmed >= 7); // 至少应该修剪7个
            Assert.True(pool.TotalPooled <= 3);

            // Cleanup
            pool.Dispose();
        }

        [Fact]
        public void Trim_WithZeroTarget_RemovesAllPooledBuffers()
        {
            // Arrange
            var pool = new BufferPool();
            var buffer1 = pool.Rent(1024);
            var buffer2 = pool.Rent(4096);

            pool.Return(buffer1);
            pool.Return(buffer2);

            // Act
            int trimmed = pool.Trim(targetCount: 0);

            // Assert
            Assert.Equal(2, trimmed);
            Assert.Equal(0, pool.TotalPooled);
            Assert.Equal(0, pool.TotalAllocated);

            // Cleanup
            pool.Dispose();
        }

        #endregion

        #region 并发测试

        [Fact]
        public void Rent_ConcurrentAccess_ThreadSafe()
        {
            // Arrange
            var pool = new BufferPool();
            int threadCount = 10;
            int operationsPerThread = 100;

            // Act
            Parallel.For(0, threadCount, i =>
            {
                for (int j = 0; j < operationsPerThread; j++)
                {
                    var buffer = pool.Rent(1024);
                    buffer.Write(new byte[] { 1, 2, 3 }, 0, 3);
                    pool.Return(buffer);
                }
            });

            // Assert - 不应该崩溃，且最终池状态正确
            Assert.True(pool.TotalPooled <= pool.TotalAllocated);

            // Cleanup
            pool.Dispose();
        }

        [Fact]
        public void RentReturn_ConcurrentMixed_ThreadSafe()
        {
            // Arrange
            var pool = new BufferPool();
            int iterations = 1000;

            // Act
            Parallel.Invoke(
                () =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var buffer = pool.Rent(1024);
                        pool.Return(buffer);
                    }
                },
                () =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var buffer = pool.Rent(4096);
                        pool.Return(buffer);
                    }
                },
                () =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var buffer = pool.Rent(16384);
                        pool.Return(buffer);
                    }
                }
            );

            // Assert - 验证池状态一致
            Assert.True(pool.TotalPooled >= 0);
            Assert.True(pool.TotalAllocated >= pool.TotalPooled);

            // Cleanup
            pool.Dispose();
        }

        #endregion

        #region Dispose测试

        [Fact]
        public void Dispose_ClearsAllBuffers()
        {
            // Arrange
            var pool = new BufferPool();
            var buffer1 = pool.Rent(1024);
            var buffer2 = pool.Rent(4096);

            pool.Return(buffer1);
            pool.Return(buffer2);

            // Act
            pool.Dispose();

            // Assert
            Assert.Equal(0, pool.TotalAllocated);
            Assert.Equal(0, pool.TotalPooled);
        }

        [Fact]
        public void Dispose_AfterDispose_RentThrowsException()
        {
            // Arrange
            var pool = new BufferPool();
            pool.Dispose();

            // Act & Assert
            Assert.Throws<ObjectDisposedException>(() => pool.Rent(1024));
        }

        [Fact]
        public void Dispose_AfterDispose_ReturnDisposesBuffer()
        {
            // Arrange
            var pool = new BufferPool();
            var buffer = pool.Rent(1024);
            pool.Dispose();

            // Act - 不应该抛出异常，但应该释放缓冲区
            pool.Return(buffer);

            // Assert
            Assert.True(buffer.HasFlag(BufferFlags.Disposed));
        }

        #endregion

        #region 性能测试

        [Fact]
        public void Performance_RentReturnCycle_Reuses()
        {
            // Arrange
            var pool = new BufferPool();
            int iterations = 10000;

            // Act
            for (int i = 0; i < iterations; i++)
            {
                var buffer = pool.Rent(1024);
                buffer.Write(new byte[] { 1, 2, 3 }, 0, 3);
                pool.Return(buffer);
            }

            // Assert - 应该只分配少量缓冲区
            Assert.True(pool.TotalAllocated < 100, $"TotalAllocated: {pool.TotalAllocated}");
            Assert.True(pool.TotalPooled > 0);

            // Cleanup
            pool.Dispose();
        }

        [Fact]
        public void Performance_MultipleSizes_ManagesEfficiently()
        {
            // Arrange
            var pool = new BufferPool();
            var sizes = new[] { 1024, 4096, 16384, 65536, 262144 };

            // Act
            foreach (var size in sizes)
            {
                for (int i = 0; i < 100; i++)
                {
                    var buffer = pool.Rent(size);
                    pool.Return(buffer);
                }
            }

            // Assert - 每个大小应该只有少量缓冲区
            Assert.True(pool.TotalAllocated <= sizes.Length * 10);

            // Cleanup
            pool.Dispose();
        }

        #endregion
    }
}
