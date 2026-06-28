// =============================================================================
// ASH AND EMBER — Miracles/MiracleCatalog.cs
//
// Static definitions for the six Grace miracles (golden light). Pure data;
// no TaleWorlds types. The sequence string mirrors the MiracleMath constants
// so the input handler and the menu can both show the same notation to the player.
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
        CleansingRite   = 5,
        PyreOfJudgement = 6,
        HallowedGround  = 7,
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
                UsableInBattle = true, UsableOnMap = true, Gate = MiracleGate.OneVirtue,
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

            new MiracleDef {
                Type = MiracleType.CleansingRite, IsGrace = true,
                Name = "Cleansing Rite",
                Effect = "Sacred fire sweeps cold and dread from those near you — fear and frost both yield to the flame.",
                Flavour = "You say nothing. The light does not require words. What the cold laid upon them simply lifts.",
                UsableInBattle = true, UsableOnMap = true, Gate = MiracleGate.OneVirtue,
                Sequence = MiracleMath.SeqCleansingRite },

            new MiracleDef {
                Type = MiracleType.PyreOfJudgement, IsGrace = true,
                Name = "Pyre of Judgement",
                Effect = "A pillar of consecrated fire falls where your eyes are fixed — searing all who stand beneath it and hurling them from the light.",
                Flavour = "There is no mercy in the verdict and no appeal from the sentence. The light has looked upon them, and found them wanting.",
                UsableInBattle = true, UsableOnMap = false, Gate = MiracleGate.AllVirtues,
                Sequence = MiracleMath.SeqPyreJudgement },

            new MiracleDef {
                Type = MiracleType.HallowedGround, IsGrace = true,
                Name = "Hallowed Ground",
                Effect = "The earth around you is consecrated — neither cold nor curse can touch those who stand within the light, and their wounds begin to close.",
                Flavour = "Draw the circle. Speak the old words. Within it, the dark is only dark — it has no hands here.",
                UsableInBattle = true, UsableOnMap = false, Gate = MiracleGate.OneVirtue,
                Sequence = MiracleMath.SeqHallowedGround },
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
