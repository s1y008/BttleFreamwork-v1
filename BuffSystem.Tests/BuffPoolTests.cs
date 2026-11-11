using System;
using Xunit;
using BattleFramework.Core;

namespace BattleFramework.Tests
{
    public class BuffPoolTests
    {
        #region 对象池基础测试

        [Fact]
        public void Constructor_CreatesEmptyPool()
        {
            // Arrange & Act
            var pool = new BuffPool(maxPoolSize: 100);

            // Assert
            Assert.Equal(0, pool.CreatedCount);
            Assert.Equal(0, pool.AvailableCount);
        }

        [Fact]
        public void Get_CreatesNewBuffWhenPoolEmpty()
        {
            // Arrange
            var pool = new BuffPool();

            // Act
            var buff = pool.Get();

            // Assert
            Assert.NotNull(buff);
            Assert.Equal(1, pool.CreatedCount);
            Assert.Equal(0, pool.AvailableCount);
        }

        [Fact]
        public void Return_AddBuffToPool()
        {
            // Arrange
            var pool = new BuffPool();
            var buff = pool.Get();

            // Act
            pool.Return(buff);

            // Assert
            Assert.Equal(1, pool.CreatedCount);
            Assert.Equal(1, pool.AvailableCount);
        }

        [Fact]
        public void GetReturn_ReusesBuffObject()
        {
            // Arrange
            var pool = new BuffPool();
            var buff1 = pool.Get();
            pool.Return(buff1);

            // Act
            var buff2 = pool.Get();

            // Assert
            Assert.Same(buff1, buff2); // 同一个对象
            Assert.Equal(1, pool.CreatedCount); // 只创建了一次
            Assert.Equal(0, pool.AvailableCount);
        }

        [Fact]
        public void Return_ResetsBuffProperties()
        {
            // Arrange
            var pool = new BuffPool();
            var buff = pool.Get();
            buff.Type = BuffType.AttackBoost;
            buff.Id = 999;
            buff.Name = "Test";
            buff.EffectValue = 100f;

            // Act
            pool.Return(buff);
            var reused = pool.Get();

            // Assert
            Assert.Equal(BuffType.None, reused.Type);
            Assert.Equal(0, reused.Id);
            Assert.Null(reused.Name);
            Assert.Equal(0f, reused.EffectValue);
        }

        [Fact]
        public void Return_WhenPoolFull_DiscardsObject()
        {
            // Arrange
            var pool = new BuffPool(maxPoolSize: 2);
            var buff1 = pool.Get();
            var buff2 = pool.Get();
            var buff3 = pool.Get();

            pool.Return(buff1);
            pool.Return(buff2);

            // Act
            pool.Return(buff3); // 池已满

            // Assert
            Assert.Equal(2, pool.AvailableCount); // 只保留2个
        }

        [Fact]
        public void Prewarm_CreatesBuffsInAdvance()
        {
            // Arrange
            var pool = new BuffPool();

            // Act
            pool.Prewarm(50);

            // Assert
            Assert.Equal(50, pool.CreatedCount);
            Assert.Equal(50, pool.AvailableCount);
        }

        [Fact]
        public void Prewarm_RespectsMaxPoolSize()
        {
            // Arrange
            var pool = new BuffPool(maxPoolSize: 10);

            // Act
            pool.Prewarm(100); // 尝试预热100个

            // Assert
            Assert.Equal(10, pool.CreatedCount); // 最多10个
            Assert.Equal(10, pool.AvailableCount);
        }

        [Fact]
        public void Clear_RemovesAllBuffsFromPool()
        {
            // Arrange
            var pool = new BuffPool();
            pool.Prewarm(20);

            // Act
            pool.Clear();

            // Assert
            Assert.Equal(0, pool.AvailableCount);
            Assert.Equal(20, pool.CreatedCount); // CreatedCount不变
        }

        #endregion

        #region 性能测试

        [Fact]
        public void Performance_HighFrequencyGetReturn_ReuseEfficiently()
        {
            // Arrange
            var pool = new BuffPool();
            pool.Prewarm(10);

            // Act - 模拟1000次获取和归还
            for (int i = 0; i < 1000; i++)
            {
                var buff = pool.Get();
                pool.Return(buff);
            }

            // Assert - 应该只创建了10个对象
            Assert.True(pool.CreatedCount <= 11); // 最多11个（预热10 + 可能的1个）
        }

