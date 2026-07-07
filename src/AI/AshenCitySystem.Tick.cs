// =============================================================================
// ASH AND EMBER — AshenCitySystem.Tick.cs
// Daily tick and kingdom-membership reactions.
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
        // Applies the per-session display renames (Ashen settlements, Holy Temple
        // and Tribes kingdoms, their troops) exactly once per session. Names come
        // from game XML on every load, so this must run on both new games and
        // reloads. Driven from OnSessionLaunched (so the map shows correct names the
        // instant it loads) and again from the first DailyTick as a safety net for
        // the case where the Ashen clans were not yet established at launch.
        public static void EnsureSessionRenames()
        {
            if (DragonQuestSystem.WorldRekindled) return;  // the Ashen no longer exist

            // The kingdom / culture / troop renames rebrand the four vanilla realms
            // (Holy Temple, Tribes, Northmen, Duneborn) and are INDEPENDENT of whether
            // the Ashen have risen yet — so they must run every session even before any
            // Ashen clan exists. Otherwise, early in a campaign, the Aserai kingdom (and
            // the others) would still show their vanilla names in menus and war text.
            EnsureKingdomRenames();

            // The Ashen settlement rename genuinely needs the Ashen clans to exist.
            if (_settlementsRenamed) return;
            if (_ashenClanIds.Count == 0) return;          // clans not established yet
            try { RenameAshenSettlements();          } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            _settlementsRenamed = true;
        }

        // Rebrands the four vanilla kingdoms and their troops (Holy Temple, Tribes,
        // Northmen, Duneborn) once per session. Kept separate from the Ashen-gated
        // settlement rename so the realm names are correct from the first frame, even
        // in a game where the Ashen have not yet risen. Idempotent; the individual
        // rename helpers no-op when already applied.
        public static void EnsureKingdomRenames()
        {
            if (_kingdomsRenamed) return;
            if (DragonQuestSystem.WorldRekindled) return;
            try { RenameHolyTempleKingdom();         } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { RenameVlandianTroops();             } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { TempleCulture.SetupTempleKingdom(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { RenameTribesKingdom();              } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { RenameKhuzaitTroops();              } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { RenameNorthmenKingdom();            } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { RenameSturgianTroops();             } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { RenameDunebornKingdom();            } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { RenameAseraiTroops();               } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { RenameForestClansKingdom();         } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { RenameBattanianTroops();            } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            _kingdomsRenamed = true;
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        public static void DailyTick()
        {
            if (_ashenClanIds.Count == 0) return;
            // Once the world is rekindled, the Ashen cease to exist.
            if (DragonQuestSystem.WorldRekindled) return;

            // Undo any wrongly-converted ruling clan BEFORE the war / clan-eject logic,
            // or that logic would keep dragging it back into the cold. No-op when healthy.
            HealMisconvertedClans();
            if (_ashenClanIds.Count == 0) return;

            EnsureKingdomAlive();

            // Apply Ashen settlement names and Holy Temple kingdom rename on first
            // tick each session (also driven earlier from OnSessionLaunched so the
            // map shows the correct names the instant it loads — see EnsureSessionRenames).
            EnsureSessionRenames();

            // Decrement throttle counters (only when above zero)
            if (_clanThrottle      > 0) _clanThrottle--;
            if (_warThrottle       > 0) _warThrottle--;
            if (_villageThrottle   > 0) _villageThrottle--;
            if (_recoveryThrottle  > 0) _recoveryThrottle--;
            if (_prisonerThrottle  > 0) _prisonerThrottle--;
            if (_lordPartyThrottle > 0) _lordPartyThrottle--;

            // Clan kingdom enforcement — every ClanInterval days
            if (_clanThrottle == 0)
            {
                TickAshenClanKingdoms();
                _clanThrottle = ClanInterval;
            }

            // War declarations — every WarInterval days
            if (_warThrottle == 0)
            {
                DeclareWarWithAllKingdoms();
                _warThrottle = WarInterval;
            }

            // One-time ownership initialisation
            if (!_ownershipInitDone)
            {
                try { InitialiseSettlementOwnership(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _ownershipInitDone = true;
            }

            // Keep the cold confined to its set — hand back any ordinary town the
            // Ashen have seized (e.g. frontier towns near Ostican).
            ReleaseNonTargetSettlements();

            // Fast daily ops (idempotent, low cost)
            RefillGarrisons();
            RefillHeroGold();
            MaintainAshenTownHealth();
            MaintainCriminalStatus();
            ExecuteSettlementCivilians();

            // Settlement recovery — every RecoveryInterval days, max 1 change per tick
            if (_recoveryThrottle == 0)
            {
                TickSettlementRecovery();
                _recoveryThrottle = RecoveryInterval;
            }

            // Village loot state — every VillageInterval days
            if (_villageThrottle == 0)
            {
                TickAshenVillages();
                _villageThrottle = VillageInterval;
            }

            // Lord party composition — every LordPartyInterval days, one party per tick
            if (_lordPartyThrottle == 0)
            {
                try { RefillAshenLordParties(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _lordPartyThrottle = LordPartyInterval;
            }

            // Prisoner fate — every PrisonerInterval days, max 1 action per tick
            if (_prisonerThrottle == 0)
            {
                ExecuteAshenPrisoners();
                _prisonerThrottle = PrisonerInterval;
            }

            if (++_appearanceDayCounter >= AppearanceTickInterval)
            {
                _appearanceDayCounter = 0;
                ApplyAshenLookToSettlementHeroes();
            }

            bool playerIsAshen = MageKnowledge.IsAshen;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (!IsAshenClanMember(h)) continue;
                    if (!h.IsAlive) continue;
                    // Keep age at 35
                    float targetAge = 35f;
                    float currentAge = h.Age;
                    if (currentAge > targetAge + 0.5f)
                    {
                        float excessDays = (currentAge - targetAge) * 365f;
                        try { h.SetBirthDay(h.BirthDay + CampaignTime.Days(excessDays)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                    if (playerIsAshen)
                        MaxRelationsWithPlayer(h);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Kingdom rejoin prevention ─────────────────────────────────────────
        // Intentionally empty: calling ApplyByLeaveKingdom here would fire the
        // ClanChangedKingdom event again (re-entrancy). Ejection is handled
        // instead by TickAshenClanKingdoms(), which runs on the daily tick.
        public static void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification) { }

        // ── Max relations + kingdom join when player goes Ashen ──────────────────
        public static void OnPlayerBecameAshen()
        {
            if (Hero.MainHero == null) return;

            // Move player clan into the Ashen kingdom
            try
            {
                EnsureKingdomAlive();
                var clan = Hero.MainHero.Clan;
                if (clan != null && _ashenKingdom != null)
                {
                    if (clan.Kingdom != null && clan.Kingdom != _ashenKingdom)
                        try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                    if (clan.Kingdom?.StringId != AshenKingdomId)
                    {
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
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Max relations with all Ashen lords
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                    if (IsAshenClanMember(h))
                        MaxRelationsWithPlayer(h);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
