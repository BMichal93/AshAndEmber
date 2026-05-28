// =============================================================================
// LIFE & DEATH MAGIC — AI/AshenCitySystem.cs
// Manages the Ashen city clans: Tyal, Sibir (Sturgia), Baltakhand (Khuzait).
//
// On initialization:
//   1. Finds each target settlement by name and looks up its owner clan.
//   2. Removes the clan from its kingdom.
//   3. Marks all clan heroes as Ashen (no aging, kicked from kingdoms).
//   4. Renames heroes "Ashen Lord" / "Ashen Lady".
//   5. Maximises player relations with every marked hero.
//
// Daily maintenance:
//   - Resets each Ashen hero's BirthDay to block natural aging.
//
// Event hook (ClanChangedKingdom):
//   - If an Ashen clan attempts to rejoin a kingdom, eject it immediately.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static class AshenCitySystem
    {
        private static readonly HashSet<string> _ashenClanIds = new HashSet<string>();
        private static bool _initialized = false;

        // Settlement names that belong to Ashen clans
        private static readonly string[] _targetSettlementNames =
        {
            "Tyal", "Sibir", "Baltakhand",
        };

        // ── Initialization ────────────────────────────────────────────────────
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                foreach (string name in _targetSettlementNames)
                {
                    var settlement = Settlement.All.FirstOrDefault(s =>
                        s.Name.ToString().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (settlement == null) continue;

                    Clan clan = settlement.OwnerClan;
                    if (clan == null) continue;

                    _ashenClanIds.Add(clan.StringId);
                    MarkClanAshen(clan);
                }
            }
            catch { }
        }

        private static void MarkClanAshen(Clan clan)
        {
            if (clan == null) return;
            try
            {
                // Leave kingdom
                if (clan.Kingdom != null)
                    try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch { }

                // Mark and rename every hero in the clan
                foreach (Hero hero in clan.Heroes.Where(h => h.IsAlive).ToList())
                {
                    try { ColourLordRegistry.SetAshen(hero, true); } catch { }
                    try { RenameAshenHero(hero); } catch { }
                    try { MaxRelationsWithPlayer(hero); } catch { }
                    // Ensure they're also mage lords
                    try { ColourLordRegistry.SetMage(hero, true); } catch { }
                }
            }
            catch { }
        }

        private static void RenameAshenHero(Hero hero)
        {
            if (hero == null) return;
            bool female = hero.IsFemale;
            string title = female ? "Ashen Lady" : "Ashen Lord";
            try
            {
                hero.SetName(new TextObject(title + " " + hero.Name.ToString()), new TextObject(title));
            }
            catch
            {
                try { hero.SetName(new TextObject(title), new TextObject(title)); } catch { }
            }
        }

        private static void MaxRelationsWithPlayer(Hero hero)
        {
            if (hero == null || Hero.MainHero == null) return;
            try
            {
                int current = CharacterRelationManager.GetHeroRelation(Hero.MainHero, hero);
                if (current < 100)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, hero, 100 - current, false);
            }
            catch { }
        }

        // ── Daily maintenance — block aging via BirthDay reset ────────────────
        public static void DailyTick()
        {
            if (_ashenClanIds.Count == 0) return;
            bool playerIsAshen = MageKnowledge.IsAshen;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (!IsAshenClanMember(h)) continue;
                    if (!h.IsAlive) continue;
                    // Keep age at 35 by resetting birth day
                    float targetAge = 35f;
                    float currentAge = h.Age;
                    if (currentAge > targetAge + 0.5f)
                    {
                        float excessDays = (currentAge - targetAge) * 365f;
                        try { h.SetBirthDay(h.BirthDay + CampaignTime.Days(excessDays)); } catch { }
                    }
                    // Maintain max relations once player is Ashen
                    if (playerIsAshen)
                        MaxRelationsWithPlayer(h);
                }
            }
            catch { }
        }

        // ── Kingdom rejoin prevention ─────────────────────────────────────────
        // Called from CampaignBehavior.OnClanChangedKingdom
        public static void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            if (newKingdom == null) return;
            if (clan == null || !_ashenClanIds.Contains(clan.StringId)) return;
            try
            {
                ChangeKingdomAction.ApplyByLeaveKingdom(clan, false);
            }
            catch { }
        }

        // ── Max relations for all Ashen city lords when player goes Ashen ───
        public static void OnPlayerBecameAshen()
        {
            if (Hero.MainHero == null) return;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (IsAshenClanMember(h))
                        MaxRelationsWithPlayer(h);
                }
            }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        public static bool IsAshenClanMember(Hero hero)
        {
            if (hero == null || hero.Clan == null) return false;
            return _ashenClanIds.Contains(hero.Clan.StringId);
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var ids = _ashenClanIds.ToList();
            bool init = _initialized;
            store.SyncData("LDM_AshenClanIds", ref ids);
            store.SyncData("LDM_AshenCityInit", ref init);
            _ashenClanIds.Clear();
            if (ids != null) foreach (var id in ids) _ashenClanIds.Add(id);
            _initialized = init;
        }
    }
}
