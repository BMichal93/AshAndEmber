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
            // Located by StringId, never by display name: EnsureSessionRenames may already
            // have turned "Tyal" into "The Heart of Winter" by the time we run (it fires on
            // OnSessionLaunched, we fire on the Gift prompt / first daily tick), and a name
            // lookup then finds nothing at all — the realm silently never rises.
            foreach (string id in _targetSettlementIds)
            {
                try
                {
                    var settlement = Settlement.Find(id);
                    if (settlement == null) continue;

                    Clan clan = settlement.OwnerClan;
                    if (clan == null) continue;

                    // Never convert a clan that plainly belongs elsewhere: a kingdom's
                    // ruling clan, or a clan whose ancestral seat is an ordinary non-Ashen
                    // town (e.g. Raganvad, who rules Sturgia from Varcheg yet also holds
                    // Mazhadan Castle — a target). Converting such a clan drags its capital
                    // towns into the cold and, on a reload, rips a faction's ruler into the
                    // permanently-warring Ashen — the grey-Varcheg / "Raganvad declares war
                    // on everyone" bug. Both are stable identity signals, independent of the
                    // fragile holdings-enumeration timing below; the one target settlement
                    // such a clan holds is still claimed on its own by the second pass.
                    if (BelongsOutsideTheCold(clan)) continue;

                    // Only a clan that holds NOTHING but target settlements may be
                    // turned wholly Ashen. Skip otherwise, for two reasons:
                    //   • the clan also holds ordinary towns (e.g. the Khuzait ruling
                    //     clan holds Tepes Castle AND Makeb + Chaikand) — converting it
                    //     drags its Tribes cities into the cold; and
                    //   • the holdings list is not yet populated on this call. Reading
                    //     an empty list as "pure target" (Any()==false) was wrongly
                    //     converting the whole clan — the day-1 grey-cities bug.
                    // In both cases the target settlement is still claimed on its own by
                    // the second pass below; we simply never convert the whole clan.
                    var clanHoldings = clan.Settlements
                        .Where(s => s.IsTown || s.IsCastle).ToList();
                    if (clanHoldings.Count == 0) continue;   // holdings not ready — do not risk it
                    bool hasNonTarget = clanHoldings.Any(s => !IsTargetSettlementId(s.StringId));
                    if (hasNonTarget) continue;

                    if (homeSettlement == null) homeSettlement = settlement;
                    if (_ashenKingdom == null) CreateOrFindAshenKingdom(homeSettlement);

                    bool newlyAshen = _ashenClanIds.Add(clan.StringId);
                    // Claim the settlement for its own clan whether or not the clan was
                    // added on THIS target — a clan holding two of them (Vagiroving hold
                    // both Tyal and Urikskala) would otherwise leave the second unclaimed,
                    // and the second pass below would hand it to an unrelated Ashen clan.
                    _settlementClanMap[settlement.StringId] = clan.StringId;
                    if (newlyAshen)
                    {
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

                    foreach (string id in _targetSettlementIds)
                    {
                        try
                        {
                            var settlement = Settlement.Find(id);
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
                // Belt-and-suspenders: immediately hand back any non-target town the
                // setup left in Ashen hands, so the player never opens the map to a
                // Tribes city (Makeb, Chaikand, …) wrongly greyed. The daily tick also
                // enforces this, but that first fires only after a day has passed —
                // running it here fixes the day-1 view.
                try { ReleaseNonTargetSettlements(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _initialized = true;
            }
        }

        // True if the clan is the ruling clan of a living, non-Ashen kingdom.
        private static bool IsRulingClanOfNonAshenKingdom(Clan clan)
        {
            try
            {
                if (clan == null) return false;
                var k = clan.Kingdom;
                if (k == null || k.IsEliminated) return false;
                if (k.StringId == AshenKingdomId) return false;
                return k.RulingClan == clan;
            }
            catch { return false; }
        }

        // True for a clan that must never be swept wholesale into the Ashen: a kingdom's
        // ruling clan, OR a clan whose ancestral seat (InitialHomeSettlement) is an
        // ordinary, non-Ashen town/castle. Every rightful Ashen clan is seated in a target
        // city (Tyal, Sibir, Omor, Varnovapol …); Raganvad's seat is Varcheg and Monchug's
        // is Makeb, so both are excluded here regardless of the target castle each also
        // holds (Mazhadan, Tepes) — otherwise their capitals (Varcheg/Balgard,
        // Makeb/Chaikand) are dragged into the cold with them. Matched on the seat's
        // StringId: a stable identity signal, independent of both the display rename and
        // holdings-enumeration timing.
        private static bool BelongsOutsideTheCold(Clan clan)
        {
            if (clan == null) return false;
            if (IsRulingClanOfNonAshenKingdom(clan)) return true;
            try
            {
                var home = clan.InitialHomeSettlement;
                if (home != null && (home.IsTown || home.IsCastle)
                    && !IsTargetSettlementId(home.StringId))
                    return true;
            }
            catch { }
            return false;
        }

        // ── Self-heal: undo a wrongly-converted clan ──────────────────────────
        // Frees any clan an earlier build swept into the Ashen that plainly belongs
        // outside it — keyed on the STABLE ancestral seat, so it catches Raganvad even
        // though he still holds Mazhadan Castle (a target), and it never fires on a
        // rightful Ashen clan (whose seat is always a target city) no matter what it has
        // conquered or lost. A strict no-op on a healthy save. Called early in DailyTick,
        // before the war / clan-eject logic that would otherwise keep dragging it back.
        internal static void HealMisconvertedClans()
        {
            if (_ashenClanIds.Count == 0) return;
            foreach (string clanId in _ashenClanIds.ToList())
            {
                try
                {
                    var clan = Clan.All.FirstOrDefault(c => c.StringId == clanId);
                    if (clan == null || clan.IsEliminated) continue;

                    // Only act on the stable-seat signal. If the seat is unknown, never risk it.
                    var home = clan.InitialHomeSettlement;
                    if (home == null || !(home.IsTown || home.IsCastle)) continue;
                    if (IsTargetSettlementId(home.StringId)) continue;   // rightful Ashen clan — leave alone

                    // Wrongly converted. Remove from the cold's rolls first so the heir search
                    // and the eject/rejoin logic no longer count this clan as Ashen.
                    _ashenClanIds.Remove(clanId);

                    // Keep any RIGHTFUL Ashen ground it was handed (e.g. Mazhadan Castle) in the
                    // cold's hands by passing it to a remaining Ashen clan; drop the rest of its
                    // rows so its own capital towns revert with it.
                    var ashenHeir = Clan.All.FirstOrDefault(c => _ashenClanIds.Contains(c.StringId) && !c.IsEliminated);
                    var heirLord  = ashenHeir?.Leader ?? ashenHeir?.Heroes.FirstOrDefault(h => h.IsAlive && !h.IsDisabled);
                    foreach (var s in clan.Settlements.Where(x => (x.IsTown || x.IsCastle) && IsTargetSettlement(x)).ToList())
                    {
                        if (heirLord != null && !s.IsUnderSiege)
                        {
                            try { ChangeOwnerOfSettlementAction.ApplyByDefault(heirLord, s); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            _settlementClanMap[s.StringId] = ashenHeir.StringId;
                        }
                        else _settlementClanMap.Remove(s.StringId);
                    }
                    foreach (var kvp in _settlementClanMap.Where(k => k.Value == clanId).ToList())
                        _settlementClanMap.Remove(kvp.Key);

                    foreach (Hero h in clan.Heroes.Where(h => h.IsAlive).ToList())
                        try { ColourLordRegistry.SetAshen(h, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                    // Return the clan to a living kingdom of its own culture so its towns fly a
                    // banner again instead of the grey of an independent clan trapped in the
                    // Ashen's endless war. Independence is still better than Ashen if none exists.
                    RestoreClanToCultureKingdom(clan);

                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{clan.Name} — the cold's false hold breaks. They were never truly Ashen.",
                        new Color(0.6f, 0.7f, 0.85f)));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        private static void RestoreClanToCultureKingdom(Clan clan)
        {
            try
            {
                if (clan == null) return;
                Kingdom old = clan.Kingdom;
                if (old != null && old.StringId != AshenKingdomId) return; // already re-homed elsewhere

                var home = Kingdom.All.FirstOrDefault(k => !k.IsEliminated
                    && k.StringId != AshenKingdomId && k.Culture == clan.Culture && k.RulingClan != null);
                var stay = CampaignTime.Now + CampaignTime.Years(1000);

                if (home == null)
                {
                    // No living home realm of its culture — at least free it from the cold.
                    if (old != null)
                        try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    return;
                }

                // Prefer the atomic defection path so the clan's towns change banners cleanly
                // instead of flashing through an ownerless grey state (mirrors MoveClanInto).
                try
                {
                    if (old != null && !old.IsEliminated)
                        ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, old, home, stay, false);
                    else
                        ChangeKingdomAction.ApplyByJoinToKingdom(clan, home, stay, false);
                }
                catch
                {
                    try { if (clan.Kingdom != null) ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { ChangeKingdomAction.ApplyByJoinToKingdom(clan, home, stay, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
