// =============================================================================
// ASH AND EMBER — TalentSystem.MapSpells.cs
// Player campaign-map spell execution (Break Wills, Plague, Ashstorm, Toxic Fog, …).
// Partial of TalentSystem (shared static state lives in TalentSystem.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class TalentSystem
    {
        // ── Campaign map spell execution ──────────────────────────────────────
        // powerMult scales spell output (1.0 = baseline). Always called via
        // SpellMinigame which determines the multiplier from recall score.
        public static void ExecuteMapSpell(TalentId id, float powerMult = 1f)
        {
            if (!Has(id)) return;
            var def = GetDef(id);
            if (!def.IsSpell) return;

            bool isConsumable = GetDef(id)?.IsConsumable == true;
            if (!isConsumable)
            {
                if (MageKnowledge.IsAshen)
                {
                    if (_dailyMapCastCount > 0 && _rng.Next(3) == 0)
                        MageKnowledge.QueuePossessionEvent();
                    try
                    {
                        if (Hero.MainHero?.MapFaction is Kingdom ashenK)
                        {
                            ChangeCrimeRatingAction.Apply(ashenK, GetBlightCrimeCost(id), false);
                            InformationManager.DisplayMessage(new InformationMessage(
                                "The ash spreads.", new Color(0.3f, 0.35f, 0.7f)));
                        }
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                else
                {
                    int cost = GetDailyCastCost();
                    bool skipAging = Has(TalentId.Sorcerer) && (_dailyMapCastCount == 0 || _rng.Next(4) == 0);
                    if (skipAging)
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The fire gives back.", new Color(0.9f, 0.6f, 0.3f)));
                    else
                    {
                        AgingSystem.AgeHero(Hero.MainHero, cost);
                        if (cost > 1)
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"The fire demands more — {cost} days.", new Color(0.9f, 0.5f, 0.2f)));
                    }
                }
                _dailyMapCastCount++;
                try { AgingSystem.RecordMapCast(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            switch (id)
            {
                case TalentId.BreakWills:   CastBreakWills(powerMult);   break;
                case TalentId.Inspire:      CastInspire(powerMult);      break;
                case TalentId.Plague:       CastPlague(powerMult);       break;
                case TalentId.Clairvoyance: CastClairvoyance(powerMult); break;
                case TalentId.Extinguish:   CastExtinguish(powerMult);   break;
                case TalentId.Fade:         CastFade(powerMult);         break;
                case TalentId.Ashstorm:     CastAshstorm(powerMult);     break;
                case TalentId.ToxicFog:     CastToxicFog(powerMult);     break;
            }

            try { MageKnowledge.RewardCastSkill(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void CastBreakWills(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                var target = MobileParty.All
                    .Where(p => p.IsActive && FactionManager.IsAtWarAgainstFaction(p.MapFaction, MobileParty.MainParty.MapFaction)
                             && p.LeaderHero != null
                             && (p.GetPosition2D - playerPos).Length < 75f)
                    .OrderBy(p => (p.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Unsettle — no enemy party in range."); return; }
                int morale = (int)(40f * mult);
                target.RecentEventsMorale -= morale;
                var tClan = target.LeaderHero?.Clan;
                float infLoss = 10f * mult;
                if (tClan != null) tClan.Influence = Math.Max(0f, tClan.Influence - infLoss);
                string infLine = tClan != null ? $" -{(int)infLoss} influence." : "";
                Msg($"Unsettle — dread settles over {target.Name}. -{morale} morale.{infLine}");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void CastInspire(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                int morale = (int)(40f * mult);
                MobileParty.MainParty.RecentEventsMorale += morale;
                int healPerTroop = Math.Max(1, (int)(8f * mult));
                var roster = MobileParty.MainParty.MemberRoster;
                var wounded = roster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.WoundedNumber > 0).ToList();
                int roused = 0;
                foreach (var entry in wounded)
                {
                    int heal = Math.Min(entry.WoundedNumber, healPerTroop);
                    try { roster.AddToCounts(entry.Character, 0, false, -heal); roused += heal; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                string msg = roused > 0
                    ? $"Kindle — warmth floods your ranks. +{morale} morale, {roused} soldier{(roused != 1 ? "s" : "")} rise from their wounds."
                    : $"Kindle — warmth floods your ranks. +{morale} morale.";
                Msg(msg);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void CastPlague(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                var playerFaction = MobileParty.MainParty.MapFaction;
                var target = Settlement.All
                    .Where(s => s.IsVillage && s.Village != null && s.MapFaction != null
                             && s.MapFaction != playerFaction
                             && FactionManager.IsAtWarAgainstFaction(s.MapFaction, playerFaction))
                    .OrderBy(s => (s.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Wither — no enemy villages found."); return; }
                float before    = target.Village.Hearth;
                float reduction = 0.20f * mult;
                target.Village.Hearth = Math.Max(10f, before * (1f - reduction));
                Msg($"Wither — something old settles over {target.Name}. Hearth reduced by {(int)(reduction * 100f)}%.");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void CastClairvoyance(float mult)
        {
            try
            {
                if (Hero.MainHero?.Clan?.Kingdom != null)
                {
                    int influence = (int)(25f * mult);
                    Hero.MainHero.Clan.Influence += influence;
                    Msg($"Clairvoyance — the threads of power revealed. +{influence} influence.");
                }
                else
                {
                    int gold = (int)(700f * mult);
                    Hero.MainHero.ChangeHeroGold(gold);
                    Msg($"Clairvoyance — no throne to bend, but the fire finds other currents. +{gold} gold.");
                }
            }
            catch { Msg("Clairvoyance — insight granted."); }

            // Reveal any pending NPC scheme against the player and offer to cancel it for 2000 gold.
            try
            {
                if (SchemeSystem.TryGetSchemeAgainstPlayer(out Hero schemer, out SchemeType schemeType))
                {
                    string schemerName = schemer?.Name?.ToString() ?? "someone";
                    string typeName    = schemeType.ToString();
                    MageKnowledge._deferredInquiry = () =>
                    {
                        try
                        {
                            InformationManager.ShowInquiry(new InquiryData(
                                "The Fire Sees Further",
                                $"The threads do not lie. {schemerName} has set a {typeName} scheme against you — it has not yet resolved.\n\nPay 2000 gold now to sever it before it reaches you?",
                                true, true,
                                "Pay 2000 gold — cancel the scheme",
                                "Let it play out",
                                () =>
                                {
                                    if (Hero.MainHero != null && Hero.MainHero.Gold >= 2000)
                                    {
                                        Hero.MainHero.ChangeHeroGold(-2000);
                                        SchemeSystem.CancelSchemesFromInstigator(schemer);
                                        Msg("The scheme collapses before it begins. 2000 gold spent.");
                                    }
                                    else
                                    {
                                        Msg("Not enough gold — the scheme remains in motion.");
                                    }
                                },
                                () => { Msg("You know it is coming. That is something."); }));
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    };
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void CastExtinguish(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                var target = MobileParty.All
                    .Where(p => p.IsActive && FactionManager.IsAtWarAgainstFaction(p.MapFaction, MobileParty.MainParty.MapFaction)
                             && p.MemberRoster.TotalRegulars > 0
                             && (p.GetPosition2D - playerPos).Length < 60f)
                    .OrderBy(p => (p.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Extinguish — no enemy party in range."); return; }
                int count  = (int)((5 + _rng.Next(8)) * mult);  // base 5–12, scaled
                int morale = (int)(30f * mult);
                int actual = 0;
                var troops = target.MemberRoster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
                for (int i = 0; i < count && troops.Count > 0; i++)
                {
                    int idx   = _rng.Next(troops.Count);
                    int wound = _rng.Next(2) == 0 ? 1 : 0;
                    try { target.MemberRoster.AddToCounts(troops[idx].Character, wound == 1 ? 0 : -1, false, wound); actual++; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                target.RecentEventsMorale -= morale;
                Msg($"Extinguish — {actual} fire{(actual != 1 ? "s" : "")} snuffed in {target.Name}. Their courage breaks. -{morale} morale.");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void CastFade(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) { Msg("Fade — no party to conceal."); return; }
                // Strong recall (≥1.20×) extends concealment by an extra day.
                _fadeDaysRemaining = mult >= 1.20f ? 2 : 1;
                int days = _fadeDaysRemaining;
                bool applied = TrySetPartyConcealed(MobileParty.MainParty, true);
                if (applied)
                {
                    Msg($"Fade — ash wraps your party. For {days} days, enemy scouts will not find you.");
                }
                else
                {
                    // Fallback when IgnoreByOtherParties is not accessible: scatter nearby enemies.
                    Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                    int scattered = 0;
                    foreach (MobileParty p in MobileParty.All.ToList())
                    {
                        if (!p.IsActive || p == MobileParty.MainParty) continue;
                        try { if (!FactionManager.IsAtWarAgainstFaction(p.MapFaction, MobileParty.MainParty.MapFaction)) continue; } catch { continue; }
                        if ((p.GetPosition2D - playerPos).Length > 80f) continue;
                        try { p.RecentEventsMorale -= 40f; scattered++; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                    string tail = scattered > 0
                        ? $" {scattered} nearby enemy {(scattered == 1 ? "party is" : "parties are")} thrown into confusion."
                        : "";
                    Msg($"Fade — the ash rises around you.{tail}");
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static float GetBlightCrimeCost(TalentId id)
        {
            switch (id)
            {
                case TalentId.Ashstorm:
                case TalentId.Extinguish:
                case TalentId.Clairvoyance: return 15f;
                case TalentId.BreakWills:
                case TalentId.Plague:       return 10f;
                case TalentId.Fade:         return 5f;
                default:                    return 5f;
            }
        }

        private static void CastAshstorm(float mult)
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos     = MobileParty.MainParty.GetPosition2D;
                var  playerFaction = MobileParty.MainParty.MapFaction;

                var target = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle)
                             && s.Town != null
                             && s.MapFaction != null
                             && FactionManager.IsAtWarAgainstFaction(s.MapFaction, playerFaction)
                             && (s.GetPosition2D - playerPos).Length < 50f)
                    .OrderBy(s => (s.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();

                if (target == null) { Msg("Ashstorm — no enemy settlement within reach."); return; }

                // Kill garrison soldiers
                int toKill = (int)((10 + _rng.Next(20)) * mult);
                int killed = 0;
                var garrison = target.Town?.GarrisonParty?.MemberRoster;
                if (garrison != null)
                {
                    var troops = garrison.GetTroopRoster()
                        .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
                    for (int i = 0; i < toKill && troops.Count > 0; i++)
                    {
                        int idx = _rng.Next(troops.Count);
                        try { garrison.AddToCounts(troops[idx].Character, -1); killed++; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }

                // Burn food stores, drop prosperity and security
                try { target.Town.FoodStocks    -= (int)(150f * mult); }            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { target.Town.Prosperity    = Math.Max(100f, target.Town.Prosperity - (int)(250f * mult)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { target.Town.Security      = Math.Max(0f,   target.Town.Security   - (int)(25f  * mult)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                string killLine = killed > 0 ? $" {killed} garrison soldier{(killed != 1 ? "s" : "")} consumed." : "";
                Msg($"Ashstorm — fire rains over {target.Name}.{killLine} Food burns. The walls remember it.");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void CastToxicFog(float mult)
        {
            // Consume the one-time powder before resolving effects.
            _purchased.Remove(TalentId.ToxicFog);

            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos    = MobileParty.MainParty.GetPosition2D;
                const float range = 60f;

                var affectedLords    = new HashSet<Hero>();
                var affectedKingdoms = new HashSet<Kingdom>();
                int totalWounded     = 0;
                var settlementsHit   = new List<string>();

                // ── Settlements in range ──────────────────────────────────────────
                foreach (var s in Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle) && s.Town != null
                             && (s.GetPosition2D - playerPos).Length < range).ToList())
                {
                    settlementsHit.Add(s.Name?.ToString() ?? "?");

                    // Kill all militia
                    try { s.Militia = 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                    // Wound 40–70 % of garrison
                    var garrison = s.Town?.GarrisonParty?.MemberRoster;
                    if (garrison != null)
                    {
                        foreach (var e in garrison.GetTroopRoster()
                            .Where(e => !e.Character.IsHero).ToList())
                        {
                            int healthy = e.Number - e.WoundedNumber;
                            int toWound = Math.Min(healthy,
                                (int)(healthy * (0.40f + (float)_rng.NextDouble() * 0.30f) * mult));
                            if (toWound > 0)
                                try { garrison.AddToCounts(e.Character, 0, false, toWound); totalWounded += toWound; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                    }

                    var owner = s.OwnerClan?.Leader;
                    if (owner != null && owner != Hero.MainHero)
                    {
                        affectedLords.Add(owner);
                        if (s.OwnerClan?.Kingdom != null) affectedKingdoms.Add(s.OwnerClan.Kingdom);
                    }
                }

                // ── Mobile parties in range (all factions — fog does not discriminate) ───
                foreach (var party in MobileParty.All
                    .Where(p => p.IsActive && p != MobileParty.MainParty
                             && (p.GetPosition2D - playerPos).Length < range).ToList())
                {
                    foreach (var e in party.MemberRoster.GetTroopRoster()
                        .Where(e => !e.Character.IsHero).ToList())
                    {
                        int healthy = e.Number - e.WoundedNumber;
                        int toWound = Math.Min(healthy,
                            (int)(healthy * (0.20f + (float)_rng.NextDouble() * 0.30f) * mult));
                        if (toWound > 0)
                            try { party.MemberRoster.AddToCounts(e.Character, 0, false, toWound); totalWounded += toWound; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                    // Track lords and kingdoms for relations/war — only where they exist
                    if (party.LeaderHero != null)
                    {
                        affectedLords.Add(party.LeaderHero);
                        if (party.MapFaction is Kingdom k) affectedKingdoms.Add(k);
                    }
                }

                // ── 50 / 50: own party ────────────────────────────────────────────
                bool hitsOwn   = _rng.Next(2) == 0;
                int  ownWounded = 0;
                if (hitsOwn)
                {
                    foreach (var e in MobileParty.MainParty.MemberRoster.GetTroopRoster()
                        .Where(e => !e.Character.IsHero).ToList())
                    {
                        int healthy = e.Number - e.WoundedNumber;
                        int toWound = Math.Min(healthy, (int)(healthy * (0.15f + (float)_rng.NextDouble() * 0.15f)));
                        if (toWound > 0)
                            try { MobileParty.MainParty.MemberRoster.AddToCounts(e.Character, 0, false, toWound); ownWounded += toWound; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }

                // ── Relation penalties (-20 per affected lord) ────────────────────
                foreach (var lord in affectedLords)
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -20, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                // ── Criminal rating (+50 in every affected kingdom) ──────────────
                foreach (var targetK in affectedKingdoms)
                {
                    try { ChangeCrimeRatingAction.Apply(targetK, 50f, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                // ── War risk (30 % per affected non-allied kingdom) ───────────────
                var playerKingdom = Hero.MainHero?.Clan?.Kingdom;
                foreach (var targetK in affectedKingdoms)
                {
                    if (targetK == playerKingdom) continue;
                    if (_rng.NextDouble() < 0.30)
                    {
                        try
                        {
                            if (playerKingdom != null
                                && !FactionManager.IsAtWarAgainstFaction(targetK, playerKingdom))
                                DeclareWarAction.ApplyByDefault(targetK, playerKingdom);
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }

                // ── Outcome message ───────────────────────────────────────────────
                string hitLine = settlementsHit.Count > 0
                    ? $" The cloud drifts across {string.Join(", ", settlementsHit.Take(3))}{(settlementsHit.Count > 3 ? " and more" : "")}."
                    : "";
                string ownLine = hitsOwn && ownWounded > 0
                    ? $" The wind turned — {ownWounded} of your own men are choking."
                    : " Your men were far enough upwind.";
                Msg($"The vessel shatters. A yellow-green cloud rolls across the land.{hitLine} Militia dead. {totalWounded} soldiers felled.{ownLine}");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

    }
}
