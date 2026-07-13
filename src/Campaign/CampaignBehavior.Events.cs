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
            try { AshenCitySystem.OnClanChangedKingdom(clan, oldKingdom, newKingdom, detail, showNotification); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // OnMakePeace resets the war throttle to 0 so the next daily tick
        // immediately re-declares war (within one in-game day).
        // DeclareWarAction is NOT called here — doing so during save loading
        // crashes the campaign while it is only partially initialised.
        private void OnMakePeace(IFaction faction1, IFaction faction2,
            MakePeaceAction.MakePeaceDetail detail)
        {
            try { AshenCitySystem.OnPeaceMade(faction1, faction2); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private void OnMobilePartyCreated(MobileParty party)
        {
            try { FireWorshippersSystem.OnPartyCreated(party); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            try { SettlementEncounters.OnPartyEnteredSettlement(party, settlement); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (party == MobileParty.MainParty)
            {
                try { AshenCitySystem.ApplyAshenAppearanceToSettlement(settlement); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { EmberConclaveSystem.OnSettlementEntered(settlement); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AshenMapTone.OnSettlementEntered(settlement); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try
                {
                    if (MageKnowledge.IsKnownMage && _rng.Next(8) == 0)
                    {
                        string[] reactions =
                        {
                            "Some here look at your hands before they look at your face.",
                            "A merchant adjusts their stall as you pass. Old habit, or something more.",
                            "The innkeeper gives you a room without asking your name. They already know it.",
                            "Someone in the crowd whispers to their companion as you pass. The companion looks — then looks away.",
                        };
                        InformationManager.DisplayMessage(new InformationMessage(
                            reactions[_rng.Next(reactions.Length)],
                            new Color(0.65f, 0.6f, 0.7f)));
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try
                {
                    string scar = CampaignMapEvents.GetSettlementScar(settlement);
                    if (scar != null)
                        InformationManager.DisplayMessage(new InformationMessage(
                            scar, new Color(0.55f, 0.55f, 0.70f)));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            try { SettlementEncounters.OnPartyLeftSettlement(party, settlement); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Session launch ────────────────────────────────────────────────────
        // Fires once each session, after the campaign is loaded and before the player
        // can act. Applies the per-session display renames immediately so the map shows
        // "The Holy Temple" / "Tribes of the East" / Ashen settlement names the instant
        // it appears, rather than after the first in-game day ticks over.
        //
        // It deliberately does NOT call AshenCitySystem.Initialize(): on a reload the
        // Ashen clans/settlements are already restored from the save (so the renames
        // have what they need), and on a NEW game the Ashen kingdom is established by
        // the deferred new-game flow. Creating the kingdom and moving city clans here,
        // before the engine has finished building the new campaign world, caused those
        // moves to be undone — the Ashen cities snapped back to Sturgia at game start.
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try { AshenCitySystem.EnsureSessionRenames(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SettlementEncounters.RegisterWaitMenu(starter); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                MageElementKnowledge.ResetForNewGame();
                RivalShadowSystem.ResetForNewGame();
                AshenMapTone.ResetForNewGame();
                CampaignMapEvents.ResetForNewGame();
                SettlementEncounters.ResetForNewGame();
                ScholarBargainQuestSystem.ResetForNewGame();
                DragonQuestSystem.ResetForNewGame();
                KeybindReferenceSystem.ResetForNewGame();
                KeybindReferenceSystem.EnsureForSession();   // present from the very start
                AshenQuestSystem.ResetForNewGame();
                BurningLabQuestSystem.ResetForNewGame();
                EmberConclaveSystem.ResetForNewGame();
                AshenRuinSystem.ResetForNewGame();
                ApprenticeSystem.ResetForNewGame();
                MiracleCampaignBehavior.ResetForNewGame();
                NatureCampaignBehavior.ResetForNewGame();
                CrystalTalents.ResetForNewGame();
                SacredSiteTalents.ResetForNewGame();
                // Clear the previous campaign's static state BEFORE re-establishing,
                // or a new game started in the same session inherits the old game's
                // sanctuaries/altars and cooldowns (static-leak bug class).
                SanctuaryCampaignBehavior.ResetForNewGame();
                try { SanctuaryCampaignBehavior.EstablishForNewCampaign();   } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                AshenAltarsCampaignBehavior.ResetForNewGame();
                try { AshenAltarsCampaignBehavior.EstablishForNewCampaign(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                TribalKingdomBehavior.ResetForNewGame();
                try { TribalKingdomBehavior.EstablishForNewCampaign(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // Each establishment is individually guarded: a failure in one world
                // system (e.g. crystal items missing from ModuleData) must never abort
                // the rest of new-game setup — above all the Gift-prompt wiring below,
                // without which the player can never choose their magic at all.
                try { CrystallinesCampaignBehavior.EstablishForNewCampaign(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                SacredSitesCampaignBehavior.ResetForNewGame();
                try { ElementalLordRegistry.EstablishForNewCampaign(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // Apply any character-creation backstory boon AFTER the resets above,
                // so it is not wiped (the pick was recorded during creation).
                try { CreationBackstoryRework.ApplyPendingBoons(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // Every culture — Sturgian included — takes the ordinary path: the Gift
                // prompt decides their magic. The Ashen are something you BECOME in play
                // (the Last Ember at a century's age, captivity, the cold's darker turns),
                // never a starting state.
                MageKnowledge._deferredInquiry = ShowGiftPrompt;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                },
                false, 1, 1,
                "Choose.",
                "",
                chosen =>
                {
                    bool isMage = chosen?.Any(e => e.Identifier is string s && s == "yes") == true;
                    MageKnowledge.SetMage(isMage);
                    if (isMage)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The fire stirs in you. Its gestures are written in your Codex of Hand and Voice.",
                            new Color(0.7f, 0.5f, 1.0f)));
                    }
                    else
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Your fire is silent. You live as others do, and the world will treat you as it treats them.",
                            new Color(0.6f, 0.6f, 0.6f)));
                    }
                    _selectionDone = true;
                    try { ColourLordRegistry.SeedInitialLords(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    // Seed the attuned seers — now the mage's TEACHERS — for every new
                    // campaign (was previously tied to the removed Living-Ember choice).
                    try { NatureCampaignBehavior.EstablishForNewCampaign(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { AshenCitySystem.Initialize(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { AshenCitySystem.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { ReassignImperialSettlements(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    // Greet every new ruler with a brief pointer to the journal — the
                    // full controls manual now lives there ("Notes for the Adventurer"),
                    // so we no longer dump the whole codex on them at the start.
                    MageKnowledge._deferredInquiry = MageKnowledge.ShowControlsPointer;
                },
                _ =>
                {
                    MageKnowledge.SetMage(false);
                    _selectionDone = true;
                    try { ColourLordRegistry.SeedInitialLords(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { NatureCampaignBehavior.EstablishForNewCampaign(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { AshenCitySystem.Initialize(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { AshenCitySystem.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { ReassignImperialSettlements(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                },
                "", false
            ), false, true);
        }

        private static void ReassignImperialSettlements()
        {
            // ── Northern Empire ───────────────────────────────────────────────
            // Marunath  (town_B1) + castles B5/B2          → Northern Empire
            // Seonon    (by name) + nearby castles          → Northern Empire
            // Ocs Hall, Rovalt    (Vlandia → North border)  → Northern Empire
            // Car Banseth         (Battania → North border) → Northern Empire
            // ── Western Empire ────────────────────────────────────────────────
            // Jaculan   (town_V6) + castles V2/V7           → Western  Empire
            // Charas, Galend, Pravend (Vlandia → West)      → Western  Empire
            // Quyaz, Sanala           (Aserai  → West)      → Western  Empire
            // ── Southern Empire ───────────────────────────────────────────────
            // Razih, Qasira (by name) + nearby castles      → Southern Empire
            // Akkalat             (Khuzait → South border)  → Southern Empire
            // ── Ashen kingdom ─────────────────────────────────────────────────
            // Ostican   + nearby castles                    → Ashen kingdom
            // ── Holy Temple (Vlandia) ─────────────────────────────────────────
            // Stripped cities above leave Vlandia with 1–2 cities (Ortysia/Sargot).
            // ── Border lords ──────────────────────────────────────────────────
            // A few of the Battanian/Vlandian/Aserai clans stripped of a castle above
            // also swear to that castle's new Empire (see MigrateBorderLords) — never
            // the Tribes of the East, who keep every lord regardless of Akkalat's fall.
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

                // If an Empire's leader reference resolves to a hero whose clan has
                // gone Ashen (a stale RulingClan pointer after the cold claimed it),
                // every transfer below would hand the border cities to the Ashen
                // instead of the Empire. Better to skip that Empire's assignments
                // entirely than to feed the cold.
                if (AshenCitySystem.IsAshenClanMember(northLeader) || ColourLordRegistry.IsAshenLord(northLeader)) northLeader = null;
                if (AshenCitySystem.IsAshenClanMember(westLeader)  || ColourLordRegistry.IsAshenLord(westLeader))  westLeader  = null;
                if (AshenCitySystem.IsAshenClanMember(southLeader) || ColourLordRegistry.IsAshenLord(southLeader)) southLeader = null;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Original owners of the border settlements below, captured just before
            // their fief changes hands — feeds MigrateBorderLords further down, which
            // follows a FEW of these clans into their castle's new Empire rather than
            // leaving them landless nobles in a kingdom that no longer holds their home.
            // Aserai→Western (Quyaz/Sanala) and Vlandia→Northern (Ocs Hall/Rovalt) are
            // deliberately NOT captured — only cities move there, not their lords — and
            // Khuzait (Akkalat) is never captured: the Tribes of the East keep every lord.
            var battaniaToNorthern = new List<Clan>();
            var vlandiaToWestern   = new List<Clan>();
            var aseraiToSouthern   = new List<Clan>();

            // ── Northern Empire (explicit IDs) ────────────────────────────────
            if (northLeader != null)
            {
                foreach (string id in new[] { "town_B1", "castle_B5", "castle_B2" })
                    try
                    {
                        var s = Settlement.Find(id);
                        if (s != null && !AshenCitySystem.IsAshenSettlement(s))
                        {
                            if (s.OwnerClan != null) battaniaToNorthern.Add(s.OwnerClan);
                            ChangeOwnerOfSettlementAction.ApplyByDefault(northLeader, s); StabiliseSettlement(s);
                        }
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // ── Western Empire (explicit IDs) ─────────────────────────────────
            if (westLeader != null)
            {
                foreach (string id in new[] { "town_V6", "castle_V2", "castle_V7" })
                    try
                    {
                        var s = Settlement.Find(id);
                        if (s != null)
                        {
                            if (s.OwnerClan != null) vlandiaToWestern.Add(s.OwnerClan);
                            ChangeOwnerOfSettlementAction.ApplyByDefault(westLeader, s); StabiliseSettlement(s);
                        }
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // ── Northern Empire (by name) ─────────────────────────────────────
            if (northLeader != null)
            {
                // Seonon: Battanian city near the Northern Empire border
                try { AssignSettlementAndNearby("Seonon",      northLeader, 40f, battaniaToNorthern); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // Ocs Hall, Rovalt: Vlandian castles at the Vlandia–North border
                try { AssignSettlementAndNearby("Ocs Hall",    northLeader, 40f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AssignSettlementAndNearby("Rovalt",      northLeader, 40f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // Car Banseth: Battanian city at the Battania–North border
                try { AssignSettlementAndNearby("Car Banseth", northLeader, 40f, battaniaToNorthern); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // ── Western Empire (by name) ──────────────────────────────────────
            if (westLeader != null)
            {
                // Vlandian coastal cities reassigned to Western Empire
                try { AssignSettlementAndNearby("Charas",  westLeader, 40f, vlandiaToWestern); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AssignSettlementAndNearby("Galend",  westLeader, 40f, vlandiaToWestern); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AssignSettlementAndNearby("Pravend", westLeader, 40f, vlandiaToWestern); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // Aserai border cities reassigned to Western Empire
                try { AssignSettlementAndNearby("Quyaz",  westLeader, 40f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AssignSettlementAndNearby("Sanala", westLeader, 40f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // ── Southern Empire (by name) ─────────────────────────────────────
            if (southLeader != null)
            {
                // Razih, Qasira: Aserai cities near the Southern Empire border
                try { AssignSettlementAndNearby("Razih",   southLeader, 40f, aseraiToSouthern); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AssignSettlementAndNearby("Qasira",  southLeader, 40f, aseraiToSouthern); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // Akkalat: Khuzait city at the Khuzait–South border — the Tribes of the
                // East keep their lord; only the city (not the clan) changes hands.
                try { AssignSettlementAndNearby("Akkalat", southLeader, 40f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // ── Ashen kingdom ─────────────────────────────────────────────────
            if (ashenLeader != null)
                try { AssignSettlementAndNearby("Ostican", ashenLeader, 40f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // A few border lords swear to the Empire now holding their castle instead
            // of staying landless in their old kingdom. Deliberately a minority of the
            // clans stripped above, so no kingdom is gutted or overstuffed.
            try
            {
                MigrateBorderLords(battaniaToNorthern, northLeader?.Clan?.Kingdom, 3);
                MigrateBorderLords(vlandiaToWestern,   westLeader?.Clan?.Kingdom,  2);
                MigrateBorderLords(aseraiToSouthern,   southLeader?.Clan?.Kingdom, 2);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── The Holy Temple (Vlandia) — declare founding war with Ashen ───
            // Ashen seized Ostican from Vlandia above; this seeds the permanent war.
            try
            {
                var vlandia = Kingdom.All.FirstOrDefault(k => k.StringId == "vlandia" && !k.IsEliminated);
                var ashen   = Kingdom.All.FirstOrDefault(k => k.StringId == "ashen_kingdom" && !k.IsEliminated);
                if (vlandia != null && ashen != null && !vlandia.IsAtWarWith(ashen))
                    DeclareWarAction.ApplyByDefault(vlandia, ashen);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Present Vlandia as The Holy Temple — the Templars — from the very start.
            // The daily tick re-applies this on every reload (names revert to XML on
            // load), but new players should see the Templars immediately at character
            // creation, not only after the first in-game day passes.
            try { AshenCitySystem.RenameHolyTempleKingdom(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Finds a settlement by exact display name, transfers it and all non-town
        // settlements (castles/villages) within `radius` map-units to `newOwner`.
        // Silently skips anything that can't be found or transferred. When
        // `capturedClans` is supplied, each settlement's owning clan is recorded
        // to it BEFORE the transfer, for MigrateBorderLords to draw from afterward.
        private static void AssignSettlementAndNearby(string settlementName, Hero newOwner, float radius,
            List<Clan> capturedClans = null)
        {
            Settlement anchor = null;
            try { anchor = Settlement.All.FirstOrDefault(s => s.Name?.ToString() == settlementName); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (anchor == null) return;
            // Never strip an Ashen holding for an Empire — the cold realm keeps its
            // own. (Matched by StringId, so it holds after the display rename too.)
            if (AshenCitySystem.IsAshenSettlement(anchor)) return;

            // Transfer the anchor itself
            try
            {
                if (capturedClans != null && anchor.OwnerClan != null) capturedClans.Add(anchor.OwnerClan);
                ChangeOwnerOfSettlementAction.ApplyByDefault(newOwner, anchor); StabiliseSettlement(anchor);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Transfer nearby castles within radius (skip villages — they belong to their
            // bound town; skip Ashen castles so the radius sweep cannot bleed the cold realm,
            // e.g. the castles around The Ashen Crown that sit near the Northern border).
            try
            {
                Vec2 anchorPos = anchor.GetPosition2D;
                foreach (Settlement nearby in Settlement.All
                    .Where(s => s != anchor && s.IsCastle && !s.IsUnderSiege
                             && !AshenCitySystem.IsAshenSettlement(s)
                             && (s.GetPosition2D - anchorPos).Length <= radius)
                    .ToList())
                {
                    try
                    {
                        if (capturedClans != null && nearby.OwnerClan != null) capturedClans.Add(nearby.OwnerClan);
                        ChangeOwnerOfSettlementAction.ApplyByDefault(newOwner, nearby); StabiliseSettlement(nearby);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── A few border lords follow their castle into its new Empire ─────────
        // Picks the lowest-tier ("minor") clans among `candidates` — the original
        // owners of settlements just reassigned above — and moves up to `maxCount`
        // of them into `target`, mirroring the Ashen conversion in
        // AshenCitySystem.Setup.cs (leave old kingdom, then join the new one).
        // Deliberately a minority: leaving most clans in their home kingdom keeps
        // Vlandia, Battania and Aserai from being gutted by their own border loss.
        private static void MigrateBorderLords(List<Clan> candidates, Kingdom target, int maxCount)
        {
            if (target == null || candidates == null || candidates.Count == 0) return;

            var chosen = candidates
                .Where(c => c != null && !c.IsEliminated && c.Leader != null
                         && c != Hero.MainHero?.Clan
                         && c.Kingdom != target
                         && c.Kingdom?.RulingClan != c)   // never uproot a home kingdom's own ruler
                .Distinct()
                .OrderBy(c => c.Tier)
                .ThenBy(c => c.StringId)
                .Take(maxCount);

            foreach (Clan clan in chosen)
            {
                try
                {
                    if (clan.Kingdom != null)
                        ChangeKingdomAction.ApplyByLeaveKingdom(clan, false);
                    if (target.RulingClan == null)
                        ChangeKingdomAction.ApplyByCreateKingdom(clan, target, false);
                    else
                        ChangeKingdomAction.ApplyByJoinToKingdom(
                            clan, target, CampaignTime.Now + CampaignTime.Years(1000), false);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // Sets Town loyalty and security to maximum so code-driven captures don't
        // trigger a rebellion on the very next game tick.
        private static void StabiliseSettlement(Settlement s)
        {
            if (s?.Town == null) return;
            try { s.Town.Loyalty  = 100f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { s.Town.Security = 100f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

    }
}
