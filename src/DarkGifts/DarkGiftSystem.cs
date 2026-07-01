// =============================================================================
// ASH AND EMBER — DarkGifts/DarkGiftSystem.cs
//
// Dark Gifts are permanent boons purchased at Dark Altars through blood
// sacrifice. Each requires a geometrically growing number of prisoners
// then prisoners + lords. Gifts are permanent but can be renounced at
// any Dark Altar.
//
// Owning any Dark Gift:
//   • Blocks Grace (Sanctuary) and Nature (Living Ember) magic.
//   • Requires Merciless (Mercy <= -1) OR Devious (Honor <= -1) to be active.
//     If the player ceases to be either, gifts remain owned but are inactive
//     until the player returns to darkness.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;

namespace AshAndEmber
{
    public enum DarkGiftId
    {
        IronVeil      = 0,  // Permanent armour bonus — 10% incoming damage reduction
        DarkStrike    = 1,  // On melee hit: weapon erupts for 20 bonus dark damage
        SoulMirror    = 2,  // When struck in melee: reflect 20% of incoming damage
        DarkSpirit    = 3,  // On battle start: dark spirit(s) orbit and damage enemies (up to 3)
        HorseKiller   = 4,  // Every horse within 5 m dies
        SoulDrain     = 5,  // Melee hits drain 30 morale from the victim
        BloodPact     = 6,  // Each kill heals you for 12 HP
        DreadPresence = 7,  // Every 3 s: nearby enemies lose morale and may rout
    }

    // Per-gift sacrifice costs indexed by total gifts already owned (0-based).
    // The first gift a player buys costs PrisonerCosts[0] prisoners + LordCosts[0] lords.
    public static class DarkGiftCosts
    {
        // Total gifts currently owned (counting DarkSpirit per stack) → cost for the next one.
        private static readonly int[] PrisonerCosts = { 5, 12, 25, 50, 80, 130, 200, 300 };
        private static readonly int[] LordCosts     = { 0,  0,  1,  2,  4,   6,   9,  12 };

        // "Owned count" for cost purposes: DarkSpirit can be stacked 1-3×, each
        // stack counts as one purchased gift for the escalation.
        public static int GetNextPrisonerCost(int totalOwned)
        {
            int idx = Math.Min(totalOwned, PrisonerCosts.Length - 1);
            return PrisonerCosts[idx];
        }

        public static int GetNextLordCost(int totalOwned)
        {
            int idx = Math.Min(totalOwned, LordCosts.Length - 1);
            return LordCosts[idx];
        }
    }

    public static class DarkGiftSystem
    {
        // ── Player gift state ──────────────────────────────────────────────────
        // Most gifts are binary (owned or not). DarkSpirit stacks up to 3.
        private static readonly HashSet<DarkGiftId> _ownedGifts = new HashSet<DarkGiftId>();
        private static int _darkSpiritCount = 0; // 0–3; independent of _ownedGifts for DarkSpirit

        // ── NPC gift state (session-only, re-seeded each load) ─────────────────
        // Maps heroStringId → list of gifted DarkGiftIds.
        private static readonly Dictionary<string, List<DarkGiftId>> _npcGifts
            = new Dictionary<string, List<DarkGiftId>>();

        private static readonly Random _rng = new Random();

        // ── Queries ────────────────────────────────────────────────────────────

        /// Total number of "gift slots" owned (DarkSpirit counts per stack).
        public static int TotalOwned =>
            _ownedGifts.Count(g => g != DarkGiftId.DarkSpirit)
            + _darkSpiritCount;

        public static bool HasGift(DarkGiftId gift) =>
            gift == DarkGiftId.DarkSpirit
                ? _darkSpiritCount > 0
                : _ownedGifts.Contains(gift);

        public static int DarkSpiritCount => _darkSpiritCount;

        public static bool HasAnyGift =>
            _ownedGifts.Count > 0 || _darkSpiritCount > 0;

        /// True if the player qualifies to use their gifts right now.
        /// Gifts are owned permanently but only active while Merciless or Devious.
        public static bool GiftsActive
        {
            get
            {
                if (!HasAnyGift) return false;
                try
                {
                    var h = Hero.MainHero;
                    if (h == null) return false;
                    int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                    int honor = h.GetTraitLevel(DefaultTraits.Honor);
                    return mercy <= -1 || honor <= -1;
                }
                catch { return false; }
            }
        }

