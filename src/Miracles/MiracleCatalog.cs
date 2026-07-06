// =============================================================================
// ASH AND EMBER — Miracles/MiracleCatalog.cs
//
// The Grace miracles (golden light). This is not a second power granted from
// outside — it is the same Fire the rest of the mod draws on, called through
// emotional and intellectual alignment rather than a drawn cone. Each of the
// five PERSONALITY traits is a different way of holding that alignment, and
// grants two miracles — one for battle, one for the campaign map — once the
// hero holds that trait at +1 or higher: the Fire answers a settled, honest
// state of mind, not a rank of piety. The old honor/mercy/generosity "virtue
// gates" are gone; the trait IS the gate.
//
// Flavour text should never write "the light" as a watching, judging, or
// granting party — it is the visible shape of the caster's own resonance
// with the Fire. If a line reads like a deity deciding someone's worth,
// rewrite it so the caster (or their own conviction) is doing the deciding.
//
// Two further miracles (The Undivided Flame, battle; The Reckoning, map) answer
// only once all five traits are held at the gate at once — RequiresAllTraits —
// rather than a single trait. Their damage leans hard on the two things that
// resist the Fire: the Ashen's cold, and the Kindled's untempered wildness.
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

        // Answer only once all five traits stand at the gate at once.
        UndividedFlame = 10, // All five — battle: nova, devastating to Ashen and the Kindled
        Reckoning      = 11, // All five — map:    strikes the nearest Ashen and wild elemental bands
    }

    public struct MiracleDef
    {
        public MiracleType Type;
        public GraceTrait  Trait;          // ignored when RequiresAllTraits is set
        public bool        IsGrace;
        public bool        RequiresAllTraits; // The Undivided Flame / The Reckoning: all five at once
        public string      Name;
        public string      Effect;
        public string      Flavour;
        public bool        UsableInBattle;
        public bool        UsableOnMap;
        public string      Sequence;

        public string Context => UsableInBattle ? "battle only" : "field only";

        // The Undivided Flame / The Reckoning ask a heavier toll than an ordinary
        // prayer — twice the Grace, win or fizzle, matching how much rarer their
        // gate is to meet in the first place.
        public int GraceCost => RequiresAllTraits ? 2 : 1;

        // Shown on the litany so the player knows which virtue unlocks it.
        public string GateNote => RequiresAllTraits ? "[all five virtues +1]" : $"[{TraitName} +1]";

        // Used where a locked prayer explains itself to the player.
        public string GateExplanation => RequiresAllTraits
            ? "it answers only to one who holds all five virtues at once, each at +1 or higher"
            : $"it is granted by the {TraitName} (that trait must stand at +1 or higher)";

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
                Effect = "Feeling for the ones beside you turns the Fire gentle — your wounds close, and theirs closest to you.",
                Flavour = "Not a colder fire, held at a distance. The same one, held differently — the part of you that reaches for another before itself.",
                UsableInBattle = true, UsableOnMap = false,
                Sequence = MiracleMath.SeqMercyMend },
            new MiracleDef {
                Type = MiracleType.MercyRelief, Trait = GraceTrait.Mercy, IsGrace = true,
                Name = "The Mending Road",
                Effect = "The party's wounded mend faster, and the worst of their hurts is eased.",
                Flavour = "No surgeon worked this. Something in you settled overnight, and the carts are lighter for it by morning.",
                UsableInBattle = false, UsableOnMap = true,
                Sequence = MiracleMath.SeqMercyRelief },

            // ── Valor ───────────────────────────────────────────────────────────
            new MiracleDef {
                Type = MiracleType.ValorFury, Trait = GraceTrait.Valor, IsGrace = true,
                Name = "Light of Valour",
                Effect = "Your certainty catches like dry kindling — courage and speed surge through those near you.",
                Flavour = "The officers stop shouting. The men already know what to do — something in the air said so before anyone spoke it.",
                UsableInBattle = true, UsableOnMap = false,
                Sequence = MiracleMath.SeqValorFury },
            new MiracleDef {
                Type = MiracleType.ValorMarch, Trait = GraceTrait.Valor, IsGrace = true,
                Name = "The Long March",
                Effect = "The column finds its second wind — morale lifts and the miles fall away faster.",
                Flavour = "No one calls a halt. They want to be there before the sky dims — your certainty is catching, same as it always does.",
                UsableInBattle = false, UsableOnMap = true,
                Sequence = MiracleMath.SeqValorMarch },

            // ── Honor ───────────────────────────────────────────────────────────
            new MiracleDef {
                Type = MiracleType.HonorAegis, Trait = GraceTrait.Honor, IsGrace = true,
                Name = "Aegis of the Oath",
                Effect = "Your own resolve hardens into a golden ward — you hold more than your body alone should.",
                Flavour = "The priests call it faith given shape. What actually holds you is plainer than that: conviction, made solid. It holds until it doesn't.",
                UsableInBattle = true, UsableOnMap = false,
                Sequence = MiracleMath.SeqHonorAegis },
            new MiracleDef {
                Type = MiracleType.HonorOath, Trait = GraceTrait.Honor, IsGrace = true,
                Name = "The Sworn Word",
                Effect = "An oath spoken in full conviction steadies a wavering town, or warms a lord toward you.",
                Flavour = "Honour is not a feeling. It is a debt you choose to carry — and something in you, not above you, keeps the ledger.",
                UsableInBattle = false, UsableOnMap = true,
                Sequence = MiracleMath.SeqHonorOath },

            // ── Generosity ──────────────────────────────────────────────────────
            new MiracleDef {
                Type = MiracleType.GraceBlessing, Trait = GraceTrait.Generosity, IsGrace = true,
                Name = "Shared Light",
                Effect = "Your own warmth widens to the ground around you — those within are warded against cold and curse, and their wounds begin to close.",
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
                Effect = "A pillar of fire falls where your eyes are fixed — the same clarity that weighs a ledger weighs a life, and sears all beneath it.",
                Flavour = "There is no mercy in the verdict, because you rendered it, and no appeal, because you don't grant them. The Fire only carries out what you had already decided.",
                UsableInBattle = true, UsableOnMap = false,
                Sequence = MiracleMath.SeqInsightPyre },
            new MiracleDef {
                Type = MiracleType.InsightSight, Trait = GraceTrait.Calculating, IsGrace = true,
                Name = "Far-Sight",
                Effect = "Your own attention, kindled, reaches out along the roads — what moves on them, and how close it has come.",
                Flavour = "Foresight is only attention, paid early. The Fire doesn't see for you — it just lengthens your reach.",
                UsableInBattle = false, UsableOnMap = true,
                Sequence = MiracleMath.SeqInsightSight },

            // ── All five, at once ──────────────────────────────────────────────
            new MiracleDef {
                Type = MiracleType.UndividedFlame, Trait = GraceTrait.Calculating, IsGrace = true,
                RequiresAllTraits = true,
                Name = "The Undivided Flame",
                Effect = "Every part of you agrees at once, and the Fire answers whole — those beside you are warded and mended, and the Ashen's cold and the Kindled's untempered wildness burn hardest of all.",
                Flavour = "Mercy does not flinch. Honour does not calculate. Calculation does not hesitate. For one breath there is no seam left in you for the Fire to catch on — so it simply pours through.",
                UsableInBattle = true, UsableOnMap = false,
                Sequence = MiracleMath.SeqUndividedFlame },
            new MiracleDef {
                Type = MiracleType.Reckoning, Trait = GraceTrait.Calculating, IsGrace = true,
                RequiresAllTraits = true,
                Name = "The Reckoning",
                Effect = "Cast out from a whole heart, the Fire finds what resists it nearby — the grey banners and the wild, untempered kindling alike — and answers both.",
                Flavour = "It does not ask which is worse: the cold that refuses the Fire, or the flame that never learned to mean anything. It only asks which is nearest.",
                UsableInBattle = false, UsableOnMap = true,
                Sequence = MiracleMath.SeqReckoning },
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
