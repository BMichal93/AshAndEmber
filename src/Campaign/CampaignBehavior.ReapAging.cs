// =============================================================================
// ASH AND EMBER — CampaignBehavior.ReapAging.cs
// Reap yields, prisoner escape, overexertion, NPC battle aging/morale.
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

                    // Companion mages age 25% faster — the fire burns closer, more personally.
                    if (ColourLordRegistry.IsCompanionMage(companion))
                        agingCost = (int)Math.Ceiling(agingCost * 1.25);

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
    }
}