        /// Returns true when gifts are owned but the trait requirement is currently unmet.
        public static bool GiftsDisabled => HasAnyGift && !GiftsActive;

        /// True when the player qualifies to purchase (or already owns) gifts — same trait gate.
        public static bool PlayerQualifies()
        {
            try
            {
                var h = Hero.MainHero;
                if (h == null) return false;
                int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                int honor = h.GetTraitLevel(DefaultTraits.Honor);
                return mercy <= -1 || honor <= -1;
            }
            catch { return false; }
        }

        // ── Hardening the heart (trait sacrifice) ──────────────────────────────
        // The dark gifts answer only the Merciless or Devious. A player whose heart
        // is still too warm can spill prisoners' blood at the altar to drive their
        // Mercy down toward the cruelty the gifts demand — one step per offering.
        public const int CrueltyPrisonerCost = 2;

        // True while the heart can still be hardened further (Mercy above the floor).
        public static bool CanHardenHeart()
        {
            try
            {
                var h = Hero.MainHero;
                return h != null && h.GetTraitLevel(DefaultTraits.Mercy) > -2;
            }
            catch { return false; }
        }

        // Sacrifice CrueltyPrisonerCost prisoners to drop Mercy by one step (toward
        // Merciless, which satisfies the dark-gift gate). Returns false with a reason
        // when there are too few prisoners or the heart is already as cold as it goes.
        public static bool TryHardenHeart(out string msg)
        {
            msg = "";
            var h = Hero.MainHero;
            if (h == null) { msg = "There is no one here for the altar to take from."; return false; }

            int mercy = 0;
            try { mercy = h.GetTraitLevel(DefaultTraits.Mercy); } catch { }
            if (mercy <= -2)
            {
                msg = "Your heart is already as cold as the altar-stone. There is nothing warm left to give up.";
                return false;
            }

            int soldiers = 0;
            try
            {
                var party = TaleWorlds.CampaignSystem.Party.MobileParty.MainParty;
                soldiers = party?.PrisonRoster?.GetTroopRoster()
                    .Where(e => !e.Character.IsHero).Sum(e => e.Number) ?? 0;
            }
            catch { }
            if (soldiers < CrueltyPrisonerCost)
            {
                msg = $"The altar demands {CrueltyPrisonerCost} prisoners for this offering. You hold {soldiers}.";
                return false;
            }

            ConsumeSacrifice(CrueltyPrisonerCost, 0);
            try { h.SetTraitLevel(DefaultTraits.Mercy, mercy - 1); } catch { }
            msg = "Their blood cools something in you. A measure of mercy bleeds away, and the cruelty the gifts hunger for settles in its place.";
            return true;
        }

        // The other road to the dark: an oath broken over the altar drives Honour
        // down toward Devious, which likewise satisfies the gifts' gate.
        public static bool CanHardenHonor()
        {
            try
            {
                var h = Hero.MainHero;
                return h != null && h.GetTraitLevel(DefaultTraits.Honor) > -2;
            }
            catch { return false; }
        }

        public static bool TryHardenHonor(out string msg)
        {
            msg = "";
            var h = Hero.MainHero;
            if (h == null) { msg = "There is no one here for the altar to take from."; return false; }

            int honor = 0;
            try { honor = h.GetTraitLevel(DefaultTraits.Honor); } catch { }
            if (honor <= -2)
            {
                msg = "You have no honour left to break. The altar has already taken it all.";
                return false;
            }

            int soldiers = 0;
            try
            {
                var party = TaleWorlds.CampaignSystem.Party.MobileParty.MainParty;
                soldiers = party?.PrisonRoster?.GetTroopRoster()
                    .Where(e => !e.Character.IsHero).Sum(e => e.Number) ?? 0;
            }
            catch { }
            if (soldiers < CrueltyPrisonerCost)
            {
                msg = $"The altar demands {CrueltyPrisonerCost} prisoners for this offering. You hold {soldiers}.";
                return false;
            }

            ConsumeSacrifice(CrueltyPrisonerCost, 0);
            try { h.SetTraitLevel(DefaultTraits.Honor, honor - 1); } catch { }
            msg = "You swear a false word over their bodies and mean none of it. Something honest in you goes quiet, and the deviousness the gifts hunger for takes its place.";
            return true;
        }

