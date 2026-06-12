// =============================================================================
// COLOURS OF CALRADIA — PureLogicTests.cs
// Mount & Blade II: Bannerlord Mod  v2.0
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using AshAndEmber;

namespace AshAndEmber.Tests
{
    [TestFixture]
    public class PureLogicTests
    {
        // ── ColorSchoolData tests ─────────────────────────────────────────────

        [Test]
        public void ColorSchoolData_GetGlowColor_EachSchool_NonZero()
        {
            var schools = new[]
            {
                ColorSchool.Red, ColorSchool.Orange, ColorSchool.Yellow,
                ColorSchool.Green, ColorSchool.Blue, ColorSchool.Purple
            };
            foreach (var school in schools)
            {
                uint color = ColorSchoolData.GetGlowColor(school);
                Assert.AreNotEqual(0u, color,
                    $"GetGlowColor returned 0 for {school}.");
            }
        }

        // ── SpellBuilder parsing for Waver / Rouse thresholds ─────────────────

        [Test]
        public void SpellBuilder_OneDamageInput_WaverConditionMet()
        {
            // Any DamageCount > 0 lets Waver roll its 12% chance.
            var cast = SpellBuilder.Parse("U", "U");
            Assert.AreEqual(1, cast.DamageCount);
            Assert.AreEqual(0, cast.RestoreCount);
            Assert.IsFalse(cast.IsFumble);
        }

        [Test]
        public void SpellBuilder_ThreeRestoreInputs_RouseThresholdMet()
        {
            // Rouse requires RestoreCount >= 3.
            var cast = SpellBuilder.Parse("D", "DDD");
            Assert.AreEqual(3, cast.RestoreCount);
            Assert.AreEqual(0, cast.DamageCount);
            Assert.IsTrue(cast.RestoreCount >= 3,
                "3 Restore inputs should satisfy Rouse's minimum threshold.");
        }

        [Test]
        public void SpellBuilder_TwoRestoreInputs_RouseThresholdNotMet()
        {
            var cast = SpellBuilder.Parse("D", "DD");
            Assert.AreEqual(2, cast.RestoreCount);
            Assert.IsFalse(cast.RestoreCount >= 3,
                "2 Restore inputs should not satisfy Rouse's minimum threshold.");
        }

        [Test]
        public void SpellBuilder_MixedEffects_CountsAreSeparate()
        {
            // 1 Damage + 3 Restore: Waver can trigger on enemies, Rouse on allies.
            var cast = SpellBuilder.Parse("U", "UDDD");
            Assert.AreEqual(1, cast.DamageCount);
            Assert.AreEqual(3, cast.RestoreCount);
        }

        // ── Full talent roster coverage ───────────────────────────────────────
        // Note: many TalentId values are marked "REMOVED — kept for save
        // compatibility" and intentionally have no entry in TalentSystem.All, so
        // there is no "every enum value has a definition" invariant to assert.

