// =============================================================================
// ASH AND EMBER — AI/AshenFactionCulture.cs
// The Ashen (formerly Sturgian) culture's mechanics for the player.
//
//   Ashen Endurance   — reduced army wages in the cold months (vanilla feat,
//                       relabelled).
//   The Hard North    — enemy looters deal less damage to villages (vanilla
//                       feat, relabelled).
//   The Grey Tithe    — settlements yield less tax income (vanilla feat,
//                       relabelled as penalty).
//
//   Cold Marrow       — real mechanic: the player's hero recovers one extra
//                       day of wound time each morning.
//
// DailyTick() is called from MagicCampaignBehavior.
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    internal static class AshenFactionCulture
    {
        public static bool IsPlayerAshen
        {
            get
            {
                try { return Hero.MainHero?.Culture?.StringId == "sturgia"; }
                catch { return false; }
            }
        }

        // ── Cold Marrow — extra wound recovery ────────────────────────────────
        // The fire that runs through Ashen blood closes wounds that would linger.
        // Reduces the player hero's remaining wound days by one extra each morning.
        public static void DailyTick()
        {
            if (!IsPlayerAshen) return;
            try
            {
                var hero = Hero.MainHero;
                if (hero == null || !hero.IsAlive) return;
                if (hero.HitPoints >= hero.MaxHitPoints) return;

                int newHp = Math.Min(hero.MaxHitPoints, hero.HitPoints + 10);
                hero.HitPoints = newHp;

                if (newHp >= hero.MaxHitPoints)
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Cold Marrow — the ash in your blood has sealed the last of your wounds.",
                        new Color(0.65f, 0.75f, 0.85f)));
            }
            catch { }
        }
    }
}
