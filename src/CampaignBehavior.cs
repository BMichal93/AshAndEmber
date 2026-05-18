// =============================================================================
// COLOURS OF CALRADIA — CampaignBehavior.cs
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
    // 6. CAMPAIGN BEHAVIOR  — save/load, character creation, daily events
    // =========================================================================
    public class MagicCampaignBehavior : CampaignBehaviorBase
    {
        private bool _selectionDone;

        private static readonly ColorSchool[] _allSchoolsOrdered =
        {
            ColorSchool.Red, ColorSchool.Orange, ColorSchool.Yellow,
            ColorSchool.Green, ColorSchool.Blue, ColorSchool.Purple
        };

        // ── Daily overindulgent food drain (Orange B1) ───────────────────────
        // How many extra food units consumed per day per Orange caster
        private const int OverindulgentFoodDrain = 2;
        // Party size soft-penalty threshold (Yellow C1 Uncharismatic)
        private const int UncharismaticPenaltyThreshold = 3;

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnMissionEnded);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.HeroCreated.AddNonSerializedListener(this, OnHeroCreated);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, OnCompanionAdded);
            CampaignEvents.HeroLevelledUp.AddNonSerializedListener(this, OnHeroLevelledUp);
        }

        // ── New game: present colour selection ───────────────────────────────
        private void OnNewGameCreated()
        {
            var elements = _allSchoolsOrdered.Select(school =>
            {
                var info = ColorSchoolData.Info[school];
                string hint = $"{info.FlavorText}\n\nPenalty: {info.AttributePenalty}\n{info.LimitationA}\n{info.LimitationB}";
                return new InquiryElement(school, info.Name, null, true, hint);
            }).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Colours of Calradia",
                "Magic flows through the world in colours. Choose which colours call to you — or choose none to walk an uncoloured path. Hover each colour to read its penalties.",
                elements,
                false, 0, 6,
                "These colours call to me.",
                "No colour calls to me.",
                chosen =>
                {
                    var schools = chosen?.Select(e => (ColorSchool)e.Identifier).ToList()
                                  ?? new List<ColorSchool>();
                    if (schools.Count == 0)
                        InformationManager.DisplayMessage(new InformationMessage(
                            "No colour calls to you. You walk an uncoloured path.", Color.FromUint(0xFFAAAAAA)));
                    else
                    {
                        foreach (var s in schools) ColourKnowledge.AddSchool(s);
                        ApplySchoolPenalties(schools);
                        ShowStartingSpells(schools);
                    }
                    _selectionDone = true;
                    ColourLordRegistry.SeedInitialLords();
                },
                _ =>
                {
                    _selectionDone = true;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "No colour calls to you. You walk an uncoloured path.", Color.FromUint(0xFFAAAAAA)));
                    ColourLordRegistry.SeedInitialLords();
                },
                "", false
            ), false, true);
        }

        private void ApplySchoolPenalties(List<ColorSchool> schools)
        {
            Hero player = Hero.MainHero;
            if (player == null) return;

            foreach (ColorSchool school in schools)
            {
                var info = ColorSchoolData.Info[school];
                try
                {
                    CharacterAttribute attr = ColorSchoolData.GetPenaltyAttribute(school);
                    player.HeroDeveloper.AddAttribute(attr, -1, false);
                }
                catch { }

                InformationManager.DisplayMessage(new InformationMessage(
                    $"{info.Name} — {info.AttributePenalty}. The mark your gift left on your body.",
                    ColorSchoolData.GetMessageColor(school)));
            }

            // Madness — colours that are not adjacent on the ring (Red-Orange-Yellow-Green-Blue-Purple)
            // create an internal conflict that fractures the mage's personality.
            if (schools.Count >= 2 && !AreColoursContiguous(schools))
            {
                ApplyMadness(player);
                MBInformationManager.AddQuickInformation(
                    new TextObject("MADNESS — The colours you wield are incompatible. Their conflicting natures fracture your sense of self. Two personality traits have shifted. You will not recover them."),
                    8000, null, null, "");
            }
            // Five or more colours — madness regardless of adjacency, and the fracture never heals.
            if (schools.Count > 4)
            {
                ApplyMadness(player);
                MBInformationManager.AddQuickInformation(
                    new TextObject("THE FRACTURE — No mind can hold five colours whole. Something in you breaks — and it will keep breaking, week after week, for as long as you walk with this burden."),
                    8000, null, null, "");
            }
        }

        // Returns true if the chosen schools form a contiguous arc on the colour ring.
        // Ring order: Red(0)-Orange(1)-Yellow(2)-Green(3)-Blue(4)-Purple(5), wraps around.
        private bool AreColoursContiguous(List<ColorSchool> schools)
        {
            var positions = schools.Select(s => (int)s).ToHashSet();
            int n = 6;
            // Try each chosen colour as arc start; walk forward checking all are present
            foreach (int start in positions)
            {
                bool allPresent = true;
                for (int step = 0; step < positions.Count; step++)
                {
                    if (!positions.Contains((start + step) % n)) { allPresent = false; break; }
                }
                if (allPresent) return true;
            }
            return false;
        }

        private static readonly TraitObject[] MadnessTraits =
        {
            DefaultTraits.Mercy, DefaultTraits.Valor, DefaultTraits.Honor,
            DefaultTraits.Generosity, DefaultTraits.Calculating
        };

        private void ApplyMadness(Hero player)
        {
            var rng = new Random();
            var shuffled = MadnessTraits.OrderBy(_ => rng.Next()).Take(2).ToList();
            foreach (TraitObject trait in shuffled)
            {
                try
                {
                    int current = player.GetTraitLevel(trait);
                    int shift   = rng.Next(2) == 0 ? 1 : -1;
                    int next    = Math.Max(-2, Math.Min(2, current + shift));
                    if (next != current)
                    {
                        player.SetTraitLevel(trait, next);
                        string dir = shift > 0 ? "increased" : "decreased";
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Madness: Your {trait.StringId} trait has {dir} ({next:+0;-0}).",
                            Color.FromUint(0xFFCC44FF)));
                    }
                }
                catch { }
            }
        }

        private void ShowStartingSpells(List<ColorSchool> schools)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "Your colours are chosen. Hold Left Alt and type a 4-key combo (WASD) then release to cast. S opens spellbook.",
                new Color(0.8f, 0.8f, 0.8f)));

            foreach (ColorSchool school in schools)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"── {ColorSchoolData.Info[school].Name} spells ──",
                    ColorSchoolData.GetMessageColor(school)));

                foreach (SpellEntry s in SpellDatabase.BySchool(school))
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"  [{s.Combo}]  {s.Name}",
                        ColorSchoolData.GetMessageColor(school)));
                }
            }
        }

        // ── Daily tick ───────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (!_selectionDone)
                _selectionDone = true;

            ColourLordRegistry.SeedInitialLords();
            ColourLordRegistry.FlushAnnouncements();
            ColourLordRegistry.DailyMapCast();
            ColourUnitRegistry.SeedInitialUnits();
            ColourUnitRegistry.DailyMaintenance();
            ColourUnitRegistry.DailyMapCast();
            ApplyDailyLimitations();
        }

        private void ApplyDailyLimitations()
        {
            if (!ColourKnowledge.HasAnySchool) return;
            Hero player = Hero.MainHero;
            MobileParty party = MobileParty.MainParty;
            if (player == null || party == null) return;

            // B1 Overindulgent (Orange) — extra food drain
            if (ColourKnowledge.HasSchool(ColorSchool.Orange))
            {
                try
                {
                    ItemObject food = MBObjectManager.Instance.GetObject<ItemObject>("grain")
                                   ?? MBObjectManager.Instance.GetObject<ItemObject>("fish");
                    if (food != null && party.ItemRoster.FindIndexOfItem(food) >= 0)
                    {
                        party.ItemRoster.AddToCounts(food, -Math.Min(OverindulgentFoodDrain,
                            party.ItemRoster.GetItemNumber(food)));
                    }
                }
                catch { }
            }

            // C1 Uncharismatic (Yellow) — morale drain if party is near its limit
            if (ColourKnowledge.HasSchool(ColorSchool.Yellow))
            {
                try
                {
                    int partySize = party.MemberRoster.TotalManCount;
                    int limit     = party.Party.PartySizeLimit;
                    // If party within 3 of its natural limit, impose morale penalty
                    if (limit - partySize <= UncharismaticPenaltyThreshold)
                        party.RecentEventsMorale -= 3f;
                }
                catch { }
            }
        }

        // ── Hourly tick ──────────────────────────────────────────────────────
        private void OnHourlyTick()
        {
            ColourLordRegistry.CheckRespawnTimers();
            SpellEffects.TickHourlyMapEffects();
        }

        // ── Weekly tick ──────────────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            if (ColourKnowledge.AllSchools.Count() <= 4) return;
            Hero player = Hero.MainHero;
            if (player == null) return;
            // Five or more colours — the fracture never heals; traits shift without warning each week
            var rng = new Random();
            bool anyChanged = false;
            foreach (TraitObject trait in MadnessTraits)
            {
                try
                {
                    int next    = rng.Next(5) - 2; // −2 to +2
                    int current = player.GetTraitLevel(trait);
                    if (next != current)
                    {
                        player.SetTraitLevel(trait, next);
                        anyChanged = true;
                    }
                }
                catch { }
            }
            if (anyChanged)
                InformationManager.DisplayMessage(new InformationMessage(
                    "The Fracture: Five colours tear at your sense of self — your personality shifts without warning.",
                    Color.FromUint(0xFFCC44FF)));
        }

        // ── Mission ended ────────────────────────────────────────────────────
        private void OnMissionEnded(IMission mission)
        {
            ActiveEffectManager.ClearMissionEffects();
            ColourLordAI.ClearCooldowns();
            SpellEffects.ClearAreaEffects();   // removes all engine lights + particle emitters
            SpellEffects.ClearSelfEffects();
            SpellEffects.ClearGlows();
            SpellEffects.ClearMoves();         // releases stale agent refs from Bastion pushes
            SpellEffects.RestoreColourNamePrefixes();
            ColourUnitRegistry.OnMissionEnded();
        }

        // ── Hero killed ──────────────────────────────────────────────────────
        private void OnHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (!ColourLordRegistry.IsColourLord(victim)) return;
            ColourLordRegistry.OnLordDied(victim);
        }

        // ── Children inherit colours ─────────────────────────────────────────
        private void OnHeroCreated(Hero hero, bool bornNaturally)
        {
            if (!bornNaturally || !ColourKnowledge.HasAnySchool) return;
            try
            {
                bool parentIsPlayer = hero.Mother == Hero.MainHero || hero.Father == Hero.MainHero;
                if (!parentIsPlayer) return;
                if (MBRandom.RandomInt(100) >= 30) return;

                var parentSchools = ColourKnowledge.AllSchools.ToList();
                int parentCount   = parentSchools.Count;

                // Child gets parent count ± 1, clamped to [1, 6]
                int delta      = MBRandom.RandomInt(3) - 1; // -1, 0, or +1
                int childCount = Math.Max(1, Math.Min(6, parentCount + delta));

                // Always share at least one colour with parent
                var childSchools = new List<ColorSchool>();
                childSchools.Add(parentSchools[MBRandom.RandomInt(parentSchools.Count)]);

                // Fill remaining slots from schools the child doesn't yet have
                var pool = ((ColorSchool[])Enum.GetValues(typeof(ColorSchool)))
                    .Where(s => !childSchools.Contains(s)).ToList();
                while (childSchools.Count < childCount && pool.Count > 0)
                {
                    int idx = MBRandom.RandomInt(pool.Count);
                    childSchools.Add(pool[idx]);
                    pool.RemoveAt(idx);
                }

                ColourLordRegistry.GrantChildColours(hero, childSchools);
                ColourKnowledge.AddGiftedChild(hero.StringId);

                string schoolNames = string.Join(", ",
                    childSchools.Select(s => ColorSchoolData.Info[s].Name));
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{hero.Name} was born carrying {childCount} colour{(childCount > 1 ? "s" : "")}: {schoolNames}.",
                    new Color(0.75f, 0.75f, 0.85f)));
            }
            catch { }
        }

        // ── Level-up colour learning ──────────────────────────────────────────
        private bool _levelUpPickActive;

        private void OnHeroLevelledUp(Hero hero, bool shouldNotify)
        {
            if (hero != Hero.MainHero) return;
            if (!ColourKnowledge.HasAnySchool) return;
            if (hero.Level % 10 != 0) return;
            if (ColourKnowledge.AllSchools.Count() >= 6) return;
            if (_levelUpPickActive) return;

            var available = _allSchoolsOrdered.Where(s => !ColourKnowledge.HasSchool(s)).ToList();
            if (available.Count == 0) return;

            _levelUpPickActive = true;

            var elements = available.Select(school =>
            {
                var info = ColorSchoolData.Info[school];
                string hint = $"{info.FlavorText}\n\nPenalty: {info.AttributePenalty}\n{info.LimitationA}\n{info.LimitationB}";
                return new InquiryElement(school, info.Name, null, true, hint);
            }).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Level {hero.Level} — A Colour Stirs",
                "Your growth opens new channels. Choose one colour to learn — or decline. Hover each colour to read its penalties.",
                elements,
                false, 0, 1,
                "This colour calls to me.",
                "Not now.",
                chosen =>
                {
                    _levelUpPickActive = false;
                    if (chosen?.Count > 0)
                    {
                        ColorSchool school = (ColorSchool)chosen[0].Identifier;
                        ColourKnowledge.AddSchool(school);
                        ApplySchoolPenalties(new List<ColorSchool> { school });
                        ShowStartingSpells(new List<ColorSchool> { school });
                    }
                    else
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The colour recedes. No new school learned.", Color.FromUint(0xFFAAAAAA)));
                },
                _ =>
                {
                    _levelUpPickActive = false;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The colour recedes. No new school learned.", Color.FromUint(0xFFAAAAAA)));
                },
                "", false
            ), false, true);
        }

        // ── Companions may have colours ──────────────────────────────────────
        private void OnCompanionAdded(Hero companion)
        {
            ColourLordRegistry.TryGrantCompanionColours(companion);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("COC_SelectionDone", ref _selectionDone);
            ColourKnowledge.Save(dataStore);
            ColourLordRegistry.Save(dataStore);
            ColourUnitRegistry.Save(dataStore);
        }
    }
}
