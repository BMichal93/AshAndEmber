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
            if (_settlementsRenamed) return;
            if (_ashenClanIds.Count == 0) return;          // clans not established yet
            if (DragonQuestSystem.WorldRekindled) return;  // the Ashen no longer exist
            try { RenameAshenSettlements();          } catch { }
            try { RenameHolyTempleKingdom();         } catch { }
            try { RenameVlandianTroops();             } catch { }
            try { TempleCulture.SetupTempleKingdom(); } catch { }
            try { RenameTribesKingdom();              } catch { }
            try { RenameKhuzaitTroops();              } catch { }
            try { RenameSturgianTroops();             } catch { }
            try { RenameAseraiTroops();               } catch { }
            _settlementsRenamed = true;
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        public static void DailyTick()
        {
            if (_ashenClanIds.Count == 0) return;
            // Once the world is rekindled, the Ashen cease to exist.
            if (DragonQuestSystem.WorldRekindled) return;

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
                try { InitialiseSettlementOwnership(); } catch { }
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
                try { RefillAshenLordParties(); } catch { }
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
                        try { h.SetBirthDay(h.BirthDay + CampaignTime.Days(excessDays)); } catch { }
                    }
                    if (playerIsAshen)
                        MaxRelationsWithPlayer(h);
                }
            }
            catch { }
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
                        try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch { }

                    if (clan.Kingdom?.StringId != AshenKingdomId)
                    {
                        bool needsRuler = _ashenKingdom.RulingClan == null;
                        if (needsRuler)
                            try { ChangeKingdomAction.ApplyByCreateKingdom(clan, _ashenKingdom, false); } catch { }
                        else
                            try { ChangeKingdomAction.ApplyByJoinToKingdom(
                                    clan, _ashenKingdom,
                                    CampaignTime.Now + CampaignTime.Years(1000),
                                    false); }
                            catch { }
                    }
                }
            }
            catch { }

            // Max relations with all Ashen lords
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                    if (IsAshenClanMember(h))
                        MaxRelationsWithPlayer(h);
            }
            catch { }
        }
    }
}
