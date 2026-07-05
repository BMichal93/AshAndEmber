// =============================================================================
// ASH AND EMBER — DragonQuestSystem.Ending.cs
// Three possible endings: Banish (campaign continues), Merge (player becomes
// the Vessel), Sacrifice (Last Binding — player dies, Ashen crumble).
// Also contains the Sacrifice ending sequence (TickEnding / ShowSacrificeDialog)
// and the grimoire summary.
// Partial of DragonQuestSystem (shared state lives in DragonQuestSystem.cs).
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
    public static partial class DragonQuestSystem
    {
        // ── Banish ending ─────────────────────────────────────────────────────
        private static void ShowBanishEnding()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Cast Him Out",

                    "The choice is made in a breath.\n\n" +
                    "Aelisar does not resist. There is no dramatic unravelling, no flash of fire " +
                    "or cold. He simply... recedes. The way a voice grows quieter when you stop " +
                    "walking toward it.\n\n" +
                    "\"I understand,\" he says, from very far away. " +
                    "\"You have your own life to carry. That was always a valid answer.\"\n\n" +
                    "The pull is gone. The visions will not come again. Whatever you collected " +
                    "of him across the grey march — it dissolves, quietly, into nothing permanent.\n\n" +
                    "The Ashen still move. The cycle continues. The grey will come again, " +
                    "in a generation or three — and whoever faces it will do so without knowing " +
                    "what you found, or what you chose.\n\n" +
                    "That is how the world works. You have known it for a long time.",

                    true, false,
                    "Then I go on.",
                    "",
                    () =>
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The Sundered Crown — quest ended. The cycle continues, unchanged.",
                            new Color(0.60f, 0.55f, 0.65f)));
                    },
                    () => { }
                ), true, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Merge ending ──────────────────────────────────────────────────────
        private static void ShowMergeEnding()
        {
            try
            {
                // Grant fire magic if the player isn't already a mage
                bool wasNotMage = !MageKnowledge.IsMage;
                if (wasNotMage)
                {
                    try { MageKnowledge.SetMage(true); }         catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { ColourLordRegistry.SetMage(Hero.MainHero, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 10; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                string fireNote = wasNotMage
                    ? "His covenant opens the fire in you — a flame you did not know you carried. " +
                      "Ten focus points. The gift is real.\n\n"
                    : "His covenant deepens what you already carry. " +
                      "The fire you know becomes something older and heavier.\n\n";

                InformationManager.ShowInquiry(new InquiryData(
                    "The Vessel",

                    "It is not painful. That is the thing you did not expect.\n\n" +
                    "Aelisar settles into you the way a long winter settles into stone — " +
                    "not violently, not all at once, but completely. " +
                    "You feel him find the shape of you and fit himself around it, " +
                    "and then you feel the places where you end and he begins " +
                    "become less precise than they were.\n\n" +
                    fireNote +
                    "\"The aging,\" he says — and it is strange, hearing your own voice " +
                    "carry his cadence — \"will not touch you the way it touched others. " +
                    "The covenant I made with the fire holds against that. " +
                    "You will not pay years for what you cast. I already did.\"\n\n" +
                    "You stand in your camp for a long time. You are still you. " +
                    "You are also him — and he was enormous, and old, and certain of very little " +
                    "except that he was right to try.\n\n" +
                    "You are not sure you disagree.\n\n" +
                    "The Ashen still move. The cycle continues. " +
                    "But something in the grey tide — something the lords carry without knowing it — " +
                    "recognises what you are now. And hesitates.",

                    true, false,
                    "I carry it.",
                    "",
                    () =>
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            wasNotMage
                                ? "The Sundered Crown — Aelisar's fire is yours. Ten focus points granted. " +
                                  "His covenant bars the aging that burns other mages."
                                : "The Sundered Crown — Aelisar merged. His covenant bars the aging that burns other mages.",
                            new Color(0.85f, 0.65f, 0.25f)));
                    },
                    () => { }
                ), true, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Sacrifice ending sequence ─────────────────────────────────────────
        // Phase 1: set worldBound, begin killing Ashen lords
        // Phase 2: continue killing Ashen lords and mage lords
        // Phase 3: kill mage companions, redistribute Ashen settlements
        // Phase 4: finish redistribution, show final dialog
        private static void TickEnding()
        {
            try
            {
                switch (_endingPhase)
                {
                    case 1:
                        _worldBound = true;
                        KillAshenLords(5);
                        _endingPhase = 2;
                        break;

                    case 2:
                        KillAshenLords(20);
                        KillMageLords(5);
                        _endingPhase = 3;
                        break;

                    case 3:
                        KillMageLords(20);
                        KillMageCompanions();
                        RedistributeAshenSettlements(3);
                        _endingPhase = 4;
                        break;

                    case 4:
                        RedistributeAshenSettlements(20);
                        _endingPhase = 5;
                        if (MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = ShowSacrificeDialog;
                        break;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ShowSacrificeDialog()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Last Binding",

                    "You do not feel it happen as a single thing.\n\n" +
                    "Aelisar goes first — or perhaps he goes with you. " +
                    "The distinction stops mattering almost immediately. " +
                    "Everything you gathered of him across seven lives and three sacred places " +
                    "and the cold heart of winter gives itself to the breaking.\n\n" +
                    "You feel the mechanism shatter. Not dramatically — the way a keystone fails " +
                    "in a vault, quietly, and then suddenly there is no vault. " +
                    "The grey tide, which has been moving toward something it recognised as inevitable, " +
                    "loses the thing it was moving toward. The cold finds nothing to call it forward.\n\n" +
                    "For a moment you are aware of everything: " +
                    "the march stopping, the grey retreating, " +
                    "the Ashen lords falling where they stand — not in battle, " +
                    "not with ceremony, simply the final release of things that were already done. " +
                    "Somewhere far away, the first new fire kindles in a hearth " +
                    "that will not know what it cost.\n\n" +
                    "\"This is enough,\" says Aelisar — or says what you both have become — " +
                    "at the very end. Not triumphant. Certain.\n\n" +
                    "You think: yes. This is enough.\n\n" +
                    "Then you do not think anything.",

                    true, false,
                    "It is done.",
                    "",
                    () =>
                    {
                        try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    () => { }
                ), true, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Sacrifice ending helpers ───────────────────────────────────────────
        private static void KillAshenLords(int cap)
        {
            int killed = 0;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (killed >= cap) break;
                    if (!h.IsAlive || h.IsChild || h == Hero.MainHero) continue;
                    if (!ColourLordRegistry.IsAshenLord(h)) continue;
                    try { KillCharacterAction.ApplyByMurder(h, null, false); killed++; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void KillMageLords(int cap)
        {
            int killed = 0;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (killed >= cap) break;
                    if (!h.IsAlive || h.IsChild || h == Hero.MainHero) continue;
                    if (!ColourLordRegistry.IsColourLord(h)) continue;
                    try { KillCharacterAction.ApplyByMurder(h, null, false); killed++; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void KillMageCompanions()
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                foreach (var entry in roster.GetTroopRoster().ToList())
                {
                    Hero companion = entry.Character?.HeroObject;
                    if (companion == null || companion == Hero.MainHero) continue;
                    if (!ColourLordRegistry.IsColourLord(companion)) continue;
                    try { KillCharacterAction.ApplyByMurder(companion, null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void RedistributeAshenSettlements(int cap)
        {
            int moved = 0;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenKingdomId)
                    .ToList();
                if (kingdoms.Count == 0) return;

                foreach (Settlement s in Settlement.All.ToList())
                {
                    if (moved >= cap) break;
                    if (s.MapFaction?.StringId != AshenKingdomId) continue;
                    if (s.IsUnderSiege) continue;

                    var target = kingdoms[_rng.Next(kingdoms.Count)];
                    Hero lord = target.Leader ?? target.RulingClan?.Leader;
                    if (lord == null) continue;
                    try
                    {
                        ChangeOwnerOfSettlementAction.ApplyByDefault(lord, s);
                        try { if (s.Town != null) { s.Town.Loyalty = 100f; s.Town.Security = 100f; } } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        moved++;
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Grimoire summary ──────────────────────────────────────────────────
        public static string GetGrimoireSummary()
        {
            switch (_phase)
            {
                case PhaseIdle:
                case PhaseFirstContact:
                    return "";

                case PhaseFirstRefused:
                    return "\nA presence tried to reach you after the first Ashen lord fell. " +
                           "You refused it once. It is still waiting.\n";

                case PhasePermanentlyClosed:
                    return "\nThe presence of Aelisar Veth withdrew when you refused it the second time. " +
                           "The quest is closed.\n";

                case PhaseEndedBanish:
                    return "\nQuest Ended: The Sundered Crown.\n" +
                           "You cast Aelisar out. The cycle continues, unchanged.\n";

                case PhaseEndedMerge:
                    return "\nQuest Ended: The Sundered Crown.\n" +
                           "Aelisar Veth rests in you. His fire, his covenant, his weight — all yours now.\n" +
                           "His covenant bars the aging that burns other mages.\n";

                case PhaseEndedSacrifice:
                case PhaseAccepted: // legacy compat — should not occur
                    return "\nThe Last Binding has fired. The grey retreats and will not return.\n" +
                           "It cost everything. Both of them agreed it was enough.\n";

                case PhaseColdActive:
                    return "\nQuest Ended: The Sundered Crown (failed — the fire went out).\n" +
                           "Quest: Bring the Eternal Cold — in progress.\n";

                case PhaseColdDone:
                    return "\nCalradia is one unbroken silence. The cold has everything it wanted.\n";

                default:
                    // PhaseActive or PhaseAllDone
                    break;
            }

            string lords  = _lordsSlain >= TargetLordsSlain
                ? "✓" : $"{_lordsSlain}/{TargetLordsSlain}";
            string ruin1  = AshenRuinSystem.IsCleared(DestinedRuinVillages[0]) ? "✓" : "○";
            string ruin2  = AshenRuinSystem.IsCleared(DestinedRuinVillages[1]) ? "✓" : "○";
            string ruin3  = AshenRuinSystem.IsCleared(DestinedRuinVillages[2]) ? "✓" : "○";
            string heart  = _heartCaptured ? "✓" : "○";

            string status = _phase == PhaseAllDone
                ? "\n  [All conditions met — Aelisar awaits your answer.]\n"
                : "";

            return $"\nQuest: The Sundered Crown\n" +
                   $"  {lords}   Ashen lords silenced (need {TargetLordsSlain})\n" +
                   $"  {ruin1}   The Sunken Scriptorium  (Dravend)\n" +
                   $"  {ruin2}   The Shattered Throne    (Epis)\n" +
                   $"  {ruin3}   The Dragon's Tomb       (Myzea)\n" +
                   $"  {heart}   The Heart of Winter     (Tyal)\n" +
                   status;
        }

        // Legacy constant — referenced nowhere externally but kept for save compat.
        private const int PhaseAccepted = 99;
    }
}
