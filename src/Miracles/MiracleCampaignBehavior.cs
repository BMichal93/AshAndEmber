// =============================================================================
// ASH AND EMBER — Miracles/MiracleCampaignBehavior.cs
//
// Serializes Grace/Cold counters (MIRACLE_Grace / MIRACLE_Cold save keys) and
// drives daily NPC miracle use on the campaign map. Grace lords with strong
// virtue simulate Radiant Mending or Guidance on their parties; Cold lords
// simulate Dreadmending or Dread Presence on nearby enemies.
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
            try { store.SyncData("MIRACLE_Grace", ref MiracleInventory._grace); } catch { }
            try { store.SyncData("MIRACLE_Cold",  ref MiracleInventory._cold);  } catch { }
        }

        public static void ResetForNewGame() => MiracleInventory.ResetForNewGame();

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
                        catch { }
                    }
                    else if (sum <= -3 && _rng.NextDouble() < chance)
                    {
                        // Cold miracle on campaign
                        MiracleType type = ColdCampaignChoice(hero);
                        try
                        {
                            string result = MiracleEffects.ApplyCampaignMiracle(hero, hero.PartyBelongedTo, type);
                            if (!string.IsNullOrEmpty(result) && _rng.NextDouble() < 0.20)
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{hero.Name} — {result}", new Color(0.35f, 0.50f, 0.85f)));
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static MiracleType GraceCampaignChoice(Hero hero)
        {
            try
            {
                if (hero.PartyBelongedTo?.MemberRoster != null)
                {
                    int wounded = hero.PartyBelongedTo.MemberRoster.GetTroopRoster()
                        .Sum(e => e.WoundedNumber);
                    if (wounded > 5) return MiracleType.RadiantMending;
                }
            }
            catch { }
            return MiracleType.LightOfGuidance;
        }

        private static MiracleType ColdCampaignChoice(Hero hero)
        {
            return _rng.Next(2) == 0 ? MiracleType.DreadPresence : MiracleType.Dreadmending;
        }

        private static bool IsPriest(Hero hero)
        {
            try
            {
                string name = hero.CharacterObject?.Name?.ToString() ?? hero.Name?.ToString() ?? "";
                return name.StartsWith("Priest", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
