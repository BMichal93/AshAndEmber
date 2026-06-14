// =============================================================================
// ASH AND EMBER — Alchemy/AlchemyMath.cs
//
// Pure numerical logic for the Alchemy system: satchel capacity, Medicine brew
// odds, backfire selection, NPC gating, and all effect magnitudes. No TaleWorlds
// types — everything here is deterministic and covered by tests/PureLogicTests.cs.
// All randomness is passed in as rolls in [0,1) so outcomes stay testable.
//
// Design notes:
//   • A satchel holds elixirs equal to the carrier's Intelligence (min 1).
//   • Brewing always yields an elixir; a failed Medicine test taints it, so it
//     backfires on the drinker. NPCs are bound by the same rules.
//   • Effect magnitudes live here as constants so battle/campaign code and the
//     test suite agree on a single source of truth.
// =============================================================================

using System;

namespace AshAndEmber
{
    // The elixirs the alchemists of the deep south have committed to glass.
    public enum ElixirType
    {
        HealingDraught      = 0,  // restore a quarter of lifeblood (battle + map)
        EmberBrew           = 1,  // berserk: speed + striking power (battle)
        OathWine            = 2,  // lifts party morale (map)
        HearthsmokeCenser   = 3,  // burned by a village, swells its hearth (map)
        CausticVial         = 4,  // bursts in a caustic cloud around you (battle)
        StonebloodTonic     = 5,  // flesh turns to slag-stone, blunting blows (battle)
        FieldSurgeonPhiltre = 6,  // mends the wounded of your column (map)
        VeilOfAsh           = 7,  // a shroud nothing can touch, briefly (battle)
        HoarfrostDraught    = 8,  // a chilling burst — nearby foes slow and soften (battle)
        PyrebloodPhiltre    = 9,  // a second wind: closes wounds and hardens the skin (battle)
        MarrowmendTincture  = 10, // deep rest in a bottle: heals you and your wounded (map)
        KindlingCenser      = 11, // burned by a town, steadies its people (map)
    }

    // How well the brewer reads their own work after bottling it. Brewing no
    // longer announces clean-vs-tainted outright; a second test against the
    // brewer's Intelligence decides what they believe about the vial in hand.
    public enum BrewAppraisal
    {
        Correct,    // you read it true — you know whether it is sound
        Unknown,    // you cannot tell — the result is left in doubt
        Misleading, // you read it wrong — you are told the opposite of the truth
    }

    // What a tainted elixir does instead of its promise.
    public enum AlchemyBackfire
    {
        SelfWound      = 0, // the brew turns on the drinker
        TroopBlast     = 1, // it bursts, scalding your own
        MoraleCollapse = 2, // a foul reek breaks the column's spirit
        CreepingBlight = 3, // a slow poison gnaws over time
        Enfeeblement   = 4, // limbs go leaden, guard goes soft
    }

    public static class AlchemyMath
    {
        public const int ElixirTypeCount = 12;

        // ── Satchel capacity ─────────────────────────────────────────────────
        // One vial per point of Intelligence; never less than one so any hero
        // can carry at least a single draught.
        public static int CarryCapacity(int intelligence)
            => Math.Max(1, intelligence);

        // ── Brewing (Medicine) ───────────────────────────────────────────────
        // A novice surgeon (0 Medicine) still lands roughly one brew in five;
        // a master (300) almost never spoils the work. Linear between.
        public const float BrewBaseChance  = 0.20f;
        public const float BrewPerSkill    = 0.0030f;
        public const float BrewChanceFloor = 0.10f;
        public const float BrewChanceCeil  = 0.95f;

        public static float BrewSuccessChance(int medicineSkill)
        {
            float c = BrewBaseChance + medicineSkill * BrewPerSkill;
            return Clamp(c, BrewChanceFloor, BrewChanceCeil);
        }

        // True = clean elixir; false = tainted (will backfire on use).
        public static bool IsBrewSuccess(int medicineSkill, double roll)
            => roll < BrewSuccessChance(medicineSkill);

        // ── Reading the brew (Intelligence) ──────────────────────────────────
        // After bottling, a separate test against Intelligence decides what the
        // brewer learns:
        //   • roll lands in the low band → Correct  (you know the truth)
        //   • roll lands in the high band → Misleading (you believe the opposite)
        //   • anything between → Unknown (you cannot tell)
        // A sharp mind widens the "know" band and shrinks the "misled" band, but
        // never to certainty either way — alchemy keeps a little of its mystery.
        // Scaled for the attribute range (Intelligence is roughly 0–10, not 0–300).
        public const float ReadBaseChance     = 0.30f; // know-the-truth chance at 0 Intelligence
        public const float ReadPerPoint       = 0.060f;
        public const float ReadChanceFloor    = 0.20f;
        public const float ReadChanceCeil     = 0.90f;
        public const float MisreadBaseChance  = 0.22f; // believe-a-lie chance at 0 Intelligence
        public const float MisreadPerPoint    = 0.015f;
        public const float MisreadChanceFloor = 0.03f;

        public static float ReadTrueChance(int intelligence)
            => Clamp(ReadBaseChance + intelligence * ReadPerPoint, ReadChanceFloor, ReadChanceCeil);

