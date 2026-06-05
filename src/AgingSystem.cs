// =============================================================================
// LIFE & DEATH MAGIC — AgingSystem.cs
// Aging cost mechanic: each battle spell costs 1.6^(n-1) days (geometric),
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
            }
            catch { }
        }

        // Legacy stub; clear any per-mission knockdown state
        public static void ClearKnockdowns() { }

        /// <summary>
        /// Battle spell aging cost: geometric curve 1.6^(n-1) days — weak spells cost little, powerful ones cost a great deal.
        /// Examples: 1 input = 1 day | 2 = 2 | 3 = 3 | 4 = 4 | 5 = 7 | 6 = 11 | 7 = 17 | 8 = 27 | 9 = 43 | 10 = 69.
        /// Tempered (BattleMage) talent subtracts 1 from the total cost (minimum 1, never free),
        /// and beyond age 40 also shaves 0.5% per year off the final cost, capped at 30%.
        /// </summary>
        public static int ComputeBattleAgingCost(int totalInputs, bool hasBattleMageTalent)
        {
            int cost = Math.Max(1, (int)Math.Round(Math.Pow(1.6, totalInputs - 1)));  // geometric: 1.6^(n-1)
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
