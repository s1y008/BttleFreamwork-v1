using System;
using Xunit;
using BattleFramework.Core;

namespace BattleFramework.Tests
{
    public class BuffSystemTests
    {
        #region 构造函数和基础测试

        [Fact]
        public void Constructor_CreatesEmptyBuffSystem()
        {
            // Arrange & Act
            var buffSystem = new BuffSystemPooled(ownerId: 1);

            // Assert
            Assert.Equal(0, buffSystem.ActiveBuffCount);
            Assert.Equal(BuffType.None, buffSystem.ActiveBuffFlags);
        }

        #endregion

        #region 添加Buff测试

        [Fact]
        public void AddBuff_AddsBuffSuccessfully()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            var buff = new Buff
            {
                Id = 1,
                Name = "攻击力提升",
                Type = BuffType.AttackBoost,
                Category = BuffCategory.Positive,
                Duration = 10f,
                EffectValue = 50f
            };

            // Act
            bool added = buffSystem.AddBuff(buff);

            // Assert
            Assert.True(added);
            Assert.Equal(1, buffSystem.ActiveBuffCount);
            Assert.True(buffSystem.HasBuff(BuffType.AttackBoost));
        }

        [Fact]
        public void AddBuff_TriggersOnBuffAddedEvent()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            Buff addedBuff = null;
            buffSystem.OnBuffAdded += (buff) => addedBuff = buff;

            var buff = new Buff
            {
                Id = 1,
                Type = BuffType.SpeedBoost,
                Duration = 5f
            };

            // Act
            buffSystem.AddBuff(buff);

