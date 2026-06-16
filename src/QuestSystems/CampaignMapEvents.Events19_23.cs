// =============================================================================
// ASH AND EMBER — CampaignMapEvents.Events19_23.cs
// World events 19–23 (Mage Fatwa, Temple Rises, Wolf, Gambit, …).
// Partial of CampaignMapEvents (shared state lives in CampaignMapEvents.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class CampaignMapEvents
    {
        // ── Event 19: Mage Fatwa ─────────────────────────────────────────────
        // Religious terror sweeps a random non-Ashen kingdom. Fanatics hunt
        // mage lords — 0–3 are killed by the mob before the violence is spent.
        // Ashen lords are immune (the mob does not touch what it truly fears).
        //
        // Safety constraints:
        //   • Never kills the player hero.
        //   • Only targets mage lords who are not clan leaders (avoids instant
        //     succession chaos mid-event) and whose clan has ≥ 2 living members.
        //   • Skips kingdoms with no eligible mage lord targets (silent no-fire).
        private static void TryFireMageFatwa()
        {
            if (_rng.NextDouble() >= ChanceMageFatwa) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenKingdomId)
                    .ToList();
                if (kingdoms.Count == 0) return;

                // Weight kingdoms that actually have mage lords
                var eligible = kingdoms
                    .Where(k => Hero.AllAliveHeroes.Any(h =>
                        h.IsLord && h.IsAlive && !h.IsChild && !h.IsPrisoner
                        && h != Hero.MainHero
                        && h.Clan?.Kingdom == k
                        && ColourLordRegistry.IsColourLord(h)
                        && !ColourLordRegistry.IsAshenLord(h)
                        && h.Clan.Leader != h
                        && h.Clan.Heroes.Count(x => x.IsAlive && !x.IsChild) >= 2))
                    .ToList();
                if (eligible.Count == 0) return;

                var kingdom = eligible[_rng.Next(eligible.Count)];

                var targets = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsChild && !h.IsPrisoner
                             && h != Hero.MainHero
                             && h.Clan?.Kingdom == kingdom
                             && ColourLordRegistry.IsColourLord(h)
                             && !ColourLordRegistry.IsAshenLord(h)
                             && h.Clan.Leader != h
                             && h.Clan.Heroes.Count(x => x.IsAlive && !x.IsChild) >= 2)
                    .OrderBy(_ => _rng.Next())
                    .Take(_rng.Next(4))   // 0–3
                    .ToList();

                string kingdomName = kingdom.Name?.ToString() ?? "the realm";

                if (targets.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Mage Fatwa — fear of the fire and ash swept {kingdomName} like a fever. " +
                        $"Torches were lit. Doors were barred. The mages stayed hidden long enough for the mood to break."));
                    return;
                }

                var killed = new List<string>();
                foreach (var h in targets)
                {
                    try
                    {
                        killed.Add(h.Name?.ToString() ?? "a mage");
                        KillCharacterAction.ApplyByMurder(h, null, false);
                    }
                    catch { }
                }

                string nameList = killed.Count == 1 ? killed[0]
                    : killed.Count == 2 ? $"{killed[0]} and {killed[1]}"
                    : $"{killed[0]}, {killed[1]}, and {killed.Count - 2} others";

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Mage Fatwa — a preacher in {kingdomName} declared that the fire-touched were an abomination. " +
                    $"The crowd agreed. {nameList} did not survive the week. " +
                    $"The mob does not need to understand what it fears — only that it fears it."));
            }
            catch { }
        }

        // ── Event 20: The Temple Rises ────────────────────────────────────────
        // Once per campaign, after campaign day 100: one of the three canonical
        // cities (Diathma/Makeb/Omor) — or any valid Empire/Khuzait/Sturgia town
        // if none are eligible — breaks away. Its owner clan founds The Temple,
        // a militant holy order sworn to end the Ashen. One second clan joins
        // automatically. The player is offered the choice to join too.
        //
        // The Temple is always at war with the Ashen (re-declared daily).
        // It never initiates war on other factions (the AI is too resource-starved
        // with one city; any war it is drawn into is by other factions' choice).
        //
        // Safety constraints:
        //   • Fires at most once (_templeFounded flag, saved/loaded).
        //   • Not before TempleEarliestDay (120).
        //   • Never takes a ruling clan (faction stays viable).
        //   • Never targets a besieged city.
        //   • Never targets a city whose owner clan is the player's.
        //   • Settlement stabilised immediately: loyalty + security forced to 100.
        //   • Kingdom creation uses modern API with legacy MBObjectManager fallback;
        //     any failure at creation time aborts the whole event silently.
        //   • _templeFounded is only set TRUE after the kingdom is confirmed valid.
        private static void TryFireTheTemple()
        {
            if (_templeFounded) return;
            if (!_debugForceNextTemple)
            {
                double days = ElapsedCampaignDays();
                if (days < TempleEarliestDay) return;
                float chance = days >= TempleNearCertainDay ? ChanceTempleLatent
                             : days >= TempleSecondTierDay  ? ChanceTempleSecond
                             :                                ChanceTheTemple;
                if (_rng.NextDouble() >= chance) return;
            }
            if (!TryClaimWeeklySlot()) return;
            _debugForceNextTemple = false;
            try
            {
                // ── Pick the founding city ─────────────────────────────────────
                var preferredNames = new[] { "Diathma", "Makeb", "Omor" }
                    .OrderBy(_ => _rng.Next()).ToArray();

                Settlement chosenCity = null;
                foreach (var pName in preferredNames)
                {
                    var s = Settlement.All.FirstOrDefault(x =>
                        x.IsTown
                        && string.Equals(x.Name?.ToString(), pName, StringComparison.OrdinalIgnoreCase)
                        && IsValidTempleCity(x));
                    if (s != null) { chosenCity = s; break; }
                }

                if (chosenCity == null)
                {
                    // Fallback: any qualifying Empire/Khuzait/Sturgia town
                    var fallbackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        { "empire_w", "empire", "empire_s", "empire_n", "khuzait", "sturgia" };
                    chosenCity = Settlement.All
                        .Where(x => x.IsTown && IsValidTempleCity(x)
                                 && x.OwnerClan?.Kingdom != null
                                 && fallbackIds.Contains(x.OwnerClan.Kingdom.StringId))
                        .OrderBy(_ => _rng.Next())
                        .FirstOrDefault();
                }

                if (chosenCity == null) return;

                Clan foundingClan   = chosenCity.OwnerClan;
                Kingdom sourceKingdom = foundingClan.Kingdom;

                // ── Find a second clan to join automatically ────────────────────
                Clan secondClan = sourceKingdom?.Clans
                    .Where(c => c != null && !c.IsEliminated
                             && c != foundingClan
                             && c != sourceKingdom.RulingClan
                             && c.Leader != null && c.Leader.IsAlive
                             && !c.Leader.IsChild
                             && c.Leader != Hero.MainHero
                             && c.Heroes.Count(h => h.IsAlive && !h.IsChild) >= 1)
                    .OrderBy(_ => _rng.Next())
                    .FirstOrDefault();

                string cityName   = chosenCity.Name?.ToString()   ?? "a great city";
                string clanName   = foundingClan.Name?.ToString() ?? "the founders";
                string secondName = secondClan?.Name?.ToString();

                // ── Create The Temple kingdom ──────────────────────────────────
                Kingdom temple = null;
                try
                {
                    temple = MBObjectManager.Instance.CreateObject<Kingdom>("the_temple");
                    if (temple == null) return;

                    // InitializeKingdom(name, informalName, culture, banner,
                    //   color1, color2, capitalSettlement,
                    //   encyclopediaText, rulerTitle, rulerDescription)
                    temple.InitializeKingdom(
                        new TextObject("The Temple"),
                        new TextObject("Temple"),
                        foundingClan.Culture,
                        new Banner(foundingClan.Banner.Serialize()),
                        foundingClan.Color,
                        foundingClan.Color2,
                        chosenCity,
                        new TextObject("A militant order sworn to oppose the Ashen at any cost."),
                        new TextObject("High Templar"),
                        new TextObject("Led by those who answered the call when kingdoms would not."));

                    // For a new empty kingdom, ApplyByCreateKingdom makes the clan its ruler.
                    // Fall back to ApplyByJoinToKingdom if the former API isn't available.
                    try   { ChangeKingdomAction.ApplyByCreateKingdom(foundingClan, temple, false); }
                    catch { ChangeKingdomAction.ApplyByJoinToKingdom(foundingClan, temple); }
                }
                catch { temple = null; }

                if (temple == null || temple.IsEliminated) return;
                _templeFounded = true;   // set only once kingdom is confirmed valid

                // ── Transfer and stabilise the founding city ───────────────────
                try
                {
                    ChangeOwnerOfSettlementAction.ApplyByDefault(foundingClan.Leader, chosenCity);
                    if (chosenCity.Town != null)
                    {
                        chosenCity.Town.Loyalty  = 100f;
                        chosenCity.Town.Security = 100f;
                    }
                }
                catch { }

                // ── Second clan joins ──────────────────────────────────────────
                if (secondClan != null)
                    try { ChangeKingdomAction.ApplyByJoinToKingdom(secondClan, temple); } catch { }

                // ── Declare permanent war on the Ashen ─────────────────────────
                try
                {
                    var ashen = Kingdom.All.FirstOrDefault(k =>
                        k.StringId == AshenKingdomId && !k.IsEliminated);
                    if (ashen != null && !temple.IsAtWarWith(ashen))
                        DeclareWarAction.ApplyByDefault(temple, ashen);
                }
                catch { }

                // ── Player prompt ──────────────────────────────────────────────
                string secondLine = secondName != null
                    ? $" {secondName} answered the call before the sun rose."
                    : "";
                string warningLine =
                    (Hero.MainHero?.Clan != null
                  && Hero.MainHero.Clan == Hero.MainHero.Clan?.Kingdom?.RulingClan)
                    ? "\n\n[Warning: you are your faction's ruling clan. Joining will leave your kingdom leaderless.]"
                    : "";

                string body =
                    $"A preacher climbed the steps of the great hall in {cityName} and spoke of fire — " +
                    $"a flame that lives in us all and the cold that walks south to extinguish it.\n\n" +
                    $"He said the kingdoms argue policy while the Ashen march goes unanswered. " +
                    $"He said that we must stand against them as one, or perish.\n\n" +
                    $"{clanName} listened. Then they left their old banners behind.{secondLine} " +
                    $"They have raised a new standard: The Temple. " +
                    $"Their only declared war is with the Ashen. " +
                    $"It will not end until one side has no ground left to stand on.{warningLine}";

                InformationManager.ShowInquiry(new InquiryData(
                    "The Temple Rises",
                    body,
                    true, true,
                    "Join The Temple",
                    "Watch from a distance",
                    () =>
                    {
                        // ChangeKingdomAction.ApplyByJoinToKingdom silently rejects tier-0 clans.
                        if ((Hero.MainHero?.Clan?.Tier ?? 0) < 1)
                        {
                            MBInformationManager.AddQuickInformation(new TextObject(
                                "Your clan is too small to answer the call. Prove yourselves first."));
                            return;
                        }
                        // Kingdom actions are not safe inside an inquiry callback.
                        // Defer to the next daily tick where campaign state is stable.
                        _pendingTempleJoin = true;
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "Your clan answers the call. The Temple's banner is yours now."));
                    },
                    null
                ), true);
            }
            catch { }
        }

        // ── Event 21: Peasant Unrest ─────────────────────────────────────────
        // The people have had enough. Three bands of desperate peasants-turned-
        // brigands take to the roads near a random lord's settlement.
        //
        // Safety: looter parties use the same hideout-safe pattern as Ashen Spawn.
        private static void TryFirePeasantUnrest()
        {
            if (_rng.NextDouble() >= ChancePeasantUnrest) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenKingdomId)
                    .ToList();
                if (kingdoms.Count == 0) return;

                var kingdom = kingdoms[_rng.Next(kingdoms.Count)];
                var anchors = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle) && s.MapFaction == kingdom)
                    .ToList();
                if (anchors.Count == 0) return;

                var anchor = anchors[_rng.Next(anchors.Count)];

                int spawned = 0;
                for (int i = 0; i < 3; i++)
                {
                    if (SpawnLooterParty(anchor.GetPosition2D, 50) != null) spawned++;
                }

                if (spawned > 0)
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Peasant Unrest — The people of {kingdom.Name} have had enough. " +
                        $"Three ragged bands broke from the fields near {anchor.Name} last night, " +
                        $"carrying scythes and old iron. No lord called them — no lord can stop them easily."));
            }
            catch { }
        }

        // ── Event 22: A Wolf in Sheep's Clothing ─────────────────────────────
        // A minor lord in a random kingdom is accused of serving the Ashen.
        //
        // Not in player's kingdom: silent execution, notification only.
        // Player in kingdom, tier < 4: Charm-modified 33% chance player is accused
        //   and expelled; otherwise a random minor lord is executed.
        // Player in kingdom, tier ≥ 4: four-choice Inquiry (accuse, accuse other,
        //   say nothing, suggest innocence — last has 33% traitor-twist).
        private static void TryFireWolfSheepClothing()
        {
            if (_rng.NextDouble() >= ChanceWolfSheepCloth) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && k.Leader != null
                             && k.Clans.Count(c => c != null && !c.IsEliminated) >= 3)
                    .ToList();
                if (kingdoms.Count == 0) return;

                var kingdom     = kingdoms[_rng.Next(kingdoms.Count)];
                var ruler       = kingdom.Leader;
                string kingdomName = kingdom.Name?.ToString() ?? "the realm";
                string rulerName   = ruler?.Name?.ToString() ?? "the ruler";

                // Collect two candidate minor lords
                var minorLords = kingdom.Clans
                    .Where(c => c != null && !c.IsEliminated
                             && c != kingdom.RulingClan
                             && c.Leader != null && c.Leader.IsAlive
                             && !c.Leader.IsChild
                             && c.Leader != Hero.MainHero
                             && c.Heroes.Count(h => h.IsAlive && !h.IsChild) >= 1)
                    .OrderBy(_ => _rng.Next())
                    .Take(2)
                    .Select(c => c.Leader)
                    .ToList();
                if (minorLords.Count == 0) return;

                var    lord1     = minorLords[0];
                var    lord2     = minorLords.Count > 1 ? minorLords[1] : null;
                string lord1Name = lord1.Name?.ToString() ?? "a lord";
                string lord2Name = lord2?.Name?.ToString() ?? "";
                bool   hasBoth   = lord2 != null && lord2.IsAlive;
                bool   playerIn  = Hero.MainHero?.Clan?.Kingdom == kingdom;

                if (!playerIn)
                {
                    var victim = minorLords[_rng.Next(minorLords.Count)];
                    try { KillCharacterAction.ApplyByMurder(victim, null, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"A Wolf in Sheep's Clothing — {victim.Name} of {kingdomName} was accused " +
                        $"of serving the Ashen. The verdict arrived before they could speak. " +
                        $"Their family maintains innocence. The court did not ask."));
                    return;
                }

                int playerTier = Hero.MainHero?.Clan?.Tier ?? 0;

                if (playerTier < 4)
                {
                    int   charm = Hero.MainHero?.GetSkillValue(DefaultSkills.Charm) ?? 0;
                    float p     = Math.Max(0.05f, 0.33f - charm / 300f * 0.25f);
                    if (_rng.NextDouble() < p)
                    {
                        string clName = Hero.MainHero?.Clan?.Name?.ToString() ?? "your clan";
                        try { ChangeKingdomAction.ApplyByLeaveKingdom(Hero.MainHero.Clan, false); } catch { }
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"A Wolf in Sheep's Clothing — The whispers of {kingdomName} found {clName}. " +
                            $"There was no real trial. {rulerName} signed the expulsion before midday. " +
                            $"You are cast out. Your Charm softened the odds — this time it was not enough."));
                    }
                    else
                    {
                        var victim = minorLords[_rng.Next(minorLords.Count)];
                        try { KillCharacterAction.ApplyByMurder(victim, null, false); } catch { }
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"A Wolf in Sheep's Clothing — {kingdomName}'s court needed an answer. " +
                            $"{victim.Name} gave them one by existing. Executed before sunset; " +
                            $"guilt neither proven nor questioned."));
                    }
                    return;
                }

                // Tier ≥ 4: four choices
                var elems = new List<InquiryElement>
                {
                    new InquiryElement("a", $"Accuse {lord1Name} — they are the traitor.", null, true,
                        $"{lord1Name} is executed. +10 with {rulerName}."),
                    new InquiryElement("b",
                        hasBoth ? $"Accuse {lord2Name} — they are the traitor." : "Let the court choose.",
                        null, true,
                        hasBoth ? $"{lord2Name} is executed. +10 with {rulerName}."
                                : "A random lord is chosen. No relation effects."),
                    new InquiryElement("c", "Say nothing. Let the court decide.", null, true,
                        "One is executed at random. No relation effects."),
                    new InquiryElement("d", "Suggest both are innocent. The evidence doesn't hold.", null, true,
                        $"+100 with the accused. 33% chance one was truly a traitor — if so, −10 with {rulerName}."),
                };

                string body =
                    $"The court of {kingdomName} is alive with whispers. " +
                    $"{lord1Name}" + (hasBoth ? $" and {lord2Name} are" : " is") +
                    $" accused of serving the Ashen. The evidence is thin. The mood is not.\n\n" +
                    $"{rulerName} turns to you. At your clan's standing, your voice carries weight.";

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "A Wolf in Sheep's Clothing",
                    body, elems, false, 1, 1, "Speak", "",
                    chosen =>
                    {
                        try
                        {
                            switch (chosen?[0]?.Identifier as string)
                            {
                                case "a":
                                    try { KillCharacterAction.ApplyByMurder(lord1, null, false); } catch { }
                                    if (ruler?.IsAlive == true && Hero.MainHero != null)
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                            Hero.MainHero, ruler, +10, false); } catch { }
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"A Wolf in Sheep's Clothing — You named {lord1Name}. " +
                                        $"The court accepted it. The execution was before dusk. " +
                                        $"{rulerName} nodded in your direction."));
                                    break;
                                case "b":
                                    if (hasBoth)
                                    {
                                        try { KillCharacterAction.ApplyByMurder(lord2, null, false); } catch { }
                                        if (ruler?.IsAlive == true && Hero.MainHero != null)
                                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                                Hero.MainHero, ruler, +10, false); } catch { }
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            $"A Wolf in Sheep's Clothing — You named {lord2Name}. " +
                                            $"The court accepted it without debate. " +
                                            $"You bought goodwill, and you know exactly what that cost."));
                                    }
                                    else
                                    {
                                        var v = minorLords[_rng.Next(minorLords.Count)];
                                        try { KillCharacterAction.ApplyByMurder(v, null, false); } catch { }
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            $"A Wolf in Sheep's Clothing — The court chose. " +
                                            $"{v.Name} did not survive the night."));
                                    }
                                    break;
                                case "c":
                                {
                                    var v = minorLords[_rng.Next(minorLords.Count)];
                                    try { KillCharacterAction.ApplyByMurder(v, null, false); } catch { }
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"A Wolf in Sheep's Clothing — You said nothing. " +
                                        $"The court chose its own answer. {v.Name} did not survive the night. " +
                                        $"You kept your hands clean. Someone's blood was on them regardless."));
                                    break;
                                }
                                case "d":
                                {
                                    if (lord1.IsAlive && Hero.MainHero != null)
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                            Hero.MainHero, lord1, +100, false); } catch { }
                                    if (hasBoth && lord2.IsAlive && Hero.MainHero != null)
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                            Hero.MainHero, lord2, +100, false); } catch { }

                                    if (_rng.NextDouble() < 0.33)
                                    {
                                        var traitor = minorLords[_rng.Next(minorLords.Count)];
                                        try { ColourLordRegistry.SetAshen(traitor, true); } catch { }
                                        try { AshenCitySystem.ApplyAshenPersonality(traitor); } catch { }
                                        try { ColourLordRegistry.SetMage(traitor, true); } catch { }
                                        try { AshenCitySystem.OnHeroSetAshen(traitor); } catch { }
                                        try { MageKnowledge.ApplyAshenAppearance(traitor); } catch { }
                                        if (ruler?.IsAlive == true && Hero.MainHero != null)
                                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                                Hero.MainHero, ruler, -10, false); } catch { }
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            $"A Wolf in Sheep's Clothing — You spoke for their innocence and were believed. " +
                                            $"Three days later, {traitor.Name} vanished from their chambers, " +
                                            $"found among the Ashen — grey-eyed and cold. The accusation was true. " +
                                            $"{rulerName} did not forget that you vouched for them."));
                                    }
                                    else
                                    {
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            $"A Wolf in Sheep's Clothing — You spoke for their innocence. " +
                                            $"The court, grudgingly, accepted it. The accused remember. " +
                                            $"Whether the accusation had merit, neither you nor anyone else " +
                                            $"will ever be entirely certain."));
                                    }
                                    break;
                                }
                            }
                        }
                        catch { }
                    }, null, "", false), false);
            }
            catch { }
        }

        // ── Event 23: The Ashen Gambit ────────────────────────────────────────
        // Fires at most once per campaign, no earlier than AshenGambitEarliestDay.
        // Ashen assassins — woven through every Imperial court like cold thread —
        // coordinate their move in a single night of dark fire and silence:
        //
        //   Phase 1 — Kill all living Empire faction leaders (silent murder, no
        //             attribution). Skipped leaders: player hero, child heroes,
        //             any king whose kingdom has only 1 surviving clan (succession
        //             must be possible).
        //   Phase 2 — Apply −30 morale to every active Empire lord party.
        //   Phase 3 — Apply −30 security to every Empire town (floor 0).
        //   Phase 4 — Spawn AshenGambitSpawnCount Ashen Spawn warbands, each with
        //             minStrength 80, distributed across Empire settlement anchors.
        //             All spawns use the hideout-safe pattern to prevent crashes.
        //   Phase 4b— Up to AshenGambitCastleCount random Empire castles (not under
        //             siege, not player-owned) are seized by a random Ashen lord via
        //             ChangeOwnerOfSettlementAction. Each castle is stabilised to
        //             prevent an immediate rebellion tick.
        //   Phase 5 — Ensure the Ashen kingdom is at war with every Empire faction,
        //             then surge Ashen lord party morale +50 to drive them onto the
        //             offensive.
        //
        // Sanctuary protection blocks the event with a notification.
        // Safety constraints:
        //   • Never kills the player hero.
        //   • Only kills leaders of kingdoms with ≥ 2 surviving clans.
        //   • Each phase is individually try/caught; one failure cannot abort the rest.
        private static void TryFireAshenGambit()
        {
            if (_ashenGambitFired) return;
            if (ElapsedCampaignDays() < AshenGambitEarliestDay) return;
            if (_rng.NextDouble() >= ChanceAshenGambit) return;
            if (!TryClaimWeeklySlot()) return;

            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "The Ashen Gambit — The sanctuary's ward blazes bright. The assassins feel it like a wall of fire " +
                    "and pull back into the dark. Tonight, the Empire's lords sleep safely."));
                return;
            }

            _ashenGambitFired = true;

            // ── Phase 1: Kill all living Empire faction leaders ───────────────
            var killedNames = new List<string>();
            try
            {
                var empireKingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && EmpireKingdomIds.Contains(k.StringId)
                             && k.Leader != null
                             && k.Leader.IsAlive
                             && !k.Leader.IsChild
                             && k.Leader != Hero.MainHero
                             && k.Clans.Count(c => c != null && !c.IsEliminated) >= 2)
                    .ToList();

                foreach (var kingdom in empireKingdoms)
                {
                    try
                    {
                        killedNames.Add(kingdom.Leader.Name?.ToString() ?? "an emperor");
                        KillCharacterAction.ApplyByMurder(kingdom.Leader, null, false);
                    }
                    catch { }
                }
            }
            catch { }

            // ── Phase 2: −30 morale to all active Empire lord parties ─────────
            int moraleHit = 0;
            try
            {
                foreach (var hero in Hero.AllAliveHeroes)
                {
                    if (!hero.IsLord || hero.IsChild || hero.PartyBelongedTo == null) continue;
                    if (hero.Clan?.Kingdom == null) continue;
                    if (!EmpireKingdomIds.Contains(hero.Clan.Kingdom.StringId)) continue;
                    try
                    {
                        hero.PartyBelongedTo.RecentEventsMorale -= 30f;
                        moraleHit++;
                    }
                    catch { }
                }
            }
            catch { }

            // ── Phase 3: −30 security to every Empire town (floor 0) ─────────
            int secHit = 0;
            try
            {
                foreach (var settlement in Settlement.All)
                {
                    if (!settlement.IsTown || settlement.Town == null) continue;
                    if (!EmpireKingdomIds.Contains(settlement.MapFaction?.StringId)) continue;
                    try
                    {
                        settlement.Town.Security = Math.Max(0f, settlement.Town.Security - 30f);
                        secHit++;
                    }
                    catch { }
                }
            }
            catch { }

            // ── Phase 4: Spawn Ashen Spawn across Empire heartlands ───────────
            int spawned = 0;
            try
            {
                var empireAnchors = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle)
                             && EmpireKingdomIds.Contains(s.MapFaction?.StringId))
                    .Select(s => s.GetPosition2D)
                    .ToList();

                if (empireAnchors.Count > 0)
                {
                    for (int i = 0; i < AshenGambitSpawnCount; i++)
                    {
                        var anchor = empireAnchors[i % empireAnchors.Count];
                        var party  = SpawnAshenSpawnParty(anchor, baseTroops: 15, minStrength: 80f);
                        if (party != null) spawned++;
                    }
                }
            }
            catch { }

            // ── Phase 4b: Seize Empire castles in the dead of night ──────────
            var seizedNames = new List<string>();
            try
            {
                var empireCastles = Settlement.All
                    .Where(s => s.IsCastle
                             && !s.IsUnderSiege
                             && s.OwnerClan?.Kingdom != null
                             && EmpireKingdomIds.Contains(s.OwnerClan.Kingdom.StringId)
                             && s.OwnerClan.Leader != Hero.MainHero)
                    .OrderBy(_ => _rng.Next())
                    .Take(AshenGambitCastleCount)
                    .ToList();

                var ashenLords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsDisabled && !h.IsPrisoner
                             && ColourLordRegistry.IsAshenLord(h))
                    .ToList();

                if (ashenLords.Count > 0)
                {
                    foreach (var castle in empireCastles)
                    {
                        try
                        {
                            var lord = ashenLords[_rng.Next(ashenLords.Count)];
                            ChangeOwnerOfSettlementAction.ApplyByDefault(lord, castle);
                            StabiliseSettlement(castle);
                            seizedNames.Add(castle.Name?.ToString() ?? "a castle");
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // ── Phase 5: Ashen go on the offensive ───────────────────────────
            try
            {
                var ashenKingdom = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == AshenKingdomId && !k.IsEliminated);

                if (ashenKingdom != null)
                {
                    foreach (var empire in Kingdom.All
                        .Where(k => !k.IsEliminated && EmpireKingdomIds.Contains(k.StringId)))
                    {
                        try
                        {
                            if (!ashenKingdom.IsAtWarWith(empire))
                                DeclareWarAction.ApplyByDefault(ashenKingdom, empire);
                        }
                        catch { }
                    }
                }

                // Surge Ashen lord party morale — push them into aggressive campaigning
                foreach (var party in MobileParty.All)
                {
                    if (!party.IsActive) continue;
                    var leader = party.LeaderHero;
                    if (leader != null && ColourLordRegistry.IsAshenLord(leader))
                    {
                        try { party.RecentEventsMorale += 50f; } catch { }
                    }
                }
            }
            catch { }

            // ── Notification ──────────────────────────────────────────────────
            string leaderStr = killedNames.Count == 0
                ? "the Imperial thrones stand empty by morning"
                : killedNames.Count == 1
                    ? $"{killedNames[0]} is dead"
                    : killedNames.Count <= 3
                        ? string.Join(", ", killedNames.Take(killedNames.Count - 1))
                          + $" and {killedNames[killedNames.Count - 1]} are dead"
                        : $"{killedNames[0]}, {killedNames[1]}, and {killedNames.Count - 2} other rulers are dead";

            string seizedStr = seizedNames.Count == 0 ? "" :
                seizedNames.Count == 1
                    ? $"{seizedNames[0]} fell to the Ashen before the sun rose. "
                    : string.Join(", ", seizedNames.Take(seizedNames.Count - 1))
                      + $" and {seizedNames[seizedNames.Count - 1]} fell to the Ashen before the sun rose. ";

            MBInformationManager.AddQuickInformation(new TextObject(
                $"The Ashen Gambit — In a single night of cold fire and silence, every Imperial throne was struck at once. " +
                $"{leaderStr}. Their courts woke to ash on the pillows and cooling blood on the floors. " +
                (moraleHit > 0 ? $"Dread swept through {moraleHit} Imperial warbands. " : "") +
                (secHit > 0 ? $"{secHit} Imperial cit{(secHit != 1 ? "ies" : "y")} erupted in panic and suspicion. " : "") +
                seizedStr +
                (spawned > 0
                    ? $"{spawned} Ashen Spawn rose from the shadows across the heartlands before dawn. "
                    : "") +
                "The cold armies do not wait. They march."));
        }

    }
}
