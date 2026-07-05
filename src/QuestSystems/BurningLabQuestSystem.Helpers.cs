// =============================================================================
// ASH AND EMBER — BurningLabQuestSystem.Helpers.cs
// Option builders, lookups, settlement/clan stabilisation, small helpers.
// Partial of BurningLabQuestSystem (shared state lives in BurningLabQuestSystem.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class BurningLabQuestSystem
    {
        // ── Helpers ────────────────────────────────────────────────────────────

        private static void AddImperialOption(List<InquiryElement> list, string empireId,
            string choiceId, string label, string hint)
        {
            Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == empireId && !x.IsEliminated);
            if (k == null) return;
            Hero leader = k.Leader;
            if (leader == null || !leader.IsAlive || leader.IsChild) return;
            list.Add(new InquiryElement(choiceId, label, null, true, hint));
        }

        private static void AddFactionOption(List<InquiryElement> list, string factionId,
            string choiceId, string label, string hint)
        {
            Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == factionId && !x.IsEliminated);
            if (k == null) return;
            list.Add(new InquiryElement(choiceId, label, null, true, hint));
        }

        private static string PickLivingImperialEmpireId()
        {
            var available = EmpireIds.Where(id =>
            {
                Kingdom k = GetKingdom(id);
                return k != null && !k.IsEliminated && k.Leader != null && k.Leader.IsAlive;
            }).ToList();
            return available.Count > 0 ? available[_rng.Next(available.Count)] : null;
        }

        private static Hero FindArenicosHero()
        {
            if (_arenicosHeroId == null) return null;
            return Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _arenicosHeroId);
        }

        private static Kingdom GetKingdom(string id)
        {
            if (id == null) return null;
            return Kingdom.All.FirstOrDefault(k => k.StringId == id);
        }

        private static void StabiliseSettlement(Settlement s)
        {
            if (s?.Town == null) return;
            try { s.Town.Loyalty  = 100f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { s.Town.Security = 100f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Moves a clan into targetKingdom carrying its fiefs intact. Prefers the
        // atomic defection action when the clan already belongs to a kingdom, so
        // settlements change banners cleanly instead of passing through an
        // ownerless "independent" state that can strand them as rebel/free cities.
        // The player is NEVER moved automatically — serving Arenicos (or refusing)
        // is the player's own choice. Newly held fiefs are stabilised so the
        // reshuffle cannot tip them straight into rebellion.
        private static void MoveClanInto(Clan clan, Kingdom targetKingdom)
        {
            if (clan == null || clan.IsEliminated) return;
            if (targetKingdom == null || targetKingdom.IsEliminated) return;
            if (clan == Clan.PlayerClan) return;        // the player is never moved automatically
            if (clan.Kingdom == targetKingdom) return;  // already there

            var stay    = CampaignTime.Now + CampaignTime.Years(1000);
            Kingdom old = clan.Kingdom;
            try
            {
                if (targetKingdom.RulingClan == null)
                {
                    // No ruler to defect to — seed the kingdom with this clan instead.
                    if (old != null && !old.IsEliminated)
                        try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    ChangeKingdomAction.ApplyByCreateKingdom(clan, targetKingdom, false);
                }
                else if (old != null && !old.IsEliminated)
                {
                    ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, old, targetKingdom, stay, false);
                }
                else
                {
                    ChangeKingdomAction.ApplyByJoinToKingdom(clan, targetKingdom, stay, false);
                }
            }
            catch
            {
                // Last-resort fallback to the legacy two-step path.
                try { if (clan.Kingdom != null) ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { ChangeKingdomAction.ApplyByJoinToKingdom(clan, targetKingdom, stay, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            StabiliseClanFiefs(clan);
        }

        // Resets loyalty/security on every town a clan holds so a wave of kingdom
        // changes does not push its fiefs into rebellion.
        private static void StabiliseClanFiefs(Clan clan)
        {
            if (clan == null) return;
            try { foreach (var s in clan.Settlements.ToList()) StabiliseSettlement(s); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void Notify(string text)
        {
            try { MBInformationManager.AddQuickInformation(new TextObject(text)); }
            catch
            {
                try { InformationManager.DisplayMessage(new InformationMessage(text,
                    new Color(0.80f, 0.65f, 0.30f))); }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        private static void GainGold(int amount)
        {
            try { Hero.MainHero?.ChangeHeroGold(amount); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            Notify($"+{amount} gold.");
        }

        private static void ShiftHonour(int delta)
        {
            try
            {
                Hero h = Hero.MainHero;
                if (h == null) return;
                int v = h.GetTraitLevel(DefaultTraits.Honor);
                h.SetTraitLevel(DefaultTraits.Honor, Math.Max(-2, Math.Min(2, v + delta)));
                string sign = delta >= 0 ? "+" : "";
                Notify($"(Honour {sign}{delta})");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static CharacterObject GetTier4Troop(Kingdom kingdom)
        {
            try
            {
                var culture = kingdom?.Culture;
                CharacterObject found = null;
                if (culture != null)
                    found = CharacterObject.All.FirstOrDefault(c =>
                        !c.IsHero && c.Tier == 4 && c.Culture == culture);
                return found ?? CharacterObject.All.FirstOrDefault(c => !c.IsHero && c.Tier == 4);
            }
            catch { return null; }
        }

        private static double ElapsedCampaignDays()
        {
            try { return CampaignMapEvents.ElapsedCampaignDays(); }
            catch { return 0.0; }
        }
    }
}
