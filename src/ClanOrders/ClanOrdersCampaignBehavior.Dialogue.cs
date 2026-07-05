// =============================================================================
// ASH AND EMBER — ClanOrders/ClanOrdersCampaignBehavior.Dialogue.cs
// Dialogue hooks and faction/target inquiry pickers for clan party orders.
// Partial of ClanOrdersCampaignBehavior (state lives in the base file).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class ClanOrdersCampaignBehavior
    {
        // ── Session launched ──────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RegisterDialogue(starter);
            try { ReassertAllOrders(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Dialogue lines ────────────────────────────────────────────────────
        // Priority 101 — fires just ahead of vanilla lord_pretalk lines so our
        // option appears at the top of the list without suppressing any others.
        private static void RegisterDialogue(CampaignGameStarter starter)
        {
            const int P = 101;

            // Entry — player opens order screen
            try
            {
                starter.AddPlayerLine(
                    "clanord_open", "lord_pretalk", "clanord_listen",
                    "I have orders for you.",
                    CondIsClanPartyLeader, CaptureOrderLeader, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // NPC acknowledgement
            try
            {
                starter.AddDialogLine(
                    "clanord_npc_listen", "clanord_listen", "clanord_choose",
                    "Your word is my road, {PLAYER_FIRST_NAME}. What do you need of me?",
                    null, null, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Branch: travel to a settlement
            try
            {
                starter.AddPlayerLine(
                    "clanord_travel", "clanord_choose", "close_window",
                    "Ride to a settlement — I need your presence there.",
                    null,
                    () =>
                    {
                        try { MageKnowledge._deferredInquiry = OpenTravelFactionFilter; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Branch: hunt a named lord
            try
            {
                starter.AddPlayerLine(
                    "clanord_hunt", "clanord_choose", "close_window",
                    "Hunt a lord for me. Do not return until it is done.",
                    null,
                    () =>
                    {
                        try { MageKnowledge._deferredInquiry = OpenHuntFactionFilter; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Branch: show / cancel current order
            try
            {
                starter.AddPlayerLine(
                    "clanord_status", "clanord_choose", "clanord_status_response",
                    "Report your current orders.",
                    CondHasOrder, null, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddDialogLine(
                    "clanord_status_npc", "clanord_status_response", "clanord_status_choice",
                    "{CLANORD_STATUS_TEXT}",
                    null, BuildStatusText, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddPlayerLine(
                    "clanord_cancel_order", "clanord_status_choice", "clanord_cancel_ack",
                    "Stand down. I am rescinding those orders.",
                    null, CancelCurrentOrder, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddDialogLine(
                    "clanord_cancel_npc", "clanord_cancel_ack", "lord_pretalk",
                    "Understood. We return to our own counsel until you call again.",
                    null, null, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddPlayerLine(
                    "clanord_status_keep", "clanord_status_choice", "lord_pretalk",
                    "Carry on.",
                    null, null, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Branch: nothing
            try
            {
                starter.AddPlayerLine(
                    "clanord_nothing", "clanord_choose", "lord_pretalk",
                    "Nothing for now. Carry on.",
                    null, null, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Conditions ────────────────────────────────────────────────────────
        private static bool CondIsClanPartyLeader()
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                if (h == null || !h.IsAlive || h == Hero.MainHero) return false;
                if (h.Clan != Hero.MainHero?.Clan) return false;
                var party = h.PartyBelongedTo;
                if (party == null || party == MobileParty.MainParty) return false;
                if (party.LeaderHero != h) return false;
                if (party.IsGarrison || party.IsCaravan) return false;
                return true;
            }
            catch { return false; }
        }

        private static bool CondHasOrder()
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                if (h?.PartyBelongedTo == null) return false;
                return HasOrder(h.PartyBelongedTo);
            }
            catch { return false; }
        }

        // ── Consequences ──────────────────────────────────────────────────────
        // Capture the leader's StringId while the conversation is still open.
        private static string _pendingLeaderHeroId;

        private static void CaptureOrderLeader()
        {
            try { _pendingLeaderHeroId = Hero.OneToOneConversationHero?.StringId; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // {PLAYER_FIRST_NAME} is not an engine-registered conversation token, so
            // set it explicitly for the acknowledgement line below.
            try
            {
                var first = Hero.MainHero?.FirstName ?? Hero.MainHero?.Name;
                if (first != null) MBTextManager.SetTextVariable("PLAYER_FIRST_NAME", first);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void BuildStatusText()
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                if (h?.PartyBelongedTo == null || !TryGetOrder(h.PartyBelongedTo, out string type, out string targetId))
                {
                    MBTextManager.SetTextVariable("CLANORD_STATUS_TEXT", "I have no active orders at this time.");
                    return;
                }

                string desc;
                if (type == "travel")
                {
                    var dest = Settlement.All.FirstOrDefault(s => s.StringId == targetId);
                    desc = dest != null
                        ? $"We are riding for {dest.Name}. The road is long, but we will reach it."
                        : "We were bound for a settlement — but it seems that destination no longer stands.";
                }
                else
                {
                    Hero target = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == targetId);
                    desc = target != null
                        ? $"We are hunting {target.Name}. We will find them."
                        : "Our quarry is already dead. The hunt is over.";
                }

                MBTextManager.SetTextVariable("CLANORD_STATUS_TEXT", desc);
            }
            catch
            {
                MBTextManager.SetTextVariable("CLANORD_STATUS_TEXT", "...");
            }
        }

        private static void CancelCurrentOrder()
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                if (h?.PartyBelongedTo != null)
                    ClearOrder(h.PartyBelongedTo);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Faction filter ────────────────────────────────────────────────────
        private static Kingdom _filterKingdom;

        private static void OpenTravelFactionFilter()
        {
            try
            {
                var factions = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && Settlement.All.Any(s => (s.IsTown || s.IsCastle) && s.OwnerClan?.Kingdom == k))
                    .OrderBy(k => k.Name?.ToString() ?? "")
                    .ToList();

                var elements = new List<InquiryElement>();
                foreach (var k in factions)
                {
                    int count = Settlement.All.Count(s => (s.IsTown || s.IsCastle) && s.OwnerClan?.Kingdom == k);
                    elements.Add(new InquiryElement(k.StringId, k.Name?.ToString() ?? "?", null, true,
                        $"{count} settlement{(count != 1 ? "s" : "")}"));
                }

                // Independent / no-kingdom settlements
                int indepCount = Settlement.All.Count(s => (s.IsTown || s.IsCastle) && s.OwnerClan?.Kingdom == null);
                if (indepCount > 0)
                    elements.Add(new InquiryElement("__none__", "Independent / No Kingdom", null, true,
                        $"{indepCount} settlement{(indepCount != 1 ? "s" : "")}"));

                if (elements.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("No settlements found."));
                    return;
                }

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        "Issue Travel Order — Choose Faction",
                        "Select the realm whose lands to ride to:",
                        elements, true, 1, 1, "Select", "Cancel",
                        chosen =>
                        {
                            try
                            {
                                if (chosen == null || chosen.Count == 0) return;
                                string kid = chosen[0].Identifier as string;
                                _filterKingdom = kid == "__none__" ? null
                                    : Kingdom.All.FirstOrDefault(k => k.StringId == kid);
                                OpenTravelTargetUI();
                            }
                            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }, null),
                    true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void OpenHuntFactionFilter()
        {
            try
            {
                var factions = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.Heroes.Any(h => h.IsLord && h.IsAlive && !h.IsChild
                                               && !h.IsPrisoner && h != Hero.MainHero))
                    .OrderBy(k => k.Name?.ToString() ?? "")
                    .ToList();

                var elements = new List<InquiryElement>();
                foreach (var k in factions)
                {
                    int count = k.Heroes.Count(h => h.IsLord && h.IsAlive && !h.IsChild
                                                 && !h.IsPrisoner && h != Hero.MainHero);
                    elements.Add(new InquiryElement(k.StringId, k.Name?.ToString() ?? "?", null, true,
                        $"{count} lord{(count != 1 ? "s" : "")}"));
                }

                int indepCount = Hero.AllAliveHeroes.Count(h => h.IsLord && h.IsAlive && !h.IsChild
                                                              && !h.IsPrisoner && h != Hero.MainHero
                                                              && h.Clan?.Kingdom == null);
                if (indepCount > 0)
                    elements.Add(new InquiryElement("__none__", "Independent / No Kingdom", null, true,
                        $"{indepCount} independent lord{(indepCount != 1 ? "s" : "")}"));

                if (elements.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("No lords found to hunt."));
                    return;
                }

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        "Issue Hunt Order — Choose Faction",
                        "Select the realm whose lord shall be hunted:",
                        elements, true, 1, 1, "Select", "Cancel",
                        chosen =>
                        {
                            try
                            {
                                if (chosen == null || chosen.Count == 0) return;
                                string kid = chosen[0].Identifier as string;
                                _filterKingdom = kid == "__none__" ? null
                                    : Kingdom.All.FirstOrDefault(k => k.StringId == kid);
                                OpenHuntTargetUI();
                            }
                            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }, null),
                    true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Settlement target picker ──────────────────────────────────────────
        private static void OpenTravelTargetUI()
        {
            try
            {
                var settlements = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle)
                             && (_filterKingdom == null
                                 ? s.OwnerClan?.Kingdom == null
                                 : s.OwnerClan?.Kingdom == _filterKingdom))
                    .OrderBy(s => s.Name?.ToString() ?? "")
                    .Take(60)
                    .ToList();

                if (settlements.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("No settlements found in that realm."));
                    return;
                }

                string factionLabel = _filterKingdom?.Name?.ToString() ?? "Independent";

                var elements = settlements.Select(s =>
                {
                    string label = $"{s.Name}  [{s.OwnerClan?.Name?.ToString() ?? "?"}]  ({(s.IsTown ? "Town" : "Castle")})";
                    string hint  = $"Owner: {s.OwnerClan?.Leader?.Name?.ToString() ?? "?"}";
                    return new InquiryElement(s.StringId, label, null, true, hint);
                }).ToList();

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"Ride to Settlement  ({factionLabel})",
                        "Choose the destination:",
                        elements, true, 1, 1, "Issue Order", "Back",
                        OnTravelTargetChosen, null),
                    true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void OnTravelTargetChosen(List<InquiryElement> selected)
        {
            try
            {
                if (selected == null || selected.Count == 0) return;
                Settlement dest = Settlement.All.FirstOrDefault(s => s.StringId == (selected[0].Identifier as string));
                if (dest == null) return;

                MobileParty party = FindOrderLeaderParty();
                if (party == null) return;

                SetOrder(party, "travel", dest.StringId);
                try { party.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"{party.LeaderHero?.Name} sets out for {dest.Name}."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Lord target picker ────────────────────────────────────────────────
        private static void OpenHuntTargetUI()
        {
            try
            {
                MobileParty orderedParty = FindOrderLeaderParty();
                float orderedStrength = 0f;
                try { orderedStrength = orderedParty?.GetTotalLandStrengthWithFollowers() ?? 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                var lords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsChild && !h.IsPrisoner
                             && h != Hero.MainHero
                             && (_filterKingdom == null
                                 ? h.Clan?.Kingdom == null
                                 : h.Clan?.Kingdom == _filterKingdom))
                    .OrderBy(h => h.Name?.ToString() ?? "")
                    .Take(60)
                    .ToList();

                if (lords.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("No lords found to hunt."));
                    return;
                }

                int leadershipPct = LeadershipSuccessChance(Hero.MainHero);

                var elements = lords.Select(h =>
                {
                    float targetStr = 0f;
                    try { targetStr = h.PartyBelongedTo?.GetTotalLandStrengthWithFollowers() ?? 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    bool risky = orderedStrength > 0f && targetStr > orderedStrength * 1.5f;

                    string label = $"{h.Name}  [{h.Clan?.Name}]{(risky ? "  [RISKY]" : "")}";
                    string hint  = risky
                        ? $"Quarry strength: {(int)targetStr}  |  Your party: {(int)orderedStrength}  |  Leadership roll: {leadershipPct}%"
                        : $"Quarry strength: {(int)targetStr}  |  Your party: {(int)orderedStrength}";
                    return new InquiryElement(h.StringId, label, null, true, hint);
                }).ToList();

                string factionLabel = _filterKingdom?.Name?.ToString() ?? "Independent";

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"Hunt a Lord  ({factionLabel})",
                        "Name your quarry. Orders marked [RISKY] require a Leadership roll.",
                        elements, true, 1, 1, "Issue Order", "Back",
                        OnHuntTargetChosen, null),
                    true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void OnHuntTargetChosen(List<InquiryElement> selected)
        {
            try
            {
                if (selected == null || selected.Count == 0) return;
                Hero target = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == (selected[0].Identifier as string));
                if (target == null) return;

                MobileParty party = FindOrderLeaderParty();
                if (party == null) return;

                float orderedStr = 0f;
                float targetStr  = 0f;
                try { orderedStr = party?.GetTotalLandStrengthWithFollowers() ?? 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { targetStr  = target.PartyBelongedTo?.GetTotalLandStrengthWithFollowers() ?? 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                bool isRisky = orderedStr > 0f && targetStr > orderedStr * 1.5f;

                if (isRisky)
                {
                    int pct    = LeadershipSuccessChance(Hero.MainHero);
                    bool pass  = _rng.Next(100) < pct;
                    string leader = party.LeaderHero?.Name?.ToString() ?? "Your captain";

                    if (!pass)
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"{leader} weighs the odds and refuses: the quarry is too strong."));
                        return;
                    }

                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"{leader} nods grimly. \"We will find a way, my lord.\""));
                }

                // Direct toward quarry's last known position
                Settlement dest = null;
                try
                {
                    dest = target.CurrentSettlement
                        ?? target.PartyBelongedTo?.CurrentSettlement
                        ?? target.HomeSettlement;
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                SetOrder(party, "hunt", target.StringId);
                if (dest != null)
                    try { party.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"{party.LeaderHero?.Name} rides to hunt {target.Name}."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Helper ────────────────────────────────────────────────────────────
        private static MobileParty FindOrderLeaderParty()
        {
            if (string.IsNullOrEmpty(_pendingLeaderHeroId)) return null;
            Hero h = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == _pendingLeaderHeroId);
            return h?.PartyBelongedTo;
        }
    }
}