        // ── Purchase / renounce ────────────────────────────────────────────────

        public static bool CanBuyGift(DarkGiftId gift)
        {
            if (!PlayerQualifies()) return false;
            if (gift == DarkGiftId.DarkSpirit) return _darkSpiritCount < 3;
            return !_ownedGifts.Contains(gift);
        }

        /// Try to purchase a gift by consuming prisoners and lord prisoners from
        /// the player's party. Returns false if the party lacks the required
        /// sacrifices or if the gift is already owned.
        public static bool TryPurchaseGift(DarkGiftId gift, out string errorMsg)
            => TryPurchaseGift(gift, 0, out errorMsg);

        /// Overload that accepts a prisoner discount (e.g. from WhisperTier).
        public static bool TryPurchaseGift(DarkGiftId gift, int prisonerDiscount, out string errorMsg)
        {
            errorMsg = "";
            if (!CanBuyGift(gift))
            {
                errorMsg = gift == DarkGiftId.DarkSpirit && _darkSpiritCount >= 3
                    ? "You already carry three dark spirits. The altar takes no more."
                    : "You already bear this gift.";
                return false;
            }

            int owned = TotalOwned;
            // Templar culture (The Order's Price): dark gifts cost twice as much.
            int pCost = TempleCulture.DarkGiftCost(Math.Max(1, DarkGiftCosts.GetNextPrisonerCost(owned) - prisonerDiscount));
            int lCost = TempleCulture.DarkGiftCost(DarkGiftCosts.GetNextLordCost(owned));
            int fCost = GetNextFocusCost(owned);

            // Both prices are checked before either is spent.
            if (!HasSufficientSacrifice(pCost, lCost, out errorMsg))
                return false;
            int haveFocus = 0;
            try { haveFocus = Hero.MainHero?.HeroDeveloper?.UnspentFocusPoints ?? 0; } catch { }
            if (haveFocus < fCost)
            {
                errorMsg = $"The gift also demands {fCost} focus point{(fCost != 1 ? "s" : "")} of your will; you have {haveFocus}.";
                return false;
            }

            ConsumeSacrifice(pCost, lCost);
            try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints -= fCost; } catch { }
            ApplyGift(gift);

            // Owning a gift clears Grace and blocks Nature.
            try { MiracleInventory._grace = 0; } catch { }

            return true;
        }

        // The will the gift also takes, on top of the blood — the shared gentle
        // talent curve (1,1,2,2,2,3,…) keyed to how many gifts you already bear.
        public static int GetNextFocusCost(int totalOwned) => TalentCostCurve.Cost(totalOwned);

        // Grant a gift with no sacrifice — for character creation and scripted
        // events. Clears Grace, since gifts bar the holy path.
        public static void GrantGift(DarkGiftId gift)
        {
            ApplyGift(gift);
            try { MiracleInventory._grace = 0; } catch { }
        }

        // Grant one random gift (used by the character-creation dark path).
        public static DarkGiftId GrantRandomGift()
        {
            var pool = (DarkGiftId[])Enum.GetValues(typeof(DarkGiftId));
            DarkGiftId gift = pool[_rng.Next(pool.Length)];
            GrantGift(gift);
            return gift;
        }

        public static void RenounceGift(DarkGiftId gift)
        {
            if (gift == DarkGiftId.DarkSpirit)
            {
                if (_darkSpiritCount > 0) _darkSpiritCount--;
            }
            else
            {
                _ownedGifts.Remove(gift);
            }
        }

        public static IEnumerable<DarkGiftId> AllOwnedGifts()
        {
            foreach (var g in _ownedGifts) yield return g;
            for (int i = 0; i < _darkSpiritCount; i++) yield return DarkGiftId.DarkSpirit;
        }

        // ── Sacrifice helpers ──────────────────────────────────────────────────

        private static bool HasSufficientSacrifice(int prisoners, int lords, out string error)
        {
            error = "";
            try
            {
                var party = TaleWorlds.CampaignSystem.Party.MobileParty.MainParty;
                if (party?.PrisonRoster == null) { error = "No prisoners."; return false; }

                var roster = party.PrisonRoster.GetTroopRoster().ToList();
                int heroCount   = roster.Count(e => e.Character.IsHero && e.Number > 0);
                int soldierCount = roster.Where(e => !e.Character.IsHero)
                                         .Sum(e => e.Number);

                if (soldierCount < prisoners)
                {
                    error = $"Requires {prisoners} prisoner{(prisoners != 1 ? "s" : "")} "
                          + $"(you have {soldierCount}).";
                    return false;
                }
                if (heroCount < lords)
                {
                    error = $"Requires {lords} captured lord{(lords != 1 ? "s" : "")} "
                          + $"(you have {heroCount}).";
                    return false;
                }
                return true;
            }
            catch { error = "Cannot read prisoner roster."; return false; }
        }