            // Assert
            Assert.NotNull(addedBuff);
            Assert.Equal(BuffType.SpeedBoost, addedBuff.Type);
        }

        [Fact]
        public void AddBuff_StackableBuff_IncreasesStackCount()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            var buff1 = new Buff
            {
                Id = 1,
                Type = BuffType.AttackBoost,
                Stackable = true,
                MaxStack = 5,
                Duration = 10f
            };
            var buff2 = new Buff
            {
                Id = 2,
                Type = BuffType.AttackBoost,
                Stackable = true,
                MaxStack = 5,
                Duration = 10f
            };

            // Act
            buffSystem.AddBuff(buff1);
            buffSystem.AddBuff(buff2);

            // Assert
            Assert.Equal(1, buffSystem.ActiveBuffCount); // 只有一个Buff实例
            Assert.Equal(2, buffSystem.GetBuffStackCount(BuffType.AttackBoost));
        }

        [Fact]
        public void AddBuff_NonStackableBuff_RefreshesDuration()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            var buff1 = new Buff
            {
                Id = 1,
                Type = BuffType.Shield,
                Stackable = false,
                Duration = 5f
            };

            buffSystem.AddBuff(buff1);
            buffSystem.Update(3f); // 经过3秒

            var buff2 = new Buff
            {
                Id = 2,
                Type = BuffType.Shield,
                Stackable = false,
                Duration = 10f
            };

            // Act
            buffSystem.AddBuff(buff2);

            // Assert
            Assert.Equal(1, buffSystem.ActiveBuffCount);
            Assert.True(buffSystem.GetBuffRemainingTime(BuffType.Shield) > 5f);
        }

        [Fact]
        public void AddBuff_WhenInvincible_BlocksNegativeBuffs()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.Invincible,
                Duration = 10f
            });

            var poisonBuff = new Buff
            {
                Id = 2,
                Type = BuffType.Poisoned,
                Category = BuffCategory.Negative,
                Duration = 5f
            };

            // Act
            bool added = buffSystem.AddBuff(poisonBuff);

            // Assert
            Assert.False(added);
            Assert.False(buffSystem.HasBuff(BuffType.Poisoned));
        }

        [Fact]
        public void AddBuff_WhenControlImmune_BlocksControlEffects()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.ControlImmune,
                Duration = 10f
            });

            var stunBuff = new Buff
            {
                Id = 2,
                Type = BuffType.Stunned,
                Category = BuffCategory.Control,
                Duration = 3f
            };

            // Act
            bool added = buffSystem.AddBuff(stunBuff);

            // Assert
            Assert.False(added);
            Assert.False(buffSystem.HasBuff(BuffType.Stunned));
        }

        #endregion

        #region 移除Buff测试

        [Fact]
        public void RemoveBuff_RemovesBuffByType()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.AttackBoost,
                Duration = 10f
            });

            // Act
            bool removed = buffSystem.RemoveBuff(BuffType.AttackBoost);

            // Assert
            Assert.True(removed);
            Assert.Equal(0, buffSystem.ActiveBuffCount);
            Assert.False(buffSystem.HasBuff(BuffType.AttackBoost));
        }

        [Fact]
        public void RemoveBuffById_RemovesSpecificBuff()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff { Id = 1, Type = BuffType.SpeedBoost, Duration = 10f });
            buffSystem.AddBuff(new Buff { Id = 2, Type = BuffType.DefenseBoost, Duration = 10f });

            // Act
            bool removed = buffSystem.RemoveBuffById(1);

            // Assert
            Assert.True(removed);
            Assert.Equal(1, buffSystem.ActiveBuffCount);
            Assert.False(buffSystem.HasBuff(BuffType.SpeedBoost));
            Assert.True(buffSystem.HasBuff(BuffType.DefenseBoost));
        }

        [Fact]
        public void RemoveBuff_TriggersOnBuffRemovedEvent()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            Buff removedBuff = null;
            buffSystem.OnBuffRemoved += (buff) => removedBuff = buff;

            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.Berserk,
                Duration = 10f
            });

            // Act
            buffSystem.RemoveBuff(BuffType.Berserk);

            // Assert
            Assert.NotNull(removedBuff);
            Assert.Equal(BuffType.Berserk, removedBuff.Type);
        }

        [Fact]
        public void DispelNegativeBuffs_RemovesDebuffs()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.Poisoned,
                Category = BuffCategory.Negative,
                Dispellable = true,
                Duration = 10f
            });
            buffSystem.AddBuff(new Buff
            {
                Id = 2,
                Type = BuffType.Slowed,
                Category = BuffCategory.Negative,
                Dispellable = true,
                Duration = 10f
            });
            buffSystem.AddBuff(new Buff
            {
                Id = 3,
                Type = BuffType.AttackBoost,
                Category = BuffCategory.Positive,
                Duration = 10f
            });

            // Act
            int dispelled = buffSystem.DispelNegativeBuffs(count: 1);

            // Assert
            Assert.Equal(1, dispelled);
            Assert.Equal(2, buffSystem.ActiveBuffCount); // 1个减益被移除，1个减益+1个增益保留
            Assert.True(buffSystem.HasBuff(BuffType.AttackBoost));
        }

        [Fact]
        public void ClearAllBuffs_RemovesAllBuffs()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff { Id = 1, Type = BuffType.AttackBoost, Duration = 10f });
            buffSystem.AddBuff(new Buff { Id = 2, Type = BuffType.SpeedBoost, Duration = 10f });
            buffSystem.AddBuff(new Buff { Id = 3, Type = BuffType.Poisoned, Duration = 10f });

            // Act
            buffSystem.ClearAllBuffs();

            // Assert
            Assert.Equal(0, buffSystem.ActiveBuffCount);
            Assert.Equal(BuffType.None, buffSystem.ActiveBuffFlags);
        }

        #endregion

        #region 位标志检查测试

        [Fact]
        public void HasBuff_ReturnsTrueForActiveBuff()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff { Id = 1, Type = BuffType.Shield, Duration = 10f });

            // Act & Assert
            Assert.True(buffSystem.HasBuff(BuffType.Shield));
            Assert.False(buffSystem.HasBuff(BuffType.Invincible));
        }

        [Fact]
        public void HasAnyBuff_DetectsAnyMatchingBuff()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff { Id = 1, Type = BuffType.AttackBoost, Duration = 10f });

            // Act & Assert
            Assert.True(buffSystem.HasAnyBuff(BuffType.AttackBoost | BuffType.DefenseBoost));
            Assert.False(buffSystem.HasAnyBuff(BuffType.SpeedBoost | BuffType.Shield));
        }

        [Fact]
        public void HasAllBuffs_RequiresAllBuffs()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff { Id = 1, Type = BuffType.AttackBoost, Duration = 10f });
            buffSystem.AddBuff(new Buff { Id = 2, Type = BuffType.DefenseBoost, Duration = 10f });

            // Act & Assert
            Assert.True(buffSystem.HasAllBuffs(BuffType.AttackBoost | BuffType.DefenseBoost));
            Assert.False(buffSystem.HasAllBuffs(BuffType.AttackBoost | BuffType.SpeedBoost));
        }

        [Fact]
        public void HasPositiveBuff_DetectsPositiveBuffs()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.AttackBoost,
                Category = BuffCategory.Positive,
                Duration = 10f
            });

            // Act & Assert
            Assert.True(buffSystem.HasPositiveBuff());
        }

        [Fact]
        public void HasNegativeBuff_DetectsNegativeBuffs()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.Poisoned,
                Category = BuffCategory.Negative,
                Duration = 10f
            });

            // Act & Assert
            Assert.True(buffSystem.HasNegativeBuff());
        }

        [Fact]
        public void IsControlled_DetectsControlEffects()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.Stunned,
                Category = BuffCategory.Control,
                Duration = 3f
            });

            // Act & Assert
            Assert.True(buffSystem.IsControlled());
        }

        [Fact]
        public void CanMove_ChecksMovementImpairments()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);

            // Act & Assert - 初始可以移动
            Assert.True(buffSystem.CanMove());

            // 添加冰冻效果
            buffSystem.AddBuff(new Buff { Id = 1, Type = BuffType.Frozen, Duration = 5f });
            Assert.False(buffSystem.CanMove());

            // 移除冰冻
            buffSystem.RemoveBuff(BuffType.Frozen);
            Assert.True(buffSystem.CanMove());
        }

        [Fact]
        public void CanAttack_ChecksAttackImpairments()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);

            // Act & Assert
            Assert.True(buffSystem.CanAttack());

            buffSystem.AddBuff(new Buff { Id = 1, Type = BuffType.Disarmed, Duration = 5f });
            Assert.False(buffSystem.CanAttack());
        }

        [Fact]
        public void CanCast_ChecksCastImpairments()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);

            // Act & Assert
            Assert.True(buffSystem.CanCast());

            buffSystem.AddBuff(new Buff { Id = 1, Type = BuffType.Silenced, Duration = 5f });
            Assert.False(buffSystem.CanCast());
        }

        #endregion

        #region 更新和过期测试

        [Fact]
        public void Update_DecreasesRemainingTime()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.Shield,
                Duration = 10f
            });

            // Act
            buffSystem.Update(3f);

            // Assert
            float remaining = buffSystem.GetBuffRemainingTime(BuffType.Shield);
            Assert.True(remaining > 6.9f && remaining < 7.1f); // 约7秒
        }

        [Fact]
        public void Update_RemovesExpiredBuffs()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.SpeedBoost,
                Duration = 5f
            });

            // Act
            buffSystem.Update(6f); // 超过持续时间

            // Assert
            Assert.Equal(0, buffSystem.ActiveBuffCount);
            Assert.False(buffSystem.HasBuff(BuffType.SpeedBoost));
        }

        [Fact]
        public void Update_PermanentBuffsNeverExpire()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.Invincible,
                Duration = -1f // 永久
            });

            // Act
            buffSystem.Update(1000f); // 很长时间

            // Assert
            Assert.Equal(1, buffSystem.ActiveBuffCount);
            Assert.True(buffSystem.HasBuff(BuffType.Invincible));
        }

        [Fact]
        public void Update_TriggersTickEvent()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            int tickCount = 0;
            buffSystem.OnBuffTick += (buff) => tickCount++;

            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.Poisoned,
                Duration = 10f,
                TickInterval = 1f
            });

            // Act
            buffSystem.Update(1.1f);
            buffSystem.Update(1.0f);
            buffSystem.Update(1.0f);

            // Assert
            Assert.Equal(3, tickCount);
        }

        #endregion

        #region 查询测试

        [Fact]
        public void GetBuffs_ReturnsBuffsOfType()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff { Id = 1, Type = BuffType.Poisoned, Duration = 10f });
            buffSystem.AddBuff(new Buff { Id = 2, Type = BuffType.Burning, Duration = 10f });

            // Act
            var poisonBuffs = buffSystem.GetBuffs(BuffType.Poisoned);

            // Assert
            Assert.Single(poisonBuffs);
            Assert.Equal(BuffType.Poisoned, poisonBuffs[0].Type);
        }

        [Fact]
        public void GetBuffsByCategory_ReturnsMatchingBuffs()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.AttackBoost,
                Category = BuffCategory.Positive,
                Duration = 10f
            });
            buffSystem.AddBuff(new Buff
            {
                Id = 2,
                Type = BuffType.Poisoned,
                Category = BuffCategory.Negative,
                Duration = 10f
            });

            // Act
            var positiveBuffs = buffSystem.GetBuffsByCategory(BuffCategory.Positive);

            // Assert
            Assert.Single(positiveBuffs);
            Assert.Equal(BuffType.AttackBoost, positiveBuffs[0].Type);
        }

        [Fact]
        public void CalculateStatBonus_SumsEffectValues()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.AttackBoost,
                EffectValue = 50f,
                Stackable = true,
                MaxStack = 5,
                Duration = 10f
            });
            buffSystem.AddBuff(new Buff
            {
                Id = 2,
                Type = BuffType.AttackBoost,
                EffectValue = 50f,
                Stackable = true,
                MaxStack = 5,
                Duration = 10f
            });

            // Act
            float bonus = buffSystem.CalculateStatBonus(BuffType.AttackBoost);

            // Assert
            Assert.Equal(100f, bonus); // 50 * 2层
        }

        [Fact]
        public void CalculateStatMultiplier_MultipliesPercentages()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Type = BuffType.AttackBoost,
                EffectPercent = 0.2f, // +20%
                Duration = 10f
            });

            // Act
            float multiplier = buffSystem.CalculateStatMultiplier(BuffType.AttackBoost);

            // Assert
            Assert.Equal(1.2f, multiplier);
        }

        #endregion

        #region 集成测试

        [Fact]
        public void IntegrationTest_CompleteBuffLifecycle()
        {
            // Arrange
            var buffSystem = new BuffSystemPooled(1);
            int addedCount = 0;
            int removedCount = 0;
            int tickCount = 0;

            buffSystem.OnBuffAdded += (b) => addedCount++;
            buffSystem.OnBuffRemoved += (b) => removedCount++;
            buffSystem.OnBuffTick += (b) => tickCount++;

            // Act - 添加中毒Buff
            var poisonBuff = new Buff
            {
                Id = 1,
                Type = BuffType.Poisoned,
                Category = BuffCategory.Negative,
                Duration = 5f,
                TickInterval = 1f,
                EffectValue = 10f
            };
            buffSystem.AddBuff(poisonBuff);

            // 模拟时间流逝
            buffSystem.Update(1.1f); // Tick 1
            buffSystem.Update(1.0f); // Tick 2
            buffSystem.Update(1.0f); // Tick 3
            buffSystem.Update(2.0f); // 超过持续时间，Buff过期

            // Assert
            Assert.Equal(1, addedCount);
            Assert.Equal(1, removedCount);
            Assert.True(tickCount >= 3);
            Assert.Equal(0, buffSystem.ActiveBuffCount);
        }

        [Fact]
        public void IntegrationTest_MultipleCombatScenario()
        {
            // Arrange - 战斗场景
            var buffSystem = new BuffSystemPooled(playerId: 1);

            // Act - 玩家获得攻击加成
            buffSystem.AddBuff(new Buff
            {
                Id = 1,
                Name = "狂战士之怒",
                Type = BuffType.AttackBoost,
                Category = BuffCategory.Positive,
                Duration = 10f,
                EffectPercent = 0.5f // +50%攻击
            });

            // 玩家被敌人中毒
            buffSystem.AddBuff(new Buff
            {
                Id = 2,
                Name = "剧毒",
                Type = BuffType.Poisoned,
                Category = BuffCategory.Negative,
                Duration = 8f,
                TickInterval = 2f,
                EffectValue = 20f
            });

            // 玩家被眩晕
            buffSystem.AddBuff(new Buff
            {
                Id = 3,
                Name = "雷霆一击",
                Type = BuffType.Stunned,
                Category = BuffCategory.Control,
                Duration = 3f
            });

            // Assert - 检查状态
            Assert.Equal(3, buffSystem.ActiveBuffCount);
            Assert.True(buffSystem.HasPositiveBuff());
            Assert.True(buffSystem.HasNegativeBuff());
            Assert.True(buffSystem.IsControlled());
            Assert.False(buffSystem.CanMove());
            Assert.False(buffSystem.CanAttack());

            // Act - 时间经过3秒，眩晕解除
            buffSystem.Update(3.1f);

            // Assert - 眩晕已解除
            Assert.False(buffSystem.IsControlled());
            Assert.True(buffSystem.CanMove());
            Assert.True(buffSystem.CanAttack());
            Assert.Equal(2, buffSystem.ActiveBuffCount);

            // Act - 使用净化技能驱散减益
            buffSystem.DispelNegativeBuffs(count: 10);

            // Assert - 只剩增益Buff
            Assert.Equal(1, buffSystem.ActiveBuffCount);
            Assert.True(buffSystem.HasBuff(BuffType.AttackBoost));
            Assert.False(buffSystem.HasBuff(BuffType.Poisoned));
        }

        #endregion
    }
}
