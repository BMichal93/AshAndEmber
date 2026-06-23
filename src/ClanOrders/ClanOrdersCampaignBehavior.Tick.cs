// =============================================================================
// ASH AND EMBER — ClanOrders/ClanOrdersCampaignBehavior.Tick.cs
// Daily AI: keep ordered parties on task; resolve hunt confrontations.
// Partial of ClanOrdersCampaignBehavior (state lives in the base file).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class ClanOrdersCampaignBehavior
    {
        // Map-unit distance within which a hunt party can attempt an abstract kill.
        private const float HuntEngageRange = 15f;

        // ── Daily tick ────────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (Campaign.Current == null) return;
            try { TickAllOrders(); } catch { }
        }

        // ── Hero killed ───────────────────────────────────────────────────────
        // Catches kills from any source (natural warfare, schemes, etc.) so hunts
        // that resolve through map combat clean themselves up automatically.
        private void OnHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            try
            {
                if (victim == null) return;
                for (int i = _orderPartyIds.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (_orderTypes[i] != "hunt" || _orderTargetIds[i] != victim.StringId) continue;

                        var party = MobileParty.All.FirstOrDefault(p => p.StringId == _orderPartyIds[i]);
                        string leader = party?.LeaderHero?.Name?.ToString() ?? "Your party";

                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"{leader} reports: {victim.Name} is dead. The hunt is complete."));

                        RemoveOrderAt(i);
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ── Order processing ──────────────────────────────────────────────────
        private static void TickAllOrders()
        {
            for (int i = _orderPartyIds.Count - 1; i >= 0; i--)
            {
                try
                {
                    var party = MobileParty.All.FirstOrDefault(p => p.StringId == _orderPartyIds[i]);
                    if (party == null || !party.IsActive)
                    {
                        RemoveOrderAt(i);
                        continue;
                    }

                    if (_orderTypes[i] == "travel") TickTravel(i, party, _orderTargetIds[i]);
                    else if (_orderTypes[i] == "hunt") TickHunt(i, party, _orderTargetIds[i]);
                }
                catch { }
            }
        }

        // ── Travel ────────────────────────────────────────────────────────────
        private static void TickTravel(int idx, MobileParty party, string settlementId)
        {
            try
            {
                var dest = Settlement.All.FirstOrDefault(s => s.StringId == settlementId);
                if (dest == null) { RemoveOrderAt(idx); return; }

                // Arrived?
                if (party.CurrentSettlement == dest)
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"{party.LeaderHero?.Name} has arrived at {dest.Name}."));
                    RemoveOrderAt(idx);
                    return;
                }

                // Re-apply movement each day to keep the party on course.
                try { party.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); } catch { }
            }
            catch { }
        }

        // ── Hunt ──────────────────────────────────────────────────────────────
        private static void TickHunt(int idx, MobileParty party, string heroId)
        {
            try
            {
                Hero target = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId);
                if (target == null)
                {
                    // Target already dead — clear without a message (OnHeroKilled handles it normally).
                    RemoveOrderAt(idx);
                    return;
                }

                var targetParty = target.PartyBelongedTo;

                // Steer toward quarry's last known position.
                Settlement dest = null;
                try
                {
                    dest = target.CurrentSettlement
                        ?? targetParty?.CurrentSettlement
                        ?? target.HomeSettlement;
                }
                catch { }

                if (dest != null)
                    try { party.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); } catch { }

                // If already at war, natural map combat handles the engagement — nothing extra needed.
                bool atWar = false;
                try
                {
                    atWar = party.MapFaction != null && targetParty?.MapFaction != null
                         && FactionManager.IsAtWarAgainstFaction(party.MapFaction, targetParty.MapFaction);
                }
                catch { }

                if (atWar || targetParty == null) return;

                // Not at war: check proximity for abstract assassination.
                float distSq = float.MaxValue;
                try { distSq = (party.GetPosition2D - targetParty.GetPosition2D).LengthSquared; } catch { }

                if (distSq <= HuntEngageRange * HuntEngageRange)
                    TryQueueAssassination(party, target);
            }
            catch { }
        }

        // ── Abstract assassination inquiry ────────────────────────────────────
        // Queued through _deferredInquiry so it never opens mid-dialogue or when
        // another popup is already visible.
        private static void TryQueueAssassination(MobileParty party, Hero target)
        {
            try
            {
                if (MageKnowledge._deferredInquiry != null) return; // another event is pending

                float orderedStr = 0f;
                float targetStr  = 0f;
                try { orderedStr = party.Party?.TotalStrength ?? 0f; } catch { }
                try { targetStr  = target.PartyBelongedTo?.Party?.TotalStrength ?? 0f; } catch { }

                float ratio      = orderedStr > 0f ? orderedStr / Math.Max(1f, targetStr) : 0.5f;
                int   successPct = (int)Math.Min(85f, Math.Max(10f, ratio * 65f));

                string partyId   = party.StringId;
                string targetId  = target.StringId;
                string leaderName = party.LeaderHero?.Name?.ToString() ?? "Your captain";
                string targetName = target.Name?.ToString() ?? "the quarry";

                MageKnowledge._deferredInquiry = () =>
                {
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "The Quarry is Within Reach",
                            $"{leaderName}'s warband has cornered {targetName} away from open battle.\n\n"
                            + $"Your party: {(int)orderedStr} strength  |  Quarry: {(int)targetStr} strength\n"
                            + $"Odds of success: {successPct}%\n\n"
                            + "Strike now while you have the advantage — or pull back and await a better moment?",
                            true, true, "Strike Now", "Fall Back",
                            () => ResolveAssassination(partyId, targetId, successPct),
                            () =>
                            {
                                try
                                {
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"{leaderName} withdraws into shadow. The hunt continues."));
                                }
                                catch { }
                            }),
                        true);
                    }
                    catch { }
                };
            }
            catch { }
        }

        private static void ResolveAssassination(string partyId, string targetId, int successPct)
        {
            try
            {
                var   party   = MobileParty.All.FirstOrDefault(p => p.StringId == partyId);
                Hero  target  = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == targetId);
                string leader = party?.LeaderHero?.Name?.ToString() ?? "Your party";

                if (target == null)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("The quarry is already gone."));
                    return;
                }

                bool success = _rng.Next(100) < successPct;

                if (success)
                {
                    if (party != null) ApplyCasualties(party, 0.15f);
                    // OnHeroKilled fires from within ApplyByMurder and handles the
                    // notification + order cleanup — no extra message needed here.
                    try { KillCharacterAction.ApplyByMurder(target, party?.LeaderHero, false); } catch { }
                }
                else
                {
                    if (party != null)
                    {
                        ApplyCasualties(party, 0.30f);

                        // Pull back to a friendly settlement
                        var rally = Hero.MainHero?.HomeSettlement
                                 ?? Hero.MainHero?.Clan?.Settlements.FirstOrDefault();
                        if (rally != null)
                            try { party.SetMoveGoToSettlement(rally, MobileParty.NavigationType.Default, false); } catch { }
                    }

                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"{leader} failed — {target.Name} escaped. Your men took losses and are falling back."));
                }
            }
            catch { }
        }

        // ── Casualties helper ─────────────────────────────────────────────────
        private static void ApplyCasualties(MobileParty party, float fraction)
        {
            try
            {
                var roster = party.MemberRoster;
                if (roster == null) return;
                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    try
                    {
                        if (e.Character?.IsHero == true) continue; // never kill named heroes
                        int casualties = Math.Max(0, (int)(e.Number * fraction));
                        if (casualties > 0)
                            roster.AddToCounts(e.Character, -casualties);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
