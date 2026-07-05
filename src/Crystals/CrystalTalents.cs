// =============================================================================
// ASH AND EMBER — Crystals/CrystalTalents.cs
//
// The lapidary's craft — a small learnable upgrade layer for the focused-light
// crystals, studied with focus points at a Crystalline Chamber. These refine how
// the crystals answer you; they do not change the stones themselves.
//
//   Lasting Lattice   — the lattice holds; a crystal shatters far less often.
//   Mending Light     — the stored light looks after its bearer: each use mends you.
//   Brilliant Lattice — you draw the light more fiercely: crystals hit harder.
//
// (Slots 1 and 2 once held Waking Light and Swift Kindling — a night-casting
// unlock and a charge-speed cut. Crystals now fire instantly with no daylight
// gate, so both were dead; they are repurposed here. The enum's INT values are
// unchanged, so a save that owned the old talents keeps the new effect in the
// same slot.)
//
// Persisted in the save by CrystallinesCampaignBehavior. Bought at the Chamber
// (Study the lattice), each costing one focus point more than the last.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public enum CrystalTalentId
    {
        LastingLattice   = 0, // shatter far less often
        MendingLight     = 1, // each use mends the bearer
        BrilliantLattice = 2, // crystal damage & healing hit harder
    }

    public static class CrystalTalents
    {
        private static readonly CrystalTalentId[] _all =
            { CrystalTalentId.LastingLattice, CrystalTalentId.MendingLight, CrystalTalentId.BrilliantLattice };

        private static readonly HashSet<CrystalTalentId> _owned = new HashSet<CrystalTalentId>();

        // ── The hooks the crystal system reads ───────────────────────────────────
        public static bool  Has(CrystalTalentId id) => _owned.Contains(id);
        public static float ShatterMult => Has(CrystalTalentId.LastingLattice) ? 0.4f : 1f;
        public static float MendOnUse   => Has(CrystalTalentId.MendingLight)     ? 25f  : 0f;
        public static float PotencyMult => Has(CrystalTalentId.BrilliantLattice) ? 1.3f : 1f;

        public static int OwnedCount => _owned.Count;

        // Shared gentle curve (1,1,2,2,2,3,…), as every talent tree uses.
        public static int NextCost() => TalentCostCurve.Cost(OwnedCount);

        public static bool TryLearn(CrystalTalentId id, out string message)
        {
            message = "";
            if (Has(id)) { message = $"You have already mastered {Name(id)}."; return false; }
            int cost = NextCost();
            var hero = Hero.MainHero;
            int have = 0;
            try { have = hero?.HeroDeveloper?.UnspentFocusPoints ?? 0; } catch { }
            if (have < cost)
            {
                message = $"{Name(id)} asks {cost} focus point{(cost != 1 ? "s" : "")}; you have {have}.";
                return false;
            }
            try { hero.HeroDeveloper.UnspentFocusPoints -= cost; } catch { }
            _owned.Add(id);
            message = $"You learn {Name(id)}. ({cost} focus point{(cost != 1 ? "s" : "")})";
            return true;
        }

        // ── The Chamber's study menu ──────────────────────────────────────────────
        public static void ShowCodex()
        {
            int have = 0;
            try { have = Hero.MainHero?.HeroDeveloper?.UnspentFocusPoints ?? 0; } catch { }
            int cost = NextCost();

            var elements = new List<InquiryElement>();
            foreach (var id in _all)
            {
                bool known = Has(id);
                elements.Add(new InquiryElement((int)id,
                    $"{Name(id)}  —  {(known ? "[known]" : $"{cost} fp")}", null, !known, Effect(id)));
            }

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    $"Study the Lattice   [Focus: {have}]",
                    $"The Chamber's keepers will teach the lapidary's finer craft. The next costs {cost} focus " +
                    $"point{(cost != 1 ? "s" : "")}. Choose what to learn.",
                    elements, true, 1, 1, "Study", "Close",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var id = (CrystalTalentId)(int)chosen[0].Identifier;
                        if (TryLearn(id, out string msg))
                            InformationManager.DisplayMessage(new InformationMessage(msg, Glow));
                        else
                            InformationManager.DisplayMessage(new InformationMessage(msg, Dim));
                    },
                    null, "", false), false, true);
            }
            catch { }
        }

        public static string Name(CrystalTalentId id)
        {
            switch (id)
            {
                case CrystalTalentId.LastingLattice: return "Lasting Lattice";
                case CrystalTalentId.MendingLight:   return "Mending Light";
                default:                             return "Brilliant Lattice";
            }
        }

        public static string Effect(CrystalTalentId id)
        {
            switch (id)
            {
                case CrystalTalentId.LastingLattice: return "Lasting Lattice — the crystal's structure holds; it shatters far less often when you draw from it.";
                case CrystalTalentId.MendingLight:   return "Mending Light — the stored light looks after its bearer; every crystal you loose also mends you (+25 HP).";
                default:                             return "Brilliant Lattice — you draw the light more fiercely; a crystal's damage and healing strike about a third harder.";
            }
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────────
        public static void ResetForNewGame() => _owned.Clear();

        public static void Save(IDataStore store)
        {
            try
            {
                var owned = _owned.Select(t => (int)t).ToList();
                store.SyncData("CRYSTAL_Talents", ref owned);
                if (owned != null)
                {
                    _owned.Clear();
                    foreach (int i in owned) _owned.Add((CrystalTalentId)i);
                }
            }
            catch { }
        }

        private static readonly Color Glow = new Color(0.7f, 0.85f, 1.0f);
        private static readonly Color Dim  = new Color(0.7f, 0.68f, 0.6f);
    }
}
