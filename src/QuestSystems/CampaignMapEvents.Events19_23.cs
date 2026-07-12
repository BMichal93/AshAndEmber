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

                bool isFatwaTemple = IsTempleFaction(kingdom);
                bool isFatwaTribes = IsTribes(kingdom);

                if (targets.Count == 0)
                {
                    string noKillMsg = isFatwaTemple
                        ? $"The Inquisitor's Writ — the Templar Inquisition of {kingdomName} raised a writ against the fire-touched. The accused sealed their doors and let the order's fury burn itself out. No blood was spilled — this time."
                        : isFatwaTribes
                        ? $"Mage Fatwa — the God-King of {kingdomName} moved against fire-touched who answer to no blood-pact but their own. The accused rode fast enough. The Vanguard came back empty-handed."
                        : $"Mage Fatwa — fear of the fire and ash swept {kingdomName} like a fever. Torches were lit. Doors were barred. The mages stayed hidden long enough for the mood to break.";
                    MBInformationManager.AddQuickInformation(new TextObject(noKillMsg));
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
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                string nameList = killed.Count == 1 ? killed[0]
                    : killed.Count == 2 ? $"{killed[0]} and {killed[1]}"
                    : $"{killed[0]}, {killed[1]}, and {killed.Count - 2} others";

                string fatwaMsg = isFatwaTemple
                    ? $"The Inquisitor's Writ — the Templar Inquisition of {kingdomName} declared the fire-touched an abomination against the Light's covenant. There was no mob. There was only the writ, the guard, and the door. {nameList} did not survive the chapter-room. The Temple does not need a crowd to be thorough."
                    : isFatwaTribes
                    ? $"Mage Fatwa — the God-King of {kingdomName} named the fire-touched as rivals to the divine flame. The tribesmen agreed without much persuading. {nameList} did not survive the week. The God-King does not share fire with those who serve no blood-pact."
                    : $"Mage Fatwa — a preacher in {kingdomName} declared that the fire-touched were an abomination. The crowd agreed. {nameList} did not survive the week. The mob does not need to understand what it fears — only that it fears it.";
                MBInformationManager.AddQuickInformation(new TextObject(fatwaMsg));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 20: The Temple Rises (retired) ─────────────────────────────
        // Vlandia IS The Holy Temple from the start of the campaign, so this
        // dynamic-founding event no longer exists.

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
                {
                    string unrestMsg = IsTempleFaction(kingdom)
                        ? $"Unrest in the Faithful — The lowest ranks of {kingdom.Name}'s faithful have broken from the tithe-roads near {anchor.Name}. Lay brothers and penitents who expected protection and received silence. They carry pilgrim staves and repurposed tools. No prior spoke for them. No prior can easily stop them."
                        : IsTribes(kingdom)
                        ? $"The Tribute Breaks — Tribesmen of {kingdom.Name} have broken from the tribute roads near {anchor.Name}, riding without the God-King's mark. Three bands, furious and armed. The steppe does not keep the unfed still for long."
                        : $"Peasant Unrest — The people of {kingdom.Name} have had enough. Three ragged bands broke from the fields near {anchor.Name} last night, carrying scythes and old iron. No lord called them — no lord can stop them easily.";
                    MBInformationManager.AddQuickInformation(new TextObject(unrestMsg));
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                             && IsFactionConspireEligible(k)
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

                bool isWolfTemple = IsTempleFaction(kingdom);
                bool isWolfTribes = IsTribes(kingdom);

                if (!playerIn)
                {
                    var victim = minorLords[_rng.Next(minorLords.Count)];
                    try { KillCharacterAction.ApplyByMurder(victim, null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    string worldNotif = isWolfTemple
                        ? $"A Wolf in Sheep's Clothing — {victim.Name} of {kingdomName} was denounced before the tribunal as an Ashen sympathiser. The Inquisitor's writ arrived before they could answer the charge. Their family maintains their faith. The tribunal did not ask."
                        : isWolfTribes
                        ? $"A Wolf in Sheep's Clothing — {victim.Name} of {kingdomName} was named before the God-King's war-council as having sold a blood-pact to the Ashen. The God-King's word was sentence enough. Their kin deny it. The denial was not heard."
                        : $"A Wolf in Sheep's Clothing — {victim.Name} of {kingdomName} was accused of serving the Ashen. The verdict arrived before they could speak. Their family maintains innocence. The court did not ask.";
                    MBInformationManager.AddQuickInformation(new TextObject(worldNotif));
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
                        try { ChangeKingdomAction.ApplyByLeaveKingdom(Hero.MainHero.Clan, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        string castOutMsg = isWolfTemple
                            ? $"A Wolf in Sheep's Clothing — The Inquisitor's eye fell on {clName}. There was no proper trial; the writ of expulsion was signed before you could address the altar. You are cast out. Your Charm softened the odds — this time, the Temple's fear was stronger."
                            : isWolfTribes
                            ? $"A Wolf in Sheep's Clothing — The God-King's eye fell on {clName}. You are cast out — not tried, not heard; the divine fire does not argue with itself. Your Charm was not enough to stand against the God-King's word."
                            : $"A Wolf in Sheep's Clothing — The whispers of {kingdomName} found {clName}. There was no real trial. {rulerName} signed the expulsion before midday. You are cast out. Your Charm softened the odds — this time it was not enough.";
                        MBInformationManager.AddQuickInformation(new TextObject(castOutMsg));
                    }
                    else
                    {
                        var victim = minorLords[_rng.Next(minorLords.Count)];
                        try { KillCharacterAction.ApplyByMurder(victim, null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        string scapeMsg = isWolfTemple
                            ? $"A Wolf in Sheep's Clothing — The tribunal of {kingdomName} needed a name. {victim.Name} gave them one by existing. Condemned before sunset; their faith neither proven nor questioned."
                            : isWolfTribes
                            ? $"A Wolf in Sheep's Clothing — The God-King needed the accusation to land somewhere. {victim.Name} was closest when the fire fell. Gone before sunrise; guilt neither spoken nor answered."
                            : $"A Wolf in Sheep's Clothing — {kingdomName}'s court needed an answer. {victim.Name} gave them one by existing. Executed before sunset; guilt neither proven nor questioned.";
                        MBInformationManager.AddQuickInformation(new TextObject(scapeMsg));
                    }
                    return;
                }

                // Tier ≥ 4: four choices
                string accuseLabel1 = isWolfTemple ? $"Name {lord1Name} — they carry the Ashen mark." : isWolfTribes ? $"Name {lord1Name} — they broke the blood-pact." : $"Accuse {lord1Name} — they are the traitor.";
                string accuseLabel2 = hasBoth
                    ? (isWolfTemple ? $"Name {lord2Name} — they carry the Ashen mark." : isWolfTribes ? $"Name {lord2Name} — they sold the blood-pact to the Ashen." : $"Accuse {lord2Name} — they are the traitor.")
                    : (isWolfTemple ? "Let the Inquisition choose." : isWolfTribes ? "Let the God-King decide." : "Let the court choose.");
                string silenceLabel = isWolfTemple ? "Keep silent. Let the tribunal decide." : isWolfTribes ? "Say nothing. Let the God-King's fire fall where it falls." : "Say nothing. Let the court decide.";
                string innocentLabel = isWolfTemple ? "Vouch for their faith. The evidence was planted." : isWolfTribes ? "Speak for them. A blood-pact cannot be broken so easily." : "Suggest both are innocent. The evidence doesn't hold.";

                var elems = new List<InquiryElement>
                {
                    new InquiryElement("a", accuseLabel1, null, true,
                        $"{lord1Name} is executed. +10 with {rulerName}."),
                    new InquiryElement("b", accuseLabel2, null, true,
                        hasBoth ? $"{lord2Name} is executed. +10 with {rulerName}." : "A random lord is chosen. No relation effects."),
                    new InquiryElement("c", silenceLabel, null, true,
                        "One is executed at random. No relation effects."),
                    new InquiryElement("d", innocentLabel, null, true,
                        $"+100 with the accused. 33% chance one was truly a traitor — if so, −10 with {rulerName}."),
                };

                string bodyOpening = isWolfTemple
                    ? $"The Inquisition of {kingdomName} has named names. {lord1Name}" + (hasBoth ? $" and {lord2Name} are" : " is") + $" accused of bearing the Ashen mark. The evidence is an interpreter's word and a cold hearthstone. The mood in the chapter house is not."
                    : isWolfTribes
                    ? $"The God-King has spoken a name. {lord1Name}" + (hasBoth ? $" and {lord2Name} are" : " is") + $" accused of swearing a blood-pact to the Ashen. The accusation comes from the war-council, not from proof. But the tribesmen believe what the God-King says."
                    : $"The court of {kingdomName} is alive with whispers. {lord1Name}" + (hasBoth ? $" and {lord2Name} are" : " is") + $" accused of serving the Ashen. The evidence is thin. The mood is not.";
                string body = $"{bodyOpening}\n\n{rulerName} turns to you. At your clan's standing, your voice carries weight.";

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
                                    try { KillCharacterAction.ApplyByMurder(lord1, null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                    if (ruler?.IsAlive == true && Hero.MainHero != null)
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                            Hero.MainHero, ruler, +10, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                    {
                                        string aMsg = isWolfTemple
                                            ? $"A Wolf in Sheep's Clothing — You named {lord1Name} before the tribunal. The Inquisition accepted it. The writ was sealed before dusk. {rulerName} acknowledged you with a nod."
                                            : isWolfTribes
                                            ? $"A Wolf in Sheep's Clothing — You named {lord1Name} before the God-King's war-council. The council accepted it without debate. They were gone before sunrise. {rulerName} remembered your voice."
                                            : $"A Wolf in Sheep's Clothing — You named {lord1Name}. The court accepted it. The execution was before dusk. {rulerName} nodded in your direction.";
                                        MBInformationManager.AddQuickInformation(new TextObject(aMsg));
                                    }
                                    break;
                                case "b":
                                    if (hasBoth)
                                    {
                                        try { KillCharacterAction.ApplyByMurder(lord2, null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                        if (ruler?.IsAlive == true && Hero.MainHero != null)
                                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                                Hero.MainHero, ruler, +10, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                        string bBothMsg = isWolfTemple
                                            ? $"A Wolf in Sheep's Clothing — You named {lord2Name}. The Inquisition accepted it without discussion. You bought standing in the chapter house, and you know exactly what that cost."
                                            : isWolfTribes
                                            ? $"A Wolf in Sheep's Clothing — You named {lord2Name} before the God-King. The divine fire accepted it without a second voice. You earned the God-King's regard. You know what was paid for it."
                                            : $"A Wolf in Sheep's Clothing — You named {lord2Name}. The court accepted it without debate. You bought goodwill, and you know exactly what that cost.";
                                        MBInformationManager.AddQuickInformation(new TextObject(bBothMsg));
                                    }
                                    else
                                    {
                                        var v = minorLords[_rng.Next(minorLords.Count)];
                                        try { KillCharacterAction.ApplyByMurder(v, null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                        string bChooseMsg = isWolfTemple
                                            ? $"A Wolf in Sheep's Clothing — The Inquisition chose their own answer. {v.Name} did not survive the night."
                                            : isWolfTribes
                                            ? $"A Wolf in Sheep's Clothing — The God-King chose. {v.Name} did not see the next dawn."
                                            : $"A Wolf in Sheep's Clothing — The court chose. {v.Name} did not survive the night.";
                                        MBInformationManager.AddQuickInformation(new TextObject(bChooseMsg));
                                    }
                                    break;
                                case "c":
                                {
                                    var v = minorLords[_rng.Next(minorLords.Count)];
                                    try { KillCharacterAction.ApplyByMurder(v, null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                    string cMsg = isWolfTemple
                                        ? $"A Wolf in Sheep's Clothing — You kept silent before the tribunal. The Inquisition chose its own answer. {v.Name} did not survive the chapter-room. Your hands stay clean. Their blood does not."
                                        : isWolfTribes
                                        ? $"A Wolf in Sheep's Clothing — You kept silent. The God-King decided. {v.Name} did not see the next sunrise. The accusation needed somewhere to land."
                                        : $"A Wolf in Sheep's Clothing — You said nothing. The court chose its own answer. {v.Name} did not survive the night. You kept your hands clean. Someone's blood was on them regardless.";
                                    MBInformationManager.AddQuickInformation(new TextObject(cMsg));
                                    break;
                                }
                                case "d":
                                {
                                    if (lord1.IsAlive && Hero.MainHero != null)
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                            Hero.MainHero, lord1, +100, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                    if (hasBoth && lord2.IsAlive && Hero.MainHero != null)
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                            Hero.MainHero, lord2, +100, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                                    if (_rng.NextDouble() < 0.33)
                                    {
                                        var traitor = minorLords[_rng.Next(minorLords.Count)];
                                        try { ColourLordRegistry.SetAshen(traitor, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                        try { AshenCitySystem.ApplyAshenPersonality(traitor); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                        try { ColourLordRegistry.SetMage(traitor, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                        try { AshenCitySystem.OnHeroSetAshen(traitor); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                        try { MageKnowledge.ApplyAshenAppearance(traitor); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                        if (ruler?.IsAlive == true && Hero.MainHero != null)
                                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                                Hero.MainHero, ruler, -10, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                        string dTrueMsg = isWolfTemple
                                            ? $"A Wolf in Sheep's Clothing — You vouched for their faith and were believed. Three days later, {traitor.Name} was found at the edge of the Ashen lands — grey-eyed and cold. The accusation was true. {rulerName} has not forgotten that you spoke for them."
                                            : isWolfTribes
                                            ? $"A Wolf in Sheep's Clothing — You spoke for them before the God-King's war-council and were heard. Three nights later, {traitor.Name} rode out of camp and did not return — found among the Ashen, grey-eyed and cold. The blood-pact was real. {rulerName} did not forget."
                                            : $"A Wolf in Sheep's Clothing — You spoke for their innocence and were believed. Three days later, {traitor.Name} vanished from their chambers, found among the Ashen — grey-eyed and cold. The accusation was true. {rulerName} did not forget that you vouched for them.";
                                        MBInformationManager.AddQuickInformation(new TextObject(dTrueMsg));
                                    }
                                    else
                                    {
                                        string dFalseMsg = isWolfTemple
                                            ? $"A Wolf in Sheep's Clothing — You vouched for their faith. The tribunal, reluctantly, accepted it. The accused remember your name. Whether the accusation had merit, neither you nor the Inquisition will ever be entirely certain."
                                            : isWolfTribes
                                            ? $"A Wolf in Sheep's Clothing — You spoke for them before the God-King's war-council. The council, grudgingly, accepted it. The accused remember. Whether the blood-pact was broken, the divine fire will not say."
                                            : $"A Wolf in Sheep's Clothing — You spoke for their innocence. The court, grudgingly, accepted it. The accused remember. Whether the accusation had merit, neither you nor anyone else will ever be entirely certain.";
                                        MBInformationManager.AddQuickInformation(new TextObject(dFalseMsg));
                                    }
                                    break;
                                }
                            }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }, null, "", false), false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
                            if (lord.Clan != null) AshenCitySystem.RegisterConqueredSettlement(castle, lord.Clan);
                            seizedNames.Add(castle.Name?.ToString() ?? "a castle");
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }

                // Surge Ashen lord party morale — push them into aggressive campaigning
                foreach (var party in MobileParty.All)
                {
                    if (!party.IsActive) continue;
                    var leader = party.LeaderHero;
                    if (leader != null && ColourLordRegistry.IsAshenLord(leader))
                    {
                        try { party.RecentEventsMorale += 50f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
