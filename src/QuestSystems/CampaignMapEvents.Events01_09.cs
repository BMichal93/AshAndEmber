// =============================================================================
// ASH AND EMBER — CampaignMapEvents.Events01_09.cs
// World events 1–9 (Plague, Withering, March, Long Night, Tide, …).
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
        // ── Event 1: Ashen Plague ─────────────────────────────────────────────
        // Wounds all healthy garrison troops in a random city or castle, then
        // spawns AshenPlagueSpawnCount Ashen Spawn parties near the settlement.
        private static void TryFireAshenPlague()
        {
            if (_rng.NextDouble() >= ChanceAshenPlague) return;
            if (!TryClaimWeeklySlot()) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Ashen Plague — the sanctuary's protective ward turns it aside. The grey sickness finds no purchase."));
                return;
            }
            try
            {
                // Require a settlement with a living garrison
                var candidates = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle)
                             && s.Town?.GarrisonParty?.MemberRoster?.TotalManCount > 0)
                    .ToList();
                if (candidates.Count == 0) return;

                var target = candidates[_rng.Next(candidates.Count)];
                var garrison = target.Town.GarrisonParty;

                // Wound all healthy (non-hero) garrison troops
                int totalWounded = 0;
                foreach (var entry in garrison.MemberRoster.GetTroopRoster().ToList())
                {
                    if (entry.Character.IsHero) continue;
                    int healthy = entry.Number - entry.WoundedNumber;
                    if (healthy <= 0) continue;
                    try
                    {
                        // AddToCounts(char, countDelta, isHero, woundedDelta)
                        garrison.MemberRoster.AddToCounts(entry.Character, 0, false, healthy);
                        totalWounded += healthy;
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                // Spawn Ashen Spawn parties near the afflicted settlement
                int spawned = 0;
                for (int i = 0; i < AshenPlagueSpawnCount; i++)
                {
                    var party = SpawnAshenSpawnParty(target.GetPosition2D, baseTroops: 12, minStrength: 0f);
                    if (party != null) spawned++;
                }

                if (totalWounded > 0 || spawned > 0)
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Ashen Plague — a grey sickness sweeps through the garrison of {target.Name}. " +
                        $"{totalWounded} soldier{(totalWounded != 1 ? "s" : "")} are struck down by fever and ash." +
                        (spawned > 0 ? $" {spawned} Ashen Spawn close on the afflicted settlement." : "")));
                RecordScar(target.StringId, 1);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 2: Great Withering ──────────────────────────────────────────
        // Coin-flip: either a random village loses 80% of its hearth, or a
        // random city loses 50% of its prosperity.
        private static void TryFireGreatWithering()
        {
            if (_rng.NextDouble() >= ChanceGreatWithering) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                if (_rng.Next(2) == 0)
                {
                    // Village: reduce hearth to 20% of current (= -80%)
                    var villages = Settlement.All
                        .Where(s => s.IsVillage && s.Village != null && s.Village.Hearth > 20f)
                        .ToList();
                    if (villages.Count == 0) return;

                    var target = villages[_rng.Next(villages.Count)];
                    float before = target.Village.Hearth;
                    target.Village.Hearth = Math.Max(10f, before * 0.20f);

                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Great Withering — the hearth-fires of {target.Name} gutter and die. " +
                        $"Hearth: {before:F0} → {target.Village.Hearth:F0}."));
                    RecordScar(target.StringId, 0);
                }
                else
                {
                    // City: reduce prosperity by 50%
                    var cities = Settlement.All
                        .Where(s => s.IsTown && s.Town != null && s.Town.Prosperity > 50f)
                        .ToList();
                    if (cities.Count == 0) return;

                    var target = cities[_rng.Next(cities.Count)];
                    float before = target.Town.Prosperity;
                    target.Town.Prosperity = Math.Max(10f, before * 0.50f);

                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Great Withering — something cold and old passes through {target.Name}. " +
                        $"Prosperity: {before:F0} → {target.Town.Prosperity:F0}."));
                    RecordScar(target.StringId, 0);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 3: Ashen March ──────────────────────────────────────────────
        // Spawns AshenMarchPartyCount Ashen Spawn parties (each with TotalStrength
        // ≥ MinAshenMarchStrength) spread across a random non-Ashen kingdom.
        private static void TryFireAshenMarch()
        {
            if (_rng.NextDouble() >= ChanceAshenMarch) return;
            if (!TryClaimWeeklySlot()) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Ashen March — the holy ward holds. The grey tide finds the roads blocked by something it cannot name."));
                return;
            }
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenKingdomId)
                    .ToList();
                if (kingdoms.Count == 0) return;

                var kingdom = kingdoms[_rng.Next(kingdoms.Count)];

                // Collect spawn anchors from the kingdom's towns and castles
                var anchors = Settlement.All
                    .Where(s => s.MapFaction == kingdom && (s.IsTown || s.IsCastle))
                    .Select(s => s.GetPosition2D)
                    .ToList();
                if (anchors.Count == 0) return;

                int spawned = 0;
                for (int i = 0; i < AshenMarchPartyCount; i++)
                {
                    // Distribute parties across settlement anchors
                    var anchor = anchors[i % anchors.Count];
                    var party = SpawnAshenSpawnParty(anchor, baseTroops: 20, minStrength: MinAshenMarchStrength);
                    if (party != null) spawned++;
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    spawned > 0
                        ? $"Ashen March — {spawned} Ashen Spawn descend upon {kingdom.Name}. The grey tide does not rest."
                        : $"Ashen March — the grey tide stirs near {kingdom.Name}, but finds no foothold today."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 4: Long Night ───────────────────────────────────────────────
        // Forces SpellEffects.GetCampaignLightLevel() to return Dark for
        // LongNightDuration days. Does not modify the campaign clock —
        // only the mod's internal light-level logic is affected.
        // Will not stack: skips if a Long Night is already in progress.
        private static void TryFireLongNight()
        {
            if (_longNightDaysRemaining > 0) return;
            if (_rng.NextDouble() >= ChanceLongNight) return;
            if (!TryClaimWeeklySlot()) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Long Night — the protective rites hold. The darkness stirs at the edge but cannot cross."));
                return;
            }

            _longNightDaysRemaining = LongNightDuration;

            // Spawn Ashen parties that emerge from the darkness
            int spawned = 0;
            try
            {
                var anchors = Settlement.All
                    .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId)
                    .Select(s => s.GetPosition2D)
                    .ToList();
                for (int i = 0; i < 3 && anchors.Count > 0; i++)
                {
                    var pos = anchors[_rng.Next(anchors.Count)];
                    var party = SpawnAshenSpawnParty(pos, 14, 50f);
                    if (party != null) spawned++;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            MBInformationManager.AddQuickInformation(new TextObject(
                $"Long Night — the sun does not rise. {LongNightDuration} days of unbroken darkness fall over Calradia. " +
                (spawned > 0 ? $"Ashen shapes pour from the shadow. {spawned} warbands take the roads." : "Something stirs in the dark.")));
        }

        // ── Event 5: Ashen Tide ───────────────────────────────────────────────
        // A random non-Ashen castle is claimed by a random Ashen lord via
        // ChangeOwnerOfSettlementAction.ApplyByDefault. The castle's original
        // clan loses the fief instantly — no siege required.
        private static void TryFireAshenTide()
        {
            if (_rng.NextDouble() >= ChanceAshenTide) return;
            if (!TryClaimWeeklySlot()) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Ashen Tide — the sanctuary's blessing turns the cold back. The castle holds."));
                return;
            }
            try
            {
                // Target: a castle not already under Ashen control or active siege
                var castles = Settlement.All
                    .Where(s => s.IsCastle
                             && !s.IsUnderSiege
                             && s.OwnerClan?.Kingdom?.StringId != AshenKingdomId)
                    .ToList();
                if (castles.Count == 0) return;

                // Claimant: any living Ashen lord who is not a prisoner
                var ashenLords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsDisabled && !h.IsPrisoner
                             && ColourLordRegistry.IsAshenLord(h))
                    .ToList();
                if (ashenLords.Count == 0) return;

                var castle = castles[_rng.Next(castles.Count)];
                var lord   = ashenLords[_rng.Next(ashenLords.Count)];

                ChangeOwnerOfSettlementAction.ApplyByDefault(lord, castle);
                StabiliseSettlement(castle);
                if (lord.Clan != null) AshenCitySystem.RegisterConqueredSettlement(castle, lord.Clan);

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Ashen Tide — {castle.Name} bends to the cold fire. " +
                    $"{lord.Name} claims it without a blade drawn."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 6: Fire Fades ───────────────────────────────────────────────
        // 2–4 non-Ashen lords aged 25–55 who are NOT clan leaders are killed.
        // Player hero is always spared. Their home settlement also loses
        // hearth/prosperity as their fire fades from that place too.
        //
        // Safety constraints:
        //   • !IsChild  — Bannerlord's ApplyByOldAge/succession code is not safe
        //                 for child heroes; IsChild is the engine's own flag.
        //   • not clan leader — killing a ruling-clan leader triggers complex
        //                 succession that can corrupt campaign state mid-event.
        //   • ApplyByMurder(null, false) — neutral "mystery death", no killer
        //                 assigned, no notification; avoids old-age succession path.
        private static void TryFireFireFades()
        {
            if (_rng.NextDouble() >= ChanceFireFades) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var candidates = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive
                             && !h.IsChild
                             && h.Age >= 25f && h.Age < 56f
                             && (h.Clan == null || h.Clan.Leader != h)
                             && h != Hero.MainHero
                             && !ColourLordRegistry.IsAshenLord(h))
                    .ToList();
                if (candidates.Count == 0) return;

                // Choose 2–4 victims deliberately rather than random 50% of a small pool
                int targetCount = Math.Min(candidates.Count, 2 + _rng.Next(3));
                var chosen = candidates.OrderBy(_ => _rng.Next()).Take(targetCount).ToList();

                var names = new List<string>();
                int killed = 0;
                foreach (var hero in chosen)
                {
                    try
                    {
                        // The fire fades from their home too
                        try
                        {
                            var home = hero.HomeSettlement ?? hero.Clan?.HomeSettlement;
                            if (home?.Village != null)
                                home.Village.Hearth = Math.Max(10f, home.Village.Hearth * 0.70f);
                            else if (home?.IsTown == true && home.Town != null)
                                home.Town.Prosperity = Math.Max(10f, home.Town.Prosperity * 0.85f);
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                        KillCharacterAction.ApplyByMurder(hero, null, false);
                        names.Add(hero.Name.ToString());
                        killed++;
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                if (killed > 0)
                {
                    string nameList = killed <= 3
                        ? string.Join(", ", names)
                        : $"{names[0]}, {names[1]}, and {killed - 2} others";
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Fire Fades — {nameList} did not wake this morning. " +
                        "Something ancient and cold moved through the realm in the dark hours. Their hearths grow cold behind them. " +
                        $"[{killed} lord{(killed != 1 ? "s" : "")} killed; home settlements lost hearth and prosperity.]"));
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 7: Darkened Roads ───────────────────────────────────────────
        // All caravans operating in a random non-Ashen kingdom are destroyed.
        // Also drains 15% prosperity from every town in the kingdom and spawns
        // 2 Ashen ambush parties to fill the vacuum. Skips if no caravans exist.
        // Uses DestroyPartyAction which is the clean campaign-system way to
        // remove a mobile party; the owning merchant heroes survive and may
        // rebuild their caravans later.
        private static void TryFireDarkenedRoads()
        {
            if (_rng.NextDouble() >= ChanceDarkenedRoads) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenKingdomId)
                    .ToList();
                if (kingdoms.Count == 0) return;

                var kingdom = kingdoms[_rng.Next(kingdoms.Count)];

                // Destroy all active caravans in the kingdom
                var caravans = MobileParty.All
                    .Where(p => p.IsActive && p.IsCaravan && p.MapFaction == kingdom)
                    .ToList();
                if (caravans.Count == 0) return; // no caravans = nothing dramatic to destroy

                int destroyed = 0;
                foreach (var caravan in caravans)
                {
                    try { DestroyPartyAction.Apply(caravan.Party, null); destroyed++; }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                // Trade collapse — every town in the kingdom loses 15% prosperity
                try
                {
                    foreach (var s in Settlement.All)
                    {
                        if (!s.IsTown || s.Town == null || s.MapFaction != kingdom) continue;
                        s.Town.Prosperity = Math.Max(10f, s.Town.Prosperity * 0.85f);
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // Ashen spawn move in to fill the vacuum
                var anchors = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle) && s.MapFaction == kingdom)
                    .Select(s => s.GetPosition2D)
                    .ToList();
                int spawned = 0;
                for (int i = 0; i < 2 && anchors.Count > 0; i++)
                {
                    var pos = anchors[_rng.Next(anchors.Count)];
                    var p = SpawnAshenSpawnParty(pos, 14, 50f);
                    if (p != null) spawned++;
                }

                string darkenedMsg = IsTempleFaction(kingdom)
                    ? $"Darkened Roads — {destroyed} supply train{(destroyed != 1 ? "s" : "")} and pilgrim convoy{(destroyed != 1 ? "s" : "")} vanish on the holy roads of {kingdom.Name}. The tithe-carts do not arrive. The temple gates close early. " + (spawned > 0 ? "Something grey moves in the silence left behind." : "The pilgrims' road is cold and still.")
                    : IsTribes(kingdom)
                    ? $"Darkened Roads — {destroyed} tribute-column{(destroyed != 1 ? "s" : "")} vanish on the steppe-roads of {kingdom.Name}. Tribute-riders do not return. The war-camp waits for gold and grain that will not arrive. " + (spawned > 0 ? "Ashen shapes follow the tribute-lanes east." : "The tribute roads are cold and empty.")
                    : $"Darkened Roads — {destroyed} caravan{(destroyed != 1 ? "s" : "")} vanish on the roads of {kingdom.Name}. Trade dies. Prosperity crumbles. " + (spawned > 0 ? "Ashen shapes move where merchants once walked." : "The roads fall silent and cold.");
                MBInformationManager.AddQuickInformation(new TextObject(darkenedMsg));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 8: Seeds of Betrayal ────────────────────────────────────────
        // A faction leader is murdered by their own court. The murderous clan is
        // expelled from the faction. Inspired by the Red Wedding.
        //
        // If the player's clan is tier 4+ in the affected kingdom, a choice
        // appears: back the conspirators or warn the court.
        //   Back: +50 with schemer clan, −100 with ruling clan.
        //   Warn: −100 with schemer clan, +20 with ruling clan, 33% plot stops.
        //
        // Safety constraints:
        //   • Excludes the player as faction leader (k.Leader != Hero.MainHero).
        //   • Requires the target faction to have ≥ 3 clans so one expulsion does
        //     not immediately collapse the realm.
        //   • Uses ApplyByMurder(leader, null, false) — neutral quiet death, engine
        //     handles succession automatically.
        //   • The expelled clan uses ApplyByLeaveKingdom — safe at any time.
        private static void TryFireSeedsOfBetrayal()
        {
            if (_rng.NextDouble() >= ChanceSeedsOfBetrayal) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var candidates = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && IsFactionConspireEligible(k)
                             && k.Leader != null
                             && k.Leader != Hero.MainHero
                             && k.Leader.IsAlive
                             && !k.Leader.IsPrisoner
                             && !k.Leader.IsChild
                             && k.Clans.Count(c => c != null && !c.IsEliminated) >= 3)
                    .ToList();
                if (candidates.Count == 0) return;

                var  kingdom      = candidates[_rng.Next(candidates.Count)];
                var  leader       = kingdom.Leader;
                Clan oldRulingClan = kingdom.RulingClan;

                var scapegoats = kingdom.Clans
                    .Where(c => c != null && !c.IsEliminated
                             && c != kingdom.RulingClan
                             && (c.Leader == null || c.Leader != Hero.MainHero)
                             && c.Heroes.Any(h => h.IsAlive && !h.IsChild))
                    .ToList();

                Clan expelled = scapegoats.Count > 0 ? scapegoats[_rng.Next(scapegoats.Count)] : null;

                string leaderName   = leader.Name?.ToString()          ?? "the lord";
                string kingdomName  = kingdom.Name?.ToString()         ?? "the realm";
                string expelledName = expelled?.Name?.ToString()       ?? "a noble house";
                string oldRulerName = oldRulingClan?.Name?.ToString()  ?? "the ruling clan";

                bool playerQualifies = PlayerIsQualifiedForEvent(kingdom)
                                    && Hero.MainHero?.Clan != expelled;

                if (playerQualifies)
                {
                    bool isTemple = IsTempleFaction(kingdom);
                    bool isTribes = IsTribes(kingdom);
                    string plotSetup = isTemple
                        ? $"A sacred oath has been broken in secret — {expelledName} has readied the chalice. {leaderName} will not survive tonight's Vespers. They are asking if you will stand with the Light they serve."
                        : isTribes
                        ? $"Riders crossed the war-camp boundary after dark — sworn brothers with blades beneath their cloaks. {expelledName} has broken the blood-pact. The Priest-King will not see tomorrow's sun rise. They have sent a rider to you."
                        : $"Word has reached you in the dark — {expelledName} is moving against {leaderName} of {kingdomName}. The wine is already prepared. They are asking if you stand with them.";
                    string backLabel = isTemple ? $"Stand with {expelledName}" : isTribes ? $"Ride with {expelledName}" : $"Back {expelledName}";
                    string warnLabel = isTemple ? $"Warn the High Templar" : isTribes ? $"Warn the Priest-King" : $"Warn {leaderName}";

                    string body = $"{plotSetup}\n\n"
                        + $"{backLabel}: +50 relations with {expelledName}, −100 with {oldRulerName}.\n"
                        + $"{warnLabel}: −100 with {expelledName}, +20 with {oldRulerName}, 33% chance the plot is stopped.";

                    InformationManager.ShowInquiry(new InquiryData(
                        "Seeds of Betrayal",
                        body,
                        true, true,
                        backLabel, warnLabel,
                        () =>
                        {
                            try
                            {
                                try { KillCharacterAction.ApplyByMurder(leader, null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                if (expelled != null && expelled.Kingdom == kingdom)
                                    try { ChangeKingdomAction.ApplyByLeaveKingdom(expelled, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                PlayerRelationWithClan(expelled, +50);
                                PlayerRelationWithClan(oldRulingClan, -100);
                                string msg = IsTempleFaction(kingdom)
                                    ? $"Seeds of Betrayal — {leaderName} of {kingdomName} did not survive Vespers. You played your part in silence. {expelledName} was gone before the bells rang — grateful and gone. {oldRulerName} will know who raised the chalice."
                                    : IsTribes(kingdom)
                                    ? $"Seeds of Betrayal — {leaderName} of {kingdomName} did not ride out at dawn. You were there when the blood-pact broke. {expelledName} scattered before the Vanguard could be raised — grateful and gone. {oldRulerName} will know whose banner rode beside them."
                                    : $"Seeds of Betrayal — {leaderName} of {kingdomName} did not survive the feast. You were part of it. {expelledName} fled before dawn — grateful, and gone. {oldRulerName} will know who held the blade.";
                                MBInformationManager.AddQuickInformation(new TextObject(msg));
                            }
                            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        },
                        () =>
                        {
                            try
                            {
                                PlayerRelationWithClan(expelled, -100);
                                if (_rng.NextDouble() < 0.33)
                                {
                                    PlayerRelationWithClan(oldRulingClan, +20);
                                    string msg = IsTempleFaction(kingdom)
                                        ? $"Seeds of Betrayal — your warning reached {leaderName} in time. The rite was altered; {expelledName}'s move collapsed before it could be made. {oldRulerName} owes you a debt the Temple does not speak of lightly. {expelledName} will not forget your name."
                                        : IsTribes(kingdom)
                                        ? $"Seeds of Betrayal — your rider reached the Priest-King in time. {expelledName}'s blood-pact was broken before it could be sealed. {oldRulerName} owes you a warrior's debt. {expelledName} will not forget."
                                        : $"Seeds of Betrayal — your warning reached {leaderName} in time. The feast was cancelled. {expelledName}'s plot collapsed in daylight. {oldRulerName} owes you something, whether or not they say so. {expelledName} will not forget your name.";
                                    MBInformationManager.AddQuickInformation(new TextObject(msg));
                                }
                                else
                                {
                                    try { KillCharacterAction.ApplyByMurder(leader, null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                    if (expelled != null && expelled.Kingdom == kingdom)
                                        try { ChangeKingdomAction.ApplyByLeaveKingdom(expelled, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                    PlayerRelationWithClan(oldRulingClan, +20);
                                    string msg = IsTempleFaction(kingdom)
                                        ? $"Seeds of Betrayal — {leaderName} of {kingdomName} did not survive despite your warning. {expelledName} moved before your word could reach the altar. {oldRulerName} remembers who tried. {expelledName} remembers too."
                                        : IsTribes(kingdom)
                                        ? $"Seeds of Betrayal — {leaderName} of {kingdomName} was already dead when your rider arrived. {expelledName} had struck before the war-tent opened for the day. {oldRulerName} remembers who sent the warning. {expelledName} remembers too."
                                        : $"Seeds of Betrayal — {leaderName} of {kingdomName} did not survive despite your warning. {expelledName} moved before the word could spread. {oldRulerName} remembers who tried. {expelledName} remembers too.";
                                    MBInformationManager.AddQuickInformation(new TextObject(msg));
                                }
                            }
                            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                    ), true);
                }
                else
                {
                    try { KillCharacterAction.ApplyByMurder(leader, null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    if (expelled != null && expelled.Kingdom == kingdom)
                        try { ChangeKingdomAction.ApplyByLeaveKingdom(expelled, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    string worldMsg = IsTempleFaction(kingdom)
                        ? $"Seeds of Betrayal — {leaderName} of {kingdomName} was found cold before morning prayer. The chalice had been prepared in secret. {expelledName} vanished before the bells, their insignia stripped from the chapel wall. The covenant endures — but something under it has shifted."
                        : IsTribes(kingdom)
                        ? $"Seeds of Betrayal — {leaderName} of {kingdomName} did not ride out at dawn. The blood-pact was broken in the dark. {expelledName} rode east before the war-camp woke, their standard abandoned in the dust. The divine fire passes by blood. The Priest-King's succession has already begun."
                        : $"Seeds of Betrayal — {leaderName} of {kingdomName} did not survive the feast. The wine was poisoned. The doors were barred. {expelledName} fled before dawn, their banners cut from the hall. Someone will sit the seat they left empty. Someone always does.";
                    MBInformationManager.AddQuickInformation(new TextObject(worldMsg));
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event 9: Broken Will ─────────────────────────────────────────────
        // A faction leader looks into the cold fire long enough that it begins
        // to look back. That faction declares war on every other kingdom —
        // it becomes as isolated and hostile as the Ashen themselves.
        //
        // Fires at most BrokenWillMaxFires times per campaign, never before
        // campaign day BrokenWillEarliestDay. Uses a re-entrancy guard.
        //
        // Safety constraints:
        //   • Skips the player's faction.
        //   • Skips already-broken kingdoms.
        //   • Checks !IsAtWarWith before declaring to avoid duplicate war actions.
        private static bool _declaringBrokenWill    = false;
        private static bool _templeFounded          = false;
        private static bool _pendingTempleJoin      = false;

        internal static bool TempleFounded => _templeFounded;
        private static int  _protectedDaysRemaining = 0;
        private static bool _ashenGambitFired       = false;
        private static bool _deadMarchFirstFired   = false;
        private static int  _deadMarchLastFiredDay = 0;
        private static bool _undyingHostFired      = false;
        private static int  _campaignStartDay       = -1;
        // Event throttle: at most one event fires per weekly tick, and no event fires
        // until EventCooldownDays have passed since the last one.
        private static bool _weeklySlotFilled    = false;
        private static bool _warSlotFilled       = false; // separate slot for war-triggering events
        private static int  _lastEventElapsedDay = -EventCooldownDays;
        // Independent of the slot system — tracks when we last ensured an inter-faction war exists.
        private static int  _lastConflictSeedDay = 0;

    }
}
