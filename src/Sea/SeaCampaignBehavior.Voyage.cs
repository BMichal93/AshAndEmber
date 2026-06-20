// =============================================================================
// ASH AND EMBER — Sea/SeaCampaignBehavior.Voyage.cs
// The player voyage: wait-menu, arrival, and all crossing hazards.
// Partial of SeaCampaignBehavior (shared static state lives in SeaCampaignBehavior.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class SeaCampaignBehavior
    {
        // =====================================================================
        // SEA TRAVEL
        // =====================================================================
        private static void StartVoyage(Settlement dest)
        {
            try
            {
                var here = Settlement.CurrentSettlement;
                if (!IsPort(here) || !IsPort(dest) || here == dest) return;

                float dist = PortDistance(here, dest);
                int fare = SeaMath.Fare(dist, PartySize());
                if (Hero.MainHero.Gold < fare)
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The captain looks at your purse and shakes his head. The fare is {fare} denars."));
                    return;
                }

                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, fare, true);
                _fareEscrow = fare;

                _voyageOrigin       = here;
                _voyageDest         = dest;
                bool emberwindUsed  = _emberwindCalled;
                bool natureCalmUsed = _stillWatersCalled;
                _emberwindCalled    = false;
                _stillWatersCalled  = false;
                _voyageEmberwind    = emberwindUsed || natureCalmUsed;
                _voyageNatureCalm   = natureCalmUsed && !emberwindUsed;
                _voyageHoursTotal   = SeaMath.TravelHours(dist, _voyageEmberwind);
                _voyageHoursElapsed = 0f;
                _voyageDone         = false;

                // Roll the crossing's hazards up front and schedule them at a
                // random point in the middle stretch of the voyage. Crossings bound
                // for an Ashen-held port run a far greater risk of every hazard.
                bool ashenDest = IsAshenPort(dest);
                _pirateAtHour = _rng.NextDouble() < SeaMath.AshenAdjusted(SeaMath.PirateChance(dist), ashenDest)
                    ? _voyageHoursTotal * (0.25f + 0.5f * (float)_rng.NextDouble()) : -1f;
                _stormAtHour = !_voyageEmberwind && _rng.NextDouble() < SeaMath.AshenAdjusted(SeaMath.StormChancePerVoyage, ashenDest)
                    ? _voyageHoursTotal * (0.25f + 0.5f * (float)_rng.NextDouble()) : -1f;

                // Fog settles in the early-to-middle stretch; Emberwind burns it clear.
                _fogAtHour = !_voyageEmberwind && _rng.NextDouble() < SeaMath.AshenAdjusted(SeaMath.FogChancePerVoyage, ashenDest)
                    ? _voyageHoursTotal * (0.15f + 0.35f * (float)_rng.NextDouble()) : -1f;

                // A wrecked vessel drifts into view in the middle of the crossing.
                _floatsamAtHour = _rng.NextDouble() < SeaMath.AshenAdjusted(SeaMath.FloatsamChancePerVoyage, ashenDest)
                    ? _voyageHoursTotal * (0.30f + 0.40f * (float)_rng.NextDouble()) : -1f;

                // Shipwreck survivors in a boat — a moral decision mid-crossing.
                _survivorsAtHour = _rng.NextDouble() < SeaMath.AshenAdjusted(SeaMath.SurvivorChancePerVoyage, ashenDest)
                    ? _voyageHoursTotal * (0.20f + 0.50f * (float)_rng.NextDouble()) : -1f;

                // A sea serpent surfaces — rare even in lawless waters.
                _serpentAtHour = _rng.NextDouble() < SeaMath.SerpentChancePerVoyage
                    ? _voyageHoursTotal * (0.35f + 0.35f * (float)_rng.NextDouble()) : -1f;

                // Check for a blockade at the destination. The encounter fires
                // near the end of the crossing — the party is committed by then.
                _blockadeAtHour = -1f; _blockadeFaction = null; _blockadeStrength = 0f;
                try
                {
                    if (Hero.MainHero?.MapFaction != null)
                    {
                        var blk = HostileBlockade(dest, Hero.MainHero.MapFaction);
                        if (blk.HasValue)
                        {
                            _blockadeAtHour   = _voyageHoursTotal * 0.80f;
                            _blockadeFaction  = blk.Value.Faction;
                            _blockadeStrength = blk.Value.Strength;
                        }
                    }
                }
                catch { }

                GameMenu.SwitchToMenu("sea_voyage");
            }
            catch { }
        }

        private static void ResetVoyageState()
        {
            _voyageOrigin       = null;
            _voyageDest         = null;
            _voyageHoursTotal   = 0f;
            _voyageHoursElapsed = 0f;
            _pirateAtHour       = -1f;
            _stormAtHour        = -1f;
            _fogAtHour          = -1f;
            _floatsamAtHour     = -1f;
            _survivorsAtHour    = -1f;
            _serpentAtHour      = -1f;
            _blockadeAtHour     = -1f;
            _blockadeFaction    = null;
            _blockadeStrength   = 0f;
            _voyageEmberwind    = false;
            _voyageNatureCalm   = false;
            _voyageDone         = false;
        }

        private static void VoyageOnInit(MenuCallbackArgs args)
        {
            try
            {
                UpdateVoyageText();
                args.MenuContext.GameMenu.StartWait();
                args.MenuContext.GameMenu.SetTargetedWaitingTimeAndInitialProgress(
                    Math.Max(1f, _voyageHoursTotal), 0f);
            }
            catch { }
        }

        private static bool VoyageOnCondition(MenuCallbackArgs args) => true;

        private static void VoyageOnConsequence(MenuCallbackArgs args)
        {
            // The engine fires this when the hours targeted at init elapse —
            // but storms and failed escapes lengthen the crossing mid-voyage.
            // If there is water left, re-arm the wait instead of arriving early.
            try
            {
                if (_voyageDone || _voyageDest == null) return;
                float remaining = _voyageHoursTotal - _voyageHoursElapsed;
                if (remaining <= 0.01f) { Arrive(); return; }
                args.MenuContext.GameMenu.StartWait();
                args.MenuContext.GameMenu.SetTargetedWaitingTimeAndInitialProgress(
                    Math.Max(1f, remaining), 0f);
            }
            catch { try { Arrive(); } catch { } }
        }

        private static void VoyageOnTick(MenuCallbackArgs args, CampaignTime dt)
        {
            try
            {
                if (_voyageDone || _voyageDest == null) return;
                _voyageHoursElapsed += (float)dt.ToHours;

                if (_stormAtHour >= 0f && _voyageHoursElapsed >= _stormAtHour)
                {
                    _stormAtHour = -1f;
                    FireStorm();
                }
                if (_fogAtHour >= 0f && _voyageHoursElapsed >= _fogAtHour)
                {
                    _fogAtHour = -1f;
                    FireFog();
                }
                if (_floatsamAtHour >= 0f && _voyageHoursElapsed >= _floatsamAtHour)
                {
                    _floatsamAtHour = -1f;
                    FireFlotsam();
                }
                if (_survivorsAtHour >= 0f && _voyageHoursElapsed >= _survivorsAtHour)
                {
                    _survivorsAtHour = -1f;
                    FireSurvivors();
                }
                if (_serpentAtHour >= 0f && _voyageHoursElapsed >= _serpentAtHour)
                {
                    _serpentAtHour = -1f;
                    FireSeaSerpent();
                }
                if (_pirateAtHour >= 0f && _voyageHoursElapsed >= _pirateAtHour)
                {
                    _pirateAtHour = -1f;
                    FireCorsairs();
                }
                if (_blockadeAtHour >= 0f && _voyageHoursElapsed >= _blockadeAtHour)
                {
                    _blockadeAtHour = -1f;
                    FireBlockade();
                }

                UpdateVoyageText();
                try
                {
                    args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(
                        Math.Min(1f, _voyageHoursElapsed / Math.Max(1f, _voyageHoursTotal)));
                }
                catch { }

                if (_voyageHoursElapsed >= _voyageHoursTotal)
                    Arrive();
            }
            catch { }
        }

        private static void UpdateVoyageText()
        {
            try
            {
                int left = Math.Max(0, (int)(_voyageHoursTotal - _voyageHoursElapsed));
                string wind = _voyageNatureCalm
                    ? " The waters lie still beneath the hull — the deep holds its breath."
                    : (_voyageEmberwind ? " The Emberwind hums in the rigging." : "");
                MBTextManager.SetTextVariable("SEA_VOYAGE_TEXT",
                    $"At sea, bound for {_voyageDest?.Name}.{wind} The coast is a smudge behind you. About {left} hour(s) of water remain.");
            }
            catch { }
        }

        private static void Arrive()
        {
            if (_voyageDone) return;
            _voyageDone = true;
            try
            {
                var dest = _voyageDest;
                _fareEscrow = 0;

                // Step off the origin's menus/encounter, drop the party at the
                // destination's gate, and let the engine walk it in normally.
                try
                {
                    if (TaleWorlds.CampaignSystem.Encounters.PlayerEncounter.Current != null)
                        TaleWorlds.CampaignSystem.Encounters.PlayerEncounter.Finish(true);
                }
                catch { }

                if (dest != null)
                {
                    var main = MobileParty.MainParty;
                    try { main.Position = dest.GatePosition; } catch { }
                    try { main.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); } catch { }

                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The ship noses into {dest.Name}. Land legs come back slowly.",
                        new Color(0.65f, 0.75f, 0.9f)));
                }
                ResetVoyageState();
            }
            catch { }
        }

        // ── Storm ──────────────────────────────────────────────────────────────
        private static void FireStorm()
        {
            try
            {
                float remaining = Math.Max(0f, _voyageHoursTotal - _voyageHoursElapsed);
                int extra = SeaMath.StormExtraHours(remaining, _rng.NextDouble());
                _voyageHoursTotal += extra;
                int hurt = ApplySeaCasualties(MobileParty.MainParty, 0.05f);

                string body =
                    "The sky goes the color of wet slate and the sea stands up. The crew lash down what " +
                    "they can and pray to whatever listens out here. " +
                    $"The storm costs you {extra} hour(s) of hard-won water" +
                    (hurt > 0 ? $" and leaves {hurt} of your soldiers battered below decks." : ".");
                InformationManager.ShowInquiry(new InquiryData(
                    "⛈  Storm", body, true, false, "Ride it out.", "", null, null), true);
            }
            catch { }
        }

        // ── Sea Fog ────────────────────────────────────────────────────────────
        private static void FireFog()
        {
            try
            {
                int extra = 3 + _rng.Next(3); // 3–5 hours lost if slowing down or unlucky push

                var options = new List<InquiryElement>
                {
                    new InquiryElement("slow", "Heave to and sound the lead", null, true,
                        $"Take it careful. The coast finds you eventually — adds {extra} hours."),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("burn",
                        $"Burn it away ({SeaMath.FogBurnAgingDays} days aging)", null, true,
                        "Push a thread of the Inner Fire through the air. The fog boils off clean — no delay, no danger."));
                if (NatureKnowledge.IsAttuned)
                {
                    bool canAffordFog = Hero.MainHero.HitPoints > SeaMath.FogPartHpCost + 10;
                    options.Add(new InquiryElement("part",
                        $"Part the fog ({SeaMath.FogPartHpCost} HP)", null, canAffordFog,
                        canAffordFog
                            ? "Call to the wind. The living air parts the grey curtain like a hand through smoke — no delay."
                            : "You do not have enough left to give. The land does not take the dying."));
                }
                options.Add(new InquiryElement("push",
                    "Push through — the captain swears he knows these waters", null, true,
                    "Even odds. Either you thread the channel cleanly, or something hard finds the hull."));

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "🌫  Sea Fog",
                    "The fog comes down like a curtain — twenty feet of visibility, no horizon, no stars. " +
                    "The helmsman is steering by feel and prayer.",
                    options, false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        string pick = chosen?[0]?.Identifier as string ?? "slow";
                        switch (pick)
                        {
                            case "burn":
                                try { AgingSystem.AgeHero(Hero.MainHero, SeaMath.FogBurnAgingDays); } catch { }
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    "A breath of the Inner Fire and the fog tears apart like cloth. " +
                                    "The crew stares. The crossing continues."));
                                break;
                            case "part":
                                try { Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - SeaMath.FogPartHpCost); } catch { }
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    "You reach into the air and call. The wind stirs, warm and deliberate, " +
                                    "and the fog peels back on both sides. The crew does not ask how. " +
                                    "The crossing continues."));
                                break;
                            case "push":
                                if (_rng.NextDouble() < 0.5)
                                {
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "The captain threads it. The fog lifts after a tense hour and open water spreads ahead."));
                                }
                                else
                                {
                                    int hurt = ApplySeaCasualties(MobileParty.MainParty, 0.04f);
                                    _voyageHoursTotal += extra;
                                    InformationManager.ShowInquiry(new InquiryData(
                                        "🌫  Sea Fog — Hard Landing",
                                        "Something solid materializes out of the grey — a reef, or the shoulder of a headland. " +
                                        "The hull scrapes and holds, but " +
                                        (hurt > 0 ? $"{hurt} men are thrown about and hurt" : "the crew is badly shaken") +
                                        $". It takes {extra} hours to find open water again.",
                                        true, false, "Limp on.", "", null, null), true);
                                }
                                break;
                            default: // slow
                                _voyageHoursTotal += extra;
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    $"The captain shortens sail and takes it slow. The fog burns off eventually — {extra} hours behind schedule."));
                                break;
                        }
                    },
                    null, "", false), true, true);
            }
            catch { }
        }

        // ── Flotsam ────────────────────────────────────────────────────────────
        private static void FireFlotsam()
        {
            try
            {
                var options = new List<InquiryElement>
                {
                    new InquiryElement("salvage",
                        "Heave to and put men on the wreck", null, true,
                        "Board the hulk and strip what the sea left behind. Adds 2 hours."),
                    new InquiryElement("pass",
                        "Leave it. Dead ships keep their own time.", null, true,
                        "Sail past and stay on schedule."),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("sense",
                        $"Read the wreck ({SeaMath.SenseWreckAgingDays} days aging)", null, true,
                        "Let the Inner Fire taste the hull — feel where coin and cargo lay heaviest. Finds more than blind hands would."));
                if (NatureKnowledge.IsAttuned)
                {
                    bool canAffordSense = Hero.MainHero.HitPoints > SeaMath.SenseWreckHpCost + 10;
                    options.Add(new InquiryElement("feel",
                        $"Feel the wreck ({SeaMath.SenseWreckHpCost} HP)", null, canAffordSense,
                        canAffordSense
                            ? "Let the living world taste the wood — feel where hands last gripped, where life last moved. More yield than blind salvage."
                            : "You do not have enough left to give."));
                }

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "⚓  Flotsam",
                    "A dark shape rolls in the swell ahead — a trading cog, or what's left of one. No sail, no crew visible. " +
                    "The flag she flew has been torn away. She could be days dead, or hours.",
                    options, false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        string pick = chosen?[0]?.Identifier as string ?? "pass";
                        switch (pick)
                        {
                            case "salvage":
                            {
                                _voyageHoursTotal += 2f;
                                int gold = SeaMath.FloatsamGold(_rng.NextDouble());
                                if (gold > 0)
                                    try { GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, gold, true); } catch { }
                                InformationManager.ShowInquiry(new InquiryData(
                                    "⚓  Flotsam — Salvaged",
                                    "Your men go over the side with ropes. The hold has been ransacked — corsairs, most likely — " +
                                    $"but the bilges still yielded {gold} denars of overlooked coin and goods. Two hours behind schedule.",
                                    true, false, "Back on course.", "", null, null), true);
                                break;
                            }
                            case "sense":
                            {
                                try { AgingSystem.AgeHero(Hero.MainHero, SeaMath.SenseWreckAgingDays); } catch { }
                                _voyageHoursTotal += 1f;
                                int gold = SeaMath.FloatsamGold(_rng.NextDouble()) * 2;
                                if (gold > 0)
                                    try { GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, gold, true); } catch { }
                                InformationManager.ShowInquiry(new InquiryData(
                                    "⚓  Flotsam — Sensed",
                                    "The Inner Fire finds the warm spots — where hands last gripped, where coin lay heaviest. " +
                                    $"Your men follow the warmth and pull {gold} denars from the wreck before it rolls and sinks.",
                                    true, false, "Back on course.", "", null, null), true);
                                break;
                            }
                            case "feel":
                            {
                                try { Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - SeaMath.SenseWreckHpCost); } catch { }
                                _voyageHoursTotal += 1f;
                                int gold = SeaMath.FloatsamGold(_rng.NextDouble()) * 2;
                                if (gold > 0)
                                    try { GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, gold, true); } catch { }
                                InformationManager.ShowInquiry(new InquiryData(
                                    "⚓  Flotsam — Felt",
                                    "You reach into the timbers and feel the memory of the wood. Life passed here, and coin changed hands. " +
                                    $"Your men follow the current of it and surface with {gold} denars before the hull slips under.",
                                    true, false, "Back on course.", "", null, null), true);
                                break;
                            }
                            default: // pass
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    "You sail past. The wreck slowly turns in the current, keeping its secrets."));
                                break;
                        }
                    },
                    null, "", false), true, true);
            }
            catch { }
        }

        // ── Corsairs ───────────────────────────────────────────────────────────
        private static void FireCorsairs()
        {
            try
            {
                float dist = _voyageOrigin != null && _voyageDest != null
                    ? PortDistance(_voyageOrigin, _voyageDest) : 300f;
                float playerStr  = FleetStrengthOf(MobileParty.MainParty, searTheTide: false);
                float corsairStr = SeaMath.CorsairStrength(Math.Max(1f, playerStr), dist, _rng.NextDouble());
                int fare    = SeaMath.Fare(dist, PartySize());
                int tribute = SeaMath.TributeDemand(fare, corsairStr);

                string odds = corsairStr > playerStr * 1.1f ? "They have the numbers."
                            : corsairStr < playerStr * 0.8f ? "They may have picked the wrong hull."
                            : "It could go either way.";

                var options = new List<InquiryElement>
                {
                    new InquiryElement("fight", "Repel boarders — steel on the gunwales", null, true,
                        $"An honest boarding fight. {odds}"),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("sear", $"Sear the Tide ({SeaMath.SearTheTideAgingDays} days aging)", null, true,
                        "Open the Inner Fire over open water. Burning rigging, screaming corsairs, and much better odds."));
                if (NatureKnowledge.IsAttuned)
                {
                    bool canAffordCurrent = Hero.MainHero.HitPoints > SeaMath.CallCurrentHpCost + 10;
                    options.Add(new InquiryElement("current",
                        $"Call the Current ({SeaMath.CallCurrentHpCost} HP)", null, canAffordCurrent,
                        canAffordCurrent
                            ? "Reach into the water and pull. A rogue current catches their hulls and throws their formation. Much better odds."
                            : "You do not have enough left to give."));
                }
                options.Add(new InquiryElement("tribute", $"Pay tribute ({tribute} denars)", null, Hero.MainHero.Gold >= tribute,
                    Hero.MainHero.Gold >= tribute
                        ? "Coin buys passage. It always has."
                        : "You cannot afford what they're asking."));
                options.Add(new InquiryElement("flee", "Crowd sail and run", null, true,
                    "Half the time the wind loves you. The other half, they board you winded and angry."));

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "☠  Corsairs",
                    "Sails on the horizon — low, fast hulls that don't fly any kingdom's colors. They've seen you, " +
                    "and they're turning. The captain looks at you, because you're the one with soldiers.",
                    options, false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        string pick = chosen?[0]?.Identifier as string ?? "fight";
                        switch (pick)
                        {
                            case "sear":
                                try { AgingSystem.AgeHero(Hero.MainHero, SeaMath.SearTheTideAgingDays); } catch { }
                                ResolveBoardingFight(FleetStrengthOf(MobileParty.MainParty, searTheTide: true), corsairStr);
                                break;
                            case "current":
                                try { Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - SeaMath.CallCurrentHpCost); } catch { }
                                ResolveBoardingFight(FleetStrengthOf(MobileParty.MainParty, searTheTide: true), corsairStr);
                                break;
                            case "tribute":
                                try { GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, tribute, true); } catch { }
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    $"The corsairs take {tribute} denars and sheer away, already hunting the next sail."));
                                break;
                            case "flee":
                                if (_rng.NextDouble() < SeaMath.FleeEscapeChance)
                                {
                                    _voyageHoursTotal += 4f;
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "The wind holds. The low hulls fall away astern — four hours lost beating off course."));
                                }
                                else
                                {
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "They cut the angle and close. The fight comes anyway, on their terms."));
                                    ResolveBoardingFight(playerStr * SeaMath.FleeStrengthPenalty, corsairStr);
                                }
                                break;
                            default:
                                ResolveBoardingFight(playerStr, corsairStr);
                                break;
                        }
                    },
                    null, "", false), true, true);
            }
            catch { }
        }

        private static void ResolveBoardingFight(float playerStr, float corsairStr)
        {
            try
            {
                var outcome = SeaMath.ResolveSeaBattle(playerStr, corsairStr, _rng.NextDouble());
                int hurt = ApplySeaCasualties(MobileParty.MainParty, outcome.CasualtyFraction);

                if (outcome.Victory)
                {
                    if (outcome.LootGold > 0)
                        try { GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, outcome.LootGold, true); } catch { }
                    try { GainRenownAction.Apply(Hero.MainHero, 3f); } catch { }
                    InformationManager.ShowInquiry(new InquiryData(
                        "☠  Corsairs — Repelled",
                        "It is ugly, close work between the rails, and then it is over. The corsairs that can still " +
                        $"swim, swim. You strip {outcome.LootGold} denars from their hulks" +
                        (hurt > 0 ? $", though {hurt} of your soldiers paid for the privilege." : "."),
                        true, false, "Sail on.", "", null, null), true);
                }
                else
                {
                    int stolen = Math.Min(Hero.MainHero.Gold, Math.Max(100, Hero.MainHero.Gold * 15 / 100));
                    if (stolen > 0)
                        try { GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, stolen, true); } catch { }
                    InformationManager.ShowInquiry(new InquiryData(
                        "☠  Corsairs — Overrun",
                        "They take the deck and hold it long enough to take everything else. " +
                        $"{hurt} of your soldiers are cut down or pulled into the water, and {stolen} denars leave " +
                        "with the corsairs. They let the ship limp on — a stripped hull is tomorrow's customer.",
                        true, false, "Endure it.", "", null, null), true);
                }
            }
            catch { }
        }

        // ── Blockade encounter ─────────────────────────────────────────────────
        private static void FireBlockade()
        {
            try
            {
                float playerStr = FleetStrengthOf(MobileParty.MainParty, searTheTide: false);
                float blkStr    = _blockadeStrength;
                string fName    = _blockadeFaction?.Name?.ToString() ?? "hostile ships";

                string odds = blkStr > playerStr * 1.1f ? "Their fleet outguns yours."
                            : blkStr < playerStr * 0.8f ? "Your fleet should carry it."
                            : "It will be a bloody approach.";

                var options = new List<InquiryElement>
                {
                    new InquiryElement("fight", "Force the harbor — break through the line", null, true,
                        $"An assault on the blockade fleet. {odds}"),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("sear",
                        $"Sear the Tide ({SeaMath.SearTheTideAgingDays} days aging)", null, true,
                        "Open the Inner Fire over the blockade line. Burning rigging, broken formation, and much better odds."));
                if (NatureKnowledge.IsAttuned)
                {
                    bool canAffordLine = Hero.MainHero.HitPoints > SeaMath.CallCurrentHpCost + 10;
                    options.Add(new InquiryElement("current_blk",
                        $"Call the Current ({SeaMath.CallCurrentHpCost} HP)", null, canAffordLine,
                        canAffordLine
                            ? "Reach into the water beneath the blockade. A surge of current capsizes galleys and scatters the line. Better odds of forcing the harbor."
                            : "You do not have enough left to give."));
                }
                options.Add(new InquiryElement("turn", "Turn back — the harbor is denied today", null, true,
                    "Abort the crossing and return to your port of origin. Your fare will be refunded."));

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "⚓  Blockade",
                    $"War galleys flying {fName} colors hold the harbor mouth. They have formed a line " +
                    "and they are not moving. You are still a good hour out, but there is no other way in.",
                    options, false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        string pick = chosen?[0]?.Identifier as string ?? "fight";
                        float effectiveStr = playerStr;
                        if (pick == "sear")
                        {
                            try { AgingSystem.AgeHero(Hero.MainHero, SeaMath.SearTheTideAgingDays); } catch { }
                            effectiveStr = FleetStrengthOf(MobileParty.MainParty, searTheTide: true);
                        }
                        else if (pick == "current_blk")
                        {
                            try { Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - SeaMath.CallCurrentHpCost); } catch { }
                            effectiveStr = FleetStrengthOf(MobileParty.MainParty, searTheTide: true);
                        }
                        if (pick == "turn")
                            TurnBackFromBlockade();
                        else
                            ResolveBlockadeBattle(effectiveStr, blkStr);
                    },
                    null, "", false), true, true);
            }
            catch { }
        }

        private static void ResolveBlockadeBattle(float playerStr, float blkStr)
        {
            try
            {
                var outcome = SeaMath.ResolveSeaBattle(playerStr, blkStr, _rng.NextDouble());
                int hurt = ApplySeaCasualties(MobileParty.MainParty, outcome.CasualtyFraction);

                if (outcome.Victory)
                {
                    if (outcome.LootGold > 0)
                        try { GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, outcome.LootGold, true); } catch { }
                    try { GainRenownAction.Apply(Hero.MainHero, 5f); } catch { }
                    InformationManager.ShowInquiry(new InquiryData(
                        "⚓  Blockade Broken",
                        "You force the line at bloody cost. Burning hulks drift aside and the harbor mouth opens." +
                        (hurt > 0 ? $" {hurt} of your soldiers paid for the approach." : ""),
                        true, false, "Press on.", "", null, null), true);
                    // Voyage continues — VoyageOnTick will call Arrive() normally.
                }
                else
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        "⚓  Blockade — Repulsed",
                        "The line holds. Your ships are beaten back with heavy loss." +
                        (hurt > 0 ? $" {hurt} soldiers are gone." : "") +
                        " You limp back to your port of origin.",
                        true, false, "Withdraw.", "", () => TurnBackFromBlockade(), null), true);
                }
            }
            catch { }
        }

        private static void TurnBackFromBlockade()
        {
            try
            {
                if (_fareEscrow > 0)
                {
                    GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, _fareEscrow, true);
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The harbormaster refunds your fare — the harbor was denied.",
                        new Color(0.65f, 0.75f, 0.9f)));
                    _fareEscrow = 0;
                }
                var origin = _voyageOrigin;
                ResetVoyageState();
                if (origin != null)
                {
                    var main = MobileParty.MainParty;
                    try { main.Position = origin.GatePosition; } catch { }
                    try { main.SetMoveGoToSettlement(origin, MobileParty.NavigationType.Default, false); } catch { }
                }
                try { GameMenu.SwitchToMenu("town"); } catch { }
            }
            catch { }
        }

        // ── Survivors ─────────────────────────────────────────────────────────
        private static void FireSurvivors()
        {
            try
            {
                var options = new List<InquiryElement>
                {
                    new InquiryElement("take",
                        "Heave to and bring them aboard", null, true,
                        "Slow down and take the survivors on. Costs 2 hours — but it is the decent thing."),
                    new InquiryElement("pass",
                        "Row past. The sea's arithmetic is harsh but honest.", null, true,
                        "Hold course. The crossing does not slow."),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("read",
                        $"Read the boat before you close ({SeaMath.SenseWreckAgingDays} day aging)", null, true,
                        "The Inner Fire can taste the boat from here — learn who they are before you decide whether to close."));
                if (NatureKnowledge.IsAttuned)
                {
                    bool canAffordFeel = Hero.MainHero.HitPoints > SeaMath.FeelLifeHpCost + 10;
                    options.Add(new InquiryElement("feel_life",
                        $"Feel for life before you close ({SeaMath.FeelLifeHpCost} HP)", null, canAffordFeel,
                        canAffordFeel
                            ? "Let the living world taste the boat — feel whether those aboard are what they seem."
                            : "You do not have enough left to give."));
                }

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "🚣  Survivors",
                    "A ship's boat, low in the water and rowing hard. Ragged figures, a broken mast lashed across the gunwales — " +
                    "what was a merchant cog, or a naval escort, or something worse. " +
                    "They have seen your sail and are pulling toward you. You are their only hope.",
                    options, false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        string pick = chosen?[0]?.Identifier as string ?? "pass";
                        switch (pick)
                        {
                            case "take":
                            {
                                _voyageHoursTotal += 2f;
                                var lord = FindRandomLord();
                                if (lord != null)
                                {
                                    string lName = lord.Name?.ToString() ?? "a lord";
                                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 1, false); } catch { }
                                    InformationManager.ShowInquiry(new InquiryData(
                                        "🚣  Survivors — Rescued",
                                        $"You pull them in — a half-dozen men, one of whom claims service to {lName}. " +
                                        $"He is quiet now, but he will remember this. " +
                                        $"Two hours lost. (Relation with {lName}: +1)",
                                        true, false, "Sail on.", "", null, null), true);
                                }
                                else
                                {
                                    InformationManager.ShowInquiry(new InquiryData(
                                        "🚣  Survivors — Rescued",
                                        "You pull them in. Ordinary men, far from home, with nothing left to offer but their gratitude. " +
                                        "Two hours lost. The crossing continues.",
                                        true, false, "Sail on.", "", null, null), true);
                                }
                                break;
                            }
                            case "feel_life":
                            {
                                try { Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - SeaMath.FeelLifeHpCost); } catch { }
                                if (_rng.NextDouble() < 0.60)
                                {
                                    var lord = FindRandomLord();
                                    _voyageHoursTotal += 2f;
                                    if (lord != null)
                                    {
                                        string lName = lord.Name?.ToString() ?? "a lord";
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 2, false); } catch { }
                                        InformationManager.ShowInquiry(new InquiryData(
                                            "🚣  Survivors — Felt",
                                            $"The living world tastes the boat: men sworn to {lName}, worth saving. " +
                                            $"You take them on. {lName} will hear of this. " +
                                            $"Two hours lost. (Relation with {lName}: +2)",
                                            true, false, "Sail on.", "", null, null), true);
                                    }
                                    else
                                    {
                                        InformationManager.ShowInquiry(new InquiryData(
                                            "🚣  Survivors — Felt",
                                            "The living world finds honest men in misfortune — no hidden threat, no sickness. You take them on. " +
                                            "Two hours lost.",
                                            true, false, "Sail on.", "", null, null), true);
                                    }
                                }
                                else
                                {
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "The living world finds fever and despair in the boat — not deception, but a sickness that would spread. " +
                                        "You leave a waterskin on a rope and sail on. The sea's arithmetic is what it is."));
                                }
                                break;
                            }
                            case "read":
                            {
                                try { AgingSystem.AgeHero(Hero.MainHero, SeaMath.SenseWreckAgingDays); } catch { }
                                if (_rng.NextDouble() < 0.60)
                                {
                                    var lord = FindRandomLord();
                                    _voyageHoursTotal += 2f;
                                    if (lord != null)
                                    {
                                        string lName = lord.Name?.ToString() ?? "a lord";
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 2, false); } catch { }
                                        InformationManager.ShowInquiry(new InquiryData(
                                            "🚣  Survivors — Sensed",
                                            $"The Inner Fire tastes the boat: men sworn to {lName}, worth saving. " +
                                            $"You take them on. {lName} will hear of this. " +
                                            $"Two hours lost. (Relation with {lName}: +2)",
                                            true, false, "Sail on.", "", null, null), true);
                                    }
                                    else
                                    {
                                        InformationManager.ShowInquiry(new InquiryData(
                                            "🚣  Survivors — Sensed",
                                            "The Inner Fire finds honest men in misfortune — no threat, no trick. You take them on. " +
                                            "Two hours lost.",
                                            true, false, "Sail on.", "", null, null), true);
                                    }
                                }
                                else
                                {
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "The Inner Fire finds fever and delirium in the boat — disease, not misfortune. " +
                                        "You pass them a waterskin on a rope and sail on. Grim arithmetic."));
                                }
                                break;
                            }
                            default: // pass
                            {
                                AddSeaMorale(-1f);
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    "You sail past. The figures in the boat stop rowing as you go by. The crossing does not slow."));
                                break;
                            }
                        }
                    },
                    null, "", false), true, true);
            }
            catch { }
        }

        // ── Sea Serpent ───────────────────────────────────────────────────────
        private static void FireSeaSerpent()
        {
            try
            {
                var options = new List<InquiryElement>
                {
                    new InquiryElement("flee",
                        "Crowd sail and run — no good comes from this", null, true,
                        "Push the ship hard and put distance between it and you. Costs hours and shakes the crew."),
                    new InquiryElement("hold",
                        "Hold course. The captain swears it is just a whale.", null, true,
                        "Even odds it passes without incident. If it does not, the hull will know it."),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("speak",
                        $"Speak to it through the Inner Fire ({SeaMath.SerpentAgingDays} days aging)", null, true,
                        "Ancient things in the deep listen to the Fire. It costs years — but they do not always mean harm."));
                if (NatureKnowledge.IsAttuned)
                {
                    bool canAffordCommune = Hero.MainHero.HitPoints > SeaMath.SerpentCommuneHpCost + 10;
                    options.Add(new InquiryElement("commune",
                        $"Commune with the deep ({SeaMath.SerpentCommuneHpCost} HP)", null, canAffordCommune,
                        canAffordCommune
                            ? "The living world runs deep as well as wide. Let it carry your presence down. " +
                              "Old things in dark water are not always what they seem."
                            : "You do not have enough left to give."));
                }

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "🐉  Something in the Deep",
                    "The water changes color beneath the hull — dark green to grey to black, and something is in the black. " +
                    "A shape longer than the ship, slow and purposeful, circling. One of the sailors has stopped working. " +
                    "The captain says nothing. He is gripping the rail very hard.",
                    options, false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        string pick = chosen?[0]?.Identifier as string ?? "hold";
                        switch (pick)
                        {
                            case "flee":
                            {
                                int extra = 3 + _rng.Next(3);
                                _voyageHoursTotal += extra;
                                AddSeaMorale(-2f);
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    $"You crack on every scrap of sail and run. The shape does not follow — " +
                                    $"or if it does, it is content to let you go. {extra} hours lost. " +
                                    "The crew does not speak for the rest of the crossing."));
                                break;
                            }
                            case "speak":
                            {
                                try { AgingSystem.AgeHero(Hero.MainHero, SeaMath.SerpentAgingDays); } catch { }
                                AddSeaMorale(2f);
                                InformationManager.ShowInquiry(new InquiryData(
                                    "🐉  The Deep Listens",
                                    "You let the Fire out — just a thread, just enough. The shape slows. Stills. " +
                                    "For a long moment there is nothing but the water and something enormous beneath it, paying attention. " +
                                    "Then it turns, smooth and deliberate, and is gone into the dark. " +
                                    "The crew does not ask what you did. They do not want to know.",
                                    true, false, "Sail on.", "", null, null), true);
                                break;
                            }
                            case "commune":
                            {
                                try { Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - SeaMath.SerpentCommuneHpCost); } catch { }
                                AddSeaMorale(2f);
                                InformationManager.ShowInquiry(new InquiryData(
                                    "🐉  The Deep Receives",
                                    "You open yourself to the living world beneath the water — the cold, the pressure, the slow dark patience of it. " +
                                    "The shape beneath the hull goes still. For a moment it is aware of you, and you of it. " +
                                    "Then something vast and old simply... moves on. The crew breathes again. " +
                                    "The crossing continues.",
                                    true, false, "Sail on.", "", null, null), true);
                                break;
                            }
                            default: // hold
                            {
                                if (_rng.NextDouble() < 0.50)
                                {
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "It circles twice and goes down. The captain exhales. The crew pretends they were not afraid. " +
                                        "The crossing continues."));
                                }
                                else
                                {
                                    int extra = 2 + _rng.Next(3);
                                    _voyageHoursTotal += extra;
                                    int hurt = ApplySeaCasualties(MobileParty.MainParty, 0.03f);
                                    AddSeaMorale(-3f);
                                    InformationManager.ShowInquiry(new InquiryData(
                                        "🐉  It Surfaces",
                                        "The shape comes up. Not the whole of it — just enough, for just a moment. " +
                                        "The hull shudders, ropes part, a man goes into the water and does not come back. " +
                                        (hurt > 0 ? $"{hurt} of your soldiers are shaken and hurt. " : "") +
                                        $"It takes {extra} hours to sort the damage and convince the crew to row.",
                                        true, false, "Press on.", "", null, null), true);
                                }
                                break;
                            }
                        }
                    },
                    null, "", false), true, true);
            }
            catch { }
        }

        private static Hero FindRandomLord()
        {
            try
            {
                return Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero && !h.IsPrisoner)
                    .OrderBy(_ => _rng.Next())
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        private static void AddSeaMorale(float delta)
        {
            try { if (MobileParty.MainParty != null) MobileParty.MainParty.RecentEventsMorale += delta; } catch { }
        }
    }
}
