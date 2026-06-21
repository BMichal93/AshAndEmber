// =============================================================================
// ASH AND EMBER — Nature/NatureCharge.cs
// Holds the player's elemental charge(s) and the channelling fill.
//
// Channel: standing still while focusing fills a bar; when it completes you gain
//          one charge of the local element. The charge then lasts ~10 s.
// Cast:    the input handler spends the charge as the element's attack or support.
// Element: decided by the battle terrain (cached at mission start). Mixed/unknown
//          ground gives a random element.
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

        // Each charge: (element, seconds remaining).
        private static readonly List<(NatureElement element, float timer)> _charges
            = new List<(NatureElement, float)>();

        // Channel progress in seconds toward one charge.
        private static float _fill = 0f;

        // Battle terrain cached at mission start.
        private static NatureElement[] _battleTerrainElements = null;

        // ── Capacity / talents ──────────────────────────────────────────────────
        private static int Capacity => TalentSystem.Has(TalentId.NatureLivingRoot) ? 2 : 1;

        // Channel speed multiplier from talents (faster fill).
        private static float FillRate
        {
            get
            {
                float rate = 1f;
                if (TalentSystem.Has(TalentId.NatureStillDraw)) rate += 1f;   // twice as fast
                if (TalentSystem.Has(TalentId.NatureDeepEarth)) rate += 0.5f;
                return rate;
            }
        }

        // ── Public state ────────────────────────────────────────────────────────
        public static bool HasCharge   => _charges.Count > 0;
        public static int  ChargeCount => _charges.Count;
        public static bool IsFull      => _charges.Count >= Capacity;
        public static int  MaxCharges  => Capacity;

        public static NatureElement CurrentElement =>
            _charges.Count > 0 ? _charges[0].element : NatureElement.None;

        // 0..1 fill toward the next charge — for the channel bar (Part 3).
        public static float FillProgress01 =>
            NatureMath.ChannelFillSeconds <= 0f ? 1f
            : Math.Min(1f, _fill / NatureMath.ChannelFillSeconds);

        public static bool IsChannelling => _fill > 0f && !IsFull;

        // The element the bar should colour itself with: the held charge if any,
        // else a preview of the local terrain (first option of a mixed set).
        public static NatureElement PreviewElement(bool inMission)
        {
            if (HasCharge) return CurrentElement;
            var pool = GetCurrentTerrainElements(inMission);
            return (pool != null && pool.Length > 0) ? pool[0] : NatureElement.Wind;
        }

        // ── Terrain ─────────────────────────────────────────────────────────────
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
                    var terrain = Campaign.Current.MapSceneWrapper?.GetTerrainTypeAtPosition(pos);
                    if (terrain.HasValue) terrainName = terrain.Value.ToString();
                }
                catch { }
                _battleTerrainElements = NatureMath.TerrainElements(terrainName);
            }
            catch { }
        }

        private static NatureElement[] GetCurrentTerrainElements(bool inMission)
        {
            if (inMission && _battleTerrainElements != null) return _battleTerrainElements;
            try
            {
                if (Campaign.Current == null || MobileParty.MainParty == null)
                    return NatureMath.TerrainElements("Plain");
                CampaignVec2 pos = MobileParty.MainParty.Position;
                string terrainName = "Plain";
                try
                {
                    var terrain = Campaign.Current.MapSceneWrapper?.GetTerrainTypeAtPosition(pos);
                    if (terrain.HasValue) terrainName = terrain.Value.ToString();
                }
                catch { }
                return NatureMath.TerrainElements(terrainName);
            }
            catch { return NatureMath.TerrainElements("Plain"); }
        }

        public static NatureElement[] PeekTerrainElements(bool inMission) => GetCurrentTerrainElements(inMission);

        // ── Channel ─────────────────────────────────────────────────────────────
        // Advances the fill while the player stands still and focuses. Returns true
        // on the frame a charge is granted. No-op (and no progress) once full.
        public static bool ChannelTick(float dt, bool inMission)
        {
            if (IsFull) { _fill = 0f; return false; }
            if (dt <= 0f) return false;
            // Dark gifts silence the living world — no nature magic while gifted.
            if (DarkGiftSystem.HasAnyGift) { _fill = 0f; return false; }

            _fill += dt * FillRate;
            if (_fill < NatureMath.ChannelFillSeconds) return false;

            _fill = 0f;
            NatureElement[] pool = GetCurrentTerrainElements(inMission);
            NatureElement el = (pool != null && pool.Length > 0)
                ? pool[_rng.Next(pool.Length)] : NatureElement.Wind;
            _charges.Add((el, NatureMath.ChargeLifeSeconds));
            return true;
        }

        public static void ResetFill() => _fill = 0f;

        // Grant a charge directly (campaign standing-still — Part 4).
        public static bool GrantCampaignCharge()
        {
            if (IsFull) return false;
            if (DarkGiftSystem.HasAnyGift) return false;   // gifts block nature
            NatureElement[] pool = GetCurrentTerrainElements(false);
            NatureElement el = (pool != null && pool.Length > 0)
                ? pool[_rng.Next(pool.Length)] : NatureElement.Wind;
            _charges.Add((el, NatureMath.ChargeLifeSeconds));
            return true;
        }

        // ── Release ─────────────────────────────────────────────────────────────
        public static NatureElement Release()
        {
            if (_charges.Count == 0) return NatureElement.None;
            NatureElement el = _charges[0].element;
            _charges.RemoveAt(0);
            return el;
        }

        // ── Expiry tick (battle) ────────────────────────────────────────────────
        public static void MissionTick(float dt)
        {
            if (TalentSystem.Has(TalentId.NatureOpenGrip)) return;  // charges never fade
            for (int i = _charges.Count - 1; i >= 0; i--)
            {
                float t = _charges[i].timer - dt;
                if (t <= 0f) _charges.RemoveAt(i);
                else _charges[i] = (_charges[i].element, t);
            }
        }

        // ── Clear ───────────────────────────────────────────────────────────────
        public static void ClearForMission()
        {
            _charges.Clear();
            _battleTerrainElements = null;
            _fill = 0f;
        }
    }
}
