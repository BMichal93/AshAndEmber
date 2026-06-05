// =============================================================================
// LIFE & DEATH MAGIC — AgingSystem.cs
// Aging cost mechanic: each battle spell costs (totalInputs / 4) days,
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

namespace AshAndEmber
{
    public static class AgingSystem
    {
        private static readonly Random _rng = new Random();
        private static bool _pendingAshenDecision = false;

        // Tracks which aging milestones the player has already received (ages 50/60/70/80/90).
        // Persisted so reloading doesn't re-fire a milestone the player already got.
        private static readonly HashSet<int> _milestonesTriggered = new HashSet<int>();

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
                hero.SetBirthDay(hero.BirthDay - CampaignTime.Days(days));

                if (hero == Hero.MainHero)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The fire burns its cost — {days} day{(days > 1 ? "s" : "")} older. Age: {(int)hero.Age}.",
                        new Color(0.7f, 0.5f, 0.3f)));

                CheckAgeLimit(hero);

                if (hero == Hero.MainHero)
                    try { CheckAgingMilestone(hero); } catch { }
            }
            catch { }
        }

        // Legacy stub; clear any per-mission knockdown state
        public static void ClearKnockdowns() { }

        /// <summary>
        /// Battle spell aging cost: geometric — round(1.4^(n−1)), capped at 84 days (1 Bannerlord year).
        /// Bannerlord year = 84 campaign days (4 seasons × 21 days).
        /// Examples: 1–2 inputs = 1 day | 5 = 4 | 7 = 8 | 10 = 21 | 12 = 41 | 14 = 80 | 16+ = 84 (cap).
        /// Tempered (BattleMage) talent subtracts 1 from the total cost (minimum 1, never free),
        /// and beyond age 40 also shaves 0.5% per year off the final cost, capped at 30%.
        /// </summary>
        public static int ComputeBattleAgingCost(int totalInputs, bool hasBattleMageTalent)
        {
            // Geometric scaling: small spells are cheap; large spells become very expensive.
            // Base 1.4, standard rounding, hard cap at 84 campaign days (= 1 Bannerlord year).
            int cost = Math.Min(84, Math.Max(1, (int)(Math.Pow(1.4, totalInputs - 1) + 0.5)));
            if (hasBattleMageTalent) cost = Math.Max(1, cost - 1);

            // Tempered (merged Veteran's Ash): each year beyond 40 shaves 0.5% off cost, capped at 30%.
            // At age 50 → -5%, age 70 → -15%, age 100 → -30% (death threshold).
            try
            {
                if (hasBattleMageTalent && Hero.MainHero != null)
                {
                    float age = (float)Hero.MainHero.Age;
                    if (age > 40f)
                    {
                        float reduction = Math.Min(0.30f, (age - 40f) * 0.005f);
                        cost = Math.Max(1, (int)Math.Round(cost * (1f - reduction)));
                    }
                }
            }
            catch { }

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
                    ApplyMilestoneBoon(milestone);
                    ShowMilestoneEvent(milestone);
                }
            }
        }

        private static void ApplyMilestoneBoon(int milestone)
        {
            try
            {
                switch (milestone)
                {
                    case 50:
                        if (Hero.MainHero?.Clan != null) Hero.MainHero.Clan.Renown += 75f;
                        break;

                    case 60:
                        foreach (var h in Hero.AllAliveHeroes
                            .Where(h => h.IsAlive && ColourLordRegistry.IsColourLord(h)).ToList())
                        {
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, h, 2); } catch { }
                        }
                        break;

                    case 70:
                        if (Hero.MainHero?.Clan != null) Hero.MainHero.Clan.Renown += 150f;
                        try
                        {
                            var party70 = Hero.MainHero?.PartyBelongedTo;
                            if (party70 != null) party70.RecentEventsMorale += 30f;
                        }
                        catch { }
                        break;

                    case 80:
                        try
                        {
                            var roster = Hero.MainHero?.PartyBelongedTo?.MemberRoster;
                            if (roster != null)
                            {
                                foreach (var element in roster.GetTroopRoster().ToList())
                                {
                                    if (element.WoundedNumber > 0)
                                        roster.AddToCounts(element.Character, 0, false, -element.WoundedNumber);
                                }
                            }
                        }
                        catch { }
                        break;

                    case 90:
                        if (Hero.MainHero?.Clan != null) Hero.MainHero.Clan.Renown += 300f;
                        break;
                }
            }
            catch { }
        }

        private static void ShowMilestoneEvent(int milestone)
        {
            try
            {
                string title, body, boon;
                switch (milestone)
                {
                    case 50:
                        title = "Fifty Years";
                        body  = "Most who carry the fire never see this birthday. You have. The world has noticed.";
                        boon  = "+75 renown";
                        break;
                    case 60:
                        title = "Sixty Years";
                        body  = "There are mages who were children when you first cast. They know your name now — as a warning, or an ideal.";
                        boon  = "+2 relations with all mage lords";
                        break;
                    case 70:
                        title = "Seventy Years";
                        body  = "The fire is not burning you alive. It is burning you clear. Your soldiers feel it — something steadier than courage.";
                        boon  = "+150 renown, party morale +30";
                        break;
                    case 80:
                        title = "Eighty Years";
                        body  = "You are not living longer. You are burning slower. The wounded at your side rise — the fire lends them what it will not spend on you.";
                        boon  = "All wounded troops healed";
                        break;
                    case 90:
                        title = "Ninety Years";
                        body  = "Ten years to the limit. Whatever you have left to do, the fire will remember it long after you are done.";
                        boon  = "+300 renown";
                        break;
                    default:
                        return;
                }

                if (MageKnowledge._deferredInquiry == null)
                {
                    MageKnowledge._deferredInquiry = () =>
                    {
                        try
                        {
                            InformationManager.ShowInquiry(new InquiryData(
                                title,
                                $"{body}\n\n{boon}.",
                                true, false,
                                "The fire endures.",
                                null,
                                () => { }, null));
                        }
                        catch { }
                    };
                }
            }
            catch { }
        }

        // ── Persistence ───────────────────────────────────────────────────────

        public static void Save(IDataStore store)
        {
            var list = _milestonesTriggered.ToList();
            store.SyncData("AG_Milestones", ref list);
            if (list != null)
            {
                _milestonesTriggered.Clear();
                foreach (var m in list) _milestonesTriggered.Add(m);
            }
        }

        public static void ResetForNewGame()
        {
            _milestonesTriggered.Clear();
        }
    }
}
