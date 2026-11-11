using System;
using Xunit;
using BattleFramework.Core;

namespace BattleFramework.Tests
{
    public class BufferSystemTests
    {
        #region 构造函数测试

        [Fact]
        public void Constructor_WithValidCapacity_CreatesBuffer()
        {
            // Arrange & Act
            var buffer = new BufferSystem(1024);

            // Assert
            Assert.Equal(1024, buffer.Capacity);
            Assert.Equal(0, buffer.Length);
            Assert.Equal(0, buffer.Position);
            Assert.True(buffer.HasFlag(BufferFlags.Allocated));
        }

        [Fact]
        public void Constructor_WithInitialFlags_SetsFlags()
        {
            // Arrange & Act
            var buffer = new BufferSystem(1024, BufferFlags.Pooled | BufferFlags.Temporary);

            // Assert
            Assert.True(buffer.HasFlag(BufferFlags.Allocated));
            Assert.True(buffer.HasFlag(BufferFlags.Pooled));
            Assert.True(buffer.HasFlag(BufferFlags.Temporary));
        }

        [Fact]
        public void Constructor_WithZeroCapacity_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new BufferSystem(0));
        }

        [Fact]
        public void Constructor_WithNegativeCapacity_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new BufferSystem(-100));
        }

        #endregion

        #region 标志位操作测试

        [Fact]
        public void SetFlag_SetsSpecificFlag()
        {
            // Arrange
            var buffer = new BufferSystem(1024);

            // Act
            buffer.SetFlag(BufferFlags.Locked);

            // Assert
            Assert.True(buffer.HasFlag(BufferFlags.Locked));
            Assert.True(buffer.HasFlag(BufferFlags.Allocated));
        }

        [Fact]
        public void ClearFlag_ClearsSpecificFlag()
        {
            // Arrange
            var buffer = new BufferSystem(1024, BufferFlags.Locked | BufferFlags.Dirty);

            // Act
            buffer.ClearFlag(BufferFlags.Locked);

            // Assert
            Assert.False(buffer.HasFlag(BufferFlags.Locked));
            Assert.True(buffer.HasFlag(BufferFlags.Dirty));
        }

        [Fact]
        public void ToggleFlag_TogglesSpecificFlag()
        {
            // Arrange
            var buffer = new BufferSystem(1024);

            // Act
            buffer.ToggleFlag(BufferFlags.Locked);
            Assert.True(buffer.HasFlag(BufferFlags.Locked));

            buffer.ToggleFlag(BufferFlags.Locked);
            Assert.False(buffer.HasFlag(BufferFlags.Locked));
        }

        [Fact]
        public void HasAnyFlag_DetectsAnyMatchingFlag()
        {
            // Arrange
            var buffer = new BufferSystem(1024, BufferFlags.Locked);

            // Act & Assert
            Assert.True(buffer.HasAnyFlag(BufferFlags.Locked | BufferFlags.Dirty));
            Assert.False(buffer.HasAnyFlag(BufferFlags.Reading | BufferFlags.Writing));
        }

        [Fact]
        public void SetFlags_SetsMultipleFlags()
        {
            // Arrange
            var buffer = new BufferSystem(1024);

            // Act
            buffer.SetFlags(BufferFlags.Locked | BufferFlags.Dirty | BufferFlags.Shared);

            // Assert
            Assert.True(buffer.HasFlag(BufferFlags.Locked));
            Assert.True(buffer.HasFlag(BufferFlags.Dirty));
            Assert.True(buffer.HasFlag(BufferFlags.Shared));
        }

        [Fact]
        public void ClearFlags_ClearsMultipleFlags()
        {
            // Arrange
            var buffer = new BufferSystem(1024, BufferFlags.Locked | BufferFlags.Dirty | BufferFlags.Shared);

            // Act
            buffer.ClearFlags(BufferFlags.Locked | BufferFlags.Dirty);

            // Assert
            Assert.False(buffer.HasFlag(BufferFlags.Locked));
            Assert.False(buffer.HasFlag(BufferFlags.Dirty));
            Assert.True(buffer.HasFlag(BufferFlags.Shared));
        }

        #endregion

        #region 写入操作测试

        [Fact]
        public void Write_WritesDataSuccessfully()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };

            // Act
            int written = buffer.Write(data, 0, data.Length);

            // Assert
            Assert.Equal(5, written);
            Assert.Equal(5, buffer.Position);
            Assert.Equal(5, buffer.Length);
            Assert.True(buffer.HasFlag(BufferFlags.Dirty));
        }

        [Fact]
        public void Write_WhenLocked_ThrowsException()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            buffer.Lock();
            byte[] data = new byte[] { 1, 2, 3 };

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => buffer.Write(data, 0, data.Length));
        }

        [Fact]
        public void Write_ExceedingCapacity_WritesPartial()
        {
            // Arrange
            var buffer = new BufferSystem(10);
            byte[] data = new byte[20];

            // Act
            int written = buffer.Write(data, 0, data.Length);

            // Assert
            Assert.Equal(10, written);
            Assert.Equal(10, buffer.Position);
            Assert.Equal(10, buffer.Length);
        }

        [Fact]
        public void Write_NullBuffer_ThrowsException()
        {
            // Arrange
            var buffer = new BufferSystem(1024);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => buffer.Write(null, 0, 10));
        }

        [Fact]
        public void Write_InvalidOffset_ThrowsException()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            byte[] data = new byte[10];

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Write(data, -1, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Write(data, 20, 5));
        }

        [Fact]
        public void Write_SetsWritingFlag_TemporarilyDuringOperation()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            byte[] data = new byte[5];

            // Act
            buffer.Write(data, 0, data.Length);

            // Assert - Writing flag should be cleared after operation
            Assert.False(buffer.HasFlag(BufferFlags.Writing));
        }

        #endregion

        #region 读取操作测试

        [Fact]
        public void Read_ReadsDataSuccessfully()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            byte[] writeData = new byte[] { 1, 2, 3, 4, 5 };
            buffer.Write(writeData, 0, writeData.Length);
            buffer.Position = 0; // 重置位置

            byte[] readData = new byte[5];

            // Act
            int read = buffer.Read(readData, 0, readData.Length);

            // Assert
            Assert.Equal(5, read);
            Assert.Equal(5, buffer.Position);
            Assert.Equal(writeData, readData);
        }

        [Fact]
        public void Read_ExceedingLength_ReadsPartial()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            byte[] writeData = new byte[] { 1, 2, 3, 4, 5 };
            buffer.Write(writeData, 0, writeData.Length);
            buffer.Position = 0;

            byte[] readData = new byte[10];

            // Act
            int read = buffer.Read(readData, 0, readData.Length);

            // Assert
            Assert.Equal(5, read);
            Assert.Equal(5, buffer.Position);
        }

        [Fact]
        public void Read_NullBuffer_ThrowsException()
        {
            // Arrange
            var buffer = new BufferSystem(1024);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => buffer.Read(null, 0, 10));
        }

        [Fact]
        public void Read_SetsReadingFlag_TemporarilyDuringOperation()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            byte[] data = new byte[5];

            // Act
            buffer.Read(data, 0, data.Length);

            // Assert - Reading flag should be cleared after operation
            Assert.False(buffer.HasFlag(BufferFlags.Reading));
        }

        #endregion

        #region 清空操作测试

        [Fact]
        public void Clear_ClearsBufferSuccessfully()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            buffer.Write(data, 0, data.Length);

            // Act
            buffer.Clear();

            // Assert
            Assert.Equal(0, buffer.Length);
            Assert.Equal(0, buffer.Position);
            Assert.False(buffer.HasFlag(BufferFlags.Dirty));
        }

        [Fact]
        public void Clear_WhenLocked_ThrowsException()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            buffer.Lock();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => buffer.Clear());
        }

        #endregion

        #region 锁定操作测试

        [Fact]
        public void Lock_LocksBuffer()
        {
            // Arrange
            var buffer = new BufferSystem(1024);

            // Act
            buffer.Lock();

            // Assert
            Assert.True(buffer.HasFlag(BufferFlags.Locked));
        }

        [Fact]
        public void Unlock_UnlocksBuffer()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            buffer.Lock();

            // Act
            buffer.Unlock();

            // Assert
            Assert.False(buffer.HasFlag(BufferFlags.Locked));
        }

        #endregion

        #region 内存固定测试

        [Fact]
        public void Pin_PinsMemory()
        {
            // Arrange
            var buffer = new BufferSystem(1024);

            // Act
            IntPtr ptr = buffer.Pin();

            // Assert
            Assert.NotEqual(IntPtr.Zero, ptr);
            Assert.True(buffer.HasFlag(BufferFlags.Pinned));

            // Cleanup
            buffer.Unpin();
        }

        [Fact]
        public void Pin_WhenAlreadyPinned_ThrowsException()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            buffer.Pin();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => buffer.Pin());

            // Cleanup
            buffer.Unpin();
        }

        [Fact]
        public void Unpin_UnpinsMemory()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            buffer.Pin();

            // Act
            buffer.Unpin();

            // Assert
            Assert.False(buffer.HasFlag(BufferFlags.Pinned));
        }

        #endregion

        #region Span和Memory测试

        [Fact]
        public void AsSpan_ReturnsValidSpan()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            buffer.Write(data, 0, data.Length);
            buffer.Position = 0;

            // Act
            var span = buffer.AsSpan();

            // Assert
            Assert.Equal(5, span.Length);
            Assert.Equal(1, span[0]);
            Assert.Equal(5, span[4]);
        }

        [Fact]
        public void AsMemory_ReturnsValidMemory()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            buffer.Write(data, 0, data.Length);
            buffer.Position = 0;

            // Act
            var memory = buffer.AsMemory();

            // Assert
            Assert.Equal(5, memory.Length);
            Assert.Equal(1, memory.Span[0]);
            Assert.Equal(5, memory.Span[4]);
        }

        #endregion

        #region Dispose测试

        [Fact]
        public void Dispose_DisposesBuffer()
        {
            // Arrange
            var buffer = new BufferSystem(1024);

            // Act
            buffer.Dispose();

            // Assert
            Assert.True(buffer.HasFlag(BufferFlags.Disposed));
            Assert.Throws<ObjectDisposedException>(() => buffer.Write(new byte[10], 0, 10));
        }

        [Fact]
        public void Dispose_WhenPinned_UnpinsFirst()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            buffer.Pin();

            // Act
            buffer.Dispose();

            // Assert
            Assert.False(buffer.HasFlag(BufferFlags.Pinned));
            Assert.True(buffer.HasFlag(BufferFlags.Disposed));
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Arrange
            var buffer = new BufferSystem(1024);

            // Act
            buffer.Dispose();
            buffer.Dispose();
            buffer.Dispose();

            // Assert - No exception should be thrown
            Assert.True(buffer.HasFlag(BufferFlags.Disposed));
        }

        #endregion

        #region 集成测试

        [Fact]
        public void IntegrationTest_WriteReadCycle()
        {
            // Arrange
            var buffer = new BufferSystem(1024);
            byte[] originalData = new byte[256];
            for (int i = 0; i < originalData.Length; i++)
                originalData[i] = (byte)(i % 256);

            // Act - Write
            int written = buffer.Write(originalData, 0, originalData.Length);
            buffer.Position = 0; // Reset for reading

            // Act - Read
            byte[] readData = new byte[256];
            int read = buffer.Read(readData, 0, readData.Length);

            // Assert
            Assert.Equal(256, written);
            Assert.Equal(256, read);
            Assert.Equal(originalData, readData);
        }

        [Fact]
        public void IntegrationTest_MultipleWritesIncreasesLength()
        {
            // Arrange
            var buffer = new BufferSystem(1024);

            // Act
            buffer.Write(new byte[] { 1, 2, 3 }, 0, 3);
            buffer.Write(new byte[] { 4, 5, 6 }, 0, 3);
            buffer.Write(new byte[] { 7, 8, 9 }, 0, 3);

            // Assert
            Assert.Equal(9, buffer.Length);
            Assert.Equal(9, buffer.Position);
        }

        #endregion
    }
}
