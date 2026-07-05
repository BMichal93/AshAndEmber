// =============================================================================
// ASH AND EMBER — Miracles/MiracleCampaignBehavior.cs
//
// Serializes the Grace counter (MIRACLE_Grace save key) and drives daily NPC
// Grace miracle use on the campaign map. Grace lords with strong virtue simulate
// Radiant Mending or Guidance on their parties. Dark lords now use the Dark
// Gift system instead — Cold miracle NPC AI has been retired.
//
// MIRACLE_Cold is read as a dummy on load for backward compatibility with saves
// that pre-date the removal of player Cold (altars now grant Dark Gifts).
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public class MiracleCampaignBehavior : CampaignBehaviorBase
    {
        private static readonly Random _rng = new Random();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try { store.SyncData("MIRACLE_Grace", ref MiracleInventory._grace); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { int dummy = 0; store.SyncData("MIRACLE_Cold", ref dummy); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } // Cold removed
            try { MiracleTalents.Save(store); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void ResetForNewGame()
        {
            MiracleInventory.ResetForNewGame();
            MiracleTalents.ResetForNewGame();
        }

        private void OnDailyTick()
        {
            if (Campaign.Current == null) return;
            try
            {
                foreach (var hero in Hero.AllAliveHeroes.ToList())
                {
                    if (hero == Hero.MainHero || hero.PartyBelongedTo == null) continue;

                    int honor = 0, mercy = 0, generosity = 0;
                    try
                    {
                        honor      = hero.GetTraitLevel(DefaultTraits.Honor);
                        mercy      = hero.GetTraitLevel(DefaultTraits.Mercy);
                        generosity = hero.GetTraitLevel(DefaultTraits.Generosity);
                    }
                    catch { continue; }

                    int sum = honor + mercy + generosity;
                    bool isPriest = IsPriest(hero);
                    double chance = MiracleMath.NpcDailyUseChance(isPriest);

                    if (sum >= 3 && _rng.NextDouble() < chance)
                    {
                        // Grace miracle on campaign
                        MiracleType type = GraceCampaignChoice(hero);
                        try
                        {
                            string result = MiracleEffects.ApplyCampaignMiracle(hero, hero.PartyBelongedTo, type);
                            if (!string.IsNullOrEmpty(result) && _rng.NextDouble() < 0.20)
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{hero.Name} — {result}", new Color(0.90f, 0.78f, 0.35f)));
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static MiracleType GraceCampaignChoice(Hero hero)
        {
            try
            {
                if (hero.PartyBelongedTo?.MemberRoster != null)
                {
                    int wounded = hero.PartyBelongedTo.MemberRoster.GetTroopRoster()
                        .Sum(e => e.WoundedNumber);
                    if (wounded > 5) return MiracleType.MercyRelief;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return MiracleType.ValorMarch;
        }

        private static bool IsPriest(Hero hero)
        {
            try
            {
                string name = hero.CharacterObject?.Name?.ToString() ?? hero.Name?.ToString() ?? "";
                return name.IndexOf("Priest", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }
    }
}
