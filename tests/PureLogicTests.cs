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
    }
}
