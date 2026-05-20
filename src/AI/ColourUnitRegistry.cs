// =============================================================================
// COLOURS OF CALRADIA — AI/ColourUnitRegistry.cs
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
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Engine;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.CampaignSystem.MapEvents;

namespace ColoursOfCalradia
{
    // =========================================================================
    // 11. COLOUR UNIT ENTRY  — data for one persistent magical common soldier
    // =========================================================================
    public class ColourUnitEntry
    {
        public string            Id;
        public string            DisplayName;
        public List<ColorSchool> Schools;
        public string            PartyStringId;
        public bool              IsAlive;
    }

    public class RespawnEntry
    {
        public string            PartyStringId;
        public List<ColorSchool> Schools;
        public int               DaysLeft;
    }

    // =========================================================================
    // 12. COLOUR UNIT REGISTRY
    //     Single-school mage soldiers embedded in armies and bandit groups.
    //     Lord parties (200+ troops): 1 unit; (350+ troops): 2 units. Daily 5% reseed.
    //     Bandit parties (40+ troops): 4% initial chance, 1% daily reseed.
    //     Units are persistent: they survive between battles and die permanently.
    //     On death, they re-queue (3–5 days) and respawn in a party of the same type.
    // =========================================================================
    public static class ColourUnitRegistry
    {
        private static readonly Dictionary<string, ColourUnitEntry> _units
            = new Dictionary<string, ColourUnitEntry>();

        // Per-mission: agentIndex → unitId
        private static readonly Dictionary<int, string> _missionAgents
            = new Dictionary<int, string>();
        private static readonly HashSet<string> _diedThisMission
            = new HashSet<string>();
        private static readonly Dictionary<string, float> _cooldowns
            = new Dictionary<string, float>();

        private static bool  _missionInitialized;
        private static bool  _seeded;
        private static int   _nextId = 1;
        private static float _aiAccum;
        private static readonly Random _rng = new Random();
        private static readonly Dictionary<string, int> _mapCooldowns = new Dictionary<string, int>();
        private static readonly List<RespawnEntry>      _respawnQueue  = new List<RespawnEntry>();

        private const float CastCooldown   = 20f;
        private const float AiTickInterval = 0.5f;

        // ── Name generation ───────────────────────────────────────────────────
        private static readonly string[] _firstNames =
        {
            "Mira", "Aldric", "Sena", "Corvin", "Thessa", "Boran", "Lirien",
            "Garath", "Vessa", "Doran", "Amael", "Corith", "Neva", "Rhuel",
            "Kessa", "Aldren", "Tireth", "Morva", "Selith", "Davan"
        };

        private static readonly Dictionary<ColorSchool, string[]> _schoolSuffixes =
            new Dictionary<ColorSchool, string[]>
            {
                [ColorSchool.Red]    = new[] { "the Ember",   "Bloodhanded", "Pyremark"    },
                [ColorSchool.Orange] = new[] { "the Bright",  "Goldenvoiced","the Warm"    },
                [ColorSchool.Yellow] = new[] { "the Pale",    "of Dread",    "the Craven"  },
                [ColorSchool.Green]  = new[] { "the Tender",  "Root-spoken", "the Verdant" },
                [ColorSchool.Blue]   = new[] { "the Still",   "Coldwater",   "the Patient" },
                [ColorSchool.Purple] = new[] { "the Hollow",  "the Grey",    "the Fading"  },
            };

        private static string NewId() => $"cou_{_nextId++}";

        private static string GenerateName(ColorSchool primary)
        {
            string first    = _firstNames[_rng.Next(_firstNames.Length)];
            string[] sfx    = _schoolSuffixes[primary];
            return $"{first} {sfx[_rng.Next(sfx.Length)]}";
        }

        // ── Seeding ───────────────────────────────────────────────────────────
        public static void SeedInitialUnits()
        {
            if (_seeded) return;
            _seeded = true;
            try
            {
                foreach (MobileParty party in MobileParty.All.ToList())
                {
                    if      (party.IsLordParty)              SeedLordParty(party);
                    else if (party.IsBandit) TrySeedBanditParty(party);
                }
            }
            catch { }
        }

        private static void SeedLordParty(MobileParty party)
        {
            // Only very large armies get 1-2 single-school mage soldiers
            int size = party.MemberRoster.TotalManCount;
            if (size < 200) return;
            int count = size >= 350 ? 2 : 1;
            for (int i = 0; i < count; i++)
                CreateUnit(party, 1);
        }

