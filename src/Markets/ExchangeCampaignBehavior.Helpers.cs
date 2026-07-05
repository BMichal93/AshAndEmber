// =============================================================================
// ASH AND EMBER — ExchangeCampaignBehavior.Helpers.cs
// Trade-skill, mood, gold, and XP helpers.
// Partial of ExchangeCampaignBehavior (shared static state lives in ExchangeCampaignBehavior.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class ExchangeCampaignBehavior
    {
        // ── Helpers ───────────────────────────────────────────────────────────
        private static int TradeSkill()
        {
            try { return Hero.MainHero?.GetSkillValue(DefaultSkills.Trade) ?? 0; }
            catch { return 0; }
        }

        private static int CurrentMood()
        {
            try { return SpeculationMath.MoodShift(Settlement.CurrentSettlement?.Town?.Prosperity ?? 3000f); }
            catch { return 0; }
        }

        private static string MoodLabel(int mood)
            => mood > 0 ? "Booming" : mood < 0 ? "Depressed" : "Steady";

        private static string Pct(int v) => v >= 0 ? $"+{v}%" : $"{v}%";

        private static void GiveGold(int amount)
        {
            if (amount <= 0) return;
            try { if (Hero.MainHero != null) Hero.MainHero.Gold += amount; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void AddTradeXp(int xp)
        {
            if (xp <= 0) return;
            try { Hero.MainHero?.HeroDeveloper?.AddSkillXp(DefaultSkills.Trade, xp); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
