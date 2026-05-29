// =============================================================================
// LIFE & DEATH MAGIC — AgingSystem.cs
// Aging cost mechanic: each battle spell costs (totalInputs / 4) days,
// each campaign spell costs 1 day (Resonance: 25% chance to skip).
// On reaching age 100, the mage dies.
// =============================================================================

using System;
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

        // ── Core aging ────────────────────────────────────────────────────────

        /// <summary>
        /// Ages <paramref name="hero"/> by <paramref name="days"/> in-game days.
        /// Shows a message only for the player hero.
        /// </summary>
        public static void AgeHero(Hero hero, int days)
        {
            if (hero == null || days <= 0) return;
            try
            {
                hero.SetBirthDay(hero.BirthDay - CampaignTime.Days(days));

                if (hero == Hero.MainHero)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The fire burns its cost — {days} day{(days > 1 ? "s" : "")} older. Age: {(int)hero.Age}.",
                        new Color(0.7f, 0.5f, 0.3f)));

                CheckAgeLimit(hero);
            }
            catch { }
        }

        // Legacy stub; clear any per-mission knockdown state
        public static void ClearKnockdowns() { }

        /// <summary>
        /// Battle spell aging cost: 1 day per 2 inputs (round up, min 1).
        /// BattleMage talent subtracts 1 from the total cost (minimum 0).
        /// Examples: 2 inputs = 1 day | 4 inputs = 2 days | 6 inputs = 3 days
        /// </summary>
        public static int ComputeBattleAgingCost(int totalInputs, bool hasBattleMageTalent)
        {
            // Divide by 2 (round up, min 1) so the player ages at roughly twice the NPC rate
            // rather than 4× — 4 inputs = 2 days, 6 inputs = 3 days, etc.
            int cost = Math.Max(1, (totalInputs + 1) / 2);
            if (hasBattleMageTalent) cost = Math.Max(0, cost - 1);
            return cost;
        }

        /// <summary>
        /// Reverses aging by <paramref name="days"/> in-game days.
        /// Shows a message only for the player hero.
        /// </summary>
        public static void RejuvenateHero(Hero hero, int days)
        {
            if (hero == null || days <= 0) return;
            try
            {
                hero.SetBirthDay(hero.BirthDay + CampaignTime.Days(days));

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
    }
}
