// =============================================================================
// COLOURS OF CALRADIA — CampaignBehavior.cs
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
    // 6. CAMPAIGN BEHAVIOR  — save/load, character creation, daily events
    // =========================================================================
    public class MagicCampaignBehavior : CampaignBehaviorBase
    {
        private bool _selectionDone;
        private bool _blightLearnActive;
        private static readonly Random _rng = new Random();

        private static readonly ColorSchool[] _allSchoolsOrdered =
        {
            ColorSchool.Red, ColorSchool.Orange, ColorSchool.Yellow,
            ColorSchool.Green, ColorSchool.Blue, ColorSchool.Purple
        };

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnMissionEnded);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.HeroCreated.AddNonSerializedListener(this, OnHeroCreated);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, OnCompanionAdded);
            CampaignEvents.HeroLevelledUp.AddNonSerializedListener(this, OnHeroLevelledUp);
        }

        // ── New game: present colour selection ───────────────────────────────
        private void OnNewGameCreated()
        {
            ColourKnowledge.ResetForNewGame();
            SpellEffects.ResetCampaignCounters();
            BlightSystem.ResetForNewGame();
            SaturationSystem.ResetForNewGame();

            var elements = new List<InquiryElement>
            {
                new InquiryElement("PRISM", "I am a Prism", null, true,
                    "[Easy mode] You are born knowing all six colours. " +
                    "You are immune to Madness and Oversaturation. No attribute penalties apply.")
            };
            elements.AddRange(_allSchoolsOrdered.Select(school =>
            {
                var info = ColorSchoolData.Info[school];
                string hint = $"{info.FlavorText}\n\nPenalty: {info.AttributePenalty}\n{info.LimitationA}\n{info.LimitationB}";
                return new InquiryElement(school, info.Name, null, true, hint);
            }));

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Colours of Calradia",
                "Magic flows through the world in colours. Choose which colours call to you — or choose none to walk an uncoloured path. Hover each colour to read its penalties.",
                elements,
                false, 0, 6,
                "These colours call to me.",
                "No colour calls to me.",
                chosen =>
                {
                    if (chosen?.Any(e => e.Identifier is string s && s == "PRISM") == true)
                    {
                        foreach (var school in _allSchoolsOrdered) ColourKnowledge.AddSchool(school);
                        ColourLordRegistry.SetPlayerAsPrism();
                        ShowStartingSpells(_allSchoolsOrdered.ToList());
                    }
                    else
                    {
                        var schools = chosen?.Where(e => e.Identifier is ColorSchool)
                                             .Select(e => (ColorSchool)e.Identifier).ToList()
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
                    }
                    _selectionDone = true;
                    ColourLordRegistry.SeedInitialLords();
                    BlightSystem.InitializeBlights();
                },
                _ =>
                {
                    _selectionDone = true;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "No colour calls to you. You walk an uncoloured path.", Color.FromUint(0xFFAAAAAA)));
                    ColourLordRegistry.SeedInitialLords();
                    BlightSystem.InitializeBlights();
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

            // Madness — colours that are not adjacent on the spectrum (Red-Orange-Yellow-Green-Blue-Purple)
            // create an internal conflict that fractures the mage's personality. Red and Purple do not wrap.
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

        // Returns true if the chosen schools form a contiguous segment on the colour spectrum.
        // Spectrum order: Red(0)-Orange(1)-Yellow(2)-Green(3)-Blue(4)-Purple(5) — linear, no wrap.
        // Red and Purple are at opposite ends and are NOT adjacent.
        private bool AreColoursContiguous(List<ColorSchool> schools)
        {
            var positions = schools.Select(s => (int)s).ToHashSet();
            int min = positions.Min();
            int max = positions.Max();
            for (int i = min; i <= max; i++)
                if (!positions.Contains(i)) return false;
            return true;
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
                "Your colours are chosen. Hold Left Alt and type a 5-key combo (WASD) then release to cast. S opens spellbook.",
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
            ColourLordRegistry.FlushDeferredPrismInquiry();
            BlightSystem.InitializeBlights();
            ColourLordRegistry.DailyMapCast();
            ColourUnitRegistry.SeedInitialUnits();
            ColourUnitRegistry.DailyMaintenance();
            ColourUnitRegistry.DailyMapCast();
            SaturationSystem.FlushMaxDepletionPrompt();
        }

        // ── Hourly tick ──────────────────────────────────────────────────────
        private void OnHourlyTick()
        {
            ColourLordRegistry.CheckRespawnTimers();
            BlightSystem.CheckRespawnTimers();
            SpellEffects.TickHourlyMapEffects();
            SaturationSystem.CheckNightReset();
        }

        // ── Weekly tick ──────────────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            var rng = new Random();

            // Five or more colours on the player — the fracture never heals (Prism immune)
            if (ColourKnowledge.AllSchools.Count() > 4 && !SaturationSystem.IsPlayerPrism)
            {
                Hero player = Hero.MainHero;
                if (player != null)
                {
                    bool anyChanged = false;
                    foreach (TraitObject trait in MadnessTraits)
                    {
                        try
                        {
                            int next    = rng.Next(5) - 2;
                            int current = player.GetTraitLevel(trait);
                            if (next != current) { player.SetTraitLevel(trait, next); anyChanged = true; }
                        }
                        catch { }
                    }
                    if (anyChanged)
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The Fracture: Five colours tear at your sense of self — your personality shifts without warning.",
                            Color.FromUint(0xFFCC44FF)));
                }
            }

            // NPC Prism lord — personality shifts every week (player Prism is immune)
            Hero prism = ColourLordRegistry.GetPrismLord();
            if (prism != null && prism != Hero.MainHero)
            {
                bool prismChanged = false;
                foreach (TraitObject trait in MadnessTraits)
                {
                    try
                    {
                        int next    = rng.Next(5) - 2;
                        int current = prism.GetTraitLevel(trait);
                        if (next != current) { prism.SetTraitLevel(trait, next); prismChanged = true; }
                    }
                    catch { }
                }
                if (prismChanged)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The Prism — {prism.Name}'s personality fractures again. Their nature bends without reason.",
                        new Color(0.9f, 0.7f, 1.0f)));
            }

            // 2% chance a random NPC colour lord oversaturates each week
            try
            {
                var npcLords = Hero.AllAliveHeroes
                    .Where(h => h != Hero.MainHero
                             && ColourLordRegistry.IsColourLord(h)
                             && !BlightSystem.IsBlight(h)
                             && !ColourLordRegistry.IsPrismLord(h))
                    .ToList();
                if (npcLords.Count > 0 && rng.Next(50) == 0)
                    ColourLordRegistry.OnLordOversaturated(npcLords[rng.Next(npcLords.Count)]);
            }
            catch { }
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

            // Offer colour learning for any blight the player personally killed this mission
            if (ColourKnowledge.HasAnySchool)
            {
                foreach (ColorSchool school in BlightSystem.ConsumePlayerBlightKills())
                {
                    if (!ColourKnowledge.HasSchool(school))
                        ShowBlightLearningPrompt(school);
                }
            }
            else
            {
                BlightSystem.ConsumePlayerBlightKills(); // drain without offering (no attunement)
            }
        }

        // ── Battle bonus: colour lords heal a fraction of wounded after each battle ──
        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null) return;
            try { ApplyBattleBonus(mapEvent.AttackerSide); } catch { }
            try { ApplyBattleBonus(mapEvent.DefenderSide); } catch { }
        }

        private void ApplyBattleBonus(MapEventSide side)
        {
            if (side == null) return;
            try
            {
                foreach (var meparty in side.Parties)
                {
                    try
                    {
                        PartyBase party = meparty?.Party;
                        Hero leader = party?.LeaderHero;
                        if (leader == null || !ColourLordRegistry.IsColourLord(leader)) continue;

                        var colors = ColourLordRegistry.GetColors(leader);
                        if (colors.Count == 0) continue;

                        // Post-battle wound recovery — colour magic mends the injured
                        float healFraction = colors.Count * 0.05f;
                        foreach (var element in party.MemberRoster.GetTroopRoster().ToList())
                        {
                            int wounded = element.WoundedNumber;
                            if (wounded <= 0) continue;
                            int toHeal = Math.Max(1, (int)Math.Round(wounded * healFraction));
                            toHeal = Math.Min(toHeal, wounded);
                            party.MemberRoster.AddToCounts(element.Character, 0, false, -toHeal);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ── Hero killed ──────────────────────────────────────────────────────
        private void OnHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (ColourLordRegistry.IsColourLord(victim))
                ColourLordRegistry.OnLordDied(victim);

            if (BlightSystem.IsBlight(victim))
            {
                ColorSchool school = BlightSystem.GetBlightSchool(victim);
                BlightSystem.OnBlightKilled(victim);
                // NPC lord kills blight: 7% chance to absorb its colour
                if (killer != null && killer != Hero.MainHero)
                    BlightSystem.OnNpcKilledBlight(killer, school);
                // Player kill is handled via mission-level flag → OnMissionEnded
            }
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

        // ── Blight colour learning prompt ─────────────────────────────────────
        private void ShowBlightLearningPrompt(ColorSchool school)
        {
            if (_blightLearnActive) return;
            if (ColourKnowledge.AllSchools.Count() >= 6) return;

            _blightLearnActive = true;
            var info = ColorSchoolData.Info[school];
            string hint = $"{info.FlavorText}\n\nPenalty: {info.AttributePenalty}\n{info.LimitationA}\n{info.LimitationB}";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"The Blight Falls — {info.Name} Awakens",
                $"You have slain the {info.Name} Blight. Their colour seeps into your blood. Will you accept it?",
                new List<InquiryElement> { new InquiryElement(school, info.Name, null, true, hint) },
                false, 0, 1,
                "Accept the colour.",
                "Reject it.",
                chosen =>
                {
                    _blightLearnActive = false;
                    if (chosen?.Count > 0)
                    {
                        ColourKnowledge.AddSchool(school);
                        ApplyBlightCrimePenalty(school);
                        ApplySchoolPenalties(new List<ColorSchool> { school });
                        ShowStartingSpells(new List<ColorSchool> { school });
                    }
                    else
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The colour recedes. You reject what the Blight offered.", Color.FromUint(0xFFAAAAAA)));
                },
                _ =>
                {
                    _blightLearnActive = false;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The colour recedes.", Color.FromUint(0xFFAAAAAA)));
                },
                "", false
            ), false, true);
        }

        // ── Level-up: recalc saturation max ──────────────────────────────────
        private void OnHeroLevelledUp(Hero hero, bool shouldNotify)
        {
            if (hero == Hero.MainHero)
                SaturationSystem.RecalcMax();
        }

        // ── Companions may have colours ──────────────────────────────────────
        private void OnCompanionAdded(Hero companion)
        {
            ColourLordRegistry.TryGrantCompanionColours(companion);
        }

        private void ApplyBlightCrimePenalty(ColorSchool school)
        {
            Hero player = Hero.MainHero;
            if (player == null) return;

            const float CrimePenalty = 30f;
            const float InfluencePenalty = 12f;

            try
            {
                IFaction crimeFaction = player.Clan?.Kingdom as IFaction ?? player.Clan as IFaction;
                if (crimeFaction != null)
                    ChangeCrimeRatingAction.Apply(crimeFaction, CrimePenalty, true);
            }
            catch { }

            try
            {
                if (player.Clan?.Kingdom != null)
                    player.AddInfluenceWithKingdom(-InfluencePenalty);
            }
            catch { }

            var info = ColorSchoolData.Info[school];
            InformationManager.DisplayMessage(new InformationMessage(
                $"The colours darken around you. {info.Name} blight leaves a dangerous stain: crime rises sharply{(player.Clan?.Kingdom != null ? " and your influence gutters." : ".")}",
                ColorSchoolData.GetMessageColor(school)));
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("COC_SelectionDone", ref _selectionDone);
            ColourKnowledge.Save(dataStore);
            ColourLordRegistry.Save(dataStore);
            ColourUnitRegistry.Save(dataStore);
            BlightSystem.Save(dataStore);
            SaturationSystem.Save(dataStore);
        }
    }
}
