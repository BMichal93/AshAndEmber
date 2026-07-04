// =============================================================================
// ASH AND EMBER — CampaignMapEvents.Branded.cs
// "The Branded" — a mage lord's inner fire is consuming them.
// The player can Harvest (repay the fire's debt on their years; the lord dies),
// Soothe (steady the lord's fire at a small cost), or Leave (do nothing).
// Partial of CampaignMapEvents (shared state lives in CampaignMapEvents.cs).
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

namespace AshAndEmber
{
    public static partial class CampaignMapEvents
    {
        // ── The Branded ────────────────────────────────────────────────────────
        // A mage lord's inner fire runs too bright — they are burning themselves
        // from the inside. The player, as a fellow fire-carrier, is the only one
        // who can see it. This is a moment of choice: take from them, or let it pass.
        //
        // Gate: player must be an active mage (not Ashen — the cold cannot feel another's heat).
        // Candidate: a non-Ashen colour lord who is alive, not the player, not a clan leader
        //            (clan leaders have too much plot armour to dissolve cleanly mid-campaign).
        internal static void TryFireTheBranded()
        {
            // Cheap eligibility gates must run BEFORE claiming the weekly slot.
            // If the slot were claimed first and then returned (the common non-mage
            // case), the 14-day event cooldown would start with no event fired,
            // silently suppressing all other world events for two weeks.
            if (!MageKnowledge.IsMage || MageKnowledge.IsAshen) return;
            if (Hero.MainHero == null) return;
            if (MageKnowledge._deferredInquiry != null) return;
            if (_rng.NextDouble() >= ChanceTheBranded) return;
            if (!TryClaimWeeklySlot()) return;

            try
            {
                var candidates = Hero.AllAliveHeroes
                    .Where(h =>
                        h.IsLord && h.IsAlive && !h.IsChild && !h.IsPrisoner
                        && h != Hero.MainHero
                        && ColourLordRegistry.IsColourLord(h)
                        && !ColourLordRegistry.IsAshenLord(h)
                        && h.Clan?.Leader != h
                        && h.Clan?.Heroes.Count(x => x.IsAlive && !x.IsChild) >= 2)
                    .ToList();

                if (candidates.Count == 0) return;

                var branded = candidates[_rng.Next(candidates.Count)];
                string brandedName = branded.Name?.ToString() ?? "a mage lord";
                string clanName    = branded.Clan?.Name?.ToString() ?? "their house";

                MageKnowledge._deferredInquiry = () => ShowBrandedEvent(branded, brandedName, clanName);
            }
            catch { }
        }

        private static void ShowBrandedEvent(Hero branded, string brandedName, string clanName)
        {
            if (branded == null || !branded.IsAlive)
            {
                MageKnowledge._deferredInquiry = null;
                return;
            }

            // The life-harvest answers the BLOOD discipline (the merged art's heir to
            // the retired Reap talent — legacy owners keep the right).
            bool canHarvest = MageElementKnowledge.HasBlood || TalentSystem.Has(TalentId.Reap);

            string affirmLabel = canHarvest ? "Harvest the fire" : "Leave them to it";
            string affirmDesc  = canHarvest
                ? "Draw the escaping heat into yourself. It will kill them — but their fire will add years to yours."
                : "You carry the fire too. You know what this looks like, and you will not touch it.";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Branded",
                $"The fire has turned inward in {brandedName} of {clanName}. You feel it before you see it — " +
                $"a shimmer at the edge of your awareness, the way a forge glows through a wall. " +
                $"They do not know it yet, or they do and cannot stop it. Their inner fire is consuming " +
                $"them from the marrow outward. It will not stop on its own.\n\n" +
                $"You are the only one present who can see it for what it is.",
                new List<InquiryElement>
                {
                    new InquiryElement("harvest",
                        canHarvest ? "Harvest the fire — draw it out, take what is given." : "Leave them. This is not yours to take.",
                        null, true,
                        canHarvest
                            ? "Fifteen days of your fire's debt repaid. They will not survive the drawing."
                            : "You watch, and you walk away."),
                    new InquiryElement("leave",
                        canHarvest ? "Leave. Let it run its course." : "Offer what little steadying you can.",
                        null, true,
                        canHarvest
                            ? "They will almost certainly die within the month regardless. You gain nothing."
                            : "You cannot save them, but you can give them a cleaner few days at the end. −3 days of youth for the effort."),
                },
                false, 1, 1, "Decide", "",
                sub =>
                {
                    string choice = sub?[0]?.Identifier as string;
                    switch (choice)
                    {
                        case "harvest" when canHarvest:
                            OnBrandedHarvest(branded, brandedName);
                            break;
                        case "leave" when !canHarvest: // labelled "Offer what little steadying you can"
                            OnBrandedSoothe(branded, brandedName);
                            break;
                        default: // walk away — "leave" with the Blood right, "harvest" without it
                            OnBrandedLeave(branded, brandedName);
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void OnBrandedHarvest(Hero branded, string brandedName)
        {
            try
            {
                // The harvest repays the fire's debt (life expectancy), the same
                // ledger the Blood discipline feeds — it does not de-age the body.
                AgingSystem.RestoreLifeExpectancy(Hero.MainHero, 15);
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"You draw the fire from {brandedName}. For a moment there is warmth — real warmth, " +
                    $"the kind that restores. Then it is over. Fifteen days of your fire's debt, repaid. " +
                    $"They are nothing at all."));
                try
                {
                    if (branded.IsAlive)
                        KillCharacterAction.ApplyByOldAge(branded, true);
                }
                catch { }
                try
                {
                    if (branded.MapFaction is Kingdom k)
                        ChangeCrimeRatingAction.Apply(k, 20f, true);
                }
                catch { }
                try { ShiftTrait(DefaultTraits.Mercy, -1); } catch { }
            }
            catch { }
        }

        private static void OnBrandedLeave(Hero branded, string brandedName)
        {
            MBInformationManager.AddQuickInformation(new TextObject(
                $"You watch {brandedName} from a distance and walk away. " +
                $"The fire in them flares bright and then is gone. You carry nothing from this."));
        }

        private static void OnBrandedSoothe(Hero branded, string brandedName)
        {
            try
            {
                // The steadying is paid for as advertised — 3 days of the player's years.
                try { AgingSystem.AgeHero(Hero.MainHero, 3); } catch { }
                try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, branded, 15, false); } catch { }
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"You reach in and steady {brandedName}'s fire — carefully, at a cost you feel but cannot measure. " +
                    $"They will carry the mark, but they will carry it longer. " +
                    $"They do not fully understand what you did. They feel only that they owe you something they cannot name."));
            }
            catch { }
        }

        private static void ShiftTrait(TraitObject trait, int delta)
        {
            try
            {
                if (Hero.MainHero == null) return;
                int cur = Hero.MainHero.GetTraitLevel(trait);
                int next = Math.Max(-3, Math.Min(3, cur + delta));
                if (next != cur) Hero.MainHero.SetTraitLevel(trait, next);
            }
            catch { }
        }
    }
}
