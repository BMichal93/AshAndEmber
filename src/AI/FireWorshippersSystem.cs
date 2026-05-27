// =============================================================================
// LIFE & DEATH MAGIC — AI/FireWorshippersSystem.cs
// Renames ~10% of newly created Looter / forest_bandit parties to
// "Fire Worshippers" and ~10% of sea_raider / mountain_bandit parties to
// "Ashen Spawn". Tracked parties are guaranteed at least one bandit mage.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem.Party;
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
                }
                else if (isAshenCategory)
                {
                    TryRenameParty(party, "Ashen Spawn");
                    _ashenSpawnIds.Add(party.StringId);
                }
            }
            catch { }
        }

        private static readonly MethodInfo _setCustomName = typeof(MobileParty)
            .GetMethod("SetCustomName", BindingFlags.Public | BindingFlags.Instance);
        private static readonly FieldInfo _nameField = typeof(MobileParty)
            .GetField("_name", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void TryRenameParty(MobileParty party, string name)
        {
            var txt = new TextObject(name);
            try
            {
                if (_setCustomName != null) { _setCustomName.Invoke(party, new object[] { txt }); return; }
                _nameField?.SetValue(party, txt);
            }
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
