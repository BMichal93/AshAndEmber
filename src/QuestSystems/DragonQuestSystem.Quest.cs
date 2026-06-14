// =============================================================================
// ASH AND EMBER — DragonQuestSystem.Quest.cs
// Goal checks, the old-man event, pop-ups, final prompt, and ending.
// Partial of DragonQuestSystem (shared state lives in DragonQuestSystem.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static partial class DragonQuestSystem
    {
        // ── Goal checks ───────────────────────────────────────────────────────
        private static void CheckGoals()
        {
            try
            {
                // Goal 1 — Clan Tier 6
                if (!_goal1Done && (Hero.MainHero?.Clan?.Tier ?? 0) >= TargetClanTier)
                {
                    _goal1Done = true;
                    try { _questLog?.LogGoal1(); } catch { }
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = () => ShowGoalComplete(1);
                }
            }
            catch { }
            try
            {
                // Goal 2 — Capture Tyal
                if (!_goal2Done)
                {
                    var tyal = Settlement.All.FirstOrDefault(s =>
                        s.Name.ToString().IndexOf(TyalMarker, StringComparison.OrdinalIgnoreCase) >= 0
                        && (s.IsTown || s.IsCastle));
                    if (tyal != null && tyal.OwnerClan == Hero.MainHero?.Clan)
                    {
                        _goal2Done = true;
                        try { _questLog?.LogGoal2(); } catch { }
                        if (MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = () => ShowGoalComplete(2);
                    }
                }
            }
            catch { }
            try
            {
                // Goal 3 — Hero Level 25
                if (!_goal3Done && (Hero.MainHero?.Level ?? 0) >= TargetHeroLevel)
                {
                    _goal3Done = true;
                    try { _questLog?.LogGoal3(); } catch { }
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = () => ShowGoalComplete(3);
                }
            }
            catch { }
        }

        // ── Old man event ─────────────────────────────────────────────────────
        private static void ShowOldManEvent()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Last Ember",

                    "A very old man steps out from the shadow of a wall as you pass. He is not threatening, " +
                    "but he is deliberate. His eyes are the eyes of someone who has carried the fire for a very long time " +
                    "and has almost nothing left.\n\n" +
                    "\"You. The one who carries it. I have walked a long road to find you.\"\n\n" +
                    "He does not wait for a response.\n\n" +
                    "\"The world is going cold. The Ashen spread because no one is willing to spend themselves to stop them. " +
                    "I have outlived everyone I knew by forty years — mages who spent themselves on the wrong fires, " +
                    "lords who spent themselves on the wrong wars. None of it was enough.\"\n\n" +
                    "He steadies himself against the wall. His hands are shaking.\n\n" +
                    "\"There is a way to rekindle the world. One great burning — everything, at once. " +
                    "The one who carries it must give everything they have. Not some of it. All. " +
                    "Every mage's fire, every thread of the gift still woven through Calradia. " +
                    "The Ashen will crumble. The darkness will break. But the cost is everything.\"\n\n" +
                    "He looks at you with the patience of a man who has said this many times before and been refused.\n\n" +
                    "\"I have spent forty years looking for someone strong enough and willing enough. " +
                    "You may be neither. But you are the last one I will find.\"\n\n" +
                    "He sits down against the wall. His fire goes out quietly, " +
                    "like a candle that has burned everything it was given. " +
                    "By the time you reach him, he is cold.\n\n" +
                    "The question is whether you heard him.",

                    true, true,
                    "I heard him. Tell me the path.",
                    "I have no time for dying old men.",

                    () =>
                    {
                        // Accept — quest begins
                        _phase = PhaseActive;
                        try { _questLog = new DragonQuestLog(); _questLog.StartQuest(); _questLog.LogStarted(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Quest added: The Last Flight of the Dragons.",
                            new Color(0.75f, 0.55f, 0.3f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Goals: Clan Tier {TargetClanTier}  ·  Capture Tyal  ·  Reach Level {TargetHeroLevel}",
                            new Color(0.65f, 0.5f, 0.25f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Check quest progress in the Grimoire (Alt+X).",
                            new Color(0.6f, 0.5f, 0.25f)));
                    },
                    () =>
                    {
                        // Refuse — quest permanently unavailable; lore: "died with him"
                        _phase = PhaseFailed;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "You walked away. Whatever he was offering died with him.",
                            new Color(0.5f, 0.5f, 0.5f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Goal completion pop-ups ───────────────────────────────────────────
        private static void ShowGoalComplete(int goal)
        {
            try
            {
                string title, body, button;
                switch (goal)
                {
                    case 1:
                        title = "The Forge of Power";
                        body  = "Your clan's name is known across the length of Calradia. " +
                                "Lords who would not have received you now send messengers. " +
                                "The fire you carry has become something the world takes seriously.\n\n" +
                                "The old man's words come back to you: *gain a grasp on the world.*\n\n" +
                                "You understand them now. The first condition is met.";
                        button = "The world is listening.";
                        break;
                    case 2:
                        title = "The Cold Heart";
                        body  = "You stand in the great hall of Tyal. The Ashen who built this place " +
                                "are gone, or cowed, or watching from the shadows.\n\n" +
                                "You feel it — the deep cold underneath everything here. " +
                                "The memory of what was done in this room. " +
                                "The long dark that the Ashen have been carrying west.\n\n" +
                                "You were inside the darkness. Now you know its dimensions.\n\n" +
                                "The second condition is met.";
                        button = "Now I understand the weight of it.";
                        break;
                    default:
                        title = "The Fire at Full Height";
                        body  = "You have been through enough battles, enough decisions, enough years " +
                                "to have changed beyond what you were. " +
                                "The inner fire burns different now — steadier, hotter, more certain of itself.\n\n" +
                                "The old man said *gain the power.* He meant this: " +
                                "the kind of strength that comes from having been tested " +
                                "enough times that you no longer flinch.\n\n" +
                                "The third condition is met.";
                        button = "I am ready.";
                        break;
                }

                InformationManager.ShowInquiry(new InquiryData(
                    title, body, true, false, button, "",
                    () => { }, () => { }
                ), true, true);
            }
            catch { }
        }

        // ── Final prompt ──────────────────────────────────────────────────────
        private static void ShowFinalPrompt()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Last Light",

                    "It is possible now. All three conditions are met.\n\n" +
                    "To rekindle the world, you would pour everything — not just your own fire, " +
                    "but the fire of every mage still living, every thread of the gift " +
                    "woven through Calradia. They will not survive it. You will not survive it.\n\n" +
                    "But the cold will break.\n\n" +
                    "The Ashen will crumble. The darkness will retreat. " +
                    "And the world will have a morning it would not have had.\n\n" +
                    "The chance exists. It will not exist forever.\n\n" +
                    "This is what he was asking.\n\n" +
                    "(Choosing to rekindle ENDS YOUR CAMPAIGN — your hero dies and the game is over. " +
                    "Refusing closes the quest forever; the campaign continues.)",

                    true, true,
                    "Do it. Light everything.",
                    "Not like this. Not now.",

                    () =>
                    {
                        // Yes — begin ending sequence
                        _phase       = PhaseRekindled;
                        _endingPhase = 1;
                        try { _questLog?.LogComplete(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The fire begins to move. There is no taking it back.",
                            new Color(0.9f, 0.65f, 0.15f)));
                    },
                    () =>
                    {
                        // No — quest fails
                        _phase = PhaseFailed;
                        try { _questLog?.LogFailed(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The chance fleets. Whatever he offered is gone. The world turns as it will.",
                            new Color(0.5f, 0.5f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Ending sequence ───────────────────────────────────────────────────
        // Called from DailyTick when _endingPhase > 0.
        // Phase 1: Set rekindled flag, kill Ashen lords (up to 5).
        // Phase 2: Kill remaining Ashen lords, kill mage lords (up to 5).
        // Phase 3: Kill remaining mage lords + mage companions, redistribute Ashen settlements (up to 3).
        // Phase 4: Finish redistribution, show final dialog → player dies.
        private static void TickEnding()
        {
            try
            {
                switch (_endingPhase)
                {
                    case 1:
                        _worldRekindled = true;
                        KillAshenLords(5);
                        _endingPhase = 2;
                        break;

                    case 2:
                        KillAshenLords(20);   // finish remaining Ashen
                        KillMageLords(5);
                        _endingPhase = 3;
                        break;

                    case 3:
                        KillMageLords(20);    // finish remaining mage lords
                        KillMageCompanions();
                        RedistributeAshenSettlements(3);
                        _endingPhase = 4;
                        break;

                    case 4:
                        RedistributeAshenSettlements(20); // finish remaining
                        _endingPhase = 5;
                        // Queue the final dialog
                        if (MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = ShowEndingDialog;
                        break;
                }
            }
            catch { }
        }

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
                    try
                    {
                        KillCharacterAction.ApplyByMurder(h, null, false);
                        killed++;
                    }
                    catch { }
                }
            }
            catch { }
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
                    try
                    {
                        KillCharacterAction.ApplyByMurder(h, null, false);
                        killed++;
                    }
                    catch { }
                }
            }
            catch { }
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
                    try { KillCharacterAction.ApplyByMurder(companion, null, false); } catch { }
                }
            }
            catch { }
        }

        private static void RedistributeAshenSettlements(int cap)
        {
            const string AshenId = "ashen_kingdom";
            int moved = 0;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenId)
                    .ToList();
                if (kingdoms.Count == 0) return;

                foreach (Settlement s in Settlement.All.ToList())
                {
                    if (moved >= cap) break;
                    if (s.MapFaction?.StringId != AshenId) continue;
                    if (s.IsUnderSiege) continue;

                    var target = kingdoms[_rng.Next(kingdoms.Count)];
                    Hero lord = target.Leader ?? target.RulingClan?.Leader;
                    if (lord == null) continue;
                    try
                    {
                        ChangeOwnerOfSettlementAction.ApplyByDefault(lord, s);
                        try { if (s.Town != null) { s.Town.Loyalty = 100f; s.Town.Security = 100f; } } catch { }
                        moved++;
                    }
                    catch { }
                }
            }
            catch { }
        }

    }
}
