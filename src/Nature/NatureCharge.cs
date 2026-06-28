// =============================================================================
// ASH AND EMBER — Nature/NatureCharge.cs
// Holds the player's elemental charge(s), the chosen element, and the fill.
//
// Choose:  while focused, trace a direction to DRAW that element
//          (W=Wind, S=Earth, A=Water, D=Storm). The land no longer decides.
// Channel: standing still while focusing fills a bar toward the chosen element;
//          when it completes you gain one charge. The charge then lasts ~30 s.
// Cast:    the input handler spends the charge as the element's attack or support.
// Cost:    each gathered charge spends the place's living energy — cheap for an
//          element the land favours, dear for one it does not (LivingEnergy).
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

        // The element the player has chosen to draw (traced with W/A/S/D while
        // focused). Persists across battles so the choice is not re-made each fight.
        private static NatureElement _selectedElement = NatureElement.None;

        // Outcome of the most recent gather (so callers can apply a sour recoil).
        private static DrawOutcome _lastGather;
        public static DrawOutcome LastGatherOutcome => _lastGather;

        // Battle terrain cached at mission start.
        private static NatureElement[] _battleTerrainElements = null;

        // ── Capacity / talents ──────────────────────────────────────────────────
        private static int Capacity => TalentSystem.Has(TalentId.NatureLivingRoot) ? 2 : 1;

        // Effective channel fill threshold (Templar penalty adds 1 s).
        private static float EffectiveChannelFillSeconds
            => TempleCulture.NatureChannelSeconds(NatureMath.ChannelFillSeconds);

        // Channel speed multiplier from talents (faster fill). Still Draw is the
        // fill-speed rite; Deep Earth instead spares the land (see DrainMult).
        private static float FillRate
        {
            get
            {
                float rate = 1f;
                if (TalentSystem.Has(TalentId.NatureStillDraw)) rate += 1f;   // twice as fast
                return rate;
            }
        }

        // Deep Earth draws gently — its bearer takes only half as much from the land.
        private static float DrainMult
            => TalentSystem.Has(TalentId.NatureDeepEarth) ? 0.5f : 1f;

        // ── Public state ────────────────────────────────────────────────────────
        public static bool HasCharge   => _charges.Count > 0;
        public static int  ChargeCount => _charges.Count;
        public static bool IsFull      => _charges.Count >= Capacity;
        public static int  MaxCharges  => Capacity;

        public static NatureElement CurrentElement =>
            _charges.Count > 0 ? _charges[0].element : NatureElement.None;

        // ── Element selection ─────────────────────────────────────────────────────
        public static NatureElement SelectedElement => _selectedElement;
        public static bool HasSelection => _selectedElement != NatureElement.None;

        // Choosing a different element interrupts any fill toward the old one.
        public static void SelectElement(NatureElement el)
        {
            if (el == NatureElement.None || el == _selectedElement) return;
            _selectedElement = el;
            _fill = 0f;
        }

        // 0..1 fill toward the next charge — for the channel bar (Part 3).
        public static float FillProgress01 =>
            EffectiveChannelFillSeconds <= 0f ? 1f
            : Math.Min(1f, _fill / EffectiveChannelFillSeconds);

        public static bool IsChannelling => _fill > 0f && !IsFull;

        // The element the bar should colour itself with: the held charge if any,
        // else the element the player has chosen to draw.
        public static NatureElement PreviewElement(bool inMission)
        {
            if (HasCharge) return CurrentElement;
            return _selectedElement;
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
        // Advances the fill while the player stands still and focuses on a chosen
        // element. Returns true on the frame a charge is granted. No-op without a
        // chosen element, and no progress once full.
        public static bool ChannelTick(float dt, bool inMission)
        {
            if (IsFull) { _fill = 0f; return false; }
            if (dt <= 0f) return false;
            if (!HasSelection) { _fill = 0f; return false; }   // nothing chosen to draw
            // Dark gifts silence the living world — no nature magic while gifted.
            if (DarkGiftSystem.HasAnyGift) { _fill = 0f; return false; }

            _fill += dt * FillRate;
            if (_fill < EffectiveChannelFillSeconds) return false;

            _fill = 0f;
            NatureElement el = _selectedElement;
            _charges.Add((el, NatureMath.ChargeLifeSeconds));
            DrainEnergy(el);
            return true;
        }

        public static void ResetFill() => _fill = 0f;

        // Grant a charge of the chosen element directly (campaign standing-still).
        public static bool GrantCampaignCharge(NatureElement el)
        {
            if (IsFull) return false;
            if (DarkGiftSystem.HasAnyGift) return false;   // gifts block nature
            if (el == NatureElement.None) return false;
            _selectedElement = el;
            _charges.Add((el, NatureMath.ChargeLifeSeconds));
            DrainEnergy(el);
            return true;
        }

        // Spend the local area's living energy for a gathered charge and remember
        // the outcome (so the gather site can apply a sour recoil).
        private static void DrainEnergy(NatureElement el)
        {
            _lastGather = default(DrawOutcome);
            try
            {
                if (MobileParty.MainParty == null) return;
                // The Green Draught: while the rare-weed communion holds, a draw may
                // cost the land nothing at all — you are, for a day, part of it.
                if (NatureKnowledge.WeedBlessingActive
                    && _rng.NextDouble() < LivingEnergyMath.WeedFreeDrawChance)
                {
                    Msg("The land gives freely — the green in you answers, and nothing is spent.");
                    return;
                }
                _lastGather = LivingEnergy.DrawNature(MobileParty.MainParty.GetPosition2D, el, announce: true, drainMult: DrainMult);
            }
            catch { }
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

        private static void Msg(string text)
        {
            try { InformationManager.DisplayMessage(new InformationMessage(text, new Color(0.35f, 0.75f, 0.35f))); }
            catch { }
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
