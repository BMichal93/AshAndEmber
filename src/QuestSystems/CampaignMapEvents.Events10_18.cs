// =============================================================================
// ASH AND EMBER — CampaignMapEvents.Events10_18.cs
// World events 10–18 (Long March, Whispers, Tyranny, seasons, …).
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
        // ── Event 10: The Long March ─────────────────────────────────────────
        // Four massive Ashen warbands (100+ troops each) materialise within
        // one of the western, southern, eastern, or northern realms:
        // Vlandia, Aserai, Khuzait Khanate, or Sturgia.
        // This is a deliberate targeted invasion, not a scatter event.
        //
        // Safety constraints:
        //   • Skips eliminated kingdoms and kingdoms with no settlement anchors.
        //   • SpawnAshenSpawnParty wraps everything in try/catch.
        private static void TryFireTheLongMarch()
        {
            if (_rng.NextDouble() >= ChanceTheLongMarch) return;
            if (!TryClaimWeeklySlot()) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "The Long March — the protective rites form a wall the grey tide cannot cross. The columns turn back."));
                return;
            }
            try
            {
                // Shuffle eligible target kingdoms so selection is not always Vlandia
                var targets = LongMarchTargets
                    .Select(id => Kingdom.All.FirstOrDefault(k => k.StringId == id && !k.IsEliminated))
                    .Where(k => k != null)
                    .OrderBy(_ => _rng.Next())
                    .ToList();
                if (targets.Count == 0) return;

                var kingdom = targets[0];
                var anchors = Settlement.All
                    .Where(s => s.MapFaction == kingdom && (s.IsTown || s.IsCastle))
                    .Select(s => s.GetPosition2D)
                    .ToList();
                if (anchors.Count == 0) return;

                int spawned = 0;
                for (int i = 0; i < 4; i++)
                {
                    var anchor = anchors[_rng.Next(anchors.Count)];
                    // 10 × 10 = 100 base troops; minStrength 80 tops up if needed
                    var party = SpawnAshenSpawnParty(anchor, baseTroops: 10, minStrength: 80f);
                    if (party != null) spawned++;
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    spawned > 0
                        ? $"The Long March — {spawned} great columns of Ashen Spawn set foot in {kingdom.Name}. " +
                          $"These are not raiders. They do not break and scatter. They march."
                        : $"The Long March — something moved through {kingdom.Name}. The roads show it. The villages show it. But whatever passed has gone."));
            }
            catch { }
        }

        // ── Event 11: Whispers from the Ash ──────────────────────────────────
        // 1–3 mage lords hear the cold calling them by name. They abandon their
        // factions and join the Ashen, gaining Ashen lord status, personality,
        // and the cold fire's mark.
        //
        // Safety constraints:
        //   • Never converts the player hero.
        //   • Only converts clan leaders of clans with at least 2 living heroes,
        //     so the source clan is never left headless mid-event.
        //   • Requires the source kingdom to retain at least 2 clans after the
        //     defection, to prevent immediate faction extinction.
        //   • ColourLordRegistry.SetAshen + OnHeroSetAshen handle clan movement
        //     and kingdom placement safely.
        private static void TryFireWhispersFromTheAsh()
        {
            if (_rng.NextDouble() >= ChanceWhispers) return;
            if (!TryClaimWeeklySlot()) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Whispers from the Ash — the holy ward silences the call. The mages hear nothing but flame."));
                return;
            }
            try
            {
                // Eligible: mage lords, clan leaders, 2+ alive members in clan, kingdom stays viable
                var candidates = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsChild && !h.IsPrisoner
                             && h != Hero.MainHero
                             && ColourLordRegistry.IsColourLord(h)
                             && !ColourLordRegistry.IsAshenLord(h)
                             && h.Clan != null && h.Clan.Leader == h   // clan leader only
                             && h.Clan.Heroes.Count(x => x.IsAlive && !x.IsChild) >= 2
                             && h.Clan.Kingdom != null
                             && h.Clan.Kingdom.StringId != AshenKingdomId
                             && h.Clan.Kingdom != Hero.MainHero?.Clan?.Kingdom
                             && h.Clan.Kingdom.Clans.Count(c => c != null && !c.IsEliminated) >= 3)
                    .ToList();
                if (candidates.Count == 0) return;

                int count = Math.Min(candidates.Count, 1 + _rng.Next(3)); // 1–3
                var chosen = candidates.OrderBy(_ => _rng.Next()).Take(count).ToList();

                var names = new List<string>();
                foreach (var hero in chosen)
                {
                    try
                    {
                        names.Add(hero.Name?.ToString() ?? "a lord");
                        try { ColourLordRegistry.SetAshen(hero, true); }              catch { }
                        try { AshenCitySystem.ApplyAshenPersonality(hero); }          catch { }
                        try { ColourLordRegistry.SetMage(hero, true); }               catch { }
                        try { AshenCitySystem.OnHeroSetAshen(hero); }                 catch { }
                        try { MageKnowledge.ApplyAshenAppearance(hero); }             catch { }
                    }
                    catch { }
                }

                if (names.Count == 0) return;
                string nameStr = names.Count == 1
                    ? names[0]
                    : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Whispers from the Ash — {nameStr} heard something in the fire " +
                    $"that they cannot explain and cannot forget. They have gone north. " +
                    $"Their banners are cold. Their eyes are grey. " +
                    $"Their former lords received only a letter — unsigned, unaddressed, already cold. " +
                    $"[{names.Count} mage lord{(names.Count != 1 ? "s" : "")} defected to the Ashen.]"));
            }
            catch { }
        }

        // ── Event 12: Tyranny ────────────────────────────────────────────────
        // A faction leader's paranoia turns lethal. All tier-5 and tier-6 clan
        // heads within their realm are executed in a single night. The ruling
        // clan is bankrupted — influence drained to zero. One of the executed
        // clans defects before the blade falls.
        //
        // If the player's clan is tier 4+ in the affected kingdom, a choice
        // appears: support the tyrant or defy them.
        //   Support: +100 with tyrant, −50 with all condemned clans.
        //   Defy: 33% chance the player is also executed (game over path).
        //
        // Safety constraints:
        //   • Never has the player as tyrant (k.Leader != Hero.MainHero).
        //   • Only kills clan leaders whose clan has ≥ 2 living members.
        //   • Requires at least one tier-5/6 non-ruling clan to exist.
        //   • Defecting clan uses ApplyByLeaveKingdom (safe outside ClanChangedKingdom).
        //   • Ruling clan influence floor is 0f (never negative).
        private static void TryFireTyranny()
        {
            if (_rng.NextDouble() >= ChanceTyranny) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && IsFactionConspireEligible(k)
                             && k.Leader != null && k.Leader != Hero.MainHero
                             && k.RulingClan != null
                             && k.Clans.Any(c => c != null && !c.IsEliminated
                                              && c != k.RulingClan
                                              && c.Tier >= 5
                                              && c.Leader != null
                                              && c.Leader.IsAlive && !c.Leader.IsChild
                                              && c.Heroes.Count(h => h.IsAlive && !h.IsChild) >= 2))
                    .ToList();
                if (kingdoms.Count == 0) return;

                var kingdom  = kingdoms[_rng.Next(kingdoms.Count)];
                var tyrant   = kingdom.Leader;
                var ruling   = kingdom.RulingClan;

                var condemned = kingdom.Clans
                    .Where(c => c != null && !c.IsEliminated
                             && c != ruling
                             && c.Tier >= 5
                             && c.Leader != null
                             && c.Leader.IsAlive && !c.Leader.IsChild
                             && (c.Leader == null || c.Leader != Hero.MainHero)
                             && c.Heroes.Count(h => h.IsAlive && !h.IsChild) >= 2)
                    .ToList();
                if (condemned.Count == 0) return;

                Clan defector = condemned[_rng.Next(condemned.Count)];

                string tyrantName   = tyrant?.Name?.ToString()   ?? "the lord";
                string kingdomName  = kingdom.Name?.ToString()   ?? "the realm";
                string defectorName = defector?.Name?.ToString() ?? "one house";

                bool playerQualifies = PlayerIsQualifiedForEvent(kingdom);

                if (playerQualifies)
                {
                    bool isTemple = IsTempleFaction(kingdom);
                    bool isTribes = IsTribes(kingdom);
                    var executedClans = condemned.Where(c => c != defector).ToList();
                    string condemnedNames = executedClans.Count == 0 ? "the high lords"
                        : executedClans.Count <= 2
                            ? string.Join(" and ", executedClans.Select(c => c.Name?.ToString() ?? "a house"))
                            : (executedClans[0].Name?.ToString() ?? "a house") + " and others";

                    string bodySetup = isTemple
                        ? $"{tyrantName} of {kingdomName} has called the high lords before the altar — and means to condemn them as apostates and heretics. {condemnedNames} are on the list. {defectorName} has already left the temple before dawn."
                        : isTribes
                        ? $"{tyrantName} of {kingdomName} has summoned the clan chiefs to the great war-tent — and means not to let them leave standing. {condemnedNames} are marked. {defectorName} rode out before the summons arrived."
                        : $"{tyrantName} of {kingdomName} has called the high lords to feast — and means to keep them there permanently. {condemnedNames} are condemned. {defectorName} has already fled.";
                    string supportLabel = isTemple ? $"Stand with the Inquisition" : isTribes ? $"Ride with {tyrantName}" : $"Support {tyrantName}";

                    string body = $"{bodySetup}\n\n"
                        + $"{supportLabel}: +100 relations with {tyrantName}, −50 with all condemned clans.\n"
                        + $"Defy the tyrant: 33% chance of being added to the execution list.";

                    InformationManager.ShowInquiry(new InquiryData(
                        "Tyranny",
                        body,
                        true, true,
                        supportLabel,
                        "Defy the tyrant",
                        () =>
                        {
                            try
                            {
                                try { ChangeKingdomAction.ApplyByLeaveKingdom(defector, false); } catch { }
                                var executed = new List<string>();
                                foreach (var clan in condemned)
                                {
                                    if (clan == defector) continue;
                                    if (clan.Leader == null || !clan.Leader.IsAlive) continue;
                                    try
                                    {
                                        executed.Add(clan.Leader.Name?.ToString() ?? "a lord");
                                        KillCharacterAction.ApplyByMurder(clan.Leader, null, false);
                                    }
                                    catch { }
                                }
                                try { ruling.Influence = 0f; } catch { }

                                if (tyrant != null && tyrant.IsAlive)
                                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                        Hero.MainHero, tyrant, +100, false); } catch { }
                                foreach (var clan in condemned)
                                    PlayerRelationWithClan(clan, -50);

                                string exList = executed.Count == 0 ? "none"
                                    : executed.Count <= 3 ? string.Join(", ", executed)
                                    : $"{executed[0]}, {executed[1]}, and {executed.Count - 2} others";
                                string suppMsg = IsTempleFaction(kingdom)
                                    ? $"Tyranny — you stood with {tyrantName}'s judgment. {exList} were condemned as heretics before morning prayer. {defectorName} read the writ and chose exile. The Inquisition's gratitude is a quiet thing. The hatred of the condemned will outlast them."
                                    : IsTribes(kingdom)
                                    ? $"Tyranny — you rode with {tyrantName}. {exList} did not leave the war-tent. {defectorName} read the signs and rode east before it happened. The Khan's gratitude is worth having. The blood-debt of the condemned will not be forgotten."
                                    : $"Tyranny — you stood with {tyrantName}. {exList} did not leave the feast. {defectorName} read the invitation and chose the road. The tyrant's gratitude is real. The hatred of the condemned will outlast them.";
                                MBInformationManager.AddQuickInformation(new TextObject(suppMsg));
                            }
                            catch { }
                        },
                        () =>
                        {
                            try
                            {
                                try { ChangeKingdomAction.ApplyByLeaveKingdom(defector, false); } catch { }
                                var executed = new List<string>();
                                foreach (var clan in condemned)
                                {
                                    if (clan == defector) continue;
                                    if (clan.Leader == null || !clan.Leader.IsAlive) continue;
                                    try
                                    {
                                        executed.Add(clan.Leader.Name?.ToString() ?? "a lord");
                                        KillCharacterAction.ApplyByMurder(clan.Leader, null, false);
                                    }
                                    catch { }
                                }
                                try { ruling.Influence = 0f; } catch { }

                                if (_rng.NextDouble() < 0.33)
                                {
                                    string deathMsg = IsTempleFaction(kingdom)
                                        ? $"Tyranny — you defied {tyrantName}'s Inquisition. They added your name to the list of apostates. The temple guards came before dawn."
                                        : IsTribes(kingdom)
                                        ? $"Tyranny — you defied {tyrantName} in the war-tent. They added your name before the wind could carry a warning. The Khan's riders found you."
                                        : $"Tyranny — you defied {tyrantName}. They added your name to the list. The blade found you before dawn.";
                                    MBInformationManager.AddQuickInformation(new TextObject(deathMsg));
                                    try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, false); } catch { }
                                }
                                else
                                {
                                    string exList = executed.Count == 0 ? "none"
                                        : executed.Count <= 3 ? string.Join(", ", executed)
                                        : $"{executed[0]}, {executed[1]}, and {executed.Count - 2} others";
                                    string defMsg = IsTempleFaction(kingdom)
                                        ? $"Tyranny — you defied {tyrantName}'s judgment. The Inquisition moved anyway — {exList} before morning prayer. Your defiance was noted. For now, the writ did not carry your name."
                                        : IsTribes(kingdom)
                                        ? $"Tyranny — you defied {tyrantName} before the clans. The purge happened anyway — {exList} before the next dawn. Your courage was seen. The blade did not find you this time."
                                        : $"Tyranny — you defied {tyrantName}. The purge happened anyway — {exList} before dawn. Your defiance was noted. For now, the blade did not find you.";
                                    MBInformationManager.AddQuickInformation(new TextObject(defMsg));
                                }
                            }
                            catch { }
                        }
                    ), true);
                }
                else
                {
                    try { ChangeKingdomAction.ApplyByLeaveKingdom(defector, false); } catch { }
                    var executed = new List<string>();
                    foreach (var clan in condemned)
                    {
                        if (clan == defector) continue;
                        if (clan.Leader == null || !clan.Leader.IsAlive) continue;
                        try
                        {
                            executed.Add(clan.Leader.Name?.ToString() ?? "a lord");
                            KillCharacterAction.ApplyByMurder(clan.Leader, null, false);
                        }
                        catch { }
                    }
                    try { ruling.Influence = 0f; } catch { }

                    string exList2 = executed.Count == 0 ? "none"
                        : executed.Count <= 3 ? string.Join(", ", executed)
                        : $"{executed[0]}, {executed[1]}, and {executed.Count - 2} others";
                    string worldMsg = IsTempleFaction(kingdom)
                        ? $"Tyranny — {tyrantName} of {kingdomName} convened the holy tribunal and did not let the accused speak. {exList2} — condemned as apostates before morning prayer. {defectorName} read the writ and chose exile before it was served. The temple is quieter now, and colder."
                        : IsTribes(kingdom)
                        ? $"Tyranny — {tyrantName} of {kingdomName} called the clan chiefs into the great war-tent and would not let them leave standing. {exList2} — gone before the next dawn. {defectorName} read the signs and rode east. The steppe absorbs these things. It does not forget them."
                        : $"Tyranny — {tyrantName} of {kingdomName} called their great lords to feast and did not let them leave. {exList2} — dead before dawn. {defectorName} read the invitation and chose the road instead. The throne room is emptier now. The ruling clan's influence is the price of what happened here.";
                    MBInformationManager.AddQuickInformation(new TextObject(worldMsg));
                }
            }
            catch { }
        }

        // ── Event 13: Stolen Heirloom ─────────────────────────────────────────
        // A rival clan within a faction seizes power — the faction leader changes
        // to the head of a different clan inside the same kingdom.
        //
        // If the player's clan is tier 4+ in the affected kingdom (and is neither
        // the old ruler nor the usurper), a choice appears.
        //   Back the usurper: +50 with usurper, −100 with old ruling clan.
        //   Stand with old rulers: −100 with usurper, +20 with old ruling clan,
        //     33% chance the coup fails.
        //
        // Uses ChangeRulingClanAction.Apply(newClan) which is the engine's own
        // ruling-clan transition. Falls back to a no-op if the action throws.
        //
        // Safety constraints:
        //   • Excludes the Ashen kingdom.
        //   • Requires ≥ 2 non-eliminated clans in the kingdom.
        //   • The new ruling clan must already be a member of the kingdom.
        //   • Wrapped entirely in try/catch; a failure is silent and harmless.
        private static void TryFireStolenHeirloom()
        {
            if (_rng.NextDouble() >= ChanceStolenHeirloom) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && IsFactionConspireEligible(k)
                             && k.RulingClan != null
                             && k.Clans.Count(c => c != null && !c.IsEliminated) >= 2)
                    .ToList();
                if (kingdoms.Count == 0) return;

                var  kingdom  = kingdoms[_rng.Next(kingdoms.Count)];
                Clan oldRuler = kingdom.RulingClan;

                var rivals = kingdom.Clans
                    .Where(c => c != null && !c.IsEliminated
                             && c != oldRuler
                             && c.Leader != null
                             && c.Leader.IsAlive
                             && !c.Leader.IsChild
                             && c.Leader != Hero.MainHero)
                    .ToList();
                if (rivals.Count == 0) return;

                var    usurper = rivals[_rng.Next(rivals.Count)];
                string oldName  = oldRuler?.Name?.ToString() ?? "the old house";
                string newName  = usurper.Name?.ToString()   ?? "a rival house";
                string kingName = kingdom.Name?.ToString()   ?? "the realm";

                bool playerQualifies = PlayerIsQualifiedForEvent(kingdom)
                                    && Hero.MainHero?.Clan != oldRuler
                                    && Hero.MainHero?.Clan != usurper;

                if (playerQualifies)
                {
                    bool isTemple = IsTempleFaction(kingdom);
                    bool isTribes = IsTribes(kingdom);
                    string sealLabel = isTemple ? "the Covenant Seal" : isTribes ? "the Khan's blood-claim" : $"the seal of {kingName}";
                    string moveDesc = isTemple
                        ? $"{newName} is moving to claim {sealLabel} of {kingName} from {oldName}. The transfer of the Covenant Seal is not done in open daylight. Word has reached you — they are asking where your allegiance rests."
                        : isTribes
                        ? $"{newName} is moving to claim {sealLabel} of {kingName} over {oldName}. The clans watch which banner rises at dawn. Your name has weight in the east."
                        : $"{newName} is moving to seize the seal of {kingName} from {oldName}. Word has reached you — and they are waiting to see which way your clan stands.";

                    string body = $"{moveDesc}\n\n"
                        + $"Back the seizure: +50 relations with {newName}, −100 with {oldName}.\n"
                        + $"Stand with {oldName}: −100 with {newName}, +20 with {oldName}, 33% chance the coup fails.";

                    InformationManager.ShowInquiry(new InquiryData(
                        "Stolen Heirloom",
                        body,
                        true, true,
                        $"Back {newName}",
                        $"Stand with {oldName}",
                        () =>
                        {
                            try
                            {
                                try { ChangeRulingClanAction.Apply(kingdom, usurper); } catch { }
                                PlayerRelationWithClan(usurper,  +50);
                                PlayerRelationWithClan(oldRuler, -100);
                                string succMsg = IsTempleFaction(kingdom)
                                    ? $"Stolen Heirloom — you backed {newName}'s move. The Covenant Seal of {kingName} passed to new hands before morning prayer. {oldName} knows exactly where you stood."
                                    : IsTribes(kingdom)
                                    ? $"Stolen Heirloom — you rode with {newName}. The Khan's claim of {kingName} passed at dawn. {oldName} knows which banner you raised."
                                    : $"Stolen Heirloom — you backed {newName}'s move. The seal of {kingName} changed hands. {oldName} knows exactly where you stood.";
                                MBInformationManager.AddQuickInformation(new TextObject(succMsg));
                            }
                            catch { }
                        },
                        () =>
                        {
                            try
                            {
                                PlayerRelationWithClan(usurper,  -100);
                                PlayerRelationWithClan(oldRuler, +20);
                                if (_rng.NextDouble() < 0.33)
                                {
                                    string stopMsg = IsTempleFaction(kingdom)
                                        ? $"Stolen Heirloom — your opposition reached the right ears. {newName}'s claim to the Covenant Seal of {kingName} collapsed before it was presented. {oldName} holds it still. {newName} has not forgotten your part in this."
                                        : IsTribes(kingdom)
                                        ? $"Stolen Heirloom — your riders reached the clan elders in time. {newName}'s claim to {kingName} was rejected by the council. The blood-right stays with {oldName}. {newName} will not forget."
                                        : $"Stolen Heirloom — your opposition was enough. {newName}'s move collapsed before it landed. {kingName} stays in {oldName}'s hands. {newName} has not forgotten your part in it.";
                                    MBInformationManager.AddQuickInformation(new TextObject(stopMsg));
                                }
                                else
                                {
                                    try { ChangeRulingClanAction.Apply(kingdom, usurper); } catch { }
                                    string failMsg = IsTempleFaction(kingdom)
                                        ? $"Stolen Heirloom — despite your opposition, {newName} pressed the claim. The Covenant Seal of {kingName} is in their hands now. {oldName} is grateful, though without power. {newName} will not forget your name."
                                        : IsTribes(kingdom)
                                        ? $"Stolen Heirloom — despite your opposition, {newName} rode ahead. The blood-claim of {kingName} is theirs now. {oldName} remembers who stood with them. {newName} will not forget your name."
                                        : $"Stolen Heirloom — despite your opposition, {newName} pressed ahead. The seal of {kingName} is in their hands now. {oldName} is grateful, though powerless. {newName} will not forget your name.";
                                    MBInformationManager.AddQuickInformation(new TextObject(failMsg));
                                }
                            }
                            catch { }
                        }
                    ), true);
                }
                else
                {
                    try { ChangeRulingClanAction.Apply(kingdom, usurper); } catch { }
                    string worldMsg = IsTempleFaction(kingdom)
                        ? $"Stolen Heirloom — the Covenant Seal of {kingName} passed to new hands in the dark. {newName} holds it now. {oldName} held it at dusk. The transfer was quiet. The Temple's faithful will not know who arranged it for some time."
                        : IsTribes(kingdom)
                        ? $"Stolen Heirloom — the Khan's blood-claim of {kingName} changed at dawn without a battle. {newName} raised their banner where {oldName}'s had flown. The clans watched. The steppe does not care who flies the standard — only who keeps it."
                        : $"Stolen Heirloom — the signet ring of {kingName} changed hands in the night. {newName} holds the seal now. {oldName} held it at sundown. No swords were drawn. That may be the most frightening part.";
                    MBInformationManager.AddQuickInformation(new TextObject(worldMsg));
                }
            }
            catch { }
        }

        // ── Event 14: Iron Winter ─────────────────────────────────────────────
        // The cold bites deep into the north. Villages lose half their hearth;
        // towns lose half their prosperity and food stocks. Fires only in winter.
        //
        // "North" = Sturgia and the Northern Empire (see NorthernKingdoms array).
        //
        // Safety: all property writes are clamped to 10 (never zeroed). Wrapped
        // in try/catch per settlement so one bad settlement can't abort the rest.
        private static void TryFireIronWinter()
        {
            if (!IsWinter()) return;
            if (_rng.NextDouble() >= ChanceIronWinter) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                // Pick one random northern kingdom rather than devastating all of them at once
                var northKingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && System.Array.IndexOf(NorthernKingdoms, k.StringId) >= 0)
                    .ToList();
                if (northKingdoms.Count == 0) return;
                var kingdom = northKingdoms[_rng.Next(northKingdoms.Count)];

                int villages = 0, towns = 0;
                foreach (var s in Settlement.All)
                {
                    if (s == null || s.MapFaction != kingdom) continue;
                    if (s.IsVillage && s.Village != null)
                    {
                        try { s.Village.Hearth = Math.Max(10f, s.Village.Hearth * 0.5f); villages++; } catch { }
                    }
                    else if (s.IsTown && s.Town != null)
                    {
                        try
                        {
                            s.Town.Prosperity = Math.Max(10f, s.Town.Prosperity * 0.5f);
                            s.Town.FoodStocks = Math.Max(10f, s.Town.FoodStocks * 0.5f);
                            towns++;
                        }
                        catch { }
                    }
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Iron Winter — the cold descended on {kingdom.Name} and refused to leave. " +
                    $"{villages} village{(villages != 1 ? "s" : "")} cannot keep their fires lit. " +
                    $"{towns} cit{(towns != 1 ? "ies" : "y")} ha{(towns != 1 ? "ve" : "s")} halved their stores. " +
                    $"The roads are quiet in the wrong way."));
            }
            catch { }
        }

        // ── Event 15: Scorching Sun ───────────────────────────────────────────
        // The desert bakes. Villages in the south lose half their hearth;
        // towns lose half their prosperity and food stocks. Fires only in summer.
        //
        // "Desert" = Aserai and the Southern Empire (see DesertKingdoms array).
        //
        // Safety: same per-settlement try/catch and floor clamping as Iron Winter.
        private static void TryFireScorchingSun()
        {
            if (!IsSummer()) return;
            if (_rng.NextDouble() >= ChanceScorchingSun) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                // Pick one random desert kingdom rather than scorching all of them at once
                var desertKingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && System.Array.IndexOf(DesertKingdoms, k.StringId) >= 0)
                    .ToList();
                if (desertKingdoms.Count == 0) return;
                var kingdom = desertKingdoms[_rng.Next(desertKingdoms.Count)];

                int villages = 0, towns = 0;
                foreach (var s in Settlement.All)
                {
                    if (s == null || s.MapFaction != kingdom) continue;
                    if (s.IsVillage && s.Village != null)
                    {
                        try { s.Village.Hearth = Math.Max(10f, s.Village.Hearth * 0.5f); villages++; } catch { }
                    }
                    else if (s.IsTown && s.Town != null)
                    {
                        try
                        {
                            s.Town.Prosperity = Math.Max(10f, s.Town.Prosperity * 0.5f);
                            s.Town.FoodStocks = Math.Max(10f, s.Town.FoodStocks * 0.5f);
                            towns++;
                        }
                        catch { }
                    }
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Scorching Sun — the sky above {kingdom.Name} has been white with heat for three weeks. " +
                    $"The wells in {villages} village{(villages != 1 ? "s" : "")} are low or dry. " +
                    $"{towns} cit{(towns != 1 ? "ies" : "y")} ha{(towns != 1 ? "ve" : "s")} rationed their stores. " +
                    $"The land remembers."));
            }
            catch { }
        }

        // ── Event 16: The First Green ─────────────────────────────────────────
        // Spring only. The world stirs back to life — flowers push through the
        // soil, rivers run clear. Ash has not yet smothered the season.
        // All active lord parties outside the Ashen kingdom receive a small
        // morale boost (+10 RecentEventsMorale).
        private static void TryFireFirstGreen()
        {
            if (!IsSpring()) return;
            if (_rng.NextDouble() >= ChanceFirstGreen) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                int boosted = 0;
                foreach (var party in MobileParty.All.ToList())
                {
                    if (party == null || !party.IsActive || !party.IsLordParty) continue;
                    if (party.MapFaction == null || party.MapFaction.IsEliminated) continue;
                    if (party.MapFaction.StringId == AshenKingdomId) continue;
                    try { party.RecentEventsMorale += 10f; boosted++; } catch { }
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"The First Green — flowers push through the soil. Rivers run clear. " +
                    $"For a week the ash feels further away than it is. " +
                    $"Across {boosted} warband{(boosted != 1 ? "s" : "")}, soldiers lift their eyes from the grey horizon. " +
                    $"The world has not forgotten how to be alive."));
            }
            catch { }
        }

        // ── Event 17: The Amber Harvest ───────────────────────────────────────
        // Autumn only. The crops gave what they promised before the cold comes.
        // All villages not under the Ashen banner gain +20 hearth as granaries
        // fill and hearths are stocked for winter.
        private static void TryFireAmberHarvest()
        {
            if (!IsAutumn()) return;
            if (_rng.NextDouble() >= ChanceAmberHarvest) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                int villages = 0;
                foreach (var s in Settlement.All)
                {
                    if (s == null || !s.IsVillage || s.Village == null) continue;
                    if (s.MapFaction == null || s.MapFaction.StringId == AshenKingdomId) continue;
                    try { s.Village.Hearth += 20f; villages++; } catch { }
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"The Amber Harvest — the fields gave what they promised. " +
                    $"Across {villages} village{(villages != 1 ? "s" : "")}, granaries are full and hearths burn warm. " +
                    $"There is laughter again, and the smell of fresh bread on the autumn air. " +
                    $"Let the cold come."));
            }
            catch { }
        }

        // ── Event 18: Game of Thrones ─────────────────────────────────────────
        // Triggered 2 days after a faction leader dies (5% chance, 4+ clan kingdom).
        // All non-ruling, non-player clans leave the kingdom and become independent.
        // They keep their fiefs — the kingdom fractures.
        //
        // Safety constraints:
        //   • Never fires for the Ashen (excluded at trigger).
        //   • Never fires for the player's faction.
        //   • Never ejects the current ruling clan or the player's clan.
        //   • Each ejection in its own try/catch; a bad clan can't abort others.
        //   • Requires kingdom still exists and is not eliminated by the time of firing.
        //   • Uses ChangeKingdomAction.ApplyByLeaveKingdom — the only safe API path.
        private static void FireGameOfThrones(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated) return;
            if (kingdom.StringId == AshenKingdomId) return;

            var ruling      = kingdom.RulingClan;
            var playerClan  = Hero.MainHero?.Clan;
            string kingName = kingdom.Name?.ToString() ?? "the realm";
            string newLeader= kingdom.Leader?.Name?.ToString() ?? "a new lord";

            var toEject = kingdom.Clans
                .Where(c => c != null && !c.IsEliminated
                         && c != ruling
                         && c != playerClan
                         && c.Heroes.Any(h => h.IsAlive && !h.IsChild))
                .ToList();

            if (toEject.Count == 0) return;

            var expelled = new List<string>();
            foreach (var clan in toEject)
            {
                try
                {
                    expelled.Add(clan.Name?.ToString() ?? "a house");
                    ChangeKingdomAction.ApplyByLeaveKingdom(clan, false);
                }
                catch { }
            }

            if (expelled.Count == 0) return;

            string nameList = expelled.Count <= 3
                ? string.Join(", ", expelled)
                : $"{expelled[0]}, {expelled[1]}, and {expelled.Count - 2} others";

            string gotMsg = IsTempleFaction(kingdom)
                ? $"Game of Thrones — When {kingName}'s High Templar fell, the covenant that held the order together fell with them. {nameList} broke from the Temple before a new one could be named — they took their charters and walked out the gates. {newLeader} inherits the altar, and a much smaller faithful. [{expelled.Count} clan{(expelled.Count != 1 ? "s" : "")} left and became independent.]"
                : IsTribes(kingdom)
                ? $"Game of Thrones — When {kingName}'s Khan fell, the blood-compact that held the clans together dissolved. {nameList} raised their own banners and rode for open steppe with everything they could carry. {newLeader} inherits the war-tent — and a much smaller horde. [{expelled.Count} clan{(expelled.Count != 1 ? "s" : "")} left and became independent.]"
                : $"Game of Thrones — When {kingName}'s lord fell, the wolves came out from behind their smiles. The court had been held together by one will. Without it, {nameList} raised their own banners and walked out the gate with everything they owned. {newLeader} inherits a throne — and a much smaller kingdom. What was one realm is now many ambitions. [{expelled.Count} clan{(expelled.Count != 1 ? "s" : "")} left and became independent.]";
            MBInformationManager.AddQuickInformation(new TextObject(gotMsg));
        }

    }
}
