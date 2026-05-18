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
                int min = positions.Min();
                int max = positions.Max();
                bool contiguous = true;
                for (int i = min; i <= max; i++)
                    if (!positions.Contains(i)) { contiguous = false; break; }
                if (contiguous) return 0;
                return 5;
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

        public static void ResetForNewGame()
        {
            _chosenSchools.Clear();
            foreach (var key in _castCounters.Keys.ToList()) _castCounters[key] = 0;
            _giftedChildIds.Clear();
            _purpleFertilityLevel = 1.0f;
        }

        public static bool IsChildGifted(string heroId) => _giftedChildIds.Contains(heroId);
        public static void AddGiftedChild(string heroId) => _giftedChildIds.Add(heroId);

        // Called after each successful cast to drift personality and track stats
        public static void RecordCast(ColorSchool school)
        {
            if (!_castCounters.ContainsKey(school)) return;
            _castCounters[school]++;

            if (_castCounters[school] % 25 != 0) return;

            if (school == ColorSchool.Purple)
            {
                // Purple hollows out all personality — each threshold nudges every trait toward 0
                try
                {
                    if (Hero.MainHero == null) return;
                    var traits = new[] { DefaultTraits.Calculating, DefaultTraits.Generosity,
                                         DefaultTraits.Mercy, DefaultTraits.Valor };
                    bool anyChanged = false;
                    foreach (var trait in traits)
                    {
                        int current = Hero.MainHero.GetTraitLevel(trait);
                        if (current == 0) continue;
                        int next = current > 0 ? current - 1 : current + 1;
                        Hero.MainHero.SetTraitLevel(trait, next);
                        anyChanged = true;
                    }
                    if (anyChanged)
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The grey hollows you out. Your personality fades toward nothing.",
                            ColorSchoolData.GetMessageColor(ColorSchool.Purple)));
                }
                catch { }
                return;
            }

            // All other schools shift their associated trait in one direction
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

        private static string ComboToArrows(string combo) =>
            combo.Replace("U", "↑").Replace("D", "↓").Replace("L", "←").Replace("R", "→");

        public static void ShowGrimoire(bool inMission = false, bool usingController = false)
        {
            if (!HasAnySchool)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "No colour calls to you.", Color.FromUint(0xFFAAAAAA)));
                return;
            }

            bool inBattle = inMission && SpellEffects.IsBattleMission();
            var lines     = new List<string>();

            var allKnown = SpellDatabase.All
                .Where(s => HasSchool(s.School))
                .OrderBy(s => (int)s.School).ToList();

            foreach (var grp in allKnown.GroupBy(s => s.School))
            {
                if (lines.Count > 0) lines.Add("");
                string schoolName = ColorSchoolData.Info[grp.Key].Name;
                lines.Add($"── {schoolName} ──");

                var battle = grp.Where(s => s.Context == SpellContext.Mission).ToList();
                var map    = grp.Where(s => s.Context == SpellContext.Map).ToList();

                if (battle.Count > 0)
                {
                    lines.Add("  [ Battle ]");
                    foreach (var s in battle)
                        lines.Add($"      {s.Name} [{ComboToArrows(s.Combo)}]: {s.ShortDesc}");
                }
                if (map.Count > 0)
                {
                    if (battle.Count > 0) lines.Add("");
                    lines.Add("  [ Campaign ]");
                    foreach (var s in map)
                        lines.Add($"      {s.Name} [{ComboToArrows(s.Combo)}]: {s.ShortDesc}");
                }
            }

            if (lines.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "No spells available.", Color.FromUint(0xFFAAAAAA)));
                return;
            }

            string active      = inBattle ? "battle" : "campaign map";
            string inputHint   = usingController
                ? "Hold LT + left stick (↑/←/→), then release."
                : "Hold Left Alt + combo (W/A/D keys), then release.";
            string description = $"{inputHint}  Active: {active}.\n\n"
                               + string.Join("\n", lines);

            InformationManager.ShowInquiry(new InquiryData(
                "Spell prism",
                description,
                true, false,
                "Close", "",
                () => { }, null
            ), true, true);
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
