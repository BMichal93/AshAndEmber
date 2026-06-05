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
        // Guard against HeroKilledEvent firing multiple times for the same execution
        // (Bannerlord can fire the event twice under certain load conditions).
        private readonly HashSet<string> _executedLordIds = new HashSet<string>();
        // Tracks how many days each Ashen lord has been in captivity (StringId → days).
        // Ashen lords auto-escape after 3 days — the cold does not yield to chains.
        private readonly Dictionary<string, int> _ashenCaptiveDays = new Dictionary<string, int>();

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
                BurningLabQuestSystem.ResetForNewGame();
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
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Casting costs criminal standing. After your first working each day, further casts risk possession. Alt+X = Grimoire.",
                            new Color(0.3f, 0.35f, 0.7f)));
                    }
                    else if (isMage)
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
                    if (isAshen)
                    {
                        try
                        {
                            if (Hero.MainHero?.Clan?.Kingdom is TaleWorlds.CampaignSystem.Kingdom oldK)
                                TaleWorlds.CampaignSystem.Actions.ChangeCrimeRatingAction.Apply(oldK, 50f, true);
                        }
                        catch { }
                        try { AshenCitySystem.OnPlayerBecameAshen(); } catch { }
                    }
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
                try { TalentSystem.ResetDailyCastCount(); } catch { }
                try { TalentSystem.EnforceKinship(); } catch { }
                try { TalentSystem.DailyFadeTick(); } catch { }
                try { AgingSystem.DailyAgeCheck(); } catch { }
                try { CampaignMapEvents.DailyTick(); } catch { }
                try { SettlementEncounters.DailyTick(); } catch { }
                try { DragonQuestSystem.DailyTick(); } catch { }
                try { BurningLabQuestSystem.DailyTick(); } catch { }
                try { CheckReapPrisonerYield(); } catch { }
                if (_reapRaidCooldown > 0) _reapRaidCooldown--;
                try { CheckAshenPrisonerEscape(); } catch { }
                try { CheckMageOverexertion(); } catch { }
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
                try { BurningLabQuestSystem.WeeklyTick(); } catch { }
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

        // ── Ashen prisoner auto-escape ────────────────────────────────────────
        // Ashen lords escape captivity after at most 3 days — the cold does not
        // yield to chains.
        private void CheckAshenPrisonerEscape()
        {
            try
            {
                // Tick up days for each Ashen lord prisoner; release at 3 days.
                foreach (Hero h in Hero.AllAliveHeroes
                    .Where(x => x.IsAlive && x.IsPrisoner && ColourLordRegistry.IsAshenLord(x)).ToList())
                {
                    if (!_ashenCaptiveDays.TryGetValue(h.StringId, out int days))
                        days = 0;
                    days++;
                    if (days >= 3)
                    {
                        _ashenCaptiveDays.Remove(h.StringId);
                        try { EndCaptivityAction.ApplyByEscape(h, null); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{h.Name} — the cold does not yield to chains. They walked out of captivity in the night.",
                            new Color(0.38f, 0.50f, 0.75f)));
                    }
                    else
                    {
                        _ashenCaptiveDays[h.StringId] = days;
                    }
                }
                // Clear stale entries for heroes no longer a prisoner
                foreach (string id in _ashenCaptiveDays.Keys.ToList())
                {
                    Hero h = null;
                    try { h = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == id); } catch { }
                    if (h == null || !h.IsPrisoner) _ashenCaptiveDays.Remove(id);
                }
            }
            catch { }
        }

        // ── Mage overexertion → Ashen whisper ────────────────────────────────
        // Mage lords aged 80+ hear the cold's call more clearly each day.
        // The chance scales gradually from ~0.05%/day at 80 to ~0.5%/day at 95.
        private void CheckMageOverexertion()
        {
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes
                    .Where(x => x.IsLord && x.IsAlive && !x.IsChild
                             && !ColourLordRegistry.IsAshenLord(x)
                             && ColourLordRegistry.IsColourLord(x)
                             && x != Hero.MainHero
                             && x.Age >= 80f).ToList())
                {
                    float excess      = Math.Min(15f, (float)h.Age - 80f);
                    float dailyChance = 0.0005f + excess * 0.00003f; // 0.05%→0.095%/day
                    if (_rng.NextDouble() < dailyChance)
                        TryConvertMageToAshen(h, "could feel the cold at the edge of the fire");
                }
            }
            catch { }
        }

        // ── Ashen conversion helper ───────────────────────────────────────────
        private static void TryConvertMageToAshen(Hero h, string reason)
        {
            if (h == null || !h.IsAlive || ColourLordRegistry.IsAshenLord(h)) return;
            // Require clan viability so the conversion doesn't collapse a faction.
            if (h.Clan?.Kingdom != null
                && h.Clan.Kingdom.Clans.Count(c => c != null && !c.IsEliminated) < 2) return;
            try
            {
                try { ColourLordRegistry.SetAshen(h, true); }              catch { }
                try { AshenCitySystem.ApplyAshenPersonality(h); }          catch { }
                try { ColourLordRegistry.SetMage(h, true); }               catch { }
                try { AshenCitySystem.OnHeroSetAshen(h); }                 catch { }
                try { MageKnowledge.ApplyAshenAppearance(h); }             catch { }
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{h.Name} — {reason}. The fire did not answer. Something colder did.",
                    new Color(0.38f, 0.50f, 0.75f)));
            }
            catch { }
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

                            // agingCost = sum of ComputeBattleAgingCost(inputs) per spell,
                            // already computed geometrically inside RecordCast.
                            int agingCost = ColourLordAI.ConsumeBattleCasts(leader);
                            if (agingCost <= 0) continue;

                            if (!ColourLordRegistry.IsAshenLord(leader))
                            {
                                AgeHeroDeferred(leader, agingCost);
                                // Heavy overexertion: if a lord aged 15+ days in one battle,
                                // the cold whispers to them — 8% chance of Ashen conversion.
                                if (agingCost >= 15 && _rng.Next(100) < 8)
                                    TryConvertMageToAshen(leader, "overexerted themselves in battle");
                            }
                            if (playerInvolved)
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{leader.Name} is spent by the working — {agingCost} day{(agingCost > 1 ? "s" : "")} older.",
                                    new Color(0.5f, 0.4f, 0.7f)));
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // Off-screen battles: ColourLordAI never ran, so _battleCasts is empty.
            // Apply small random aging (80% chance, 1–3 days) to simulate mages casting.
            if (!playerInvolved)
            {
                try
                {
                    foreach (MapEventSide side in new[] { mapEvent.AttackerSide, mapEvent.DefenderSide })
                    {
                        if (side == null) continue;
                        foreach (var meparty in side.Parties)
                        {
                            try
                            {
                                Hero leader = meparty?.Party?.LeaderHero;
                                if (leader == null || leader == Hero.MainHero
                                    || !ColourLordRegistry.IsColourLord(leader)
                                    || ColourLordRegistry.IsAshenLord(leader)) continue;
                                if (_rng.NextDouble() < 0.80)
                                    AgeHeroDeferred(leader, 1 + _rng.Next(3));
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                return;
            }

            // Also age companion mages travelling in the player's party.
            // ApplyNpcBattleAging only reaches party leaders above; companions are non-leaders
            // and would never be aged otherwise even though ColourLordAI tracks their casts.
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                foreach (var entry in roster.GetTroopRoster().ToList())
                {
                    Hero companion = entry.Character?.HeroObject;
                    if (companion == null || companion == Hero.MainHero) continue;
                    if (!ColourLordRegistry.IsColourLord(companion)) continue;

                    int agingCost = ColourLordAI.ConsumeBattleCasts(companion);
                    if (agingCost <= 0) continue;

                    if (!ColourLordRegistry.IsAshenLord(companion))
                        AgeHeroDeferred(companion, agingCost);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{companion.Name} is spent by the working — {agingCost} day{(agingCost > 1 ? "s" : "")} older.",
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

                // Reap: executing a captured lord draws back 100 campaign days.
                // Guard with a StringId set — HeroKilledEvent can fire twice under certain
                // Bannerlord load/save conditions, causing double rejuvenation.
                if (killer == Hero.MainHero
                    && MageKnowledge.IsMage
                    && TalentSystem.Has(TalentId.Reap)
                    && victim.StringId != null
                    && !_executedLordIds.Contains(victim.StringId))
                {
                    _executedLordIds.Add(victim.StringId);
                    try { AgingSystem.RejuvenateHero(Hero.MainHero, 100); } catch { }
                }
            }
            catch { }
        }

        // ── Child inheritance ─────────────────────────────────────────────────
        // 75% if both parents are mages, 50% if one, 10% otherwise.
        // Ashen lords cannot have children — the cold preserves, not creates.
        private void OnHeroCreated(Hero hero, bool bornNaturally)
        {
            if (!bornNaturally) return;
            try
            {
                // Ashen parents cannot produce living children — still the cold.
                bool motherAshen = hero.Mother != null && ColourLordRegistry.IsAshenLord(hero.Mother);
                bool fatherAshen = hero.Father != null && ColourLordRegistry.IsAshenLord(hero.Father);
                if (motherAshen || fatherAshen)
                {
                    try { KillCharacterAction.ApplyByMurder(hero, null, false); } catch { }
                    return;
                }

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
            // Executed lord IDs — persisted so the double-fire guard survives save/load
            try
            {
                var elList = _executedLordIds.ToList();
                dataStore.SyncData("LDM_ExecutedLordIds", ref elList);
                if (elList != null)
                {
                    _executedLordIds.Clear();
                    foreach (var id in elList) _executedLordIds.Add(id);
                }
            }
            catch { }
            // Ashen prisoner escape tracker (save/load)
            try
            {
                var capKeys = _ashenCaptiveDays.Keys.ToList();
                var capVals = _ashenCaptiveDays.Values.ToList();
                dataStore.SyncData("LDM_AshenCapKeys", ref capKeys);
                dataStore.SyncData("LDM_AshenCapVals", ref capVals);
                if (capKeys != null && capVals != null && capKeys.Count == capVals.Count)
                {
                    _ashenCaptiveDays.Clear();
                    for (int i = 0; i < capKeys.Count; i++)
                        _ashenCaptiveDays[capKeys[i]] = capVals[i];
                }
            }
            catch { }
            try { MageKnowledge.Save(dataStore); } catch { }
            try { ColourLordRegistry.Save(dataStore); } catch { }
            try { AshenCitySystem.Save(dataStore); } catch { }
            try { FireWorshippersSystem.Save(dataStore); } catch { }
            try { CampaignMapEvents.Save(dataStore); } catch { }
            try { SettlementEncounters.Save(dataStore); } catch { }
            try { DragonQuestSystem.Save(dataStore); } catch { }
            try { BurningLabQuestSystem.Save(dataStore); } catch { }
        }
    }
}