        private static void TrySeedBanditParty(MobileParty party)
        {
            if (party.MemberRoster.TotalManCount < 40) return;
            if (_rng.Next(100) >= 4) return;
            CreateUnit(party, 1);
        }

        private static void CreateUnit(MobileParty party, int schoolCount)
        {
            var pool   = new List<ColorSchool>((ColorSchool[])Enum.GetValues(typeof(ColorSchool)));
            var chosen = new List<ColorSchool>();
            for (int i = 0; i < Math.Min(schoolCount, pool.Count); i++)
            {
                int idx = _rng.Next(pool.Count);
                chosen.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            if (chosen.Count == 0) return;

            string id = NewId();
            _units[id] = new ColourUnitEntry
            {
                Id            = id,
                DisplayName   = GenerateName(chosen[0]),
                Schools       = chosen,
                PartyStringId = party.StringId,
                IsAlive       = true
            };
        }

        // Called every game day: clean up dead parties, process respawns, seed new ones.
        public static void DailyMaintenance()
        {
            try
            {
                // Decrement map cooldowns
                foreach (string key in _mapCooldowns.Keys.ToList())
                {
                    _mapCooldowns[key]--;
                    if (_mapCooldowns[key] <= 0) _mapCooldowns.Remove(key);
                }

                // Snapshot MobileParty.All once — repeated enumeration is the main perf cost.
                var allParties  = MobileParty.All.ToList();
                var allPartyIds = new HashSet<string>(allParties.Select(p => p.StringId));
                // O(1) lookup: which party IDs already have a living colour unit
                var occupiedIds = new HashSet<string>(
                    _units.Values.Where(u => u.IsAlive).Select(u => u.PartyStringId));

                // Process respawn queue
                foreach (RespawnEntry entry in _respawnQueue.ToList())
                {
                    entry.DaysLeft--;
                    if (entry.DaysLeft > 0) continue;

                    _respawnQueue.Remove(entry);

                    MobileParty origin = allParties.FirstOrDefault(p => p.StringId == entry.PartyStringId);
                    bool wasLord = origin?.IsLordParty ?? false;

                    MobileParty target;
                    if (wasLord)
                    {
                        var candidates = allParties
                            .Where(p => p.IsLordParty && p.MemberRoster.TotalManCount >= 200
                                        && !occupiedIds.Contains(p.StringId)).ToList();
                        target = candidates.Count > 0 ? candidates[_rng.Next(candidates.Count)] : null;
                    }
                    else
                    {
                        target = allParties.FirstOrDefault(p =>
                            p.StringId == entry.PartyStringId && p.IsBandit
                            && p.MemberRoster.TotalManCount >= 40);
                        if (target == null)
                        {
                            var candidates = allParties
                                .Where(p => p.IsBandit && p.MemberRoster.TotalManCount >= 40
                                            && !occupiedIds.Contains(p.StringId)).ToList();
                            target = candidates.Count > 0 ? candidates[_rng.Next(candidates.Count)] : null;
                        }
                    }

                    if (target == null)
                    {
                        entry.DaysLeft = 5;
                        _respawnQueue.Add(entry);
                        continue;
                    }

                    CreateUnit(target, entry.Schools.Count);
                    occupiedIds.Add(target.StringId);
                }

                // Remove units whose party disbanded — O(1) per unit using the HashSet
                foreach (var entry in _units.Values.ToList())
                {
                    if (!entry.IsAlive) continue;
                    if (!allPartyIds.Contains(entry.PartyStringId))
                        entry.IsAlive = false;
                }

                // Seed newly-qualifying parties
                foreach (MobileParty party in allParties)
                {
                    if (occupiedIds.Contains(party.StringId)) continue;

                    if (party.IsLordParty && party.MemberRoster.TotalManCount >= 200
                        && _rng.Next(100) < 5)
                    {
                        CreateUnit(party, 1);
                        occupiedIds.Add(party.StringId);
                    }
                    else if (party.IsBandit && party.MemberRoster.TotalManCount >= 40
                        && _rng.Next(100) < 1)
                    {
                        CreateUnit(party, 1);
                        occupiedIds.Add(party.StringId);
                    }
                }
            }
            catch { }
        }

        private const int MaxUnitMapCastsPerDay = 1;

        public static void DailyMapCast()
        {
            int castsToday = 0;
            foreach (ColourUnitEntry unit in _units.Values.ToList())
            {
                if (castsToday >= MaxUnitMapCastsPerDay) break;
                if (!unit.IsAlive || unit.Schools.Count == 0) continue;
                if (_mapCooldowns.ContainsKey(unit.Id)) continue;
                if (_rng.Next(100) >= 3) continue; // 3% chance per day

                ColorSchool school = unit.Schools[_rng.Next(unit.Schools.Count)];
                string      name   = unit.DisplayName;
                string      msg    = null;

                try
                {
                    switch (school)
                    {
                        case ColorSchool.Green:
                        {
                            MobileParty p = MobileParty.All.FirstOrDefault(x => x.StringId == unit.PartyStringId);
                            if (p != null) { p.RecentEventsMorale += 5f; msg = $"{name} channels Green and mends the weary."; }
                            break;
                        }
                        case ColorSchool.Orange:
                        {
                            MobileParty p = MobileParty.All.FirstOrDefault(x => x.StringId == unit.PartyStringId);
                            if (p != null) { p.RecentEventsMorale += 8f; msg = $"{name} stirs Orange fire — the column marches with purpose."; }
                            break;
                        }
                        case ColorSchool.Yellow:
                        {
                            MobileParty p = MobileParty.All.FirstOrDefault(x => x.StringId == unit.PartyStringId);
                            if (p?.LeaderHero != null)
                            {
                                p.LeaderHero.AddSkillXp(DefaultSkills.Leadership, 20f);
                                msg = $"{name} invokes Yellow resonance — wisdom flows through the ranks.";
                            }
                            break;
                        }
                        default:
                            msg = $"{name} murmurs in the tongue of {school} — something stirs.";
                            break;
                    }
                }
                catch { }

                if (msg != null)
                {
                    InformationManager.DisplayMessage(new InformationMessage(msg,
                        ColorSchoolData.GetMessageColor(school)));
                    _mapCooldowns[unit.Id] = 6 + _rng.Next(4);
                    castsToday++;
                }
            }
        }

        // ── Mission AI ────────────────────────────────────────────────────────
        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;

            _aiAccum += dt;
            if (_aiAccum < AiTickInterval) return;
            _aiAccum = 0f;

            if (!SpellEffects.IsBattleMission()) return;

            if (!_missionInitialized)
            {
                InitializeMissionAgents();
                _missionInitialized = true;
            }

            if (_missionAgents.Count == 0) return;

            foreach (string key in _cooldowns.Keys.ToList())
            {
                _cooldowns[key] -= AiTickInterval;
                if (_cooldowns[key] <= 0f) _cooldowns.Remove(key);
            }

            foreach (var kvp in _missionAgents.ToList())
            {
                if (_cooldowns.ContainsKey(kvp.Value)) continue;
                if (!_units.TryGetValue(kvp.Value, out ColourUnitEntry unit)) continue;

                Agent agent = Mission.Current.Agents.FirstOrDefault(a => a.Index == kvp.Key);
                if (agent == null || !agent.IsActive()) continue;

                CastUnitSpell(agent, unit);
            }
        }