        private static void ConsumeSacrifice(int prisoners, int lords)
        {
            try
            {
                var party = TaleWorlds.CampaignSystem.Party.MobileParty.MainParty;
                if (party?.PrisonRoster == null) return;

                var roster = party.PrisonRoster.GetTroopRoster().ToList();

                // Consume lord prisoners first.
                int lordsLeft = lords;
                foreach (var entry in roster.Where(e => e.Character.IsHero && e.Number > 0))
                {
                    if (lordsLeft <= 0) break;
                    party.PrisonRoster.AddToCounts(entry.Character, -1);
                    lordsLeft--;
                }

                // Then consume the cheapest common prisoners.
                int prisonersLeft = prisoners;
                foreach (var entry in roster
                    .Where(e => !e.Character.IsHero && e.Number > 0)
                    .OrderBy(e => e.Character.Tier))
                {
                    if (prisonersLeft <= 0) break;
                    int take = Math.Min(entry.Number, prisonersLeft);
                    party.PrisonRoster.AddToCounts(entry.Character, -take);
                    prisonersLeft -= take;
                }
            }
            catch { }
        }

        private static void ApplyGift(DarkGiftId gift)
        {
            if (gift == DarkGiftId.DarkSpirit)
                _darkSpiritCount = Math.Min(_darkSpiritCount + 1, 3);
            else
                _ownedGifts.Add(gift);
        }

        // ── NPC gift API ───────────────────────────────────────────────────────

        public static bool NpcHasGift(Hero hero, DarkGiftId gift) =>
            hero != null &&
            _npcGifts.TryGetValue(hero.StringId, out var list) &&
            list.Contains(gift);

        public static int NpcSpiritCount(Hero hero)
        {
            if (hero == null) return 0;
            if (!_npcGifts.TryGetValue(hero.StringId, out var list)) return 0;
            return list.Count(g => g == DarkGiftId.DarkSpirit);
        }

        public static void SeedNpcGifts(Hero hero, bool isAshenLord, bool isEvilLord)
        {
            if (hero == null) return;
            if (_npcGifts.ContainsKey(hero.StringId)) return;

            var gifts = new List<DarkGiftId>();

            // NPCs only benefit from on-hit / on-kill gifts; the tick-based ones
            // (DarkSpirit, Pale Rider's Curse, Dread Presence) run for the player
            // only, so they are excluded from NPC seeding.
            if (isAshenLord)
            {
                // Ashen lords always get 1-2 gifts. SoulDrain is thematic.
                gifts.Add(DarkGiftId.SoulDrain);
                if (_rng.Next(2) == 0) gifts.Add(DarkGiftId.SoulMirror);
                if (_rng.Next(3) == 0) gifts.Add(_rng.Next(2) == 0 ? DarkGiftId.DarkStrike : DarkGiftId.IronVeil);
            }
            else if (isEvilLord)
            {
                // Evil lords have a 30% chance for one minor gift.
                if (_rng.Next(10) < 3)
                {
                    var pool = new[] { DarkGiftId.DarkStrike, DarkGiftId.IronVeil, DarkGiftId.SoulDrain };
                    gifts.Add(pool[_rng.Next(pool.Length)]);
                }
            }

            if (gifts.Count > 0)
                _npcGifts[hero.StringId] = gifts;
        }

        // ── Lore and description ───────────────────────────────────────────────

        public static string GetGiftName(DarkGiftId gift) => gift switch
        {
            DarkGiftId.IronVeil      => "Iron Veil",
            DarkGiftId.DarkStrike    => "Dark Strike",
            DarkGiftId.SoulMirror    => "Soul Mirror",
            DarkGiftId.DarkSpirit    => "Dark Spirit",
            DarkGiftId.HorseKiller   => "Pale Rider's Curse",
            DarkGiftId.SoulDrain     => "Soul Drain",
            DarkGiftId.BloodPact     => "Blood Pact",
            DarkGiftId.DreadPresence => "Dread Presence",
            _                        => "Unknown Gift",
        };

