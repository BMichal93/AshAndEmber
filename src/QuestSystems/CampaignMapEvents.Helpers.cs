// =============================================================================
// ASH AND EMBER — CampaignMapEvents.Helpers.cs
// Throttle, elapsed-days, seasonal, and party-spawn helpers.
// Partial of CampaignMapEvents (shared state lives in CampaignMapEvents.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class CampaignMapEvents
    {
        // ── Event throttle helpers ────────────────────────────────────────────
        // Called by each TryFireXxx after its probability roll succeeds.
        // Returns true (and claims the slot) if no event has fired this tick yet.
        // Returns false if another event already claimed the slot this tick.
        private static bool TryClaimWeeklySlot()
        {
            if (_weeklySlotFilled) return false;
            _weeklySlotFilled = true;
            return true;
        }

        // War-triggering events use their own slot so they never compete with
        // Ashen / political / seasonal events for the main weekly slot.
        private static bool TryClaimWarSlot()
        {
            if (_warSlotFilled) return false;
            _warSlotFilled = true;
            return true;
        }

        // ── Elapsed-days helper ───────────────────────────────────────────────
        // Returns days elapsed since the campaign started.
        // Falls back to absolute ToDays for saves loaded without the start-day record.
        // Internal: also used by BurningLabQuestSystem for its day-gated trigger.
        internal static double ElapsedCampaignDays()
            => _campaignStartDay >= 0
               ? Math.Max(0.0, CampaignTime.Now.ToDays - _campaignStartDay)
               : CampaignTime.Now.ToDays;

        // ── Seasonal helpers ──────────────────────────────────────────────────
        // Use Bannerlord's own GetSeasonOfYear property (returns CampaignTime.Seasons enum)
        // so season detection matches exactly what the player sees on screen.
        private static bool IsWinter() => CampaignTime.Now.GetSeasonOfYear == CampaignTime.Seasons.Winter;
        private static bool IsSummer() => CampaignTime.Now.GetSeasonOfYear == CampaignTime.Seasons.Summer;
        private static bool IsSpring()  => CampaignTime.Now.GetSeasonOfYear == CampaignTime.Seasons.Spring;
        private static bool IsAutumn()  => CampaignTime.Now.GetSeasonOfYear == CampaignTime.Seasons.Autumn;

        // Sets Town loyalty and security to max so code-driven captures don't
        // immediately trigger a rebellion on the next game tick.
        private static void StabiliseSettlement(Settlement s)
        {
            if (s?.Town == null) return;
            try { s.Town.Loyalty  = 100f; } catch { }
            try { s.Town.Security = 100f; } catch { }
        }

        // ── Whisper network intel ─────────────────────────────────────────────
        // At Whisper Tier 3, the player's cold-touched network occasionally surfaces
        // early warnings about conditions that precede major world events.
        // Fires at ~30% chance per weekly tick; does not claim the weekly event slot.
        private static void TryFireWhisperIntel()
        {
            if (!MageKnowledge.IsMage) return;
            if (MageKnowledge.WhisperTier < 3) return;
            if (_rng.NextDouble() >= 0.30) return;

            int day = (int)ElapsedCampaignDays();
            var candidates = new System.Collections.Generic.List<string>();

            if (!_ashenGambitFired && day >= AshenGambitEarliestDay - 28)
                candidates.Add(
                    "The whispers are clearest on this: something moves in the Empire's shadow — something that was waiting there.");
            if (!_undyingHostFired && day >= UndyingHostEarliestDay)
                candidates.Add(
                    "Your informants describe a great stillness in the north. Armies that have stopped moving. Not retreating — gathering.");
            if (_longNightDaysRemaining > 0)
                candidates.Add(
                    "The cold feeds the night. Your whispers say it is not weather — it has intention.");
            if (_brokenWillFired < BrokenWillMaxFires && day >= BrokenWillEarliestDay)
                candidates.Add(
                    "A court gone strange — your network sends that much. Lords who stare at maps without sleeping. Something turns them.");

            if (candidates.Count == 0)
            {
                string[] generic = {
                    "The whispers return with nothing new — only a shape you can't name, moving through your contacts.",
                    "Your informants are afraid. They won't say of what, which tells you more than words would.",
                    "Something passed through the grey roads last night. No tracks. Your outriders found warm ash where a fire had been.",
                };
                candidates.Add(generic[_rng.Next(generic.Length)]);
            }

            InformationManager.DisplayMessage(new InformationMessage(
                candidates[_rng.Next(candidates.Count)],
                new Color(0.50f, 0.70f, 0.90f)));
        }

        // ── Public spawn entry point ──────────────────────────────────────────
        // Allows SettlementEncounters to spawn a gate-ambush Ashen party near a
        // settlement without duplicating the spawn logic.
        public static void SpawnAshenAmbushNear(Vec2 pos, int troops, float minStrength)
            => SpawnAshenSpawnParty(pos, troops, minStrength);

        // Returns the spawned party so callers (e.g. settlement encounter combat
        // triggers) can pass it directly into PlayerEncounter.SetupFields.
        // `troops` here is the EXACT number of soldiers added (no 10× scaling) —
        // encounter battles describe small groups, not warbands.
        //
        // `ashen` selects the flavour of the foe: true → a renamed Ashen Spawn
        // party of thralls and invokers (the Cold Embrace circle); false → an
        // ordinary bandit band (the drunk retainer at the gate), never marked as
        // Ashen. Either way the roster is wiped to exactly `troops` so the looter
        // bandit-clan's default template can't pad it with stray looters.
        public static MobileParty SpawnCombatPartyAt(Vec2 pos, int troops, bool ashen = false)
            => SpawnAshenSpawnParty(pos, troops, 0f, exactTroops: true, ashen: ashen);

        // ── Party spawning helper ─────────────────────────────────────────────
        // Creates a single Ashen Spawn bandit party near anchorPos, registers
        // it with FireWorshippersSystem, and returns it (or null on failure).
        //
        // baseTroops   — starting sea_raider / mountain_bandit count
        // minStrength  — if > 0, tops up troops until Party.TotalStrength reaches this value
        //
        // Failure modes (all silent):
        //   • No bandit clan found in Clan.BanditFactions
        //   • BanditPartyComponent.CreateBanditParty returns null
        //   • Neither "sea_raider" nor "mountain_bandit" CharacterObject exists
        private static MobileParty SpawnAshenSpawnParty(Vec2 anchorPos, int baseTroops, float minStrength, bool exactTroops = false, bool ashen = true)
        {
            try
            {
                Clan banditClan = Clan.BanditFactions.FirstOrDefault(c => c != null && !c.IsEliminated);
                if (banditClan == null) return null;

                var pt = banditClan.DefaultPartyTemplate;
                if (pt == null) return null;

                // Bannerlord crashes on post-battle loot screen if the party's home hideout
                // is null — the engine tries to scatter survivors back to it.
                // Priority: bandit clan's own hideout → nearest hideout → any world hideout.
                // If nothing exists, bail out rather than passing null to CreateBanditParty.
                Hideout hideout = null;
                try
                {
                    Settlement hs = banditClan.Settlements.FirstOrDefault(s => s?.Hideout != null);
                    if (hs == null)
                        hs = Settlement.All
                            .Where(s => s?.Hideout != null)
                            .OrderBy(s => (s.GetPosition2D.x - anchorPos.x) * (s.GetPosition2D.x - anchorPos.x)
                                        + (s.GetPosition2D.y - anchorPos.y) * (s.GetPosition2D.y - anchorPos.y))
                            .FirstOrDefault();
                    if (hs == null)
                        hs = Settlement.All.FirstOrDefault(s => s?.Hideout != null);
                    hideout = hs?.Hideout;
                }
                catch { }
                if (hideout == null) return null; // no valid hideout → skip to prevent native crash

                // Scatter spawn point around the anchor (radius ≈ 4 map units)
                const float scatter = 4f;
                Vec2 spawnPos = anchorPos + new Vec2(
                    (float)(_rng.NextDouble() - 0.5) * scatter * 2f,
                    (float)(_rng.NextDouble() - 0.5) * scatter * 2f
                );
                var spawnCVec = new CampaignVec2(spawnPos, true);

                string partyId = "ashen_spawn_evt_" + _rng.Next(999999).ToString("D6");
                MobileParty party = BanditPartyComponent.CreateBanditParty(partyId, banditClan, hideout, false, pt, spawnCVec);
                if (party == null) return null;

                // An exact-count combat party must read as precisely what the event
                // describes, so wipe the bandit-clan template fill first — otherwise
                // the looter faction's default roster pads it with stray looters.
                if (exactTroops)
                    try { party.MemberRoster.Clear(); } catch { }

                // Ashen parties march as their own thralls; ordinary bandit bands use
                // looters. Each falls back through the vanilla bandit troops so the
                // spawn still succeeds if a custom troop is missing.
                CharacterObject troop = ashen
                    ? (MBObjectManager.Instance.GetObject<CharacterObject>("ashen_thrall")
                    ?? MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                    ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit"))
                    : (MBObjectManager.Instance.GetObject<CharacterObject>("looter")
                    ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit"));
                if (troop == null) return null;

                party.MemberRoster.AddToCounts(troop, exactTroops ? baseTroops : baseTroops * 10);

                // Top up to reach minimum strength requirement (rarely needed with 10× base)
                if (minStrength > 0f)
                {
                    int guard = 20; // safety cap — 20 × 5 = max 100 extra troops
                    while (party.Party.EstimatedStrength < minStrength && guard-- > 0)
                        party.MemberRoster.AddToCounts(troop, 5);
                }

                // Register with FireWorshippersSystem so IsAshenSpawn() returns true
                // and the party is renamed "Ashen Spawn" with invokers mixed in.
                // Ordinary bandit bands are left as the looter clan's own party.
                if (ashen)
                    FireWorshippersSystem.ForceMarkAsAshenSpawn(party);

                return party;
            }
            catch { return null; }
        }

    }
}
