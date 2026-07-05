// =============================================================================
// ASH AND EMBER — SettlementEncounters.Helpers.cs
// General encounter helpers.
// Partial of SettlementEncounters (shared state lives in SettlementEncounters.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class SettlementEncounters
    {
        // ── Helpers ───────────────────────────────────────────────────────────
        private static void Msg(string text, Color c)
        {
            try { MBInformationManager.AddQuickInformation(new TextObject(text)); }
            catch { try { InformationManager.DisplayMessage(new InformationMessage(text, c)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }
        }

        private static void AddMorale(float delta)
        {
            try { if (MobileParty.MainParty != null) MobileParty.MainParty.RecentEventsMorale += delta; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void AgePlayer(int days)
        {
            try { AgingSystem.AgeHero(Hero.MainHero, days); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ShiftTrait(TraitObject trait, int delta)
        {
            try
            {
                Hero h = Hero.MainHero;
                if (h == null) return;
                int v = h.GetTraitLevel(trait);
                h.SetTraitLevel(trait, Math.Min(2, Math.Max(-2, v + delta)));
                string sign = delta >= 0 ? "+" : "";
                Msg($"({trait.Name} {sign}{delta})", delta >= 0 ? GoodColor : DimColor);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static bool ChangeGold(int amount)
        {
            if (amount < 0 && (Hero.MainHero?.Gold ?? 0) < -amount)
            {
                Msg($"Not enough gold. (Need {-amount}, have {Hero.MainHero?.Gold ?? 0})", BadColor);
                return false;
            }
            try { Hero.MainHero?.ChangeHeroGold(amount); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return true;
        }

        private static void ChangeRenown(float amount)
        {
            try
            {
                if (Hero.MainHero?.Clan != null)
                {
                    Hero.MainHero.Clan.Renown = Math.Max(0f, Hero.MainHero.Clan.Renown + amount);
                    string sign = amount >= 0 ? "+" : "";
                    Msg($"({sign}{amount:F0} renown)", GoodColor);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ChangeCrime(float amount)
        {
            try
            {
                var kingdom = Hero.MainHero?.MapFaction as Kingdom;
                if (kingdom != null)
                    ChangeCrimeRatingAction.Apply(kingdom, amount, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ChangeRelWithOwner(Settlement s, int delta)
        {
            try
            {
                Hero owner = s.OwnerClan?.Leader;
                if (owner != null && owner != Hero.MainHero)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, owner, delta, false);
                    string sign = delta >= 0 ? "+" : "";
                    Msg($"(Relation with {owner.Name}: {sign}{delta})", delta >= 0 ? GoodColor : BadColor);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ChangeRelWithRandomLord(int delta)
        {
            try
            {
                var lords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero && !h.IsPrisoner)
                    .ToList();
                if (lords.Count == 0) return;
                var lord = lords[_rng.Next(lords.Count)];
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, delta, false);
                string sign = delta >= 0 ? "+" : "";
                Msg($"(Relation with {lord.Name}: {sign}{delta})", delta >= 0 ? GoodColor : BadColor);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static string GoldStr(int amount)
            => amount >= 0 ? $"+{amount} gold" : $"{amount} gold";

    }
}
