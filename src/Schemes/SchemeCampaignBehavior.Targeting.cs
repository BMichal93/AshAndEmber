// =============================================================================
// ASH AND EMBER — SchemeCampaignBehavior.Targeting.cs
// Faction/lord/settlement pickers, confirmation, and commit.
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
        // ── Faction filter ────────────────────────────────────────────────────
        // Step inserted between scheme selection and target selection.
        // Viper's Counsel skips this step — it always targets the player's own kingdom.
        internal static void OpenFactionFilterUI()
        {
            try
            {
                if (_selectedDef == null) return;

                // Viper's Counsel: always targets own kingdom — skip faction step
                if (_selectedDef.Type == SchemeType.VipersCounsel)
                {
                    _selectedKingdom = null;
                    OpenLordTargetUI();
                    return;
                }

                bool needsLord = _selectedDef.NeedsLord;

                // Collect factions that have at least one valid target for this scheme type
                var factions = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != "ashen_kingdom"
                             && (needsLord
                                 ? k.Heroes.Any(h => h.IsLord && h.IsAlive && !h.IsChild
                                                  && !h.IsPrisoner && h != Hero.MainHero)
                                 : Settlement.All.Any(s => (s.IsTown || s.IsCastle)
                                                        && s.OwnerClan?.Kingdom == k)))
                    .OrderBy(k => k.Name?.ToString() ?? "")
                    .ToList();

                if (factions.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("No valid factions to scheme against."));
                    return;
                }

                var elements = factions.Select(k =>
                {
                    int count = needsLord
                        ? k.Heroes.Count(h => h.IsLord && h.IsAlive && !h.IsChild
                                           && !h.IsPrisoner && h != Hero.MainHero)
                        : Settlement.All.Count(s => (s.IsTown || s.IsCastle) && s.OwnerClan?.Kingdom == k);
                    string hint = needsLord
                        ? $"{count} lord{(count != 1 ? "s" : "")} available"
                        : $"{count} settlement{(count != 1 ? "s" : "")} available";
                    return new InquiryElement(k.StringId, k.Name?.ToString() ?? "?", null, true, hint);
                }).ToList();

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"Choose Faction — {_selectedDef.Name}",
                        "Select the faction to scheme against:",
                        elements, true, 1, 1,
                        "Select", "Back",
                        chosen =>
                        {
                            try
                            {
                                if (chosen == null || chosen.Count == 0) return;
                                string kid = chosen[0].Identifier as string;
                                _selectedKingdom = Kingdom.All.FirstOrDefault(k => k.StringId == kid);
                                if (_selectedKingdom == null) return;
                                if (needsLord) OpenLordTargetUI();
                                else           OpenSettlementTargetUI();
                            }
                            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }, null),
                    true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Target selection: lords ───────────────────────────────────────────
        internal static void OpenLordTargetUI()
        {
            try
            {
                bool isVipers = _selectedDef.Type == SchemeType.VipersCounsel;
                var playerKingdom = Hero.MainHero?.Clan?.Kingdom;

                if (isVipers && playerKingdom == null)
                {
                    MBInformationManager.AddQuickInformation(
                        new TextObject("You must belong to a kingdom to use Viper's Counsel."));
                    return;
                }

                // Filter to the selected faction (or own kingdom for Viper's Counsel)
                Kingdom factionFilter = isVipers ? playerKingdom : _selectedKingdom;

                var lords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero
                             && (factionFilter == null || h.Clan?.Kingdom == factionFilter))
                    .OrderBy(h => h.Clan?.Kingdom?.Name?.ToString() ?? "")
                    .ThenBy(h => h.Name?.ToString() ?? "")
                    .Take(60)
                    .ToList();

                if (lords.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject(isVipers
                        ? "No rival lords found within your kingdom."
                        : $"No valid lord targets found in {_selectedKingdom?.Name?.ToString() ?? "that faction"}."));
                    return;
                }

                var elements = lords.Select(h =>
                {
                    float ch      = SchemeSystem.ComputeSuccessChance(Hero.MainHero, _selectedDef.Type, h, null);
                    int   cost    = SchemeSystem.ComputeGoldCost(_selectedDef, h, null);
                    int   infCost = SchemeSystem.ComputeInfluenceCost(_selectedDef, h, null);
                    bool  blk     = SchemeSystem.IsHardBlocked(_selectedDef.Type, h, null);
                    bool  cd      = SchemeSystem.IsOnCooldown(_selectedDef.Type, h, null);
                    string label = $"{h.Name}  [{h.Clan?.Name}]"
                                 + (blk ? "  [BLOCKED]" : "");
                    string hint  = $"Success: {(int)(ch * 100)}%  |  Cost: {cost}g / {infCost} inf  |  Tier: {h.Clan?.Tier ?? 0}"
                                 + (cd ? "  [5× repeat penalty]" : "");
                    return new InquiryElement(h.StringId, label, null, !blk, hint);
                }).ToList();

                string factionLabel = isVipers
                    ? playerKingdom?.Name?.ToString() ?? "your kingdom"
                    : _selectedKingdom?.Name?.ToString() ?? "selected faction";
                string selectMsg = isVipers
                    ? $"Select a lord from {factionLabel} to undermine in the king's eyes:"
                    : $"Select the lord to target in {factionLabel}:";

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"Target — {_selectedDef.Name}  ({factionLabel})",
                        selectMsg,
                        elements, true, 1, 1,
                        "Confirm", "Back",
                        OnLordTargetChosen, null),
                    true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void OnLordTargetChosen(List<InquiryElement> selected)
        {
            try
            {
                if (selected == null || selected.Count == 0) return;
                string heroId = selected[0].Identifier as string;
                Hero target = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId);
                if (target == null) return;
                ShowConfirmation(target, null);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Target selection: settlements ─────────────────────────────────────
        internal static void OpenSettlementTargetUI()
        {
            try
            {
                string factionLabel = _selectedKingdom?.Name?.ToString() ?? "selected faction";

                var settlements = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle)
                             && (_selectedKingdom == null || s.OwnerClan?.Kingdom == _selectedKingdom))
                    .OrderBy(s => s.Name?.ToString() ?? "")
                    .Take(60)
                    .ToList();

                if (settlements.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(
                        new TextObject($"No valid settlement targets found in {factionLabel}."));
                    return;
                }

                var elements = settlements.Select(s =>
                {
                    float ch      = SchemeSystem.ComputeSuccessChance(Hero.MainHero, _selectedDef.Type, null, s);
                    int   cost    = SchemeSystem.ComputeGoldCost(_selectedDef, null, s);
                    int   infCost = SchemeSystem.ComputeInfluenceCost(_selectedDef, null, s);
                    bool  cd      = SchemeSystem.IsOnCooldown(_selectedDef.Type, null, s);
                    string label = $"{s.Name}  [{s.OwnerClan?.Name?.ToString() ?? "?"}]  "
                                 + $"Security: {(int)(s.Town?.Security ?? 0)}";
                    string hint  = $"Success: {(int)(ch * 100)}%  |  Cost: {cost}g / {infCost} inf"
                                 + (cd ? "  [5× repeat penalty]" : "");
                    return new InquiryElement(s.StringId, label, null, true, hint);
                }).ToList();

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"Target — {_selectedDef.Name}  ({factionLabel})",
                        $"Select the settlement to target in {factionLabel}:",
                        elements, true, 1, 1,
                        "Confirm", "Back",
                        OnSettlementTargetChosen, null),
                    true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void OnSettlementTargetChosen(List<InquiryElement> selected)
        {
            try
            {
                if (selected == null || selected.Count == 0) return;
                string settId = selected[0].Identifier as string;
                Settlement target = Settlement.All.FirstOrDefault(s => s.StringId == settId);
                if (target == null) return;
                ShowConfirmation(null, target);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Confirmation ──────────────────────────────────────────────────────
        private static void ShowConfirmation(Hero targetHero, Settlement targetSett)
        {
            try
            {
                if (_selectedDef == null) return;

                int    goldCost  = SchemeSystem.ComputeGoldCost(_selectedDef, targetHero, targetSett);
                int    infCost   = SchemeSystem.ComputeInfluenceCost(_selectedDef, targetHero, targetSett);
                bool   retaliation = SchemeSystem.PlayerRetaliationActive;
                if (retaliation)
                {
                    goldCost /= 2;
                    infCost  /= 2;
                }
                bool   onCooldown = SchemeSystem.IsOnCooldown(_selectedDef.Type, targetHero, targetSett);
                string tName     = targetHero?.Name?.ToString() ?? targetSett?.Name?.ToString() ?? "target";
                string cdNote    = onCooldown ? "\n[!] Repeat-use penalty — cost is 5× base." : "";
                if (retaliation) cdNote += "\nRetaliation — the fire answers: costs halved today.";
                bool   isAss     = _selectedDef.Type == SchemeType.Assassinate;
                string traitNote = isAss
                    ? "\nPersonality: Honor −1  Calculating −1  Mercy −1  — on commit"
                    : "\nPersonality: Honor −1  Calculating −1  — on commit";
                int roguery   = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0;
                int charm     = Hero.MainHero?.GetSkillValue(DefaultSkills.Charm)   ?? 0;
                int rounds    = Math.Min(10, 5 + roguery / 100);
                int sidePct   = (int)(Math.Max(0.20f, Math.Min(0.80f, 0.20f + (roguery / 500f) * 0.60f)) * 100f);
                int charmPct  = (int)(Math.Max(0.20f, Math.Min(0.80f, 0.20f + (charm  / 500f) * 0.60f)) * 100f);

                // Kept short on purpose — the native confirmation dialog has no scroll
                // bar, and the old wall of text (full press-on ranges, a duplicated
                // skip explanation) overflowed the box and pushed the Proceed button
                // off-screen. Exact press-on shifts and the skip odds are hidden by
                // design anyway (see the "skip" InquiryElement's own hint below), so
                // dropping them here loses nothing you can act on.
                var    cfg      = SchemeMinigame.GetPublicConfig(_selectedDef.Type);
                string failNote = isAss
                    ? "If blown (exposure >21): assassin captured — crime +80, relations −80, 60% chance of war."
                    : "If blown (exposure >21): operation backfires — consequences specific to the scheme type.";
                int skipPct = (int)(SchemeMinigame.SkipSuccessChance * 100f);
                string body     = $"Scheme: {_selectedDef.Name}   Target: {tName}\n"
                                + $"Cost: {goldCost}g  +  {infCost} influence{cdNote}\n"
                                + $"Threshold ≥{cfg.RiskSum}  |  Blown at 21  |  Rounds: {rounds} (Roguery {roguery})"
                                + traitNote + "\n\n"
                                + "Each round brings a field report: push hard, tread carefully, or pull back — the "
                                + "exact shift stays hidden until you commit. Sidestep "
                                + $"({sidePct}% Roguery) and Talk It Down ({charmPct}% Charm) are one-use field "
                                + "abilities. Run out of rounds and it's a coin flip between bust and a quiet fail.\n\n"
                                + failNote;

                var options = new List<InquiryElement>
                {
                    new InquiryElement("play", "Begin the Gambit — run the operation", null, true,
                        "Play the Gambit in full: field reports, press-on choices, and a chance at a stronger result the deeper you push."),
                    new InquiryElement("skip", $"Trust to Instinct — skip the Gambit  [{skipPct}% Roguery]", null, true,
                        $"One Roguery gamble ({skipPct}%) decides success or failure immediately — no field reports, no bonus, no risk of running out of rounds."),
                };

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        "Confirm Operation", body, options, false, 1, 1,
                        "Proceed", "Stand Down",
                        chosen =>
                        {
                            try
                            {
                                bool playMinigame = (chosen?[0]?.Identifier as string ?? "play") == "play";
                                CommitScheme(targetHero, targetSett, playMinigame);
                            }
                            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        },
                        null),
                    true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void CommitScheme(Hero targetHero, Settlement targetSett, bool playMinigame)
        {
            try
            {
                if (_selectedDef == null || Hero.MainHero == null) return;

                if (SchemeSystem.IsHardBlocked(_selectedDef.Type, targetHero, targetSett))
                {
                    MBInformationManager.AddQuickInformation(
                        new TextObject("That target is currently blocked — the path is not yet clear."));
                    _selectedDef = null;
                    try { GameMenu.SwitchToMenu("town"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    return;
                }

                int goldCost = SchemeSystem.ComputeGoldCost(_selectedDef, targetHero, targetSett);
                int infCost  = SchemeSystem.ComputeInfluenceCost(_selectedDef, targetHero, targetSett);

                if (SchemeSystem.PlayerRetaliationActive)
                {
                    goldCost /= 2;
                    infCost  /= 2;
                }

                if (!SchemeSystem.DebugFree)
                {
                    if (Hero.MainHero.Gold < goldCost
                        || (Hero.MainHero.Clan?.Influence ?? 0f) < infCost)
                    {
                        MBInformationManager.AddQuickInformation(
                            new TextObject("Insufficient funds — the scheme cannot be arranged."));
                        _selectedDef = null;
                        try { GameMenu.SwitchToMenu("town"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return;
                    }
                    try { Hero.MainHero.Gold -= goldCost; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { if (Hero.MainHero.Clan != null) Hero.MainHero.Clan.Influence -= infCost; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                try { ShiftPlayerTrait(DefaultTraits.Honor,       -1); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { ShiftPlayerTrait(DefaultTraits.Calculating,  -1); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (_selectedDef.Type == SchemeType.Assassinate)
                    try { ShiftPlayerTrait(DefaultTraits.Mercy, -1); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                var capturedDef  = _selectedDef;
                var capturedHero = targetHero;
                var capturedSett = targetSett;
                _selectedDef = null;

                // Stamp per-target cooldown NOW so a save-reload before the first phase
                // cannot bypass the cost and retry the same target for free. The minigame
                // will overwrite this with the outcome-correct value on resolution.
                try { SchemeSystem.PreStampTargetCooldown(capturedDef.Type, capturedHero, capturedSett); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // Record the committed operation. If the player reloads before the
                // Gambit (or the deferred skip resolution) resolves, OnSessionLaunched
                // re-launches the right path so the costs already paid are not silently lost.
                try { SchemeSystem.SetPendingPlayerOperation(capturedDef.Type, capturedHero, capturedSett, skip: !playMinigame); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                try { GameMenu.SwitchToMenu("town"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // Defer so menu transition completes before the first inquiry opens.
                MageKnowledge._deferredInquiry = () =>
                {
                    try
                    {
                        if (playMinigame) SchemeMinigame.Begin(capturedDef, capturedHero, capturedSett);
                        else              SchemeMinigame.ResolveSkip(capturedDef, capturedHero, capturedSett);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                };
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ShiftPlayerTrait(TraitObject trait, int delta)
        {
            var hero = Hero.MainHero;
            if (hero == null) return;
            int cur = hero.GetTraitLevel(trait);
            hero.SetTraitLevel(trait, Math.Min(2, Math.Max(-2, cur + delta)));
        }
    }
}
