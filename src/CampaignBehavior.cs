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
            var shuffled = MadnessTraits.OrderBy(_ => _rng.Next()).Take(2).ToList();
            foreach (TraitObject trait in shuffled)
            {
                try
                {
                    int current = player.GetTraitLevel(trait);
                    int shift   = _rng.Next(2) == 0 ? 1 : -1;
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

            try { ColourLordRegistry.SeedInitialLords(); } catch { }
            try { ColourLordRegistry.FlushAnnouncements(); } catch { }
            try { ColourLordRegistry.FlushDeferredPrismInquiry(); } catch { }
            try { ColourLordRegistry.FlushDeferredKills(); } catch { }
            try { BlightSystem.InitializeBlights(); } catch { }
            try { ColourLordRegistry.DailyMapCast(); } catch { }
            try { ColourUnitRegistry.SeedInitialUnits(); } catch { }
            try { ColourUnitRegistry.DailyMaintenance(); } catch { }
            try { ColourUnitRegistry.DailyMapCast(); } catch { }
            try { SaturationSystem.FlushMaxDepletionPrompt(); } catch { }
        }

        // ── Hourly tick ──────────────────────────────────────────────────────
        private void OnHourlyTick()
        {
            try { ColourLordRegistry.CheckRespawnTimers(); } catch { }
            try { BlightSystem.CheckRespawnTimers(); } catch { }
            try { SaturationSystem.CheckNightReset(); } catch { }
        }

        // ── Weekly tick ──────────────────────────────────────────────────────
        private void OnWeeklyTick()
        {

            // Five or more colours on the player — the fracture never heals (Prism immune)
            try
            {
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
                                int next    = _rng.Next(5) - 2;
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
            }
            catch { }

            // NPC Prism lord is immune to madness — no weekly personality fracture.

            // 5% chance a random NPC colour lord oversaturates each week
            try
            {
                var npcLords = Hero.AllAliveHeroes
                    .Where(h => h != Hero.MainHero
                             && ColourLordRegistry.IsColourLord(h)
                             && !BlightSystem.IsBlight(h)
                             && !ColourLordRegistry.IsPrismLord(h))
                    .ToList();
                if (npcLords.Count > 0 && _rng.Next(20) == 0)
                    ColourLordRegistry.OnLordOversaturated(npcLords[_rng.Next(npcLords.Count)]);
            }
            catch { }

            // ~3% chance per school per week for a campaign magical event
            // Season biases which schools are active
            try { ApplyCampaignMagicEvents(); } catch { }
        }

        private void ApplyCampaignMagicEvents()
        {
            if (Campaign.Current == null || MobileParty.MainParty == null) return;

            CampaignTime.Seasons season = CampaignTime.Seasons.Spring;
            try { season = CampaignTime.Now.GetSeasonOfYear; } catch { }

            foreach (ColorSchool school in _allSchoolsOrdered)
            {
                // Season modifier: in-season schools roll 5%, off-season 3%, opposite season 1%
                int threshold;
                bool inSeason = IsSchoolInSeason(school, season);
                bool opposite = IsSchoolOpposite(school, season);
                if (inSeason) threshold = 95;     // 5% chance
                else if (opposite) threshold = 99; // 1% chance
                else threshold = 97;               // 3% chance

                if (_rng.Next(100) < threshold) continue;

                ApplyOneCampaignMagicEvent(school);
            }
        }

        private bool IsSchoolInSeason(ColorSchool school, CampaignTime.Seasons season)
        {
            switch (season)
            {
                case CampaignTime.Seasons.Spring: return school == ColorSchool.Green;
                case CampaignTime.Seasons.Summer: return school == ColorSchool.Red || school == ColorSchool.Yellow;
                case CampaignTime.Seasons.Autumn: return school == ColorSchool.Orange || school == ColorSchool.Red;
                case CampaignTime.Seasons.Winter: return school == ColorSchool.Blue || school == ColorSchool.Purple;
                default: return false;
            }
        }

        private bool IsSchoolOpposite(ColorSchool school, CampaignTime.Seasons season)
        {
            switch (season)
            {
                case CampaignTime.Seasons.Summer: return school == ColorSchool.Blue || school == ColorSchool.Purple;
                case CampaignTime.Seasons.Winter: return school == ColorSchool.Red || school == ColorSchool.Yellow;
                default: return false;
            }
        }

        private void ApplyOneCampaignMagicEvent(ColorSchool school)
        {
            Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
            IFaction playerFaction = Hero.MainHero?.MapFaction;

            switch (school)
            {
                case ColorSchool.Red:
                {
                    // A nearby party — any faction — loses soldiers to a surge of red
                    var target = MobileParty.All
                        .Where(p => p.IsActive && p.MemberRoster.TotalRegulars > 5
                                 && (p.GetPosition2D - playerPos).Length < 30f)
                        .OrderBy(_ => _rng.Next()).FirstOrDefault();
                    if (target != null)
                    {
                        var troops = target.MemberRoster.GetTroopRoster()
                            .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
                        if (troops.Count > 0)
                        {
                            var e = troops[_rng.Next(troops.Count)];
                            try { target.MemberRoster.AddToCounts(e.Character, 0, false, 1 + _rng.Next(3)); } catch { }
                            MBInformationManager.AddQuickInformation(
                                new TextObject($"✦ Crimson Sky: A surge of red tears through {target.Name}. Soldiers fall wounded. ✦"),
                                0, null, null, "");
                        }
                    }
                    break;
                }
                case ColorSchool.Orange:
                {
                    // A random nearby party — any faction — receives a morale surge
                    var target = MobileParty.All
                        .Where(p => p.IsActive && (p.GetPosition2D - playerPos).Length < 30f)
                        .OrderBy(_ => _rng.Next()).FirstOrDefault() ?? MobileParty.MainParty;
                    try { target.RecentEventsMorale += 8f; } catch { }
                    MBInformationManager.AddQuickInformation(
                        new TextObject($"✦ Gilded Hour: Warmth sweeps through {target.Name} without warning. Morale +8. ✦"),
                        0, null, null, "");
                    break;
                }
                case ColorSchool.Yellow:
                {
                    // Random nearby settlement (any) loses loyalty
                    var target = Settlement.All
                        .Where(s => s.IsTown && s.Town != null
                                 && (s.GetPosition2D - playerPos).Length < 40f)
                        .OrderBy(_ => _rng.Next()).FirstOrDefault();
                    if (target?.Town != null)
                    {
                        float before = target.Town.Loyalty;
                        try { target.Town.Loyalty = Math.Max(0f, before - 8f); } catch { }
                        MBInformationManager.AddQuickInformation(
                            new TextObject($"✦ Sickly Haze: Dread drifts through {target.Name}. Loyalty falls. ✦"),
                            0, null, null, "");
                    }
                    break;
                }
                case ColorSchool.Green:
                {
                    // Random nearby village — any faction — gets a hearth boost
                    var target = Settlement.All
                        .Where(s => s.IsVillage && s.Village != null
                                 && (s.GetPosition2D - playerPos).Length < 40f)
                        .OrderBy(_ => _rng.Next()).FirstOrDefault();
                    if (target?.Village != null)
                    {
                        target.Village.Hearth += 10f;
                        MBInformationManager.AddQuickInformation(
                            new TextObject($"✦ Living Surge: The green breathes into {target.Name}. Hearth grows. ✦"),
                            0, null, null, "");
                    }
                    break;
                }
                case ColorSchool.Blue:
                {
                    // A random nearby lord — any kingdom — gains influence
                    var target = Hero.AllAliveHeroes
                        .Where(h => h.IsLord && h.IsAlive && h.Clan?.Kingdom != null
                                 && h.PartyBelongedTo != null
                                 && (h.PartyBelongedTo.GetPosition2D - playerPos).Length < 30f)
                        .OrderBy(_ => _rng.Next()).FirstOrDefault() ?? Hero.MainHero;
                    if (target?.Clan?.Kingdom != null)
                    {
                        try { GainKingdomInfluenceAction.ApplyForDefault(target, 2); } catch { }
                        MBInformationManager.AddQuickInformation(
                            new TextObject($"✦ Scholar's Veil: An insight arrives for {target.Name}. Influence +2. ✦"),
                            0, null, null, "");
                    }
                    break;
                }
                case ColorSchool.Purple:
                {
                    // A random nearby lord — any faction — ages 3 days
                    var target = Hero.AllAliveHeroes
                        .Where(h => h.IsLord && h.IsAlive && h.Clan != null
                                 && h.PartyBelongedTo != null
                                 && (h.PartyBelongedTo.GetPosition2D - playerPos).Length < 30f)
                        .OrderBy(_ => _rng.Next()).FirstOrDefault();
                    if (target == null)
                        target = Hero.AllAliveHeroes
                            .Where(h => h.IsLord && h.IsAlive && h.Clan != null)
                            .OrderBy(_ => _rng.Next()).FirstOrDefault();
                    if (target != null)
                    {
                        try { target.SetBirthDay(target.BirthDay - CampaignTime.Days(3)); } catch { }
                        MBInformationManager.AddQuickInformation(
                            new TextObject($"✦ Grey Shroud: Three days quietly taken from {target.Name}. ✦"),
                            0, null, null, "");
                    }
                    break;
                }
            }
        }

        // ── Mission ended ────────────────────────────────────────────────────
        private void OnMissionEnded(IMission mission)
        {
            try { ActiveEffectManager.ClearMissionEffects(); } catch { }
            try { ColourLordAI.ClearCooldowns(); } catch { }
            try { SpellEffects.ClearAreaEffects(); } catch { }
            try { SpellEffects.ClearSelfEffects(); } catch { }
            try { SpellEffects.ClearGlows(); } catch { }
            try { SpellEffects.ClearMoves(); } catch { }
            try { SpellEffects.RestoreColourNamePrefixes(); } catch { }
            try { ColourUnitRegistry.OnMissionEnded(); } catch { }

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
            try { ApplyBattleOversaturation(mapEvent); } catch { }
        }

        private void ApplyBattleOversaturation(MapEvent mapEvent)
        {
            bool playerInvolved = false;
            try
            {
                playerInvolved = mapEvent.AttackerSide.Parties.Any(p => p.Party == PartyBase.MainParty)
                              || mapEvent.DefenderSide.Parties.Any(p => p.Party == PartyBase.MainParty);
            }
            catch { }

            foreach (MapEventSide side in new[] { mapEvent.AttackerSide, mapEvent.DefenderSide })
            {
                if (side == null) continue;
                try
                {
                    foreach (var meparty in side.Parties)
                    {
                        try
                        {
                            Hero leader = meparty?.Party?.LeaderHero;
                            if (leader == null
                                || leader == Hero.MainHero
                                || !ColourLordRegistry.IsColourLord(leader)
                                || BlightSystem.IsBlight(leader)
                                || ColourLordRegistry.IsPrismLord(leader)) continue;

                            if (_rng.Next(100) >= 3) continue;

                            // Casting strain: hero's body pays the cost of channelling in battle
                            try
                            {
                                leader.HitPoints = Math.Max(1, leader.HitPoints / 4);
                                if (playerInvolved)
                                    InformationManager.DisplayMessage(new InformationMessage(
                                        $"{leader.Name} is strained by the colour — the casting takes its toll.",
                                        Color.FromUint(0xFFCCAA22)));
                            }
                            catch { }
                        }
                        catch { }
                    }
                }
                catch { }
            }
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
                try { ColourLordRegistry.OnLordDied(victim); } catch { }

            if (BlightSystem.IsBlight(victim))
            {
                ColorSchool school = BlightSystem.GetBlightSchool(victim);
                try { BlightSystem.OnBlightKilled(victim); } catch { }
                // NPC lord kills blight: 7% chance to absorb its colour
                if (killer != null && killer != Hero.MainHero)
                    try { BlightSystem.OnNpcKilledBlight(killer, school); } catch { }
                // Player kill is handled via mission-level flag → OnMissionEnded
            }
        }

        // ── Children inherit colours ─────────────────────────────────────────
        private void OnHeroCreated(Hero hero, bool bornNaturally)
        {
            if (!bornNaturally) return;
            try
            {
                bool parentIsPlayer = hero.Mother == Hero.MainHero || hero.Father == Hero.MainHero;

                if (parentIsPlayer)
                {
                    if (!ColourKnowledge.HasAnySchool) return;
                    Hero otherParent = hero.Mother == Hero.MainHero ? hero.Father : hero.Mother;
                    bool otherHasColours = otherParent != null && ColourLordRegistry.IsColourLord(otherParent);
                    bool playerHasMultiple = ColourKnowledge.AllSchools.Count() >= 2;
                    bool guaranteed = otherHasColours || playerHasMultiple;
                    if (!guaranteed && MBRandom.RandomInt(100) >= 50) return;

                    var parentSchools = ColourKnowledge.AllSchools.ToList();
                    int parentCount   = parentSchools.Count;
                    int delta         = MBRandom.RandomInt(3) - 1;
                    int childCount    = Math.Max(1, Math.Min(6, parentCount + delta));

                    var childSchools = new List<ColorSchool>();
                    childSchools.Add(parentSchools[MBRandom.RandomInt(parentSchools.Count)]);
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

                    string schoolNames = string.Join(", ", childSchools.Select(s => ColorSchoolData.Info[s].Name));
                    MBInformationManager.AddQuickInformation(
                        new TextObject($"{hero.Name} was born carrying {childCount} colour{(childCount > 1 ? "s" : "")}: {schoolNames}."),
                        0, hero.CharacterObject, null, "");
                }
                else
                {
                    // NPC child — inherit if both parents are colour lords (guaranteed) or one is (50%)
                    bool motherIsLord = hero.Mother != null && ColourLordRegistry.IsColourLord(hero.Mother);
                    bool fatherIsLord = hero.Father != null && ColourLordRegistry.IsColourLord(hero.Father);
                    if (!motherIsLord && !fatherIsLord) return;
                    bool bothAreLords = motherIsLord && fatherIsLord;
                    if (!bothAreLords && MBRandom.RandomInt(100) >= 50) return;

                    var parentSchools = new List<ColorSchool>();
                    if (motherIsLord) foreach (var s in ColourLordRegistry.GetColors(hero.Mother)) if (!parentSchools.Contains(s)) parentSchools.Add(s);
                    if (fatherIsLord) foreach (var s in ColourLordRegistry.GetColors(hero.Father)) if (!parentSchools.Contains(s)) parentSchools.Add(s);
                    if (parentSchools.Count == 0) return;

                    int childCount = Math.Min(bothAreLords ? 1 + MBRandom.RandomInt(2) : 1, parentSchools.Count);
                    var childSchools = parentSchools.OrderBy(_ => _rng.Next()).Take(childCount).ToList();
                    ColourLordRegistry.GrantChildColours(hero, childSchools);
                }
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
                try { SaturationSystem.RecalcMax(); } catch { }
        }

        // ── Companions may have colours ──────────────────────────────────────
        private void OnCompanionAdded(Hero companion)
        {
            try { ColourLordRegistry.TryGrantCompanionColours(companion); } catch { }
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
