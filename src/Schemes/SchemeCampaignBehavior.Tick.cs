// =============================================================================
// ASH AND EMBER — SchemeCampaignBehavior.Tick.cs
// Daily tick and the counter-intelligence sweep.
// Partial of SchemeCampaignBehavior (shared state lives in SchemeCampaignBehavior.cs).
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
    public partial class SchemeCampaignBehavior
    {
        // ── Daily tick ────────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
            try { SchemeSystem.DailyTick();     } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SchemeSystem.NpcSchemeTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Counter-intelligence sweep ────────────────────────────────────────
        private const int SweepCostGold = 500;

        // 40% at Roguery 0 → 85% at Roguery 500.
        private static float SweepSuccessChance(int roguery)
            => Math.Max(0.40f, Math.Min(0.85f, 0.40f + roguery / 500f * 0.45f));

        private static void RunCounterIntelSweep()
        {
            try
            {
                if (Hero.MainHero == null || Hero.MainHero.Gold < SweepCostGold) return;
                try { Hero.MainHero.Gold -= SweepCostGold; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                var plot = SchemeSystem.FindSchemeAgainstPlayerInterests();
                if (plot == null)
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "Your informants come back with tavern gossip and empty hands. "
                        + "If someone is plotting against you, they have not started yet."));
                    return;
                }

                int roguery = Hero.MainHero.GetSkillValue(DefaultSkills.Roguery);
                if (_rng.NextDouble() < SweepSuccessChance(roguery))
                {
                    Hero schemer = SchemeSystem.FindHeroById(plot.InstigatorId);
                    string who   = schemer?.Name?.ToString() ?? "an unknown hand";
                    var def      = SchemeSystem.GetDefinition(plot.Type);
                    SchemeSystem.RemovePendingScheme(plot);
                    try { Hero.MainHero.HeroDeveloper?.AddSkillXp(DefaultSkills.Roguery, 300); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Your informants caught the agent mid-errand. The {def?.Name ?? "scheme"} against you "
                        + $"is unravelled before it lands — and the hand behind it belongs to {who}."));
                }
                else
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "Your informants found tracks — fresh ones — but the trail went cold in the lower quarter. "
                        + "Something is in motion against you. You could not stop it."));
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

    }
}
