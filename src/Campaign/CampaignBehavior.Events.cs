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
                try { AshenMapTone.OnSettlementEntered(settlement); } catch { }
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
                catch { }
                try
                {
                    string scar = CampaignMapEvents.GetSettlementScar(settlement);
                    if (scar != null)
                        InformationManager.DisplayMessage(new InformationMessage(
                            scar, new Color(0.55f, 0.55f, 0.70f)));
                }
                catch { }
            }
        }

        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            try { SettlementEncounters.OnPartyLeftSettlement(party, settlement); } catch { }
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
            try { AshenCitySystem.EnsureSessionRenames(); } catch { }
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
                AshenMapTone.ResetForNewGame();
                CampaignMapEvents.ResetForNewGame();
                SettlementEncounters.ResetForNewGame();
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
                // Clear the previous campaign's static state BEFORE re-establishing,
                // or a new game started in the same session inherits the old game's
                // sanctuaries/altars and cooldowns (static-leak bug class).
                SanctuaryCampaignBehavior.ResetForNewGame();
                try { SanctuaryCampaignBehavior.EstablishForNewCampaign();   } catch { }
                AshenAltarsCampaignBehavior.ResetForNewGame();
                try { AshenAltarsCampaignBehavior.EstablishForNewCampaign(); } catch { }
                TribalKingdomBehavior.ResetForNewGame();
                // Each establishment is individually guarded: a failure in one world
                // system (e.g. crystal items missing from ModuleData) must never abort
                // the rest of new-game setup — above all the Gift-prompt wiring below,
                // without which the player can never choose their magic at all.
                try { CrystallinesCampaignBehavior.EstablishForNewCampaign(); } catch { }
                // Apply any character-creation backstory boon AFTER the resets above,
                // so it is not wiped (the pick was recorded during creation).
                try { CreationBackstoryRework.ApplyPendingBoons(); } catch { }
                // Every culture — Sturgian included — takes the ordinary path: the Gift
                // prompt decides their magic. The Ashen are something you BECOME in play
                // (the Last Ember at a century's age, captivity, the cold's darker turns),
                // never a starting state.
                MageKnowledge._deferredInquiry = ShowGiftPrompt;
            }
            catch { }
        }

        private void ShowGiftPrompt()
        {
            // Templars (Vlandia) walk the path of Grace; the Living Ember collides with it
            // (both share the channel and are mutually exclusive), so the Order forbids it.
            bool isTemplar = Hero.MainHero?.Culture?.StringId == "vlandia";
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Gift",
                "As a child, you sometimes sensed things others could not — warmth ebbing from the wounded, the weight behind dying eyes. Do you feel it still?",
                new List<InquiryElement>
                {
                    new InquiryElement("yes", "I feel it still.", null, true,
                        "The fire stirs in you. Press Alt+X/LB+RB to open your grimoire."),
                    new InquiryElement("no", "I don't feel it.", null, true,
                        "The fire faded. You live as others do, and the world will treat you as it treats them."),
                    new InquiryElement("living_ember", "The world beneath me has always been louder than the fire.", null,
                        !isTemplar,
                        isTemplar
                            ? "The Templars keep the inner fire as a sacred trust; the old earth-listening is forbidden to the Order, for it cannot share a soul with Grace. [Not for Templars]"
                            : "You hear the living land. Seek those in the old forests, the still rivers, the open steppe — those who still remember how to listen."),
                },
                false, 1, 1,
                "Choose.",
                "",
                chosen =>
                {
                    bool isLivingEmber = chosen?.Any(e => e.Identifier is string s && s == "living_ember") == true;
                    bool isMage        = chosen?.Any(e => e.Identifier is string s && s == "yes")          == true;
                    MageKnowledge.SetMage(isMage);
                    if (isLivingEmber)
                    {
                        NatureKnowledge.SetAttuned(true);
                        try { NatureCampaignBehavior.EstablishForNewCampaign(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The land has always known you were listening. Find those who remember the old ways — the hermits who still hear the root-voice.",
                            new Color(0.35f, 0.75f, 0.35f)));
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
                            "Your fire is silent. You live as others do, and the world will treat you as it treats them.",
                            new Color(0.6f, 0.6f, 0.6f)));
                    }
                    _selectionDone = true;
                    try { ColourLordRegistry.SeedInitialLords(); } catch { }
                    try { AshenCitySystem.Initialize(); } catch { }
                    try { AshenCitySystem.DailyTick(); } catch { }
                    try { ReassignImperialSettlements(); } catch { }
                    // Greet every new ruler with a brief pointer to the journal — the
                    // full controls manual now lives there ("Notes for the Adventurer"),
                    // so we no longer dump the whole codex on them at the start.
                    MageKnowledge._deferredInquiry = MageKnowledge.ShowControlsPointer;
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
            // Razih     (by name) + nearby castles          → Southern Empire
            // Akkalat             (Khuzait → South border)  → Southern Empire
            // ── Ashen kingdom ─────────────────────────────────────────────────
            // Ostican   + nearby castles                    → Ashen kingdom
            // ── Holy Temple (Vlandia) ─────────────────────────────────────────
            // Stripped cities above leave Vlandia with 1–2 cities (Ortysia/Sargot).
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

            // ── Northern Empire (explicit IDs) ────────────────────────────────
            if (northLeader != null)
            {
                foreach (string id in new[] { "town_B1", "castle_B5", "castle_B2" })
                    try
                    {
                        var s = Settlement.Find(id);
                        if (s != null && !AshenCitySystem.IsAshenSettlement(s)) { ChangeOwnerOfSettlementAction.ApplyByDefault(northLeader, s); StabiliseSettlement(s); }
                    }
                    catch { }
            }

            // ── Western Empire (explicit IDs) ─────────────────────────────────
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

            // ── Northern Empire (by name) ─────────────────────────────────────
            if (northLeader != null)
            {
                // Seonon: Battanian city near the Northern Empire border
                try { AssignSettlementAndNearby("Seonon",      northLeader, 40f); } catch { }
                // Ocs Hall, Rovalt: Vlandian castles at the Vlandia–North border
                try { AssignSettlementAndNearby("Ocs Hall",    northLeader, 40f); } catch { }
                try { AssignSettlementAndNearby("Rovalt",      northLeader, 40f); } catch { }
                // Car Banseth: Battanian city at the Battania–North border
                try { AssignSettlementAndNearby("Car Banseth", northLeader, 40f); } catch { }
            }

            // ── Western Empire (by name) ──────────────────────────────────────
            if (westLeader != null)
            {
                // Vlandian coastal cities reassigned to Western Empire
                try { AssignSettlementAndNearby("Charas",  westLeader, 40f); } catch { }
                try { AssignSettlementAndNearby("Galend",  westLeader, 40f); } catch { }
                try { AssignSettlementAndNearby("Pravend", westLeader, 40f); } catch { }
                // Aserai border cities reassigned to Western Empire
                try { AssignSettlementAndNearby("Quyaz",  westLeader, 40f); } catch { }
                try { AssignSettlementAndNearby("Sanala", westLeader, 40f); } catch { }
            }

            // ── Southern Empire (by name) ─────────────────────────────────────
            if (southLeader != null)
            {
                // Razih: Aserai city near the Southern Empire border
                try { AssignSettlementAndNearby("Razih",   southLeader, 40f); } catch { }
                // Akkalat: Khuzait city at the Khuzait–South border
                try { AssignSettlementAndNearby("Akkalat", southLeader, 40f); } catch { }
            }

            // ── Ashen kingdom ─────────────────────────────────────────────────
            if (ashenLeader != null)
                try { AssignSettlementAndNearby("Ostican", ashenLeader, 40f); } catch { }

            // ── The Holy Temple (Vlandia) — declare founding war with Ashen ───
            // Ashen seized Ostican from Vlandia above; this seeds the permanent war.
            try
            {
                var vlandia = Kingdom.All.FirstOrDefault(k => k.StringId == "vlandia" && !k.IsEliminated);
                var ashen   = Kingdom.All.FirstOrDefault(k => k.StringId == "ashen_kingdom" && !k.IsEliminated);
                if (vlandia != null && ashen != null && !vlandia.IsAtWarWith(ashen))
                    DeclareWarAction.ApplyByDefault(vlandia, ashen);
            }
            catch { }

            // Present Vlandia as The Holy Temple — the Templars — from the very start.
            // The daily tick re-applies this on every reload (names revert to XML on
            // load), but new players should see the Templars immediately at character
            // creation, not only after the first in-game day passes.
            try { AshenCitySystem.RenameHolyTempleKingdom(); } catch { }
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
            // Never strip an Ashen holding for an Empire — the cold realm keeps its
            // own. (Matched by StringId, so it holds after the display rename too.)
            if (AshenCitySystem.IsAshenSettlement(anchor)) return;

            // Transfer the anchor itself
            try { ChangeOwnerOfSettlementAction.ApplyByDefault(newOwner, anchor); StabiliseSettlement(anchor); } catch { }

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
