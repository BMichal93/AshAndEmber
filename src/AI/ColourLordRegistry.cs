// =============================================================================
// ASH AND EMBER — AI/ColourLordRegistry.cs
// Tracks which NPC lords carry the gift (isMage).
// Population target: ~20% of all lords. Weekly regulator keeps it stable.
// Mage lords cast the unified element magic — the same five elements the player
// wields (KnownElements), in battle (ColourLordAI) and on the campaign map
// (DailyMapCast → TalentSystem.ExecuteNpcElementMapSpell). Ashen lords know them
// all and wear the cold mask. Casting spends life expectancy; a lord burns out and
// dies once he reaches his (spend-reduced) death age. The legacy path-archetype
// talents (_lordTalents) are retained only for save compatibility and Dark Gift
// seeding — they no longer drive casting.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public static class ColourLordRegistry
    {
        private static readonly HashSet<string> _mageIds        = new HashSet<string>();
        private static readonly HashSet<string> _ashenIds        = new HashSet<string>();
        private static readonly HashSet<string> _companionMageIds = new HashSet<string>();
        // Spell/enchantment talents per lord, keyed by StringId. Assigned at seeding via AssignPathArchetype.
        private static readonly Dictionary<string, List<int>> _lordTalents
            = new Dictionary<string, List<int>>();
        private static bool _seeded = false;
        private static readonly Random _rng = new Random();
        private const float TargetFraction = 0.20f;
        private const float LowerBound     = 0.10f;
        private const float UpperBound     = 0.30f;
        // Map-cast cooldown per lord: heroId → days remaining
        private static readonly Dictionary<string, int> _campaignCooldowns
            = new Dictionary<string, int>();

        // Life expectancy spent to casting per lord: heroId → days. Mirrors the
        // player's ledger (AgingSystem) — casting no longer makes a lord OLDER, it
        // lowers the age at which the fire finally burns them out. The Ashen pay
        // nothing (the cold preserves what remains).
        private static readonly Dictionary<string, int> _lordDaysSpent
            = new Dictionary<string, int>();

        // Six NPC path archetypes — each mirrors one player fire path.
        // Spells  : campaign-map workings (TalentId values 4–8, filtered in DailyMapCast).
        // Enchants: battle passives activated by ColourLordAI.
        private static readonly (TalentId[] Spells, TalentId[] Enchants)[] _npcPathArchetypes =
        {
            // Reaper — attrition and life-drain
            (new[] { TalentId.Extinguish, TalentId.Plague },    new[] { TalentId.Smoulder, TalentId.Sunder }),
            // Seer — foresight and political influence
            (new[] { TalentId.Clairvoyance },                   new[] { TalentId.Ashveil, TalentId.Reflect }),
            // Warden — defence and endurance
            (new[] { TalentId.Inspire },                         new[] { TalentId.Ashveil, TalentId.CinderShell, TalentId.Reflect }),
            // Heartfire — warmth and rally
            (new[] { TalentId.Inspire, TalentId.Clairvoyance }, new[] { TalentId.Hearthlight, TalentId.CinderShell }),
            // Pyrelord — ruin and conquest
            (new[] { TalentId.Extinguish },                      new[] { TalentId.Scatter, TalentId.Immolate, TalentId.Sunder }),
            // Ashbinder — control and unmaking
            (new[] { TalentId.BreakWills, TalentId.Plague },    new[] { TalentId.Smoulder }),
        };

        // Used only by AssignCompanionEnchantments — companions draw from the full pool.
        private static readonly TalentId[] DamageEnchantments =
            { TalentId.Scatter, TalentId.Smoulder, TalentId.Sunder, TalentId.Immolate };

        private static readonly TalentId[] RestoreEnchantments =
            { TalentId.Ashveil, TalentId.CinderShell, TalentId.Hearthlight, TalentId.Reflect };

        // ── Public API ────────────────────────────────────────────────────────
        public static bool IsColourLord(Hero hero) =>
            hero != null && _mageIds.Contains(hero.StringId);

        public static bool IsAshenLord(Hero hero) =>
            hero != null && _ashenIds.Contains(hero.StringId);

        public static void SetMage(Hero hero, bool value)
        {
            if (hero == null) return;
            if (value)
            {
                // The Forest Clans (Battania) do not master fire — they live
                // alongside creatures of it instead (the Kindled). No Battanian
                // LORD ever carries the gift on his own account; the player's own
                // choice at character creation is untouched regardless of
                // culture, and the cold's Ashen corruption overrides this (it is
                // forced on a lord, not a culture mastering the flame — see
                // MarkClanAshen, which calls SetAshen before this).
                try
                {
                    if (hero != Hero.MainHero && hero.Culture?.StringId == "battania"
                        && !_ashenIds.Contains(hero.StringId))
                        return;
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _mageIds.Add(hero.StringId);
                if (!_lordTalents.ContainsKey(hero.StringId))
                    AssignPathArchetype(hero.StringId);
            }
            else
            {
                _mageIds.Remove(hero.StringId);
                _lordTalents.Remove(hero.StringId);
            }
        }

        public static void SetAshen(Hero hero, bool value)
        {
            if (hero == null) return;
            if (value)
            {
                _ashenIds.Add(hero.StringId);
                MageKnowledge.ApplyAshenAppearance(hero);
                // Move clan to the Ashen kingdom (or eject if kingdom isn't ready yet)
                try { AshenCitySystem.OnHeroSetAshen(hero); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // Ashen lords take the cold-fire destroyer archetype.
                // Overwrite any prior path assignment so their talent set is coherent.
                var ashenTalents = new List<int>
                {
                    (int)TalentId.Extinguish,
                    (int)TalentId.BreakWills,
                    (int)TalentId.Plague,
                    (int)TalentId.Scatter,   // cold fire flings enemies away
                };
                if (_rng.Next(2) == 0) ashenTalents.Add((int)TalentId.Smoulder); // 50% terror drain
                if (_rng.Next(2) == 0) ashenTalents.Add((int)TalentId.Sunder);   // 50% strips defences
                if (_rng.Next(10) < 4) ashenTalents.Add((int)TalentId.Immolate); // 40% the cold that takes
                _lordTalents[hero.StringId] = ashenTalents;
                // Seed dark gifts for this Ashen lord.
                try { DarkGiftSystem.SeedNpcGifts(hero, isAshenLord: true, isEvilLord: false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            else
            {
                _ashenIds.Remove(hero.StringId);
            }
        }

        public static bool HasTalent(Hero hero, TalentId id) =>
            hero != null &&
            _lordTalents.TryGetValue(hero.StringId, out var list) &&
            list.Contains((int)id);

        /// Registers the God-King (Tribes of the East ruler) as a Pyrelord mage.
        /// Not Ashen — he is fire and conquest, not cold ruin.
        public static void SetGodKing(Hero hero)
        {
            if (hero == null) return;
            _mageIds.Add(hero.StringId);
            _lordTalents[hero.StringId] = new List<int>
            {
                (int)TalentId.Extinguish,
                (int)TalentId.Scatter,
                (int)TalentId.Immolate,
                (int)TalentId.Sunder,
                (int)TalentId.BreakWills,
                (int)TalentId.Smoulder,
            };
        }

        /// Registers the false emperor as mage+Ashen with the full arsenal.
        /// Skips clan movement and appearance change — he wears the emperor's face.
        public static void SetFalseEmperor(Hero hero)
        {
            if (hero == null) return;
            _mageIds.Add(hero.StringId);
            _ashenIds.Add(hero.StringId);
            _lordTalents[hero.StringId] = new List<int>
            {
                (int)TalentId.Extinguish,
                (int)TalentId.BreakWills,
                (int)TalentId.Plague,
                (int)TalentId.Scatter,
                (int)TalentId.Smoulder,
                (int)TalentId.Sunder,
                (int)TalentId.Immolate,
            };
        }

        // Registers a companion hero as a mage AND flags them as a companion mage
        // so aging is tracked at 1.25× the normal rate.
        public static void RegisterCompanionMage(Hero hero)
        {
            if (hero == null) return;
            SetMage(hero, true);
            _companionMageIds.Add(hero.StringId);
        }

        public static bool IsCompanionMage(Hero hero) =>
            hero != null && _companionMageIds.Contains(hero.StringId);

        public static void ResetForNewGame()
        {
            _seeded = false;
            _mageIds.Clear();
            _ashenIds.Clear();
            _companionMageIds.Clear();
            _lordTalents.Clear();
            _campaignCooldowns.Clear();
            _lordDaysSpent.Clear();
        }

        // ── Seeding ───────────────────────────────────────────────────────────
        public static void SeedInitialLords()
        {
            if (_seeded) return;
            _seeded = true;
            try
            {
                var lords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h != Hero.MainHero && h.IsAlive)
                    .ToList();
                int target = Math.Max(1, (int)(lords.Count * TargetFraction));
                // The Forest Clans (Battania) never carry the gift — see SetMage.
                var eligible = lords.Where(h => (h.Culture?.StringId ?? "") != "battania").ToList();
                Shuffle(eligible);
                for (int i = 0; i < Math.Min(target, eligible.Count); i++)
                {
                    _mageIds.Add(eligible[i].StringId);
                    AssignPathArchetype(eligible[i].StringId);
                }
                // seeding is silent — no announcement

                // Seed dark gifts for evil lords (low Honor AND low Mercy).
                try
                {
                    foreach (Hero h in Hero.AllAliveHeroes
                        .Where(h => h.IsLord && h != Hero.MainHero && h.IsAlive
                                 && !_ashenIds.Contains(h.StringId)
                                 && h.GetTraitLevel(DefaultTraits.Honor) <= -2
                                 && h.GetTraitLevel(DefaultTraits.Mercy) <= -2))
                    {
                        DarkGiftSystem.SeedNpcGifts(h, isAshenLord: false, isEvilLord: true);
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // Seed dark gifts for Aserai lords — the desert carries the culture of darkness.
                try
                {
                    foreach (Hero h in Hero.AllAliveHeroes
                        .Where(h => h.IsLord && h != Hero.MainHero && h.IsAlive
                                 && !_ashenIds.Contains(h.StringId)
                                 && h.Clan?.Kingdom?.StringId == "aserai"))
                    {
                        DarkGiftSystem.SeedNpcGifts(h, isAshenLord: false, isEvilLord: true);
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void AssignPathArchetype(string heroId)
        {
            var (spells, enchants) = _npcPathArchetypes[_rng.Next(_npcPathArchetypes.Length)];
            var assigned = spells.Select(t => (int)t).ToList();
            // 40% chance for one enchantment from this path's pool
            if (_rng.Next(100) < 40 && enchants.Length > 0)
                assigned.Add((int)enchants[_rng.Next(enchants.Length)]);
            _lordTalents[heroId] = assigned;
        }

        // ── Companion enchantment assignment ─────────────────────────────────
        // Guarantees at least `count` enchantments for companions who carry the gift.
        public static void AssignCompanionEnchantments(Hero hero, int count)
        {
            if (hero == null || count <= 0) return;
            if (!_lordTalents.TryGetValue(hero.StringId, out var list))
            {
                list = new List<int>();
                _lordTalents[hero.StringId] = list;
            }

            var pool = DamageEnchantments.Concat(RestoreEnchantments).ToList();
            Shuffle(pool);
            int added = 0;
            foreach (TalentId t in pool)
            {
                if (!list.Contains((int)t))
                {
                    list.Add((int)t);
                    added++;
                    if (added >= count) break;
                }
            }
        }

        // ── Population regulator ──────────────────────────────────────────────
        public static void CheckPopulationBounds()
        {
            try
            {
                var allLords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h != Hero.MainHero && h.IsAlive)
                    .ToList();
                if (allLords.Count == 0) return;
                int mageLords = allLords.Count(h => _mageIds.Contains(h.StringId));
                float pct = mageLords / (float)allLords.Count;

                if (pct < LowerBound)
                {
                    int needed = (int)(allLords.Count * TargetFraction) - mageLords;
                    // The Forest Clans (Battania) never carry the gift — see SetMage.
                    var candidates = allLords.Where(h => !_mageIds.Contains(h.StringId)
                                                       && (h.Culture?.StringId ?? "") != "battania").ToList();
                    Shuffle(candidates);
                    int added = 0;
                    for (int i = 0; i < needed && i < candidates.Count; i++)
                    {
                        _mageIds.Add(candidates[i].StringId);
                        AssignPathArchetype(candidates[i].StringId);
                        added++;
                    }
                    // population adjustment is silent
                }
                else if (pct > UpperBound)
                {
                    int excess = mageLords - (int)(allLords.Count * TargetFraction);
                    // Exclude Ashen lords from the trim set — they are registered in
                    // both _mageIds and _ashenIds (MarkClanAshen adds both), but are
                    // permanent and must keep their casting ability. Trimming one from
                    // _mageIds would make IsColourLord false, silently disabling battle
                    // and map casting while they remained diplomatically Ashen.
                    var mages = allLords.Where(h => _mageIds.Contains(h.StringId)
                                                 && !_ashenIds.Contains(h.StringId)).ToList();
                    Shuffle(mages);
                    for (int i = 0; i < excess && i < mages.Count; i++)
                    {
                        _mageIds.Remove(mages[i].StringId);
                        _lordTalents.Remove(mages[i].StringId);
                    }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Life expectancy (spellcasting cost — mirrors the player) ──────────────
        // A mage lord's casting toll is booked here instead of shifting their birth
        // day: it lowers the age at which they burn out (from the base 100), leaving
        // their current age — and thus their skills — untouched. 1 year = 84 days.
        public static void SpendLordLifeExpectancy(Hero hero, int days)
        {
            if (hero == null || days <= 0) return;
            if (_ashenIds.Contains(hero.StringId)) return;   // the cold preserves what remains
            _lordDaysSpent.TryGetValue(hero.StringId, out int cur);
            _lordDaysSpent[hero.StringId] = cur + days;
        }

        // The age at which the fire burns a lord out — the base 100 minus the life
        // the working has already spent. Floored at 20 so a heavy caster still lives.
        public static float LordDeathAge(Hero hero)
        {
            if (hero == null) return 100f;
            _lordDaysSpent.TryGetValue(hero.StringId, out int spent);
            return Math.Max(20f, 100f - spent / 84f);
        }

        // Years of life a lord has left before the fire burns him out — his real
        // spellcasting RESOURCE. The Ashen pay nothing, so they read as unlimited.
        // Used by both the battle AI (ColourLordAI) and the map AI (DailyMapCast)
        // to make lords spend their years like people: freely while young, sparingly
        // near burnout. See NpcCastPlanner.
        public const float UnlimitedLife = 999f;
        public static float LifeBudgetYears(Hero hero)
        {
            if (hero == null) return UnlimitedLife;
            if (_ashenIds.Contains(hero.StringId)) return UnlimitedLife;
            try { return Math.Max(0f, LordDeathAge(hero) - hero.Age); }
            catch { return UnlimitedLife; }
        }

        // A lord's temperament, read from the Calculating trait: >0 hoards his years
        // (miser), <0 spends them recklessly (impulsive), 0 is balanced.
        public static CasterTemper TemperOf(Hero hero)
        {
            try
            {
                int calc = hero.GetTraitLevel(DefaultTraits.Calculating);
                if (calc > 0) return CasterTemper.Calculating;
                if (calc < 0) return CasterTemper.Impulsive;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return CasterTemper.Balanced;
        }

        // ── A lord's learned elements (canonical — used by battle AND map AI) ──────
        // Fire is innate to every mage. Beyond it a lord has LEARNED 0–4 of the other
        // elements, fixed by his identity and scaled by his standing (a tier-6 magnate
        // knows more than a landless knight). The Ashen know them all. Both the battle
        // AI (ColourLordAI) and the campaign-map AI (DailyMapCast) draw from this, so a
        // lord uses ONLY the unified element magic the player wields — no old fire-path
        // spells or brands.
        private static readonly MagicElement[] _learnableElements =
            { MagicElement.Wind, MagicElement.Earth, MagicElement.Water, MagicElement.Spirit };

        public static int LearnedElementCount(Hero hero)
        {
            try
            {
                if (hero == null) return 0;
                if (IsAshenLord(hero)) return 4;                     // the Ashen know them all
                int tier   = Math.Max(0, Math.Min(6, hero.Clan?.Tier ?? 0));
                int jitter = (int)((uint)StableHash(hero.StringId ?? "") % 3); // 0..2
                return Math.Max(0, Math.Min(4, (tier + jitter) / 2));
            }
            catch { return 0; }
        }

        // The lord's repertoire, Fire first, then his learned elements (stable order).
        public static List<MagicElement> KnownElements(Hero hero)
        {
            var known = new List<MagicElement> { MagicElement.Fire };
            int n = LearnedElementCount(hero);
            if (n > 0 && hero != null)
            {
                string id = hero.StringId ?? "";
                foreach (var el in _learnableElements
                    .OrderBy(e => StableHash(id + "|" + (int)e))
                    .Take(n))
                    known.Add(el);
            }
            return known;
        }

        private static int StableHash(string s)
        {
            unchecked { int hash = (int)2166136261; foreach (char c in s) { hash ^= c; hash *= 16777619; } return hash; }
        }

        // Kill all mage lords who have reached their (expectancy-reduced) death age
        // (called from weekly tick). The Ashen never burn out.
        public static void CheckAgeLimit()
        {
            try
            {
                var toKill = Hero.AllAliveHeroes
                    .Where(h => h != Hero.MainHero && h.IsAlive && _mageIds.Contains(h.StringId)
                             && !_ashenIds.Contains(h.StringId)
                             && h.Age >= LordDeathAge(h))
                    .ToList();
                foreach (Hero h in toKill)
                {
                    try
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{h.Name} — a century spent. The current takes what remains.",
                            new Color(0.5f, 0.3f, 0.7f)));
                        KillCharacterAction.ApplyByOldAge(h, true);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Campaign map casting ───────────────────────────────────────────────
        public static void DailyMapCast()
        {
            if (DragonQuestSystem.WorldRekindled) return;
            try
            {
                // Build a single id→hero map so the inner loop is O(1) per lord
                // instead of O(n) → avoids O(n²) behaviour with many mage lords.
                Dictionary<string, Hero> heroById;
                try { heroById = Hero.AllAliveHeroes.ToDictionary(h => h.StringId, h => h); }
                catch { return; }

                // At most 2 false emperor casts, 1 Ashen, and 1 regular mage lord cast per campaign day.
                int falseEmperorCastsToday = 0;
                int ashenCastsToday  = 0;
                int normalCastsToday = 0;
                const int MaxFalseEmperorCastsPerDay = 2;
                const int MaxAshenCastsPerDay  = 1;
                const int MaxNormalCastsPerDay = 1;

                foreach (string id in _mageIds.ToList())
                {
                    heroById.TryGetValue(id, out Hero hero);
                    if (hero == null || !hero.IsAlive || hero.IsPrisoner) continue;

                    bool isBlight = IsAshenLord(hero);
                    bool isFalseEmperor = isBlight
                        && BurningLabQuestSystem.IsArenicosHero(hero)
                        && !BurningLabQuestSystem.ArenicosIsTrue;

                    // Skip once the per-type daily cap is reached
                    if (isFalseEmperor  && falseEmperorCastsToday >= MaxFalseEmperorCastsPerDay) continue;
                    if (!isFalseEmperor && isBlight && ashenCastsToday >= MaxAshenCastsPerDay)   continue;
                    if (!isBlight && normalCastsToday >= MaxNormalCastsPerDay) continue;

                    // First encounter: seed a random initial offset so lords don't all
                    // cast on the same day after seeding or loading a save.
                    if (!_campaignCooldowns.ContainsKey(id))
                        _campaignCooldowns[id] = isFalseEmperor ? _rng.Next(3) : isBlight ? _rng.Next(7) : _rng.Next(8);

                    if (_campaignCooldowns.TryGetValue(id, out int cd) && cd > 0)
                    { _campaignCooldowns[id] = cd - 1; continue; }

                    // False emperor casts voraciously; Blight lords cast hungrily.
                    // Normal lords spend their remaining life like people (mirrors the
                    // battle model): a lord with years to spare works often, one near
                    // burnout rarely — nudged by temperament (impulsive spend, the
                    // calculating hoard). See NpcCastPlanner / [[feedback-npc-parity]].
                    int castChance;
                    if (isFalseEmperor) castChance = 30;
                    else if (isBlight)  castChance = 10;
                    else
                    {
                        float lifeFrac = NpcCastPlanner.LifeFrac(LifeBudgetYears(hero));
                        castChance = 2 + (int)Math.Round(7f * lifeFrac);   // 2 (burnout) .. 9 (fresh)
                        switch (TemperOf(hero))
                        {
                            case CasterTemper.Impulsive:   castChance += 2; break;
                            case CasterTemper.Calculating: castChance = Math.Max(1, castChance - 2); break;
                        }
                    }
                    if (_rng.Next(100) >= castChance) continue;

                    // Cast one of the elements this lord has learned — the same unified
                    // element map workings the player wields, no old fire-path spells.
                    var known = KnownElements(hero);
                    MagicElement chosen = known[_rng.Next(known.Count)];

                    // A studied lord's rite sometimes answers as a blended working
                    // instead — the same fusion the player commands by chord, applied
                    // to the map spell. Summons never come out of this (TryFuse only
                    // returns a Fusion for non-Spirit pairs, so this is naturally a
                    // no-op whenever Spirit was drawn).
                    if (_rng.Next(100) < 35)
                    {
                        foreach (var partner in known)
                        {
                            if (partner == chosen) continue;
                            var fused = ElementComboMath.TryFuse(chosen, partner);
                            if (fused == null || !ElementComboMath.IsFusion(fused.Value)) continue;
                            chosen = fused.Value;
                            break;
                        }
                    }
                    try
                    {
                        TalentSystem.ExecuteNpcElementMapSpell(hero, chosen);
                        // False emperor recovers fastest; Blight lords quickly; normal lords need days
                        _campaignCooldowns[id] = isFalseEmperor ? 2 + _rng.Next(3)
                                               : isBlight       ? 5 + _rng.Next(4)
                                                                 : 5 + _rng.Next(5);
                        if (hero.Clan != null) hero.Clan.Renown += 3f;
                        if (isFalseEmperor) falseEmperorCastsToday++;
                        else if (isBlight)  ashenCastsToday++;
                        else                normalCastsToday++;
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Death ─────────────────────────────────────────────────────────────
        public static void OnLordDied(Hero hero)
        {
            if (hero == null) return;
            _mageIds.Remove(hero.StringId);
            _ashenIds.Remove(hero.StringId);
            _companionMageIds.Remove(hero.StringId);
            _lordTalents.Remove(hero.StringId);
            _campaignCooldowns.Remove(hero.StringId);
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var ids           = _mageIds.ToList();
            var ashenIds      = _ashenIds.ToList();
            var companionIds  = _companionMageIds.ToList();
            bool seeded  = _seeded;
            var cdKeys = _campaignCooldowns.Keys.ToList();
            var cdVals = _campaignCooldowns.Values.ToList();
            var lifeKeys = _lordDaysSpent.Keys.ToList();
            var lifeVals = _lordDaysSpent.Values.ToList();

            // Flatten lord talents: parallel lists of heroId, count, flat ints
            var talentIds   = _lordTalents.Keys.ToList();
            var talentCnts  = _lordTalents.Values.Select(v => v.Count).ToList();
            var talentFlat  = _lordTalents.Values.SelectMany(v => v).ToList();

            store.SyncData("LDM_MageIds",       ref ids);
            store.SyncData("LDM_AshenIds",      ref ashenIds);
            store.SyncData("LDM_CompanionIds",  ref companionIds);
            store.SyncData("LDM_MageSeeded",    ref seeded);
            store.SyncData("LDM_CdKeys",      ref cdKeys);
            store.SyncData("LDM_CdVals",      ref cdVals);
            store.SyncData("LDM_LifeKeys",    ref lifeKeys);
            store.SyncData("LDM_LifeVals",    ref lifeVals);
            store.SyncData("LDM_TalentIds",   ref talentIds);
            store.SyncData("LDM_TalentCnts",  ref talentCnts);
            store.SyncData("LDM_TalentFlat",  ref talentFlat);

            _mageIds.Clear();
            if (ids != null) foreach (var id in ids) _mageIds.Add(id);
            _ashenIds.Clear();
            if (ashenIds != null) foreach (var id in ashenIds) _ashenIds.Add(id);
            _companionMageIds.Clear();
            if (companionIds != null) foreach (var id in companionIds) _companionMageIds.Add(id);
            _seeded = seeded;

            _campaignCooldowns.Clear();
            if (cdKeys != null && cdVals != null)
                for (int i = 0; i < Math.Min(cdKeys.Count, cdVals.Count); i++)
                    _campaignCooldowns[cdKeys[i]] = cdVals[i];

            _lordDaysSpent.Clear();
            if (lifeKeys != null && lifeVals != null)
                for (int i = 0; i < Math.Min(lifeKeys.Count, lifeVals.Count); i++)
                    _lordDaysSpent[lifeKeys[i]] = lifeVals[i];

            _lordTalents.Clear();
            if (talentIds != null && talentCnts != null && talentFlat != null)
            {
                int si = 0;
                for (int i = 0; i < talentIds.Count; i++)
                {
                    int cnt = i < talentCnts.Count ? talentCnts[i] : 0;
                    var list = new List<int>();
                    for (int j = 0; j < cnt && si < talentFlat.Count; j++, si++)
                        list.Add(talentFlat[si]);
                    if (list.Count > 0)
                        _lordTalents[talentIds[i]] = list;
                }
            }
        }

        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }
    }
}
