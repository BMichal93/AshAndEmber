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
    }
}
