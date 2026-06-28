// =============================================================================
// ASH AND EMBER — Nature/LivingEnergy.cs
//
// The per-area reserve of living warmth. The world map is divided into coarse
// cells; each holds an energy reserve sized by the life that grows there (see
// LivingEnergyMath). A battlefield simply uses the cell its fight stands on, so a
// battle "establishes" its energy from the land it is fought over.
//
// Every draw of nature magic and every cast of fire — by the player OR by any NPC
// mage — spends some of the local cell's reserve. Drawn dry, the cell falls into
// debt: each further working bleeds a nearby village's hearth and may sour on the
// caster. Left in peace, a cell heals a little each day.
//
// Reserves persist in the campaign save (parallel lists, the project's idiom).
// Capacity and terrain are recomputed lazily from the cell coordinates, so only
// the live energy and warning state need storing.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace AshAndEmber
{
    // What a single draw did to the land.
    public struct DrawOutcome
    {
        public EnergyOmen Omen;     // a warning band freshly crossed (None if not)
        public bool Soured;         // the land bit back — apply caster recoil
        public bool BelowEmpty;     // the reserve is in debt
    }

    public static class LivingEnergy
    {
        // Coarse grid; ~10 map units a side. Fine enough to feel local, coarse
        // enough that a region shares one reserve.
        private const float CellSize = 10f;

        private static readonly Random _rng = new Random();

        private static readonly Dictionary<long, float>      _energy    = new Dictionary<long, float>();
        private static readonly Dictionary<long, int>        _announced = new Dictionary<long, int>();
        // Caches (recomputed from the cell coords; never serialised).
        private static readonly Dictionary<long, float>      _capacity  = new Dictionary<long, float>();
        private static readonly Dictionary<long, string>     _terrain   = new Dictionary<long, string>();

        private static readonly Color NatureColor = new Color(0.35f, 0.75f, 0.35f);

        // ── Cell maths ──────────────────────────────────────────────────────────
        private static long Key(Vec2 pos)
        {
            int cx = (int)Math.Floor(pos.x / CellSize);
            int cy = (int)Math.Floor(pos.y / CellSize);
            return ((long)cx << 32) ^ (uint)cy;
        }

        private static Vec2 CellCentre(long key)
        {
            int cx = (int)(key >> 32);
            int cy = (int)(key & 0xFFFFFFFFL);
            return new Vec2((cx + 0.5f) * CellSize, (cy + 0.5f) * CellSize);
        }

        private static string TerrainName(long key)
        {
            if (_terrain.TryGetValue(key, out string cached)) return cached;
            string name = "Plain";
            try
            {
                var wrapper = Campaign.Current?.MapSceneWrapper;
                if (wrapper != null)
                {
                    var t = wrapper.GetTerrainTypeAtPosition(new CampaignVec2(CellCentre(key), true));
                    name = t.ToString();
                }
            }
            catch { }
            _terrain[key] = name;
            return name;
        }

        private static float Capacity(long key)
        {
            if (_capacity.TryGetValue(key, out float c)) return c;
            c = LivingEnergyMath.AreaCapacity(TerrainName(key));
            _capacity[key] = c;
            return c;
        }

        private static float Energy(long key)
        {
            if (_energy.TryGetValue(key, out float e)) return e;
            e = Capacity(key);                 // an untouched place is brimming
            _energy[key] = e;
            return e;
        }

        // ── Public draws ──────────────────────────────────────────────────────
        // A nature draw of one charge of `el` at this map position. `drainMult`
        // lets a gentle-handed caster (Deep Earth) take less from the land. A nature
        // caster who forces an exhausted land suffers its backlash.
        public static DrawOutcome DrawNature(Vec2 mapPos, NatureElement el, bool announce, float drainMult = 1f)
        {
            long key = Key(mapPos);
            float amount = LivingEnergyMath.NatureDrain(el, TerrainName(key)) * drainMult;
            return ApplyDrain(key, mapPos, amount, announce, canBiteBack: true);
        }

        // A fire cast of `totalInputs` strokes at this map position (element-blind).
        // Inner Fire does not commune with the land — it merely BURNS it. So a fire
        // cast still spends the reserve (leaving the place exhausted for any nature
        // caster who follows), but the land cannot recoil on the fire mage in turn.
        public static DrawOutcome DrawFire(Vec2 mapPos, int totalInputs, bool announce)
        {
            long key = Key(mapPos);
            float amount = LivingEnergyMath.FireDrain(totalInputs, TerrainName(key));
            return ApplyDrain(key, mapPos, amount, announce, canBiteBack: false);
        }

        private static DrawOutcome ApplyDrain(long key, Vec2 mapPos, float amount, bool announce, bool canBiteBack)
        {
            var outcome = new DrawOutcome();
            float cap = Capacity(key);
            float before = Energy(key);
            float beforeFrac = LivingEnergyMath.Fraction(before, cap);

            float after = before - amount;
            float floor = LivingEnergyMath.MinReserve(cap);
            if (after < floor) after = floor;
            _energy[key] = after;

            float afterFrac = LivingEnergyMath.Fraction(after, cap);

            // Warning band — announce once per band, to the player only.
            int announced = _announced.TryGetValue(key, out int a) ? a : 0;
            EnergyOmen omen = LivingEnergyMath.OmenCrossed(beforeFrac, afterFrac, (EnergyOmen)announced);
            if (omen != EnergyOmen.None)
            {
                _announced[key] = (int)omen;
                outcome.Omen = omen;
                if (announce) Msg(LivingEnergyMath.OmenText(omen));
            }

            // In debt: the land takes from the living things around it, and may sour
            // on the one who forced it — but only a nature caster commands the land
            // closely enough for it to bite back. Fire merely burns it dry.
            if (after < 0f)
            {
                outcome.BelowEmpty = true;
                if (canBiteBack)
                {
                    try { BleedNearestHearth(mapPos, announce); } catch { }
                    if (_rng.NextDouble() < LivingEnergyMath.SourChance)
                        outcome.Soured = true;
                }
            }
            return outcome;
        }

        // ── Hearth toll ─────────────────────────────────────────────────────────
        private static void BleedNearestHearth(Vec2 mapPos, bool announce)
        {
            Settlement nearest = null;
            float best = float.MaxValue;
            foreach (var s in Settlement.All)
            {
                if (s == null || !s.IsVillage || s.Village == null) continue;
                float d = (s.GetPosition2D - mapPos).LengthSquared;
                if (d < best) { best = d; nearest = s; }
            }
            if (nearest == null) return;
            try
            {
                float h = nearest.Village.Hearth;
                nearest.Village.Hearth = Math.Max(10f, h - LivingEnergyMath.HearthToll);
            }
            catch { }
            if (announce)
                Msg($"The exhausted land takes what it must — the hearth of {nearest.Name} dims.");
        }

        // ── Daily regrowth ────────────────────────────────────────────────────
        public static void DailyRegen()
        {
            foreach (long key in _energy.Keys.ToList())
            {
                float cap = Capacity(key);
                float e = _energy[key] + LivingEnergyMath.DailyRegen(cap);
                if (e >= cap)
                {
                    // Fully healed — forget the cell to keep the save lean.
                    _energy.Remove(key);
                    _announced.Remove(key);
                    continue;
                }
                _energy[key] = e;
                // Recovering past a band re-arms its warning.
                _announced[key] = (int)LivingEnergyMath.LevelOf(LivingEnergyMath.Fraction(e, cap));
            }
        }

        // ── Lifecycle ───────────────────────────────────────────────────────────
        public static void ResetForNewGame()
        {
            _energy.Clear();
            _announced.Clear();
            _capacity.Clear();
            _terrain.Clear();
        }

        // Drop the lazily-cached terrain/capacity (they are reloaded on demand).
        // Energy/announce state is preserved across save loads.
        public static void ClearCaches()
        {
            _capacity.Clear();
            _terrain.Clear();
        }

        public static void Save(IDataStore store)
        {
            var xs = new List<int>();
            var ys = new List<int>();
            var es = new List<float>();
            var an = new List<int>();
            foreach (var kv in _energy)
            {
                xs.Add((int)(kv.Key >> 32));
                ys.Add((int)(kv.Key & 0xFFFFFFFFL));
                es.Add(kv.Value);
                an.Add(_announced.TryGetValue(kv.Key, out int a) ? a : 0);
            }
            store.SyncData("NATURE_EnergyCellX", ref xs);
            store.SyncData("NATURE_EnergyCellY", ref ys);
            store.SyncData("NATURE_EnergyValue", ref es);
            store.SyncData("NATURE_EnergyOmen",  ref an);

            _energy.Clear();
            _announced.Clear();
            if (xs != null && ys != null && es != null)
            {
                int n = Math.Min(xs.Count, Math.Min(ys.Count, es.Count));
                for (int i = 0; i < n; i++)
                {
                    long key = ((long)xs[i] << 32) ^ (uint)ys[i];
                    _energy[key] = es[i];
                    if (an != null && i < an.Count) _announced[key] = an[i];
                }
            }
            // Recompute terrain/capacity lazily next time they are needed.
            _capacity.Clear();
            _terrain.Clear();
        }

        private static void Msg(string text)
            => InformationManager.DisplayMessage(new InformationMessage(text, NatureColor));
    }
}
