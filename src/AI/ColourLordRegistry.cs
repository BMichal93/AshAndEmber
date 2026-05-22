// =============================================================================
// COLOURS OF CALRADIA — AI/ColourLordRegistry.cs
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
    // 9. COLOUR LORD REGISTRY
    //    Lords have colour schools. Distribution per faction:
    //      ~10% of lords → 1 random colour
    //       ~7%          → 2 colours
    //       ~5%          → 3 colours
    //        1 lord      → 4 colours (faction archmage)
    // =========================================================================
    public static class ColourLordRegistry
    {
        private static bool _seeded;
        private static readonly Random _rng = new Random();

        // lord StringId → list of colour schools
        private static readonly Dictionary<string, List<ColorSchool>> _lordColors
            = new Dictionary<string, List<ColorSchool>>();

        // Respawn timers: factionId → hours remaining
        private static readonly Dictionary<string, int> _respawnHours
            = new Dictionary<string, int>();

        // Campaign map cast cooldowns: lord StringId → days until next cast
        private static readonly Dictionary<string, int> _campaignCooldowns
            = new Dictionary<string, int>();

        // XP reflection for Inspired Word (resolved once, reused across all lord casts)
        private static MethodInfo _lordXpMethod;
        private static bool       _lordXpResolved;

        // Pending mage announcements — flushed on first daily tick so they're visible to the player
        private static readonly List<(string message, Color color)> _pendingAnnouncements
            = new List<(string, Color)>();

        // Prism lord — carries all 6 colours, personality shifts weekly, casts very often
        private static string _prismLordId = null;
        private static int    _prismRespawnHours = 0;

        // Deferred Prism offer (shown on next daily tick, not during a mission or event)
        private static Action _deferredPrismInquiry = null;

        // Deferred oversaturation kills — KillCharacterAction must not fire inside a weekly tick
        private static readonly List<(string heroId, ColorSchool blightSchool)> _deferredKills
            = new List<(string, ColorSchool)>();

        // Desired trait level per colour school — applied when colours are assigned
        // Key: trait to set; Value: target level (positive or negative)
        private static readonly Dictionary<ColorSchool, List<(TraitObject Trait, int Level)>> _colourTraits =
            new Dictionary<ColorSchool, List<(TraitObject, int)>>
        {
            [ColorSchool.Red]    = new List<(TraitObject, int)> { (DefaultTraits.Calculating, -1), (DefaultTraits.Valor, +1) },
            [ColorSchool.Orange] = new List<(TraitObject, int)> { (DefaultTraits.Generosity, +1) },
            [ColorSchool.Yellow] = new List<(TraitObject, int)> { (DefaultTraits.Mercy, -1), (DefaultTraits.Honor, -1) },
            [ColorSchool.Green]  = new List<(TraitObject, int)> { (DefaultTraits.Mercy, +1), (DefaultTraits.Honor, +1) },
            [ColorSchool.Blue]   = new List<(TraitObject, int)> { (DefaultTraits.Calculating, +1) },
            [ColorSchool.Purple] = new List<(TraitObject, int)> { (DefaultTraits.Valor, -1) },
        };

        // ── Public access ─────────────────────────────────────────────────────
        public static bool IsColourLord(Hero hero) =>
            hero != null && _lordColors.ContainsKey(hero.StringId);

        public static IEnumerable<Hero> GetAllColourLords() =>
            _lordColors.Keys
                .Select(id => Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id))
                .Where(h => h != null);

        public static bool IsPrismLord(Hero hero) =>
            hero != null && _prismLordId != null && hero.StringId == _prismLordId;

        public static Hero GetPrismLord() =>
            _prismLordId == null ? null : Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _prismLordId);

        public static IReadOnlyList<ColorSchool> GetColors(Hero hero)
        {
            if (hero == null || !_lordColors.TryGetValue(hero.StringId, out var list))
                return Array.Empty<ColorSchool>();
            return list;
        }

        public static bool HasColor(Hero hero, ColorSchool school) =>
            _lordColors.TryGetValue(hero?.StringId ?? "", out var list) && list.Contains(school);

        // ── Seeding ───────────────────────────────────────────────────────────
        public static void SeedInitialLords()
        {
            if (_seeded) return;
            _seeded = true;
            try
            {
                foreach (Kingdom kingdom in Campaign.Current.Kingdoms)
                    SeedFaction(kingdom);
                SeedPrismLord();
                ApplyAllRelationships();
            }
            catch { }
        }

        private static void SeedPrismLord()
        {
            if (_prismLordId != null) return;
            try
            {
                var candidates = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h != Hero.MainHero && h.IsAlive
                             && h.MapFaction is Kingdom && !IsPrismLord(h))
                    .ToList();
                if (candidates.Count == 0) return;

                Hero prism = candidates[_rng.Next(candidates.Count)];
                _prismLordId = prism.StringId;
                _lordColors[prism.StringId] = new List<ColorSchool>
                {
                    ColorSchool.Red, ColorSchool.Orange, ColorSchool.Yellow,
                    ColorSchool.Green, ColorSchool.Blue, ColorSchool.Purple
                };
                ApplyColourTraits(prism, _lordColors[prism.StringId]);
                _pendingAnnouncements.Add((
                    $"The Prism walks — {prism.Name} of {prism.MapFaction?.Name} carries all six colours. Their nature shifts without warning.",
                    new Color(0.9f, 0.7f, 1.0f)));
            }
            catch { }
        }

        private static readonly string[] _archmageFlavour =
        {
            "Four colours burn in {0} of {1}. Do not face them lightly.",
            "They say {0} of {1} bleeds in four colours. The field bends around them.",
            "{0} of {1} carries four schools. Soldiers speak of them in hushed tones.",
            "Four marks — four colours scar {0} of {1}. The most dangerous kind.",
            "{0} of {1} wears four colours like armour. None who faced them alone lived to boast.",
        };
        private static readonly string[] _multiFlavour =
        {
            "{0} of {1} carries {2}.",
            "The colours {2} have taken root in {0} of {1}.",
            "{0} of {1} is touched by {2} — a rare combination.",
            "Scouts report {0} of {1} wielding {2}.",
            "Two schools stir in {0} of {1}: {2}.",
        };
        private static readonly string[] _singleFlavour =
        {
            "{0} of {1} walks with the {2}.",
            "The {2} has chosen {0} of {1}.",
            "{0} of {1} — marked by {2}.",
            "A single colour, but sharp: {0} of {1} carries {2}.",
            "The {2} stirs in {0} of {1}.",
        };

        private static string FormatAnnouncement(Hero lord, IReadOnlyList<ColorSchool> colors)
        {
            string name    = lord.Name.ToString();
            string faction = lord.MapFaction?.Name?.ToString() ?? "the world";
            string colourList = string.Join(", ", colors.Select(c => ColorSchoolData.Info[c].Name));

            if (colors.Count >= 4)
            {
                string tmpl = _archmageFlavour[_rng.Next(_archmageFlavour.Length)];
                return string.Format(tmpl, name, faction);
            }
            else if (colors.Count >= 2)
            {
                string tmpl = _multiFlavour[_rng.Next(_multiFlavour.Length)];
                return string.Format(tmpl, name, faction, colourList);
            }
            else
            {
                string colour = ColorSchoolData.Info[colors[0]].Name;
                string tmpl   = _singleFlavour[_rng.Next(_singleFlavour.Length)];
                return string.Format(tmpl, name, faction, colour);
            }
        }

        public static void FlushAnnouncements()
        {
            if (_pendingAnnouncements.Count == 0) return;
            InformationManager.DisplayMessage(new InformationMessage(
                $"The colours have awoken. {_pendingAnnouncements.Count} mages walk among the lords of Calradia.",
                new Color(0.85f, 0.65f, 1.0f)));
            foreach (var (msg, col) in _pendingAnnouncements)
                InformationManager.DisplayMessage(new InformationMessage(msg, col));
            _pendingAnnouncements.Clear();
        }

        private static void SeedFaction(IFaction faction)
        {
            var lords = Hero.AllAliveHeroes
                .Where(h => h.MapFaction == faction && h.IsLord && !IsColourLord(h))
                .ToList();
            if (lords.Count == 0) return;

            // Shuffle
            for (int i = lords.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var tmp = lords[i]; lords[i] = lords[j]; lords[j] = tmp;
            }

            // Assign one archmage (4 colours)
            Hero archmage = lords[0];
            _lordColors[archmage.StringId] = PickColors(4);
            try { ApplyColourTraits(archmage, _lordColors[archmage.StringId]); } catch { }
            _pendingAnnouncements.Add((
                FormatAnnouncement(archmage, _lordColors[archmage.StringId]),
                new Color(0.9f, 0.7f, 0.9f)));

            for (int i = 1; i < lords.Count; i++)
            {
                int roll = _rng.Next(100);
                int colorCount;
                if      (roll <  5) colorCount = 3;
                else if (roll < 12) colorCount = 2;
                else if (roll < 22) colorCount = 1;
                else                continue;

                _lordColors[lords[i].StringId] = PickColors(colorCount);
                try { ApplyColourTraits(lords[i], _lordColors[lords[i].StringId]); } catch { }
                _pendingAnnouncements.Add((
                    FormatAnnouncement(lords[i], _lordColors[lords[i].StringId]),
                    new Color(0.7f, 0.4f, 0.8f)));
            }
        }

        private static List<ColorSchool> PickColors(int count)
        {
            var all = new List<ColorSchool>(
                (ColorSchool[])Enum.GetValues(typeof(ColorSchool)));
            var result = new List<ColorSchool>();
            count = Math.Min(count, all.Count);
            for (int i = 0; i < count; i++)
            {
                int idx = _rng.Next(all.Count);
                result.Add(all[idx]);
                all.RemoveAt(idx);
            }
            return result;
        }

        // Applies personality traits to a hero based on their colour schools.
        // Existing traits in the same direction are kept or made more extreme.
        // Conflicting traits (opposite sign) are resolved by coin flip.
        public static void ApplyColourTraits(Hero hero, IReadOnlyList<ColorSchool> colors)
        {
            if (hero == null || colors == null || colors.Count == 0) return;

            // Merge desired trait levels across all assigned colours
            var desired = new Dictionary<TraitObject, int>();
            foreach (ColorSchool school in colors)
            {
                if (!_colourTraits.TryGetValue(school, out var entries)) continue;
                foreach (var (trait, level) in entries)
                {
                    if (!desired.ContainsKey(trait))
                    {
                        desired[trait] = level;
                    }
                    else
                    {
                        int existing = desired[trait];
                        if (Math.Sign(existing) == Math.Sign(level))
                            // Same direction: keep the more extreme value
                            desired[trait] = Math.Abs(level) > Math.Abs(existing) ? level : existing;
                        else
                            // Colours disagree on this trait: pick randomly
                            desired[trait] = _rng.Next(2) == 0 ? existing : level;
                    }
                }
            }

            // Apply to hero, respecting existing trait values
            foreach (var kvp in desired)
            {
                try
                {
                    TraitObject trait  = kvp.Key;
                    int         target = kvp.Value;
                    int         current = hero.GetTraitLevel(trait);

                    int chosen;
                    if (current == 0 || Math.Sign(current) == Math.Sign(target))
                        // No conflict: use whichever is more extreme
                        chosen = Math.Abs(target) >= Math.Abs(current) ? target : current;
                    else
                        // Conflict (hero already leans the other way): pick randomly
                        chosen = _rng.Next(2) == 0 ? current : target;

                    // Clamp to valid trait range
                    chosen = Math.Max(-2, Math.Min(2, chosen));
                    hero.SetTraitLevel(trait, chosen);
                }
                catch { }
            }
        }

        // ── Blight colour grant ───────────────────────────────────────────────
        // Called when an NPC lord kills a blight and learns its colour school.
        public static void GrantBlightColour(Hero hero, ColorSchool school)
        {
            if (hero == null) return;
            if (!_lordColors.TryGetValue(hero.StringId, out var schools))
                _lordColors[hero.StringId] = schools = new List<ColorSchool>();
            if (schools.Contains(school)) return;
            schools.Add(school);
            try { ApplyColourTraits(hero, schools); } catch { }
            try { ApplyColorRelationships(hero, schools); } catch { }
            try { ApplyBlightPenalty(hero, school); } catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                $"{hero.Name} drinks in the Blight's colour — {ColorSchoolData.Info[school].Name} flares in their blood, but the world answers with a stain of crime and waning influence.",
                ColorSchoolData.GetMessageColor(school)));
        }

        private static void ApplyBlightPenalty(Hero hero, ColorSchool school)
        {
            if (hero == null) return;

            const float CrimePenalty = 30f;
            const float InfluencePenalty = 12f;

            try
            {
                IFaction crimeFaction = hero.Clan?.Kingdom as IFaction ?? hero.Clan as IFaction;
                if (crimeFaction != null)
                    ChangeCrimeRatingAction.Apply(crimeFaction, CrimePenalty, true);
            }
            catch { }

            try
            {
                if (hero.Clan?.Kingdom != null)
                    hero.AddInfluenceWithKingdom(-InfluencePenalty);
            }
            catch { }

            try
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{hero.Name}'s triumph leaves a dark sigil upon their clan. Crime swells; the court's favour slips like ash through open fingers.",
                    ColorSchoolData.GetMessageColor(school)));
            }
            catch { }
        }

        // ── Children ──────────────────────────────────────────────────────────
        public static void GrantChildColours(Hero hero, List<ColorSchool> schools)
        {
            if (hero == null || schools == null || schools.Count == 0) return;
            _lordColors[hero.StringId] = new List<ColorSchool>(schools);
            ApplyColourTraits(hero, schools);
            ApplyColorRelationships(hero, schools);
        }

        // ── Companions ────────────────────────────────────────────────────────
        public static void TryGrantCompanionColours(Hero companion)
        {
            if (companion == null || IsColourLord(companion)) return;
            int roll = _rng.Next(100);
            int count;
            if      (roll < 10) count = 2;
            else if (roll < 30) count = 1;
            else                return;

            _lordColors[companion.StringId] = PickColors(count);
            ApplyColourTraits(companion, _lordColors[companion.StringId]);
            ApplyColorRelationships(companion, _lordColors[companion.StringId]);
            string colorNames = string.Join(", ", _lordColors[companion.StringId]
                .Select(c => ColorSchoolData.Info[c].Name));
            InformationManager.DisplayMessage(new InformationMessage(
                $"{companion.Name} carries colour magic: {colorNames}.",
                new Color(0.7f, 0.7f, 0.5f)));
        }

        // ── Death / Respawn ───────────────────────────────────────────────────
        public static void OnLordDied(Hero hero)
        {
            bool wasPrism = IsPrismLord(hero);
            _lordColors.Remove(hero.StringId);
            _campaignCooldowns.Remove(hero.StringId);

            if (wasPrism)
            {
                _prismLordId = null;
                InformationManager.DisplayMessage(new InformationMessage(
                    $"The Prism has fallen — {hero.Name} is dead. The six colours scatter.",
                    new Color(0.9f, 0.7f, 1.0f)));

                bool playerHasAll    = ColourKnowledge.AllSchools.Count() >= 6;
                bool playerIsPrism   = SaturationSystem.IsPlayerPrism;
                if (playerHasAll && !playerIsPrism && _rng.Next(100) < 30)
                {
                    _deferredPrismInquiry = OfferPlayerPrism;
                }
                else
                {
                    _prismRespawnHours = 720; // 1 month
                    InformationManager.DisplayMessage(new InformationMessage(
                        "A new Prism will rise within a month.",
                        new Color(0.9f, 0.7f, 1.0f)));
                }
                return;
            }

            string factionId = (hero.MapFaction as Kingdom)?.StringId;
            if (factionId == null) return;

            _respawnHours[factionId] = 168; // 7 days
            InformationManager.DisplayMessage(new InformationMessage(
                $"The colours of {hero.Name} are extinguished. They will pass to another in one week.",
                Color.FromUint(0xFFAA6644)));
        }

        // ── Player Prism ──────────────────────────────────────────────────────
        public static void SetPlayerAsPrism()
        {
            Hero player = Hero.MainHero;
            if (player == null) return;
            _prismLordId = player.StringId;
            _lordColors[player.StringId] = new List<ColorSchool>
            {
                ColorSchool.Red, ColorSchool.Orange, ColorSchool.Yellow,
                ColorSchool.Green, ColorSchool.Blue, ColorSchool.Purple
            };
            SaturationSystem.SetPlayerPrism(true);
            try { ApplyAllRelationships(); } catch { }
        }

        private static void OfferPlayerPrism()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Prism Falls to You",
                "The Prism is dead. You carry all six colours. The mantle seeks a new vessel — and it has found you.\n\n" +
                "Accept, and you become the Prism: Madness and Oversaturation will no longer touch you. " +
                "The world fractures around you instead.",
                new List<InquiryElement>
                {
                    new InquiryElement("accept", "Accept the mantle", null, true,
                        "You become the Prism. Immune to Madness and Oversaturation."),
                    new InquiryElement("refuse", "Refuse", null, true,
                        "The mantle passes on. A new Prism will rise within a month."),
                },
                false, 1, 1,
                "Choose.",
                "",
                chosen =>
                {
                    string choice = chosen?.Count > 0 ? chosen[0].Identifier?.ToString() : "refuse";
                    if (choice == "accept")
                        SetPlayerAsPrism();
                    else
                    {
                        _prismRespawnHours = 720;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "You refuse the mantle. A new Prism will rise within a month.",
                            new Color(0.9f, 0.7f, 1.0f)));
                    }
                },
                null, "", false
            ), false, true);
        }

        public static void FlushDeferredPrismInquiry()
        {
            if (_deferredPrismInquiry == null) return;
            var action = _deferredPrismInquiry;
            _deferredPrismInquiry = null;
            action?.Invoke();
        }

        // ── NPC oversaturation ────────────────────────────────────────────────
        public static void OnLordOversaturated(Hero hero)
        {
            if (!_lordColors.TryGetValue(hero?.StringId ?? "", out var schools)) return;

            int roll = _rng.Next(100);
            if (roll < 80)
            {
                string factionId = (hero.MapFaction as Kingdom)?.StringId;
                _lordColors.Remove(hero.StringId);
                _campaignCooldowns.Remove(hero.StringId);
                if (factionId != null) _respawnHours[factionId] = 168;
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{hero.Name} is destroyed by Oversaturation — their colours scatter.",
                    new Color(0.6f, 0.4f, 0.9f)));
            }
            else
            {
                // Defer the kill — KillCharacterAction fires HeroKilled events which are unsafe
                // to trigger inside a weekly tick enumeration. Flushed on next daily tick.
                ColorSchool blightSchool = schools[_rng.Next(schools.Count)];
                _lordColors.Remove(hero.StringId);
                _campaignCooldowns.Remove(hero.StringId);
                _deferredKills.Add((hero.StringId, blightSchool));
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{hero.Name} is consumed by Oversaturation — they will not survive the night. " +
                    $"The {ColorSchoolData.Info[blightSchool].Name} Blight stirs.",
                    new Color(0.6f, 0.4f, 0.9f)));
            }
        }

        // Called from CampaignBehavior.OnDailyTick — safe context for KillCharacterAction
        public static void FlushDeferredKills()
        {
            if (_deferredKills.Count == 0) return;
            var toKill = new List<(string heroId, ColorSchool blightSchool)>(_deferredKills);
            _deferredKills.Clear();
            foreach (var (heroId, blightSchool) in toKill)
            {
                try
                {
                    Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId);
                    if (hero == null || !hero.IsAlive) continue;
                    KillCharacterAction.ApplyByMurder(hero, null, true);
                    BlightSystem.SpawnBlightFromOversaturation(blightSchool);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{hero.Name} has died from Oversaturation. " +
                        $"The {ColorSchoolData.Info[blightSchool].Name} Blight rises from their ruin.",
                        new Color(0.6f, 0.4f, 0.9f)));
                }
                catch { }
            }
        }

        public static void CheckRespawnTimers()
        {
            // Prism respawn
            if (_prismLordId == null && _prismRespawnHours > 0)
            {
                _prismRespawnHours--;
                if (_prismRespawnHours <= 0)
                {
                    _prismRespawnHours = 0;
                    try
                    {
                        var candidates = Hero.AllAliveHeroes
                            .Where(h => h.IsLord && h != Hero.MainHero && h.IsAlive
                                     && h.MapFaction is Kingdom)
                            .ToList();
                        if (candidates.Count > 0)
                        {
                            Hero prism = candidates[_rng.Next(candidates.Count)];
                            _prismLordId = prism.StringId;
                            _lordColors[prism.StringId] = new List<ColorSchool>
                            {
                                ColorSchool.Red, ColorSchool.Orange, ColorSchool.Yellow,
                                ColorSchool.Green, ColorSchool.Blue, ColorSchool.Purple
                            };
                            ApplyColourTraits(prism, _lordColors[prism.StringId]);
                            ApplyColorRelationships(prism, _lordColors[prism.StringId]);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"The Prism rises again — {prism.Name} of {prism.MapFaction?.Name} has been chosen by all six colours.",
                                new Color(0.9f, 0.7f, 1.0f)));
                        }
                        else
                            _prismRespawnHours = 24; // retry in 1 day
                    }
                    catch { }
                }
            }

            // Normal lord respawn (per faction)
            foreach (string factionId in _respawnHours.Keys.ToList())
            {
                _respawnHours[factionId]--;
                if (_respawnHours[factionId] > 0) continue;
                _respawnHours.Remove(factionId);

                // Seed one new colour lord in this faction
                Kingdom kingdom = Campaign.Current?.Kingdoms
                    .FirstOrDefault(k => k.StringId == factionId);
                if (kingdom == null) continue;

                var candidates = Hero.AllAliveHeroes
                    .Where(h => h.MapFaction == kingdom && h.IsLord && !IsColourLord(h) &&
                                h.Age < 50f)
                    .ToList();
                if (candidates.Count == 0)
                {
                    _respawnHours[factionId] = 24;
                    continue;
                }

                Hero chosen = candidates[_rng.Next(candidates.Count)];
                _lordColors[chosen.StringId] = PickColors(1 + _rng.Next(2));
                ApplyColourTraits(chosen, _lordColors[chosen.StringId]);
                ApplyColorRelationships(chosen, _lordColors[chosen.StringId]);
                InformationManager.DisplayMessage(new InformationMessage(
                    FormatAnnouncement(chosen, _lordColors[chosen.StringId]),
                    new Color(0.7f, 0.5f, 0.8f)));
            }
        }

        // ── Colour relationship system ────────────────────────────────────────
        // Called once after all lords are seeded to apply initial relationships.
        private static void ApplyAllRelationships()
        {
            try
            {
                var lords = _lordColors.Keys
                    .Select(id => Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id))
                    .Where(h => h != null)
                    .ToList();

                for (int i = 0; i < lords.Count; i++)
                {
                    for (int j = i + 1; j < lords.Count; j++)
                    {
                        ApplyRelationBetween(lords[i], _lordColors[lords[i].StringId],
                                             lords[j], _lordColors[lords[j].StringId]);
                    }
                }
            }
            catch { }
        }

        // Applied when a new lord gets colours later (respawn, companion, child).
        private static void ApplyColorRelationships(Hero hero, IReadOnlyList<ColorSchool> colors)
        {
            try
            {
                foreach (var kvp in _lordColors)
                {
                    if (kvp.Key == hero.StringId) continue;
                    Hero other = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == kvp.Key);
                    if (other == null) continue;
                    ApplyRelationBetween(hero, colors, other, kvp.Value);
                }
            }
            catch { }
        }

        private static void ApplyRelationBetween(
            Hero a, IReadOnlyList<ColorSchool> aColors,
            Hero b, IReadOnlyList<ColorSchool> bColors)
        {
            // 5+ colours → penalty with all colour lords (regardless of shared colour)
            if (aColors.Count >= 5 || bColors.Count >= 5)
            {
                try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(a, b, -5, false); } catch { }
                return;
            }
            // 1–2 colours on each side + at least one shared colour → bonus
            if (aColors.Count <= 2 && bColors.Count <= 2)
            {
                bool shared = aColors.Any(s => bColors.Contains(s));
                if (shared)
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(a, b, 5, false); } catch { }
            }
        }

        // ── Campaign map effects (lord-only) ──────────────────────────────────
        private const int MaxLordMapCastsPerDay = 1;

        public static void DailyMapCast()
        {
            int castsToday = 0;
            foreach (var kvp in _lordColors.ToList())
            {
                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == kvp.Key);
                if (hero == null || !hero.IsAlive) continue;

                // Battle morale floor — maintained daily so it's in place before any battle resolves.
                // Sets a minimum, not an addition, so it cannot accumulate unboundedly.
                // 1 colour → 10 floor, 6 colours → 60 floor (hard cap 60).
                if (hero.PartyBelongedTo != null)
                {
                    float floor = Math.Min(60f, kvp.Value.Count * 10f);
                    try
                    {
                        if (hero.PartyBelongedTo.RecentEventsMorale < floor)
                            hero.PartyBelongedTo.RecentEventsMorale = floor;
                    }
                    catch { }
                }

                if (_campaignCooldowns.TryGetValue(kvp.Key, out int cd) && cd > 0)
                { _campaignCooldowns[kvp.Key] = cd - 1; continue; }

                if (castsToday >= MaxLordMapCastsPerDay) continue;

                if (_rng.Next(100) >= 5) continue; // 5% chance per day

                _campaignCooldowns[kvp.Key] = 12 + _rng.Next(5);
                ColorSchool school = kvp.Value[_rng.Next(kvp.Value.Count)];
                CastLordMapSpell(hero, school);
                castsToday++;
            }
        }

        // Pick a random element from a filtered list without sorting the whole collection.
        private static T PickRandom<T>(IEnumerable<T> source) where T : class
        {
            var list = source.ToList();
            return list.Count > 0 ? list[_rng.Next(list.Count)] : null;
        }

        private static void CastLordMapSpell(Hero lord, ColorSchool school)
        {
            string msg = null;
            string spellName = null;

            try
            {
                switch (school)
                {
                    case ColorSchool.Red:
                    {
                        int variant = _rng.Next(3);
                        if (variant == 0)
                        {
                            // Mirror player Invoke Red: wound soldiers in a nearby enemy party
                            spellName = "Withering Strike";
                            var target = PickRandom(MobileParty.All.Where(
                                p => p != lord.PartyBelongedTo && p.IsActive && p.MapFaction != null
                                     && p.MapFaction != lord.MapFaction && lord.MapFaction != null
                                     && lord.MapFaction.IsAtWarWith(p.MapFaction)
                                     && p.MemberRoster.TotalRegulars > p.MemberRoster.TotalWounded));
                            if (target != null)
                            {
                                var troops = target.MemberRoster.GetTroopRoster()
                                    .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
                                if (troops.Count > 0)
                                {
                                    int wounds = 1 + _rng.Next(3);
                                    for (int i = 0; i < wounds; i++)
                                    {
                                        var e = troops[_rng.Next(troops.Count)];
                                        try { target.MemberRoster.AddToCounts(e.Character, 0, false, 1); } catch { }
                                    }
                                    msg = $"{wounds} soldier{(wounds > 1 ? "s" : "")} in {target.Name} fall wounded.";
                                }
                            }
                        }
                        else if (variant == 1)
                        {
                            // Mirror player Affect Red: plunder a village
                            spellName = "Pillager's Brand";
                            var target = PickRandom(Settlement.All.Where(
                                s => s.IsVillage && s.Village != null && s.MapFaction != lord.MapFaction));
                            if (target?.Village != null)
                            {
                                target.Village.Hearth = Math.Max(10f, target.Village.Hearth * 0.8f);
                                msg = $"Fire and ruin — {target.Name} diminishes under {lord.Name}'s rage.";
                            }
                        }
                        else
                        {
                            // Commune Red (Crimson Tithe): sacrifice own soldiers for morale
                            spellName = "Crimson Tithe";
                            if (lord.PartyBelongedTo != null)
                            {
                                var troops = lord.PartyBelongedTo.MemberRoster.GetTroopRoster()
                                    .Where(e => !e.Character.IsHero && e.Number > 2).ToList();
                                if (troops.Count > 0)
                                {
                                    var e = troops[_rng.Next(troops.Count)];
                                    try { lord.PartyBelongedTo.MemberRoster.AddToCounts(e.Character, -1); } catch { }
                                    try { lord.PartyBelongedTo.RecentEventsMorale += 15f; } catch { }
                                    msg = $"{lord.Name} burns one of their own soldiers into the red — their host finds new fury. Morale surges.";
                                }
                            }
                        }
                        break;
                    }

                    case ColorSchool.Orange:
                    {
                        int variant = _rng.Next(3);
                        if (variant == 0)
                        {
                            // Mirror player Affect Orange: morale boost
                            spellName = "Rallying Call";
                            if (lord.PartyBelongedTo != null)
                            { lord.PartyBelongedTo.RecentEventsMorale += 10f; msg = $"Warmth moves through {lord.Name}'s ranks."; }
                        }
                        else if (variant == 1)
                        {
                            // Mirror player Invoke Orange: XP to a random own soldier
                            spellName = "Inspired Word";
                            if (lord.PartyBelongedTo != null)
                            {
                                var troops = lord.PartyBelongedTo.MemberRoster.GetTroopRoster()
                                    .Where(e => !e.Character.IsHero && e.Number > 0).ToList();
                                if (troops.Count > 0)
                                {
                                    var element = troops[_rng.Next(troops.Count)];
                                    if (!_lordXpResolved)
                                    {
                                        _lordXpResolved = true;
                                        _lordXpMethod = typeof(TroopRoster).GetMethod("AddXpToTroop",
                                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    }
                                    if (_lordXpMethod != null)
                                        try { _lordXpMethod.Invoke(lord.PartyBelongedTo.MemberRoster, new object[] { 200, element.Character }); } catch { }
                                    msg = $"Inspiration stirs in {element.Character.Name} under {lord.Name}'s command.";
                                }
                            }
                        }
                        else
                        {
                            // Commune Orange (Good Word): improve relations with a random nearby lord
                            spellName = "Good Word";
                            var target = PickRandom(Hero.AllAliveHeroes.Where(
                                h => h.IsLord && h != lord && h.IsAlive && h.Clan != null
                                     && h.MapFaction == lord.MapFaction));
                            if (target != null)
                            {
                                try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(lord, target, 3, false); } catch { }
                                msg = $"A word of warmth from {lord.Name} reaches {target.Name}. Relations improve.";
                            }
                        }
                        break;
                    }

                    case ColorSchool.Yellow:
                    {
                        int variant = _rng.Next(3);
                        if (variant == 0)
                        {
                            // Mirror player Affect Yellow: conscript a prisoner from an enemy party
                            spellName = "Press Gang";
                            var target = PickRandom(MobileParty.All.Where(
                                p => p != lord.PartyBelongedTo && p.IsActive
                                     && p.MapFaction != null && p.MapFaction != lord.MapFaction
                                     && p.PrisonRoster.TotalRegulars > 0));
                            if (target != null && lord.PartyBelongedTo != null)
                            {
                                var prisoners = target.PrisonRoster.GetTroopRoster()
                                    .Where(e => !e.Character.IsHero && e.Number > 0).ToList();
                                if (prisoners.Count > 0)
                                {
                                    var element = prisoners[_rng.Next(prisoners.Count)];
                                    int count = Math.Min(1 + _rng.Next(2), element.Number);
                                    try
                                    {
                                        target.PrisonRoster.AddToCounts(element.Character, -count);
                                        lord.PartyBelongedTo.MemberRoster.AddToCounts(element.Character, count);
                                    }
                                    catch { }
                                    msg = $"{count} {element.Character.Name}{(count > 1 ? "s" : "")} are coerced from {target.Name} into {lord.Name}'s service.";
                                }
                            }
                        }
                        else if (variant == 1)
                        {
                            // Mirror player Invoke Yellow: enemy party loses morale
                            spellName = "Creeping Fear";
                            var target = PickRandom(MobileParty.All.Where(
                                p => p != lord.PartyBelongedTo && p.IsActive && p.MapFaction != null
                                     && p.MapFaction != lord.MapFaction && lord.MapFaction != null
                                     && lord.MapFaction.IsAtWarWith(p.MapFaction)));
                            if (target != null)
                            {
                                try { target.RecentEventsMorale -= 15f; } catch { }
                                msg = $"Fear settles over {target.Name}'s ranks — {lord.Name} breathes terror into the world.";
                            }
                        }
                        else
                        {
                            // Commune Yellow (Sow Doubt): enemy town loses loyalty
                            spellName = "Sow Doubt";
                            var target = PickRandom(Settlement.All.Where(
                                s => s.IsTown && s.Town != null && s.MapFaction != null
                                     && s.MapFaction != lord.MapFaction && lord.MapFaction != null
                                     && lord.MapFaction.IsAtWarWith(s.MapFaction)));
                            if (target?.Town != null)
                            {
                                try { target.Town.Loyalty = Math.Max(0f, target.Town.Loyalty - 10f); } catch { }
                                msg = $"Dread seeps through {target.Name} — {lord.Name}'s shadow unnerves the populace.";
                            }
                        }
                        break;
                    }

                    case ColorSchool.Green:
                    {
                        int variant = _rng.Next(3);
                        if (variant == 0)
                        {
                            // Mirror player Affect Green: heal wounded in own party
                            spellName = "Mending Touch";
                            if (lord.PartyBelongedTo != null)
                            {
                                int healed = 0;
                                foreach (var e in lord.PartyBelongedTo.MemberRoster.GetTroopRoster().ToList())
                                {
                                    int h = Math.Min(e.WoundedNumber, 3);
                                    if (h > 0) { lord.PartyBelongedTo.MemberRoster.AddToCounts(e.Character, 0, false, -h); healed += h; }
                                }
                                if (healed > 0) msg = $"{lord.Name} tends the wounds. {healed} soldiers recover.";
                            }
                        }
                        else if (variant == 1)
                        {
                            // Mirror player Invoke Green: add grain to own party
                            spellName = "Green's Bounty";
                            if (lord.PartyBelongedTo != null)
                            {
                                ItemObject grain = MBObjectManager.Instance.GetObject<ItemObject>("grain");
                                if (grain != null) lord.PartyBelongedTo.ItemRoster.AddToCounts(grain, 5);
                                msg = $"Grain ripens in {lord.Name}'s stores.";
                            }
                        }
                        else
                        {
                            // Commune Green (Verdant Bond): bless a friendly village
                            spellName = "Verdant Bond";
                            var target = PickRandom(Settlement.All.Where(
                                s => s.IsVillage && s.Village != null && s.MapFaction == lord.MapFaction));
                            if (target?.Village != null)
                            {
                                target.Village.Hearth += 15f;
                                msg = $"The green breathes through {lord.Name} into {target.Name}. The village flourishes.";
                            }
                        }
                        break;
                    }

                    case ColorSchool.Blue:
                    {
                        int variant = _rng.Next(3);
                        if (variant == 0)
                        {
                            // Mirror player Invoke Blue: own clan gains influence
                            spellName = "Scholar's Word";
                            if (lord.Clan != null)
                            {
                                try { ChangeClanInfluenceAction.Apply(lord.Clan, 8f); } catch { }
                                msg = $"{lord.Name}'s insight earns favour — their clan's influence grows.";
                            }
                        }
                        else if (variant == 1)
                        {
                            // Negative mirror: enemy clan loses influence
                            spellName = "Scholar's Scheme";
                            var target = PickRandom(Hero.AllAliveHeroes.Where(
                                h => h.IsLord && h.MapFaction != lord.MapFaction && h.Clan != null && h.IsAlive));
                            if (target?.Clan != null)
                            {
                                try { ChangeClanInfluenceAction.Apply(target.Clan, -8f); } catch { }
                                msg = $"{lord.Name}'s cold insight undermines {target.Name}'s standing.";
                            }
                        }
                        else
                        {
                            // Commune Blue (Arcane Sight): undermine an enemy party's morale
                            spellName = "Arcane Sight";
                            var target = PickRandom(MobileParty.All.Where(
                                p => p != lord.PartyBelongedTo && p.IsActive && p.MapFaction != null
                                     && p.MapFaction != lord.MapFaction && lord.MapFaction != null
                                     && lord.MapFaction.IsAtWarWith(p.MapFaction)));
                            if (target != null)
                            {
                                try { target.RecentEventsMorale -= 10f; } catch { }
                                msg = $"{lord.Name}'s Scholar's eye finds {target.Name}'s weakness — their soldiers feel watched and uncertain.";
                            }
                        }
                        break;
                    }

                    case ColorSchool.Purple:
                    {
                        int variant = _rng.Next(3);
                        if (variant == 0)
                        {
                            // Mirror player Invoke Purple: enemy lord loses renown + morale
                            spellName = "Wither's Touch";
                            var target = PickRandom(Hero.AllAliveHeroes.Where(
                                h => h.IsLord && h.MapFaction != lord.MapFaction
                                     && h.Clan != null && h.IsAlive && h.PartyBelongedTo != null));
                            if (target != null)
                            {
                                try { ChangeClanInfluenceAction.Apply(target.Clan, -5f); } catch { }
                                try { target.PartyBelongedTo.RecentEventsMorale -= 10f; } catch { }
                                msg = $"A withering touches {target.Name} — their influence wanes and their soldiers are unsettled.";
                            }
                        }
                        else if (variant == 1)
                        {
                            // Mirror player Affect Purple: scatter nearby enemy parties
                            spellName = "Grey Veil";
                            if (lord.PartyBelongedTo != null)
                            {
                                Vec2 lordPos = lord.PartyBelongedTo.GetPosition2D;
                                int scattered = 0;
                                foreach (MobileParty p in MobileParty.All.ToList())
                                {
                                    if (p == lord.PartyBelongedTo || !p.IsActive) continue;
                                    if (p.MapFaction == null || p.MapFaction == lord.MapFaction) continue;
                                    if (lord.MapFaction != null && !lord.MapFaction.IsAtWarWith(p.MapFaction)) continue;
                                    if ((p.GetPosition2D - lordPos).Length > 15f) continue;
                                    Vec2 away = p.GetPosition2D - lordPos;
                                    if (away.Length < 0.01f) away = new Vec2(1f, 0f); else away = away.Normalized();
                                    Vec2 dest = p.GetPosition2D + away * 10f;
                                    try { p.SetMoveGoToPoint(new CampaignVec2(dest, true), MobileParty.NavigationType.Default); scattered++; } catch { }
                                }
                                if (scattered > 0)
                                    msg = $"{lord.Name} vanishes from their pursuers. {scattered} {(scattered == 1 ? "party" : "parties")} scatter.";
                            }
                        }
                        else
                        {
                            // Commune Purple (Grey Curse): age a random enemy lord 3 days, clan renown -2
                            spellName = "Grey Curse";
                            var target = PickRandom(Hero.AllAliveHeroes.Where(
                                h => h.IsLord && h.MapFaction != lord.MapFaction
                                     && h.Clan != null && h.IsAlive));
                            if (target != null)
                            {
                                try { target.SetBirthDay(target.BirthDay - CampaignTime.Days(3)); } catch { }
                                try { target.Clan.AddRenown(-2f); } catch { }
                                msg = $"The grey reaches from {lord.Name} into {target.Name}'s years. Three days taken, and their clan dims.";
                            }
                        }
                        break;
                    }
                }
            }
            catch { }

            if (msg == null || spellName == null) return;

            InformationManager.DisplayMessage(new InformationMessage(
                $"✦ {lord.Name} channels {spellName} ({ColorSchoolData.Info[school].Name}). {msg} ✦",
                ColorSchoolData.GetMessageColor(school)));
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            // Flatten List<ColorSchool> per lord into parallel count + flat-int lists —
            // IDataStore cannot serialize List<List<T>>.
            var lordIds        = _lordColors.Keys.ToList();
            var lordSchoolCnts = _lordColors.Values.Select(v => v.Count).ToList();
            var lordSchoolFlat = _lordColors.Values.SelectMany(v => v.Select(s => (int)s)).ToList();
            var rKeys          = _respawnHours.Keys.ToList();
            var rVals          = _respawnHours.Values.ToList();
            var ccKeys         = _campaignCooldowns.Keys.ToList();
            var ccVals         = _campaignCooldowns.Values.ToList();
            bool seeded        = _seeded;
            var prismIdList    = _prismLordId != null ? new List<string> { _prismLordId } : new List<string>();
            int prismRespawn   = _prismRespawnHours;
            var dkHeroIds      = _deferredKills.Select(x => x.heroId).ToList();
            var dkSchools      = _deferredKills.Select(x => (int)x.blightSchool).ToList();

            store.SyncData("COC_LordIds",        ref lordIds);
            store.SyncData("COC_LordSchoolCnts", ref lordSchoolCnts);
            store.SyncData("COC_LordSchoolFlat", ref lordSchoolFlat);
            store.SyncData("COC_RespawnKeys",    ref rKeys);
            store.SyncData("COC_RespawnVals",    ref rVals);
            store.SyncData("COC_CdKeys",         ref ccKeys);
            store.SyncData("COC_CdVals",         ref ccVals);
            store.SyncData("COC_LordSeeded",     ref seeded);
            store.SyncData("COC_PrismId",        ref prismIdList);
            store.SyncData("COC_PrismRespawn",   ref prismRespawn);
            store.SyncData("COC_DkHeroIds",      ref dkHeroIds);
            store.SyncData("COC_DkSchools",      ref dkSchools);

            _seeded = seeded;
            _prismLordId = prismIdList?.Count > 0 ? prismIdList[0] : null;
            _prismRespawnHours = prismRespawn;

            _deferredKills.Clear();
            if (dkHeroIds != null && dkSchools != null)
                for (int i = 0; i < Math.Min(dkHeroIds.Count, dkSchools.Count); i++)
                    _deferredKills.Add((dkHeroIds[i], (ColorSchool)dkSchools[i]));

            _lordColors.Clear();
            if (lordIds != null && lordSchoolCnts != null && lordSchoolFlat != null)
            {
                int si = 0;
                for (int i = 0; i < lordIds.Count; i++)
                {
                    int cnt = i < lordSchoolCnts.Count ? lordSchoolCnts[i] : 0;
                    var schools = new List<ColorSchool>();
                    for (int j = 0; j < cnt && si < lordSchoolFlat.Count; j++, si++)
                        schools.Add((ColorSchool)lordSchoolFlat[si]);
                    if (schools.Count > 0)
                        _lordColors[lordIds[i]] = schools;
                }
            }

            _respawnHours.Clear();
            if (rKeys != null && rVals != null)
                for (int i = 0; i < Math.Min(rKeys.Count, rVals.Count); i++)
                    _respawnHours[rKeys[i]] = rVals[i];

            _campaignCooldowns.Clear();
            if (ccKeys != null && ccVals != null)
                for (int i = 0; i < Math.Min(ccKeys.Count, ccVals.Count); i++)
                    _campaignCooldowns[ccKeys[i]] = ccVals[i];
        }
    }
}
