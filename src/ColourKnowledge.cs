// =============================================================================
// COLOURS OF CALRADIA — ColourKnowledge.cs
// Mount & Blade II: Bannerlord Mod  v2.0
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Engine;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.CampaignSystem.MapEvents;

namespace ColoursOfCalradia
{
    // =========================================================================
    // 4. COLOUR KNOWLEDGE  — tracks chosen schools, cast counts, grimoire
    // =========================================================================
    public static class ColourKnowledge
    {
        private static readonly HashSet<ColorSchool> _chosenSchools = new HashSet<ColorSchool>();
        // Accumulated casts per school — every 25 casts shifts the associated trait
        private static readonly Dictionary<ColorSchool, int> _castCounters =
            new Dictionary<ColorSchool, int>
            {
                [ColorSchool.Red]    = 0,
                [ColorSchool.Orange] = 0,
                [ColorSchool.Yellow] = 0,
                [ColorSchool.Green]  = 0,
                [ColorSchool.Blue]   = 0,
                [ColorSchool.Purple] = 0,
            };
        private static readonly HashSet<string> _giftedChildIds = new HashSet<string>();

        private static int   _grimoirePage         = 0;
        private const  int   GrimoirePageSize       = 4;
        private static float _purpleFertilityLevel  = 1.0f;

        public static bool  HasAnySchool            => _chosenSchools.Count > 0;
        public static bool  HasSchool(ColorSchool school) => _chosenSchools.Contains(school);
        public static IEnumerable<ColorSchool> AllSchools => _chosenSchools;
        public static float PurpleFertilityLevel    => _purpleFertilityLevel;

        // Returns the % chance that a player formation order is scrambled by madness.
        // 5% for non-contiguous colours, 10% for 5 colours, 20% for 6 colours.
        public static int GetMadnessOrderChance()
        {
            int count = _chosenSchools.Count;
            if (count == 6) return 20;
            if (count == 5) return 10;
            if (count >= 2)
            {
                var positions = _chosenSchools.Select(s => (int)s).ToHashSet();
                const int n = 6;
                foreach (int start in positions)
                {
                    bool ok = true;
                    for (int step = 0; step < positions.Count; step++)
                        if (!positions.Contains((start + step) % n)) { ok = false; break; }
                    if (ok) return 0; // contiguous — no order madness
                }
                return 5; // non-contiguous
            }
            return 0;
        }

        public static bool ReducePurpleFertility()
        {
            if (_purpleFertilityLevel <= 0.01f) return false;
            _purpleFertilityLevel = Math.Max(0.01f, _purpleFertilityLevel - 0.01f);
            return true;
        }

        public static void AddSchool(ColorSchool school)
        {
            _chosenSchools.Add(school);
        }

        public static bool IsChildGifted(string heroId) => _giftedChildIds.Contains(heroId);
        public static void AddGiftedChild(string heroId) => _giftedChildIds.Add(heroId);

        // Called after each successful cast to drift personality and track stats
        public static void RecordCast(ColorSchool school)
        {
            if (!_castCounters.ContainsKey(school)) return;
            _castCounters[school]++;

            if (_castCounters[school] % 25 != 0) return;

            // Shift personality trait
            try
            {
                var (trait, direction) = ColorSchoolData.GetTraitEffect(school);
                if (trait == null || direction == 0 || Hero.MainHero == null) return;
                int current = Hero.MainHero.GetTraitLevel(trait);
                int next = Math.Max(-2, Math.Min(2, current + direction));
                if (next != current)
                {
                    Hero.MainHero.SetTraitLevel(trait, next);
                    string dir = direction > 0 ? "increased" : "decreased";
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Your {trait.StringId} trait has {dir} from repeated {ColorSchoolData.Info[school].Name} casting.",
                        ColorSchoolData.GetMessageColor(school)));
                }
            }
            catch { }
        }

        public static void ShowGrimoire(bool inMission = false)
        {
            if (!HasAnySchool)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "No colour calls to you.", Color.FromUint(0xFFAAAAAA)));
                return;
            }

            var context = inMission ? SpellContext.Mission : SpellContext.Map;
            var known = SpellDatabase.All
                .Where(s => HasSchool(s.School) && s.Context == context)
                .ToList();

            if (known.Count == 0)
            {
                string where = inMission ? "battle" : "campaign map";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"No spells available for the {where}.", Color.FromUint(0xFFAAAAAA)));
                return;
            }

            string header = inMission ? "Battle Spells" : "Campaign Spells";
            int totalPages = (known.Count + GrimoirePageSize - 1) / GrimoirePageSize;
            _grimoirePage  = _grimoirePage % totalPages;
            var page       = known.Skip(_grimoirePage * GrimoirePageSize).Take(GrimoirePageSize).ToList();

            InformationManager.DisplayMessage(new InformationMessage(
                $"══ Spellbook — {header} — Page {_grimoirePage + 1}/{totalPages} ══",
                new Color(0.8f, 0.8f, 0.8f)));

            foreach (SpellEntry s in page)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"  [{s.Combo}]  {s.Name}  ({ColorSchoolData.Info[s.School].Name})",
                    ColorSchoolData.GetMessageColor(s.School)));
            }

            if (totalPages > 1)
                InformationManager.DisplayMessage(new InformationMessage(
                    "[ Press S / L3 again for next page ]",
                    new Color(0.5f, 0.5f, 0.5f)));

            _grimoirePage = (_grimoirePage + 1) % totalPages;
        }

        public static void Save(IDataStore store)
        {
            var schoolList = _chosenSchools.Select(s => (int)s).ToList();
            var ccKeys     = _castCounters.Keys.Select(k => (int)k).ToList();
            var ccVals     = _castCounters.Values.ToList();
            var giftedList = _giftedChildIds.ToList();

            store.SyncData("COC_ChosenSchools",    ref schoolList);
            store.SyncData("COC_CastCounterKeys",  ref ccKeys);
            store.SyncData("COC_CastCounterVals",  ref ccVals);
            store.SyncData("COC_GiftedChildIds",   ref giftedList);
            store.SyncData("COC_PurpleFertility",  ref _purpleFertilityLevel);

            _chosenSchools.Clear();
            if (schoolList != null)
                foreach (int v in schoolList) _chosenSchools.Add((ColorSchool)v);

            if (ccKeys != null && ccVals != null)
                for (int i = 0; i < Math.Min(ccKeys.Count, ccVals.Count); i++)
                    _castCounters[(ColorSchool)ccKeys[i]] = ccVals[i];

            _giftedChildIds.Clear();
            if (giftedList != null)
                foreach (var id in giftedList) _giftedChildIds.Add(id);
        }
    }
}
