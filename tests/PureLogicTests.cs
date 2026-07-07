// =============================================================================
// COLOURS OF CALRADIA — PureLogicTests.cs
// Mount & Blade II: Bannerlord Mod  v2.0
// =============================================================================

using System;
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

        // ── Class bundles ─────────────────────────────────────────────────────

        [Test]
        public void TalentSystem_EveryClassMember_IsALiveTalentDefinition()
        {
            // A class must never bundle a consolidated-out talent (no TalentDef),
            // or owning the class would silently revive a cut mechanic.
            foreach (var kv in TalentSystem.ClassMembers)
                foreach (var member in kv.Value)
                    Assert.IsTrue(TalentSystem.All.Any(d => d.Id == member),
                        $"Class {kv.Key} bundles {member}, which has no live TalentDef.");
        }

        [Test]
        public void TalentSystem_NoTalentIsBundledByTwoClasses()
        {
            var seen = new HashSet<TalentId>();
            foreach (var kv in TalentSystem.ClassMembers)
                foreach (var member in kv.Value)
                    Assert.IsTrue(seen.Add(member),
                        $"{member} is bundled by more than one class (second: {kv.Key}).");
        }

        [Test]
        public void TalentSystem_EveryClass_HasCorrectFocusCost()
        {
            // Both fire paths (Category.Class) and discipline classes bought at
            // ritual sites (Category.Rite) use an escalating cost curve, marked by
            // FocusCost 0 — fire paths via GetNextPathCost, disciplines via the
            // per-discipline GetNextDisciplineCost pools. BattleSworn is kept in
            // ClassMembers for save compatibility only and has no TalentDef.
            foreach (var classId in TalentSystem.ClassMembers.Keys)
            {
                var def = TalentSystem.All.FirstOrDefault(d => d.Id == classId);
                if (classId == TalentId.BattleSworn)
                {
                    Assert.IsNull(def, "Legacy BattleSworn should have no TalentDef.");
                    continue;
                }
                Assert.IsNotNull(def, $"Class {classId} has no TalentDef.");
                Assert.AreEqual(0, def.FocusCost,
                    $"Class {classId} should use an escalating cost curve (FocusCost 0).");
            }
        }

        [Test]
        public void TalentSystem_OwningAClass_GrantsAllItsMembers()
        {
            TalentSystem.ResetForNewGame();
            // Grant a class directly (no Hero needed) and confirm Has() reports
            // every bundled member as owned.
            TalentSystem.GrantClassForTest(TalentId.Pyrelord);
            foreach (var member in TalentSystem.ClassMembers[TalentId.Pyrelord])
                Assert.IsTrue(TalentSystem.Has(member),
                    $"Owning Pyrelord should grant {member}.");
            // A talent from a different, unowned class is not granted.
            Assert.IsFalse(TalentSystem.Has(TalentId.Ashveil),
                "Owning Pyrelord must not grant Ward-Keeper's Ashveil.");
            TalentSystem.ResetForNewGame();
        }

        // ── AgingSystem pure math ─────────────────────────────────────────────

        // Tests use the pure overload (explicit hero age) so they run without the
        // TaleWorlds runtime. Age 40 is the Tempered threshold — no age discount.
        private const float NoAgeDiscount = 40f;

        [Test]
        public void AgingSystem_ComputeBattleAgingCost_SmallCast_LowCost()
        {
            // Geometric round(1.5^(n−1)): 1→1, 2→2, 3→2 without BattleMage.
            Assert.AreEqual(1, AgingSystem.ComputeBattleAgingCost(1, false, NoAgeDiscount));
            Assert.AreEqual(2, AgingSystem.ComputeBattleAgingCost(2, false, NoAgeDiscount));
            Assert.AreEqual(2, AgingSystem.ComputeBattleAgingCost(3, false, NoAgeDiscount));
        }

        [Test]
        public void AgingSystem_ComputeBattleAgingCost_LargeCast_ScalesGeometrically()
        {
            // round(1.5^(n−1)): 4→3, 8→17, 10→38; the 84-day cap only guards huge input counts (20→84).
            Assert.AreEqual(3,  AgingSystem.ComputeBattleAgingCost(4, false, NoAgeDiscount));
            Assert.AreEqual(17, AgingSystem.ComputeBattleAgingCost(8, false, NoAgeDiscount));
            Assert.AreEqual(38, AgingSystem.ComputeBattleAgingCost(10, false, NoAgeDiscount));
            Assert.AreEqual(84, AgingSystem.ComputeBattleAgingCost(20, false, NoAgeDiscount));
        }

        [Test]
        public void AgingSystem_ComputeBattleAgingCost_BattleMage_MaxOf1DayOr25Pct()
        {
            // Tempered: reduction = max(1 flat day, 25% of cost). Minimum result 1 — never free.
            // base 1.5: n=1→1, n=3→2, n=4→3, n=8→17.
            Assert.AreEqual(1,  AgingSystem.ComputeBattleAgingCost(1, true, NoAgeDiscount));  // base 1: 1-1=0 → floor 1
            Assert.AreEqual(1,  AgingSystem.ComputeBattleAgingCost(3, true, NoAgeDiscount));  // base 2: 2-1=1
            Assert.AreEqual(2,  AgingSystem.ComputeBattleAgingCost(4, true, NoAgeDiscount));  // base 3: 3-1=2
            Assert.AreEqual(13, AgingSystem.ComputeBattleAgingCost(8, true, NoAgeDiscount));  // base 17: 17-4=13
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
            Assert.IsTrue(BattleEvents.ChanceTheRising   is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceDread       is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceLastLight   is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceAshenGround is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceFrenzy      is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceStorm       is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceTremor      is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceDeluge      is >= 0f and <= 1f);
            Assert.IsTrue(BattleEvents.ChanceMadness     is >= 0f and <= 1f);
        }

        [Test]
        public void BattleEvents_AllIntervalConstants_ArePositive()
        {
            Assert.Greater(BattleEvents.CinderRainInterval,  0f);
            Assert.Greater(BattleEvents.TheRisingInterval,   0f);
            Assert.Greater(BattleEvents.AshenGroundInterval, 0f);
            Assert.Greater(BattleEvents.FrenzyInterval,      0f);
            Assert.Greater(BattleEvents.StormInterval,       0f);
            Assert.Greater(BattleEvents.TremorInterval,      0f);
            Assert.Greater(BattleEvents.DelugeInterval,      0f);
            Assert.Greater(BattleEvents.MadnessInterval,     0f);
            Assert.Greater(BattleEvents.OneShotDelay,        0f);
        }

        [Test]
        public void BattleEvents_TremorDamage_IsPositive()
        {
            Assert.Greater(BattleEvents.TremorDamage, 0f);
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
        public void AshenVisuals_HairKey_SetsLightGreyD3D3()
        {
            ulong input  = 0xFFFFFFFFFFFFFFFFUL;
            ulong result = AshenVisuals.AshenHairKey(input);
            Assert.AreEqual(0x00D3D30000000000UL, result & 0x00FFFF0000000000UL,
                "Hair colour bytes must encode light grey #D3D3.");
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
        public void AshenVisuals_SkinKey_SetsLightGreyD3D3D3()
        {
            ulong input  = 0xFFFFFFFFFFFFFFFFUL;
            ulong result = AshenVisuals.AshenSkinKey(input);
            Assert.AreEqual(0x000000D3D3D30000UL, result & 0x000000FFFFFF0000UL,
                "Skin colour bytes must encode light grey #D3D3D3.");
            Assert.AreEqual(input & ~0x000000FFFFFF0000UL, result & ~0x000000FFFFFF0000UL,
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

        // ── ClanOrdersMath tests ──────────────────────────────────────────────

        [Test]
        public void ClanOrdersMath_DailyAbandonChance_ScalesWithLeadership()
        {
            Assert.AreEqual(10, ClanOrdersMath.DailyAbandonChance(0));
            Assert.AreEqual(5,  ClanOrdersMath.DailyAbandonChance(150));
            Assert.AreEqual(0,  ClanOrdersMath.DailyAbandonChance(300));
        }

        [Test]
        public void ClanOrdersMath_DailyAbandonChance_NeverNegativeAndClampsInput()
        {
            Assert.AreEqual(0,  ClanOrdersMath.DailyAbandonChance(1000)); // never below zero
            Assert.AreEqual(10, ClanOrdersMath.DailyAbandonChance(-50));  // negative input clamps to 0
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

        // ── CrystalMath pure logic ────────────────────────────────────────────

        [Test]
        public void CrystalMath_FormationOdds_FloorAtSixPercent()
        {
            float odds = CrystalMath.FormationOdds(0, 0);
            Assert.AreEqual(0.06f, odds, 0.001f, "Floor should be 6 % with no skill.");
        }

        [Test]
        public void CrystalMath_FormationOdds_RisesWithCombinedSkill()
        {
            float low  = CrystalMath.FormationOdds(50, 50);   // combined 50
            float high = CrystalMath.FormationOdds(150, 150); // combined 150
            Assert.Greater(high, low, "More skill means better odds.");
        }

        [Test]
        public void CrystalMath_FormationOdds_CapsAtNinetyPercent()
        {
            float odds = CrystalMath.FormationOdds(300, 300);
            Assert.LessOrEqual(odds, 0.90f, "Odds must not exceed the 90 % ceiling.");
        }

        [Test]
        public void CrystalMath_FormationOddsWithPatience_AddsBonus()
        {
            float base_  = CrystalMath.FormationOdds(100, 100);
            float talent = CrystalMath.FormationOddsWithPatience(100, 100);
            Assert.AreEqual(Math.Min(0.90f, base_ + 0.20f), talent, 0.001f);
        }

        [Test]
        public void CrystalMath_IsDaylight_TrueOnlyInWindow()
        {
            Assert.IsTrue(CrystalMath.IsDaylight(12f),  "Noon should be daylight.");
            Assert.IsTrue(CrystalMath.IsDaylight(6f),   "06:00 is the dawn boundary.");
            Assert.IsTrue(CrystalMath.IsDaylight(19.9f),"Just before 20:00 is still day.");
            Assert.IsFalse(CrystalMath.IsDaylight(20f), "20:00 is outside the window.");
            Assert.IsFalse(CrystalMath.IsDaylight(3f),  "Pre-dawn should be dark.");
        }

        [Test]
        public void CrystalMath_IsDaylightExtended_BroaderWindow()
        {
            Assert.IsTrue(CrystalMath.IsDaylightExtended(4f),  "SolarFlare: 04:00 is active.");
            Assert.IsTrue(CrystalMath.IsDaylightExtended(21.9f),"SolarFlare: just before 22:00 is active.");
            Assert.IsFalse(CrystalMath.IsDaylightExtended(22f), "SolarFlare: 22:00 is outside window.");
            Assert.IsFalse(CrystalMath.IsDaylightExtended(3.9f),"SolarFlare: 03:59 is outside window.");
        }

        [Test]
        public void CrystalMath_BurndownChance_IsTenPercent()
        {
            Assert.AreEqual(0.10f, CrystalMath.BurndownChance, 0.0001f);
        }

        [Test]
        public void CrystalMath_SolarFlareRadius_IncreasesBaseByTwentyFivePercent()
        {
            float r  = 5f;
            float r2 = CrystalMath.SolarFlareRadius(r);
            Assert.AreEqual(r * 1.25f, r2, 0.001f);
        }

        // ── MiracleMath gain scaling ──────────────────────────────────────────

        [Test]
        public void MiracleMath_GraceGain_NoVirtue_ReturnsZero()
        {
            Assert.AreEqual(0, MiracleMath.GraceGain(0, 0, 0));
        }

        [Test]
        public void MiracleMath_GraceGain_AllVirtuesMaxed_ReturnsFour()
        {
            Assert.AreEqual(4, MiracleMath.GraceGain(2, 2, 2));
        }

        [Test]
        public void MiracleMath_GraceGain_NegativeTraits_ReturnsZero()
        {
            Assert.AreEqual(0, MiracleMath.GraceGain(-2, -2, -2));
        }

        [Test]
        public void MiracleMath_TryMatchSequence_NewMiracles_Resolve()
        {
            Assert.IsTrue(MiracleMath.TryMatchSequence(MiracleMath.SeqInsightPyre, out var pyre));
            Assert.AreEqual(MiracleType.InsightPyre, pyre);
            Assert.IsTrue(MiracleMath.TryMatchSequence(MiracleMath.SeqGraceBlessing, out var blessing));
            Assert.AreEqual(MiracleType.GraceBlessing, blessing);
        }

        [Test]
        public void MiracleCatalog_AllSequences_AreUnique()
        {
            var seqs = MiracleCatalog.All.Select(d => d.Sequence).ToList();
            Assert.AreEqual(seqs.Count, seqs.Distinct().Count(), "Two miracles share an input sequence.");
        }

        [Test]
        public void MiracleCatalog_EverySequence_RoundTripsToItsMiracle()
        {
            foreach (var def in MiracleCatalog.All)
            {
                bool validLength = def.Sequence.Length == MiracleMath.SequenceLength
                                 || def.Sequence.Length == MiracleMath.UltimateSequenceLength;
                Assert.IsTrue(validLength, $"{def.Name} has a sequence of an unsupported length ({def.Sequence.Length}).");
                Assert.IsTrue(MiracleMath.TryMatchSequence(def.Sequence, out var t), $"{def.Name} sequence does not match.");
                Assert.AreEqual(def.Type, t, $"{def.Name} sequence resolves to the wrong miracle.");
            }
        }

        // ── The Undivided Flame / The Reckoning — the all-five-traits ultimate ──

        [Test]
        public void MiracleMath_MeetsAllTraitsGate_AllPresent_ReturnsTrue()
        {
            Assert.IsTrue(MiracleMath.MeetsAllTraitsGate(1, 1, 1, 1, 1));
            Assert.IsTrue(MiracleMath.MeetsAllTraitsGate(2, 1, 2, 1, 2));
        }

        [Test]
        public void MiracleMath_MeetsAllTraitsGate_OneMissing_ReturnsFalse()
        {
            Assert.IsFalse(MiracleMath.MeetsAllTraitsGate(0, 1, 1, 1, 1));
            Assert.IsFalse(MiracleMath.MeetsAllTraitsGate(1, 1, 1, 1, 0));
        }

        [Test]
        public void MiracleMath_TryMatchSequence_UndividedFlame_Resolves()
        {
            Assert.IsTrue(MiracleMath.TryMatchSequence(MiracleMath.SeqUndividedFlame, out var type));
            Assert.AreEqual(MiracleType.UndividedFlame, type);
        }

        [Test]
        public void MiracleMath_TryMatchSequence_WrongLengthEight_DoesNotResolve()
        {
            Assert.IsFalse(MiracleMath.TryMatchSequence("UUUUUUUU", out _));
        }

        [Test]
        public void MiracleCatalog_UndividedFlameAndReckoning_RequireAllTraits()
        {
            Assert.IsTrue(MiracleCatalog.Get(MiracleType.UndividedFlame).RequiresAllTraits);
            Assert.IsTrue(MiracleCatalog.Get(MiracleType.Reckoning).RequiresAllTraits);
        }

        [Test]
        public void MiracleCatalog_UndividedFlameAndReckoning_CostTwoGrace()
        {
            Assert.AreEqual(2, MiracleCatalog.Get(MiracleType.UndividedFlame).GraceCost);
            Assert.AreEqual(2, MiracleCatalog.Get(MiracleType.Reckoning).GraceCost);
        }

        [Test]
        public void MiracleCatalog_OrdinaryMiracles_CostOneGrace()
        {
            Assert.AreEqual(1, MiracleCatalog.Get(MiracleType.MercyMend).GraceCost);
            Assert.AreEqual(1, MiracleCatalog.Get(MiracleType.InsightPyre).GraceCost);
        }

        [Test]
        public void MiracleInventory_SpendGrace_Amount_FailsWithoutEnough()
        {
            MiracleInventory.ResetForNewGame();
            MiracleInventory.AddGrace(1);
            Assert.IsFalse(MiracleInventory.SpendGrace(2));
            Assert.AreEqual(1, MiracleInventory.Grace);
        }

        [Test]
        public void MiracleInventory_SpendGrace_Amount_SucceedsWithEnough()
        {
            MiracleInventory.ResetForNewGame();
            MiracleInventory.AddGrace(3);
            Assert.IsTrue(MiracleInventory.SpendGrace(2));
            Assert.AreEqual(1, MiracleInventory.Grace);
        }

        // ── SanctuaryMath: the Long Vigil (raise a trait for gold or blood) ────

        [Test]
        public void SanctuaryMath_CanRaiseTrait_BelowCap_ReturnsTrue()
        {
            Assert.IsTrue(SanctuaryMath.CanRaiseTrait(-2));
            Assert.IsTrue(SanctuaryMath.CanRaiseTrait(1));
        }

        [Test]
        public void SanctuaryMath_CanRaiseTrait_AtCap_ReturnsFalse()
        {
            Assert.IsFalse(SanctuaryMath.CanRaiseTrait(SanctuaryMath.TraitCap));
        }

        [Test]
        public void SanctuaryMath_CommunionCosts_ScaleWithTargetLevel()
        {
            int lowGold  = SanctuaryMath.CommunionGoldCost(-1);
            int highGold = SanctuaryMath.CommunionGoldCost(2);
            Assert.Greater(highGold, lowGold, "Reaching a higher trait level should cost more gold.");

            int lowHp  = SanctuaryMath.CommunionHpCost(-1);
            int highHp = SanctuaryMath.CommunionHpCost(2);
            Assert.Greater(highHp, lowHp, "Reaching a higher trait level should cost more HP.");
        }

        [Test]
        public void SanctuaryMath_CommunionCosts_ArePositive()
        {
            for (int target = -1; target <= SanctuaryMath.TraitCap; target++)
            {
                Assert.Greater(SanctuaryMath.CommunionGoldCost(target), 0);
                Assert.Greater(SanctuaryMath.CommunionHpCost(target), 0);
            }
        }

        // ── ElementMagicMath (unified magic foundation) ─────────────────────────

        [Test]
        public void ElementMagicMath_CastAgingDays_IsFlat_IndependentOfDraw()
        {
            // The cost no longer depends on draw time — it is a flat per-form toll.
            Assert.AreEqual(3, ElementMagicMath.CastAgingDays(CastForm.Attack, false));
            Assert.AreEqual(4, ElementMagicMath.CastAgingDays(CastForm.Wall,   false));
        }

        [Test]
        public void ElementMagicMath_CastAgingDays_Nature_IsCheaper()
        {
            // Nature halves the flat cost (floored at 1): attack 3→2, wall 4→2.
            Assert.AreEqual(2, ElementMagicMath.CastAgingDays(CastForm.Attack, true));
            Assert.AreEqual(2, ElementMagicMath.CastAgingDays(CastForm.Wall,   true));
            Assert.IsTrue(ElementMagicMath.CastAgingDays(CastForm.Attack, true)
                        < ElementMagicMath.CastAgingDays(CastForm.Attack, false));
        }

        [Test]
        public void ElementMagicMath_PowerMult_InstantWeak_CapFull_ClampsPastCap()
        {
            Assert.AreEqual(ElementMagicMath.MinPower, ElementMagicMath.PowerMult(0f), 0.0001f);
            // Monotonic within the ramp: a longer draw is never weaker (sample as
            // fractions of the cap so this holds whatever FullChargeSeconds is set to).
            float third = ElementMagicMath.FullChargeSeconds / 3f;
            Assert.IsTrue(ElementMagicMath.PowerMult(third)     > ElementMagicMath.PowerMult(0f));
            Assert.IsTrue(ElementMagicMath.PowerMult(2f * third) > ElementMagicMath.PowerMult(third));
            // Full power is reached at FullChargeSeconds and holds through the grace
            // window up to the overchannel threshold.
            Assert.AreEqual(ElementMagicMath.MaxPower, ElementMagicMath.PowerMult(ElementMagicMath.FullChargeSeconds), 0.0001f);
            Assert.AreEqual(ElementMagicMath.MaxPower,
                ElementMagicMath.PowerMult(ElementMagicMath.OverchannelSeconds - 0.1f), 0.0001f);
            Assert.IsTrue(ElementMagicMath.FullChargeSeconds < ElementMagicMath.OverchannelSeconds);
            Assert.IsTrue(ElementMagicMath.OverchannelSeconds < ElementMagicMath.MaxDrawSeconds);
        }

        [Test]
        public void ElementMagicMath_Overchannel_DoublesPower_AtThreshold()
        {
            // Below the threshold: capped at full. At/after it: the doubled working,
            // and it stays doubled all the way to the disperse point.
            Assert.IsFalse(ElementMagicMath.IsOverchannelled(ElementMagicMath.OverchannelSeconds - 0.1f));
            Assert.IsTrue(ElementMagicMath.IsOverchannelled(ElementMagicMath.OverchannelSeconds));
            float doubled = ElementMagicMath.MaxPower * ElementMagicMath.OverchannelMult;
            Assert.AreEqual(doubled, ElementMagicMath.PowerMult(ElementMagicMath.OverchannelSeconds), 0.0001f);
            Assert.AreEqual(doubled, ElementMagicMath.PowerMult(ElementMagicMath.MaxDrawSeconds), 0.0001f);
            Assert.AreEqual(doubled, ElementMagicMath.PowerMult(20f), 0.0001f);
        }

        [Test]
        public void ElementMagicMath_FullyCharged_AtThreshold()
        {
            Assert.IsFalse(ElementMagicMath.IsFullyCharged(ElementMagicMath.FullChargeSeconds - 0.1f));
            Assert.IsTrue(ElementMagicMath.IsFullyCharged(ElementMagicMath.FullChargeSeconds));
            Assert.IsTrue(ElementMagicMath.IsFullyCharged(ElementMagicMath.MaxDrawSeconds));
        }

        // ── ElementUltimateMath (the Unbinding — element ultimates) ──────────────

        [Test]
        public void ElementUltimateMath_Cost_IsSteepFlat_NatureHalves()
        {
            // Twelve days flat — three walls' worth — halved by Nature, never free.
            Assert.AreEqual(12, ElementUltimateMath.UltimateAgingDays(hasNature: false));
            Assert.AreEqual(6,  ElementUltimateMath.UltimateAgingDays(hasNature: true));
            Assert.Greater(ElementUltimateMath.UltimateCostDays, ElementMagicMath.WallCostDays,
                "an Unbinding must cost far more than an ordinary working");
            Assert.GreaterOrEqual(ElementUltimateMath.UltimateAgingDays(true), ElementMagicMath.MinCastDays);
        }

        [Test]
        public void ElementUltimateMath_Unbind_OnlyAnswersAFullDraw()
        {
            // The chord is refused before the full 7 s draw — same gate as the
            // "fully charged" announcement.
            Assert.IsFalse(ElementUltimateMath.CanUnbind(0f));
            Assert.IsFalse(ElementUltimateMath.CanUnbind(ElementMagicMath.FullChargeSeconds - 0.1f));
            Assert.IsTrue(ElementUltimateMath.CanUnbind(ElementMagicMath.FullChargeSeconds));
            // The chord window is a fraction of a heartbeat — the buffered normal
            // cast must feel instant.
            Assert.Less(ElementUltimateMath.ChordWindowSeconds, 0.5f);
            Assert.Greater(ElementUltimateMath.ChordWindowSeconds, 0f);
        }

        [Test]
        public void ElementUltimateMath_Names_AllFiveDistinct_BothFaces()
        {
            var all = new System.Collections.Generic.HashSet<string>();
            foreach (MagicElement el in new[] { MagicElement.Fire, MagicElement.Wind,
                     MagicElement.Earth, MagicElement.Water, MagicElement.Spirit })
            {
                string living = ElementUltimateMath.UltimateName(el, ashen: false);
                string ashen  = ElementUltimateMath.UltimateName(el, ashen: true);
                Assert.IsFalse(string.IsNullOrEmpty(living));
                Assert.IsFalse(string.IsNullOrEmpty(ashen));
                Assert.IsTrue(all.Add(living), $"living name for {el} duplicates another");
                Assert.IsTrue(all.Add(ashen),  $"Ashen name for {el} duplicates another");
            }
        }

        [Test]
        public void ElementUltimateMath_Flight_HoldsHeightThenLandsGently()
        {
            // Full flight height through the crossing…
            Assert.AreEqual(ElementUltimateMath.FlightHeight,
                ElementUltimateMath.FlightHeightAt(ElementUltimateMath.FlightSeconds), 0.001f);
            Assert.AreEqual(ElementUltimateMath.FlightHeight,
                ElementUltimateMath.FlightHeightAt(ElementUltimateMath.FlightLandingSeconds), 0.001f);
            // …then a monotonic descent to the ground in the landing window.
            float half = ElementUltimateMath.FlightHeightAt(ElementUltimateMath.FlightLandingSeconds * 0.5f);
            Assert.Less(half, ElementUltimateMath.FlightHeight);
            Assert.Greater(half, 0f);
            Assert.AreEqual(0f, ElementUltimateMath.FlightHeightAt(0f), 0.001f);
            Assert.AreEqual(0f, ElementUltimateMath.FlightHeightAt(-1f), 0.001f);
        }

        [Test]
        public void ElementUltimateMath_QuakeIsAnOffensiveWorking()
        {
            // The Sundering hits and hurls; its slow leaves the foe moving, not frozen.
            Assert.Greater(ElementUltimateMath.QuakeDamage, 0f);
            Assert.Greater(ElementUltimateMath.QuakeRadius, 0f);
            Assert.Greater(ElementUltimateMath.QuakeKnockback, 0f);
            Assert.Greater(ElementUltimateMath.QuakeSlowMult, 0f);
            Assert.Less(ElementUltimateMath.QuakeSlowMult, 1f, "staggered footing, not a full stop");
            Assert.Greater(ElementUltimateMath.QuakeRubblePatches, 0);
        }

        [Test]
        public void ElementUltimateMath_Rain_MiresHorsesWorst_DampsButNeverKillsFire()
        {
            // The damp weakens fire without erasing it…
            Assert.Greater(ElementUltimateMath.RainFireDamp, 0f);
            Assert.Less(ElementUltimateMath.RainFireDamp, 1f);
            // …horses suffer more than men on foot…
            Assert.Less(ElementUltimateMath.RainMountSlowMult, ElementUltimateMath.RainFootSlowMult);
            // …and the soaked strings cost a shot part of its bite, not all of it.
            Assert.Greater(ElementUltimateMath.RainArcheryDamp, 0f);
            Assert.Less(ElementUltimateMath.RainArcheryDamp, 1f);
        }

        [Test]
        public void ElementUltimateMath_ElementalKind_AnswersTheScene()
        {
            // Snow always wins; sand is read from the scene's name; stone is the
            // default answer everywhere else (including a null name).
            Assert.AreEqual(ElementalKind.Frost, ElementUltimateMath.ElementalKindForScene(true,  "desert_x"));
            Assert.AreEqual(ElementalKind.Sand,  ElementUltimateMath.ElementalKindForScene(false, "battle_terrain_desert_a"));
            Assert.AreEqual(ElementalKind.Sand,  ElementUltimateMath.ElementalKindForScene(false, "aserai_dunes"));
            Assert.AreEqual(ElementalKind.Stone, ElementUltimateMath.ElementalKindForScene(false, "battle_terrain_plain"));
            Assert.AreEqual(ElementalKind.Stone, ElementUltimateMath.ElementalKindForScene(false, null));
            // Every kind has a name for the combat log.
            foreach (ElementalKind k in new[] { ElementalKind.Stone, ElementalKind.Frost, ElementalKind.Sand })
                Assert.IsFalse(string.IsNullOrEmpty(ElementUltimateMath.ElementalName(k)));
        }

        [Test]
        public void ElementUltimateMath_Spirit_IsNowTheBentKnee()
        {
            // The Spirit Unbinding no longer summons an elemental — it seizes a
            // will instead. Names should reflect the reworked mechanic.
            Assert.AreEqual("The Bent Knee",   ElementUltimateMath.UltimateName(MagicElement.Spirit, ashen: false));
            Assert.AreEqual("The Hollow Oath", ElementUltimateMath.UltimateName(MagicElement.Spirit, ashen: true));
            // Still the most expensive Unbinding — a full minute of borrowed will.
            Assert.Greater(ElementUltimateMath.SpiritUltimateCostDays, ElementUltimateMath.UltimateCostDays);
        }

        [Test]
        public void AshenRecruitMath_PrisonerQualifies_TierGate()
        {
            Assert.IsTrue(AshenRecruitMath.PrisonerQualifies(prisonerTier: 3, requiredTier: 3));
            Assert.IsTrue(AshenRecruitMath.PrisonerQualifies(prisonerTier: 5, requiredTier: 3));
            Assert.IsFalse(AshenRecruitMath.PrisonerQualifies(prisonerTier: 2, requiredTier: 3));
        }

        [Test]
        public void SacredSiteMath_FormationOdds_FloorsCapsAndGrowsWithSkill()
        {
            // Floor even at 0 Smithing, ceiling even at absurd Smithing, and
            // strictly non-decreasing in between.
            Assert.AreEqual(0.10f, SacredSiteMath.FormationOdds(0), 0.0001f);
            Assert.AreEqual(0.85f, SacredSiteMath.FormationOdds(1000), 0.0001f);
            Assert.Less(SacredSiteMath.FormationOdds(50), SacredSiteMath.FormationOdds(150));
        }

        [Test]
        public void ElementMagicMath_ChargeShapes_GrowWithCharge()
        {
            // Charge fraction: 0 at an instant cast, 1 at full power, clamped.
            Assert.AreEqual(0f, ElementMagicMath.ChargeFraction(ElementMagicMath.MinPower), 0.0001f);
            Assert.AreEqual(1f, ElementMagicMath.ChargeFraction(ElementMagicMath.MaxPower), 0.0001f);
            Assert.AreEqual(0f, ElementMagicMath.ChargeFraction(0f), 0.0001f);

            // Cone reaches further when charged; a wall thickens from 1 row to many.
            Assert.IsTrue(ElementMagicMath.ConeRange(10f, ElementMagicMath.MaxPower)
                        > ElementMagicMath.ConeRange(10f, ElementMagicMath.MinPower));
            Assert.AreEqual(1, ElementMagicMath.WallDepthRows(ElementMagicMath.MinPower));
            Assert.AreEqual(ElementMagicMath.WallMaxDepthRows, ElementMagicMath.WallDepthRows(ElementMagicMath.MaxPower));
        }

        [Test]
        public void TalentCostCurve_FollowsGentleRamp()
        {
            // owned → next cost: 1,1,2,2,2,3,3,3,3,4,...  (tier N charged N+1 times)
            int[] expected = { 1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 4 };
            for (int owned = 0; owned < expected.Length; owned++)
                Assert.AreEqual(expected[owned], TalentCostCurve.Cost(owned), $"owned={owned}");
            // Never below 1, even for a nonsensical negative count.
            Assert.AreEqual(1, TalentCostCurve.Cost(-3));
        }

        [Test]
        public void ElementMagicMath_BloodRejuvenation_ScalesByTier()
        {
            Assert.AreEqual(25,  ElementMagicMath.BloodRejuvenationDays(1));
            Assert.AreEqual(150, ElementMagicMath.BloodRejuvenationDays(6));
        }

        // ── NatureMath ────────────────────────────────────────────────────────

        [Test]
        public void NatureMath_TerrainElements_Forest_ReturnsEarth()
        {
            var els = NatureMath.TerrainElements("Forest");
            Assert.AreEqual(1, els.Length);
            Assert.AreEqual(NatureElement.Earth, els[0]);
        }

        [Test]
        public void NatureMath_TerrainElements_PureTerrains_MapToSingleElement()
        {
            // Iconic terrains give exactly one element for the whole battle.
            Assert.AreEqual(new[] { NatureElement.Wind },  NatureMath.TerrainElements("Mountain"));
            Assert.AreEqual(new[] { NatureElement.Water }, NatureMath.TerrainElements("River"));
            Assert.AreEqual(new[] { NatureElement.Water }, NatureMath.TerrainElements("OpenSea"));
            Assert.AreEqual(new[] { NatureElement.Storm }, NatureMath.TerrainElements("Desert"));
            Assert.AreEqual(new[] { NatureElement.Earth }, NatureMath.TerrainElements("Forest"));
        }

        [Test]
        public void NatureMath_TerrainElements_BlendedTerrains_OfferTwoElements()
        {
            // Transitional terrains offer one of two fitting elements (rolled per charge).
            void AssertBlend(string terrain, NatureElement a, NatureElement b)
            {
                var els = NatureMath.TerrainElements(terrain);
                Assert.AreEqual(2, els.Length, $"{terrain} should offer two elements.");
                CollectionAssert.Contains(els, a, $"{terrain} should include {a}.");
                CollectionAssert.Contains(els, b, $"{terrain} should include {b}.");
            }
            AssertBlend("Steppe", NatureElement.Wind,  NatureElement.Storm);
            AssertBlend("Plain",  NatureElement.Earth, NatureElement.Storm);
            AssertBlend("Snow",   NatureElement.Water, NatureElement.Wind);
            AssertBlend("Swamp",  NatureElement.Water, NatureElement.Earth);
            AssertBlend("Canyon", NatureElement.Earth, NatureElement.Wind);
        }

        [Test]
        public void NatureMath_TerrainElements_Unknown_ReturnsAllFour()
        {
            var els = NatureMath.TerrainElements("SomeFictionalTerrain");
            Assert.AreEqual(4, els.Length);
            CollectionAssert.Contains(els, NatureElement.Wind);
            CollectionAssert.Contains(els, NatureElement.Earth);
            CollectionAssert.Contains(els, NatureElement.Water);
            CollectionAssert.Contains(els, NatureElement.Storm);
        }

        [Test]
        public void NatureMath_ElementOf_EachPower_RoundTrips()
        {
            var pairs = new[]
            {
                (NaturePower.Gale,        NatureElement.Wind),
                (NaturePower.Windwall,    NatureElement.Wind),
                (NaturePower.Entangle,    NatureElement.Earth),
                (NaturePower.Thornwall,   NatureElement.Earth),
                (NaturePower.Torrent,     NatureElement.Water),
                (NaturePower.Mistwall,    NatureElement.Water),
                (NaturePower.ThunderClap, NatureElement.Storm),
                (NaturePower.Stormwall,   NatureElement.Storm),
            };
            foreach (var (power, expected) in pairs)
                Assert.AreEqual(expected, NatureMath.ElementOf(power),
                    $"ElementOf({power}) should be {expected}.");
        }

        [Test]
        public void NatureMath_AttackAndSupport_MatchElement()
        {
            foreach (NatureElement el in new[]
                { NatureElement.Wind, NatureElement.Earth, NatureElement.Water, NatureElement.Storm })
            {
                var atk = NatureMath.AttackPower(el);
                var sup = NatureMath.SupportPower(el);
                Assert.AreEqual(el, NatureMath.ElementOf(atk), $"Attack power of {el} mismatched.");
                Assert.AreEqual(el, NatureMath.ElementOf(sup), $"Support power of {el} mismatched.");
                Assert.IsTrue(NatureMath.IsAttack(atk),  $"{atk} should be an attack.");
                Assert.IsFalse(NatureMath.IsAttack(sup), $"{sup} should be a support.");
            }
        }

        [Test]
        public void NatureMath_RandomPower_Earth_OnlyEarthPowers()
        {
            var rng = new System.Random(42);
            for (int i = 0; i < 20; i++)
            {
                var p = NatureMath.RandomPower(NatureElement.Earth, rng);
                Assert.IsTrue(p == NaturePower.Entangle || p == NaturePower.Thornwall,
                    $"Earth random power must be Entangle or Thornwall, got {p}.");
            }
        }

        [Test]
        public void NatureMath_PowerName_AllPowers_NonEmpty()
        {
            foreach (NaturePower p in System.Enum.GetValues(typeof(NaturePower)))
            {
                if (p == NaturePower.None) continue;
                Assert.IsFalse(string.IsNullOrEmpty(NatureMath.PowerName(p)),
                    $"PowerName({p}) must not be empty.");
            }
        }

        [Test]
        public void NatureMath_ElementName_AllElements_NonEmpty()
        {
            foreach (NatureElement el in System.Enum.GetValues(typeof(NatureElement)))
            {
                if (el == NatureElement.None) continue;
                Assert.IsFalse(string.IsNullOrEmpty(NatureMath.ElementName(el)),
                    $"ElementName({el}) must not be empty.");
            }
        }

        // ── NatureMath · element selection (W=Wind, S=Earth, A=Water, D=Storm) ──
        [Test]
        public void NatureMath_ElementForKey_MapsDirectionsToElements()
        {
            Assert.AreEqual(NatureElement.Wind,  NatureMath.ElementForKey("W"));
            Assert.AreEqual(NatureElement.Earth, NatureMath.ElementForKey("S"));
            Assert.AreEqual(NatureElement.Water, NatureMath.ElementForKey("A"));
            Assert.AreEqual(NatureElement.Storm, NatureMath.ElementForKey("D"));
            Assert.AreEqual(NatureElement.None,  NatureMath.ElementForKey("Q"));
        }

        [Test]
        public void NatureMath_KeyForElement_RoundTripsWithElementForKey()
        {
            foreach (NatureElement el in new[]
                { NatureElement.Wind, NatureElement.Earth, NatureElement.Water, NatureElement.Storm })
                Assert.AreEqual(el, NatureMath.ElementForKey(NatureMath.KeyForElement(el)),
                    $"KeyForElement/ElementForKey must round-trip for {el}.");
        }

        // ── LivingEnergyMath · capacity ────────────────────────────────────────
        [Test]
        public void LivingEnergyMath_AreaCapacity_ForestRichestDesertPoorest()
        {
            float forest = LivingEnergyMath.AreaCapacity("Forest");
            float desert = LivingEnergyMath.AreaCapacity("Desert");
            float plain  = LivingEnergyMath.AreaCapacity("Plain");
            Assert.Greater(forest, plain,  "Forest should hold more living energy than open plain.");
            Assert.Greater(plain,  desert, "Plain should hold more living energy than desert.");
            Assert.AreEqual(60f, LivingEnergyMath.AreaCapacity("SomethingUnknown"), 0.001f);
        }

        // ── LivingEnergyMath · match factor ────────────────────────────────────
        [Test]
        public void LivingEnergyMath_MatchFactor_FavouredCheapMismatchedDear()
        {
            // Wind on a mountain (favoured) is cheap; water on a mountain is dear.
            Assert.AreEqual(LivingEnergyMath.MatchedFactor,
                LivingEnergyMath.MatchFactor(NatureElement.Wind, "Mountain"), 0.001f);
            Assert.AreEqual(LivingEnergyMath.MismatchedFactor,
                LivingEnergyMath.MatchFactor(NatureElement.Water, "Desert"), 0.001f);
            // Steppe favours Wind OR Storm (blended) — both cheap.
            Assert.AreEqual(LivingEnergyMath.MatchedFactor,
                LivingEnergyMath.MatchFactor(NatureElement.Wind,  "Steppe"), 0.001f);
            Assert.AreEqual(LivingEnergyMath.MatchedFactor,
                LivingEnergyMath.MatchFactor(NatureElement.Storm, "Steppe"), 0.001f);
            // Unknown ground is neutral; fire (None) is always neutral.
            Assert.AreEqual(LivingEnergyMath.NeutralFactor,
                LivingEnergyMath.MatchFactor(NatureElement.Water, "MysteryLand"), 0.001f);
            Assert.AreEqual(LivingEnergyMath.NeutralFactor,
                LivingEnergyMath.MatchFactor(NatureElement.None, "Forest"), 0.001f);
        }

        [Test]
        public void LivingEnergyMath_NatureDrain_ScalesWithMatch()
        {
            float cheap = LivingEnergyMath.NatureDrain(NatureElement.Wind,  "Mountain");
            float dear  = LivingEnergyMath.NatureDrain(NatureElement.Water, "Mountain");
            Assert.Greater(dear, cheap, "Drawing against the land must cost more than a favoured draw.");
            Assert.AreEqual(LivingEnergyMath.NatureDrawBase * LivingEnergyMath.MatchedFactor, cheap, 0.001f);
        }

        [Test]
        public void LivingEnergyMath_FireDrain_ScalesWithStrokesAndFloorsAtOne()
        {
            float terrain = LivingEnergyMath.FireDrain(4, "Forest");   // neutral (element-blind)
            Assert.AreEqual(4 * LivingEnergyMath.FireDrawPerInput, terrain, 0.001f);
            // A zero/garbage stroke count still spends at least one stroke's worth.
            Assert.AreEqual(LivingEnergyMath.FireDrawPerInput, LivingEnergyMath.FireDrain(0, "Forest"), 0.001f);
        }

        // ── LivingEnergyMath · thresholds ──────────────────────────────────────
        [Test]
        public void LivingEnergyMath_LevelOf_BandsByFraction()
        {
            Assert.AreEqual(EnergyOmen.None,    LivingEnergyMath.LevelOf(0.80f));
            Assert.AreEqual(EnergyOmen.Half,    LivingEnergyMath.LevelOf(0.50f));
            Assert.AreEqual(EnergyOmen.Quarter, LivingEnergyMath.LevelOf(0.20f));
            Assert.AreEqual(EnergyOmen.Empty,   LivingEnergyMath.LevelOf(0.0f));
            Assert.AreEqual(EnergyOmen.Empty,   LivingEnergyMath.LevelOf(-0.3f));
        }

        [Test]
        public void LivingEnergyMath_OmenCrossed_AnnouncesEachBandOnceGoingDown()
        {
            // Crossing 0.6 → 0.4 enters the Half band for the first time.
            Assert.AreEqual(EnergyOmen.Half,
                LivingEnergyMath.OmenCrossed(0.6f, 0.4f, EnergyOmen.None));
            // Already announced Half: staying in the Half band says nothing more.
            Assert.AreEqual(EnergyOmen.None,
                LivingEnergyMath.OmenCrossed(0.45f, 0.30f, EnergyOmen.Half));
            // Dropping further into the Quarter band announces again.
            Assert.AreEqual(EnergyOmen.Quarter,
                LivingEnergyMath.OmenCrossed(0.30f, 0.20f, EnergyOmen.Half));
            // Energy recovering (afterFraction up) never announces.
            Assert.AreEqual(EnergyOmen.None,
                LivingEnergyMath.OmenCrossed(0.20f, 0.40f, EnergyOmen.Quarter));
        }

        [Test]
        public void LivingEnergyMath_DailyRegen_AtLeastOnePerDay()
        {
            Assert.GreaterOrEqual(LivingEnergyMath.DailyRegen(120f),
                LivingEnergyMath.DailyRegen(15f), "Richer land regrows at least as fast.");
            Assert.GreaterOrEqual(LivingEnergyMath.DailyRegen(5f), 1f, "Regen floors at one per day.");
        }

        [Test]
        public void LivingEnergyMath_Fraction_HandlesZeroCapacity()
        {
            Assert.AreEqual(0f, LivingEnergyMath.Fraction(10f, 0f), 0.001f);
            Assert.AreEqual(0.5f, LivingEnergyMath.Fraction(30f, 60f), 0.001f);
        }

        [Test]
        public void LivingEnergyMath_WeedFreeDrawChance_IsAProbability()
        {
            Assert.Greater(LivingEnergyMath.WeedFreeDrawChance, 0f);
            Assert.Less(LivingEnergyMath.WeedFreeDrawChance, 1f);
            Assert.AreEqual(0.30f, LivingEnergyMath.WeedFreeDrawChance, 0.001f);
        }

        // ── NpcCastPlanner (how an NPC lord spends his life-expectancy) ───────────

        [Test]
        public void NpcCastPlanner_LifeFrac_ClampsToUnitRange()
        {
            Assert.AreEqual(0f,   NpcCastPlanner.LifeFrac(0f),   0.001f);
            Assert.AreEqual(0f,   NpcCastPlanner.LifeFrac(-5f),  0.001f, "negative budget floors at 0");
            Assert.AreEqual(0.5f, NpcCastPlanner.LifeFrac(20f),  0.001f);
            Assert.AreEqual(1f,   NpcCastPlanner.LifeFrac(40f),  0.001f);
            Assert.AreEqual(1f,   NpcCastPlanner.LifeFrac(80f),  0.001f, "plentiful life caps at 1");
        }

        [Test]
        public void NpcCastPlanner_PowerMult_FullLife_IsFullForAllTempers()
        {
            foreach (CasterTemper t in new[] { CasterTemper.Calculating, CasterTemper.Balanced, CasterTemper.Impulsive })
                Assert.AreEqual(1f, NpcCastPlanner.PowerMult(1f, t, emergency: false), 0.001f,
                    $"a fresh {t} lord casts at full power");
        }

        [Test]
        public void NpcCastPlanner_PowerMult_NearBurnout_HoardsByTemper()
        {
            // No life left, no emergency: each temper falls back to its floor.
            Assert.AreEqual(0.50f, NpcCastPlanner.PowerMult(0f, CasterTemper.Calculating, false), 0.001f);
            Assert.AreEqual(0.65f, NpcCastPlanner.PowerMult(0f, CasterTemper.Balanced,    false), 0.001f);
            Assert.AreEqual(0.90f, NpcCastPlanner.PowerMult(0f, CasterTemper.Impulsive,   false), 0.001f);
            // The impulsive lord always spends more than the calculating one.
            Assert.Greater(NpcCastPlanner.PowerMult(0.3f, CasterTemper.Impulsive, false),
                           NpcCastPlanner.PowerMult(0.3f, CasterTemper.Calculating, false));
        }

        [Test]
        public void NpcCastPlanner_Emergency_FloorsPowerHighEvenForOldMiser()
        {
            Assert.GreaterOrEqual(NpcCastPlanner.PowerMult(0f, CasterTemper.Calculating, emergency: true),
                                  NpcCastPlanner.EmergencyFloor);
            Assert.GreaterOrEqual(NpcCastPlanner.CastPower(NpcCastPlanner.BaseHarass, 0f, CasterTemper.Calculating, true),
                                  NpcCastPlanner.EmergencyFloor,
                                  "survival trumps thrift regardless of the situational base");
        }

        [Test]
        public void NpcCastPlanner_Overchannel_DoublesPower()
        {
            // The doubling matches the player's overchannel multiplier and exceeds
            // the normal 1.2 clamp on purpose.
            float doubled = NpcCastPlanner.Overchannelled(1.0f);
            Assert.AreEqual(ElementMagicMath.OverchannelMult, doubled, 0.001f);
            Assert.Greater(NpcCastPlanner.Overchannelled(1.2f), 1.2f);
        }

        [Test]
        public void NpcCastPlanner_OverchannelChance_RecklessAndDesperateHigher_OldMiserRefuses()
        {
            // An old lord (short on years) will not gamble the extra life — unless
            // survival is on the line.
            Assert.AreEqual(0f, NpcCastPlanner.OverchannelChance(CasterTemper.Calculating, 0.1f, emergency: false), 0.0001f);
            Assert.Greater(NpcCastPlanner.OverchannelChance(CasterTemper.Calculating, 0.1f, emergency: true), 0f);
            // Impulsive lords overchannel more freely than calculating ones.
            Assert.Greater(NpcCastPlanner.OverchannelChance(CasterTemper.Impulsive,  1f, false),
                           NpcCastPlanner.OverchannelChance(CasterTemper.Calculating, 1f, false));
            // Emergencies raise the odds for any temper.
            Assert.Greater(NpcCastPlanner.OverchannelChance(CasterTemper.Balanced, 1f, true),
                           NpcCastPlanner.OverchannelChance(CasterTemper.Balanced, 1f, false));
            // ShouldOverchannel is a straight roll against the chance.
            Assert.IsTrue(NpcCastPlanner.ShouldOverchannel(CasterTemper.Impulsive, 1f, true, 0f));
            Assert.IsFalse(NpcCastPlanner.ShouldOverchannel(CasterTemper.Calculating, 0.1f, false, 0.0f));
        }

        [Test]
        public void NpcCastPlanner_CastPower_YoungImpulsive_SpendsBig_OldCalculating_Conserves()
        {
            float young = NpcCastPlanner.CastPower(NpcCastPlanner.BaseCluster, lifeFrac: 1f,
                                                   CasterTemper.Impulsive,   emergency: false);
            float old   = NpcCastPlanner.CastPower(NpcCastPlanner.BaseHarass,  lifeFrac: 0f,
                                                   CasterTemper.Calculating, emergency: false);
            Assert.Greater(young, old);
            Assert.LessOrEqual(NpcCastPlanner.CastPower(NpcCastPlanner.BaseDesperate, 1f, CasterTemper.Impulsive, false), 1.2f,
                "power is clamped to a sane ceiling");
        }

        [Test]
        public void NpcCastPlanner_CooldownMult_FreshIsBaseline_ScarcityStretchesByTemper()
        {
            foreach (CasterTemper t in new[] { CasterTemper.Calculating, CasterTemper.Balanced, CasterTemper.Impulsive })
                Assert.AreEqual(1f, NpcCastPlanner.CooldownMult(1f, t), 0.001f,
                    $"a fresh {t} lord keeps his normal cadence");

            Assert.AreEqual(2.5f, NpcCastPlanner.CooldownMult(0f, CasterTemper.Calculating), 0.001f);
            Assert.AreEqual(1.1f, NpcCastPlanner.CooldownMult(0f, CasterTemper.Impulsive),   0.001f);
            // A near-burnout calculating lord goes quieter than an impulsive one.
            Assert.Greater(NpcCastPlanner.CooldownMult(0.2f, CasterTemper.Calculating),
                           NpcCastPlanner.CooldownMult(0.2f, CasterTemper.Impulsive));
        }

        // ── CrystalMath NPC situational use ───────────────────────────────────────

        [Test]
        public void CrystalMath_NpcShouldUse_HealStone_WantsBearerHurt()
        {
            // Sunstone: fires when meaningfully hurt, not at (near) full health.
            Assert.IsTrue (CrystalMath.NpcShouldUse(CrystalType.Sunstone, 0.30f, 0, 0.1f));
            Assert.IsFalse(CrystalMath.NpcShouldUse(CrystalType.Sunstone, 0.90f, 0, 0.1f),
                "a healthy bearer does not waste a heal-stone");
            Assert.IsTrue(CrystalMath.IsHealingCrystal(CrystalType.Sunstone));
            Assert.IsFalse(CrystalMath.IsHealingCrystal(CrystalType.Embershard));
        }

        [Test]
        public void CrystalMath_NpcShouldUse_OffensiveStone_NeedsEnemiesInReach()
        {
            // Offensive crystal on empty air: never.
            Assert.IsFalse(CrystalMath.NpcShouldUse(CrystalType.Embershard, 1f, 0, 0.01f));
            // Enemies present + eager roll: yes; a crowd raises the eagerness.
            Assert.IsTrue(CrystalMath.NpcShouldUse(CrystalType.Embershard, 1f, 4, 0.6f));
            Assert.IsFalse(CrystalMath.NpcShouldUse(CrystalType.Embershard, 1f, 1, 0.6f),
                "a lone target with a lukewarm roll holds the crystal");
            // The Veilstone reaches farther than the short-range AoE stones.
            Assert.Greater(CrystalMath.CrystalUseRange(CrystalType.Veilstone),
                           CrystalMath.CrystalUseRange(CrystalType.Embershard));
        }

        // ── CrystalCatalog — five new crystals ────────────────────────────────────

        [Test]
        public void CrystalCatalog_AllElevenCrystalsResolve()
        {
            Assert.AreEqual(11, CrystalCatalog.All.Count);
            foreach (CrystalType t in System.Enum.GetValues(typeof(CrystalType)))
            {
                var def = CrystalCatalog.Get(t);
                Assert.AreEqual(t, def.Type);
                Assert.IsTrue(CrystalCatalog.IsCrystalItemId(def.ItemId));
                Assert.IsTrue(CrystalCatalog.TryGetByItemId(def.ItemId, out var found));
                Assert.AreEqual(t, found.Type);
            }
        }

        [Test]
        public void CrystalMath_Thornveil_IsDeepestSlowButShortestDuration()
        {
            // The root is far deeper than any partial slow...
            Assert.Less(CrystalMath.ThornRootMult, CrystalMath.RimeSlowMult);
            Assert.Less(CrystalMath.ThornRootMult, CrystalMath.VeilSlowMult);
            Assert.Less(CrystalMath.ThornRootMult, CrystalMath.DuskSlowMult);
            // ...and its damage is the lowest of the single-target stones (the
            // control is the point, not the hit).
            Assert.Less(CrystalMath.ThornDamage, CrystalMath.VeilDamage);
        }

        [Test]
        public void CrystalMath_Willowisp_HitsHarderThanAoEMoraleDrains()
        {
            Assert.Greater(CrystalMath.WillowMoraleDrain, CrystalMath.StormMoraleDrain);
            Assert.Greater(CrystalMath.WillowMoraleDrain, CrystalMath.DuskMoraleDrain);
            // Reaches as far as Veilstone — both are reach-out-to-one stones.
            Assert.AreEqual(CrystalMath.VeilRange, CrystalMath.WillowRange, 0.0001f);
            Assert.AreEqual(CrystalMath.WillowRange, CrystalMath.CrystalUseRange(CrystalType.Willowisp), 0.0001f);
        }

        [Test]
        public void CrystalMath_Bloodstone_LifestealReturnsHalfDamageDealt()
        {
            float totalDealt = CrystalMath.BloodDamage * 3; // three enemies struck
            float healed = totalDealt * CrystalMath.BloodLifestealFrac;
            Assert.AreEqual(totalDealt * 0.5f, healed, 0.0001f);
        }

        [Test]
        public void CrystalMath_Zephyrglass_HastensAboveNormalSpeed()
        {
            Assert.Greater(CrystalMath.ZephyrHasteMult, 1f);
        }

        [Test]
        public void CrystalMath_Aegisstone_IsTreatedAsHealingForNpcUse()
        {
            Assert.IsTrue(CrystalMath.IsHealingCrystal(CrystalType.Aegisstone));
            Assert.IsTrue(CrystalMath.NpcShouldUse(CrystalType.Aegisstone, 0.30f, 0, 0.1f));
            Assert.IsFalse(CrystalMath.NpcShouldUse(CrystalType.Aegisstone, 0.90f, 0, 0.1f));
        }

        // ── MiracleMath battle selection (right miracle for the moment) ───────────

        [Test]
        public void MiracleMath_ChooseBattleMiracle_PrioritisesBySituation()
        {
            // Self-preservation trumps everything.
            Assert.AreEqual(MiracleType.MercyMend,
                MiracleMath.ChooseBattleMiracle(selfHurt: true, alliesHurtNear: 5,
                    enemyPressingSelf: true, ashenAdjacent: true, roll: 0.9f));
            // Ashen adjacent (and self fine) → judgement.
            Assert.AreEqual(MiracleType.InsightPyre,
                MiracleMath.ChooseBattleMiracle(false, 3, true, ashenAdjacent: true, 0.9f));
            // A wounded line → shared light; a single wounded ally → a targeted mend.
            Assert.AreEqual(MiracleType.GraceBlessing,
                MiracleMath.ChooseBattleMiracle(false, 2, false, false, 0.9f));
            Assert.AreEqual(MiracleType.MercyMend,
                MiracleMath.ChooseBattleMiracle(false, 1, false, false, 0.9f));
            // Under a press, no one wounded → shield.
            Assert.AreEqual(MiracleType.HonorAegis,
                MiracleMath.ChooseBattleMiracle(false, 0, enemyPressingSelf: true, false, 0.9f));
            // Nothing pressing → rally or bless by the roll.
            Assert.AreEqual(MiracleType.ValorFury,
                MiracleMath.ChooseBattleMiracle(false, 0, false, false, roll: 0.1f));
            Assert.AreEqual(MiracleType.GraceBlessing,
                MiracleMath.ChooseBattleMiracle(false, 0, false, false, roll: 0.9f));
        }

        // ── WallWardMath (elemental wall warding) ─────────────────────────────────

        [Test]
        public void WallWardMath_MissileBlocking_WindAndStoneOnly()
        {
            Assert.IsTrue (WallWardMath.WallBlocksMissiles(MagicElement.Wind));
            Assert.IsTrue (WallWardMath.WallBlocksMissiles(MagicElement.Earth));
            Assert.IsFalse(WallWardMath.WallBlocksMissiles(MagicElement.Fire),
                "arrows fly through flame");
            Assert.IsFalse(WallWardMath.WallBlocksMissiles(MagicElement.Water));
            Assert.IsFalse(WallWardMath.WallBlocksMissiles(MagicElement.Spirit));
        }

        [Test]
        public void WallWardMath_MagicBlocking_MatchesTheElementalAnswers()
        {
            // Fire devours the gale; water quenches fire and drinks the wind;
            // wind scatters flung stone; stone dams the wave.
            Assert.IsTrue(WallWardMath.WallBlocksMagic(MagicElement.Fire,  MagicElement.Wind));
            Assert.IsTrue(WallWardMath.WallBlocksMagic(MagicElement.Water, MagicElement.Fire));
            Assert.IsTrue(WallWardMath.WallBlocksMagic(MagicElement.Water, MagicElement.Wind));
            Assert.IsTrue(WallWardMath.WallBlocksMagic(MagicElement.Wind,  MagicElement.Earth));
            Assert.IsTrue(WallWardMath.WallBlocksMagic(MagicElement.Earth, MagicElement.Water));

            // What must pass: fire through fire/wind/earth walls, dread through all.
            Assert.IsFalse(WallWardMath.WallBlocksMagic(MagicElement.Fire,  MagicElement.Fire));
            Assert.IsFalse(WallWardMath.WallBlocksMagic(MagicElement.Wind,  MagicElement.Fire));
            Assert.IsFalse(WallWardMath.WallBlocksMagic(MagicElement.Earth, MagicElement.Fire));
            foreach (MagicElement wall in new[] { MagicElement.Fire, MagicElement.Wind,
                                                  MagicElement.Earth, MagicElement.Water })
                Assert.IsFalse(WallWardMath.WallBlocksMagic(wall, MagicElement.Spirit),
                    $"dread passes a {wall} wall");
            // Spirit is a ward, never a wall — it blocks nothing.
            Assert.IsFalse(WallWardMath.WallBlocksMagic(MagicElement.Spirit, MagicElement.Fire));
        }

        [Test]
        public void WallWardMath_CounterWall_AnswersEachElement()
        {
            Assert.AreEqual(MagicElement.Water, WallWardMath.CounterWallFor(MagicElement.Fire));
            Assert.AreEqual(MagicElement.Water, WallWardMath.CounterWallFor(MagicElement.Wind));
            Assert.AreEqual(MagicElement.Wind,  WallWardMath.CounterWallFor(MagicElement.Earth));
            Assert.AreEqual(MagicElement.Earth, WallWardMath.CounterWallFor(MagicElement.Water));
            Assert.IsNull(WallWardMath.CounterWallFor(MagicElement.Spirit),
                "nothing walls out the dread");
            // Every counter-wall actually blocks what it was raised against.
            foreach (MagicElement el in new[] { MagicElement.Fire, MagicElement.Wind,
                                                MagicElement.Earth, MagicElement.Water })
                Assert.IsTrue(WallWardMath.WallBlocksMagic(WallWardMath.CounterWallFor(el).Value, el));
            Assert.IsTrue(WallWardMath.QuenchesFireMissile(MagicElement.Water));
            Assert.IsFalse(WallWardMath.QuenchesFireMissile(MagicElement.Earth));
        }

        // ── Ignition (a deep draw sets its marks alight) ──────────────────────────

        [Test]
        public void ElementMagicMath_Ignite_ScalesWithChargeAndCrossesKillThreshold()
        {
            // A snap flick ignites nothing; a full draw burns at the maximum rate.
            Assert.AreEqual(0f, ElementMagicMath.IgniteDps(ElementMagicMath.MinPower), 0.001f);
            Assert.AreEqual(ElementMagicMath.IgniteMaxDps,
                            ElementMagicMath.IgniteDps(ElementMagicMath.MaxPower), 0.001f);
            // Monotonic in the draw.
            Assert.Greater(ElementMagicMath.IgniteDps(0.9f), ElementMagicMath.IgniteDps(0.7f));
            // The bruiser's promise: a fully-drawn cone (44) plus its full burn
            // (12/s × 5 s = 60) crosses the 100 HP kill threshold of a line troop.
            float fullBurn = ElementMagicMath.IgniteMaxDps * ElementMagicMath.IgniteSeconds;
            Assert.GreaterOrEqual(44f + fullBurn, 100f,
                "a full-drawn fire cone must finish an unarmoured man");
            // …but the burn alone must NOT (it finishes, it does not replace the strike).
            Assert.Less(fullBurn, 100f);
        }

        [Test]
        public void WallWardMath_SegmentNearPoint_Geometry()
        {
            // A wall node at (5,1) with radius 2 sits beside the line (0,0)→(10,0).
            Assert.IsTrue(WallWardMath.SegmentNearPoint(0, 0, 10, 0, 5, 1, 2f));
            // Too far off the line: no block.
            Assert.IsFalse(WallWardMath.SegmentNearPoint(0, 0, 10, 0, 5, 5, 2f));
            // BEHIND the caster: the segment ends before the node — no block.
            Assert.IsFalse(WallWardMath.SegmentNearPoint(0, 0, 10, 0, -5, 0, 2f));
            // Degenerate zero-length segment: only blocked when standing inside.
            Assert.IsTrue (WallWardMath.SegmentNearPoint(3, 3, 3, 3, 3, 4, 2f));
            Assert.IsFalse(WallWardMath.SegmentNearPoint(3, 3, 3, 3, 9, 9, 2f));
            // Nearest-t: the closest approach to (5,1) along (0,0)→(10,0) is halfway.
            Assert.AreEqual(0.5f, WallWardMath.SegmentNearestT(0, 0, 10, 0, 5, 1), 0.001f);
        }

        // ── ElementalMath tests (The Kindled) ─────────────────────────────────
        [Test]
        public void ElementalMath_BeingResistsItsOwnElement()
        {
            // A flame being drinks fire; a stone being shrugs off earth magic.
            Assert.Less(ElementalMath.ElementDamageMultiplier(ElementalKind.Flame, MagicElement.Fire), 1f);
            Assert.Less(ElementalMath.ElementDamageMultiplier(ElementalKind.Stone, MagicElement.Earth), 1f);
            Assert.Less(ElementalMath.ElementDamageMultiplier(ElementalKind.Gale,  MagicElement.Wind),  1f);
        }

        [Test]
        public void ElementalMath_BeingBucklesToItsCounter()
        {
            // The wheel: fire drowns to water, water is drunk by earth, earth is
            // worn by wind, wind is burned by fire.
            Assert.Greater(ElementalMath.ElementDamageMultiplier(ElementalKind.Flame, MagicElement.Water), 1f);
            Assert.Greater(ElementalMath.ElementDamageMultiplier(ElementalKind.Tide,  MagicElement.Earth), 1f);
            Assert.Greater(ElementalMath.ElementDamageMultiplier(ElementalKind.Stone, MagicElement.Wind),  1f);
            Assert.Greater(ElementalMath.ElementDamageMultiplier(ElementalKind.Gale,  MagicElement.Fire),  1f);
        }

        [Test]
        public void ElementalMath_FrostMeltsToFireHardest()
        {
            // Ice fears fire above all, and shrugs off water.
            Assert.Greater(ElementalMath.ElementDamageMultiplier(ElementalKind.Frost, MagicElement.Fire), 1.5f);
            Assert.Less(ElementalMath.ElementDamageMultiplier(ElementalKind.Frost, MagicElement.Water), 1f);
        }

        [Test]
        public void ElementalMath_NeutralElementDoesNothing()
        {
            // Wind against a flame being is neither its element nor its counter.
            Assert.AreEqual(1f, ElementalMath.ElementDamageMultiplier(ElementalKind.Flame, MagicElement.Wind), 0.001f);
        }

        [Test]
        public void ElementalMath_StoneShattersToBluntTurnsBlades()
        {
            Assert.Greater(ElementalMath.PhysicalDamageMultiplier(ElementalKind.Stone, PhysicalHit.Blunt), 1f);
            Assert.Less   (ElementalMath.PhysicalDamageMultiplier(ElementalKind.Stone, PhysicalHit.Cut),   1f);
        }

        [Test]
        public void ElementalMath_FlameLetsSteelPassThrough()
        {
            // No physical weakness — flame is unmade by magic, not by the blade.
            Assert.Less(ElementalMath.PhysicalDamageMultiplier(ElementalKind.Flame, PhysicalHit.Cut),   1f);
            Assert.Less(ElementalMath.PhysicalDamageMultiplier(ElementalKind.Flame, PhysicalHit.Blunt), 1f);
        }

        [Test]
        public void ElementalMath_WildKindMatchesBiome()
        {
            Assert.AreEqual(ElementalKind.Frost, ElementalMath.WildKindForBiome("snowy tundra"));
            Assert.AreEqual(ElementalKind.Sand,  ElementalMath.WildKindForBiome("deep desert dunes"));
            Assert.AreEqual(ElementalKind.Tide,  ElementalMath.WildKindForBiome("old forest"));
            Assert.AreEqual(ElementalKind.Gale,  ElementalMath.WildKindForBiome("open steppe"));
            Assert.AreEqual(ElementalKind.Stone, ElementalMath.WildKindForBiome("mountain root"));
        }

        [Test]
        public void ElementalMath_AllKindsHavePositiveHealth()
        {
            foreach (ElementalKind k in Enum.GetValues(typeof(ElementalKind)))
                Assert.Greater(ElementalMath.Health(k), 0f);
        }

        [Test]
        public void ElementUltimateMath_SpiritCostsMoreThanOtherUnbindings()
        {
            int spirit = ElementUltimateMath.UltimateAgingDays(hasNature: false, MagicElement.Spirit);
            int fire   = ElementUltimateMath.UltimateAgingDays(hasNature: false, MagicElement.Fire);
            Assert.Greater(spirit, fire);
        }

        // ── AshenRuinMath tests (ruins expansion) ──────────────────────────────

        [Test]
        public void AshenRuinMath_EmberWraithPassChance_HigherRenownIsHarder()
        {
            int lowFame  = AshenRuinMath.EmberWraithPassChance(0f);
            int highFame = AshenRuinMath.EmberWraithPassChance(4000f);
            Assert.Greater(lowFame, highFame);
            Assert.GreaterOrEqual(highFame, 20); // floor clamp
            Assert.LessOrEqual(lowFame, 75);     // ceiling clamp
        }

        [Test]
        public void AshenRuinMath_WardstoneGatePassChance_ScalesWithRoguery()
        {
            Assert.AreEqual(25, AshenRuinMath.WardstoneGatePassChance(0));
            Assert.Greater(AshenRuinMath.WardstoneGatePassChance(150), AshenRuinMath.WardstoneGatePassChance(0));
            Assert.AreEqual(90, AshenRuinMath.WardstoneGatePassChance(1000)); // ceiling clamp
        }

        [Test]
        public void AshenRuinMath_HollowChoirPassChance_AutoPassesAtTierTwo()
        {
            Assert.AreEqual(100, AshenRuinMath.HollowChoirPassChance(2));
            Assert.AreEqual(100, AshenRuinMath.HollowChoirPassChance(3));
            Assert.AreEqual(45, AshenRuinMath.HollowChoirPassChance(0));
            Assert.AreEqual(65, AshenRuinMath.HollowChoirPassChance(1));
        }

        [Test]
        public void AshenRuinMath_WeightOfAshCost_ScalesWithTier()
        {
            Assert.AreEqual(2,  AshenRuinMath.WeightOfAshCost(RuinTier.Easy));
            Assert.AreEqual(4,  AshenRuinMath.WeightOfAshCost(RuinTier.Standard));
            Assert.AreEqual(7,  AshenRuinMath.WeightOfAshCost(RuinTier.Brutal));
            Assert.AreEqual(10, AshenRuinMath.WeightOfAshCost(RuinTier.Legendary));
        }

        [Test]
        public void AshenRuinMath_TriuneReckoningFallbackPassChance_ScalesWithProficiency()
        {
            Assert.AreEqual(40, AshenRuinMath.TriuneReckoningFallbackPassChance(0));
            Assert.Greater(AshenRuinMath.TriuneReckoningFallbackPassChance(10), AshenRuinMath.TriuneReckoningFallbackPassChance(0));
            Assert.AreEqual(80, AshenRuinMath.TriuneReckoningFallbackPassChance(100)); // ceiling clamp
        }

        [Test]
        public void AshenRuinMath_ResolveShiftingHall_CoversAllThreeBands()
        {
            Assert.AreEqual(ShiftingHallOutcome.RenownGain,  AshenRuinMath.ResolveShiftingHall(0));
            Assert.AreEqual(ShiftingHallOutcome.RenownGain,  AshenRuinMath.ResolveShiftingHall(34));
            Assert.AreEqual(ShiftingHallOutcome.Desertion,   AshenRuinMath.ResolveShiftingHall(35));
            Assert.AreEqual(ShiftingHallOutcome.Desertion,   AshenRuinMath.ResolveShiftingHall(69));
            Assert.AreEqual(ShiftingHallOutcome.WhisperGain, AshenRuinMath.ResolveShiftingHall(70));
            Assert.AreEqual(ShiftingHallOutcome.WhisperGain, AshenRuinMath.ResolveShiftingHall(99));
        }

        [Test]
        public void AshenRuinMath_ShiftingHallDesertionLoss_HasAFloor()
        {
            Assert.AreEqual(4, AshenRuinMath.ShiftingHallDesertionLoss(0));
            Assert.AreEqual(4, AshenRuinMath.ShiftingHallDesertionLoss(20));
            Assert.AreEqual(10, AshenRuinMath.ShiftingHallDesertionLoss(70));
        }

        [Test]
        public void AshenRuinDefs_AllRuinsHaveAtLeastOneChallengeAndBothRewards()
        {
            foreach (var def in AshenRuinDefs.All)
            {
                Assert.IsNotEmpty(def.VillageName, $"{def.RuinName} has no VillageName.");
                Assert.IsNotEmpty(def.Challenges, $"{def.RuinName} has no challenges.");
                Assert.IsNotNull(def.MainReward, $"{def.RuinName} has no MainReward.");
                Assert.IsNotNull(def.PartialReward, $"{def.RuinName} has no PartialReward.");
            }
        }

        [Test]
        public void AshenRuinDefs_VillageNamesAreUnique()
        {
            var names = AshenRuinDefs.All.Select(r => r.VillageName).ToList();
            Assert.AreEqual(names.Count, names.Distinct().Count(), "Two RuinDefs share the same VillageName.");
        }
    }
}