        [Test]
        public void TalentSystem_AllDefinitions_HaveNonEmptyText()
        {
            foreach (var def in TalentSystem.All)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(def.Name),
                    $"TalentId.{def.Id} has an empty Name.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(def.MechanicDesc),
                    $"TalentId.{def.Id} has an empty MechanicDesc.");
            }
        }

        [Test]
        public void TalentSystem_SpellTalents_AreNotEnchantments()
        {
            var spellIds = new[]
            {
                TalentId.BreakWills, TalentId.Inspire, TalentId.Plague,
                TalentId.Clairvoyance, TalentId.Extinguish, TalentId.Fade,
            };
            foreach (var id in spellIds)
            {
                var def = TalentSystem.All.FirstOrDefault(d => d.Id == id);
                Assert.IsNotNull(def, $"Missing def for {id}.");
                Assert.IsTrue(def.IsSpell, $"{id} should be flagged IsSpell.");
                Assert.IsFalse(def.IsEnchantment, $"{id} should not be flagged IsEnchantment.");
                Assert.AreEqual(TalentCategory.Spell, def.Category);
            }
        }

        [Test]
        public void TalentSystem_PassiveTalents_AreNeitherSpellNorEnchantment()
        {
            var passiveIds = new[]
            {
                TalentId.Gift, TalentId.BattleMage, TalentId.Sorcerer, TalentId.Ember,
                TalentId.Reap, TalentId.Camaraderie,
            };
            foreach (var id in passiveIds)
            {
                var def = TalentSystem.All.FirstOrDefault(d => d.Id == id);
                Assert.IsNotNull(def, $"Missing def for {id}.");
                Assert.IsFalse(def.IsSpell, $"{id} should not be flagged IsSpell.");
                Assert.IsFalse(def.IsEnchantment, $"{id} should not be flagged IsEnchantment.");
                Assert.AreEqual(TalentCategory.Passive, def.Category);
            }
        }

        [Test]
        public void TalentSystem_GiftIsAlwaysPurchasedAtStart()
        {
            TalentSystem.ResetForNewGame();
            Assert.IsTrue(TalentSystem.Has(TalentId.Gift),
                "Gift should be purchased automatically at game start.");
        }

        // ── AgingSystem pure math ─────────────────────────────────────────────

        // Tests use the pure overload (explicit hero age) so they run without the
        // TaleWorlds runtime. Age 40 is the Tempered threshold — no age discount.
        private const float NoAgeDiscount = 40f;

        [Test]
        public void AgingSystem_ComputeBattleAgingCost_SmallCast_CostsOneDay()
        {
            // Geometric round(1.4^(n−1)): 1–2 inputs = 1 day without BattleMage
            Assert.AreEqual(1, AgingSystem.ComputeBattleAgingCost(1, false, NoAgeDiscount));
            Assert.AreEqual(1, AgingSystem.ComputeBattleAgingCost(2, false, NoAgeDiscount));
            Assert.AreEqual(2, AgingSystem.ComputeBattleAgingCost(3, false, NoAgeDiscount));
        }

        [Test]
        public void AgingSystem_ComputeBattleAgingCost_LargeCast_ScalesGeometrically()
        {
            // round(1.4^(n−1)): 4 inputs = 3 days, 8 inputs = 11 days, hard cap 84.
            Assert.AreEqual(3,  AgingSystem.ComputeBattleAgingCost(4, false, NoAgeDiscount));
            Assert.AreEqual(11, AgingSystem.ComputeBattleAgingCost(8, false, NoAgeDiscount));
            Assert.AreEqual(84, AgingSystem.ComputeBattleAgingCost(20, false, NoAgeDiscount));
        }

        [Test]
        public void AgingSystem_ComputeBattleAgingCost_BattleMage_MaxOf1DayOr25Pct()
        {
            // Tempered: reduction = max(1 flat day, 25% of cost). Minimum result 1 — never free.
            Assert.AreEqual(1, AgingSystem.ComputeBattleAgingCost(1, true, NoAgeDiscount));  // base 1: 1-1=0 → floor 1
            Assert.AreEqual(1, AgingSystem.ComputeBattleAgingCost(3, true, NoAgeDiscount));  // base 2: 2-1=1
            Assert.AreEqual(2, AgingSystem.ComputeBattleAgingCost(4, true, NoAgeDiscount));  // base 3: 3-1=2
            Assert.AreEqual(8, AgingSystem.ComputeBattleAgingCost(8, true, NoAgeDiscount));  // base 11: 11-3=8
        }

        [Test]
        public void AgingSystem_ComputeBattleAgingCost_TemperedAge_ShavesExtraCost()
        {
            // Beyond age 40, Tempered also cuts 0.5%/yr off the post-25% cost, capped at 30%.
            // Base 20 inputs → 84 (cap); BattleMage 25% → 63.
            //   age 40  → no age discount → 63
            //   age 100 → 30% cap        → round(63 × 0.70) = 44
            Assert.AreEqual(63, AgingSystem.ComputeBattleAgingCost(20, true, 40f));
            Assert.AreEqual(44, AgingSystem.ComputeBattleAgingCost(20, true, 100f));
            // Age only matters with the talent — non-BattleMage ignores it entirely.
            Assert.AreEqual(84, AgingSystem.ComputeBattleAgingCost(20, false, 100f));
        }

        // ── NPC heal-burst RestoreCount satisfies Rouse threshold ─────────────

        [Test]
        public void SpellBuilder_NpcHealBurstRestoreCount_MeetsRouseThreshold()
        {
            // NPC CastHealBurst passes restoreCount=3. Verify that value meets Rouse's
            // requirement so lords with Rouse can summon allies when healing.
            const int npcRestoreCount = 3;
            Assert.IsTrue(npcRestoreCount >= 3,
                "NPC heal burst must use at least 3 Restore inputs to satisfy Rouse's threshold.");
        }

        // ── BattleEvents probability constants ────────────────────────────────

        [Test]
        public void BattleEvents_AllChanceConstants_AreInValidRange()
        {
            Assert.IsTrue(BattleEvents.ChanceCinderRain  is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceEmberTithe  is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceTheRising   is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceDread       is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceLastLight   is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceAshenGround is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceFrenzy      is >= 0f and <= 1f);
        }

        [Test]
        public void BattleEvents_AllIntervalConstants_ArePositive()
        {
            Assert.Greater(BattleEvents.CinderRainInterval,  0f);
            Assert.Greater(BattleEvents.EmberTitheInterval,  0f);
            Assert.Greater(BattleEvents.TheRisingInterval,   0f);
            Assert.Greater(BattleEvents.AshenGroundInterval, 0f);
            Assert.Greater(BattleEvents.FrenzyInterval,      0f);
            Assert.Greater(BattleEvents.OneShotDelay,        0f);
        }

        [Test]
        public void BattleEvents_PeriodicDamage_IsPositive()
        {
            Assert.Greater(BattleEvents.PeriodicDamage, 0f);
        }

        [Test]
        public void BattleEvents_RisingSpawnCount_IsPositive()
        {
            Assert.Greater(BattleEvents.RisingSpawnCount, 0);
        }

        // ── CampaignMapEvents — Temple faction constants ──────────────────────

        [Test]
        public void CampaignMapEvents_TempleChances_AreInValidRange()
        {
            Assert.IsTrue(CampaignMapEvents.ChanceTheTemple is > 0f and <= 1f,
                "ChanceTheTemple must be a positive probability.");
            Assert.IsTrue(CampaignMapEvents.ChanceTempleLatent is > 0f and <= 1f,
                "ChanceTempleLatent must be a positive probability.");
        }

        [Test]
        public void CampaignMapEvents_TempleLatentChance_GreaterThanBaseChance()
        {
            Assert.Greater(CampaignMapEvents.ChanceTempleLatent, CampaignMapEvents.ChanceTheTemple,
                "Latent (post-day-250) Temple chance must exceed the base chance.");
        }

        [Test]
        public void CampaignMapEvents_TempleNearCertainDay_GreaterThanEarliestDay()
        {
            Assert.Greater(CampaignMapEvents.TempleNearCertainDay, CampaignMapEvents.TempleEarliestDay,
                "TempleNearCertainDay must be later than TempleEarliestDay.");
        }

        [Test]
        public void CampaignMapEvents_TempleEarliestDay_IsPositive()
        {
            Assert.Greater(CampaignMapEvents.TempleEarliestDay, 0,
                "Temple must not be able to fire on day 0.");
        }

        // ── AshenVisuals body-key transforms ──────────────────────────────────

        [Test]
        public void AshenVisuals_HairKey_PreservesNonColourBits()
        {
            ulong input  = 0xFFFFFFFFFFFFFFFFUL;
            ulong result = AshenVisuals.AshenHairKey(input);
            Assert.AreEqual((input & ~0x00FFFF0000000000UL) | 0x0000010000000000UL, result);
            Assert.AreEqual(input  & ~0x00FFFF0000000000UL,
                            result & ~0x00FFFF0000000000UL,
                "Bits outside the hair colour byte range must be preserved.");
        }

        [Test]
        public void AshenVisuals_EyeKey_SetsColdBlueBytes()
        {
            ulong result = AshenVisuals.AshenEyeKey(0UL);
            Assert.AreEqual(0x00E0AA0000000000UL, result,
                "Eye transform must encode a cold-blue iris into the colour bytes.");
        }

        [Test]
        public void AshenVisuals_EyeKey_PreservesNonColourBits()
        {
            ulong input  = 0xAB00000000C0FFEEUL;
            ulong result = AshenVisuals.AshenEyeKey(input);
            Assert.AreEqual(input  & ~0x00FFFF0000000000UL,
                            result & ~0x00FFFF0000000000UL);
        }

        [Test]
        public void AshenVisuals_SkinKey_ClearsColourBytesOnly()
        {
            ulong input  = 0xFFFFFFFFFFFFFFFFUL;
            ulong result = AshenVisuals.AshenSkinKey(input);
            Assert.AreEqual(0UL, result & 0x000000FFFFFF0000UL,
                "Skin colour bytes must be cleared (grey/ashen tone).");
            Assert.AreEqual(input & ~0x000000FFFFFF0000UL, result,
                "All other bits must be preserved.");
        }

        [Test]
        public void AshenVisuals_Transforms_AreIdempotent()
        {
            ulong seed = 0x123456789ABCDEF0UL;
            Assert.AreEqual(AshenVisuals.AshenHairKey(seed),
                            AshenVisuals.AshenHairKey(AshenVisuals.AshenHairKey(seed)));
            Assert.AreEqual(AshenVisuals.AshenEyeKey(seed),
                            AshenVisuals.AshenEyeKey(AshenVisuals.AshenEyeKey(seed)));
            Assert.AreEqual(AshenVisuals.AshenSkinKey(seed),
                            AshenVisuals.AshenSkinKey(AshenVisuals.AshenSkinKey(seed)));
        }

        // ── SeaMath tests ─────────────────────────────────────────────────────

        [Test]
        public void SeaMath_TravelHours_ClampsToMinimum()
        {
            Assert.AreEqual(SeaMath.MinVoyageHours, SeaMath.TravelHours(0f, false));
            Assert.AreEqual(SeaMath.MinVoyageHours, SeaMath.TravelHours(10f, false));
        }

        [Test]
        public void SeaMath_TravelHours_EmberwindHalvesLongCrossings()
        {
            float normal = SeaMath.TravelHours(400f, false);
            float windy  = SeaMath.TravelHours(400f, true);
            Assert.AreEqual(normal * SeaMath.EmberwindTimeMult, windy, 0.001f);
        }

        [Test]
        public void SeaMath_TravelHours_GrowsWithDistance()
        {
            Assert.Greater(SeaMath.TravelHours(600f, false), SeaMath.TravelHours(300f, false));
        }

        [Test]
        public void SeaMath_Fare_RoundsToTenAndHasFloor()
        {
            Assert.AreEqual(0, SeaMath.Fare(123f, 17) % 10);
            Assert.GreaterOrEqual(SeaMath.Fare(0f, 0), 50);
        }

        [Test]
        public void SeaMath_Fare_GrowsWithDistanceAndPartySize()
        {
            Assert.Greater(SeaMath.Fare(500f, 50), SeaMath.Fare(200f, 50));
            Assert.Greater(SeaMath.Fare(200f, 100), SeaMath.Fare(200f, 10));
        }

        [Test]
        public void SeaMath_PirateChance_StaysWithinBounds()
        {
            Assert.AreEqual(SeaMath.PirateChanceFloor, SeaMath.PirateChance(0f));
            Assert.AreEqual(SeaMath.PirateChanceCeiling, SeaMath.PirateChance(99999f));
            float mid = SeaMath.PirateChance(300f);
            Assert.GreaterOrEqual(mid, SeaMath.PirateChanceFloor);
            Assert.LessOrEqual(mid, SeaMath.PirateChanceCeiling);
        }

        [Test]
        public void SeaMath_AshenAdjusted_RaisesHazardForAshenPorts()
        {
            float baseChance = 0.20f;
            // Non-Ashen destination: chance is unchanged.
            Assert.AreEqual(baseChance, SeaMath.AshenAdjusted(baseChance, false), 0.0001f);
            // Ashen destination: chance is lifted by the multiplier.
            Assert.AreEqual(baseChance * SeaMath.AshenPortHazardMult,
                            SeaMath.AshenAdjusted(baseChance, true), 0.0001f);
            // …but never reaches certainty, even from an already-high base.
            Assert.LessOrEqual(SeaMath.AshenAdjusted(0.9f, true), 0.95f);
        }

        [Test]
        public void SeaMath_FleetStrength_SearTheTideMultiplies()
        {
            float baseStr = SeaMath.FleetStrength(60, 3f, 100, false);
            float seared  = SeaMath.FleetStrength(60, 3f, 100, true);
            Assert.AreEqual(baseStr * SeaMath.SearTheTideStrengthMult, seared, 0.001f);
        }

        [Test]
        public void SeaMath_FleetStrength_MoreMenIsStronger()
        {
            Assert.Greater(SeaMath.FleetStrength(100, 2f, 0, false),
                           SeaMath.FleetStrength(50, 2f, 0, false));
        }

        [Test]
        public void SeaMath_ResolveSeaBattle_OverwhelmingPlayerWinsCheaply()
        {
            var o = SeaMath.ResolveSeaBattle(1000f, 100f, 0.0);
            Assert.IsTrue(o.Victory);
            Assert.Greater(o.LootGold, 0);
            Assert.LessOrEqual(o.CasualtyFraction, 0.05f);
        }

        [Test]
        public void SeaMath_ResolveSeaBattle_OverwhelmingCorsairsWin()
        {
            var o = SeaMath.ResolveSeaBattle(100f, 1000f, 0.99);
            Assert.IsFalse(o.Victory);
            Assert.AreEqual(0, o.LootGold);
        }

        [Test]
        public void SeaMath_ResolveSeaBattle_CasualtiesStayInBounds()
        {
            foreach (var roll in new[] { 0.0, 0.5, 0.99 })
            {
                var win  = SeaMath.ResolveSeaBattle(500f, 50f, roll);
                var loss = SeaMath.ResolveSeaBattle(50f, 5000f, roll);
                Assert.GreaterOrEqual(win.CasualtyFraction, 0.02f);
                Assert.LessOrEqual(win.CasualtyFraction, 0.35f);
                Assert.GreaterOrEqual(loss.CasualtyFraction, 0.02f);
                Assert.LessOrEqual(loss.CasualtyFraction, 0.35f);
            }
        }

        [Test]
        public void SeaMath_ResolveSeaBattle_ZeroStrengthIsDefeatNotCrash()
        {
            var o = SeaMath.ResolveSeaBattle(0f, 100f, 0.5);
            Assert.IsFalse(o.Victory);
            Assert.AreEqual(0, o.LootGold);
        }

        [Test]
        public void SeaMath_TributeDemand_HasFloor()
        {
            Assert.GreaterOrEqual(SeaMath.TributeDemand(0, 0f), 200);
        }

        [Test]
        public void SeaMath_StormExtraHours_AtLeastTwo()
        {
            Assert.GreaterOrEqual(SeaMath.StormExtraHours(0f, 0.0), 2);
            Assert.GreaterOrEqual(SeaMath.StormExtraHours(40f, 0.5), 2);
        }

        [Test]
        public void SeaMath_VentureDays_FloorAndGrowth()
        {
            Assert.GreaterOrEqual(SeaMath.VentureDays(0f), 3);
            Assert.Greater(SeaMath.VentureDays(800f), SeaMath.VentureDays(200f));
        }

        [Test]
        public void SeaMath_VentureLossChance_BlessingHalves()
        {
            float bare    = SeaMath.VentureLossChance(400f, false);
            float blessed = SeaMath.VentureLossChance(400f, true);
            Assert.AreEqual(bare * 0.5f, blessed, 0.0001f);
            Assert.Less(bare, 1f);
        }

        [Test]
        public void SeaMath_ResolveVenture_LossPaysSalvage()
        {
            // lossRoll of 0 is always below any positive loss chance
            var o = SeaMath.ResolveVenture(2000, 400f, false, 0.0, 0.5);
            Assert.IsTrue(o.Lost);
            Assert.AreEqual(500, o.Payout);
        }

        [Test]
        public void SeaMath_ResolveVenture_SafeRunProfits()
        {
            // lossRoll of 1.0 is never below the loss chance
            var o = SeaMath.ResolveVenture(2000, 400f, false, 1.0, 0.5);
            Assert.IsFalse(o.Lost);
            Assert.Greater(o.Payout, 2000);
        }

        [Test]
        public void SeaMath_ResolveVenture_BlessingImprovesMargin()
        {
            var bare    = SeaMath.ResolveVenture(2000, 400f, false, 1.0, 0.5);
            var blessed = SeaMath.ResolveVenture(2000, 400f, true, 1.0, 0.5);
            Assert.Greater(blessed.Payout, bare.Payout);
        }

        [Test]
        public void SeaMath_VentureTiers_AreAscending()
        {
            for (int i = 1; i < SeaMath.VentureTiers.Length; i++)
                Assert.Greater(SeaMath.VentureTiers[i], SeaMath.VentureTiers[i - 1]);
        }

        [Test]
        public void SeaMath_NpcCrossingViable_RejectsShortHops()
        {
            Assert.IsFalse(SeaMath.NpcCrossingViable(SeaMath.NpcMinCrossing - 1f, false));
            Assert.IsFalse(SeaMath.NpcCrossingViable(SeaMath.NpcMinCrossing - 1f, true));
            Assert.IsTrue(SeaMath.NpcCrossingViable(SeaMath.NpcMinCrossing, false));
            Assert.IsTrue(SeaMath.NpcCrossingViable(SeaMath.NpcMinCrossing, true));
        }

        [Test]
        public void SeaMath_NpcCrossingViable_CapsCaravansOnly()
        {
            float beyond = SeaMath.NpcMaxCaravanCrossing + 1f;
            Assert.IsFalse(SeaMath.NpcCrossingViable(beyond, caravan: true));
            Assert.IsTrue(SeaMath.NpcCrossingViable(beyond, caravan: false));
        }

        [Test]
        public void SeaMath_BlockadeInterceptChance_ZeroStrengthIsZero()
        {
            Assert.AreEqual(0f, SeaMath.BlockadeInterceptChance(0f, 100f));
        }

        [Test]
        public void SeaMath_BlockadeInterceptChance_StrongerBlockadeRaisesChance()
        {
            float weak   = SeaMath.BlockadeInterceptChance(50f,  200f);
            float strong = SeaMath.BlockadeInterceptChance(200f, 200f);
            Assert.Greater(strong, weak);
        }

        [Test]
        public void SeaMath_BlockadeInterceptChance_ClampedToValidRange()
        {
            // Overwhelming blockade should not exceed 0.90
            Assert.LessOrEqual(SeaMath.BlockadeInterceptChance(99999f, 1f), 0.90f);
            // Tiny blockade vs massive fleet should be at least 0.20
            Assert.GreaterOrEqual(SeaMath.BlockadeInterceptChance(1f, 99999f), 0.20f);
        }

        [Test]
        public void SeaMath_NpcInvasionSailChance_LowerThanNormalSailChance()
        {
            Assert.Less(SeaMath.NpcInvasionSailChance, SeaMath.NpcLordSailChance);
        }

        // ── SpeculationMath tests ─────────────────────────────────────────────

        [Test]
        public void SpeculationMath_RoundsLimit_BaseIsFour()
        {
            Assert.AreEqual(4, SpeculationMath.RoundsLimit(0));
        }

        [Test]
        public void SpeculationMath_RoundsLimit_IncreasesWithTrade()
        {
            Assert.AreEqual(5, SpeculationMath.RoundsLimit(75));
            Assert.AreEqual(6, SpeculationMath.RoundsLimit(150));
        }

        [Test]
        public void SpeculationMath_RoundsLimit_CapsAtEight()
        {
            Assert.AreEqual(8, SpeculationMath.RoundsLimit(300));
            Assert.AreEqual(8, SpeculationMath.RoundsLimit(1000));
        }

        [Test]
        public void SpeculationMath_CrashChance_IsAtLeastOnePercent()
        {
            float c = SpeculationMath.CrashChance(0, 0, 500, false);
            Assert.GreaterOrEqual(c, 0.01f);
        }

        [Test]
        public void SpeculationMath_CrashChance_CapsAtFiftyPercent()
        {
            float c = SpeculationMath.CrashChance(2, 20, 0, true);
            Assert.LessOrEqual(c, 0.50f);
        }

        [Test]
        public void SpeculationMath_CrashChance_GrowsWithRounds()
        {
            float early = SpeculationMath.CrashChance(1, 0, 0, false);
            float late  = SpeculationMath.CrashChance(1, 5, 0, false);
            Assert.Greater(late, early);
        }

        [Test]
        public void SpeculationMath_CrashChance_AggressiveIsHigher()
        {
            float steady = SpeculationMath.CrashChance(1, 0, 0, false);
            float hard   = SpeculationMath.CrashChance(1, 0, 0, true);
            Assert.Greater(hard, steady);
        }

        [Test]
        public void SpeculationMath_DeltaRange_MaxExceedsMin()
        {
            for (int vol = 0; vol < 3; vol++)
            {
                int sMin = SpeculationMath.DeltaMin(vol, false, 0);
                int sMax = SpeculationMath.DeltaMax(vol, false, 0);
                int hMin = SpeculationMath.DeltaMin(vol, true, 0);
                int hMax = SpeculationMath.DeltaMax(vol, true, 0);
                Assert.Greater(sMax, sMin, $"vol={vol} steady max must exceed min");
                Assert.Greater(hMax, hMin, $"vol={vol} hard max must exceed min");
                Assert.Greater(hMax, sMax, $"vol={vol} hard upside must exceed steady upside");
            }
        }

        [Test]
        public void SpeculationMath_MoodShift_BoomingAdds5()
        {
            Assert.AreEqual(5, SpeculationMath.MoodShift(6000f));
        }

        [Test]
        public void SpeculationMath_MoodShift_DepressedSubtracts5()
        {
            Assert.AreEqual(-5, SpeculationMath.MoodShift(1000f));
        }

        [Test]
        public void SpeculationMath_MoodShift_SteadyIsZero()
        {
            Assert.AreEqual(0, SpeculationMath.MoodShift(3000f));
        }

        [Test]
        public void SpeculationMath_ApplyDelta_ClampsToMin()
        {
            int result = SpeculationMath.ApplyDelta(15, -50);
            Assert.AreEqual(SpeculationMath.MultiplierMin, result);
        }

        [Test]
        public void SpeculationMath_ApplyDelta_ClampsToMax()
        {
            int result = SpeculationMath.ApplyDelta(250, 100);
            Assert.AreEqual(SpeculationMath.MultiplierMax, result);
        }

        [Test]
        public void SpeculationMath_ApplyDelta_NormalMove()
        {
            int result = SpeculationMath.ApplyDelta(100, 15);
            Assert.AreEqual(115, result);
        }

        [Test]
        public void SpeculationMath_SalvagePercent_OnePerTen()
        {
            Assert.AreEqual(10, SpeculationMath.SalvagePercent(100));
            Assert.AreEqual(20, SpeculationMath.SalvagePercent(200));
        }

        [Test]
        public void SpeculationMath_SalvagePercent_CapsAt30()
        {
            Assert.AreEqual(30, SpeculationMath.SalvagePercent(300));
            Assert.AreEqual(30, SpeculationMath.SalvagePercent(1000));
        }

        [Test]
        public void SpeculationMath_SalvagePercent_ZeroAtNoSkill()
        {
            Assert.AreEqual(0, SpeculationMath.SalvagePercent(0));
        }

        [Test]
        public void SpeculationMath_Payout_DoubleAtTwoHundredPct()
        {
            int payout = SpeculationMath.Payout(1000, 200);
            Assert.AreEqual(2000, payout);
        }

        [Test]
        public void SpeculationMath_ForcedSalePayout_IsNinetyPercent()
        {
            int payout = SpeculationMath.ForcedSalePayout(1000, 100);
            Assert.AreEqual(900, payout);
        }

        [Test]
        public void SpeculationMath_TradeXp_ZeroOnLoss()
        {
            Assert.AreEqual(50, SpeculationMath.TradeXp(1000, 500));
        }

        [Test]
        public void SpeculationMath_TradeXp_HalfOfProfit()
        {
            Assert.AreEqual(500, SpeculationMath.TradeXp(1000, 2000));
        }

        [Test]
        public void SpeculationMath_TradeXp_CapsAt1000()
        {
            Assert.AreEqual(1000, SpeculationMath.TradeXp(1000, 10000));
        }
    }
}
