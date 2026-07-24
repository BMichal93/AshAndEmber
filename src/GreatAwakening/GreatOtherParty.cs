// =============================================================================
// ASH AND EMBER — GreatAwakening/GreatOtherParty.cs
//
// The campaign-map party that carries The Great Other once the sacrifice
// completes. Spawned the same proven way every other roaming special party in
// this mod is (BanditPartyComponent.CreateBanditParty — see
// ElementalWildsBehavior.SpawnBand / CampaignMapEvents.Helpers.SpawnAshenSpawnParty),
// seeded with the champion troop plus a growing host of ashen_revenants.
//
// KNOWN LIMITATION (flagged rather than guessed): scenario A ("never attacks
// Duneborn, Duneborn never attacks it") is approximated by keeping the party
// permanently escorting the Duneborn leader's own army, not by an engine-level
// non-aggression pact — giving a bandit-owned party true kingdom-level
// allegiance would need a TaleWorlds party-ownership call this environment has
// no game DLL to verify against (see behaviour.md's reflection-check process).
// Worth hardening once verified on the actual build machine.
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    internal static class GreatOtherParty
    {
        internal const string ChampionTroopId = "elemental_being";
        internal const string RevenantTroopId = "ashen_revenant";

        private static readonly Random _rng = new Random();

        private static string _partyId = null;
        private static bool   _controlled = false;
        private static int    _summonDay = -1;
        private static int    _lastTopUpDay = -1;
        private static int    _lastHungerDay = -1;
        // Per-mission latch so only the first champion-troop agent seen in a
        // mission is promoted — cleared when the mission ends.
        private static bool   _championClaimedThisMission = false;
        // Set true only while the CURRENT mission's MapEvent actually involves
        // this party — mirrors ElementalWildsBehavior's PendingBattleKind pattern,
        // so identifying "which agent is the champion" never has to guess at an
        // unverified per-agent party-origin API.
        private static bool   _missionHasChampion = false;

        internal static void SyncData(IDataStore store)
        {
            try { store.SyncData("GRAWK_GOPartyId",       ref _partyId); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("GRAWK_GOControlled",    ref _controlled); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("GRAWK_GOSummonDay",     ref _summonDay); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("GRAWK_GOLastTopUpDay",  ref _lastTopUpDay); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("GRAWK_GOLastHungerDay", ref _lastHungerDay); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void ResetForNewGame()
        {
            _partyId = null; _controlled = false;
            _summonDay = -1; _lastTopUpDay = -1; _lastHungerDay = -1;
            _championClaimedThisMission = false;
        }

        internal static void ClearMissionLatch() { _championClaimedThisMission = false; _missionHasChampion = false; }

        // ── Mission hookup — mirrors ElementalWildsBehavior.OnMapEventStarted ────
        internal static void OnMapEventStarted(MapEvent mapEvent)
        {
            try
            {
                if (mapEvent == null || string.IsNullOrEmpty(_partyId)) return;
                foreach (var side in new[] { mapEvent.AttackerSide, mapEvent.DefenderSide })
                {
                    if (side == null) continue;
                    if (side.Parties.Any(p => p?.Party?.MobileParty?.StringId == _partyId))
                    {
                        _missionHasChampion = true;
                        return;
                    }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Query ────────────────────────────────────────────────────────────────
        private static MobileParty CurrentParty()
        {
            if (string.IsNullOrEmpty(_partyId)) return null;
            try { return MobileParty.All.FirstOrDefault(p => p != null && p.StringId == _partyId); }
            catch { return null; }
        }

        internal static bool IsAlive() => CurrentParty() != null;

        // While controlled AND alive, Duneborn cannot make peace with anyone
        // (AshenDiplomacyModel reads this). The moment it dies, the block lifts.
        internal static bool IsControlledAndAlive() => _controlled && IsAlive();

        // ── Summon ───────────────────────────────────────────────────────────────
        internal static void Summon(Settlement altar, bool controlled)
        {
            try
            {
                Vec2 anchor = default;
                if (altar != null) anchor = altar.GetPosition2D;
                else if (MobileParty.MainParty != null) anchor = MobileParty.MainParty.GetPosition2D;

                Clan banditClan = Clan.BanditFactions.FirstOrDefault(c => c != null && !c.IsEliminated);
                if (banditClan == null) return;
                var pt = banditClan.DefaultPartyTemplate;
                if (pt == null) return;

                Hideout hideout = null;
                try
                {
                    Settlement hs = banditClan.Settlements.FirstOrDefault(s => s?.Hideout != null)
                        ?? Settlement.All.Where(s => s?.Hideout != null)
                            .OrderBy(s => (s.GetPosition2D - anchor).LengthSquared).FirstOrDefault();
                    hideout = hs?.Hideout;
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (hideout == null) return;

                var cvec = new CampaignVec2(anchor, true);
                string partyId = "great_other_" + _rng.Next(999999).ToString("D6");
                MobileParty party = BanditPartyComponent.CreateBanditParty(partyId, banditClan, hideout, false, pt, cvec);
                if (party == null) return;

                try { party.MemberRoster.Clear(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                CharacterObject champion = MBObjectManager.Instance.GetObject<CharacterObject>(ChampionTroopId)
                    ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit");
                if (champion != null) party.MemberRoster.AddToCounts(champion, 1);

                CharacterObject revenant = MBObjectManager.Instance.GetObject<CharacterObject>(RevenantTroopId);
                if (revenant != null) party.MemberRoster.AddToCounts(revenant, GreatAwakeningMath.RevenantCap);

                try { party.Party.SetCustomName(new TextObject("The Great Other")); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                _partyId = party.StringId;
                _controlled = controlled;
                int day = (int)CampaignTime.Now.ToDays;
                _summonDay = day; _lastTopUpDay = day; _lastHungerDay = day;

                if (controlled) EscortDunebornLeader(party);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Daily: keep pace with the Duneborn leader's army (scenario A) ────────
        internal static void DailyTick()
        {
            if (!_controlled) return;
            var party = CurrentParty();
            if (party == null) return;
            try { EscortDunebornLeader(party); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void EscortDunebornLeader(MobileParty party)
        {
            Hero leader = GreatAwakeningCampaignBehavior.DunebornLeader();
            var leaderParty = leader?.PartyBelongedTo;
            if (leaderParty != null && leaderParty != party)
            {
                try { party.SetMoveEscortParty(leaderParty, MobileParty.NavigationType.Default, false); }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            else
            {
                Settlement leaderSettlement = leader?.CurrentSettlement;
                if (leaderSettlement != null)
                    try { party.SetMoveGoToSettlement(leaderSettlement, MobileParty.NavigationType.Default, false); }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // ── Weekly: top up revenants, and (every ~30 days) the Hunger ────────────
        internal static void WeeklyTick()
        {
            var party = CurrentParty();
            if (party == null) return;

            int day = 0;
            try { day = (int)CampaignTime.Now.ToDays; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            if (day - _lastTopUpDay >= GreatAwakeningMath.RevenantTopUpIntervalDays)
            {
                _lastTopUpDay = day;
                try { TopUpRevenants(party); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            if (day - _lastHungerDay >= GreatAwakeningMath.HungerIntervalDays)
            {
                _lastHungerDay = day;
                try { Hunger(party); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // Scenario B roams — nudge it toward a random city it can still prey
            // on so the marauding is visible rather than idle at the altar forever.
            // (A plain probability roll, not an idle-check, to avoid depending on
            // an unverified MobileParty.TargetSettlement read in this environment.)
            if (!_controlled && _rng.NextDouble() < 0.35)
            {
                try { RoamToward(party); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        private static void TopUpRevenants(MobileParty party)
        {
            CharacterObject revenant = MBObjectManager.Instance.GetObject<CharacterObject>(RevenantTroopId);
            if (revenant == null) return;
            int have = party.MemberRoster.GetTroopRoster()
                .Where(e => e.Character?.StringId == RevenantTroopId).Sum(e => e.Number);
            int missing = GreatAwakeningMath.RevenantCap - have;
            if (missing > 0) party.MemberRoster.AddToCounts(revenant, missing);
        }

        private static void RoamToward(MobileParty party)
        {
            Settlement target = Settlement.All
                .Where(s => s != null && s.IsTown && !AshenCitySystem.IsAshenFaction(s.MapFaction))
                .OrderBy(_ => _rng.Next())
                .FirstOrDefault();
            if (target != null) party.SetMoveGoToSettlement(target, MobileParty.NavigationType.Default, false);
        }

        // ── The Hunger ────────────────────────────────────────────────────────────
        // Every 30 days: the nearest city it may prey on (not Duneborn's while
        // controlled, not Ashen's while uncontrolled) has its prosperity, security,
        // food and militia gutted, and its garrison + militia troops killed
        // outright (not merely wounded, unlike the older Ashen Plague event).
        // Notable/townsperson Heroes are deliberately spared — see the plan notes:
        // Bannerlord does not reliably respawn a settlement's killed notables, so
        // deleting Hero objects on a recurring 30-day event for the rest of the
        // campaign would be an unrecoverable, ever-worsening save mutation.
        private static void Hunger(MobileParty party)
        {
            Vec2 pos = party.GetPosition2D;
            Settlement target = Settlement.All
                .Where(s => s != null && s.IsTown && s.Town != null
                         && (_controlled
                                ? s.MapFaction?.StringId != GreatAwakeningCampaignBehavior.DunebornKingdomId
                                : !AshenCitySystem.IsAshenFaction(s.MapFaction)))
                .OrderBy(s => (s.GetPosition2D - pos).LengthSquared)
                .FirstOrDefault();
            if (target == null) return;

            try { target.Town.Prosperity = 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { target.Town.Security   = 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { target.Town.FoodStocks = 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { target.Militia         = 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            int killed = 0;
            try
            {
                var garrison = target.Town.GarrisonParty?.MemberRoster;
                if (garrison != null)
                {
                    foreach (var e in garrison.GetTroopRoster().ToList())
                    {
                        if (e.Character.IsHero || e.Number <= 0) continue;
                        garrison.AddToCounts(e.Character, -e.Number);
                        killed += e.Number;
                    }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"The Hunger — The Great Other has fed on {target.Name}. Its stores are empty, its walls " +
                    $"unguarded, and {killed} of its soldiers do not answer muster."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Battle: promote the champion troop to Void the first time it is
        // built in a mission that actually involves this party ─────────────────
        internal static void TryRegisterChampion(Agent agent)
        {
            if (agent == null || _championClaimedThisMission || !_missionHasChampion) return;
            try
            {
                if (agent.Character?.StringId != ChampionTroopId) return;
                _championClaimedThisMission = true;
                ElementalBeings.RegisterGreatOther(agent);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
