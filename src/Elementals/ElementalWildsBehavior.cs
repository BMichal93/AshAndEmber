// =============================================================================
// ASH AND EMBER — Elementals/ElementalWildsBehavior.cs
//
// Where raw magic pools too thick and too long, the land breeds THE KINDLED —
// small bands of elemental beings that wander the remote wilds (the snow of the
// north, the deep desert, the old forests, the open steppe, the mountain roots).
// A band is an ordinary bandit party on the map — but the moment the player
// marches on it, ElementalBeings remakes its bodies into the elemental kind the
// land bred (aura + weakness), via the OnAgentBuild hook.
//
// Spawning follows the same hideout-safe bandit-party pattern as the Ashen
// marches (BanditPartyComponent.CreateBanditParty — a null home hideout crashes
// the post-battle loot screen, so we never pass one). Band → kind is persisted
// as parallel lists; nothing else here touches the save.
//
// The OTHER ways a Kindled appears are unified elsewhere and need no behaviour:
//   • an enemy mage's Spirit Unbinding summons one (ElementUltimates), and
//   • a battle can wake one (BattleEvents — The Kindling).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public class ElementalWildsBehavior : CampaignBehaviorBase
    {
        // Party StringId → the elemental kind the land bred it as.
        private static readonly Dictionary<string, int> _bandKind = new Dictionary<string, int>();
        private static readonly Random _rng = new Random();

        public static void ResetForNewGame() => _bandKind.Clear();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
        }

        public override void SyncData(IDataStore dataStore)
        {
            var ids   = _bandKind.Keys.ToList();
            var kinds = _bandKind.Values.ToList();
            dataStore.SyncData("ELEM_WildBandIds",   ref ids);
            dataStore.SyncData("ELEM_WildBandKinds", ref kinds);
            if (dataStore.IsLoading)
            {
                _bandKind.Clear();
                if (ids != null && kinds != null)
                    for (int i = 0; i < ids.Count && i < kinds.Count; i++)
                        _bandKind[ids[i]] = kinds[i];
            }
        }

        // ── Query ────────────────────────────────────────────────────────────────
        public static bool IsWildBand(MobileParty party)
            => party != null && _bandKind.ContainsKey(party.StringId);

        private static ElementalKind? KindOf(MobileParty party)
        {
            if (party != null && _bandKind.TryGetValue(party.StringId, out int k))
                return (ElementalKind)k;
            return null;
        }

        // ── Daily: breed a new band where magic has pooled ───────────────────────
        private void OnDailyTick()
        {
            try
            {
                PruneDead();
                if (_bandKind.Count >= ElementalMath.WildMaxLivingBands) return;
                if (_rng.NextDouble() >= ElementalMath.WildDailySpawnChance) return;
                SpawnWildBand();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private void SpawnWildBand()
        {
            try
            {
                Clan banditClan = Clan.BanditFactions.FirstOrDefault(c => c != null && !c.IsEliminated);
                if (banditClan == null) return;
                var pt = banditClan.DefaultPartyTemplate;
                if (pt == null) return;

                // Breed in the remote wilds — a random hideout is as far from the
                // roads as the world gets. Its land decides the element.
                var hideouts = Settlement.All.Where(s => s?.Hideout != null).ToList();
                if (hideouts.Count == 0) return;
                Settlement home = hideouts[_rng.Next(hideouts.Count)];
                Hideout hideout = home.Hideout;

                ElementalKind kind = ElementalMath.WildKindForBiome(BiomeHint(home));

                Vec2 anchor = home.GetPosition2D;
                const float scatter = 5f;
                Vec2 spawnPos = anchor + new Vec2(
                    (float)(_rng.NextDouble() - 0.5) * scatter * 2f,
                    (float)(_rng.NextDouble() - 0.5) * scatter * 2f);
                var cvec = new CampaignVec2(spawnPos, true);

                string partyId = "elem_wild_" + _rng.Next(999999).ToString("D6");
                MobileParty party = BanditPartyComponent.CreateBanditParty(partyId, banditClan, hideout, false, pt, cvec);
                if (party == null) return;

                try { party.MemberRoster.Clear(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                CharacterObject troop =
                    MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider");
                if (troop == null) return; // no troop to seed the band with

                int bodies = ElementalMath.WildPartyMinBodies
                           + _rng.Next(ElementalMath.WildPartyMaxBodies - ElementalMath.WildPartyMinBodies + 1);
                party.MemberRoster.AddToCounts(troop, bodies);

                try { party.Party.SetCustomName(new TextObject(ElementUltimateMath.ElementalName(kind))); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _bandKind[party.StringId] = (int)kind;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Culture of the nearest land, mapped to the terrain the Kindled reads.
        private static string BiomeHint(Settlement s)
        {
            string c = "";
            try { c = s?.Culture?.StringId ?? ""; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            switch (c)
            {
                case "aserai":   return "desert";
                case "sturgia":  return "snow";
                case "battania": return "forest";
                case "khuzait":  return "steppe";
                default:         return "mountain"; // empire / vlandia / unknown
            }
        }

        // ── Battle hookup: mark the coming fight as an elemental one ──────────────
        private void OnMapEventStarted(MapEvent mapEvent, PartyBase attackerParty, PartyBase defenderParty)
        {
            try
            {
                if (mapEvent == null) return;
                bool playerInvolved =
                    (mapEvent.AttackerSide?.Parties.Any(p => p.Party == PartyBase.MainParty) == true) ||
                    (mapEvent.DefenderSide?.Parties.Any(p => p.Party == PartyBase.MainParty) == true);
                if (!playerInvolved) return;

                ElementalKind? kind = null;
                foreach (var side in new[] { mapEvent.AttackerSide, mapEvent.DefenderSide })
                {
                    if (side == null) continue;
                    foreach (var p in side.Parties)
                    {
                        var k = KindOf(p?.Party?.MobileParty);
                        if (k != null) { kind = k; break; }
                    }
                    if (kind != null) break;
                }
                ElementalBeings.PendingBattleKind = kind;   // null in an ordinary fight
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Clear the mark so a later, unrelated battle never inherits it (the
        // mission-end clear is the other guard, but a fight that never opens a
        // mission — auto-resolved — must not leave it armed).
        private void OnMapEventEnded(MapEvent mapEvent)
        {
            try { ElementalBeings.PendingBattleKind = null; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void PruneDead()
        {
            try
            {
                var alive = new HashSet<string>(MobileParty.All.Select(p => p.StringId));
                var dead = _bandKind.Keys.Where(id => !alive.Contains(id)).ToList();
                foreach (var id in dead) _bandKind.Remove(id);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
