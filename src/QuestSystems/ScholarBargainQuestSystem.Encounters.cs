// =============================================================================
// ASH AND EMBER — QuestSystems/ScholarBargainQuestSystem.Encounters.cs
// Dialogue and consequences for The Scholar's Bargain.
// Partial of ScholarBargainQuestSystem (shared state lives in
// ScholarBargainQuestSystem.cs).
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
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class ScholarBargainQuestSystem
    {
        private const string ScholarName = "Ambrose Voss";

        // ═══════════════════════════════════════════════════════════════════
        // STAGE 0 — THE APPROACH
        // Called from SettlementEncounters.Dispatch (town enter/leave pool).
        // ═══════════════════════════════════════════════════════════════════
        internal static void EO_ScholarApproach(Settlement s) => ShowApproach(s);

        private static void ShowApproach(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  A Scholar at the Gate",
                $"A thin, ink-stained man falls into step beside you as you pass through {s.Name}. He introduces " +
                "himself as Ambrose Voss, a scholar of some private and unfashionable branch of study. He says he has " +
                "found something in his research — he will not say what, only that it would give you an edge over " +
                "every rival you have. All he asks in return is a roof, a guard on his door, and a patron who will not " +
                "ask too many questions too soon. He watches you the way a man watches a door he is not sure will open.",
                new List<InquiryElement>
                {
                    new InquiryElement("help",   "Offer him your protection.",       null, true, "−1000 gold. He settles in your castle."),
                    new InquiryElement("refuse", "Refuse him.",                      null, true, "He will find another patron."),
                    new InquiryElement("review", "Ask to review his papers first.",  null, true,
                        SkillHint(DefaultSkills.Engineering, 0.45f, "Judge whether the research is real")),
                    new InquiryElement("arrest", "Have him arrested and interrogated.", null, true,
                        "Honour −1, Mercy −1. He may not survive the questioning."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "help":
                            if (!ChangeGold(-1000)) { ShowApproach(s); break; }
                            _settlementId = s.StringId;
                            _stage        = StagePendingA2;
                            _countdown    = 7;
                            Msg($"{ScholarName} bows and gathers his few belongings. By evening he has a room in the " +
                                "keep and a lock on the door he asked for himself. He thanks you for the roof, not the " +
                                "coin — coin, he says, he can always find more of.", DimColor);
                            break;

                        case "refuse":
                            Msg($"You tell him you have no patronage to spare for a stranger's private theories. He " +
                                "takes it well, or well enough — he nods once, thanks you for your time, and says he " +
                                "will find someone less careful. He does not seem surprised.", DimColor);
                            StartB2Countdown();
                            _stage = StageEnded;
                            break;

                        case "review":
                            if (SkillRoll(DefaultSkills.Engineering, 0.45f))
                                Msg("You go through his papers by lamplight. The hand is careful, the citations real, " +
                                    "and the diagrams internally consistent in ways a forger would not bother with. " +
                                    "Whatever this is, it is not nonsense.", GoodColor);
                            else
                                Msg("You go through his papers by lamplight and understand perhaps one page in five. " +
                                    "The rest might be genius or the ravings of a man who has read too much alone. " +
                                    "You cannot tell.", DimColor);
                            ShowApproach(s);
                            break;

                        case "arrest":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg($"Your men take him before he can finish his sentence. He confesses to nothing under " +
                                "questioning — not because he is strong, but because the men asking do not know what " +
                                "questions to ask. He dies of it anyway, some hours before dawn, and whatever he knew " +
                                "goes into the ground with him. You get nothing.", BadColor);
                            _stage = StageEnded;
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // STAGE 1 — THE BREAKTHROUGH (fires 7 days after settling in)
        // ═══════════════════════════════════════════════════════════════════
        private static void FireBreakthrough()
        {
            if (MageKnowledge._deferredInquiry != null) { _countdown = 1; return; }

            MageKnowledge._deferredInquiry = () =>
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "★  The Scholar's Breakthrough",
                    $"{ScholarName} asks for you directly, at an hour that suggests he has not slept. He says he has " +
                    "had a breakthrough — that the dead need not stay dead, that what he has read in his private books " +
                    "can be made to walk again and fight for the hand that raised it. He says this quite calmly, the " +
                    "way a man reports good weather. All he needs, he says, is a single subject to prove the method " +
                    "sound. A peasant will do. He has already picked one.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("agree",    "Agree. Let him have his subject.", null, true, "Honour −1, Mercy −1."),
                        new InquiryElement("disagree", "Refuse him this.",                  null, true, "He will pack his things and go."),
                        new InquiryElement("hang",     "Hang him before he can try.",       null, true, "His secrets die with him."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "agree":
                                ShiftTrait(DefaultTraits.Honor, -1);
                                ShiftTrait(DefaultTraits.Mercy, -1);
                                Msg("It is over quickly, and not quietly. The peasant does not consent to any of it, " +
                                    "and nobody asks them to. Ambrose works through the night by a cold light of his " +
                                    "own making, and does not look up when you leave.", BadColor);
                                _stage     = StagePendingA3;
                                _countdown = 3;
                                break;

                            case "disagree":
                                Msg($"You tell him this far and no further. He accepts it with the same calm he asked " +
                                    "with — disappointed, not angry — and within the week he and his books are gone, " +
                                    "in search of someone less squeamish.", DimColor);
                                StartB2Countdown();
                                _stage = StageEnded;
                                break;

                            case "hang":
                                ShiftTrait(DefaultTraits.Honor, -1);
                                ShiftTrait(DefaultTraits.Mercy, -1);
                                Msg($"{ScholarName} hangs before he can raise anything at all. Whatever he had " +
                                    "actually learned — real or half-real — goes wherever unwritten things go. You " +
                                    "feel, more than you can justify, that you have been careful rather than cruel.", DimColor);
                                _stage = StageEnded;
                                break;
                        }
                    }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // STAGE 2 — THE FIRST REVENANT (fires 3 days after the sacrifice)
        // ═══════════════════════════════════════════════════════════════════
        private static void FireFirstRevenant()
        {
            if (MageKnowledge._deferredInquiry != null) { _countdown = 1; return; }

            GrantRevenant();

            MageKnowledge._deferredInquiry = () =>
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "★  The First Revenant",
                    $"The peasant does not stay in the ground. It rises cold, obedient, and utterly silent, and falls " +
                    $"in with your soldiers as though it had always marched among them. {ScholarName} looks, for the " +
                    "first time since you met him, genuinely pleased with himself. He says he can produce more of " +
                    "these — as many as you have subjects to give him.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("agree",    "Agree. Give him what he needs.",   null, true, "Honour −1, Mercy −1, and a mark against your name."),
                        new InquiryElement("disagree", "This is where it stops.",          null, true, "He will pack his things and go."),
                        new InquiryElement("hang",     "Hang him now, before this grows.", null, true, "His secrets die with him."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "agree":
                                ShiftTrait(DefaultTraits.Honor, -1);
                                ShiftTrait(DefaultTraits.Mercy, -1);
                                ChangeCrime(20f);
                                Msg("You give the order. It becomes, with unsettling speed, a routine — a delivery, " +
                                    "a night's work, a new silence added to the ranks by morning. People start to " +
                                    "notice. You stop letting yourself notice how you feel about that.", BadColor);
                                _stage     = StagePendingA4;
                                _countdown = 7;
                                break;

                            case "disagree":
                                Msg($"You tell him one was enough — more than enough. He is disappointed but not " +
                                    "surprised; he gathers his books within days and leaves to find a patron with " +
                                    "fewer scruples.", DimColor);
                                StartB2Countdown();
                                _stage = StageEnded;
                                break;

                            case "hang":
                                ShiftTrait(DefaultTraits.Honor, -1);
                                ShiftTrait(DefaultTraits.Mercy, -1);
                                Msg($"{ScholarName} hangs before he can raise a second one. The revenant he already " +
                                    "made stays in your ranks — cold, silent, and yours — but the method behind it " +
                                    "dies with the man who found it.", DimColor);
                                _stage = StageEnded;
                                break;
                        }
                    }, null, "", false), false, true);
        }

        private static void GrantRevenant()
        {
            try
            {
                var troop = MBObjectManager.Instance?.GetObject<CharacterObject>("ashen_revenant")
                         ?? MBObjectManager.Instance?.GetObject<CharacterObject>("ashen_thrall");
                if (troop != null && MobileParty.MainParty?.MemberRoster != null)
                    MobileParty.MainParty.MemberRoster.AddToCounts(troop, 1);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ═══════════════════════════════════════════════════════════════════
        // STAGE 3 — THE BARGAIN COMES DUE (fires 7 days after the second consent)
        // 50/50: permanent Ashen recruiting in the scholar's settlement, or the
        // settlement is besieged by three large Ashen Spawn warbands.
        // ═══════════════════════════════════════════════════════════════════
        private static void FireAshenOutcome()
        {
            if (MageKnowledge._deferredInquiry != null) { _countdown = 1; return; }

            Settlement s = Settlement.All.FirstOrDefault(x => x.StringId == _settlementId);
            bool success = _rng.NextDouble() < 0.5;

            MageKnowledge._deferredInquiry = () =>
            {
                if (success)
                {
                    if (s != null) AshenRecruitCampaignBehavior.GrantScholarRecruiting(s.StringId);
                    InformationManager.ShowInquiry(new InquiryData(
                        "◆  The Muster Yard",
                        $"{ScholarName} does not sleep for three days and comes out of it changed — quieter, colder, " +
                        $"and finished. Beneath {(s != null ? s.Name.ToString() : "the keep")} he has raised something " +
                        "that does not need him anymore to keep working: a standing muster of the cold-fire dead, " +
                        "ready to be raised from captives brought to the yard whether or not you ever call yourself " +
                        "Ashen. He calls it a gift. You are less sure the word is his to give.",
                        true, false, "So be it.", "",
                        () => { _stage = StageEnded; }, null));
                }
                else
                {
                    if (s != null)
                    {
                        Vec2 pos = s.GetPosition2D;
                        for (int i = 0; i < 3; i++)
                            CampaignMapEvents.SpawnAshenAmbushNear(pos, 20, 150f);
                    }
                    InformationManager.ShowInquiry(new InquiryData(
                        "◆  The Cold Gets In",
                        $"Whatever {ScholarName} finished, it did not stay his to command. The cold comes up through " +
                        $"{(s != null ? s.Name.ToString() : "the keep")}'s own foundations faster than anyone can " +
                        "name it, and by the time riders bring you word, there are already banners of Ashen Spawn " +
                        "massing on the roads around it. The scholar is nowhere to be found. You suspect he was the " +
                        "least of what walked out.",
                        true, false, "Damn him.", "",
                        () => { _stage = StageEnded; }, null));
                }
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // B2 — WHAT BECAME OF HIM (fires 7-30 days after the scholar is turned away)
        // ═══════════════════════════════════════════════════════════════════
        private static void FireOtherLordConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _b2Countdown = 1; return; }

            var candidates = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && h != Hero.MainHero
                         && h.Clan != null && h.Clan != Hero.MainHero?.Clan
                         && !ColourLordRegistry.IsAshenLord(h)
                         && h.Clan.Settlements.Any(cs => cs.IsCastle))
                .ToList();

            Hero lord = candidates.Count > 0 ? candidates[_rng.Next(candidates.Count)] : null;
            double roll = _rng.NextDouble();

            MageKnowledge._deferredInquiry = () =>
            {
                string body;
                if (lord == null)
                {
                    body = $"Word reaches you, eventually, of {ScholarName} — a name mentioned in passing by a " +
                           "merchant who met him on the road, still looking for a patron. Nothing more comes of it. " +
                           "Whatever he was building, he built it somewhere you will never hear of again.";
                }
                else if (roll < 0.33)
                {
                    ColourLordRegistry.SetAshen(lord, true);
                    body = $"Word reaches you that {lord.Name} has gone still and grey, and that {(lord.Clan?.Name?.ToString() ?? "their")} " +
                           "banners over one of their castles now fly the colour of cold ash. A scholar's name is " +
                           $"mentioned in the same breath as the change — {ScholarName}. You never learn exactly what " +
                           "he traded them, only that it worked.";
                }
                else if (roll < 0.66)
                {
                    try
                    {
                        var troop = MBObjectManager.Instance?.GetObject<CharacterObject>("ashen_revenant");
                        if (troop != null) lord.PartyBelongedTo?.MemberRoster.AddToCounts(troop, 20);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    body = $"Word reaches you that {lord.Name}'s muster has grown by twenty men who do not eat, " +
                           "sleep, or speak — cold soldiers raised somewhere quiet, under some scholar's instruction. " +
                           $"You hear the name {ScholarName} mentioned once, carefully, by someone who does not want " +
                           "to be asked how they know it.";
                }
                else
                {
                    body = $"Word reaches you that a scholar named {ScholarName} was hanged in {lord.Name}'s lands " +
                           "for practising something the local priests would not name. Whatever he offered, it seems, " +
                           "this lord wanted none of it. Nothing more comes of the matter.";
                }

                InformationManager.ShowInquiry(new InquiryData(
                    "★  What Became of Him", body, true, false, "So be it.", "",
                    null, null));
            };
        }
    }
}
