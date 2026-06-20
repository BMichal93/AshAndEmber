// =============================================================================
// LIFE & DEATH MAGIC — AgingSystem.cs
// Aging cost mechanic: each battle spell costs round(1.65^(n-1)) days (max 84),
// each campaign spell costs 1 day (Resonance: 25% chance to skip).
// On reaching age 100, the mage dies.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class AgingSystem
    {
        private static readonly Random _rng = new Random();
        private static bool _pendingAshenDecision = false;

        // Tracks which aging milestones the player has already received (ages 50/60/70/80/90).
        // Persisted so reloading doesn't re-fire a milestone the player already got.
        private static readonly HashSet<int> _milestonesTriggered = new HashSet<int>();

        // Milestone queued but not yet shown — held until the campaign map is active.
        private static int _pendingMilestoneAge = 0;

        // ── Core aging ────────────────────────────────────────────────────────

        /// <summary>
        /// Ages <paramref name="hero"/> by <paramref name="days"/> in-game days.
        /// Shows a message only for the player hero.
        /// </summary>
        public static void AgeHero(Hero hero, int days)
        {
            if (hero == null || days <= 0) return;
            // Ashen do not age — the cold preserves what remains
            if (hero == Hero.MainHero && MageKnowledge.IsAshen) return;
            if (hero != Hero.MainHero && ColourLordRegistry.IsAshenLord(hero)) return;
            try
            {
                if (hero == Hero.MainHero) _ledgerDaysSpent += days;
                hero.SetBirthDay(hero.BirthDay - CampaignTime.Days(days));

                if (hero == Hero.MainHero)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The fire burns its cost — {days} day{(days > 1 ? "s" : "")} older. Age: {(int)hero.Age}.",
                        new Color(0.7f, 0.5f, 0.3f)));

                CheckAgeLimit(hero);

                if (hero == Hero.MainHero)
                {
                    try { CheckAgingMilestone(hero); } catch { }
                    try { FlushPendingMilestone(); } catch { }
                }
            }
            catch { }
        }

        // Legacy stub; clear any per-mission knockdown state
        public static void ClearKnockdowns() { }

        /// <summary>
        /// Battle spell aging cost: geometric — round(1.65^(n−1)), capped at 84 days (1 Bannerlord year).
        /// Bannerlord year = 84 campaign days (4 seasons × 21 days).
        /// With the 5-form + 5-effect cap (max 10 inputs): 1→1 | 2→2 | 3→3 | 4→4 | 5→7 | 6→12 | 7→20 | 8→33 | 9→55 | 10→84 (cap).
        /// Tempered (BattleMage) talent reduces cost by whichever is larger: 25% or 1 flat day
        /// (minimum 1 — battle casts are never free), and beyond age 40 also shaves 0.5% per year
        /// off the final cost, capped at 30%. The flat floor means even 1-input spells feel the talent.
        /// </summary>
        public static int ComputeBattleAgingCost(int totalInputs, bool hasBattleMageTalent)
        {
            // Game-facing wrapper: read the hero's age, then defer to the pure overload.
            // Keeping the math in a TaleWorlds-free overload preserves testability
            // (PureLogicTests cannot load Hero) and the AgingSystem purity convention.
            float age = 0f;
            try { if (Hero.MainHero != null) age = (float)Hero.MainHero.Age; }
            catch { }
            return ComputeBattleAgingCost(totalInputs, hasBattleMageTalent, age);
        }

        /// <summary>
        /// Pure form of <see cref="ComputeBattleAgingCost(int,bool)"/> taking the hero's
        /// age explicitly so it can be exercised without the TaleWorlds runtime.
        /// </summary>
        public static int ComputeBattleAgingCost(int totalInputs, bool hasBattleMageTalent, float heroAge)
        {
            // Geometric scaling: small spells are cheap; large spells become very expensive.
            // Base 1.65, standard rounding, hard cap at 84 campaign days (= 1 Bannerlord year).
            // At the 5+5 max (10 inputs), 1.65^9 ≈ 90.6 → rounds to 84 (cap).
            int cost = Math.Min(84, Math.Max(1, (int)(Math.Pow(1.65, totalInputs - 1) + 0.5)));
            if (hasBattleMageTalent)
            {
                int reduction = Math.Max(1, (int)Math.Round(cost * 0.25f));
                cost = Math.Max(1, cost - reduction);
            }

            // Tempered (merged Veteran's Ash): each year beyond 40 shaves 0.5% off cost, capped at 30%.
            // At age 50 → -5%, age 70 → -15%, age 100 → -30% (death threshold).
            if (hasBattleMageTalent && heroAge > 40f)
            {
                float reduction = Math.Min(0.30f, (heroAge - 40f) * 0.005f);
                cost = Math.Max(1, (int)Math.Round(cost * (1f - reduction)));
            }

            return cost;
        }

        /// <summary>
        /// Reverses aging by <paramref name="days"/> campaign days, clamped so the hero never drops below age 20.
        /// Bannerlord year = 84 campaign days (4 seasons × 21 days).
        /// Shows a message only for the player hero.
        /// </summary>
        public static void RejuvenateHero(Hero hero, int days)
        {
            if (hero == null || days <= 0) return;
            try
            {
                const float MinAge = 20f;
                float currentAge = (float)hero.Age;
                if (currentAge <= MinAge) return;

                // Clamp days so we never push below minimum age.
                // 1 Bannerlord year = 84 campaign days (4 seasons × 21 days).
                int maxDays = Math.Max(0, (int)((currentAge - MinAge) * 84f));
                days = Math.Min(days, maxDays);
                if (days <= 0) return;

                if (hero == Hero.MainHero) _ledgerDaysReclaimed += days;
                hero.SetBirthDay(hero.BirthDay + CampaignTime.Days(days));

                // Hard floor: float math in the clamp above can drift. Snap back if needed.
                if ((float)hero.Age < MinAge)
                    try { hero.SetBirthDay(hero.BirthDay - CampaignTime.Days((int)((MinAge - (float)hero.Age) * 84f) + 1)); } catch { }

                if (hero == Hero.MainHero)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The fire gives back — {days} day{(days > 1 ? "s" : "")} younger. Age: {(int)hero.Age}.",
                        new Color(0.9f, 0.6f, 0.3f)));
            }
            catch { }
        }

        // ── Death at 100 ──────────────────────────────────────────────────────

        public static void CheckAgeLimit(Hero hero)
        {
            if (hero == null || !hero.IsAlive) return;
            if (hero.Age < 100f) return;
            // Ashen mages are immune to age-death
            if (hero == Hero.MainHero && MageKnowledge.IsAshen) return;
            if (hero != Hero.MainHero && ColourLordRegistry.IsAshenLord(hero)) return;
            if (hero != Hero.MainHero && BurningLabQuestSystem.IsArenicosHero(hero)) return;
            try
            {
                if (hero == Hero.MainHero)
                {
                    if (_pendingAshenDecision) return;
                    _pendingAshenDecision = true;
                    // Defer the choice to the campaign layer
                    MageKnowledge.QueueAshenPrompt(() => _pendingAshenDecision = false);
                    return;
                }

                // NPC mage: 5% chance to become Ashen instead of dying
                if (ColourLordRegistry.IsColourLord(hero) && _rng.Next(100) < 5)
                {
                    ColourLordRegistry.SetAshen(hero, true);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{hero.Name} — the fire does not die. Something colder burns in its place.",
                        new Color(0.3f, 0.35f, 0.7f)));
                    return;
                }

                InformationManager.DisplayMessage(new InformationMessage(
                    $"{hero.Name} — a century spent. The fire burns to ash at last.",
                    new Color(0.6f, 0.5f, 0.35f)));
                KillCharacterAction.ApplyByOldAge(hero, true);
            }
            catch { }
        }

        /// <summary>Called on daily tick to check all mage lords for age 100.</summary>
        public static void DailyAgeCheck()
        {
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.Where(h => h.IsAlive && ColourLordRegistry.IsColourLord(h)).ToList())
                    CheckAgeLimit(h);
                // Also check player
                if (Hero.MainHero != null && MageKnowledge.IsMage)
                    CheckAgeLimit(Hero.MainHero);
            }
            catch { }
        }

        // ── Aging milestones ──────────────────────────────────────────────────

        private static readonly int[] _milestoneAges = { 50, 60, 70, 80, 90 };

        private static void CheckAgingMilestone(Hero hero)
        {
            int age = (int)hero.Age;
            foreach (int milestone in _milestoneAges)
            {
                if (age >= milestone && _milestonesTriggered.Add(milestone))
                {
                    if (_pendingMilestoneAge == 0)
                        _pendingMilestoneAge = milestone;
                }
            }
        }

        /// <summary>
        /// Shows the pending age-milestone popup if we are on the campaign map.
        /// Call from AgeHero (immediate path when not in battle) and from OnMissionEnded / OnDailyTick.
        /// </summary>
        public static void FlushPendingMilestone()
        {
            if (_pendingMilestoneAge <= 0) return;
            try { if (Mission.Current != null) return; } catch { }
            if (MageKnowledge._deferredInquiry != null) return;

            int milestone = _pendingMilestoneAge;
            _pendingMilestoneAge = 0;
            ShowMilestoneEvent(milestone);
        }

        private static void ShowMilestoneEvent(int milestone)
        {
            try
            {
                string title, body, button;
                switch (milestone)
                {
                    case 50:
                        title  = "Fifty Years of Ash";
                        body   = "You have outlived half your blood. The fire does not celebrate. It simply keeps burning, and so do you — a little less of what you were before, a little more of something else entirely.\n\nYou do not know if that is a fair trade.";
                        button = "Time passes.";
                        break;
                    case 60:
                        title  = "Sixty Years";
                        body   = "The faces around you change now. The lords you knew when you first learned the fire's name — some are ash, some are grey-haired, some you cannot place anymore.\n\nYou remember everything. That is its own weight.";
                        button = "So it goes.";
                        break;
                    case 70:
                        title  = "Seventy Years";
                        body   = "The fire is still there. That is not comfort — it is accounting. You have traded seventy years for what the fire gave you, and you cannot say anymore whether the ledger was worth keeping.\n\nThe flame does not answer when you ask.";
                        button = "The years mount.";
                        break;
                    case 80:
                        title  = "Eighty Years";
                        body   = "People stop calling you old. They call you ancient, as if you have become a landmark rather than a person. There are children alive who were born after you first learned to cast. They will bury you, perhaps.\n\nYou find that is not the part that troubles you.";
                        button = "What remains.";
                        break;
                    case 90:
                        title  = "Ninety Years";
                        body   = "A decade before the end — not the fire's end, which is your body failing or the cold claiming you. The world has moved in ways you remember as fresh and recent, and then moved on again, and a third time, and still you burn.\n\nThe weight of that is different from what you expected.";
                        button = "A little more.";
                        break;
                    default:
                        return;
                }

                MageKnowledge._deferredInquiry = () =>
                {
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            title, body,
                            true, false,
                            button, null,
                            () => { }, null));
                    }
                    catch { }
                };
            }
            catch { }
        }

        // ── The Ledger of Years ───────────────────────────────────────────────
        // Running account of what the fire has taken from (and returned to) the
        // player, shown in the grimoire so the aging economy is visible.

        private static int _ledgerDaysSpent     = 0; // days of life the fire has taken
        private static int _ledgerDaysReclaimed = 0; // days clawed back (Reap, Ember, rites)
        private static int _ledgerBattleCasts   = 0;
        private static int _ledgerMapCasts      = 0;
        private static int _missionCastCount    = 0; // resets each mission — not persisted

        public static int  LedgerDaysSpent     => _ledgerDaysSpent;
        public static int  MissionCastCount    => _missionCastCount;
        public static void ClearMissionCasts() => _missionCastCount = 0;
        public static void RecordBattleCast()  { _ledgerBattleCasts++; _missionCastCount++; }
        public static void RecordMapCast()     => _ledgerMapCasts++;

        public static string BuildLedgerText()
        {
            var h = Hero.MainHero;
            if (h == null) return "";
            try
            {
                int age = (int)h.Age;
                var lines = new System.Text.StringBuilder();
                lines.Append("── THE LEDGER OF YEARS ──────────────────────────\n");
                if (MageKnowledge.IsAshen)
                {
                    lines.Append($"  Age: {age} — the cold preserves you. The ledger is closed.\n");
                }
                else
                {
                    // 1 Bannerlord year = 84 campaign days (4 seasons × 21 days).
                    int daysLeft  = Math.Max(0, (int)((100f - (float)h.Age) * 84f));
                    int yearsLeft = daysLeft / 84;
                    lines.Append($"  Age: {age}   |   Until the fire burns out (100): ~{yearsLeft} year{(yearsLeft != 1 ? "s" : "")} ({daysLeft} days)\n");
                }
                lines.Append($"  Days the fire has taken: {_ledgerDaysSpent}");
                lines.Append($"   |   Days reclaimed: {_ledgerDaysReclaimed}\n");
                lines.Append($"  Workings: {_ledgerBattleCasts} in battle, {_ledgerMapCasts} on the map\n");
                int net = _ledgerDaysSpent - _ledgerDaysReclaimed;
                if (net > 84)
                    lines.Append($"  The fire holds {net / 84} year{(net / 84 != 1 ? "s" : "")} of your life. It does not give receipts.\n");
                if (!MageKnowledge.IsAshen)
                {
                    string coldNote = MageKnowledge.WhisperTier switch
                    {
                        3 => "  The cold: very close. You hear it even in daylight.\n",
                        2 => "  The cold: it favours you. The grey altars open faster; the sanctuary flame leans away.\n",
                        1 => "  The cold: it has noticed you. Nothing more — yet.\n",
                        _ => "",
                    };
                    lines.Append(coldNote);
                }
                try { lines.Append(TempleCovenant.LedgerLine()); } catch { }
                lines.Append("\n");
                return lines.ToString();
            }
            catch { return ""; }
        }

        // ── Persistence ───────────────────────────────────────────────────────

        public static void Save(IDataStore store)
        {
            var list = _milestonesTriggered.ToList();
            store.SyncData("AG_Milestones",        ref list);
            if (list != null)
            {
                _milestonesTriggered.Clear();
                foreach (var m in list) _milestonesTriggered.Add(m);
            }

            store.SyncData("AG_PendingMilestone",  ref _pendingMilestoneAge);
            store.SyncData("AG_LedgerSpent",       ref _ledgerDaysSpent);
            store.SyncData("AG_LedgerReclaimed",   ref _ledgerDaysReclaimed);
            store.SyncData("AG_LedgerBattle",      ref _ledgerBattleCasts);
            store.SyncData("AG_LedgerMap",         ref _ledgerMapCasts);
        }

        public static void ResetForNewGame()
        {
            _milestonesTriggered.Clear();
            _pendingMilestoneAge = 0;
            _ledgerDaysSpent     = 0;
            _ledgerDaysReclaimed = 0;
            _ledgerBattleCasts   = 0;
            _ledgerMapCasts      = 0;
            _missionCastCount    = 0;
        }
    }
}
