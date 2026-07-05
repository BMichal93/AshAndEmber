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

                // Breed at a random hideout's land — remote, but the band is then
                // set roaming toward the nearest town so it travels the roads where
                // the player can actually find it.
                var hideouts = Settlement.All.Where(s => s?.Hideout != null).ToList();
                if (hideouts.Count == 0) return;
                Settlement home = hideouts[_rng.Next(hideouts.Count)];
                SpawnBand(home.GetPosition2D, ElementalMath.WildKindForBiome(BiomeHint(home)), roam: true, announce: true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Force-spawn a band right beside the player, ready to engage — the
        // reliable way to SEE the Kindled without waiting for the wilds to breed
        // one and wander it into view. Bound to Ctrl+Shift+F9.
        public static void DebugSpawnNearPlayer()
        {
            try
            {
                var main = MobileParty.MainParty;
                if (main == null) return;
                ElementalKind kind = (ElementalKind)_rng.Next(6);   // any of the six
                var party = new ElementalWildsBehavior().SpawnBand(
                    main.GetPosition2D + new Vec2(0.15f, 0f), kind, roam: false, announce: false);
                Announce(party == null
                    ? "[DEBUG] Kindled spawn FAILED — no bandit clan/hideout/troop available."
                    : $"[DEBUG] {ElementUltimateMath.ElementalName(kind)} band spawned beside you — engage it.");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Shared spawner. Returns the party (or null on any failure).
        private MobileParty SpawnBand(Vec2 anchor, ElementalKind kind, bool roam, bool announce)
        {
            try
            {
                Clan banditClan = Clan.BanditFactions.FirstOrDefault(c => c != null && !c.IsEliminated);
                if (banditClan == null) return null;
                var pt = banditClan.DefaultPartyTemplate;
                if (pt == null) return null;

                // A home hideout is mandatory — a null one crashes the post-battle
                // loot screen. Prefer the clan's own, else the nearest in the world.
                Hideout hideout = null;
                try
                {
                    Settlement hs = banditClan.Settlements.FirstOrDefault(s => s?.Hideout != null)
                        ?? Settlement.All.Where(s => s?.Hideout != null)
                            .OrderBy(s => (s.GetPosition2D - anchor).LengthSquared).FirstOrDefault();
                    hideout = hs?.Hideout;
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (hideout == null) return null;

                const float scatter = 3f;
                Vec2 spawnPos = anchor + new Vec2(
                    (float)(_rng.NextDouble() - 0.5) * scatter * 2f,
                    (float)(_rng.NextDouble() - 0.5) * scatter * 2f);
                var cvec = new CampaignVec2(spawnPos, true);

                string partyId = "elem_wild_" + _rng.Next(999999).ToString("D6");
                MobileParty party = BanditPartyComponent.CreateBanditParty(partyId, banditClan, hideout, false, pt, cvec);
                if (party == null) return null;

                try { party.MemberRoster.Clear(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                CharacterObject troop =
                    MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider");
                if (troop == null) return null;

                int bodies = ElementalMath.WildPartyMinBodies
                           + _rng.Next(ElementalMath.WildPartyMaxBodies - ElementalMath.WildPartyMinBodies + 1);
                party.MemberRoster.AddToCounts(troop, bodies);

                try { party.Party.SetCustomName(new TextObject(ElementUltimateMath.ElementalName(kind))); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _bandKind[party.StringId] = (int)kind;

                // Kick it out of the hideout onto the roads so it is actually
                // encountered, rather than sitting where it was bred.
                if (roam)
                {
                    try
                    {
                        Settlement town = Settlement.All
                            .Where(s => s != null && s.IsTown)
                            .OrderBy(s => (s.GetPosition2D - spawnPos).LengthSquared)
                            .Skip(1 + _rng.Next(3)).FirstOrDefault()
                            ?? Settlement.All.FirstOrDefault(s => s != null && s.IsTown);
                        if (town != null)
                            party.SetMoveGoToSettlement(town, MobileParty.NavigationType.Default, false);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                if (announce) AnnounceBirth(kind, spawnPos);
                return party;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); return null; }
        }

        // A quiet rumour in the message feed when a band wakes — atmospheric, and
        // it doubles as confirmation the spawn actually fired.
        private static void AnnounceBirth(ElementalKind kind, Vec2 where)
        {
            string line;
            switch (kind)
            {
                case ElementalKind.Flame: line = "Word comes of a fire that walks — the Kindled stir in the wilds."; break;
                case ElementalKind.Frost: line = "Herdsmen speak of shapes of ice moving in the northern snows."; break;
                case ElementalKind.Sand:  line = "The deep desert breeds a walking dune — the Sand-Born rise."; break;
                case ElementalKind.Tide:  line = "The old wetlands churn — the Risen Tide takes a shape that hunts."; break;
                case ElementalKind.Gale:  line = "A storm gathers itself into a body on the open steppe."; break;
                default:                  line = "The mountains give up a Stone-Born — old rock, walking."; break;
            }
            Announce(line);
        }

        private static void Announce(string text)
        {
            try { InformationManager.DisplayMessage(new InformationMessage(text, new TaleWorlds.Library.Color(0.65f, 0.55f, 0.95f))); }
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
