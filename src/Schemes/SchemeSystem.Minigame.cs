// =============================================================================
// ASH AND EMBER — SchemeSystem.Minigame.cs
// Minigame-facing cooldown/outcome/break-consequence API.
// Partial of SchemeSystem (shared state lives in SchemeSystem.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    internal static partial class SchemeSystem
    {
        // ── Minigame API ──────────────────────────────────────────────────────
        // Called by SchemeMinigame after a player operation resolves.

        /// Stamps only the per-target cooldown (no global). Called from CommitScheme
        /// BEFORE the deferred minigame launches so a save-reload between cost deduction
        /// and the first phase cannot bypass the cooldown and let the player retry for free.
        internal static void PreStampTargetCooldown(SchemeType type, Hero targetHero, Settlement targetSett)
        {
            string targetId = targetHero?.StringId ?? targetSett?.StringId ?? "";
            if (string.IsNullOrEmpty(targetId)) return;
            string cdKey = CooldownKey(type, targetId);
            _targetCooldowns[cdKey] = type == SchemeType.Assassinate ? 14 : 7;
            _playerCooldownKeys.Add(cdKey);
        }

        /// Stamps the per-target cooldown and briefly disables the scheme menu.
        /// Called by SchemeMinigame on extract, bust, or abort. Overwrites the pre-stamp
        /// from PreStampTargetCooldown with the outcome-correct value.
        internal static void SetPlayerCooldown(SchemeType type, Hero targetHero,
            Settlement targetSett, int days = -1)
        {
            // Every minigame terminal path (extract, bust, abort, rounds exhausted)
            // ends here — the committed operation is resolved.
            ClearPendingPlayerOperation();

            string targetId = targetHero?.StringId ?? targetSett?.StringId ?? "";
            if (string.IsNullOrEmpty(targetId)) return;
            string cdKey = CooldownKey(type, targetId);
            int cdDays = days >= 0 ? days : (type == SchemeType.Assassinate ? 14 : 7);
            _targetCooldowns[cdKey] = cdDays;
            _playerCooldownKeys.Add(cdKey);
            // Brief global cooldown prevents immediate menu re-entry after resolution.
            // Overwrite directly — the minigame calls this at the end of every path,
            // so the final call should always win (don't clamp to the pre-stamp value).
            _playerGlobalCooldown = days >= 0 ? days : 3;
        }

        /// Applies the minigame outcome for a player operation.
        /// Routes to ApplySuccess, a silent-retreat notification, or ApplyBreakConsequence.
        internal static void ApplyPlayerSchemeOutcome(SchemeType type, Hero instigator,
            Hero targetHero, Settlement targetSett, SchemeOutcome outcome)
        {
            if (instigator == null) return;

            var fake = new PendingScheme
            {
                InstigatorId       = instigator.StringId ?? "",
                Type               = type,
                TargetHeroId       = targetHero?.StringId ?? "",
                TargetSettlementId = targetSett?.StringId  ?? "",
                DaysRemaining      = 0,
                IsPlayer           = true
            };

            switch (outcome)
            {
                case SchemeOutcome.SmallLoss:
                {
                    string tName = targetHero?.Name?.ToString()
                                ?? targetSett?.Name?.ToString() ?? "the target";
                    MBInformationManager.AddQuickInformation(
                        new TextObject($"Your agent withdrew. The operation against {tName} dissolved without trace."));
                    break;
                }
                case SchemeOutcome.Success:
                    try { ApplySuccess(fake, instigator, targetHero, targetSett); } catch { }
                    break;

                case SchemeOutcome.Bust:
                    try { ApplyBreakConsequence(type, instigator, targetHero, targetSett); } catch { }
                    break;
            }

            // Award full skill XP on success; partial (1/3) on bust.
            try
            {
                var def = GetDefinition(type);
                if (def?.Skill != null && def.SkillXp > 0)
                {
                    int xp = outcome == SchemeOutcome.Success ? def.SkillXp
                           : outcome == SchemeOutcome.Bust    ? def.SkillXp / 3
                           : 0;
                    if (xp > 0)
                        instigator?.HeroDeveloper?.AddSkillXp(def.Skill, xp);
                }
            }
            catch { }
        }

        /// Per-scheme bust consequences — called directly from the minigame (bust = always caught).
        internal static void ApplyBreakConsequence(SchemeType type, Hero instigator,
            Hero targetHero, Settlement targetSett)
        {
            if (instigator == null) return;

            switch (type)
            {
                case SchemeType.Assassinate:
                {
                    var targetKingdom = targetHero?.Clan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 80f, false); } catch { }
                    if (targetHero != null && targetHero.IsAlive && targetHero != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, -80, false); } catch { }
                    var instigKingdom = instigator.Clan?.Kingdom;
                    if (_rng.NextDouble() < 0.60
                        && instigKingdom != null && targetKingdom != null
                        && instigKingdom != targetKingdom
                        && !instigKingdom.IsEliminated && !targetKingdom.IsEliminated
                        && !instigKingdom.IsAtWarWith(targetKingdom))
                        try { DeclareWarAction.ApplyByDefault(instigKingdom, targetKingdom); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — Your assassin was taken alive. Under interrogation, they spoke your name. War may follow."));
                    break;
                }
                case SchemeType.ForgeDocuments:
                {
                    Hero ownLeader = instigator.Clan?.Kingdom?.Leader;
                    if (ownLeader != null && ownLeader.IsAlive && ownLeader != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, ownLeader, -60, false); } catch { }
                    var targetKingdom = targetHero?.Clan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 40f, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The forgery unraveled and traced back to you. Your own lord is asking questions."));
                    break;
                }
                case SchemeType.FalseAccusations:
                {
                    if (instigator.Clan != null)
                    {
                        float loss = Math.Max(80f, instigator.Clan.Renown * 0.10f);
                        try { instigator.Clan.Renown = Math.Max(0f, instigator.Clan.Renown - loss); } catch { }
                    }
                    if (targetHero != null && targetHero.IsAlive)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, -60, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The slander was too clumsy. It circled back. Your own reputation is now in question."));
                    break;
                }
                case SchemeType.StageCoup:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 50f, false); } catch { }
                    var own = Settlement.All.FirstOrDefault(s =>
                        (s.IsTown || s.IsCastle) && s.OwnerClan == instigator.Clan && s.Town != null);
                    if (own?.Town != null)
                    {
                        try { own.Town.Loyalty  = Math.Max(0f, own.Town.Loyalty  - 25f); } catch { }
                        try { own.Town.Security = Math.Max(0f, own.Town.Security - 20f); } catch { }
                    }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The coup collapsed into riots. Your involvement reached the wrong ears — your own holdings suffer."));
                    break;
                }
                case SchemeType.PoisonWell:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 70f, false); } catch { }
                    var own = Settlement.All.FirstOrDefault(s =>
                        s.IsTown && s.OwnerClan == instigator.Clan && s.Town != null);
                    if (own?.Town != null)
                        try { own.Town.FoodStocks = Math.Max(10f, own.Town.FoodStocks * 0.60f); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The agent confused the supply lines. Your own stores were poisoned."));
                    break;
                }
                case SchemeType.BribeSoldiers:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 60f, false); } catch { }
                    Hero owner = targetSett?.OwnerClan?.Leader;
                    if (owner != null && owner.IsAlive && owner != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, owner, -70, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The soldiers took your coin and marched straight to their captain."));
                    break;
                }
                case SchemeType.BurnStorage:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 60f, false); } catch { }
                    if (targetSett?.Town != null)
                    {
                        try { targetSett.Town.FoodStocks = Math.Max(10f, targetSett.Town.FoodStocks * 0.25f); } catch { }
                        try { targetSett.Town.Prosperity = Math.Max(10f, targetSett.Town.Prosperity * 0.70f); } catch { }
                    }
                    Hero owner = targetSett?.OwnerClan?.Leader;
                    if (owner != null && owner.IsAlive && owner != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, owner, -70, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The fire leaped every wall. The destruction is total — and undeniably yours."));
                    break;
                }
                case SchemeType.SpreadTerror:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 70f, false); } catch { }
                    Hero owner = targetSett?.OwnerClan?.Leader;
                    if (owner != null && owner.IsAlive && owner != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, owner, -70, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The violence was too organized. It left a trail straight to your doorstep."));
                    break;
                }
                case SchemeType.SpreadRumors:
                {
                    var own = Settlement.All.FirstOrDefault(s =>
                        s.IsTown && s.OwnerClan == instigator.Clan && s.Town != null);
                    if (own?.Town != null)
                    {
                        try { own.Town.Loyalty    = Math.Max(0f,  own.Town.Loyalty    - 20f); } catch { }
                        try { own.Town.Prosperity = Math.Max(10f, own.Town.Prosperity * 0.92f); } catch { }
                    }
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 40f, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The rumors warped in transit. Now they're about you. Your own people are whispering."));
                    break;
                }
                case SchemeType.VipersCounsel:
                {
                    Hero king = instigator.Clan?.Kingdom?.Leader;
                    if (king != null && king.IsAlive && king != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, king, -80, false); } catch { }
                    if (targetHero != null && targetHero.IsAlive)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, -60, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The king saw through the veil and recognized the hand behind it. You are no longer welcome at court."));
                    break;
                }
                case SchemeType.HireAssassin:
                {
                    if (Hero.MainHero?.PartyBelongedTo?.MemberRoster != null)
                    {
                        foreach (var e in Hero.MainHero.PartyBelongedTo.MemberRoster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            int toWound = Math.Max(1, (e.Number - e.WoundedNumber) / 5);
                            if (toWound <= 0) continue;
                            try { Hero.MainHero.PartyBelongedTo.MemberRoster.AddToCounts(e.Character, 0, false, toWound); } catch { }
                        }
                    }
                    var targetKingdom = targetHero?.Clan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 50f, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The blade turned around. Your own escort was ambushed."));
                    break;
                }
                case SchemeType.ScatterWolves:
                {
                    var ownKingdom = instigator.Clan?.Kingdom;
                    if (ownKingdom != null && !ownKingdom.IsEliminated)
                        try { SpawnBanditsInKingdom(ownKingdom, 3); } catch { }
                    var targetKingdom = targetHero?.Clan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 40f, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The bandits took your coin and went wherever they pleased — including your own roads."));
                    break;
                }
            }
        }

    }
}
