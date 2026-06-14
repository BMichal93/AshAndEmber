// =============================================================================
// ASH AND EMBER — SchemeSystem.Execution.cs
// Success chance, execution, success/failure effects.
// Partial of SchemeSystem (shared state lives in SchemeSystem.cs).
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
    internal static partial class SchemeSystem
    {
        // ── Success chance ────────────────────────────────────────────────────
        internal static float ComputeSuccessChance(Hero instigator, SchemeType type,
            Hero targetHero, Settlement targetSettlement)
        {
            var def = GetDefinition(type);
            if (def == null || instigator == null) return 0f;

            float chance = def.BaseSuccess;

            // Skill bonus: up to +15% at skill 300
            try
            {
                int skill = instigator.GetSkillValue(def.Skill);
                chance += skill / 600f * 0.30f;
            }
            catch { }

            // Security penalty for settlement targets
            if (targetSettlement?.Town != null)
                try { chance -= targetSettlement.Town.Security / 400f; } catch { }

            // Clan-tier penalty (lower than before so high-tier targets are hard but not impossible)
            if (targetHero?.Clan != null)
                try { chance -= targetHero.Clan.Tier * 0.025f; } catch { }
            else if (targetSettlement?.OwnerClan != null)
                try { chance -= targetSettlement.OwnerClan.Tier * 0.02f; } catch { }

            // Ashen targets resist mortal scheming — cold fire does not yield
            bool isAshenTarget = (targetHero != null && ColourLordRegistry.IsAshenLord(targetHero))
                              || targetSettlement?.OwnerClan?.Kingdom?.StringId == AshenKingdomId;
            if (isAshenTarget) chance -= 0.30f;

            return Math.Max(0.05f, Math.Min(0.85f, chance));
        }

        // ── Execution ─────────────────────────────────────────────────────────
        private static void ExecuteScheme(PendingScheme s)
        {
            if (Campaign.Current == null) return;

            Hero       instigator = FindHero(s.InstigatorId);
            Hero       targetHero = string.IsNullOrEmpty(s.TargetHeroId)       ? null : FindHero(s.TargetHeroId);
            Settlement targetSett = string.IsNullOrEmpty(s.TargetSettlementId) ? null : FindSettlement(s.TargetSettlementId);

            if (instigator == null) return;
            if (!string.IsNullOrEmpty(s.TargetHeroId)       && targetHero == null) return;
            if (!string.IsNullOrEmpty(s.TargetSettlementId) && targetSett == null) return;

            float chance = (s.IsPlayer && DebugFree) ? 1f
                         : ComputeSuccessChance(instigator, s.Type, targetHero, targetSett);
            bool  ok     = _rng.NextDouble() < chance;

            if (ok) ApplySuccess(s, instigator, targetHero, targetSett);
            else    ApplyFailure(s, instigator, targetHero, targetSett);

            // If this was an NPC scheme that resolved against the player, open a 1-day retaliation window.
            if (!s.IsPlayer)
            {
                bool hitPlayer = targetHero == Hero.MainHero
                              || (targetSett?.OwnerClan != null && targetSett.OwnerClan == Hero.MainHero?.Clan);
                if (hitPlayer)
                {
                    _retaliationDays = 1;
                    try
                    {
                        MBInformationManager.AddQuickInformation(
                            new TextObject("The fire answers. For the next day, all your schemes cost half their price."));
                    }
                    catch { }
                }
            }
        }

        // ── Success effects ───────────────────────────────────────────────────
        private static void ApplySuccess(PendingScheme s, Hero instigator,
            Hero targetHero, Settlement targetSett)
        {
            string inst = instigator.Name?.ToString() ?? "Someone";
            var    col  = s.IsPlayer ? new Color(0.45f, 0.30f, 0.60f)  // shadowy violet
                                     : new Color(0.65f, 0.25f, 0.25f); // dark red for NPC
            try
            {
                switch (s.Type)
                {
                    // ── Assassinate ───────────────────────────────────────────
                    case SchemeType.Assassinate:
                        if (targetHero == null || !targetHero.IsAlive) break;
                        string tAss = targetHero.Name?.ToString() ?? "the lord";
                        if (targetHero == Hero.MainHero)
                        {
                            // Player cannot be killed by assassination — wounded instead.
                            try { targetHero.MakeWounded(); } catch { }
                            Notify(s,
                                "An assassin found you in the night. The blade missed the mark — you are wounded, not dead. Watch your back.",
                                col);
                        }
                        else
                        {
                            try { KillCharacterAction.ApplyByMurder(targetHero, null, false); } catch { }
                            Notify(s,
                                $"Done. {tAss} was found dead this morning — no witnesses, no clear wound.",
                                col);
                        }
                        break;

                    // ── Spread Terror ─────────────────────────────────────────
                    case SchemeType.SpreadTerror:
                        if (targetSett?.Town == null) break;
                        float drop = 25f + _rng.Next(20);
                        try { targetSett.Town.Security = Math.Max(0f, targetSett.Town.Security - drop); } catch { }
                        Notify(s,
                            $"Violence erupts across {targetSett.Name}. Security falls sharply.",
                            col);
                        break;

                    // ── Poison Well ───────────────────────────────────────────
                    case SchemeType.PoisonWell:
                        if (targetSett?.Town?.GarrisonParty?.MemberRoster == null) break;
                        int toKill = 20 + _rng.Next(41);
                        int killed = 0;
                        try
                        {
                            foreach (var e in targetSett.Town.GarrisonParty.MemberRoster.GetTroopRoster().ToList())
                            {
                                if (e.Character.IsHero) continue;
                                int remove = Math.Min(e.Number - e.WoundedNumber, toKill - killed);
                                if (remove <= 0) continue;
                                targetSett.Town.GarrisonParty.MemberRoster.AddToCounts(e.Character, -remove);
                                killed += remove;
                                if (killed >= toKill) break;
                            }
                        }
                        catch { }
                        Notify(s,
                            $"Sickness swept the barracks of {targetSett.Name}. {killed} militia are dead.",
                            col);
                        break;

                    // ── Stage Coup ────────────────────────────────────────────
                    case SchemeType.StageCoup:
                        if (targetSett?.Town == null) break;
                        try { targetSett.Town.Loyalty  = Math.Max(0f, targetSett.Town.Loyalty  - 40f); } catch { }
                        try { targetSett.Town.Security = Math.Max(0f, targetSett.Town.Security - 35f); } catch { }
                        Notify(s,
                            $"The garrison officers took the coin and stepped aside. Loyalty collapses in {targetSett.Name}.",
                            col);
                        break;

                    // ── Spread Rumors ─────────────────────────────────────────
                    case SchemeType.SpreadRumors:
                        if (targetSett?.Town == null) break;
                        try { targetSett.Town.Loyalty    = Math.Max(0f,  targetSett.Town.Loyalty    - 15f); } catch { }
                        try { targetSett.Town.Prosperity = Math.Max(10f, targetSett.Town.Prosperity * 0.92f); } catch { }
                        Notify(s,
                            $"Whispers have taken hold in {targetSett.Name}. Loyalty and prosperity fall.",
                            col);
                        break;

                    // ── Burn Storage ──────────────────────────────────────────
                    case SchemeType.BurnStorage:
                        if (targetSett?.Town == null) break;
                        try { targetSett.Town.FoodStocks  = Math.Max(10f, targetSett.Town.FoodStocks  * 0.50f); } catch { }
                        try { targetSett.Town.Prosperity  = Math.Max(10f, targetSett.Town.Prosperity  * 0.85f); } catch { }
                        Notify(s,
                            $"Warehouses burned through the night in {targetSett.Name}. Half the food stocks are lost.",
                            col);
                        break;

                    // ── Bribe Soldiers ────────────────────────────────────────
                    case SchemeType.BribeSoldiers:
                        if (targetSett?.Town?.GarrisonParty?.MemberRoster == null) break;
                        int toDesert = 20 + _rng.Next(31);
                        int deserted = 0;
                        try
                        {
                            foreach (var e in targetSett.Town.GarrisonParty.MemberRoster.GetTroopRoster().ToList())
                            {
                                if (e.Character.IsHero) continue;
                                int remove = Math.Min(e.Number - e.WoundedNumber, toDesert - deserted);
                                if (remove <= 0) continue;
                                targetSett.Town.GarrisonParty.MemberRoster.AddToCounts(e.Character, -remove);
                                deserted += remove;
                                if (deserted >= toDesert) break;
                            }
                        }
                        catch { }
                        Notify(s,
                            $"{deserted} soldiers left their posts in {targetSett.Name}. The garrison is weakened.",
                            col);
                        break;

                    // ── Forge Documents ───────────────────────────────────────
                    case SchemeType.ForgeDocuments:
                        if (targetHero == null || !targetHero.IsAlive) break;
                        string tForg = targetHero.Name?.ToString() ?? "the lord";
                        Hero factionLeader = targetHero.Clan?.Kingdom?.Leader;
                        if (factionLeader != null && factionLeader != targetHero && factionLeader.IsAlive)
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(targetHero, factionLeader, -55, false); } catch { }
                        Notify(s,
                            $"Forged letters reached {(factionLeader?.Name?.ToString() ?? "the faction leader")}. {tForg}'s standing with their lord is shaken.",
                            col);
                        break;

                    // ── Hire Assassin ─────────────────────────────────────────
                    case SchemeType.HireAssassin:
                        if (targetHero == null || !targetHero.IsAlive) break;
                        string tHire = targetHero.Name?.ToString() ?? "the lord";
                        try
                        {
                            if (targetHero.PartyBelongedTo?.MemberRoster != null)
                            {
                                foreach (var e in targetHero.PartyBelongedTo.MemberRoster.GetTroopRoster().ToList())
                                {
                                    if (e.Character.IsHero) continue;
                                    int toWound = Math.Max(1, (e.Number - e.WoundedNumber) / 5);
                                    if (toWound <= 0) continue;
                                    try { targetHero.PartyBelongedTo.MemberRoster.AddToCounts(e.Character, 0, false, toWound); } catch { }
                                }
                            }
                        }
                        catch { }
                        Notify(s,
                            $"The blade bloodied {tHire}'s escort and broke off. The warband is wounded and shaken.",
                            col);
                        break;

                    // ── False Accusations ─────────────────────────────────────
                    case SchemeType.FalseAccusations:
                        if (targetHero == null || !targetHero.IsAlive || targetHero.Clan == null) break;
                        string tAcc  = targetHero.Name?.ToString() ?? "the lord";
                        string cAcc  = targetHero.Clan.Name?.ToString() ?? "their clan";
                        // 5% flat renown loss, floor 50 — meaningful at any clan size
                        float renown = Math.Max(50f, targetHero.Clan.Renown * 0.05f);
                        try { targetHero.Clan.Renown = Math.Max(0f, targetHero.Clan.Renown - renown); } catch { }
                        // Also damage relations between instigator and target (they'll suspect someone)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, -20, false); } catch { }
                        Notify(s,
                            $"Slander reached the right ears. {cAcc}'s renown takes a visible hit.",
                            col);
                        break;

                    // ── Viper's Counsel ───────────────────────────────────────
                    case SchemeType.VipersCounsel:
                        if (targetHero == null || !targetHero.IsAlive || targetHero.Clan == null) break;
                        string tVipr  = targetHero.Name?.ToString() ?? "the lord";
                        string cVipr  = targetHero.Clan.Name?.ToString() ?? "their clan";
                        // Target loses 7% renown (floor 50) — more than FalseAccusations, justified by the king's direct involvement
                        float viprLoss = Math.Max(50f, targetHero.Clan.Renown * 0.07f);
                        try { targetHero.Clan.Renown = Math.Max(0f, targetHero.Clan.Renown - viprLoss); } catch { }
                        // Instigator clan gains renown — the contrast is the point
                        float viprGain = 30f + _rng.Next(21); // 30–50
                        try { if (instigator.Clan != null) instigator.Clan.Renown += viprGain; } catch { }
                        Notify(s,
                            $"The king's ear was turned against {cVipr}. Their renown falls; {inst}'s rises.",
                            col);
                        break;

                    // ── Scatter the Wolves ────────────────────────────────────
                    case SchemeType.ScatterWolves:
                        if (targetHero?.Clan?.Kingdom == null) break;
                        Kingdom scatterKingdom = targetHero.Clan.Kingdom;
                        string scatterKingdomName = scatterKingdom.Name?.ToString() ?? "the kingdom";
                        int partyCount = 5 + _rng.Next(4); // 5–8 parties
                        int scatterSpawned = 0;
                        try { scatterSpawned = SpawnBanditsInKingdom(scatterKingdom, partyCount); } catch { }
                        Notify(s,
                            $"{scatterSpawned} bandit parties now roam {scatterKingdomName}'s roads. Their lords will spend weeks chasing shadows.",
                            col);
                        break;
                }
            }
            catch { }

            // Award skill XP to the instigator on any successful scheme.
            try
            {
                var def = GetDefinition(s.Type);
                if (def?.Skill != null && def.SkillXp > 0)
                    instigator.HeroDeveloper?.AddSkillXp(def.Skill, def.SkillXp);
            }
            catch { }
        }

        // ── Failure effects ───────────────────────────────────────────────────
        // 70% — agent fled: scheme dissolved, no trace.
        // 30% — agent caught: crime rating, heavy relations penalty, possible war.
        private static void ApplyFailure(PendingScheme s, Hero instigator,
            Hero targetHero, Settlement targetSett)
        {
            // VipersCounsel always surfaces on failure — there is no silent slip when
            // the king's court is involved. The target is always told and the king sours
            // on the manipulator regardless of whether an agent was literally "caught".
            if (s.Type == SchemeType.VipersCounsel)
            {
                string tVFail = targetHero?.Name?.ToString() ?? "the lord";
                if (targetHero != null && targetHero.IsAlive && targetHero != instigator)
                {
                    int tDelta = -(50 + _rng.Next(21)); // −50 to −70
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, tDelta, false); } catch { }
                }
                Hero king = instigator.Clan?.Kingdom?.Leader;
                if (king != null && king.IsAlive && king != instigator && king != targetHero)
                {
                    int kDelta = -(30 + _rng.Next(21)); // −30 to −50
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, king, kDelta, false); } catch { }
                }
                Notify(s,
                    $"EXPOSED — {tVFail} learned of the plot before the king did. Relations with both have suffered.",
                    new Color(0.80f, 0.20f, 0.18f));
                return;
            }

            bool caught = _rng.NextDouble() < 0.30;

            string inst    = instigator.Name?.ToString() ?? "Someone";
            string tName   = targetHero?.Name?.ToString()
                          ?? targetSett?.Name?.ToString()
                          ?? "the target";
            string tOwner  = (targetSett?.OwnerClan?.Leader ?? targetSett?.MapFaction?.Leader as Hero)
                              ?.Name?.ToString() ?? "";

            if (!caught)
            {
                // 30% chance the blade found the target but didn't finish —
                // the party is bloodied and shaken even in failure.
                bool nearMiss = s.Type == SchemeType.Assassinate && _rng.NextDouble() < 0.30;
                if (nearMiss && targetHero != null && targetHero.IsAlive)
                {
                    try
                    {
                        if (targetHero.PartyBelongedTo?.MemberRoster != null)
                        {
                            foreach (var e in targetHero.PartyBelongedTo.MemberRoster.GetTroopRoster().ToList())
                            {
                                if (e.Character.IsHero) continue;
                                int toWound = Math.Max(1, (e.Number - e.WoundedNumber) / 6);
                                if (toWound <= 0) continue;
                                try { targetHero.PartyBelongedTo.MemberRoster.AddToCounts(e.Character, 0, false, toWound); } catch { }
                            }
                        }
                    }
                    catch { }
                    if (s.IsPlayer)
                        Notify(s,
                            $"The blade reached {tName}'s escort but fled before finishing. The lord lives; the coin is spent.",
                            new Color(0.60f, 0.50f, 0.30f));
                    return;
                }

                // Silent failure — agent slipped away, nothing to trace
                if (s.IsPlayer)
                    Notify(s,
                        $"No opening found. The scheme against {tName} is dissolved.",
                        new Color(0.55f, 0.55f, 0.55f));
                return;
            }

            // ── Caught: apply consequences ────────────────────────────────────
            try
            {
                // Crime rating in target's kingdom
                var targetKingdom = targetHero?.Clan?.Kingdom
                                 ?? targetSett?.OwnerClan?.Kingdom
                                 ?? (targetSett?.MapFaction as Kingdom);
                // Only apply crime rating for player schemes — the API affects the player's
                // clan regardless of who instigator is, so calling it for NPC schemes would
                // incorrectly penalise the player for plots they had no part in.
                if (s.IsPlayer && targetKingdom != null && !targetKingdom.IsEliminated)
                {
                    float crimeDelta = 30f + _rng.Next(31); // 30–60
                    try { ChangeCrimeRatingAction.Apply(targetKingdom, crimeDelta, false); } catch { }
                }

                // Relations penalty with target / settlement owner
                Hero penaltyTarget = targetHero
                    ?? targetSett?.OwnerClan?.Leader
                    ?? targetSett?.MapFaction?.Leader as Hero;

                if (penaltyTarget != null && penaltyTarget.IsAlive && penaltyTarget != instigator)
                {
                    int relDelta = -(60 + _rng.Next(21)); // −60 to −80
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, penaltyTarget, relDelta, false); } catch { }
                }

                // War declaration: assassination or coup caught, 40% chance, different kingdoms
                bool isWarTrigger = s.Type == SchemeType.Assassinate || s.Type == SchemeType.StageCoup;
                if (isWarTrigger && _rng.NextDouble() < 0.40)
                {
                    var instigKingdom = instigator.Clan?.Kingdom;
                    if (instigKingdom != null && targetKingdom != null
                        && instigKingdom != targetKingdom
                        && !instigKingdom.IsEliminated && !targetKingdom.IsEliminated
                        && !instigKingdom.IsAtWarWith(targetKingdom))
                    {
                        try { DeclareWarAction.ApplyByDefault(instigKingdom, targetKingdom); } catch { }
                    }
                }

                // Flavor notification
                string consequence = isWarTrigger
                    ? "War may follow."
                    : s.IsPlayer
                        ? "The damage to your standing is lasting."
                        : $"{inst}'s standing is damaged.";
                string ownerLine   = !string.IsNullOrEmpty(tOwner) ? $" {tOwner} knows." : "";

                Notify(s,
                    $"EXPOSED — {inst}'s plot against {tName} is known.{ownerLine} Crime rating rises; relations plummet. {consequence}",
                    new Color(0.80f, 0.20f, 0.18f));
            }
            catch { }
        }

    }
}
