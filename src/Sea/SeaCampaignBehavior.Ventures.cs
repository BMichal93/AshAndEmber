// =============================================================================
// ASH AND EMBER — Sea/SeaCampaignBehavior.Ventures.cs
// Trade ventures: funding, launch, and daily resolution.
// Partial of SeaCampaignBehavior (shared static state lives in SeaCampaignBehavior.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class SeaCampaignBehavior
    {
        // =====================================================================
        // SEA TRADE
        // =====================================================================
        private static void StartVenture(Settlement dest)
        {
            try
            {
                var here = Settlement.CurrentSettlement;
                if (!IsPort(here) || !IsPort(dest) || here == dest) return;
                if (_ventures.Count >= SeaMath.MaxActiveVentures) return;

                float dist = PortDistance(here, dest);
                var options = new List<InquiryElement>();
                foreach (int tier in SeaMath.VentureTiers)
                {
                    bool can = Hero.MainHero.Gold >= tier;
                    var safe = SeaMath.ResolveVenture(tier, dist, false, 1.0, 0.5);
                    options.Add(new InquiryElement(tier, $"{tier} denars", null, can,
                        can ? $"A typical run returns around {safe.Payout} denars in {SeaMath.VentureDays(dist)} days — if the sea allows."
                            : "Your purse cannot cover this stake."));
                }

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    $"Venture to {dest.Name}",
                    "Your factor needs a stake to fill the hold. The bigger the cargo, the bigger the return — and the bigger the prize floating out there with your name on it.",
                    options, true, 1, 1, "Invest", "Never mind",
                    chosen =>
                    {
                        if (!(chosen?[0]?.Identifier is int invested)) return;
                        if (Hero.MainHero.Gold < invested) return;

                        if (MageKnowledge.IsMage)
                        {
                            // Deferred a tick: shown straight from this callback the
                            // bless prompt is swallowed while the stake picker's layer
                            // is still closing (same trap as the destination picker).
                            MageKnowledge._deferredInquiry = () =>
                            InformationManager.ShowInquiry(new InquiryData(
                                "Bless the Cargo?",
                                $"For {SeaMath.BlessVentureAgingDays} day of aging you can breathe a ward into the hold — " +
                                "corsairs and weather find the ship half as often, and the goods arrive warm and wanted.",
                                true, true, "Bless it", "Let it sail bare",
                                () => LaunchVenture(dest, dist, invested, blessed: true),
                                () => LaunchVenture(dest, dist, invested, blessed: false)), true);
                        }
                        else
                        {
                            LaunchVenture(dest, dist, invested, blessed: false);
                        }
                    },
                    null, "", false), false, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void LaunchVenture(Settlement dest, float dist, int invested, bool blessed)
        {
            try
            {
                if (Hero.MainHero.Gold < invested) return;
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, invested, true);
                if (blessed)
                    try { AgingSystem.AgeHero(Hero.MainHero, SeaMath.BlessVentureAgingDays); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                _ventures.Add(new Venture
                {
                    DestName = dest.Name?.ToString() ?? "a far port",
                    DaysLeft = SeaMath.VentureDays(dist),
                    Invested = invested,
                    Blessed  = blessed,
                    Distance = dist,
                });
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"The cog warps out on the evening tide, {invested} denars of cargo in her hold" +
                    (blessed ? ", a faint warmth clinging to her timbers." : ".")));
                try { GameMenu.SwitchToMenu("sea_harbor"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void TickVentures()
        {
            if (_ventures.Count == 0) return;
            for (int i = _ventures.Count - 1; i >= 0; i--)
            {
                var v = _ventures[i];
                v.DaysLeft--;
                if (v.DaysLeft > 0) continue;
                _ventures.RemoveAt(i);

                var outcome = SeaMath.ResolveVenture(v.Invested, v.Distance, v.Blessed,
                                                     _rng.NextDouble(), _rng.NextDouble());
                if (outcome.Payout > 0)
                    try { GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, outcome.Payout, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                if (outcome.Lost)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Word from the coast: your venture to {v.DestName} is lost — corsairs, or the sea itself. " +
                        $"The salvagers return {outcome.Payout} denars of your {v.Invested}.",
                        new Color(0.85f, 0.45f, 0.35f)));
                }
                else
                {
                    int profit = outcome.Payout - v.Invested;
                    try { Hero.MainHero.AddSkillXp(DefaultSkills.Trade, Math.Max(10, profit / 10)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Your factor returns from {v.DestName}: {outcome.Payout} denars back on {v.Invested} invested" +
                        $" ({(profit >= 0 ? "+" : "")}{profit}).",
                        new Color(0.55f, 0.8f, 0.45f)));
                }
            }
        }
    }
}
