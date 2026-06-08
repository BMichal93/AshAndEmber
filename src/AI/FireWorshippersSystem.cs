// =============================================================================
// LIFE & DEATH MAGIC — AI/FireWorshippersSystem.cs
// Renames ~10% of newly created Looter / forest_bandit parties to
// "Fire Worshippers" and ~10% of sea_raider / mountain_bandit parties to
// "Ashen Spawn". Tracked parties are guaranteed at least one bandit mage.
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

        private static readonly HashSet<string> _fireWorshipperIds = new HashSet<string>();
        private static readonly HashSet<string> _ashenSpawnIds     = new HashSet<string>();
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

        public static bool IsFireWorshipper(MobileParty party) =>
            party != null && _fireWorshipperIds.Contains(party.StringId);

        public static bool IsAshenSpawn(MobileParty party) =>
            party != null && _ashenSpawnIds.Contains(party.StringId);

        public static bool IsSpecialParty(MobileParty party) =>
            IsFireWorshipper(party) || IsAshenSpawn(party);

        // ── Hook: called when any mobile party is created ─────────────────────
        public static void OnPartyCreated(MobileParty party)
        {
            if (party == null || !party.IsActive) return;
            try
            {
                bool isFireCategory  = ContainsFireTroop(party);
                bool isAshenCategory = ContainsAshenTroop(party);

                if (!isFireCategory && !isAshenCategory) return;
                if (_rng.NextDouble() >= RenameChance) return;

                if (isFireCategory && (!isAshenCategory || _rng.Next(2) == 0))
                {
                    TryRenameParty(party, "Fire Worshippers");
                    _fireWorshipperIds.Add(party.StringId);
                    InjectCustomTroops(party, "fire_devotee", 2 + _rng.Next(4));
                }
                else if (isAshenCategory)
                {
                    TryRenameParty(party, "Ashen Spawn");
                    _ashenSpawnIds.Add(party.StringId);
                    InjectCustomTroops(party, "ashen_thrall", 3 + _rng.Next(5));
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

        private static bool ContainsFireTroop(MobileParty party)
        {
            try
            {
                foreach (var entry in party.MemberRoster.GetTroopRoster())
                    if (entry.Character != null && _fireWorshipperTroops.Contains(entry.Character.StringId))
                        return true;
            }
            catch { }
            return false;
        }

        private static bool ContainsAshenTroop(MobileParty party)
        {
            try
            {
                foreach (var entry in party.MemberRoster.GetTroopRoster())
                    if (entry.Character != null && _ashenSpawnTroops.Contains(entry.Character.StringId))
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
            var fw = _fireWorshipperIds.ToList();
            var as_ = _ashenSpawnIds.ToList();
            store.SyncData("LDM_FireWorshippers", ref fw);
            store.SyncData("LDM_AshenSpawnIds",   ref as_);
            _fireWorshipperIds.Clear();
            if (fw != null) foreach (var id in fw) _fireWorshipperIds.Add(id);
            _ashenSpawnIds.Clear();
            if (as_ != null) foreach (var id in as_) _ashenSpawnIds.Add(id);
        }
    }
}
