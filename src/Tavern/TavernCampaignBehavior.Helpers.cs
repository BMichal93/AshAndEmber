// =============================================================================
// ASH AND EMBER — TavernCampaignBehavior.Helpers.cs
// Recruit/lord lookups and small relation/morale/crime helpers.
// Partial of TavernCampaignBehavior (shared static state lives in TavernCampaignBehavior.cs).
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
    public partial class TavernCampaignBehavior
    {
        // =====================================================================
        // HELPERS
        // =====================================================================

        private static CharacterObject GetRecruitOfTier(int targetTier)
        {
            try
            {
                var culture = Settlement.CurrentSettlement?.Culture
                           ?? Hero.MainHero?.Culture;
                CharacterObject troop = culture?.BasicTroop;
                if (troop == null) return null;

                // Walk the upgrade tree up to the target tier
                for (int i = 0; i < targetTier - 1 && troop != null; i++)
                {
                    if (troop.UpgradeTargets != null && troop.UpgradeTargets.Length > 0)
                        troop = troop.UpgradeTargets[0];
                    else
                        break;
                }
                return troop;
            }
            catch { return null; }
        }

        private static Hero GetLordInSettlement()
        {
            try
            {
                var s = Settlement.CurrentSettlement;
                if (s == null) return null;
                return Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero
                             && !h.IsPrisoner && h.CurrentSettlement == s)
                    .OrderBy(_ => _rng.Next())
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        private static void ChangeRelWithRandomHero(int delta)
        {
            try
            {
                var heroes = Hero.AllAliveHeroes
                    .Where(h => h.IsAlive && h != Hero.MainHero && !h.IsPrisoner && h.IsLord)
                    .ToList();
                if (heroes.Count == 0) return;
                var target = heroes[_rng.Next(heroes.Count)];
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, target, delta, false);
                string sign = delta >= 0 ? "+" : "";
                Msg($"(Relation with {target.Name}: {sign}{delta})", delta >= 0 ? GoodColor : BadColor);
            }
            catch { }
        }

        private static void ChangeCrime(float amount)
        {
            try
            {
                var kingdom = Hero.MainHero?.MapFaction as Kingdom;
                if (kingdom != null)
                    ChangeCrimeRatingAction.Apply(kingdom, amount, true);
            }
            catch { }
        }

        private static void AddMorale(float delta)
        {
            try { if (MobileParty.MainParty != null) MobileParty.MainParty.RecentEventsMorale += delta; } catch { }
        }

        private static void Msg(string text, Color c)
        {
            try { MBInformationManager.AddQuickInformation(new TextObject(text)); }
            catch { try { InformationManager.DisplayMessage(new InformationMessage(text, c)); } catch { } }
        }
    }
}