        private static void InitializeMissionAgents()
        {
            _missionAgents.Clear();
            if (Mission.Current == null) return;

            // Collect non-hero agents per team
            var teamPool = new Dictionary<Team, List<Agent>>();
            foreach (Agent a in Mission.Current.Agents)
            {
                if (a.IsMount || a.IsHero || !a.IsActive() || a.Team == null) continue;
                if (!teamPool.ContainsKey(a.Team)) teamPool[a.Team] = new List<Agent>();
                teamPool[a.Team].Add(a);
            }

            // Map party StringId → team via hero agents
            var partyTeam = new Dictionary<string, Team>();
            foreach (Agent a in Mission.Current.Agents)
            {
                if (!a.IsHero || a.Team == null) continue;
                Hero hero = (a.Character as CharacterObject)?.HeroObject;
                if (hero?.PartyBelongedTo != null && !partyTeam.ContainsKey(hero.PartyBelongedTo.StringId))
                    partyTeam[hero.PartyBelongedTo.StringId] = a.Team;
            }
            if (Agent.Main?.Team != null && MobileParty.MainParty != null)
                partyTeam[MobileParty.MainParty.StringId] = Agent.Main.Team;

            // Assign one agent per live colour unit whose party is in this battle
            foreach (ColourUnitEntry unit in _units.Values)
            {
                if (!unit.IsAlive) continue;
                if (!partyTeam.TryGetValue(unit.PartyStringId, out Team team)) continue;
                if (!teamPool.TryGetValue(team, out var pool) || pool.Count == 0) continue;

                int   idx   = _rng.Next(pool.Count);
                Agent agent = pool[idx];
                pool.RemoveAt(idx);

                _missionAgents[agent.Index] = unit.Id;
                SpellEffects.BeginAgentGlow(agent, unit.Schools[0], 3f);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"✦ {unit.DisplayName} stirs with {ColorSchoolData.Info[unit.Schools[0]].Name} colour.",
                    ColorSchoolData.GetMessageColor(unit.Schools[0])));
            }

