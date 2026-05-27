// =============================================================================
// LIFE & DEATH MAGIC — AgingSystem.cs  (SaturationSystem.cs)
// Replaces Saturation with the aging cost mechanic.
// Each battle spell ages the caster: 4 inputs = 1 day, +1 per 2 extra inputs.
// Each campaign spell ages the caster by 1 day (mitigated by Sorcerer talent).
// On reaching age 100, mage dies.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ColoursOfCalradia
{
    public static class AgingSystem
    {
        // Stub properties kept so any lingering call-sites compile
        public static bool IsPlayerBlight => false;
        public static bool IsPlayerPrism  => false;
        public static int  PlayerSaturation    => 0;
        public static int  PlayerMaxSaturation => 99;

        public static void ResetForNewGame() { }
        public static void GainSaturation() { }
        public static void GainSaturationCampaign() { }
        public static void CheckNightReset() { }
        public static void RecalcMax() { }
        public static void TickKnockdown(float dt) { }
        public static void FlushMaxDepletionPrompt() { }
        public static void ClearKnockdowns() { }
        public static void SetPlayerPrism(bool v) { }

        public static void Save(IDataStore store) { }

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
                        $"The current takes its toll — {days} day{(days > 1 ? "s" : "")} older. Age: {(int)hero.Age}.",
                        new Color(0.6f, 0.6f, 0.9f)));

                CheckAgeLimit(hero);
            }
            catch { }
        }

        /// <summary>
        /// Battle spell aging cost formula:
        ///   totalInputs &lt; 4  → 0 days
        ///   4–5             → 1 day
        ///   6–7             → 2 days
        ///   8–9             → 3 days  etc.
        /// With BattleMage talent the denominator shifts from 4 to 5.
        /// </summary>
        public static int ComputeBattleAgingCost(int totalInputs, bool hasBattleMageTalent)
        {
            int threshold = hasBattleMageTalent ? 5 : 4;
            if (totalInputs < threshold) return 0;
            return totalInputs / threshold;
        }

        // ── Death at 100 ──────────────────────────────────────────────────────

        public static void CheckAgeLimit(Hero hero)
        {
            if (hero == null || !hero.IsAlive) return;
            if (hero.Age < 100f) return;
            try
            {
                if (hero == Hero.MainHero)
                    InformationManager.DisplayMessage(new InformationMessage(
                        "A century of years — the current reclaims what it gave.",
                        new Color(0.4f, 0.4f, 0.8f)));
                else
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{hero.Name} — a century spent. The current takes what remains.",
                        new Color(0.4f, 0.4f, 0.8f)));

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