        [Fact]
        public void Performance_MassiveAllocation_UsesPool()
        {
            // Arrange
            var pool = new BuffPool(maxPoolSize: 50);
            pool.Prewarm(50);

            // Act - 同时获取100个Buff
            var buffs = new Buff[100];
            for (int i = 0; i < 100; i++)
            {
                buffs[i] = pool.Get();
            }

            // Assert - 创建了100个（50个从池，50个新建）
            Assert.Equal(100, pool.CreatedCount);

            // Act - 归还所有Buff
            foreach (var buff in buffs)
            {
                pool.Return(buff);
            }

            // Assert - 池中保留50个（达到maxPoolSize限制）
            Assert.Equal(50, pool.AvailableCount);
        }

        #endregion
    }

    public class BuffSystemPooledTests
    {
        #region 零GC添加Buff测试

        [Fact]
        public void AddBuff_ZeroGC_CreatesBuffFromPool()
        {
            // Arrange
            var pool = new BuffPool();
            pool.Prewarm(10);
            var buffSystem = new BuffSystemPooled(1, pool);

            // Act
            var buff = buffSystem.AddBuff(
                type: BuffType.AttackBoost,
                id: 1,
                name: "攻击提升",
                duration: 10f,
                effectValue: 50f
            );

            // Assert
            Assert.NotNull(buff);
            Assert.True(buffSystem.HasBuff(BuffType.AttackBoost));
            Assert.Equal(10, pool.CreatedCount); // 没有额外创建
        }

        [Fact]
        public void AddBuff_MultipleBuffs_ReusesPoolObjects()
        {
            // Arrange
            var pool = new BuffPool();
            pool.Prewarm(5);
            var buffSystem = new BuffSystemPooled(1, pool);

            // Act
            buffSystem.AddBuff(BuffType.AttackBoost, 1, duration: 1f);
            buffSystem.AddBuff(BuffType.DefenseBoost, 2, duration: 1f);
            buffSystem.AddBuff(BuffType.SpeedBoost, 3, duration: 1f);

            buffSystem.Update(2f); // 所有Buff过期

            buffSystem.AddBuff(BuffType.Shield, 4, duration: 1f);
            buffSystem.AddBuff(BuffType.Invincible, 5, duration: 1f);

            // Assert - 只创建了5个对象（预热的）
            Assert.Equal(5, pool.CreatedCount);
        }

        [Fact]
        public void RemoveBuff_ReturnsBuffToPool()
        {
            // Arrange
            var pool = new BuffPool();
            var buffSystem = new BuffSystemPooled(1, pool);

            buffSystem.AddBuff(BuffType.Poisoned, 1, duration: 10f);
            Assert.Equal(0, pool.AvailableCount);

            // Act
            buffSystem.RemoveBuff(BuffType.Poisoned);

            // Assert
            Assert.Equal(1, pool.AvailableCount); // 归还到池中
        }

        [Fact]
        public void ClearAllBuffs_ReturnsAllBuffsToPool()
        {
            // Arrange
            var pool = new BuffPool();
            var buffSystem = new BuffSystemPooled(1, pool);

            buffSystem.AddBuff(BuffType.AttackBoost, 1, duration: 10f);
            buffSystem.AddBuff(BuffType.DefenseBoost, 2, duration: 10f);
            buffSystem.AddBuff(BuffType.SpeedBoost, 3, duration: 10f);

            // Act
            buffSystem.ClearAllBuffs();

            // Assert
            Assert.Equal(0, buffSystem.ActiveBuffCount);
            Assert.Equal(3, pool.AvailableCount); // 3个Buff归还到池
        }

        [Fact]
        public void Update_ExpiredBuffs_ReturnsToPool()
        {
            // Arrange
            var pool = new BuffPool();
            var buffSystem = new BuffSystemPooled(1, pool);

            buffSystem.AddBuff(BuffType.AttackBoost, 1, duration: 2f);
            buffSystem.AddBuff(BuffType.DefenseBoost, 2, duration: 5f);

            // Act
            buffSystem.Update(3f); // 第一个Buff过期

            // Assert
            Assert.Equal(1, buffSystem.ActiveBuffCount);
            Assert.Equal(1, pool.AvailableCount); // 过期的Buff归还
        }

        #endregion

        #region 兼容性测试

