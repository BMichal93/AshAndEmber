// =============================================================================
// COLOURS OF CALRADIA â€” ColourKnowledge.cs
// Mount & Blade II: Bannerlord Mod  v1.2.0.0
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
    // 4. COLOUR KNOWLEDGE  â€” tracks chosen schools, cast counts, grimoire
    // =========================================================================
    public static class ColourKnowledge
    {
        private static readonly HashSet<ColorSchool> _chosenSchools = new HashSet<ColorSchool>();
        // Accumulated casts per school â€” every 25 casts shifts the associated trait
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
        private static Action _deferredInquiry = null;

        public static void FlushDeferredInquiry()
        {
            Action pending = _deferredInquiry;
            _deferredInquiry = null;
            pending?.Invoke();
        }

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
            if (count == 6) return 7;
            if (count == 5) return 5;
            if (count >= 2)
            {
                var positions = _chosenSchools.Select(s => (int)s).ToHashSet();
                int min = positions.Min();
                int max = positions.Max();
                bool contiguous = true;
                for (int i = min; i <= max; i++)
                    if (!positions.Contains(i)) { contiguous = false; break; }
                if (contiguous) return 0;
                return 3;
            }
            return 0;
        }

        public static bool ReducePurpleFertility()
        {
            if (_purpleFertilityLevel <= 0.01f) return false;
            _purpleFertilityLevel = Math.Max(0.01f, _purpleFertilityLevel - 0.01f);
            return true;
        }

        public static void AddSchool(ColorSchool school) => _chosenSchools.Add(school);

        public static void ClearAllSchools() => _chosenSchools.Clear();

        public static void ResetForNewGame()
        {
            _chosenSchools.Clear();
            foreach (var key in _castCounters.Keys.ToList()) _castCounters[key] = 0;
            _giftedChildIds.Clear();
            _purpleFertilityLevel = 1.0f;
            _deferredInquiry = null;
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
                // Purple hollows out all personality â€” each threshold nudges every trait toward 0
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

            // Saturation status at the top
            if (!SaturationSystem.IsPlayerPrism && !SaturationSystem.IsPlayerBlight)
                lines.Add($"Saturation: {SaturationSystem.PlayerSaturation}/{SaturationSystem.PlayerMaxSaturation}  (resets at nightfall)");

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
                ? "Hold LB + left stick (↑/←/→), then release."
                : "Hold Left Alt + combo (W/A/D keys), then release.";
            string closeHint   = !inMission
                ? (usingController ? "  A=Cast  B=Guide  (Guide→Back to return here)."
                                   : "  Escape to close.")
                : "";
            string description = $"{inputHint}  Active: {active} (W=↑, A=←, D=→, S mid-combo=↓).{closeHint}\n\n"
                               + string.Join("\n", lines);

            if (!inMission)
            {
                // Campaign map: offer spell cast button in place of Guide
                InformationManager.ShowInquiry(new InquiryData(
                    "Spell Prism",
                    description,
                    true, true,
                    "Cast a Spell", "Guide",
                    () => { _deferredInquiry = ShowCampaignCastMenu; },
                    () => { _deferredInquiry = () => ShowGuide(false, usingController); }
                ), true, true);
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Spell Prism",
                    description,
                    true, true,
                    "Close", "Guide",
                    () => { }, () => { _deferredInquiry = () => ShowGuide(inMission, usingController); }
                ), true, true);
            }
        }

        private static void ShowCampaignCastMenu()
        {
            var mapSpells = SpellDatabase.All
                .Where(s => s.Context == SpellContext.Map && HasSchool(s.School))
                .OrderBy(s => (int)s.School)
                .ToList();

            if (mapSpells.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "No campaign map spells available.", Color.FromUint(0xFFAAAAAA)));
                return;
            }

            var elements = mapSpells.Select(s => new InquiryElement(
                s.Combo,
                $"{s.Name}  [{ComboToArrows(s.Combo)}]",
                null, true,
                $"{s.ShortDesc}\n\n{s.Flavour}"
            )).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Cast a Campaign Spell",
                "Select a spell to cast. Requirements and limitations apply as normal.",
                elements,
                true, 1, 1,
                "Cast", "Cancel",
                chosen =>
                {
                    if (chosen?.Count > 0)
                    {
                        string combo = chosen[0].Identifier?.ToString();
                        if (!string.IsNullOrEmpty(combo))
                            _deferredInquiry = () => SpellEffects.Execute(combo);
                    }
                },
                null, "", false
            ), false, true);
        }

        private static void ShowGuide(bool inMission, bool usingController)
        {
            const string guide =
"HOW TO CAST\n" +
"  Hold focus key, input a 4-key combo, release to cast.\n" +
"  Keyboard : Hold Left Alt + W/A/D (S with empty buffer opens spellbook).\n" +
"  Controller: Hold LB + left stick. L3 opens spellbook.\n" +
"  Shortcut  : Release after 2-key form prefix for a random known spell of that form.\n" +
"\n" +
"COMBO STRUCTURE  (first 2 keys = Form | last 2 keys = Colour)\n" +
"  ↑↑  Blast   — cone attack forward\n" +
"  →→  Self    — aura on caster\n" +
"  ←←  Create  — area effect, toggleable\n" +
"  ↑←  Affect  — campaign map, situational\n" +
"  ←↑  Invoke  — campaign map, advanced\n" +
"  ↑→  Commune — campaign map, ambient effects\n" +
"\n" +
"COLOUR SUFFIXES (last 2 keys)\n" +
"  Red →→  Orange ←↓  Yellow ↓↓  Green ←←  Blue →↑  Purple ↓↑\n" +
"\n" +
"COLOUR LIMITATIONS\n" +
"  Red    — Furious: each cast auto-issues a Charge order. Blood Price: −2 HP per cast.\n" +
"  Orange — Joyful Cast: cannot cast if party morale is below 45.\n" +
"  Yellow — Animal Fear: cannot cast from horseback.\n" +
"  Green  — Nature's Calling: cannot cast Green campaign spells inside settlements.\n" +
"  Blue   — Scholar's Craft: cannot cast Blue in battle while wielding a weapon.\n" +
"  Purple — The Slow Unravelling: each Purple cast costs −1% fertility and +1 day of age.\n" +
"\n" +
"SATURATION\n" +
"  Each cast gains 0–3 Saturation. Max = hero level + 10, minus any permanent oversaturation reductions (no upper cap).\n" +
"  Resets to 0 when darkness falls (night or dark locations).\n" +
"  Oversaturation: knocked down 3s, random trait shift, Max −1 (permanent).\n" +
"  Leveling up restores 1 max Saturation per level, but never removes prior reductions.\n" +
"  Max reaches 0: choose to lose all colours OR embrace the Blight (−100 relations, immune).\n" +
"  Prism and Blights are immune to all Oversaturation effects.\n" +
"\n" +
"PERSONALITY DRIFT\n" +
"  Every 25 casts nudges the school's associated trait by 1. Purple hollows all toward 0.\n" +
"\n" +
"MADNESS\n" +
"  Non-adjacent colour schools conflict. On each cast:\n" +
"  Non-contiguous mix — 3% chance the spell fires as a random other-colour spell.\n" +
"  5 schools — 5% chance. Weekly trait fracture.\n" +
"  6 schools — 7% chance. Weekly trait fracture.\n" +
"  Adjacent colours (e.g. Red+Orange+Yellow) never trigger madness.\n" +
"\n" +
"DAY / NIGHT\n" +
"  Bright (07:00–20:00) — full casting.\n" +
"  Dim   (05:00–07:00 and 20:00–22:00) — 33% fizzle chance.\n" +
"  Dark  (22:00–05:00) — casting blocked entirely. Saturation resets.\n" +
"\n" +
"LORDS & COLOURS\n" +
"  Colour lords cast in battle and on the campaign map.\n" +
"  Lords sharing a colour (1–2 schools each) are drawn together — higher relations.\n" +
"  Lords with 5+ schools are feared and distrusted by all magical peers.\n" +
"  Colour lords maintain a morale floor before battle (10 per colour, max 60) and recover\n" +
"  wounded troops after battle (5% per colour of wounded healed).\n" +
"  Weekly: 5% chance a random colour lord oversaturates — 79% lose colours (re-seeded in\n" +
"  one week), 21% die and spawn a Blight of their colour. Blights no longer auto-respawn.\n" +
"\n" +
"MAGICAL EVENTS\n" +
"  Battle: ~8% of battles trigger a colour phenomenon at start. A sky event (season-\n" +
"  weighted) names the effect and affects all fighters on the field equally.\n" +
"  Campaign: each week, a colour-ambient event may touch a random party, settlement,\n" +
"  or lord anywhere in Calradia, independent of colour lords' own spellcasting.\n" +
"\n" +
"THE PRISM\n" +
"  One lord carries all six colours. They cast constantly in battle and on the campaign\n" +
"  map (20% independent daily chance + reaction to every other lord cast). Their\n" +
"  personality shifts each week. When slain, if you hold all six colours, you may\n" +
"  inherit the mantle (30% chance). Otherwise a new Prism rises within a month.\n" +
"  As the Prism you are immune to Madness and Oversaturation.\n" +
"  At character creation you may also choose 'I am a Prism' (easy mode) to start with\n" +
"  all six colours and full immunity, with no attribute penalties.";

            InformationManager.ShowInquiry(new InquiryData(
                "Mechanics — Colours of Calradia",
                guide,
                true, true,
                "Close", "Back",
                () => { }, () => { _deferredInquiry = () => ShowGrimoire(inMission, usingController); }
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
