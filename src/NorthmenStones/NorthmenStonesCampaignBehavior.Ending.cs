// =============================================================================
// ASH AND EMBER — NorthmenStonesCampaignBehavior.Ending.cs
//
// Once every material is fully given, the Forest Clans and Northmen stand
// truly allied, and Varcheg is still the Northmen's, the seers ask for the
// final spark — a living mage's fire, bound into the stones. Three choices:
// refuse (the Northmen never forgive it), give your own fire (you die, the
// stones bind), or give a child's instead (your spouse cannot forgive that).
//
// Once raised, the stones cast the Greater Emberfall at a random Ashen town
// every 7 days — modeled directly on GreatOtherParty.Hunger: hero notables
// are spared (Bannerlord does not reliably respawn a killed settlement
// notable), but the garrison and the town itself are gutted.
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class NorthmenStonesCampaignBehavior
    {
        private void EndingDailyTick()
        {
            if (_phase != PhaseActive) return;

            bool materialsDone = NorthmenStonesMath.IsMaterialsComplete(
                _iron, _hardwood, _tools, _silver, _denars, KindledTotal());
            if (!materialsDone) return;

            bool conditionsMet = IsNorthmenForestClansAllied() && IsVarchegNorthmenOwned();
            if (!conditionsMet)
            {
                if (!_materialsCompleteNotified)
                {
                    _materialsCompleteNotified = true;
                    try
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "Every material the seers asked for has been given — but the working cannot close. " +
                            "It needs the Forest Clans standing truly allied with the Northmen, and Varcheg " +
                            "still theirs, before the final spark can be asked for."));
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                return;
            }

            if (_resolutionHandled) return;
            if (MageKnowledge._deferredInquiry != null) return;
            _resolutionHandled = true;
            MageKnowledge._deferredInquiry = ShowFinalChoice;
        }

        private static bool HasEligibleChild()
        {
            try { return EligibleChildren().Any(); }
            catch { return false; }
        }

        private static List<Hero> EligibleChildren()
        {
            try
            {
                return Hero.AllAliveHeroes
                    .Where(h => h != null && h.IsAlive
                             && (h.Father == Hero.MainHero || h.Mother == Hero.MainHero))
                    .ToList();
            }
            catch { return new List<Hero>(); }
        }

        private static void ShowFinalChoice()
        {
            try
            {
                bool childOption = HasEligibleChild();

                var elements = new List<InquiryElement>
                {
                    new InquiryElement("agree",
                        "Agree — my own fire binds the stones.",
                        null, true,
                        "You give your own life to the working. The stones rise; you do not live to see it."),
                };
                if (childOption)
                {
                    elements.Add(new InquiryElement("child",
                        "Give a child's blood instead — it shares mine, the stones will accept it.",
                        null, true,
                        "One of your children is sacrificed in your place. The stones rise; your spouse " +
                        "will not forgive you."));
                }
                elements.Add(new InquiryElement("disagree",
                    "Disagree — I will not give you what you're asking.",
                    null, true,
                    "The working fails. The Northmen will call you a criminal for this, for the rest of " +
                    "your days."));

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "The Final Spark",

                    "The seer meets you at the half-raised ring of stones, and does not waste words on it.\n\n" +
                    "\"Every stone is set. Every stone is fed. What is left is the spark that makes iron and " +
                    "silver and bound Kindled into a working instead of a heap of dead things. Fire, from a " +
                    "living mage, given whole and given knowing. We would give one of our own if it would " +
                    "hold — it will not. It has to be a fire that already answers to yours, bound to the same " +
                    "working. It has to be you, or blood of yours.\"\n\n" +
                    "\"Choose. We will not ask you twice.\"",

                    elements, true, 1, 1,
                    "Decide.", "",
                    chosen =>
                    {
                        try { HandleFinalChoice(chosen?.FirstOrDefault()?.Identifier as string ?? "disagree"); }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    _ => { },
                    "", false
                ), false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void HandleFinalChoice(string choice)
        {
            switch (choice)
            {
                case "agree":
                    _phase = PhaseEnded;
                    _endingKind = EndingSelf;
                    ShiftTrait(DefaultTraits.Valor, 2);
                    RaiseTheStones();
                    try { NorthmenStonesQuestLog.CompleteSelfSacrifice(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    ShowSelfSacrificeEnding();
                    break;

                case "child":
                    _phase = PhaseEnded;
                    _endingKind = EndingChild;
                    ShiftTrait(DefaultTraits.Mercy, -2);
                    ShiftTrait(DefaultTraits.Valor, 1);
                    RaiseTheStones();
                    try { NorthmenStonesQuestLog.CompleteChildSacrifice(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    ShowChildSacrificeEnding();
                    break;

                default: // disagree
                    _phase = PhaseEnded;
                    _endingKind = EndingDisagree;
                    ShiftTrait(DefaultTraits.Honor, -1);
                    ShiftTrait(DefaultTraits.Mercy, 1);
                    ApplyNorthmenVendetta();
                    try { NorthmenStonesQuestLog.CompleteDisagree(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    ShowDisagreeEnding();
                    break;
            }
        }

        private static void RaiseTheStones()
        {
            _stoneBuilt = true;
            _lastEmberfallDay = CurrentCampaignDay();
        }

        private static void ShiftTrait(TraitObject trait, int delta)
        {
            try
            {
                Hero h = Hero.MainHero;
                if (h == null || trait == null) return;
                int v = h.GetTraitLevel(trait);
                h.SetTraitLevel(trait, System.Math.Max(-2, System.Math.Min(2, v + delta)));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ApplyNorthmenVendetta()
        {
            try
            {
                Kingdom northmen = NorthmenKingdom();
                if (northmen == null) return;
                foreach (Hero lord in northmen.Heroes.Where(h => h != null && h.IsLord && h.IsAlive).ToList())
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -100, false); }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ShowDisagreeEnding()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Working Fails",

                    "You give the seer your answer, and watch the fire go out of her eyes.\n\n" +
                    "\"Then it was all for nothing. The iron, the silver, the Kindled we bled to bind and you " +
                    "led here yourself — spent on a ring of dead stone.\"\n\n" +
                    "Word travels north faster than you do. You will not be welcome in a Northmen hall again — " +
                    "not as a guest, and not as anything safer than a hunted thing. They will not forget the " +
                    "one who was asked for everything, and gave nothing, at the very end.",

                    true, false, "So be it.", "",
                    () => { }, () => { }
                ), true, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ShowSelfSacrificeEnding()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Final Spark",

                    "There is no ceremony to it beyond what the stones themselves demand.\n\n" +
                    "You feel your own fire drawn out of you and into the ring — not torn, given — and for a " +
                    "moment you understand exactly what the seers meant: iron and silver and bound Kindled " +
                    "were never going to be enough on their own. They needed something that chose to burn.\n\n" +
                    "The Bonefire Circle stands at Varcheg. You will not see what it does.",

                    true, false, "It is done.", "",
                    () => { try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    () => { }
                ), true, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ShowChildSacrificeEnding()
        {
            try
            {
                var children = EligibleChildren();
                Hero child = children.Count > 0 ? children[_rng.Next(children.Count)] : null;
                Hero spouse = Hero.MainHero?.Spouse;

                string childName = child?.Name?.ToString() ?? "your child";

                InformationManager.ShowInquiry(new InquiryData(
                    "The Final Spark",

                    $"\"Blood of yours will answer for yours,\" the seer says, and you understand, too late " +
                    "to unsay it, exactly what that means.\n\n" +
                    $"{childName} does not understand what is happening until the very end. The stones do not " +
                    "care whose fire they are given, only that it is truly bound to yours. It is.\n\n" +
                    "The Bonefire Circle stands at Varcheg. You live to see it. That is not entirely a mercy.",

                    true, false, "It is done.", "",
                    () =>
                    {
                        try
                        {
                            if (child != null) KillCharacterAction.ApplyByMurder(child, Hero.MainHero, false);
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        try
                        {
                            if (spouse != null && spouse.IsAlive)
                            {
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, -100, false);
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{spouse.Name} cannot forgive what you did, and will not stay for it.",
                                    new Color(0.55f, 0.55f, 0.60f)));
                            }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    () => { }
                ), true, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── The Greater Emberfall ─────────────────────────────────────────────
        private void EmberfallDailyTick()
        {
            if (!_stoneBuilt) return;
            int day = CurrentCampaignDay();
            if (_lastEmberfallDay >= 0 && day - _lastEmberfallDay < NorthmenStonesMath.EmberfallIntervalDays) return;
            _lastEmberfallDay = day;

            try
            {
                var candidates = Settlement.All
                    .Where(s => s != null && s.IsTown && s.Town != null && AshenCitySystem.IsAshenFaction(s.MapFaction))
                    .ToList();
                if (candidates.Count == 0) return;
                Settlement target = candidates[_rng.Next(candidates.Count)];

                try { target.Town.Prosperity *= NorthmenStonesMath.EmberfallStatRemainingFrac; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { target.Town.Security   *= NorthmenStonesMath.EmberfallStatRemainingFrac; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { target.Town.FoodStocks *= NorthmenStonesMath.EmberfallStatRemainingFrac; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                int killed = 0;
                try
                {
                    var garrison = target.Town.GarrisonParty?.MemberRoster;
                    if (garrison != null)
                    {
                        foreach (var e in garrison.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero || e.Number <= 0) continue;
                            int kill = (int)(e.Number * NorthmenStonesMath.EmberfallGarrisonKillFrac);
                            if (kill <= 0) continue;
                            garrison.AddToCounts(e.Character, -kill);
                            killed += kill;
                        }
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"The Bonefire Circle turns toward {target.Name}. Fire that answers to no living hand pours " +
                    $"over the walls — {killed} of its soldiers do not answer muster, and its stores burn with them."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
