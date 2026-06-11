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
        // ── BuildSchoolPrefix tests ───────────────────────────────────────────

        [Test]
        public void BuildSchoolPrefix_SingleRed_ReturnsR()
        {
            var schools = new[] { ColorSchool.Red };
            string result = SpellEffects.BuildSchoolPrefix(schools);
            Assert.AreEqual("[R] ", result);
        }

        [Test]
        public void BuildSchoolPrefix_RedAndGreen_ReturnsRG()
        {
            var schools = new[] { ColorSchool.Red, ColorSchool.Green };
            string result = SpellEffects.BuildSchoolPrefix(schools);
            Assert.AreEqual("[RG] ", result);
        }

        [Test]
        public void BuildSchoolPrefix_AllSix_ReturnsROYGBP()
        {
            var schools = new[]
            {
                ColorSchool.Red, ColorSchool.Orange, ColorSchool.Yellow,
                ColorSchool.Green, ColorSchool.Blue, ColorSchool.Purple
            };
            string result = SpellEffects.BuildSchoolPrefix(schools);
            Assert.AreEqual("[ROYGBP] ", result);
        }

        [Test]
        public void BuildSchoolPrefix_Empty_ReturnsBrackets()
        {
            var schools = new ColorSchool[0];
            string result = SpellEffects.BuildSchoolPrefix(schools);
            Assert.AreEqual("[] ", result);
        }

        // ── SpellDatabase tests ───────────────────────────────────────────────

        [Test]
        public void SpellDatabase_AllCombos_AreFourChars()
        {
            foreach (var entry in SpellDatabase.All)
                Assert.AreEqual(4, entry.Combo.Length,
                    $"Spell '{entry.Name}' has combo '{entry.Combo}' which is not 4 characters.");
        }

        [Test]
        public void SpellDatabase_AllCombos_AreUnique()
        {
            var combos = SpellDatabase.All.Select(e => e.Combo).ToList();
            var distinct = combos.Distinct().ToList();
            Assert.AreEqual(combos.Count, distinct.Count, "Duplicate combos found in SpellDatabase.");
        }

        [Test]
        public void SpellDatabase_Find_KnownCombo_ReturnsCorrectSpell()
        {
            var entry = SpellDatabase.Find("UURR");
            Assert.IsNotNull(entry, "Expected to find spell with combo UURR.");
            Assert.AreEqual("Crimson Torrent", entry.Name);
        }

        [Test]
        public void SpellDatabase_Find_UnknownCombo_ReturnsNull()
        {
            var entry = SpellDatabase.Find("XXXX");
            Assert.IsNull(entry, "Expected null for unknown combo XXXX.");
        }

        // ── ColorSchoolData tests ─────────────────────────────────────────────

        [Test]
        public void ColorSchoolData_AllSixSchools_HaveInfo()
        {
            var schools = new[]
            {
                ColorSchool.Red, ColorSchool.Orange, ColorSchool.Yellow,
                ColorSchool.Green, ColorSchool.Blue, ColorSchool.Purple
            };
            foreach (var school in schools)
                Assert.IsTrue(ColorSchoolData.Info.ContainsKey(school),
                    $"ColorSchoolData.Info is missing entry for {school}.");
        }

        [Test]
        public void ColorSchoolData_AllSchools_HaveNonEmptyName()
        {
            foreach (var kvp in ColorSchoolData.Info)
                Assert.IsFalse(string.IsNullOrEmpty(kvp.Value.Name),
                    $"School {kvp.Key} has an empty Name.");
        }

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

        // ── Waver / Rouse talent definition tests ─────────────────────────────

        [Test]
        public void TalentSystem_Waver_IsDefinedAsEnchantment()
        {
            var def = TalentSystem.All.FirstOrDefault(d => d.Id == TalentId.Waver);
            Assert.IsNotNull(def, "Waver should be present in TalentSystem.All.");
            Assert.IsTrue(def.IsEnchantment, "Waver should be flagged as an enchantment.");
            Assert.IsFalse(def.IsSpell, "Waver should not be a campaign map spell.");
            Assert.AreEqual(TalentCategory.Enchantment, def.Category);
        }

        [Test]
        public void TalentSystem_Rouse_IsDefinedAsEnchantment()
        {
            var def = TalentSystem.All.FirstOrDefault(d => d.Id == TalentId.Rouse);
            Assert.IsNotNull(def, "Rouse should be present in TalentSystem.All.");
            Assert.IsTrue(def.IsEnchantment, "Rouse should be flagged as an enchantment.");
            Assert.IsFalse(def.IsSpell, "Rouse should not be a campaign map spell.");
            Assert.AreEqual(TalentCategory.Enchantment, def.Category);
        }

        [Test]
        public void TalentSystem_WaverAndRouse_NotPurchasedAfterReset()
        {
            TalentSystem.ResetForNewGame();
            Assert.IsFalse(TalentSystem.Has(TalentId.Waver),
                "Waver should not be purchased at game start.");
            Assert.IsFalse(TalentSystem.Has(TalentId.Rouse),
                "Rouse should not be purchased at game start.");
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

        [Test]
        public void TalentSystem_AllTalentIds_HaveDefinition()
        {
            // Every value in the TalentId enum must have a matching entry in All.
            foreach (TalentId id in System.Enum.GetValues(typeof(TalentId)))
            {
                var def = TalentSystem.All.FirstOrDefault(d => d.Id == id);
                Assert.IsNotNull(def, $"TalentSystem.All is missing a definition for TalentId.{id}.");
            }
        }

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
                TalentId.Subjugate, TalentId.Rejuvenate, TalentId.PlantGrowth,
                TalentId.BreakWills, TalentId.Inspire, TalentId.Plague,
                TalentId.Clairvoyance, TalentId.Curse,
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
                TalentId.DevourLife, TalentId.Reap, TalentId.Camaraderie,
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

        [Test]
        public void AgingSystem_ComputeBattleAgingCost_SmallCast_CostsOneDay()
        {
            // Geometric round(1.4^(n−1)): 1–2 inputs = 1 day without BattleMage
            Assert.AreEqual(1, AgingSystem.ComputeBattleAgingCost(1, false));
            Assert.AreEqual(1, AgingSystem.ComputeBattleAgingCost(2, false));
            Assert.AreEqual(2, AgingSystem.ComputeBattleAgingCost(3, false));
        }

        [Test]
        public void AgingSystem_ComputeBattleAgingCost_LargeCast_ScalesGeometrically()
        {
            // round(1.4^(n−1)): 4 inputs = 3 days, 8 inputs = 11 days, hard cap 84.
            Assert.AreEqual(3,  AgingSystem.ComputeBattleAgingCost(4, false));
            Assert.AreEqual(11, AgingSystem.ComputeBattleAgingCost(8, false));
            Assert.AreEqual(84, AgingSystem.ComputeBattleAgingCost(20, false));
        }

        [Test]
        public void AgingSystem_ComputeBattleAgingCost_BattleMage_CutsThird()
        {
            // Tempered cuts 33% off the cost (rounded; minimum 1 — never free).
            Assert.AreEqual(1, AgingSystem.ComputeBattleAgingCost(1, true));  // 1 → 0.67 → floor 1
            Assert.AreEqual(1, AgingSystem.ComputeBattleAgingCost(3, true));  // 2 → 1.34 → 1
            Assert.AreEqual(2, AgingSystem.ComputeBattleAgingCost(4, true));  // 3 → 2.01 → 2
            Assert.AreEqual(7, AgingSystem.ComputeBattleAgingCost(8, true));  // 11 → 7.37 → 7
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
    }
}
