// =============================================================================
// ASH AND EMBER — AI/TribalCulture.cs
// The Tribal (formerly Khuzait) culture's mechanics for the player.
//
//   War Fever          — tribal warriors maintain high morale; standing floor of
//                        +15 RecentEventsMorale for all player-clan parties.
//   Spoils of the Raid — a successful village raid yields bonus gold on top of
//                        the normal loot (50–150 depending on party size).
//   No Quarter         — handled in TribalKingdomBehavior.OnMakePeace; surfaced
//                        here as a readable property for condition checks.
//
// The daily morale effect is applied via DailyTick(), called from
// MagicCampaignBehavior. The raid bonus is applied via CheckRaidBonus(),
// called from MagicCampaignBehavior.OnMapEventEnded.
// =============================================================================

using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    internal static class TribalCulture
    {
        public static bool IsPlayerTribal
        {
            get
            {
                try { return Hero.MainHero?.Culture?.StringId == "khuzait"; }
                catch { return false; }
            }
        }

        // ── War Fever — daily morale floor ────────────────────────────────────
        private const float WarFeverMoraleFloor = 15f;

        public static void DailyTick()
        {
            if (!IsPlayerTribal) return;
            // The raid-bonus cooldown counts DAYS. (It used to tick down inside
            // CheckRaidBonus, i.e. once per raid map-event anywhere in Calradia —
            // effectively random. Now it is a plain 3-day cooldown.)
            if (_raidCooldown > 0) _raidCooldown--;
            try
            {
                var playerClan = Clan.PlayerClan;
                if (playerClan == null) return;

                foreach (var party in MobileParty.All.ToList())
                {
                    if (party == null || !party.IsActive) continue;
                    if (party.LeaderHero?.Clan != playerClan) continue;
                    try
                    {
                        if (party.RecentEventsMorale < WarFeverMoraleFloor)
                            party.RecentEventsMorale = WarFeverMoraleFloor;
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ── Spoils of the Raid — bonus gold on successful village raids ────────
        private static int _raidCooldown = 0;

        public static void CheckRaidBonus(MapEvent mapEvent)
        {
            if (!IsPlayerTribal) return;
            if (mapEvent.EventType != MapEvent.BattleTypes.Raid) return;
            if (_raidCooldown > 0) return;   // counted down daily in DailyTick

            bool playerAttacker = mapEvent.AttackerSide?.Parties
                .Any(p => p.Party == PartyBase.MainParty) == true;
            if (!playerAttacker) return;
            if (mapEvent.WinningSide != BattleSideEnum.Attacker) return;

            int partySize  = MobileParty.MainParty?.MemberRoster?.TotalManCount ?? 50;
            int bonusGold  = 50 + (partySize / 5) * 5; // 50–(partySize/5)*5, roughly 50-150
            if (bonusGold > 150) bonusGold = 150;

            try { Hero.MainHero?.ChangeHeroGold(bonusGold); } catch { }
            _raidCooldown = 3; // slight cooldown so back-to-back raids don't stack wildly

            InformationManager.DisplayMessage(new InformationMessage(
                $"Spoils of the Raid — the tribesmen strip the village bare. (+{bonusGold} gold)",
                new Color(0.85f, 0.6f, 0.2f)));
        }

        public static void ResetRaidCooldown() => _raidCooldown = 0;
    }
}
