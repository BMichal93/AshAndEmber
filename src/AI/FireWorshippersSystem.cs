// =============================================================================
// LIFE & DEATH MAGIC — AI/FireWorshippersSystem.cs
// Renames ~10% of newly created bandit parties to one of three special types:
//   • "Fire Worshippers"  — from Looter / forest_bandit parties
//   • "Ashen Spawn"       — from sea_raider / mountain_bandit parties
//   • "Wandering Circle"  — from steppe_bandit / desert_bandit parties
// Tracked parties are guaranteed at least one bandit mage.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static class FireWorshippersSystem
    {
        private const float RenameChance = 0.10f; // 10% of qualifying parties

        private static readonly HashSet<string> _fireWorshipperIds   = new HashSet<string>();
        private static readonly HashSet<string> _ashenSpawnIds       = new HashSet<string>();
        private static readonly HashSet<string> _wanderingCircleIds  = new HashSet<string>();
        private static readonly Random _rng = new Random();

        // Troop IDs that qualify each category
        private static readonly HashSet<string> _fireWorshipperTroops = new HashSet<string>
        {
            "looter", "forest_bandit",
        };
        private static readonly HashSet<string> _ashenSpawnTroops = new HashSet<string>
        {
            "sea_raider", "mountain_bandit",
        };
        private static readonly HashSet<string> _wanderingCircleTroops = new HashSet<string>
        {
            "steppe_bandit", "desert_bandit",
        };

        public static bool IsFireWorshipper(MobileParty party) =>
            party != null && _fireWorshipperIds.Contains(party.StringId);

        public static bool IsAshenSpawn(MobileParty party) =>
            party != null && _ashenSpawnIds.Contains(party.StringId);

        public static bool IsWanderingCircle(MobileParty party) =>
            party != null && _wanderingCircleIds.Contains(party.StringId);

        public static bool IsSpecialParty(MobileParty party) =>
            IsFireWorshipper(party) || IsAshenSpawn(party) || IsWanderingCircle(party);

        // ── Hook: called when any mobile party is created ─────────────────────
        public static void OnPartyCreated(MobileParty party)
        {
            if (party == null || !party.IsActive) return;
            try
            {
                bool isFireCategory    = ContainsTroop(party, _fireWorshipperTroops);
                bool isAshenCategory   = ContainsTroop(party, _ashenSpawnTroops);
                bool isCircleCategory  = ContainsTroop(party, _wanderingCircleTroops);

                if (!isFireCategory && !isAshenCategory && !isCircleCategory) return;
                if (_rng.NextDouble() >= RenameChance) return;

                // Resolve ties by random choice
                var matching = new List<int>();
                if (isFireCategory)   matching.Add(0);
                if (isAshenCategory)  matching.Add(1);
                if (isCircleCategory) matching.Add(2);
                int pick = matching[_rng.Next(matching.Count)];

                if (pick == 0)
                {
                    TryRenameParty(party, "Fire Worshippers");
                    _fireWorshipperIds.Add(party.StringId);
                    InjectCustomTroops(party, "fire_devotee", 2 + _rng.Next(4));
                }
                else if (pick == 1)
                {
                    TryRenameParty(party, "Ashen Spawn");
                    _ashenSpawnIds.Add(party.StringId);
                    InjectCustomTroops(party, "ashen_thrall", 3 + _rng.Next(5));
                }
                else
                {
                    TryRenameParty(party, "Wandering Circle");
                    _wanderingCircleIds.Add(party.StringId);
                    InjectCustomTroops(party, "circle_acolyte", 2 + _rng.Next(4));
                    InjectCustomTroops(party, "circle_druid",   1 + _rng.Next(2));
                }
            }
            catch { }
        }

        private static void InjectCustomTroops(MobileParty party, string troopId, int count)
        {
            try
            {
                TaleWorlds.Core.CharacterObject troop = null;
                foreach (var c in TaleWorlds.Core.CharacterObject.All)
                {
                    if (c.StringId == troopId) { troop = c; break; }
                }
                if (troop == null) return;
                party.MemberRoster.AddToCounts(troop, count);
            }
            catch { }
        }

        private static void TryRenameParty(MobileParty party, string name)
        {
            try { party.Party.SetCustomName(new TextObject(name)); }
            catch { }
        }

        private static bool ContainsTroop(MobileParty party, HashSet<string> troopIds)
        {
            try
            {
                foreach (var entry in party.MemberRoster.GetTroopRoster())
                    if (entry.Character != null && troopIds.Contains(entry.Character.StringId))
                        return true;
            }
            catch { }
            return false;
        }

        // ── Event-spawned party registration ─────────────────────────────────
        // Called by CampaignMapEvents.SpawnAshenSpawnParty() to register and
        // rename parties that were created programmatically (not via the normal
        // bandit-spawn hook) so that IsAshenSpawn() returns true for them.
        public static void ForceMarkAsAshenSpawn(MobileParty party)
        {
            if (party == null) return;
            TryRenameParty(party, "Ashen Spawn");
            _ashenSpawnIds.Add(party.StringId);
            InjectCustomTroops(party, "ashen_thrall",  3 + _rng.Next(6));
            InjectCustomTroops(party, "ashen_invoker", 1 + _rng.Next(3));
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(TaleWorlds.CampaignSystem.IDataStore store)
        {
            var fw  = _fireWorshipperIds.ToList();
            var as_ = _ashenSpawnIds.ToList();
            var wc  = _wanderingCircleIds.ToList();
            store.SyncData("LDM_FireWorshippers",   ref fw);
            store.SyncData("LDM_AshenSpawnIds",     ref as_);
            store.SyncData("LDM_WanderingCircleIds", ref wc);

            _fireWorshipperIds.Clear();
            if (fw  != null) foreach (var id in fw)  _fireWorshipperIds.Add(id);
            _ashenSpawnIds.Clear();
            if (as_ != null) foreach (var id in as_) _ashenSpawnIds.Add(id);
            _wanderingCircleIds.Clear();
            if (wc  != null) foreach (var id in wc)  _wanderingCircleIds.Add(id);
        }
    }
}