            SpellEffects.ApplyColourNamePrefixes();
        }

        private static void CastUnitSpell(Agent agent, ColourUnitEntry unit)
        {
            ColorSchool school = unit.Schools[_rng.Next(unit.Schools.Count)];
            bool cast = false;

            try
            {
                switch (school)
                {
                    case ColorSchool.Red:
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 8f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.35f) continue;
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            SpellEffects.DamageAgent(a, 40f);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                            cast = true;
                        }
                        break;

                    case ColorSchool.Orange:
                        foreach (Agent a in AlliesOf(agent)
                            .Where(a => a.Position.Distance(agent.Position) <= 17f).ToList())
                            try { a.SetMorale(Math.Min(a.GetMorale() + 15f, 100f)); cast = true; } catch { }
                        break;

                    case ColorSchool.Yellow:
                        SpellEffects.IssueBattleCommand(agent, SpellEffects.BattleCommandKind.Halt,
                            "{0} formation{1} halted.", ColorSchool.Yellow);
                        cast = true;
                        break;

                    case ColorSchool.Green:
                        foreach (Agent a in AlliesOf(agent)
                            .Where(a => a.Health < a.HealthLimit * 0.7f
                                     && a.Position.Distance(agent.Position) <= 10f).ToList())
                        {
                            a.Health = Math.Min(a.Health + 20f, a.HealthLimit);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Green, 1.5f);
                            cast = true;
                        }
                        break;

                    case ColorSchool.Blue:
                        foreach (Agent a in EnemiesOf(agent)
                            .Where(a => a.Position.Distance(agent.Position) <= 17f).ToList())
                        {
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            try
                            {
                                SpellEffects.DamageAgent(a, 10f);
                                SpellEffects.BeginAgentGlow(a, ColorSchool.Blue, 1.5f);
                                cast = true;
                            }
                            catch { }
                        }
                        break;

                    case ColorSchool.Purple:
                        foreach (Agent a in EnemiesOf(agent)
                            .Where(a => a.Position.Distance(agent.Position) <= 6f).ToList())
                        {
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            SpellEffects.DamageAgent(a, 40f);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Purple, 1.5f);
                            cast = true;
                        }
                        break;
                }
            }
            catch { }

            if (!cast) return;

            SpellEffects.BeginAgentGlow(agent, school, 3f);
            SpellEffects.TryCastSound(agent.Position, school);

            InformationManager.DisplayMessage(new InformationMessage(
                $"{unit.DisplayName} channels {ColorSchoolData.Info[school].Name}!",
                ColorSchoolData.GetMessageColor(school)));

            _cooldowns[unit.Id] = CastCooldown;
        }

        // ── Death tracking ────────────────────────────────────────────────────
        public static void OnAgentRemoved(Agent agent)
        {
            if (!_missionAgents.TryGetValue(agent.Index, out string unitId)) return;
            _diedThisMission.Add(unitId);
            _missionAgents.Remove(agent.Index);
            if (_units.TryGetValue(unitId, out ColourUnitEntry unit))
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{unit.DisplayName} has fallen — their colour fades from the world.",
                    Color.FromUint(0xFF888888)));
        }

        public static void OnMissionEnded()
        {
            foreach (string id in _diedThisMission)
            {
                if (!_units.TryGetValue(id, out ColourUnitEntry u)) continue;
                u.IsAlive = false;
                _respawnQueue.Add(new RespawnEntry
                {
                    PartyStringId = u.PartyStringId,
                    Schools       = new List<ColorSchool>(u.Schools),
                    DaysLeft      = 3 + _rng.Next(3) // 3–5 days
                });
            }
            _diedThisMission.Clear();
            _missionAgents.Clear();
            _missionInitialized = false;
            _cooldowns.Clear();
            _aiAccum = 0f;
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static IEnumerable<Agent> EnemiesOf(Agent agent)
        {
            if (Mission.Current == null || agent?.Team == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != agent && !a.IsMount && a.IsActive() && a.Team != null && a.Team != agent.Team)
                    yield return a;
        }

        private static IEnumerable<Agent> AlliesOf(Agent agent)
        {
            if (Mission.Current == null || agent?.Team == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != agent && !a.IsMount && a.IsActive() && a.Team != null && a.Team == agent.Team)
                    yield return a;
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var entries     = _units.Values.ToList();
            var ids         = entries.Select(u => u.Id).ToList();
            var names       = entries.Select(u => u.DisplayName).ToList();
            var partyIds    = entries.Select(u => u.PartyStringId).ToList();
            var aliveFlags  = entries.Select(u => u.IsAlive ? 1 : 0).ToList();
            var schoolCnts  = entries.Select(u => u.Schools.Count).ToList();
            var flatSchools = entries.SelectMany(u => u.Schools.Select(s => (int)s)).ToList();
            bool seeded     = _seeded;
            int  nextId     = _nextId;

            var rqPartyIds  = _respawnQueue.Select(r => r.PartyStringId).ToList();
            var rqDays      = _respawnQueue.Select(r => r.DaysLeft).ToList();
            var rqSchoolCnt = _respawnQueue.Select(r => r.Schools.Count).ToList();
            var rqSchools   = _respawnQueue.SelectMany(r => r.Schools.Select(s => (int)s)).ToList();

            var cdKeys = _mapCooldowns.Keys.ToList();
            var cdVals = _mapCooldowns.Values.ToList();

            store.SyncData("COU_Ids",        ref ids);
            store.SyncData("COU_Names",       ref names);
            store.SyncData("COU_PartyIds",    ref partyIds);
            store.SyncData("COU_Alive",       ref aliveFlags);
            store.SyncData("COU_SchoolCnts",  ref schoolCnts);
            store.SyncData("COU_Schools",     ref flatSchools);
            store.SyncData("COU_Seeded",      ref seeded);
            store.SyncData("COU_NextId",      ref nextId);
            store.SyncData("COU_RqPartyIds",  ref rqPartyIds);
            store.SyncData("COU_RqDays",      ref rqDays);
            store.SyncData("COU_RqSchoolCnt", ref rqSchoolCnt);
            store.SyncData("COU_RqSchools",   ref rqSchools);
            store.SyncData("COU_CdKeys",      ref cdKeys);
            store.SyncData("COU_CdVals",      ref cdVals);

            _seeded = seeded;
            _nextId = Math.Max(_nextId, nextId);

            _units.Clear();
            if (ids == null) return;

            int si = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                int cnt = schoolCnts?[i] ?? 0;
                var schools = new List<ColorSchool>();
                for (int j = 0; j < cnt && si < (flatSchools?.Count ?? 0); j++, si++)
                    schools.Add((ColorSchool)flatSchools[si]);

                _units[ids[i]] = new ColourUnitEntry
                {
                    Id            = ids[i],
                    DisplayName   = names?[i]    ?? "Unknown",
                    PartyStringId = partyIds?[i] ?? "",
                    IsAlive       = (aliveFlags?[i] ?? 1) != 0,
                    Schools       = schools
                };
            }

            _respawnQueue.Clear();
            if (rqPartyIds != null)
            {
                int rsi = 0;
                for (int i = 0; i < rqPartyIds.Count; i++)
                {
                    int cnt = rqSchoolCnt?[i] ?? 0;
                    var schools = new List<ColorSchool>();
                    for (int j = 0; j < cnt && rsi < (rqSchools?.Count ?? 0); j++, rsi++)
                        schools.Add((ColorSchool)rqSchools[rsi]);
                    _respawnQueue.Add(new RespawnEntry
                    {
                        PartyStringId = rqPartyIds[i],
                        DaysLeft      = rqDays?[i] ?? 3,
                        Schools       = schools
                    });
                }
            }

            _mapCooldowns.Clear();
            if (cdKeys != null)
                for (int i = 0; i < cdKeys.Count && i < (cdVals?.Count ?? 0); i++)
                    _mapCooldowns[cdKeys[i]] = cdVals[i];
        }
    }
}