        public static string GetGiftLore(DarkGiftId gift) => gift switch
        {
            DarkGiftId.IronVeil =>
                "The darkness weaves itself into your flesh like cold iron. Blades that should bite do not find the same depth.",
            DarkGiftId.DarkStrike =>
                "Something hungry rides in your weapon now. Each blow wakes it. It finishes what the steel began.",
            DarkGiftId.SoulMirror =>
                "What strikes you does not fully land — half of it turns back, looking for the hand that threw it.",
            DarkGiftId.DarkSpirit =>
                "A shade peeled from the world's oldest wound follows you now. It does not speak. It does not need to.",
            DarkGiftId.HorseKiller =>
                "The horses feel you before they see you. They know what walks in your shadow. They do not wait to find out.",
            DarkGiftId.SoulDrain =>
                "Every blow you land empties something. The courage runs out first. Then the will. Then whatever held them together.",
            DarkGiftId.BloodPact =>
                "Life spent at your hands comes back — briefly, imperfectly, warm in the way borrowed things are warm.",
            DarkGiftId.DreadPresence =>
                "There is a weight in your presence now that has nothing to do with your sword arm. Men feel it before the battle starts.",
            _ => "",
        };

        public static string GetGiftMechanic(DarkGiftId gift) => gift switch
        {
            DarkGiftId.IronVeil =>
                "Passive. Reduces all incoming damage by 10%.",
            DarkGiftId.DarkStrike =>
                "Passive. Each of your melee hits erupts for an additional 20 dark damage.",
            DarkGiftId.SoulMirror =>
                "Passive. Melee hits against you reflect 20% of inflicted damage back at the attacker.",
            DarkGiftId.DarkSpirit =>
                "Passive. At battle start, a dark spirit erupts from you. It pursues the nearest enemy and deals 25 damage every 4 seconds. Purchasable up to 3 times.",
            DarkGiftId.HorseKiller =>
                "Passive. Every horse within 5 metres of you takes lethal damage each second.",
            DarkGiftId.SoulDrain =>
                "Passive. Each of your melee hits drains 30 morale from the victim.",
            DarkGiftId.BloodPact =>
                "Passive. Each kill restores 12 HP to you.",
            DarkGiftId.DreadPresence =>
                "Passive. Every 3 seconds, enemies within 8 metres lose 20 morale and may panic.",
            _ => "",
        };

        // ── Save / Load ────────────────────────────────────────────────────────

        public static void SyncData(IDataStore store)
        {
            // Player gifts
            try
            {
                var ids = _ownedGifts.Select(g => (int)g).ToList();
                store.SyncData("DGIFT_OwnedIds", ref ids);
                _ownedGifts.Clear();
                if (ids != null) foreach (int i in ids) _ownedGifts.Add((DarkGiftId)i);
            }
            catch { }

            try { store.SyncData("DGIFT_SpiritCount", ref _darkSpiritCount); } catch { }
        }

        public static void ResetForNewGame()
        {
            _ownedGifts.Clear();
            _darkSpiritCount = 0;
            _npcGifts.Clear();
        }

        // ── Grimoire summary ───────────────────────────────────────────────────

        public static string BuildGiftSummary()
        {
            if (!HasAnyGift) return "";

            var lines = new System.Text.StringBuilder();
            lines.AppendLine("── DARK GIFTS ─────────────────────────────────────────────");
            string status = GiftsActive ? "[ACTIVE]" : "[INACTIVE — requires Merciless or Devious]";
            lines.AppendLine(status);
            foreach (var gift in _ownedGifts)
                lines.AppendLine($"  • {GetGiftName(gift)}: {GetGiftMechanic(gift)}");
            if (_darkSpiritCount > 0)
                lines.AppendLine($"  • Dark Spirit ×{_darkSpiritCount}: {GetGiftMechanic(DarkGiftId.DarkSpirit)}");
            lines.AppendLine();
            return lines.ToString();
        }

        // All purchasable gifts in display order (DarkSpirit appears once regardless of stack).
        public static readonly DarkGiftId[] AllGifts = (DarkGiftId[])Enum.GetValues(typeof(DarkGiftId));
    }
}
