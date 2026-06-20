// =============================================================================
// ASH AND EMBER — SettlementEncounters.Events4.cs
// Wasting, frenzy, grimoire, night visitor, broken seal, deferred consequences.
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
        // ── Deferred: FirePregnancyConsequence — 30 days after female-player outcome 1 ──
        private static void FirePregnancyConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _pregnancyCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                try { MakePregnantAction.Apply(Hero.MainHero); } catch { }
                Hero husband = Hero.MainHero?.Spouse;
                if (husband != null && husband.IsAlive)
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, husband, -50, false); } catch { }
                    Msg($"You are with child — and {husband.Name} knows it is not his. ({husband.Name} −50 relation)", BadColor);
                }
                else
                    Msg("You are with child. The road ahead looks different than it did a month ago.", DimColor);
            };
        }

        private static void PenaliseSpouseForAdoption()
        {
            try
            {
                Hero spouse = Hero.MainHero.Spouse;
                if (spouse == null || !spouse.IsAlive) return;
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, -20, false);
                Msg($"({spouse.Name} −20 relation)", BadColor);
            }
            catch { }
        }

        // ── Condition helpers ──────────────────────────────────────────────────
        private static bool HasSpouseAndChild()
        {
            try
            {
                if (Hero.MainHero?.Spouse == null || !Hero.MainHero.Spouse.IsAlive) return false;
                return Hero.AllAliveHeroes.Any(h =>
                    h.IsAlive && !h.IsDisabled &&
                    (h.Father == Hero.MainHero || h.Mother == Hero.MainHero));
            }
            catch { return false; }
        }

        // ── Helper: remove troops from garrison then party ─────────────────────
        private static void SacrificeTroops(Settlement s, int count)
        {
            int remaining = count;
            try
            {
                var garrison = s?.Town?.GarrisonParty?.MemberRoster;
                if (garrison != null)
                {
                    foreach (var e in garrison.GetTroopRoster().ToList())
                    {
                        if (e.Character.IsHero) continue;
                        int take = Math.Min(e.Number, remaining);
                        if (take <= 0) continue;
                        garrison.AddToCounts(e.Character, -take);
                        remaining -= take;
                        if (remaining <= 0) break;
                    }
                }
            }
            catch { }
            if (remaining > 0)
            {
                try
                {
                    var roster = MobileParty.MainParty?.MemberRoster;
                    if (roster != null)
                    {
                        foreach (var e in roster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            int take = Math.Min(e.Number, remaining);
                            if (take <= 0) continue;
                            roster.AddToCounts(e.Character, -take);
                            remaining -= take;
                            if (remaining <= 0) break;
                        }
                    }
                }
                catch { }
            }
            int sacrificed = count - remaining;
            if (sacrificed > 0) Msg($"({sacrificed} troops consumed by the ritual)", BadColor);
        }

        // ── E_TheWasting — enter village/city, requires spouse + living child ──
        // A strange wasting sickness takes hold of the player's family.
        private static void E_TheWasting(Settlement s)
        {
            Hero spouse = Hero.MainHero?.Spouse;
            Hero child  = Hero.AllAliveHeroes.FirstOrDefault(h =>
                h.IsAlive && !h.IsDisabled &&
                (h.Father == Hero.MainHero || h.Mother == Hero.MainHero));

            if (spouse == null || child == null) return;
            _familyFeverCooldown = 600;

            bool mage          = MageKnowledge.IsMage;
            bool isAshen       = MageKnowledge.IsAshen;
            bool hasDarkTalent = mage && (TalentSystem.Has(TalentId.Ember)
                                       || TalentSystem.Has(TalentId.Reap));
            bool isNature      = NatureKnowledge.IsAttuned;

            string spouseName = spouse.Name?.ToString() ?? "your spouse";
            string childName  = child.Name?.ToString()  ?? "your child";

            string cHint = mage
                ? $"The fire through both of them at once. Not a thing meant to be done like this."
                : "Requires mage ability.";
            string dHint = hasDarkTalent
                ? $"The ritual requires something living. It requires a lot of it."
                : "Requires Ember or Reap talent.";
            string nHint = isNature
                ? "Let the living world work through them. It takes what it needs from you — your blood, not your years."
                : "Requires attunement to The Living Ember.";
            string ashenHint = isAshen
                ? "The cold holds them in stillness — suspended between life and letting go. They will survive. The cold always asks something back."
                : "Requires Ashen affinity.";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Wasting",
                $"A rider catches you on the road with bad news: a strange wasting sickness has taken hold of both {spouseName} and {childName} at the same time. The healers have done what they can. They can sustain one of them — not both. By the time you arrive, the choice is already framed in the doorway. There is not much time.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", $"Save {spouseName}.", null, true,
                        $"You put everything into one of them. That is the shape of this decision."),
                    new InquiryElement("b", $"Save {childName}.", null, true,
                        $"You put everything into one of them. That is the shape of this decision."),
                    new InquiryElement("c", "Channel the fire through both of them.", null, mage, cHint),
                    new InquiryElement("n", "Let nature breathe through both of them.", null, isNature, nHint),
                    new InquiryElement("d", "Perform a dark ritual to sustain them.", null, hasDarkTalent, dHint),
                    new InquiryElement("cold_hold", "Hold them in the cold between living and dying.", null, isAshen, ashenHint),
                    new InquiryElement("e", "Make a pact with the cold. Pay whatever it asks.", null, true,
                        $"The cold accepts immediately. They survive. What you become is the cost."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            try { KillCharacterAction.ApplyByMurder(child, null, false); } catch { }
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, -20, false); } catch { }
                            Msg($"You put everything into {spouseName}. The fever breaks. {childName} does not wake. {spouseName} knows what you chose, and what it cost, and does not yet know what to do with either of those things.", BadColor);
                            break;
                        case "b":
                            try { KillCharacterAction.ApplyByMurder(spouse, null, false); } catch { }
                            Msg($"You put everything into {childName}. The fever breaks. {spouseName} does not wake. {childName} will be older before they understand what happened in that room. You will have to decide what to tell them.", BadColor);
                            break;
                        case "c":
                            AgePlayer(3650); // 10 years
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, 10, false); } catch { }
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, child,  10, false); } catch { }
                            Msg($"You put the fire through both of them at once — not a thing that is meant to be done like this, not a thing you will be able to explain. It costs ten years. They both wake. {spouseName} holds your face when you come back to yourself and does not ask what you gave. {childName} is already asking for food.", FireColor);
                            break;
                        case "n":
                            try { Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - 40); } catch { }
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, 8, false); } catch { }
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, child,  8, false); } catch { }
                            Msg($"You sit between them and open yourself to the living world — not the fire, not the cold, but the quiet warmth that runs through roots and rivers and the palms of living hands. It moves through you and into them. The sickness does not burn away; it simply has no purchase in something the land has touched. They both wake, slowly, over the course of a night. {spouseName} watches your face the whole time, understanding nothing but the cost. {childName} does not understand either, but they take your hand when it is over.",
                                new TaleWorlds.Library.Color(0.35f, 0.75f, 0.35f));
                            break;
                        case "cold_hold":
                            AgePlayer(1825); // 5 years
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, -10, false); } catch { }
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, child,  -10, false); } catch { }
                            Msg($"You hold the cold around them like a hand cupping a flame — not to extinguish, but to suspend. The sickness cannot spread in stillness. It costs you five years and the warmth of the next two weeks, and when {spouseName} and {childName} wake they are whole but quieter than they were. They look at you as if they went somewhere while they slept and you were what brought them back, and they are not entirely certain how they feel about the place you pulled them from.", AshenColor);
                            break;
                        case "d":
                            try
                            {
                                Hero.MainHero.SetTraitLevel(DefaultTraits.Mercy, -2);
                                ShiftTrait(DefaultTraits.Honor, -1);
                                SacrificeTroops(s, 100);
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, -20, false);
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, child,  -20, false);
                            }
                            catch { }
                            Msg($"The ritual requires something living and it requires a lot of it. One hundred of your soldiers do not wake up. You do not watch. {spouseName} and {childName} survive, fever-broken, and when they look at you afterward there is something in it that was not there before. You do not explain what you did. They do not ask. Both of you prefer it this way.", DarkColor);
                            break;
                        case "e":
                            BecomeAshen();
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, -50, false); } catch { }
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, child,  -50, false); } catch { }
                            Msg($"The cold accepts the offer immediately, as though it had been waiting. {spouseName} and {childName} wake — both of them, at the same moment, as if pulled back by the same thread. They look at you and something in both of their faces shifts before they can hide it. You are different now. The grey is already in your eyes. They are alive. They know what it cost.", AshenColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── HasFamilyOrCompanions ──────────────────────────────────────────────
        private static bool HasFamilyOrCompanions()
        {
            try
            {
                if (Hero.MainHero?.Spouse?.IsAlive == true) return true;
                if (Hero.AllAliveHeroes.Any(h =>
                        h.IsAlive && !h.IsDisabled && h.Age < 18 &&
                        (h.Father == Hero.MainHero || h.Mother == Hero.MainHero)))
                    return true;
                return Hero.AllAliveHeroes.Any(h =>
                    h.IsAlive && !h.IsDisabled && !h.IsPrisoner &&
                    h != Hero.MainHero &&
                    h.PartyBelongedTo == MobileParty.MainParty);
            }
            catch { return false; }
        }

        // ── ApplyAshenFrenzyDamage — shared kill logic for B / A-fail ─────────
        private static void ApplyAshenFrenzyDamage()
        {
            bool anyKilled = false;

            Hero spouse = Hero.MainHero?.Spouse;
            if (spouse != null && spouse.IsAlive)
            {
                try { KillCharacterAction.ApplyByMurder(spouse, Hero.MainHero, false); } catch { }
                Msg($"({spouse.Name} killed)", BadColor);
                anyKilled = true;
            }

            var children = Hero.AllAliveHeroes
                .Where(h => h.IsAlive && !h.IsDisabled && h.Age < 18 &&
                            (h.Father == Hero.MainHero || h.Mother == Hero.MainHero))
                .ToList();
            foreach (var ch in children)
            {
                try { KillCharacterAction.ApplyByMurder(ch, Hero.MainHero, false); } catch { }
                Msg($"({ch.Name} killed)", BadColor);
                anyKilled = true;
            }

            if (!anyKilled)
            {
                var companions = Hero.AllAliveHeroes
                    .Where(h => h.IsAlive && !h.IsDisabled && !h.IsPrisoner &&
                                h != Hero.MainHero &&
                                h.PartyBelongedTo == MobileParty.MainParty)
                    .ToList();
                if (companions.Count > 0)
                {
                    var victim = companions[_rng.Next(companions.Count)];
                    try { KillCharacterAction.ApplyByMurder(victim, Hero.MainHero, false); } catch { }
                    Msg($"({victim.Name} killed)", BadColor);
                }
            }
        }

        // ── FireAshenFrenzy — deferred, fires the day after BecomeAshen ───────
        private static void FireAshenFrenzy()
        {
            if (MageKnowledge._deferredInquiry != null) { _ashenFrenzyCountdown = 1; return; }

            float leadChance = SkillChance(DefaultSkills.Leadership, 0.35f);
            string leadHint  = SkillHint(DefaultSkills.Leadership, 0.35f, "Force of will — hold back the hunger");

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "★  The First Hunger",
                    "You wake before dawn and cannot say what woke you. The grey light from the window is wrong. The air is wrong. There is a sound beneath the silence — not a sound, a pressure — and the fire you used to carry has changed into something that does not distinguish between wood and flesh, between warmth given and warmth taken. It wants. It does not care what you want. The faces of the people closest to you move through your mind the way flame moves through dry straw: not as memory but as inventory.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", $"Fight it. ({(int)(leadChance * 100)}% Leadership)", null, true,
                            leadHint),
                        new InquiryElement("b", "Let it take you. You are what you are now.", null, true,
                            "You stop resisting. You will not remember all of what follows."),
                        new InquiryElement("c", "Kill yourself before it uses you.", null, true,
                            "The last decision that is entirely yours."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "a":
                                if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                                {
                                    Msg("You find something to hold onto — a name, a specific memory, the weight of a specific obligation — and you press it between yourself and the thing that is pulling. It is like holding a door shut with your hands. The pull does not stop. But it does not get through. Not this time. You are still here. You are still choosing. That will have to be enough.", FireColor);
                                }
                                else
                                {
                                    Msg("You try. The thing that is using your hands does not try. It simply moves. You come back to yourself afterward, in a room that is wrong, with the knowledge of what your hands did arriving a second after the sight of it. You failed to hold it. This is what failure costs.", BadColor);
                                    ApplyAshenFrenzyDamage();
                                }
                                break;
                            case "b":
                                Msg("You stop resisting. What happens next comes in flashes: a face you love gone pale, a sound you will not repeat to yourself, the cold smell of the thing you have become doing what it does when nothing holds it back. You surface sometime later. The room is different than when you left it. So are you.", BadColor);
                                ApplyAshenFrenzyDamage();
                                break;
                            case "c":
                                Msg("You make the decision clearly, with both hands, before the hunger can use them for anything else. It is the last decision that is entirely yours. Nobody else dies.", DimColor);
                                try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, false); } catch { }
                                break;
                        }
                    }, null, "", false), false, true);
            };
        }

        // ── ES_AncientGrimoire — siege won, mage, one-time ────────────────────
        // A strange book found in the conquered keep's sealed archive.
        private static void ES_AncientGrimoire()
        {
            _ancientBookFound = 1;

            void ShowRitePrompt()
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "★  What the Book Describes",
                    "The rituals are specific and detailed — not the vague symbolism of cult texts but something written by someone who had done them and was writing down what worked. The central one describes a working to rekindle a depleted fire-gift at the cost of the lives surrounding it. Not metaphorically. The warmth of the living, taken in bulk, pressed back into a fire-carrier who has burned too low. The author's notes in the margin suggest it was tested. They suggest it worked.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", "Discard it. You have read enough.", null, true,
                            "You set it down. Someone else will find it eventually."),
                        new InquiryElement("b", "Perform the rite.", null, true,
                            "The author tested this. They wrote that it worked."),
                        new InquiryElement("c", "Report it to the nearest temple. This should not exist.", null, true,
                            "The temple will know what to do with it. They have handled this before."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen2 =>
                    {
                        switch (chosen2?[0]?.Identifier as string)
                        {
                            case "a":
                                Msg("You set the book face-down on the table and leave it there. Someone will find it when they clear the keep. That is their problem now. You are not certain you made the right decision, but you made a decision, and that is enough for tonight.", DimColor);
                                break;
                            case "b":
                                try
                                {
                                    Hero.MainHero.SetTraitLevel(DefaultTraits.Honor, -2);
                                    Hero.MainHero.SetTraitLevel(DefaultTraits.Mercy, -2);
                                }
                                catch { }
                                // Grant Reap if not owned, else attribute point
                                if (!TalentSystem.Has(TalentId.Reap))
                                    TalentSystem.GrantFree(TalentId.Reap, Hero.MainHero);
                                else
                                    try { Hero.MainHero.HeroDeveloper.UnspentAttributePoints += 1; } catch { }
                                KillHalfParty();
                                Msg("The working is exactly what the book said it was — which is to say it is the worst thing you have done. Your soldiers fall between one breath and the next, not in pain, just gone. The fire in you surges in a way that makes the preceding days feel like ash. You are standing in a room full of people who trusted you, and half of them are not standing anymore. The book's author was correct. It works.", BadColor);
                                break;
                            case "c":
                                if (!ChangeGold(-500)) return;
                                ChangeRenown(10f);
                                ShiftTrait(DefaultTraits.Honor, 1);
                                Msg("You have the book wrapped and sealed for transport. The temple receives it with the grim recognition of people who have handled this category of thing before. The courier confirms delivery. You receive formal acknowledgement and a note of thanks that does not begin to cover what you have handed them. The renown is a side-effect — what you actually did was make sure no one else reads that margin note and decides to test the method.", GoodColor);
                                break;
                        }
                    }, null, "", false), false, true);
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Sealed Archive",
                "Your men found it behind a false wall in the keep's lower study — a sealed room, clearly personal, clearly not meant to be entered by whoever came next. Inside: a single book, handwritten, with a lock that took three of your people an hour to open. The title page has no author and no date. The first ten pages are in a cipher. The next two hundred are not.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Burn it. Some things are better unread.", null, true,
                        "Some things are better unread."),
                    new InquiryElement("b", "Read it.", null, true,
                        "See what it contains."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You burn it in the keep's hearth without reading beyond the first page. The fire takes it quickly — more quickly than paper should. Whatever was in the cipher, it goes with the rest. The room feels different when you leave it. Not better. Just different.", GoodColor);
                            break;
                        case "b":
                            MageKnowledge._deferredInquiry = ShowRitePrompt;
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Helper: kill 50% of non-hero party troops ──────────────────────────
        private static void KillHalfParty()
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                int total = roster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.Number > 0)
                    .Sum(e => e.Number);
                int toKill = total / 2;
                if (toKill <= 0) return;
                int killed = 0;
                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.Number <= 0) continue;
                    int take = Math.Min(e.Number, toKill - killed);
                    if (take <= 0) break;
                    roster.AddToCounts(e.Character, -take);
                    killed += take;
                    if (killed >= toKill) break;
                }
                if (killed > 0) Msg($"({killed} troops consumed by the rite)", BadColor);
            }
            catch { }
        }

        // ── HasHedgeWitchCondition ─────────────────────────────────────────────
        private static bool HasHedgeWitchCondition()
        {
            try
            {
                Hero h = Hero.MainHero;
                Hero spouse = h?.Spouse;
                if (spouse == null || !spouse.IsAlive) return false;
                if (h.Age < 40f || spouse.Age < 40f) return false;
                if (Hero.AllAliveHeroes.Any(c =>
                        c.IsAlive && !c.IsDisabled &&
                        (c.Father == h || c.Mother == h))) return false;
                if (spouse.GetTraitLevel(DefaultTraits.Honor) >= 1) return false;
                if (spouse.GetTraitLevel(DefaultTraits.Mercy)  >= 1) return false;
                return true;
            }
            catch { return false; }
        }

        // ── E_NightVisitor — enter village/city, conditional ──────────────────
        // A servant reports a strange figure visiting the player's spouse at night.
        private static void E_NightVisitor(Settlement s)
        {
            Hero spouse = Hero.MainHero?.Spouse;
            if (spouse == null) return;
            _hedgeWitchCooldown = 300;

            string spouseName = spouse.Name?.ToString() ?? "your spouse";
            float scoutChance = SkillChance(DefaultSkills.Scouting, 0.35f);
            string scoutHint  = SkillHint(DefaultSkills.Scouting, 0.35f, "Follow the figure without being seen");

            void ShowRevelation()
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "★  What Your Spouse Has Done",
                    $"The figure is a hedge witch — old, deliberate, carrying things in a bag that clink wrong. This is not her first visit. {spouseName} receives her without surprise: they have spoken before, many times, in the hours before dawn when the house is asleep. You piece it together quickly. The herbs, the cost, the quiet desperation of someone who has watched time run and decided to reach for something the healers will not offer. She has been trying to give you children before the years close that door entirely.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", "Say nothing. It is kind of them.", null, true,
                            "Something may come of it. Something else may come of it too."),
                        new InquiryElement("b", "Hang the witch.", null, true,
                            "You are not a patient person."),
                        new InquiryElement("c", $"Kill them both — the witch and {spouseName} — in fury.", null, true,
                            "The fury decides for you. Both of them."),
                        new InquiryElement("d", "Tell the witch to go and never come back.", null, true,
                            "You end it quietly. No blood, no answers."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen2 =>
                    {
                        switch (chosen2?[0]?.Identifier as string)
                        {
                            case "a":
                                ShiftTrait(DefaultTraits.Honor, -1);
                                // 20% fertility boost: attempt pregnancy for the appropriate hero
                                if (_rng.NextDouble() < 0.20)
                                {
                                    Hero target = (Hero.MainHero?.IsFemale == true)
                                        ? Hero.MainHero : spouse;
                                    try { MakePregnantAction.Apply(target); } catch { }
                                }
                                _hedgeWitchCurse = 7;
                                Msg($"You say nothing and leave the way you came. {spouseName} never knows you were there. The witch departs before dawn. You carry the knowledge of it without speaking it. Whatever was agreed in that room begins to work. Seven days later, so does everything else.", DimColor);
                                break;
                            case "b":
                                ShiftTrait(DefaultTraits.Calculating, -1);
                                Msg("You have the witch taken before she leaves the grounds. She does not argue. She asks only that you know what you are stopping. You have her hanged before noon. {spouseName} does not speak for three days. Neither do you.", BadColor);
                                break;
                            case "c":
                                ShiftTrait(DefaultTraits.Calculating, -1);
                                try { Hero.MainHero.SetTraitLevel(DefaultTraits.Mercy, -2); } catch { }
                                try { KillCharacterAction.ApplyByMurder(spouse, Hero.MainHero, false); } catch { }
                                Msg($"The fury comes before the thought. The witch is first — she had time to understand what was happening. {spouseName} had less. You surface an hour later in a room that cannot be unchanged. What was done out of love and desperation is done. So is {spouseName}.", BadColor);
                                break;
                            case "d":
                                Msg("You step into the room before {spouseName} can speak. You tell the witch to go — calmly, with enough in your voice that she understands this is the last visit. She leaves. {spouseName} watches you with something that is not quite relief and not quite anger. You do not discuss it. The door stays between you for a long time.", DimColor);
                                break;
                        }
                    }, null, "", false), false, true);
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "♦  The Visitor at Night",
                $"One of your servants finds you before you have finished your first cup of the morning. They are careful with their words — a strange figure, they say, has been seen entering the house in the hours before dawn. Not a burglar: too deliberate, too familiar with the layout, too expected by whoever let them in. They look at you and wait to see what you want to do with that.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Leave it be.", null, true,
                        "Whatever is happening in your house continues without you."),
                    new InquiryElement($"b", $"Investigate. ({(int)(scoutChance * 100)}% Scouting)", null, true,
                        scoutHint),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            Msg("You tell the servant you heard them and will handle it. You do not handle it. Whatever is happening in your house at odd hours continues to happen without your interference. You are not certain if that is restraint or avoidance.", DimColor);
                            break;
                        case "b":
                            if (SkillRoll(DefaultSkills.Scouting, 0.35f))
                                MageKnowledge._deferredInquiry = ShowRevelation;
                            else
                                Msg("You watch the house for three mornings without finding anything out of the ordinary. Whatever the servant saw, the timing was either coincidence or whoever it was has learned to move more carefully. The question stays open.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── EC_BrokenSeal — enter city/castle, general ───────────────────────────
        // Player stumbles on evidence of a secret inter-kingdom plot.
        // Four discovery variants; three plot types; deferred 3-day consequence.
        private static void EC_BrokenSeal(Settlement s)
        {
            var kingdoms = Kingdom.All
                .Where(k => !k.IsEliminated && k.Leader != null && k.Leader.IsAlive
                         && (Hero.MainHero?.MapFaction == null || k != Hero.MainHero.MapFaction)
                         && k.StringId != "ashen_kingdom")
                .ToList();
            if (kingdoms.Count < 2) return;

            int idxA = _rng.Next(kingdoms.Count);
            int idxB;
            do { idxB = _rng.Next(kingdoms.Count); } while (idxB == idxA);
            Kingdom kA = kingdoms[idxA];
            Kingdom kB = kingdoms[idxB];

            int plotType = _rng.Next(3) + 1;
            string plotDesc = plotType switch
            {
                1 => $"The document is an order of march. {kA.Name} is prepared to declare war on {kB.Name} — messengers ride in three days.",
                2 => $"The documents describe a quiet land-grab: a manufactured claim, bribed garrison captains, timed troop movements. {kA.Name} intends to take one of {kB.Name}'s castles without a formal declaration.",
                _ => $"The instructions are for saboteurs already inside {kB.Name}'s borders — agents moving toward a specific city, with orders to leave it in disorder while keeping {kA.Name}'s hands clean."
            };

            int variant = _rng.Next(4);
            string discovery = variant switch
            {
                0 => $"Your outriders found a body half off the road a mile back — a courier in {kA.Name}'s colours, throat cut, stripped of valuables but not of the letters inside his coat. One of your men broke the seal before thinking better of it and handed the pages over.",
                1 => $"Two men in the back corner of the tavern were speaking in the careful lowered voices of people who believe they cannot be heard. They were wrong. Between their cups you caught the shape of it — a plan, a target, a name: {kB.Name}.",
                2 => $"A soldier in {kA.Name}'s colours, three cups past sober and pleased with himself, drifted to your table and began talking about things soldiers are not supposed to discuss in public places. His sergeant will be furious tomorrow. You, however, are not.",
                _ => $"The serving girl slid a folded note under your cup as she cleared it and leaned close enough to name a price. She had lifted it from a {kA.Name} messenger an hour ago — their seal intact, {kB.Name}'s name on the outside, and something inside that she had already read and could not unread."
            };

            _brokenSealKingdomAId = kA.StringId;
            _brokenSealKingdomBId = kB.StringId;
            _brokenSealPlotType   = plotType;
            _brokenSealExtraWar   = false;

            float scoutChance = SkillChance(DefaultSkills.Scouting, 0.30f);
            string scoutHint  = SkillHint(DefaultSkills.Scouting, 0.30f, $"Reach {kB.Name}'s court in time");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Broken Seal",
                $"{discovery}\n\n{plotDesc} You now know something you were not meant to know.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ignore it. Not your concern.", null, true,
                        "Three days and whatever comes of it comes without your involvement."),
                    new InquiryElement("b", $"Ride hard and warn {kB.Name}. ({(int)(scoutChance * 100)}% Scouting, then Charm)", null, true,
                        scoutHint),
                    new InquiryElement("c", $"Get word to {kA.Name} — their secret is worth something to them.", null, true,
                        "They pay for your silence. The plot proceeds. +1,500 gold, +10 with their leader."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            _brokenSealCountdown = 3;
                            Msg($"You fold the letter along its old creases and move on. Whatever {kA.Name} is planning, it will reach {kB.Name} without your help or your interference.", DimColor);
                            break;

                        case "b":
                            if (SkillRoll(DefaultSkills.Scouting, 0.30f))
                            {
                                if (SkillRoll(DefaultSkills.Charm, 0.30f))
                                {
                                    // Both pass: plot foiled, +10 relation with Kingdom B, Kingdom A declares war
                                    _brokenSealPlotType  = 0;
                                    _brokenSealCountdown = 0;
                                    Hero leaderB = kB.Leader;
                                    if (leaderB != null && leaderB != Hero.MainHero)
                                    {
                                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leaderB, 10, false);
                                        Msg($"(Relation with {leaderB.Name}: +10)", GoodColor);
                                    }
                                    if (!kA.IsAtWarWith(kB))
                                        try { DeclareWarAction.ApplyByDefault(kA, kB); } catch { }
                                    Msg($"They believe you. {kB.Name}'s council moves before you have finished explaining — emergency session, counter-orders written, messengers dispatched. The plot is dead. {kA.Name}, knowing the game is up, drops all pretence of patience and reaches for the only option left.", GoodColor);
                                }
                                else
                                {
                                    // Scouting pass, Charm fail: +5 with leader B, consequence + extra war
                                    _brokenSealCountdown = 3;
                                    _brokenSealExtraWar  = true;
                                    Hero leaderB = kB.Leader;
                                    if (leaderB != null && leaderB != Hero.MainHero)
                                    {
                                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leaderB, 5, false);
                                        Msg($"(Relation with {leaderB.Name}: +5)", GoodColor);
                                    }
                                    Msg($"You arrived in time, showed what you had, argued clearly — and were thanked, politely, and not believed. {kB.Name}'s court categorises people who arrive with dramatic letters as a known type of problem. The warning was noted. It was not acted upon. {kA.Name}'s plan will proceed.", DimColor);
                                }
                            }
                            else
                            {
                                // Scouting fail: too late, same as Ignore
                                _brokenSealCountdown = 3;
                                Msg($"You rode hard and arrived at the wrong gate, the wrong hour, the wrong official. {kB.Name}'s court was unreachable in any useful time. By the time your letter reaches the right desk, three days will have passed. Same result. Different road.", DimColor);
                            }
                            break;

                        case "c":
                        {
                            // Sell the information back to Kingdom A — plot proceeds, player profits
                            _brokenSealCountdown = 3;
                            ChangeGold(1500);
                            Msg("(+1,500 gold)", GoldColor);
                            Hero leaderA = kA.Leader;
                            if (leaderA != null && leaderA != Hero.MainHero)
                            {
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leaderA, 10, false);
                                Msg($"(Relation with {leaderA.Name}: +10)", GoodColor);
                            }
                            Msg($"You find a way to reach {kA.Name}'s people — not with the letter, but with the fact of it: someone nearly broke the seal and you are not that someone. They understand the value of that. The coin arrives quickly and without ceremony. The plan proceeds. {kB.Name} will have no warning.", GoldColor);
                            break;
                        }
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireBrokenSealConsequence — 3 days after EC_BrokenSeal A/B-fail/C ──
        private static void FireBrokenSealConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _brokenSealCountdown = 1; return; }

            var kA        = Kingdom.All.FirstOrDefault(k => k.StringId == _brokenSealKingdomAId);
            var kB        = Kingdom.All.FirstOrDefault(k => k.StringId == _brokenSealKingdomBId);
            int plotType  = _brokenSealPlotType;
            bool extraWar = _brokenSealExtraWar;

            _brokenSealPlotType   = 0;
            _brokenSealExtraWar   = false;
            _brokenSealKingdomAId = null;
            _brokenSealKingdomBId = null;

            if (kA == null || kB == null || kA.IsEliminated || kB.IsEliminated) return;

            void MaybeExtraWar()
            {
                if (extraWar && !kB.IsAtWarWith(kA))
                    try { DeclareWarAction.ApplyByDefault(kB, kA); } catch { }
            }

            switch (plotType)
            {
                case 1: // War
                {
                    if (!kA.IsAtWarWith(kB))
                        try { DeclareWarAction.ApplyByDefault(kA, kB); } catch { }
                    MaybeExtraWar();
                    MageKnowledge._deferredInquiry = () =>
                        Msg($"Three days since the letter. {kA.Name} has declared war on {kB.Name}. You had the order in your hands.", BadColor);
                    break;
                }
                case 2: // Annexation — 50/50
                {
                    bool annexed = false;
                    if (_rng.NextDouble() < 0.5)
                    {
                        var castles = Settlement.All
                            .Where(x => x.IsCastle && x.OwnerClan?.Kingdom == kB
                                     && x.OwnerClan != null && !x.OwnerClan.IsEliminated)
                            .ToList();
                        var lordsA = Hero.AllAliveHeroes
                            .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner
                                     && h.MapFaction == kA && h != Hero.MainHero)
                            .ToList();
                        if (castles.Count > 0 && lordsA.Count > 0)
                        {
                            var castle   = castles[_rng.Next(castles.Count)];
                            var newOwner = lordsA[_rng.Next(lordsA.Count)];
                            string cName = castle.Name.ToString();
                            try { ChangeOwnerOfSettlementAction.ApplyByDefault(newOwner, castle); } catch { }
                            MaybeExtraWar();
                            annexed = true;
                            MageKnowledge._deferredInquiry = () =>
                                Msg($"{cName} now flies {kA.Name}'s banner. The transfer was quick, quiet, and complete. {kB.Name} is still working out how it happened.", BadColor);
                        }
                    }
                    if (!annexed)
                    {
                        MaybeExtraWar();
                        MageKnowledge._deferredInquiry = () =>
                            Msg($"The annexation fell through — wrong timing, or a piece that moved before the rest were ready. {kB.Name} holds what it had. This time.", DimColor);
                    }
                    break;
                }
                case 3: // Sabotage — 50/50
                {
                    bool sabotaged = false;
                    if (_rng.NextDouble() < 0.5)
                    {
                        var towns = Settlement.All
                            .Where(x => x.IsTown && x.Town != null && x.OwnerClan?.Kingdom == kB)
                            .ToList();
                        if (towns.Count > 0)
                        {
                            var target = towns[_rng.Next(towns.Count)];
                            string tName = target.Name.ToString();
                            try
                            {
                                target.Town.Prosperity = Math.Max(10f, target.Town.Prosperity - 300f);
                                target.Town.Security   = Math.Max(0f,  target.Town.Security   - 30f);
                            } catch { }
                            MaybeExtraWar();
                            sabotaged = true;
                            MageKnowledge._deferredInquiry = () =>
                                Msg($"{tName} is in disorder — fires, missing officials, spoiled grain stores. Nobody can name the cause clearly, which was the point. {kA.Name}'s agents have already left.", BadColor);
                        }
                    }
                    if (!sabotaged)
                    {
                        MaybeExtraWar();
                        MageKnowledge._deferredInquiry = () =>
                            Msg($"The saboteurs were caught or turned back. {kB.Name}'s city stands unmarked. Whatever {kA.Name} sent into it, it did not land.", DimColor);
                    }
                    break;
                }
            }
        }

        // ── Deferred: FireHopeMageConsequence — 14 days after LC_YoungMageHope ──────
        private static void FireHopeMageConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _hopeMageConsequenceCountdown = 1; return; }
            var settlement = _hopeMageSettlementId != null
                ? Settlement.All.FirstOrDefault(s => s.StringId == _hopeMageSettlementId)
                : null;
            _hopeMageSettlementId = null;
            string sName = settlement?.Name?.ToString() ?? "that city";

            if (settlement?.Town != null)
            {
                // Drop loyalty to near-zero so Bannerlord's native rebellion system fires within 1-2 days.
                try { settlement.Town.Loyalty  = 5f; } catch { }
                try { settlement.Town.Security = 0f; } catch { }
            }

            MageKnowledge._deferredInquiry = () =>
                Msg($"The walls of {sName} are lit by torches held by the city's own people tonight. " +
                    "The young mage who marched north came back with a name and a story, and the story found every ear that was ready for it. " +
                    $"Loyalty in {sName} has collapsed. The city is on the verge of tearing itself loose from its lord.", AshenColor);
        }

        // ── Deferred: FireMemoryHungerDissolution — 7 days after EV_MemoryHunger choice B ──
        private static void FireMemoryHungerDissolution()
        {
            if (MageKnowledge._deferredInquiry != null) { _memoryHungerCountdown = 1; return; }
            _memoryHungerConsumed  = false;
            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "◆  The End of the Hunger",
                    "You wake before dawn and there is nothing left. Not the fire. Not the cold. Not the name you called yourself before either of them arrived. " +
                    "What is looking at the ceiling of your tent through your eyes is not you. " +
                    "It is not certain what it is. " +
                    "It is aware that this is the end of the person who made the choice that brought it here.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", "I am still here.", null, true,
                            "You are not. But the thing that says so is convincing."),
                    },
                    false, 1, 1, "", "",
                    _ =>
                    {
                        try
                        {
                            if (Hero.MainHero != null && Hero.MainHero.IsAlive)
                                KillCharacterAction.ApplyByOldAge(Hero.MainHero, true);
                        }
                        catch { }
                    }, null, "", false), false, true);
            };
        }

        // ── Deferred: FireHedgeWitchCurse — 7 days after E_NightVisitor choice A ──
        private static void FireHedgeWitchCurse()
        {
            if (MageKnowledge._deferredInquiry != null) { _hedgeWitchCurse = 1; return; }
            WoundPlayer();
            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "♦  The Price of the Bargain",
                    "Seven days after the witch's visit. It begins in the night — not a wound, not a fever in the ordinary sense. Something the witch's working cost that was not disclosed in the agreement, or was disclosed in terms that were easy to misread at the time. You come awake cold, unable to stand, your body doing things that the healers will describe later as 'an acute episode' in the careful way healers describe things they do not understand. It takes a week to pass. Some of it does not pass.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("ok", "Endure it.", null, true, "The price was not explained in advance."),
                    },
                    false, 1, 1, "Endure", "",
                    _ => Msg("You survive it. The healers say you will recover. They mean most of it. Whatever the witch's working extracted as its price, it took it without asking and gave back something approximate.", BadColor),
                    null, "", false), false, true);
            };
        }

        // Grants a random spell or enchantment talent if the player has fewer than 5 of them.
        private static void GrantMagicalTalent()
        {
            int magCount = TalentSystem.AllPurchased
                .Count(id => { var d = TalentSystem.All.FirstOrDefault(x => x.Id == id);
                               return d != null && (d.IsSpell || d.IsEnchantment); });
            if (magCount >= 5) return;
            var available = TalentSystem.All
                .Where(t => (t.IsSpell || t.IsEnchantment) && !TalentSystem.Has(t.Id))
                .ToList();
            if (available.Count == 0) return;
            TalentSystem.GrantFree(available[_rng.Next(available.Count)].Id, Hero.MainHero);
        }

    }
}
