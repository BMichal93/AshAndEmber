// =============================================================================
// ASH AND EMBER — Nature/NatureCharge.cs
// Holds the player's current elemental charge(s) and handles terrain detection.
//
// Draw:    pulls one charge from the environment — element from terrain.
// Release: spends the oldest charge and returns the power to cast.
// Expiry:  charges expire after NatureMath.ChargeMissionExpirySec unless
//          the player owns the Open Grip talent.
// Capacity: 1 normally, 2 with the Living Root talent.
// =============================================================================

using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class NatureCharge
    {
        private static readonly Random _rng = new Random();

        // Each charge: (power, timer remaining in seconds or campaign days)
        private static readonly List<(NaturePower power, float timer)> _charges
            = new List<(NaturePower, float)>();

        // Battle terrain is cached once at mission start so mid-fight draws are
        // consistent with the map position that determined the battle terrain.
        private static NatureElement[] _battleTerrainElements = null;

        // ── Capacity ──────────────────────────────────────────────────────────
        private static int Capacity => TalentSystem.Has(TalentId.NatureLivingRoot) ? 2 : 1;

        // ── Public state ──────────────────────────────────────────────────────
        public static bool HasCharge    => _charges.Count > 0;
        public static int  ChargeCount  => _charges.Count;

        public static NaturePower CurrentPower =>
            _charges.Count > 0 ? _charges[0].power : NaturePower.None;

        // ── Terrain ───────────────────────────────────────────────────────────

        // Call at mission start to lock in the terrain type for this battle.
        public static void CacheBattleTerrain()
        {
            _battleTerrainElements = null;
            try
            {
                if (Campaign.Current == null || MobileParty.MainParty == null) return;
                CampaignVec2 pos = MobileParty.MainParty.Position;
                string terrainName = "Plain";
                try
                {
                    var terrain = Campaign.Current.MapSceneWrapper
                        ?.GetTerrainTypeAtPosition(pos);
                    if (terrain.HasValue)
                        terrainName = terrain.Value.ToString();
                }
                catch { }
                _battleTerrainElements = NatureMath.TerrainElements(terrainName);
            }
            catch { }
        }

        private static NatureElement[] GetCurrentTerrainElements(bool inMission)
        {
            if (inMission && _battleTerrainElements != null)
                return _battleTerrainElements;

            // Campaign map: read live position
            try
            {
                if (Campaign.Current == null || MobileParty.MainParty == null)
                    return NatureMath.TerrainElements("Plain");
                CampaignVec2 pos = MobileParty.MainParty.Position;
                string terrainName = "Plain";
                try
                {
                    var terrain = Campaign.Current.MapSceneWrapper
                        ?.GetTerrainTypeAtPosition(pos);
                    if (terrain.HasValue)
                        terrainName = terrain.Value.ToString();
                }
                catch { }
                return NatureMath.TerrainElements(terrainName);
            }
            catch { return NatureMath.TerrainElements("Plain"); }
        }

        // Returns the terrain element list so callers can display it before drawing.
        public static NatureElement[] PeekTerrainElements(bool inMission)
            => GetCurrentTerrainElements(inMission);

        // ── Draw ──────────────────────────────────────────────────────────────
        // Returns true and the drawn power on success, false if blocked.
        public static bool TryDraw(bool inMission, out NaturePower drawn, out string failReason)
        {
            drawn      = NaturePower.None;
            failReason = null;

            if (DarkGiftSystem.HasAnyGift)
            {
                failReason = "The darkness in you silences the root-voice.";
                return false;
            }

            if (!NatureKnowledge.IsAttuned)
            {
                failReason = "You are not attuned to the living world.";
                return false;
            }

            if (_charges.Count >= Capacity)
            {
                failReason = "Your grip is already full. Release what you hold before drawing again.";
                return false;
            }

            NatureElement[] pool = GetCurrentTerrainElements(inMission);
            NatureElement el = pool[_rng.Next(pool.Length)];
            drawn = NatureMath.RandomPower(el, _rng);

            float timer = inMission
                ? NatureMath.ChargeMissionExpirySec
                : (float)NatureMath.ChargeCampaignExpiryDays;
            _charges.Add((drawn, timer));
            return true;
        }

        // ── Release ───────────────────────────────────────────────────────────
        // Consumes the oldest charge and returns its power.
        public static NaturePower Release()
        {
            if (_charges.Count == 0) return NaturePower.None;
            NaturePower p = _charges[0].power;
            _charges.RemoveAt(0);
            return p;
        }

        // ── Tick (mission) ────────────────────────────────────────────────────
        public static void MissionTick(float dt)
        {
            if (TalentSystem.Has(TalentId.NatureOpenGrip)) return;
            for (int i = _charges.Count - 1; i >= 0; i--)
            {
                float t = _charges[i].timer - dt;
                if (t <= 0f)
                {
                    NaturePower expired = _charges[i].power;
                    _charges.RemoveAt(i);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The {NatureMath.PowerName(expired)} fades — the land takes back what you did not use.",
                        new Color(0.5f, 0.7f, 0.4f)));
                }
                else
                {
                    _charges[i] = (_charges[i].power, t);
                }
            }
        }

        // ── Tick (campaign day) ───────────────────────────────────────────────
        public static void DailyTick()
        {
            if (TalentSystem.Has(TalentId.NatureOpenGrip)) return;
            for (int i = _charges.Count - 1; i >= 0; i--)
            {
                float t = _charges[i].timer - 1f;
                if (t <= 0f)
                    _charges.RemoveAt(i);
                else
                    _charges[i] = (_charges[i].power, t);
            }
        }

        // ── Passive campaign accumulation ─────────────────────────────────────
        // Called on the daily tick: adds a free charge if empty and terrain qualifies.
        // Double rate with Living Root talent.
        public static void TryCampaignAccumulate()
        {
            if (!NatureKnowledge.IsAttuned) return;
            if (_charges.Count >= Capacity) return;
            int passes = TalentSystem.Has(TalentId.NatureLivingRoot) ? 2 : 1;
            for (int p = 0; p < passes; p++)
            {
                if (_charges.Count >= Capacity) break;
                NaturePower drawn;
                string _;
                if (TryDraw(inMission: false, out drawn, out _))
                {
                    // Passive accumulation: silent unless it filled from empty
                    if (_charges.Count == 1)
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"The land is generous. You carry a {NatureMath.PowerName(drawn)} charge.",
                            new Color(0.4f, 0.75f, 0.4f)));
                }
            }
        }

        // ── Clear ─────────────────────────────────────────────────────────────
        public static void ClearForMission()
        {
            _charges.Clear();
            _battleTerrainElements = null;
        }
    }
}