        [Fact]
        public void AddBuff_OldInterface_StillWorks()
        {
            // Arrange
            var pool = new BuffPool();
            var buffSystem = new BuffSystemPooled(1, pool);

            var buff = new Buff
            {
                Id = 1,
                Type = BuffType.Shield,
                Duration = 10f,
                EffectValue = 100f
            };

            // Act
            bool added = buffSystem.AddBuff(buff);

            // Assert
            Assert.True(added);
            Assert.True(buffSystem.HasBuff(BuffType.Shield));
        }

        #endregion

        #region 集成测试

        [Fact]
        public void IntegrationTest_HighFrequencyBuffSystem_NoGCPressure()
        {
            // Arrange
            var pool = new BuffPool(maxPoolSize: 20);
            pool.Prewarm(20);
            var buffSystem = new BuffSystemPooled(1, pool);

            int addedCount = 0;
            int removedCount = 0;
            buffSystem.OnBuffAdded += (b) => addedCount++;
            buffSystem.OnBuffRemoved += (b) => removedCount++;

            // Act - 模拟高频战斗场景
            for (int round = 0; round < 100; round++)
            {
                // 添加各种Buff
                buffSystem.AddBuff(BuffType.Poisoned, 1000 + round, duration: 0.5f, tickInterval: 0.1f, effectValue: 10f);
                buffSystem.AddBuff(BuffType.Burning, 2000 + round, duration: 0.5f, tickInterval: 0.1f, effectValue: 15f);
                buffSystem.AddBuff(BuffType.AttackBoost, 3000 + round, duration: 1f, effectPercent: 0.2f);

                // 更新
                buffSystem.Update(0.1f);

                // 随机驱散
                if (round % 10 == 0)
                {
                    buffSystem.DispelNegativeBuffs(count: 1);
                }

                // 等待Buff过期
                buffSystem.Update(0.6f);
            }

            // Assert
            Assert.True(addedCount > 0);
            Assert.True(removedCount > 0);
            Assert.True(pool.CreatedCount <= 25); // 对象复用良好，创建很少
            Assert.Equal(0, buffSystem.ActiveBuffCount); // 所有Buff已清理
        }

        [Fact]
        public void IntegrationTest_BossRaid_PoolPerformance()
        {
            // Arrange - 模拟Boss战，多个玩家和Boss使用共享池
            var sharedPool = new BuffPool(maxPoolSize: 100);
            sharedPool.Prewarm(50);

            var bossBuffSystem = new BuffSystemPooled(1, sharedPool);
            var player1BuffSystem = new BuffSystemPooled(2, sharedPool);
            var player2BuffSystem = new BuffSystemPooled(3, sharedPool);
            var player3BuffSystem = new BuffSystemPooled(4, sharedPool);

            // Act - 战斗模拟
            for (int tick = 0; tick < 50; tick++)
            {
                // Boss施加减益
                player1BuffSystem.AddBuff(BuffType.Weakened, 10000 + tick, duration: 5f, effectPercent: -0.3f);
                player2BuffSystem.AddBuff(BuffType.Slowed, 20000 + tick, duration: 3f);
                player3BuffSystem.AddBuff(BuffType.Cursed, 30000 + tick, duration: 8f);

                // 玩家施加减益给Boss
                bossBuffSystem.AddBuff(BuffType.ArmorBreak, 40000 + tick, duration: 10f, effectPercent: -0.5f);
                bossBuffSystem.AddBuff(BuffType.Bleeding, 50000 + tick, duration: 15f, tickInterval: 1f, effectValue: 100f);

                // 玩家自我加Buff
                player1BuffSystem.AddBuff(BuffType.AttackBoost, 60000 + tick, duration: 10f, effectPercent: 0.5f);
                player2BuffSystem.AddBuff(BuffType.DefenseBoost, 70000 + tick, duration: 10f, effectPercent: 0.3f);

                // 更新所有系统
                bossBuffSystem.Update(1f);
                player1BuffSystem.Update(1f);
                player2BuffSystem.Update(1f);
                player3BuffSystem.Update(1f);

                // 定期驱散
                if (tick % 10 == 0)
                {
                    player1BuffSystem.DispelNegativeBuffs(count: 10);
                    player2BuffSystem.DispelNegativeBuffs(count: 10);
                    player3BuffSystem.DispelNegativeBuffs(count: 10);
                }
            }

            // Assert - 检查对象池效率
            Assert.True(sharedPool.CreatedCount < 80); // 4个系统共享池，创建对象很少
            Console.WriteLine($"Pool efficiency: Created {sharedPool.CreatedCount}, Available {sharedPool.AvailableCount}");
        }

        #endregion
    }
}
