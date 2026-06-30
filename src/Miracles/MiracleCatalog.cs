// =============================================================================
// ASH AND EMBER — Miracles/MiracleCatalog.cs
//
// The Grace miracles (golden light). Each of the five PERSONALITY traits grants
// two miracles — one for battle, one for the campaign map — once the hero holds
// that trait at +1 or higher (the virtuous are heard; the rest are not). The old
// honor/mercy/generosity "virtue gates" are gone; the trait IS the gate.
//
// Pure data — no TaleWorlds types. The sequence string mirrors the MiracleMath
// constants so the input handler and the menu show the same notation.
// =============================================================================

using System.Collections.Generic;
using System.Linq;

namespace AshAndEmber
{
    // The five Bannerlord personality traits that grant Grace miracles.
    public enum GraceTrait { Mercy, Valor, Honor, Generosity, Calculating }

    public enum MiracleType
    {
        MercyMend     = 0,  // Mercy — battle: heal self + nearby allies
        MercyRelief   = 1,  // Mercy — map:    mend the party's wounded
        ValorFury     = 2,  // Valor — battle: courage + speed surge
        ValorMarch    = 3,  // Valor — map:    forced march (morale + speed)
        HonorAegis    = 4,  // Honor — battle: golden ward
        HonorOath     = 5,  // Honor — map:    sworn word (loyalty / relations)
        GraceBlessing = 6,  // Generosity — battle: shared light (ward + heal allies)
        GraceBounty   = 7,  // Generosity — map:    bounty (food + morale)
        InsightPyre   = 8,  // Calculating — battle: pillar of judgement
        InsightSight  = 9,  // Calculating — map:    far-sight (scout the roads)
    }

    public struct MiracleDef
    {
        public MiracleType Type;
        public GraceTrait  Trait;
        public bool        IsGrace;
        public string      Name;
        public string      Effect;
        public string      Flavour;
        public bool        UsableInBattle;
        public bool        UsableOnMap;
        public string      Sequence;

        public string Context => UsableInBattle ? "battle only" : "field only";

        // Shown on the litany so the player knows which virtue unlocks it.
        public string GateNote => $"[{TraitName} +1]";

        public string TraitName =>
            Trait == GraceTrait.Mercy       ? "Merciful"
            : Trait == GraceTrait.Valor       ? "Valorous"
            : Trait == GraceTrait.Honor       ? "Honourable"
            : Trait == GraceTrait.Generosity  ? "Generous"
            :                                   "Calculating";
    }

