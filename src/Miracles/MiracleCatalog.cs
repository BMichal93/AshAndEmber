// =============================================================================
// ASH AND EMBER — Miracles/MiracleCatalog.cs
//
// Static definitions for the ten miracles — five of Grace (golden light) and
// five of Cold (blue). Pure data; no TaleWorlds types. The sequence string
// mirrors the MiracleMath constants so the input handler and the menu can both
// show the same notation to the player.
// =============================================================================

using System.Collections.Generic;
using System.Linq;

namespace AshAndEmber
{
    public enum MiracleType
    {
        // Grace miracles
        RepelAshen      = 0,
        RadiantMending  = 1,
        LightOfGuidance = 2,
        SacredFlame     = 3,
        AegisOfFaith    = 4,
        // Cold miracles
        AshenCurse      = 10,
        Dreadmending    = 11,
        DreadPresence   = 12,
        FrostBrand      = 13,
        ShadowShroud    = 14,
    }

    public struct MiracleDef
    {
        public MiracleType Type;
        public bool        IsGrace;
        public string      Name;
        public string      Effect;
        public string      Flavour;
        public bool        UsableInBattle;
        public bool        UsableOnMap;
        public MiracleGate Gate;
        public string      Sequence;

        public string Context =>
            UsableInBattle && UsableOnMap ? "battle or field"
            : UsableInBattle              ? "battle only"
            :                               "field only";

        public string GateNote =>
            Gate == MiracleGate.AllVirtues ? "[full virtue]"
            : Gate == MiracleGate.OneVirtue? "[some virtue]"
            : "";
    }

    public static class MiracleCatalog
    {
        private static readonly List<MiracleDef> _defs = new List<MiracleDef>
        {
            // ── Grace Miracles ─────────────────────────────────────────────────
            new MiracleDef {
                Type = MiracleType.RepelAshen, IsGrace = true,
                Name = "Repel the Ashen",
                Effect = "A wave of consecrated light scorches all Ashen in a wide radius and breaks their resolve.",
                Flavour = "The flame does not argue with the cold. It simply burns, and the grey things remember what heat means.",
                UsableInBattle = true, UsableOnMap = true, Gate = MiracleGate.AllVirtues,
                Sequence = MiracleMath.SeqRepelAshen },

            new MiracleDef {
                Type = MiracleType.RadiantMending, IsGrace = true,
                Name = "Radiant Mending",
                Effect = "The light closes your wounds and the wounds of those standing near you.",
                Flavour = "A warmth that has nothing to do with fire. The kind that stays after the flame has gone.",
                UsableInBattle = true, UsableOnMap = true, Gate = MiracleGate.None,
                Sequence = MiracleMath.SeqRadiantMending },

            new MiracleDef {
                Type = MiracleType.LightOfGuidance, IsGrace = true,
                Name = "Light of Guidance",
                Effect = "A steady glow settles over the column — soldiers fight with clearer eyes and quieter fear.",
                Flavour = "The officers stop shouting. The men already know what to do. They always did.",
                UsableInBattle = true, UsableOnMap = true, Gate = MiracleGate.OneVirtue,
                Sequence = MiracleMath.SeqLightOfGuidance },

            new MiracleDef {
                Type = MiracleType.SacredFlame, IsGrace = true,
                Name = "Sacred Flame",
                Effect = "Your blade burns with consecrated fire — each blow carries the weight of the rite.",
                Flavour = "The sword doesn't know what it's touching. The flame does. That is enough.",
                UsableInBattle = true, UsableOnMap = false, Gate = MiracleGate.OneVirtue,
                Sequence = MiracleMath.SeqSacredFlame },

            new MiracleDef {
                Type = MiracleType.AegisOfFaith, IsGrace = true,
                Name = "Aegis of Faith",
                Effect = "A golden ward settles around you — more life than your body alone should hold.",
                Flavour = "The priests say faith is a shield. They mean this literally. The shield holds until it doesn't.",
                UsableInBattle = true, UsableOnMap = false, Gate = MiracleGate.AllVirtues,
                Sequence = MiracleMath.SeqAegisOfFaith },

            // ── Cold Miracles ──────────────────────────────────────────────────
            new MiracleDef {
                Type = MiracleType.AshenCurse, IsGrace = false,
                Name = "Ashen Curse",
                Effect = "Cold dark light tears at all nearby enemies — the stone's anger given form.",
                Flavour = "You feel it leave you and go looking for something to break. It is not particular about what.",
                UsableInBattle = true, UsableOnMap = true, Gate = MiracleGate.AllVirtues,
                Sequence = MiracleMath.SeqAshenCurse },

            new MiracleDef {
                Type = MiracleType.Dreadmending, IsGrace = false,
                Name = "Dreadmending",
                Effect = "Life bleeds from those nearby and flows into you — the cold keeps its own accounts.",
                Flavour = "It heals. You try not to think about where the warmth came from.",
                UsableInBattle = true, UsableOnMap = true, Gate = MiracleGate.None,
                Sequence = MiracleMath.SeqDreadmending },

            new MiracleDef {
                Type = MiracleType.DreadPresence, IsGrace = false,
                Name = "Dread Presence",
                Effect = "Something steps into the air around you — nearby enemies flinch, slow, and step back.",
                Flavour = "The men who face you don't run. They simply stop advancing. Then they back up. Then they run.",
                UsableInBattle = true, UsableOnMap = true, Gate = MiracleGate.OneVirtue,
                Sequence = MiracleMath.SeqDreadPresence },

            new MiracleDef {
                Type = MiracleType.FrostBrand, IsGrace = false,
                Name = "Frost Brand",
                Effect = "Each blow from your weapon leaves a chill — the struck slow and stumble.",
                Flavour = "The cold is patient. It does not need to hurt you quickly.",
                UsableInBattle = true, UsableOnMap = false, Gate = MiracleGate.OneVirtue,
                Sequence = MiracleMath.SeqFrostBrand },

            new MiracleDef {
                Type = MiracleType.ShadowShroud, IsGrace = false,
                Name = "Shadow Shroud",
                Effect = "Darkness clings to you — the blows that reach you find less of you there.",
                Flavour = "The cold wraps around you like a coat. What passes through it is less than what entered.",
                UsableInBattle = true, UsableOnMap = false, Gate = MiracleGate.AllVirtues,
                Sequence = MiracleMath.SeqShadowShroud },
        };

        public static IReadOnlyList<MiracleDef> All   => _defs;
        public static IEnumerable<MiracleDef> GraceAll => _defs.Where(d => d.IsGrace);
        public static IEnumerable<MiracleDef> ColdAll  => _defs.Where(d => !d.IsGrace);

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
