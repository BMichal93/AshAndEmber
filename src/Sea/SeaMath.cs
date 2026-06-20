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
        public const float StormChancePerVoyage    = 0.15f;
        public const float FogChancePerVoyage      = 0.20f;
        public const float FloatsamChancePerVoyage = 0.25f;
        public const float PirateChanceFloor       = 0.12f;
        public const float PirateChanceCeiling     = 0.40f;

        public const int FogBurnAgingDays      = 1;
        public const int SenseWreckAgingDays   = 1;
        // Chance of spotting ship survivors mid-crossing.
        public const float SurvivorChancePerVoyage = 0.20f;
        // Sea serpent sighting — rare, unsettling.
        public const float SerpentChancePerVoyage  = 0.10f;
        // Aging cost for a mage to commune with the serpent.
        public const int   SerpentAgingDays        = 2;

        // ── Nature-mage sea abilities (HP cost instead of aging days) ─────────
        // Drawing on the living world to still waters, sense the deep, and call
        // the current — all paid in the seer's own blood, not years.
        public const int StillWatersHpCost    = 20;   // halve crossing + ward storms
        public const int FogPartHpCost        = 15;   // part the sea fog
        public const int SenseWreckHpCost     = 15;   // feel the wreck / survivors
        public const int CallCurrentHpCost    = 25;   // fight aid vs corsairs / blockade
        public const int SerpentCommuneHpCost = 20;   // commune with the deep

        // ── Boarding battle ──────────────────────────────────────────────────
        public const int   SearTheTideAgingDays    = 3;
        public const float SearTheTideStrengthMult = 1.6f;
        public const float FleeStrengthPenalty     = 0.85f;
        public const float FleeEscapeChance        = 0.5f;

        // ── Trade ventures ───────────────────────────────────────────────────
        public const int MaxActiveVentures = 3;
        public const int BlessVentureAgingDays = 1;
        public static readonly int[] VentureTiers = { 500, 2000, 8000 };

        // ── Reagent cargo commissioning ──────────────────────────────────────
        // Four investment tiers: more gold → lower failure chance and higher yield.
        public struct ReagentCargoTier
        {
            public int   Cost;
            public float FailureChance;
            public int   Qty;
        }

        public static readonly ReagentCargoTier[] ReagentCargoTiers =
        {
            new ReagentCargoTier { Cost = 500,   FailureChance = 0.60f, Qty = 1 },
            new ReagentCargoTier { Cost = 2000,  FailureChance = 0.35f, Qty = 1 },
            new ReagentCargoTier { Cost = 6000,  FailureChance = 0.15f, Qty = 2 },
            new ReagentCargoTier { Cost = 15000, FailureChance = 0.05f, Qty = 3 },
        };

        // Only one reagent expedition may run at a time; duration is the natural gate.
        public const int ReagentExpeditionDaysMin  = 14;
        public const int ReagentExpeditionDaysRand = 15;  // range on top of min (14–28 days total)

        // ── NPC sea lanes ────────────────────────────────────────────────────
        // Lords and caravans leaving a harbor town may take ship instead of
        // marching. Lords only sail toward a destination their AI already
        // wants; caravans hop opportunistically between trade ports.
        public const float NpcMinCrossing        = 150f;  // shorter hops aren't worth the fare
        public const float NpcMaxCaravanCrossing = 700f;  // caravans stay on plausible trade legs
        public const float NpcLordSailChance       = 0.35f;
        public const float NpcCaravanSailChance   = 0.20f;
        public const float NpcInvasionSailChance  = 0.15f; // lord at war targeting enemy coastal port
        public const int   NpcSailCooldownDays    = 8;
        public const float NpcPortReachUnits      = 100f;  // AI target counts as "across the sea" within this of a port
        public const float PortProsperityPerDay   = 1f;    // sea commerce trickle for harbor towns

        // ── Blockades ────────────────────────────────────────────────────────
        // A lord party within this radius of a port contributes to its blockade.
        public const float BlockadeReachUnits     = 80f;

        // ── Ashen waters ─────────────────────────────────────────────────────
        // Crossing to a port held by the Ashen is markedly more perilous: the grey
        // waters off the cold coast do not forgive. Every hazard chance for such a
        // crossing is lifted by this factor (then re-clamped below 1).
        public const float AshenPortHazardMult = 1.6f;

        // Lifts a hazard probability when the destination port is Ashen-held.
        public static float AshenAdjusted(float baseChance, bool ashenDestination)
            => ashenDestination ? Clamp(baseChance * AshenPortHazardMult, 0f, 0.95f) : baseChance;

        public static bool NpcCrossingViable(float crossing, bool caravan)
            => crossing >= NpcMinCrossing && (!caravan || crossing <= NpcMaxCaravanCrossing);

        // Chance that a blockade fleet intercepts a crossing party.
        // Scales with the strength advantage of the blockader; clamped to [0.20, 0.90].
        public static float BlockadeInterceptChance(float blockadeStrength, float crosserStrength)
        {
            if (blockadeStrength <= 0f) return 0f;
            float ratio = blockadeStrength / Math.Max(1f, crosserStrength);
            return Clamp(0.60f * ratio, 0.20f, 0.90f);
        }

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

        // Flotsam gold: 80–400 denars scaled by roll.
        public static int FloatsamGold(double roll) => 80 + (int)(320 * roll);

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