    public static class MiracleCatalog
    {
        private static readonly List<MiracleDef> _defs = new List<MiracleDef>
        {
            // ── Mercy ───────────────────────────────────────────────────────────
            new MiracleDef {
                Type = MiracleType.MercyMend, Trait = GraceTrait.Mercy, IsGrace = true,
                Name = "Radiant Mending",
                Effect = "The light closes your wounds and the wounds of those standing near you.",
                Flavour = "A warmth that has nothing to do with fire — the kind that stays after the flame has gone.",
                UsableInBattle = true, UsableOnMap = false,
                Sequence = MiracleMath.SeqMercyMend },
            new MiracleDef {
                Type = MiracleType.MercyRelief, Trait = GraceTrait.Mercy, IsGrace = true,
                Name = "The Mending Road",
                Effect = "The party's wounded mend faster, and the worst of their hurts is eased.",
                Flavour = "No surgeon worked this. The carts are simply lighter by morning.",
                UsableInBattle = false, UsableOnMap = true,
                Sequence = MiracleMath.SeqMercyRelief },

            // ── Valor ───────────────────────────────────────────────────────────
            new MiracleDef {
                Type = MiracleType.ValorFury, Trait = GraceTrait.Valor, IsGrace = true,
                Name = "Light of Valour",
                Effect = "A steady glow settles over your line — courage and speed surge through those near you.",
                Flavour = "The officers stop shouting. The men already know what to do. They always did.",
                UsableInBattle = true, UsableOnMap = false,
                Sequence = MiracleMath.SeqValorFury },
            new MiracleDef {
                Type = MiracleType.ValorMarch, Trait = GraceTrait.Valor, IsGrace = true,
                Name = "The Long March",
                Effect = "The column finds its second wind — morale lifts and the miles fall away faster.",
                Flavour = "No one calls a halt. They want to be there before the light fades.",
                UsableInBattle = false, UsableOnMap = true,
                Sequence = MiracleMath.SeqValorMarch },

            // ── Honor ───────────────────────────────────────────────────────────
            new MiracleDef {
                Type = MiracleType.HonorAegis, Trait = GraceTrait.Honor, IsGrace = true,
                Name = "Aegis of the Oath",
                Effect = "A golden ward settles around you — more life than your body alone should hold.",
                Flavour = "The priests say faith is a shield. They mean it literally. It holds until it doesn't.",
                UsableInBattle = true, UsableOnMap = false,
                Sequence = MiracleMath.SeqHonorAegis },
            new MiracleDef {
                Type = MiracleType.HonorOath, Trait = GraceTrait.Honor, IsGrace = true,
                Name = "The Sworn Word",
                Effect = "An oath spoken in the light steadies a wavering town, or warms a lord toward you.",
                Flavour = "Honour is not a feeling. It is a contract, and the light is its witness.",
                UsableInBattle = false, UsableOnMap = true,
                Sequence = MiracleMath.SeqHonorOath },

            // ── Generosity ──────────────────────────────────────────────────────
            new MiracleDef {
                Type = MiracleType.GraceBlessing, Trait = GraceTrait.Generosity, IsGrace = true,
                Name = "Shared Light",
                Effect = "The ground around you is consecrated — those within are warded against cold and curse, and their wounds begin to close.",
                Flavour = "Draw the circle. What you have, you give. Within it, the dark has no hands.",
                UsableInBattle = true, UsableOnMap = false,
                Sequence = MiracleMath.SeqGraceBlessing },
            new MiracleDef {
                Type = MiracleType.GraceBounty, Trait = GraceTrait.Generosity, IsGrace = true,
                Name = "The Open Hand",
                Effect = "The stores are fuller than they were, and the column eats well tonight.",
                Flavour = "There was enough. There is always enough, for the hand that opens first.",
                UsableInBattle = false, UsableOnMap = true,
                Sequence = MiracleMath.SeqGraceBounty },

            // ── Calculating ─────────────────────────────────────────────────────
            new MiracleDef {
                Type = MiracleType.InsightPyre, Trait = GraceTrait.Calculating, IsGrace = true,
                Name = "Pyre of Judgement",
                Effect = "A pillar of consecrated fire falls where your eyes are fixed — searing all beneath it and hurling them from the light.",
                Flavour = "There is no mercy in the verdict and no appeal. The light has looked, and found them wanting.",
                UsableInBattle = true, UsableOnMap = false,
                Sequence = MiracleMath.SeqInsightPyre },
            new MiracleDef {
                Type = MiracleType.InsightSight, Trait = GraceTrait.Calculating, IsGrace = true,
                Name = "Far-Sight",
                Effect = "The light shows you the roads — what moves on them, and how close it has come.",
                Flavour = "Foresight is only attention, paid early. The light merely lengthens your reach.",
                UsableInBattle = false, UsableOnMap = true,
                Sequence = MiracleMath.SeqInsightSight },
        };

        public static IReadOnlyList<MiracleDef> All    => _defs;
        public static IEnumerable<MiracleDef> GraceAll => _defs;

        public static MiracleDef Get(MiracleType type) => _defs.First(d => d.Type == type);

        public static bool TryGetBySequence(string seq, out MiracleDef def)
        {
            def = default;
            foreach (var d in _defs)
                if (d.Sequence == seq) { def = d; return true; }
            return false;
        }
    }
}
