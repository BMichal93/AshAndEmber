// =============================================================================
// COLOURS OF CALRADIA — AI/BlightSystem.cs
// Mount & Blade II: Bannerlord Mod  v1.2.0.0.0
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace ColoursOfCalradia
{
    // =========================================================================
    // BLIGHT SYSTEM
    //   6 blights — one per colour school, one per kingdom at game start.
    //   Blights are converted wanderer heroes levelled to 35.
    //   Red/Yellow/Blue/Purple: solo, move fast (small party speed bonus).
    //   Green/Orange:           travel with 20–50 elite escorts.
    //   All blights cast their school's spells every 2s in battle with no
    //   limitations, and are immune to spells of their own school.
    //   On death, a new blight of the same colour respawns after 1 week.
    //
    //   Colour learning:
    //     Player personally kills blight → offered to learn that colour
    //     (only if player already has ≥1 colour).
    //     NPC lord kills blight → 7% chance to learn that colour.
    // =========================================================================
    public static class BlightSystem
    {
        private static bool _initialized;
        private static readonly Random _rng = new Random();
        private static readonly FieldInfo _extraSpeedBonusField =
            typeof(MobileParty).GetField("_extraSpeedBonusFromItems",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // ColorSchool (int) → hero StringId
        private static readonly Dictionary<int, string> _blightIds   = new Dictionary<int, string>();
        // ColorSchool (int) → hours until respawn
        private static readonly Dictionary<int, int>    _respawnHours = new Dictionary<int, int>();
        // Schools killed by the player in the current/last mission — consumed in OnMissionEnded
        private static readonly HashSet<int> _pendingPlayerKills = new HashSet<int>();

        private const int RespawnHours = 24 * 7; // 1 week
        private const int NpcLearnChance = 7;    // 7 % per kill

        // ── Public API ────────────────────────────────────────────────────────
        public static bool IsBlight(Hero hero) =>
            hero != null && _blightIds.ContainsValue(hero.StringId);

        public static bool IsBlight(Agent agent)
        {
            Hero h = (agent?.Character as CharacterObject)?.HeroObject;
            return IsBlight(h);
        }

        public static ColorSchool GetBlightSchool(Hero hero)
        {
            if (hero == null) return ColorSchool.Red;
            foreach (var kvp in _blightIds)
                if (kvp.Value == hero.StringId) return (ColorSchool)kvp.Key;
            return ColorSchool.Red;
        }

        public static ColorSchool GetBlightSchool(Agent agent)
        {
            Hero h = (agent?.Character as CharacterObject)?.HeroObject;
            return GetBlightSchool(h);
        }

        // Called from OnAgentRemoved in MagicMissionBehavior when player kills a blight agent.
        public static void RecordPlayerBlightKill(ColorSchool school)
            => _pendingPlayerKills.Add((int)school);

        // Drains and returns all schools the player killed this mission.
        public static IEnumerable<ColorSchool> ConsumePlayerBlightKills()
        {
            var result = _pendingPlayerKills.Select(k => (ColorSchool)k).ToList();
            _pendingPlayerKills.Clear();
            return result;
        }

        // ── Initialization ────────────────────────────────────────────────────
        // Called on new-game start to guarantee a clean state regardless of prior session.
        public static void ResetForNewGame()
        {
            _initialized = false;
            _blightIds.Clear();
            _respawnHours.Clear();
            _pendingPlayerKills.Clear();
        }

        public static void InitializeBlights()
        {
            if (_initialized) return;
            try
            {
                var kingdoms = Campaign.Current?.Kingdoms?.ToList();
                if (kingdoms == null || kingdoms.Count == 0) return;

                _initialized = true;

                // Shuffle kingdoms for random colour assignment
                for (int i = kingdoms.Count - 1; i > 0; i--)
                {
                    int j = _rng.Next(i + 1);
                    var t = kingdoms[i]; kingdoms[i] = kingdoms[j]; kingdoms[j] = t;
                }

                var allSchools = (ColorSchool[])Enum.GetValues(typeof(ColorSchool));
                for (int i = 0; i < allSchools.Length; i++)
                    TrySpawnBlight(allSchools[i], kingdoms[i % kingdoms.Count]);
            }
            catch { _initialized = false; }
        }

        // ── Spawn ─────────────────────────────────────────────────────────────
        private static void TrySpawnBlight(ColorSchool school, Kingdom nearKingdom)
        {
            try
            {
                // Avoid re-spawning a school that already has a living blight
                if (_blightIds.ContainsKey((int)school)) return;

                Hero candidate = Hero.AllAliveHeroes
                    .Where(h => h.IsWanderer && h.IsAlive
                             && h.PartyBelongedTo == null
                             && !IsBlight(h)
                             && !ColourLordRegistry.IsColourLord(h))
                    .OrderBy(_ => _rng.Next())
                    .FirstOrDefault();
                if (candidate == null) return;

                _blightIds[(int)school] = candidate.StringId;

                // Level to 35
                try
                {
                    while (candidate.Level < 35)
                        candidate.HeroDeveloper.AddSkillXp(DefaultSkills.OneHanded, 10000);
                }
                catch { }

                InformationManager.DisplayMessage(new InformationMessage(
                    $"A Blight stirs — {candidate.Name}, marked by {ColorSchoolData.Info[school].Name}, " +
                    $"wanders near {nearKingdom.Name}. Beware.",
                    ColorSchoolData.GetMessageColor(school)));

                TryCreateParty(candidate, school, GetSpawnPos(nearKingdom));
            }
            catch { }
        }

        private static CampaignVec2 GetSpawnPos(Kingdom kingdom)
        {
            try
            {
                var capital = kingdom.Settlements.FirstOrDefault(s => s.IsTown)
                           ?? kingdom.Settlements.FirstOrDefault();
                if (capital != null)
                {
                    Vec2 capitalPos = capital.GetPosition2D;
                    return new CampaignVec2(new Vec2(
                        capitalPos.X + (float)(_rng.NextDouble() - 0.5) * 30f,
                        capitalPos.Y + (float)(_rng.NextDouble() - 0.5) * 30f), true);
                }
            }
            catch { }
            return new CampaignVec2(new Vec2(400f + (float)(_rng.NextDouble() - 0.5) * 50f,
                                               400f + (float)(_rng.NextDouble() - 0.5) * 50f), true);
        }

        private static void TryCreateParty(Hero blight, ColorSchool school, CampaignVec2 pos)
        {
            try
            {
                Clan banditClan = Clan.BanditFactions?.FirstOrDefault();
                if (banditClan == null) return;

                try { blight.Clan = banditClan; } catch { }

                MobileParty party = TaleWorlds.CampaignSystem.Party.PartyComponents.BanditPartyComponent.CreateBanditParty(
                    "coc_blight_" + (int)school, banditClan, null, false, null, pos);
                if (party == null) return;

                try { party.ActualClan = banditClan; } catch { }

                // Green/Orange get 20–50 elite escorts; others get speed boost
                if (school == ColorSchool.Green || school == ColorSchool.Orange)
                    TryAddEliteEscorts(party, school);
                else
                    TryBoostSpeed(party);

                try { party.IsVisible = true; } catch { }
            }
            catch { }
        }

        private static void TryAddEliteEscorts(MobileParty party, ColorSchool school)
        {
            try
            {
                int count = 20 + _rng.Next(31); // 20–50
                CharacterObject troop = null;

                // Tier 4+ cavalry for Orange (momentum/charge theme), infantry for Green (nature/shield)
                foreach (CharacterObject c in CharacterObject.All)
                {
                    if (c.IsHero || c.Tier < 4) continue;
                    if (school == ColorSchool.Orange && c.IsMounted)  { troop = c; break; }
                    if (school == ColorSchool.Green  && !c.IsMounted) { troop = c; break; }
                }
                if (troop == null)
                    foreach (CharacterObject c in CharacterObject.All)
                        if (!c.IsHero && c.Tier >= 3) { troop = c; break; }

                if (troop != null)
                    party.MemberRoster.AddToCounts(troop, count);
            }
            catch { }
        }

        private static void TryBoostSpeed(MobileParty party)
        {
            try
            {
                // Best-effort speed boost for solo blights; safe to skip if the private field changes.
                if (_extraSpeedBonusField == null || party == null) return;
                float cur = (float)(_extraSpeedBonusField.GetValue(party) ?? 0f);
                _extraSpeedBonusField.SetValue(party, cur + 3f);
            }
            catch { }
        }

        // ── Death / Respawn ───────────────────────────────────────────────────
        // Called from CampaignBehavior.OnHeroKilled when a blight hero dies.
        public static void OnBlightKilled(Hero victim)
        {
            ColorSchool school = GetBlightSchool(victim);
            _blightIds.Remove((int)school);
            // Blights do not auto-respawn; new ones only arise from NPC oversaturation events.
            InformationManager.DisplayMessage(new InformationMessage(
                $"{victim.Name} — the {ColorSchoolData.Info[school].Name} Blight — is slain.",
                ColorSchoolData.GetMessageColor(school)));
        }

        // Blights no longer auto-respawn on a timer.
        public static void CheckRespawnTimers() { }

        // Called from the weekly NPC oversaturation event (20% path).
        public static void SpawnBlightFromOversaturation(ColorSchool school)
        {
            try
            {
                Kingdom kingdom = Campaign.Current?.Kingdoms
                    .OrderBy(_ => _rng.Next()).FirstOrDefault();
                if (kingdom != null) TrySpawnBlight(school, kingdom);
            }
            catch { }
        }

        // ── NPC lord kills blight — 7 % chance to learn the colour ───────────
        public static void OnNpcKilledBlight(Hero killer, ColorSchool school)
        {
            if (killer == null || !killer.IsLord) return;
            if (ColourLordRegistry.HasColor(killer, school)) return;
            if (_rng.Next(100) >= NpcLearnChance) return;
            ColourLordRegistry.GrantBlightColour(killer, school);
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var stateEntries = _blightIds.Select(kvp =>
            {
                int school = kvp.Key;
                string heroId = kvp.Value ?? "";
                int respawn = _respawnHours.TryGetValue(school, out int hours) ? hours : 0;
                return SerializeBlightState(school, heroId, respawn);
            }).ToList();

            var bKeys   = _blightIds.Keys.ToList();
            var bVals   = _blightIds.Values.ToList();
            var rKeys   = _respawnHours.Keys.ToList();
            var rVals   = _respawnHours.Values.ToList();
            bool inited = _initialized;

            store.SyncData("COC_BlightState", ref stateEntries);
            store.SyncData("COC_BlightKeys",   ref bKeys);
            store.SyncData("COC_BlightVals",   ref bVals);
            store.SyncData("COC_BlightRspK",   ref rKeys);
            store.SyncData("COC_BlightRspV",   ref rVals);
            store.SyncData("COC_BlightInited", ref inited);

            _initialized = inited;

            _blightIds.Clear();
            _respawnHours.Clear();

            if (stateEntries != null && stateEntries.Count > 0)
            {
                foreach (string entry in stateEntries)
                    if (TryParseBlightState(entry, out int school, out string heroId, out int respawn))
                    {
                        _blightIds[school] = heroId;
                        if (respawn > 0)
                            _respawnHours[school] = respawn;
                    }
                return;
            }

            if (bKeys != null && bVals != null)
                for (int i = 0; i < Math.Min(bKeys.Count, bVals.Count); i++)
                    _blightIds[bKeys[i]] = bVals[i];

            if (rKeys != null && rVals != null)
                for (int i = 0; i < Math.Min(rKeys.Count, rVals.Count); i++)
                    _respawnHours[rKeys[i]] = rVals[i];
        }

        private static string SerializeBlightState(int school, string heroId, int respawnHours)
            => $"{school}|{heroId}|{respawnHours}";

        private static bool TryParseBlightState(string entry, out int school, out string heroId, out int respawnHours)
        {
            school = 0;
            heroId = null;
            respawnHours = 0;
            if (string.IsNullOrWhiteSpace(entry)) return false;

            string[] parts = entry.Split(new[] { '|' }, 3);
            if (parts.Length < 3) return false;
            if (!int.TryParse(parts[0], out school)) return false;
            heroId = parts[1];
            if (!int.TryParse(parts[2], out respawnHours)) respawnHours = 0;
            return true;
        }
    }
}
