// =============================================================================
// ASH AND EMBER — CampaignBehavior.Events.cs
// Clan/kingdom/peace/settlement hooks, new-game prompt, gift, imperial reassignment.
// Partial of MagicCampaignBehavior (shared static state lives in CampaignBehavior.cs).
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
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public partial class MagicCampaignBehavior
    {
        // ── Ashen city clans + Fire Worshippers ──────────────────────────────
        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            try { AshenCitySystem.OnClanChangedKingdom(clan, oldKingdom, newKingdom, detail, showNotification); } catch { }
        }

        // OnMakePeace resets the war throttle to 0 so the next daily tick
        // immediately re-declares war (within one in-game day).
        // DeclareWarAction is NOT called here — doing so during save loading
        // crashes the campaign while it is only partially initialised.
        private void OnMakePeace(IFaction faction1, IFaction faction2,
            MakePeaceAction.MakePeaceDetail detail)
        {
            try { AshenCitySystem.OnPeaceMade(faction1, faction2); } catch { }
        }

        private void OnMobilePartyCreated(MobileParty party)
        {
            try { FireWorshippersSystem.OnPartyCreated(party); } catch { }
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            try { SettlementEncounters.OnPartyEnteredSettlement(party, settlement); } catch { }
            if (party == MobileParty.MainParty)
            {
                try { AshenCitySystem.ApplyAshenAppearanceToSettlement(settlement); } catch { }
                try { EmberConclaveSystem.OnSettlementEntered(settlement); } catch { }
            }
        }

        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            try { SettlementEncounters.OnPartyLeftSettlement(party, settlement); } catch { }
        }

        // ── New game prompt ───────────────────────────────────────────────────
        // Fires on OnCharacterCreationIsOver — once per new campaign, after the world
        // is fully built. This is the authoritative point to (re)establish systems
        // that announce or pick world content, free of session-launch ordering.
        private void OnNewGameCreated()
        {
            try
            {
                MageKnowledge.ResetForNewGame();
                RivalShadowSystem.ResetForNewGame();
                CampaignMapEvents.ResetForNewGame();
                SettlementEncounters.ResetForNewGame();
                DragonQuestSystem.ResetForNewGame();
                AshenQuestSystem.ResetForNewGame();
                BurningLabQuestSystem.ResetForNewGame();
                EmberConclaveSystem.ResetForNewGame();
                ReagentSystem.ResetForNewGame();
                AshenRuinSystem.ResetForNewGame();
                ApprenticeSystem.ResetForNewGame();
                MiracleCampaignBehavior.ResetForNewGame();
                SanctuaryCampaignBehavior.EstablishForNewCampaign();
                AshenAltarsCampaignBehavior.EstablishForNewCampaign();
                AlchemyCampaignBehavior.EstablishForNewCampaign();
                ShowLoreIntro();
            }
            catch { }
        }

        private void ShowLoreIntro()
        {
            const string loreText =
                "Fire is not merely warmth. It is the living breath of the world — mystical, sacred, " +
                "the force that binds the soul to the flesh and grants mages the power to reshape it. " +
                "Every lord, every warrior, every creature that walks beneath the sun carries this flame within them. " +
                "When it dies, so does what made them human.\n\n" +
                "In the far north, lords who refused that end made a different choice. " +
                "They did not let their fires fade. They smothered them — and welcomed the cold that flooded in to fill the void. " +
                "The cold preserved them. Stilled them. And in that stillness, it gave them purpose.\n\n" +
                "They are called the Ashen. They do not age. They do not tire. They do not stop.\n\n" +
                "For an age they waited in the frozen dark, their numbers growing in silence. " +
                "Now the Empire has shattered — Rhagaea, Lucon, and Derthert tearing at its bones — " +
                "and the Ashen have chosen this moment to march. " +
                "The clans of Calradia war amongst themselves, blind to the pale tide moving south. " +
                "Some mages, seduced by the promise of undying stillness, have already answered the cold's call.\n\n" +
                "This is the eternal war. Flame against ash. The living against those who chose otherwise. " +
                "It has been fought before, in ages no history remembers. " +
                "It has never been won.\n\n" +
                "The mystical flame is asking something of you. The ash is already listening for your answer.";

            InformationManager.ShowInquiry(new InquiryData(
                "Embers and Ash",
                loreText,
                true, false,
                "Enter the dark.",
                "",
                () => { MageKnowledge._deferredInquiry = ShowGiftPrompt; },
                () => { MageKnowledge._deferredInquiry = ShowGiftPrompt; }
            ), true, true);
        }

        private void ShowGiftPrompt()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Gift",
                "As a child, you sometimes sensed things others could not — warmth ebbing from the wounded, the weight behind dying eyes. Do you feel it still?",
                new List<InquiryElement>
                {
                    new InquiryElement("yes", "I feel it still.", null, true,
                        "The fire stirs in you. Press Alt+X/LB+RB to open your grimoire."),
                    new InquiryElement("no", "I don't feel it.", null, true,
                        "The fire faded. You live as others do, and the world will treat you as it treats them."),
                    new InquiryElement("ashen", "The fire in me died long ago.", null, true,
                        "You are Ashen. You do not age. Each casting costs criminal standing instead of years. After your first working each day, further casts risk possession. You begin aligned with the Ashen."),
                },
                false, 1, 1,
                "Choose.",
                "",
                chosen =>
                {
                    bool isMage  = chosen?.Any(e => e.Identifier is string s && s == "yes")   == true;
                    bool isAshen = chosen?.Any(e => e.Identifier is string s && s == "ashen") == true;
                    if (isAshen) isMage = true;
                    MageKnowledge.SetMage(isMage);
                    if (isAshen)
                    {
                        MageKnowledge.SetAshen(true);
                        MageKnowledge.ApplyAshenAppearance(Hero.MainHero);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The cold settled in you long ago. The world will see it before you speak.",
                            new Color(0.3f, 0.35f, 0.7f)));
                    }
                    else if (isMage)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The fire stirs in you. Its gestures are written in your Codex of Hand and Voice.",
                            new Color(0.7f, 0.5f, 1.0f)));
                    }
                    else
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The current passes you by.",
                            new Color(0.6f, 0.6f, 0.6f)));
                    }
                    _selectionDone = true;
                    try { ColourLordRegistry.SeedInitialLords(); } catch { }
                    try { AshenCitySystem.Initialize(); } catch { }
                    try { AshenCitySystem.DailyTick(); } catch { }
                    if (isAshen)
                    {
                        // Mark the player as a criminal in every non-Ashen kingdom so lords
                        // and guards treat them as an enemy from the start.
                        try
                        {
                            foreach (var k in TaleWorlds.CampaignSystem.Kingdom.All.ToList())
                            {
                                if (k.StringId == "ashen_kingdom" || k.IsEliminated) continue;
                                TaleWorlds.CampaignSystem.Actions.ChangeCrimeRatingAction.Apply(k, 80f, false);
                            }
                        }
                        catch { }
                        try { AshenCitySystem.OnPlayerBecameAshen(); } catch { }
                    }
                    try { ReassignImperialSettlements(); } catch { }
                    // Greet every new ruler — mage or not — with the controls codex.
                    // Spells, miracles and the alchemy satchel all share this one manual.
                    MageKnowledge._deferredInquiry = MageKnowledge.ShowControlsCodex;
                },
                _ =>
                {
                    MageKnowledge.SetMage(false);
                    _selectionDone = true;
                    try { ColourLordRegistry.SeedInitialLords(); } catch { }
                    try { AshenCitySystem.Initialize(); } catch { }
                    try { AshenCitySystem.DailyTick(); } catch { }
                    try { ReassignImperialSettlements(); } catch { }
                },
                "", false
            ), false, true);
        }

        private static void ReassignImperialSettlements()
        {
            // Marunath (town_B1) + castles B5/B2      → Northern Empire
            // Jaculan  (town_V6) + castles V2/V7      → Western  Empire
            // Seonon   (by name) + nearby B castles   → Northern Empire
            // Razih    (by name) + nearby A castles   → Southern Empire
            // Ostican  + nearby V castles              → Ashen kingdom
            Hero northLeader = null;
            Hero westLeader  = null;
            Hero southLeader = null;
            Hero ashenLeader = null;
            try
            {
                northLeader = Kingdom.All.FirstOrDefault(k => k.StringId == "empire")?.Leader;
                westLeader  = Kingdom.All.FirstOrDefault(k => k.StringId == "empire_w")?.Leader;
                southLeader = Kingdom.All.FirstOrDefault(k => k.StringId == "empire_s")?.Leader;
                ashenLeader = Kingdom.All.FirstOrDefault(k => k.StringId == "ashen_kingdom")?.Leader;
            }
            catch { }

            if (northLeader != null)
            {
                foreach (string id in new[] { "town_B1", "castle_B5", "castle_B2" })
                    try
                    {
                        var s = Settlement.Find(id);
                        if (s != null) { ChangeOwnerOfSettlementAction.ApplyByDefault(northLeader, s); StabiliseSettlement(s); }
                    }
                    catch { }
            }

            if (westLeader != null)
            {
                foreach (string id in new[] { "town_V6", "castle_V2", "castle_V7" })
                    try
                    {
                        var s = Settlement.Find(id);
                        if (s != null) { ChangeOwnerOfSettlementAction.ApplyByDefault(westLeader, s); StabiliseSettlement(s); }
                    }
                    catch { }
            }

            // Seonon (Battanian city near Northern Empire border) → Northern Empire
            if (northLeader != null)
                try { AssignSettlementAndNearby("Seonon", northLeader, 40f); } catch { }

            // Razih (Aserai city near Southern Empire border) → Southern Empire
            if (southLeader != null)
                try { AssignSettlementAndNearby("Razih", southLeader, 40f); } catch { }

            // Ostican (Vlandian settlement) → Ashen kingdom
            if (ashenLeader != null)
                try { AssignSettlementAndNearby("Ostican", ashenLeader, 40f); } catch { }
        }

        // Finds a settlement by exact display name, transfers it and all non-town
        // settlements (castles/villages) within `radius` map-units to `newOwner`.
        // Silently skips anything that can't be found or transferred.
        private static void AssignSettlementAndNearby(string settlementName, Hero newOwner, float radius)
        {
            Settlement anchor = null;
            try { anchor = Settlement.All.FirstOrDefault(s => s.Name?.ToString() == settlementName); }
            catch { }
            if (anchor == null) return;

            // Transfer the anchor itself
            try { ChangeOwnerOfSettlementAction.ApplyByDefault(newOwner, anchor); StabiliseSettlement(anchor); } catch { }

            // Transfer nearby castles within radius (skip villages — they belong to their bound town)
            try
            {
                Vec2 anchorPos = anchor.GetPosition2D;
                foreach (Settlement nearby in Settlement.All
                    .Where(s => s != anchor && s.IsCastle && !s.IsUnderSiege
                             && (s.GetPosition2D - anchorPos).Length <= radius)
                    .ToList())
                {
                    try { ChangeOwnerOfSettlementAction.ApplyByDefault(newOwner, nearby); StabiliseSettlement(nearby); } catch { }
                }
            }
            catch { }
        }

        // Sets Town loyalty and security to maximum so code-driven captures don't
        // trigger a rebellion on the very next game tick.
        private static void StabiliseSettlement(Settlement s)
        {
            if (s?.Town == null) return;
            try { s.Town.Loyalty  = 100f; } catch { }
            try { s.Town.Security = 100f; } catch { }
        }

    }
}
