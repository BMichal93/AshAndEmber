// =============================================================================
// ASH AND EMBER — AshenCitySystem.Setup.cs
// Kingdom/clan creation, Ashen personality and hero conversion.
// Partial of AshenCitySystem (shared static state lives in AshenCitySystem.cs).
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
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public partial class AshenCitySystem
    {
        // ── Initialization ────────────────────────────────────────────────────
        public static void Initialize()
        {
            if (_initialized) return;

            bool foundAny = false;
            Settlement homeSettlement = null;

            // First pass: process settlements whose clans exclusively own Ashen settlements.
            foreach (string name in _targetSettlementNames)
            {
                try
                {
                    var settlement = Settlement.All.FirstOrDefault(s =>
                        s.Name.ToString().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (settlement == null) continue;

                    Clan clan = settlement.OwnerClan;
                    if (clan == null) continue;

                    // Skip clans that own settlements outside our target list —
                    // those extra settlements (Vercheng, Balagad, etc.) must not be dragged in.
                    bool hasNonTarget = clan.Settlements.Any(s =>
                        (s.IsTown || s.IsCastle) &&
                        !_targetSettlementNames.Any(n =>
                            s.Name.ToString().IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0));
                    if (hasNonTarget) continue;

                    if (homeSettlement == null) homeSettlement = settlement;
                    if (_ashenKingdom == null) CreateOrFindAshenKingdom(homeSettlement);

                    if (_ashenClanIds.Add(clan.StringId))
                    {
                        _settlementClanMap[settlement.StringId] = clan.StringId;
                        MarkClanAshen(clan, settlement);
                        foundAny = true;
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // Second pass: any target settlement whose clan failed the check above is still
            // claimed — transfer it directly to the first Ashen clan so it doesn't float free
            // and get snatched by another faction.
            if (_ashenClanIds.Count > 0)
            {
                var ashenClan = Clan.All.FirstOrDefault(c => _ashenClanIds.Contains(c.StringId) && !c.IsEliminated);
                if (ashenClan != null)
                {
                    Hero ashenLord = ashenClan.Leader
                                  ?? ashenClan.Heroes.FirstOrDefault(h => h.IsAlive && !h.IsDisabled);

                    foreach (string name in _targetSettlementNames)
                    {
                        try
                        {
                            var settlement = Settlement.All.FirstOrDefault(s =>
                                s.Name.ToString().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (settlement == null) continue;
                            if (_settlementClanMap.ContainsKey(settlement.StringId)) continue;

                            _settlementClanMap[settlement.StringId] = ashenClan.StringId;
                            if (ashenLord != null)
                            {
                                try { ChangeOwnerOfSettlementAction.ApplyByDefault(ashenLord, settlement); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                try { if (settlement.Town != null) { settlement.Town.Loyalty = 100f; settlement.Town.Security = 100f; } } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            }
                            try { EnsureGarrison(settlement); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            foundAny = true;
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }
            }

            if (foundAny || _ashenKingdom != null)
            {
                try { DeclareWarWithAllKingdoms(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { ApplyAshenLookToSettlementHeroes(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _initialized = true;
            }
        }

        // ── Kingdom creation ──────────────────────────────────────────────────
        private static void CreateOrFindAshenKingdom(Settlement homeSettlement)
        {
            _ashenKingdom = Kingdom.All.FirstOrDefault(k => k.StringId == AshenKingdomId);
            if (_ashenKingdom != null) return;
            try
            {
                _ashenKingdom = Kingdom.CreateKingdom(AshenKingdomId);
                // Always use Sturgian culture so all Ashen cities share Tyal's visual theme.
                var culture = MBObjectManager.Instance.GetObject<CultureObject>("sturgia")
                           ?? homeSettlement.OwnerClan?.Culture;
                _ashenKingdom.InitializeKingdom(
                    new TextObject("The Ashen"),
                    new TextObject("Ashen"),
                    culture,
                    Banner.CreateRandomBanner(),
                    0xFF1E1E1E,    // ash black
                    0xFF3D1A1A,    // dark ember red
                    homeSettlement,
                    new TextObject("Ancient fire-lords who neither age nor die."),
                    new TextObject("The Ashen"),
                    new TextObject("Ashen Sovereign")
                );
            }
            catch { _ashenKingdom = null; }
        }

        // Sets the three personality traits that define every Ashen lord:
        // Merciless (Mercy −2), Closefisted (Generosity −2), Deceitful (Honor −2).
        // Called whenever any hero joins the Ashen — including via events.
        public static void ApplyAshenPersonality(Hero hero)
        {
            if (hero == null) return;
            try { hero.SetTraitLevel(DefaultTraits.Mercy,      -2); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { hero.SetTraitLevel(DefaultTraits.Generosity, -2); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { hero.SetTraitLevel(DefaultTraits.Honor,      -2); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Called by ColourLordRegistry.SetAshen for every hero turned Ashen ──
        // Moves the hero's clan into the Ashen kingdom. Safe to call at any time:
        // if the kingdom isn't ready yet the clan is simply ejected (Initialize
        // will re-join them on its next run).
        public static void OnHeroSetAshen(Hero hero)
        {
            var clan = hero?.Clan;
            // Never move the PLAYER'S clan into the cold because ONE of its members
            // (a child taken by the cold, an aged companion mage) turned Ashen —
            // that would eject the player's whole clan from its kingdom behind their
            // back. The player's own conversion is handled elsewhere; callers that
            // convert a player-clan member relocate that hero individually (e.g. the
            // "child of the cold" encounter moves the child to an Ashen clan itself).
            if (clan == null || hero == Hero.MainHero || clan == Clan.PlayerClan) return;
            try { ApplyAshenPersonality(hero); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try
            {
                if (_ashenKingdom == null)
                {
                    // Kingdom not created yet — just eject; daily tick will re-add later
                    if (clan.Kingdom != null)
                        try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    return;
                }

                if (clan.Kingdom?.StringId == AshenKingdomId) return; // already home

                // Eject from current kingdom first
                if (clan.Kingdom != null)
                    try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // Use ApplyByCreateKingdom only if the Ashen kingdom has no ruling clan yet;
                // this establishes the first clan as ruler so the kingdom is properly set up.
                bool needsRuler = _ashenKingdom.RulingClan == null;
                if (needsRuler)
                    try { ChangeKingdomAction.ApplyByCreateKingdom(clan, _ashenKingdom, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                else
                    try { ChangeKingdomAction.ApplyByJoinToKingdom(
                            clan, _ashenKingdom,
                            CampaignTime.Now + CampaignTime.Years(1000),
                            false); }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Clan setup ────────────────────────────────────────────────────────
        // SetAshen → OnHeroSetAshen handles kingdom placement; this method only
        // handles marking/renaming, settlement ownership, gold, and garrison.
        private static void MarkClanAshen(Clan clan, Settlement settlement)
        {
            if (clan == null) return;
            try
            {
                // Mark and rename — SetAshen calls OnHeroSetAshen which joins the Ashen kingdom
                foreach (Hero hero in clan.Heroes.Where(h => h.IsAlive).ToList())
                {
                    // Ashen lords should hold their cold thrones, not start in chains —
                    // free any who were captive when the cold claimed them.
                    if (hero.IsPrisoner)
                        try { EndCaptivityAction.ApplyByReleasedAfterBattle(hero); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { ColourLordRegistry.SetAshen(hero, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { RenameAshenHero(hero); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { ColourLordRegistry.SetMage(hero, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                // Re-assert settlement ownership (guards against fief-distribution firing in the gap)
                if (settlement != null)
                {
                    Hero lord = clan.Leader
                             ?? clan.Heroes.FirstOrDefault(h => h.IsAlive && !h.IsDisabled);
                    if (lord != null)
                    {
                        try { ChangeOwnerOfSettlementAction.ApplyByDefault(lord, settlement); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        try { if (settlement.Town != null) { settlement.Town.Loyalty = 100f; settlement.Town.Security = 100f; } } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }

                // Starting gold
                foreach (Hero hero in clan.Heroes.Where(h => h.IsAlive).ToList())
                    try { if (hero.Gold < MinHeroGold) hero.ChangeHeroGold(MinHeroGold - hero.Gold); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // Garrison boost
                if (settlement != null) try { EnsureGarrison(settlement); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void RenameAshenHero(Hero hero)
        {
            if (hero == null) return;
            string title = hero.IsFemale ? "Ashen Lady" : "Ashen Lord";
            try
            {
                hero.SetName(new TextObject(title + " " + hero.Name.ToString()),
                             new TextObject(title));
            }
            catch
            {
                try { hero.SetName(new TextObject(title), new TextObject(title)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        private static void MaxRelationsWithPlayer(Hero hero)
        {
            if (hero == null || Hero.MainHero == null) return;
            try
            {
                int cur = CharacterRelationManager.GetHeroRelation(Hero.MainHero, hero);
                if (cur < 100)
                    // SetHeroRelation directly instead of ApplyRelationChangeBetweenHeroes to
                    // avoid awarding charm XP, which otherwise causes the player to jump to
                    // level 34 immediately when starting as Ashen (one +100 per Ashen lord).
                    CharacterRelationManager.SetHeroRelation(Hero.MainHero, hero, 100);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
