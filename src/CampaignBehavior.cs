// =============================================================================
// LIFE & DEATH MAGIC — CampaignBehavior.cs
// New game prompt, inheritance, population regulation, aging, save/load.
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
    public class MagicCampaignBehavior : CampaignBehaviorBase
    {
        private bool _selectionDone;
        private int  _prisonerCountSnapshot = -1;
        private int  _dayCounter            = 0;
        private int  _reapRaidCooldown      = 0;
        private int  _lordAnnounceCountdown = -1;
        private bool _lordAnnouncementDone  = false;
        private static readonly Random _rng = new Random();

        private static readonly string[] _premonitions =
        {
            "The fire in you whispers tonight — something distant is ending.",
            "On the road, you pass the ruins of a great pyre. The air still carries old smoke. Something in you recognises it.",
            "You wake with the taste of ash on your tongue. The inner fire is restless.",
            "Your shadow moves a half-step behind you. The fire inside is watching something you cannot see.",
            "You watch a forge-fire die to coals. For a moment, you understand exactly what you are.",
            "A child in the village stares at your hands as you pass. She sees something there that you do not show others.",
            "The fire does not sleep when you do. You feel it turning in its sleep, searching.",
            "You smell smoke where there is none. An old instinct — the fire recognising itself in the distance.",
            "Rain falls, but where you stand the ground stays dry. You notice. You always notice.",
            "The torches in the hall burn a shade too orange tonight. The innkeeper doesn't see it. You do.",
            "Someone is watching you from across the market — no, not watching. Sensing. You feel it the same way they do.",
            "A wound on your hand heals overnight. You have stopped being surprised by this.",
            "The dying man reaches for your hand. You let him. The fire passes between you — not much, but some.",
            "You dream of a battlefield long before it happens. The details are wrong. The ending is not.",
            "An old mage-lord rides past on the road. Neither of you slows. Both of you know.",
            "Animals grow quiet when you enter the stable. Not frightened — still. As if listening.",
            "You stand at the edge of a river and the water pulls slightly toward you. You step back.",
            "The fire shows you a face tonight. Someone you haven't met yet. Or someone you have, changed by time.",
            "You press your palm against cold stone and feel it remember warmth. Every stone remembers something.",
            "A soldier dies in the battle beside you. For a moment, his fire is visible — a last guttering. Then nothing.",
            "The stars are wrong tonight. Not the positions — the light. Too old. Too far. The fire knows things you don't, and it is not telling you.",
            "You catch yourself speaking to the campfire, asking nothing in particular. It doesn't answer. But it listens.",
        };

        public override void RegisterEvents()
        {
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnMissionEnded);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.HeroCreated.AddNonSerializedListener(this, OnHeroCreated);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, OnCompanionAdded);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.MobilePartyCreated.AddNonSerializedListener(this, OnMobilePartyCreated);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
        }

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
                try { AshenCitySystem.ApplyAshenAppearanceToSettlement(settlement); } catch { }
        }

        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            try { SettlementEncounters.OnPartyLeftSettlement(party, settlement); } catch { }
        }

        // ── New game prompt ───────────────────────────────────────────────────
        private void OnNewGameCreated()
        {
            try
            {
                MageKnowledge.ResetForNewGame();
                CampaignMapEvents.ResetForNewGame();
                SettlementEncounters.ResetForNewGame();
                DragonQuestSystem.ResetForNewGame();
                ShowLoreIntro();
            }
            catch { }
        }

        private void ShowLoreIntro()
        {
            const string loreText =
                "Fire gives life. It gives warmth, magic, the will to endure.\n\n" +
                "When it is extinguished, only ash remains.\n\n" +
                "In the north, lords who refused to let their fire die chose the cold instead. " +
                "They are called the Ashen. They do not age. They do not negotiate. They march.\n\n" +
                "The Empire is fractured. Rhagaea, Lucon, and Derthert fight over its bones " +
                "while the ash moves south. The clans of Calradia are scattered and conflicted. " +
                "None of them are ready.\n\n" +
                "Some mages, tempted by the promise of unliving, may yet answer the cold's call.\n\n" +
                "The fire is asking you something. The ash is listening for your answer.";

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
                            "The fire stirs. Hold Alt, type form keys (WASD), press X to Break, type effect keys, release Alt to cast.",
                            new Color(0.7f, 0.5f, 1.0f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Forms: W=Blast  A=Missile  D=Barrier  S=Burst  |  Effects: W/A/D=Damage (+25 ea)  S=Restore (+15 heal ea)  |  Alt+X = Grimoire",
                            new Color(0.6f, 0.6f, 0.8f)));
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
                    try { ReassignImperialSettlements(); } catch { }
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

        // ── Daily tick ────────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            try
            {
                if (!_selectionDone)
                {
                    _selectionDone = true;
                    try { ColourLordRegistry.SeedInitialLords(); } catch { }
                    try { ReassignImperialSettlements(); } catch { }
                }
                try { AshenCitySystem.Initialize(); } catch { }
                try { AshenCitySystem.DailyTick(); } catch { }
                try { ColourLordRegistry.DailyMapCast(); } catch { }
                try { TalentSystem.EnforceKinship(); } catch { }
                try { AgingSystem.DailyAgeCheck(); } catch { }
                try { CampaignMapEvents.DailyTick(); } catch { }
                try { SettlementEncounters.DailyTick(); } catch { }
                try { DragonQuestSystem.DailyTick(); } catch { }
                try { CheckReapPrisonerYield(); } catch { }
                if (_reapRaidCooldown > 0) _reapRaidCooldown--;
                try { TickLordAnnouncement(); } catch { }
                _dayCounter++;
                if (_dayCounter % 30 == 0) try { OnMonthlyTick(); } catch { }
            }
            catch { }
        }

        // ── Weekly tick ───────────────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            try
            {
                try { ColourLordRegistry.CheckPopulationBounds(); } catch { }
                try { ColourLordRegistry.CheckAgeLimit(); } catch { }
                try { CampaignMapEvents.WeeklyTick(); } catch { }
            }
            catch { }
        }

        // ── Mission ended ─────────────────────────────────────────────────────
        private void OnMissionEnded(IMission mission)
        {
            try
            {
                try { ColourLordAI.ClearCooldowns(); } catch { }
                try { SpellEffects.ClearAreaEffects(); } catch { }
                try { SpellEffects.ClearSelfEffects(); } catch { }
                try { SpellEffects.ClearGlows(); } catch { }
                try { SpellEffects.ClearMoves(); } catch { }
            }
            catch { }
        }

        // ── Map event ended (battle result) ───────────────────────────────────
        private void OnMapEventEnded(MapEvent mapEvent)
        {
            try
            {
                if (mapEvent == null) return;
                try { ApplyNpcBattleAging(mapEvent); } catch { }
                // Flush any battle casts not consumed above (NPCs absent from this event).
                // Must run after aging so _battleCasts still holds data during ApplyNpcBattleAging.
                try { ColourLordAI.FlushBattleCasts(); } catch { }
                try { ApplyNpcBattleMoraleBonus(mapEvent); } catch { }
                try { CheckReapRaidYield(mapEvent); } catch { }
                try { SettlementEncounters.OnMapEventEnded(mapEvent); } catch { }
                try { DragonQuestSystem.OnMapEventEnded(mapEvent); } catch { }
                // Refresh snapshot so battle-captured prisoners don't count as discards
                try { _prisonerCountSnapshot = MobileParty.MainParty?.PrisonRoster?.TotalManCount ?? _prisonerCountSnapshot; } catch { }
            }
            catch { }
        }

        // ── Monthly atmospheric events ────────────────────────────────────────
        private void OnMonthlyTick()
        {
            if (!MageKnowledge.IsMage) return;
            // Random premonition message
            if (_rng.Next(3) == 0) // ~33% chance each month
            {
                string msg = _premonitions[_rng.Next(_premonitions.Length)];
                InformationManager.DisplayMessage(new InformationMessage(
                    msg, new Color(0.75f, 0.55f, 0.3f)));
            }

            // If near a mage lord, sense their fire
            try
            {
                if (Hero.MainHero?.PartyBelongedTo == null) return;
                Vec2 pos = Hero.MainHero.PartyBelongedTo.GetPosition2D;
                Hero nearMage = Hero.AllAliveHeroes.FirstOrDefault(h =>
                    h != Hero.MainHero && h.IsLord && h.IsAlive &&
                    ColourLordRegistry.IsColourLord(h) &&
                    h.PartyBelongedTo != null &&
                    (h.PartyBelongedTo.GetPosition2D - pos).Length < 30f);
                if (nearMage != null)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"You sense another fire nearby — {nearMage.Name} burns with it.",
                        new Color(0.9f, 0.6f, 0.2f)));
            }
            catch { }
        }

        // ── Lord flame announcement (fires once, ~3 days after game start) ──────
        private void TickLordAnnouncement()
        {
            if (_lordAnnouncementDone) return;
            if (_lordAnnounceCountdown < 0)
            {
                _lordAnnounceCountdown = 1; // start 1-day countdown; fires on day 3
                return;
            }
            if (_lordAnnounceCountdown > 0)
            {
                _lordAnnounceCountdown--;
                return;
            }
            // countdown == 0 → announce
            _lordAnnouncementDone = true;
            AnnounceMageLords();
        }

        private void AnnounceMageLords()
        {
            if (!MageKnowledge.IsMage) return;
            try
            {
                var lords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h != Hero.MainHero && h.IsAlive
                             && ColourLordRegistry.IsColourLord(h))
                    .ToList();
                if (lords.Count == 0) return;

                InformationManager.DisplayMessage(new InformationMessage(
                    $"The fire stirs — you sense other flames across Calradia. " +
                    $"{lords.Count} lord{(lords.Count != 1 ? "s" : "")} carr{(lords.Count != 1 ? "y" : "ies")} the gift.",
                    new Color(0.7f, 0.5f, 1.0f)));

                const int maxNamed = 5;
                var named = lords.Take(maxNamed).Select(h =>
                {
                    string place = h.Clan?.Kingdom?.Name?.ToString()
                                ?? h.Clan?.Name?.ToString()
                                ?? "the wilds";
                    return $"{h.Name} ({place})";
                });
                string tail = lords.Count > maxNamed
                    ? $" — and {lords.Count - maxNamed} others."
                    : ".";
                InformationManager.DisplayMessage(new InformationMessage(
                    string.Join(", ", named) + tail,
                    new Color(0.6f, 0.45f, 0.9f)));
            }
            catch { }
        }

        // ── Reap: raid yield (7-day cooldown) ────────────────────────────────
        private void CheckReapRaidYield(MapEvent mapEvent)
        {
            if (!MageKnowledge.IsMage || !TalentSystem.Has(TalentId.Reap)) return;
            if (mapEvent.EventType != MapEvent.BattleTypes.Raid) return;
            if (_reapRaidCooldown > 0) return;

            bool playerAttacker = mapEvent.AttackerSide?.Parties
                .Any(p => p.Party == PartyBase.MainParty) == true;
            if (!playerAttacker) return;
            if (mapEvent.WinningSide != BattleSideEnum.Attacker) return;

            AgingSystem.RejuvenateHero(Hero.MainHero, 5);
            _reapRaidCooldown = 7;
        }

        // ── Reap: prisoner discard yield ──────────────────────────────────────
        private void CheckReapPrisonerYield()
        {
            if (!MageKnowledge.IsMage || !TalentSystem.Has(TalentId.Reap)) return;

            int current = MobileParty.MainParty?.PrisonRoster?.TotalManCount ?? 0;

            if (_prisonerCountSnapshot >= 0 && current < _prisonerCountSnapshot)
            {
                int discarded = _prisonerCountSnapshot - current;
                int daysGained = 0;
                for (int i = 0; i < discarded; i++)
                {
                    if (_rng.NextDouble() < 0.05)
                        daysGained++;
                }
                if (daysGained > 0)
                    AgingSystem.RejuvenateHero(Hero.MainHero, daysGained);
            }

            _prisonerCountSnapshot = current;
        }

        private void ApplyNpcBattleAging(MapEvent mapEvent)
        {
            bool playerInvolved = false;
            try
            {
                playerInvolved =
                    mapEvent.AttackerSide.Parties.Any(p => p.Party == PartyBase.MainParty) ||
                    mapEvent.DefenderSide.Parties.Any(p => p.Party == PartyBase.MainParty);
            }
            catch { }

            // Age all party leaders who cast spells during this battle.
            foreach (MapEventSide side in new[] { mapEvent.AttackerSide, mapEvent.DefenderSide })
            {
                if (side == null) continue;
                try
                {
                    foreach (var meparty in side.Parties)
                    {
                        try
                        {
                            Hero leader = meparty?.Party?.LeaderHero;
                            if (leader == null || leader == Hero.MainHero
                                || !ColourLordRegistry.IsColourLord(leader)) continue;

                            int weight = ColourLordAI.ConsumeBattleCasts(leader);
                            if (weight <= 0) continue;

                            // weight = sum of totalInputs across all spells cast this battle.
                            // Divide by 3 to match the steeper player scaling (ceil(n/2) per cast).
                            int agingDays = Math.Max(1, weight / 3);
                            if (!ColourLordRegistry.IsAshenLord(leader))
                                AgeHeroDeferred(leader, agingDays);
                            if (playerInvolved)
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{leader.Name} is spent by the working — {agingDays} day{(agingDays > 1 ? "s" : "")} older.",
                                    new Color(0.5f, 0.4f, 0.7f)));
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // Also age companion mages travelling in the player's party.
            // ApplyNpcBattleAging only reaches party leaders above; companions are non-leaders
            // and would never be aged otherwise even though ColourLordAI tracks their casts.
            if (!playerInvolved) return;
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                foreach (var entry in roster.GetTroopRoster().ToList())
                {
                    Hero companion = entry.Character?.HeroObject;
                    if (companion == null || companion == Hero.MainHero) continue;
                    if (!ColourLordRegistry.IsColourLord(companion)) continue;

                    int weight = ColourLordAI.ConsumeBattleCasts(companion);
                    if (weight <= 0) continue;

                    int agingDays = Math.Max(1, weight / 4);
                    if (!ColourLordRegistry.IsAshenLord(companion))
                        AgeHeroDeferred(companion, agingDays);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{companion.Name} is spent by the working — {agingDays} day{(agingDays > 1 ? "s" : "")} older.",
                        new Color(0.5f, 0.4f, 0.7f)));
                }
            }
            catch { }
        }

        // Shifts a hero's birth day without triggering CheckAgeLimit immediately.
        // Safe to call during OnMapEventEnded (post-battle transition) because
        // KillCharacterAction during that window causes cascading handler crashes.
        // DailyAgeCheck runs on the next tick and handles any over-100 cases cleanly.
        private static void AgeHeroDeferred(Hero hero, int days)
        {
            if (hero == null || days <= 0) return;
            try { hero.SetBirthDay(hero.BirthDay - CampaignTime.Days(days)); } catch { }
        }

        private void ApplyNpcBattleMoraleBonus(MapEvent mapEvent)
        {
            bool playerInvolved = false;
            try
            {
                playerInvolved =
                    mapEvent.AttackerSide.Parties.Any(p => p.Party == PartyBase.MainParty) ||
                    mapEvent.DefenderSide.Parties.Any(p => p.Party == PartyBase.MainParty);
            }
            catch { }
            if (playerInvolved) return;

            foreach (MapEventSide side in new[] { mapEvent.AttackerSide, mapEvent.DefenderSide })
            {
                if (side == null) continue;
                try
                {
                    bool hasMage = side.Parties.Any(p =>
                    {
                        Hero leader = p?.Party?.LeaderHero;
                        return leader != null && ColourLordRegistry.IsColourLord(leader);
                    });
                    if (!hasMage) continue;

                    foreach (var meparty in side.Parties)
                        try
                        {
                            if (meparty?.Party?.MobileParty != null)
                                meparty.Party.MobileParty.RecentEventsMorale += 10f;
                        }
                        catch { }
                }
                catch { }
            }
        }

        // ── Hero killed ───────────────────────────────────────────────────────
        private void OnHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            try
            {
                // Game of Thrones: if this was a faction leader, the kingdom may fracture.
                // Check before any other processing since succession may change Kingdom.Leader.
                try
                {
                    var vClan = victim?.Clan;
                    if (vClan?.Kingdom?.RulingClan == vClan && vClan?.Kingdom?.Leader == victim)
                        CampaignMapEvents.OnFactionLeaderKilled(vClan.Kingdom);
                }
                catch { }

                if (ColourLordRegistry.IsColourLord(victim))
                    try { ColourLordRegistry.OnLordDied(victim); } catch { }

                if (detail != KillCharacterAction.KillCharacterActionDetail.Executed) return;
                if (killer == null) return;
                bool victimIsLord = false;
                try { victimIsLord = victim.IsLord; } catch { }
                if (!victimIsLord) return;

                // Player DevourLife: executing a captured lord draws back 100 days
                if (killer == Hero.MainHero
                    && MageKnowledge.IsMage
                    && TalentSystem.Has(TalentId.DevourLife))
                {
                    try { AgingSystem.RejuvenateHero(Hero.MainHero, 100); } catch { }
                }

                // NPC DevourLife: merciless/devious mage lord executioner absorbs 1 day
                if (killer != Hero.MainHero
                    && ColourLordRegistry.IsColourLord(killer)
                    && ColourLordRegistry.HasTalent(killer, TalentId.DevourLife))
                {
                    try
                    {
                        int merciless = killer.GetTraitLevel(DefaultTraits.Mercy);
                        int devious   = killer.GetTraitLevel(DefaultTraits.Honor);
                        if (merciless < 0 || devious < 0)
                            killer.SetBirthDay(killer.BirthDay + CampaignTime.Days(1));
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ── Child inheritance ─────────────────────────────────────────────────
        // 75% if both parents are mages, 50% if one, 10% otherwise
        private void OnHeroCreated(Hero hero, bool bornNaturally)
        {
            if (!bornNaturally) return;
            try
            {
                bool motherMage = hero.Mother != null && (
                    hero.Mother == Hero.MainHero ? MageKnowledge.IsMage
                                                 : ColourLordRegistry.IsColourLord(hero.Mother));
                bool fatherMage = hero.Father != null && (
                    hero.Father == Hero.MainHero ? MageKnowledge.IsMage
                                                 : ColourLordRegistry.IsColourLord(hero.Father));

                float chance;
                if (motherMage && fatherMage)       chance = 0.75f;
                else if (motherMage || fatherMage)   chance = 0.50f;
                else                                 chance = 0.10f;

                if ((float)_rng.NextDouble() < chance)
                {
                    bool isPlayerChild = hero.Mother == Hero.MainHero || hero.Father == Hero.MainHero;
                    if (isPlayerChild)
                        MageKnowledge.AddGiftedChild(hero.StringId);
                    else
                        ColourLordRegistry.SetMage(hero, true);
                }
            }
            catch { }
        }

        // ── Companion recruitment ─────────────────────────────────────────────
        private void OnCompanionAdded(Hero companion)
        {
            try
            {
                if (_rng.Next(100) < 10)
                    ColourLordRegistry.SetMage(companion, true);
            }
            catch { }
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            try { dataStore.SyncData("LDM_SelectionDone",    ref _selectionDone); } catch { }
            try { dataStore.SyncData("LDM_PrisonerSnapshot", ref _prisonerCountSnapshot); } catch { }
            try { dataStore.SyncData("LDM_DayCounter",       ref _dayCounter); } catch { }
            try { dataStore.SyncData("LDM_ReapRaidCooldown", ref _reapRaidCooldown); } catch { }
            try { dataStore.SyncData("LDM_LordAnnounceCD",   ref _lordAnnounceCountdown); } catch { }
            try { dataStore.SyncData("LDM_LordAnnounceDone", ref _lordAnnouncementDone); } catch { }
            try { MageKnowledge.Save(dataStore); } catch { }
            try { ColourLordRegistry.Save(dataStore); } catch { }
            try { AshenCitySystem.Save(dataStore); } catch { }
            try { FireWorshippersSystem.Save(dataStore); } catch { }
            try { CampaignMapEvents.Save(dataStore); } catch { }
            try { SettlementEncounters.Save(dataStore); } catch { }
            try { DragonQuestSystem.Save(dataStore); } catch { }
        }
    }
}