        public static float MisreadChance(int intelligence)
            => Clamp(MisreadBaseChance - intelligence * MisreadPerPoint, MisreadChanceFloor, 1f);

        public static BrewAppraisal ReadBrew(int intelligence, double roll)
        {
            double r = Clamp01(roll);
            if (r < ReadTrueChance(intelligence)) return BrewAppraisal.Correct;
            if (r >= 1.0 - MisreadChance(intelligence)) return BrewAppraisal.Misleading;
            return BrewAppraisal.Unknown;
        }

        // Maps a [0,1) roll onto one of the five backfires (equal weight).
        public static AlchemyBackfire PickBackfire(double roll)
        {
            int idx = (int)(Clamp01(roll) * 5);
            if (idx > 4) idx = 4;
            return (AlchemyBackfire)idx;
        }

        // ── NPC gating ───────────────────────────────────────────────────────
        // Per-day chance an eligible lord/companion brews-and-uses an elixir off
        // screen. Aserai-cultured heroes are the practised hands; raw Medicine
        // skill widens the odds further. Deliberately small to avoid log spam.
        public const float NpcBrewBaseChance = 0.010f;
        public const float NpcBrewAseraiBonus = 0.020f;
        public const float NpcBrewPerSkill    = 0.00010f;
        public const float NpcBrewChanceCeil  = 0.060f;

        public static float NpcDailyBrewChance(int medicineSkill, bool aserai)
        {
            float c = NpcBrewBaseChance
                    + (aserai ? NpcBrewAseraiBonus : 0f)
                    + medicineSkill * NpcBrewPerSkill;
            return Clamp(c, 0f, NpcBrewChanceCeil);
        }

        // Per-tick chance an eligible NPC hero reaches for a vial mid-battle.
        // Same cultural/skill bias as the campaign tick, scaled for frequent ticks.
        public const float NpcBattleBaseChance  = 0.0008f;
        public const float NpcBattleAseraiBonus = 0.0016f;
        public const float NpcBattlePerSkill    = 0.000010f;
        public const float NpcBattleChanceCeil  = 0.0050f;

        public static float NpcBattleUseChance(int medicineSkill, bool aserai)
        {
            float c = NpcBattleBaseChance
                    + (aserai ? NpcBattleAseraiBonus : 0f)
                    + medicineSkill * NpcBattlePerSkill;
            return Clamp(c, 0f, NpcBattleChanceCeil);
        }

        // ── Effect magnitudes ────────────────────────────────────────────────
        // Healing Draught
        public const float HealFraction = 0.25f;

        // Ember Brew (berserk)
        public const float BerserkDurationSec = 20f;
        public const float BerserkSpeedMult   = 1.20f;
        public const int   BerserkBonusDamage = 8;     // extra damage per landed melee hit
        public const float BerserkSelfHeal    = 0.15f; // top-up on the rush of fury

        // Oath-Wine (morale)
        public const int OathWineMorale = 30;

        // Hearthsmoke Censer (village hearth)
        public const float HearthsmokeBoost  = 200f;
        public const float HearthsmokeRange  = 12f; // map units to find a village

        // Caustic Vial (battle AoE)
        public const float CausticRadius = 5f;
        public const float CausticDamage = 35f;

        // Stoneblood Tonic (damage resist)
        public const float ResistDurationSec = 25f;
        public const float ResistFraction    = 0.40f; // fraction of each blow shrugged off

        // Field Surgeon's Philtre (heal wounded troops)
        public const float SurgeonHealFraction = 0.50f;

        // Veil of Ash (ward)
        public const float VeilDurationSec = 10f;

        // Hoarfrost Draught (battle AoE debuff — slows and softens nearby foes;
        // reuses the enfeeble machinery so its speed/vulnerability match the brew's
        // own backfire, only turned outward onto the enemy).
        public const float HoarfrostRadius      = 6f;
        public const float HoarfrostDurationSec = 12f;

        // Pyreblood Philtre (battle second wind — heal + stone-skin resist)
        public const float PyrebloodHealFraction = 0.20f;

        // Marrowmend Tincture (map — full self-heal + mend wounded column)
        public const float MarrowmendHealFraction = 1.0f;

        // Kindling Censer (map — steadies the nearest town's people)
        public const float KindlingLoyalty  = 15f;
        public const float KindlingSecurity = 15f;
        public const float KindlingRange    = 14f; // map units to find a town

        // ── Backfire magnitudes ──────────────────────────────────────────────
        public const float BackfireSelfWoundFraction = 0.25f; // of max HP
        public const int   BackfireTroopBlastWounds  = 6;     // troops scalded
        public const int   BackfireMoraleDrop        = 25;
        public const float BackfireDotDurationSec    = 12f;
        public const float BackfireDotPerSecond      = 4f;
        public const float BackfireEnfeebleDuration  = 18f;
        public const float BackfireEnfeebleSpeedMult = 0.80f;
        public const float BackfireEnfeebleVuln      = 0.30f; // extra damage taken

        // ── helpers ──────────────────────────────────────────────────────────
        private static float Clamp(float v, float lo, float hi)
            => v < lo ? lo : (v > hi ? hi : v);

        private static double Clamp01(double v)
            => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
