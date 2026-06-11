// =============================================================================
// ASH AND EMBER — Sea/SeaMath.cs
//
// Pure numerical logic for the sea systems: passage fares, voyage durations,
// hazard odds, abstracted boarding-battle resolution, and trade venture
// outcomes. No TaleWorlds types — everything here is covered by
// tests/PureLogicTests.cs. All randomness is passed in as rolls in [0,1) so
// every outcome is deterministic and testable.
// =============================================================================

using System;

namespace AshAndEmber
{
    public struct SeaBattleOutcome
    {
        public bool  Victory;
        public float CasualtyFraction; // fraction of the party's soldiers struck down
        public int   LootGold;         // denars stripped from the corsair hulks (victory only)
    }

    public struct VentureOutcome
    {
        public bool Lost;   // cargo taken by corsairs or the sea
        public int  Payout; // denars returned to the investor (salvage if lost)
    }

    public static class SeaMath
    {
        // ── Voyage pacing ────────────────────────────────────────────────────
        // A cog under sail covers roughly twice what a marching column manages.
        public const float ShipUnitsPerHour   = 10f;
        public const float MinVoyageHours     = 6f;
        public const float EmberwindTimeMult  = 0.5f;
        public const int   EmberwindAgingDays = 2;

        // ── Hazards ──────────────────────────────────────────────────────────
        public const float StormChancePerVoyage = 0.15f;
        public const float PirateChanceFloor    = 0.12f;
        public const float PirateChanceCeiling  = 0.40f;

        // ── Boarding battle ──────────────────────────────────────────────────
        public const int   SearTheTideAgingDays    = 3;
        public const float SearTheTideStrengthMult = 1.6f;
        public const float FleeStrengthPenalty     = 0.85f;
        public const float FleeEscapeChance        = 0.5f;

        // ── Trade ventures ───────────────────────────────────────────────────
        public const int MaxActiveVentures = 3;
        public const int BlessVentureAgingDays = 1;
        public static readonly int[] VentureTiers = { 500, 2000, 8000 };

        public static float TravelHours(float distance, bool emberwind)
        {
            float h = Math.Max(MinVoyageHours, distance / ShipUnitsPerHour);
            if (emberwind) h = Math.Max(MinVoyageHours * 0.5f, h * EmberwindTimeMult);
            return h;
        }

        // Fare scales with the crossing and with how many mouths the captain feeds.
        // Rounded down to 10 denars, never below 50.
        public static int Fare(float distance, int partySize)
        {
            int raw = 100 + (int)(distance * 2f) + Math.Max(0, partySize) * 3;
            return Math.Max(50, raw / 10 * 10);
        }

        public static float PirateChance(float distance)
        {
            float c = PirateChanceFloor + distance / 2000f;
            return Clamp(c, PirateChanceFloor, PirateChanceCeiling);
        }

        // Abstract "fleet strength": men matter most; troop quality and the
        // captain's tactics tilt the boarding fight.
        public static float FleetStrength(int troopCount, float averageTier, int tacticsSkill, bool searTheTide)
        {
            float s = Math.Max(0, troopCount)
                    * (1f + Math.Max(0f, averageTier) * 0.25f)
                    * (1f + Math.Max(0, tacticsSkill) / 300f);
            if (searTheTide) s *= SearTheTideStrengthMult;
            return s;
        }

        // Corsair packs scale to the prize — 60%–130% of the player's strength,
        // heavier on the long, lawless crossings.
        public static float CorsairStrength(float playerStrength, float distance, double roll)
        {
            float distMult = 1f + Math.Min(0.3f, distance / 2000f);
            return playerStrength * (0.6f + 0.7f * (float)roll) * distMult;
        }

        public static SeaBattleOutcome ResolveSeaBattle(float playerStrength, float corsairStrength, double roll)
        {
            var o = new SeaBattleOutcome();
            if (playerStrength <= 0f)
            {
                o.Victory = false;
                o.CasualtyFraction = 0.35f;
                o.LootGold = 0;
                return o;
            }

            float effective = playerStrength * (0.75f + 0.5f * (float)roll);
            o.Victory = effective >= corsairStrength;

            float ratio = corsairStrength / playerStrength;
            o.CasualtyFraction = Clamp(ratio * (o.Victory ? 0.08f : 0.22f), 0.02f, 0.35f);
            o.LootGold = o.Victory ? (int)(corsairStrength * 3f) : 0;
            return o;
        }

        public static int TributeDemand(int fare, float corsairStrength)
            => Math.Max(200, fare * 2 + (int)(corsairStrength * 1.5f));

        public static int StormExtraHours(float remainingHours, double roll)
            => Math.Max(2, (int)(Math.Max(0f, remainingHours) * (0.15f + 0.25f * (float)roll)));

        // Round trip plus a day of haggling in the far port.
        public static int VentureDays(float distance)
            => Math.Max(3, (int)Math.Ceiling(TravelHours(distance, false) * 2f / 24f) + 1);

        public static float VentureLossChance(float distance, bool blessed)
        {
            float c = PirateChance(distance) * 0.6f;
            if (blessed) c *= 0.5f;
            return c;
        }

        // Margin: longer routes pay better; a mage's blessing sweetens the books.
        public static VentureOutcome ResolveVenture(int invested, float distance, bool blessed,
                                                    double lossRoll, double marginRoll)
        {
            var o = new VentureOutcome();
            if (lossRoll < VentureLossChance(distance, blessed))
            {
                o.Lost = true;
                o.Payout = invested / 4; // salvage — the insurers of Calradia are not generous
                return o;
            }
            float margin = 0.15f + Math.Min(0.35f, distance / 1000f * 0.35f) + 0.25f * (float)marginRoll;
            if (blessed) margin += 0.10f;
            o.Lost = false;
            o.Payout = invested + (int)(invested * margin);
            return o;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
