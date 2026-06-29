// =============================================================================
// ASH AND EMBER — Nature/NatureInputHandler.cs
// The Living Ember input. Shares the miracle focus key (Left Ctrl) — Grace, Cold
// and Nature are mutually exclusive, so the key is unambiguous, and it avoids the
// Right Alt = AltGr (Ctrl+Alt) problem on non-US keyboards.
//
// CHANNEL: hold Ctrl and STAND STILL (hands empty, armour light). A charge of the
//          local element fills over a few seconds, then lasts ~10 s.
// CAST   : while holding Ctrl and carrying a charge —
//          Attack (left mouse) = the element's ATTACK power.
//          Block  (right mouse) = the element's SUPPORT power.
//
// Element comes from the battle terrain (Wind: mountains/steppes, Earth: forest,
// Water: rivers/shore/snow, Storm: desert/plain; mixed ground is random).
//
// Gamepad: hold R3, stand still to channel; Right Trigger = attack, Left Trigger
//          = support.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class NatureInputHandler
    {
        private static bool _wasHolding = false;
        private static bool _prevAtk, _prevBlk, _prevPadAtk, _prevPadBlk;
        private static bool _prevPadUp, _prevPadDown, _prevPadLeft, _prevPadRight;
        private const float StillSpeed = 0.3f;   // below this the caster counts as still
        private static float _blockReminder = 0f;             // throttle for "why can't I channel" hints
        private const float BlockReminderInterval = 2.5f;

        public static void ResetInputState()
        {
            _wasHolding = false;
            _prevAtk = _prevBlk = _prevPadAtk = _prevPadBlk = false;
            _prevPadUp = _prevPadDown = _prevPadLeft = _prevPadRight = false;
        }

        public static void Tick(bool inMission, float dt = 0f)
        {
            if (!NatureKnowledge.IsAttuned) return;
            if (DarkGiftSystem.HasAnyGift) return;   // the darkness silences the living world
            // On the campaign map, casting is done through the litany window
            // (ShowNatureMenu, opened with Shift+X) and charges come from standing
            // still for hours, so the direct hold-Ctrl gesture runs in battle only.
            // If a player reaches for the battle gesture on the map (Ctrl + a
            // direction), nudge them to the right key instead of failing silently.
            if (!inMission)
            {
                bool ctrl = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
                if (ctrl && !Input.IsKeyDown(InputKey.LeftAlt)
                    && (Input.IsKeyPressed(InputKey.W) || Input.IsKeyPressed(InputKey.A)
                     || Input.IsKeyPressed(InputKey.S) || Input.IsKeyPressed(InputKey.D)))
                    Msg("On the march, the land answers the litany — press Shift+X to draw the Living Ember.", NatureColor);
                return;
            }

            bool leftAltHeld = Input.IsKeyDown(InputKey.LeftAlt);
            bool ctrlHeld    = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            bool lbHeld      = Input.IsKeyDown(InputKey.ControllerLBumper);
            bool rbHeld      = Input.IsKeyDown(InputKey.ControllerRBumper);

            bool holdKb  = ctrlHeld && !leftAltHeld;
            bool holdPad = Input.IsKeyDown(InputKey.ControllerRThumb) && !lbHeld && !rbHeld;
            bool holding = holdKb || holdPad;

            if (holding)
            {
                if (!_wasHolding)
                {
                    try { ShowHint(inMission); } catch { }
                    try { if (inMission && Agent.Main != null) SpellEffects.BeginFocusVisual(Agent.Main, ColorSchool.Nature); } catch { }
                    _wasHolding = true;
                }

                // Choose the element to draw — trace a direction (W=Wind, S=Earth,
                // A=Water, D=Storm), or flick the left stick on a pad.
                ReadElementSelection(holdKb, holdPad);

                // Channel: stand still, hands empty, armour light, an element chosen
                // → fill a charge of that element.
                if (!NatureCharge.IsFull && NatureCharge.HasSelection && CanChannel(inMission))
                {
                    _blockReminder = 0f;
                    if (NatureCharge.ChannelTick(dt, inMission))
                    {
                        Msg($"A charge of {NatureMath.ElementName(NatureCharge.CurrentElement)} gathers — " +
                            $"Attack looses its force, Block calls its grace.", NatureColor);
                        ApplyGatherSour();
                    }
                }
                else
                {
                    NatureCharge.ResetFill();
                    // Tell the player WHY the land will not answer, rather than
                    // failing silently. Throttled so it informs without spamming.
                    if (!NatureCharge.IsFull)
                    {
                        string reason = !NatureCharge.HasSelection
                            ? "Choose an element to draw —  W: Wind · S: Earth · A: Water · D: Storm."
                            : ChannelBlockReason(inMission);
                        if (reason != null)
                        {
                            _blockReminder -= dt;
                            if (_blockReminder <= 0f)
                            {
                                Msg(reason, NatureColor);
                                _blockReminder = BlockReminderInterval;
                            }
                        }
                    }
                }

                // Cast: Attack (left mouse) / Block (right mouse).
                if (holdKb)
                {
                    bool atk = Input.IsKeyDown(InputKey.LeftMouseButton);
                    bool blk = Input.IsKeyDown(InputKey.RightMouseButton);
                    if (atk && !_prevAtk) TryCast(inMission, attack: true);
                    if (blk && !_prevBlk) TryCast(inMission, attack: false);
                    _prevAtk = atk; _prevBlk = blk;
                }
                if (holdPad)
                {
                    bool atk = Input.IsKeyDown(InputKey.ControllerRTrigger);
                    bool blk = Input.IsKeyDown(InputKey.ControllerLTrigger);
                    if (atk && !_prevPadAtk) TryCast(inMission, attack: true);
                    if (blk && !_prevPadBlk) TryCast(inMission, attack: false);
                    _prevPadAtk = atk; _prevPadBlk = blk;
                }
            }
            else if (_wasHolding)
            {
                _wasHolding = false;
                _prevAtk = _prevBlk = _prevPadAtk = _prevPadBlk = false;
                NatureCharge.ResetFill();
                try { if (inMission && Agent.Main != null) SpellEffects.EndFocusVisual(Agent.Main); } catch { }
            }
        }

        // Read the direction that draws an element. Keyboard taps (edge-detected)
        // and left-stick flicks both choose; the choice persists until changed.
        private static void ReadElementSelection(bool holdKb, bool holdPad)
        {
            if (holdKb)
            {
                if      (Input.IsKeyPressed(InputKey.W)) NatureCharge.SelectElement(NatureElement.Wind);
                else if (Input.IsKeyPressed(InputKey.S)) NatureCharge.SelectElement(NatureElement.Earth);
                else if (Input.IsKeyPressed(InputKey.A)) NatureCharge.SelectElement(NatureElement.Water);
                else if (Input.IsKeyPressed(InputKey.D)) NatureCharge.SelectElement(NatureElement.Storm);
            }
            if (holdPad)
            {
                bool up    = Input.IsKeyDown(InputKey.ControllerLStickUp);
                bool down  = Input.IsKeyDown(InputKey.ControllerLStickDown);
                bool left  = Input.IsKeyDown(InputKey.ControllerLStickLeft);
                bool right = Input.IsKeyDown(InputKey.ControllerLStickRight);
                if (up    && !_prevPadUp)    NatureCharge.SelectElement(NatureElement.Wind);
                if (down  && !_prevPadDown)  NatureCharge.SelectElement(NatureElement.Earth);
                if (left  && !_prevPadLeft)  NatureCharge.SelectElement(NatureElement.Water);
                if (right && !_prevPadRight) NatureCharge.SelectElement(NatureElement.Storm);
                _prevPadUp = up; _prevPadDown = down; _prevPadLeft = left; _prevPadRight = right;
            }
        }

        // If the last gather drew from an exhausted land, it may have soured — the
        // land bites back in one of many forms.
        private static void ApplyGatherSour()
        {
            try
            {
                var outcome = NatureCharge.LastGatherOutcome;
                if (!outcome.Soured) return;
                NatureBacklash.ApplyBattle(Agent.Main, announce: true);
            }
            catch { }
        }

        // Channelling requires standing still; in battle also empty hands + light armour.
        private static bool CanChannel(bool inMission)
            => ChannelBlockReason(inMission) == null;

        // Returns null when channelling is allowed, otherwise a short reason the
        // player can act on (sheathe weapon / shed armour / stand still).
        private static string ChannelBlockReason(bool inMission)
        {
            if (!inMission) return null;   // map handled separately (Part 4)
            try
            {
                Agent c = Agent.Main;
                if (c == null) return "The land cannot reach you here.";
                if (c.GetCurrentVelocity().Length >= StillSpeed)
                    return "The land answers only the still — stop moving to gather a charge.";
                if (!SpellEffects.HasFreeHand(c))
                    return "Your hands are full of steel. Sheathe your weapon (X) to draw from the land.";
                if (NatureEffects.ArmourTooHeavy(c))
                    return "Too much iron weighs you down — the living current cannot pass through heavy armour.";
                return null;
            }
            catch { return null; }
        }

        // The Living Ember window — reached through the miracle key (Shift+X / RB+L3).
        // The map's primary casting path; in battle it is a convenience alongside the
        // direct Attack/Block cast.
        public static void ShowNatureMenu()
        {
            if (DarkGiftSystem.HasAnyGift)
            {
                Msg("The darkness in you has silenced the living world. Renounce your gifts at a Dark Altar to walk with the land again.", NatureColor);
                return;
            }

            bool inBattle = false;
            try { inBattle = Mission.Current != null; } catch { }

            if (!NatureCharge.HasCharge)
            {
                if (inBattle)
                {
                    Msg("You carry no charge. Hold Ctrl, trace an element (W/S/A/D), then stand still to gather it.",
                        NatureColor);
                    return;
                }
                // On the map, the litany is where you choose which element to draw.
                ShowElementChoice();
                return;
            }

            NatureElement el = NatureCharge.CurrentElement;

            // On the campaign map only the support power has a map form; attacks need enemies.
            if (!inBattle)
            {
                NaturePower campPower = NatureMath.SupportPower(el);
                string campLabel = NatureMath.CampaignPowerLabel(campPower);
                var campOptions = new List<InquiryElement>
                {
                    new InquiryElement(campPower, campLabel, null, true, NatureMath.CampaignPowerHint(campPower)),
                };
                string campTitle = $"The Living Ember — {NatureMath.ElementName(el)}";
                string campBody  = "The land offers what it can on the march. Attacks only answer in the heat of battle.";
                try
                {
                    MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                        campTitle, campBody, campOptions, true, 1, 1, "Invoke", "Close",
                        chosen =>
                        {
                            if (chosen == null || chosen.Count == 0) return;
                            var power = (NaturePower)chosen[0].Identifier;
                            if (NatureCharge.Release() == NatureElement.None) return;
                            NatureEffects.Execute(power, null, false);
                        },
                        null, "", false), false, true);
                }
                catch
                {
                    if (NatureCharge.Release() == NatureElement.None) return;
                    NatureEffects.Execute(campPower, null, false);
                }
                return;
            }

            NaturePower atk = NatureMath.AttackPower(el);
            NaturePower sup = NatureMath.SupportPower(el);

            var options = new List<InquiryElement>
            {
                new InquiryElement(atk, $"{NatureMath.PowerName(atk)} — attack", null, true, ""),
                new InquiryElement(sup, $"{NatureMath.PowerName(sup)} — barrier", null, true, ""),
            };

            string title = $"The Living Ember — {NatureMath.ElementName(el)}";
            string body  = "Spend your charge. (Hold Ctrl, then Attack or Block to cast directly.)";

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    title, body, options, true, 1, 1, "Invoke", "Close",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var power = (NaturePower)chosen[0].Identifier;
                        if (NatureCharge.Release() == NatureElement.None) return;
                        NatureEffects.Execute(power, Agent.Main, inBattle);
                    },
                    null, "", false), false, true);
            }
            catch
            {
                // Fallback: spend the charge on the attack power.
                if (NatureCharge.Release() == NatureElement.None) return;
                NatureEffects.Execute(atk, Agent.Main, inBattle);
            }
        }

        // Map-side element choice: pick what you will draw, then halt to gather it.
        private static void ShowElementChoice()
        {
            NatureElement[] favoured = NatureCharge.PeekTerrainElements(false);
            var favSet = favoured != null ? new HashSet<NatureElement>(favoured) : new HashSet<NatureElement>();
            bool unfamiliar = favoured == null || favoured.Length >= 4;

            var options = new List<InquiryElement>();
            foreach (NatureElement el in new[] { NatureElement.Wind, NatureElement.Earth, NatureElement.Water, NatureElement.Storm })
            {
                string tag = unfamiliar ? "" : (favSet.Contains(el) ? "  (the land here favours it — a gentle draw)" : "  (against this land — a costly draw)");
                options.Add(new InquiryElement(el, $"{NatureMath.ElementName(el)} [{NatureMath.KeyForElement(el)}]{tag}", null, true, ""));
            }

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "The Living Ember — what will you draw?",
                    "Choose the element you mean to call from the land. Then halt in open country " +
                    "and let it fill your hands over a few hours. What the land favours costs it little; " +
                    "what it does not costs it dearly.",
                    options, true, 1, 1, "Attune", "Close",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var el = (NatureElement)chosen[0].Identifier;
                        NatureCharge.SelectElement(el);
                        Msg($"You set your hands to draw {NatureMath.ElementName(el)}. Halt and stand still to gather it.", NatureColor);
                    },
                    null, "", false), false, true);
            }
            catch
            {
                Msg("Choose an element to draw, then halt to gather it.", NatureColor);
            }
        }

        private static void TryCast(bool inMission, bool attack)
        {
            if (!NatureCharge.HasCharge)
            {
                Msg("You hold nothing. Stand still while focusing to gather a charge from the land.", NatureColor);
                return;
            }
            NatureElement el = NatureCharge.Release();
            if (el == NatureElement.None) return;
            NaturePower power = attack ? NatureMath.AttackPower(el) : NatureMath.SupportPower(el);
            Agent caster = inMission ? Agent.Main : null;
            NatureEffects.Execute(power, caster, inMission);
        }

        private static void ShowHint(bool inMission)
        {
            // Which elements this ground favours (a cheaper draw on the land).
            NatureElement[] favoured = NatureCharge.PeekTerrainElements(inMission);
            string favourLine = "";
            if (favoured != null && favoured.Length > 0 && favoured.Length < 4)
                favourLine = " · favoured here: " + string.Join("/", favoured.Select(NatureMath.ElementName));

            string chosen = NatureCharge.HasSelection
                ? $"drawing {NatureMath.ElementName(NatureCharge.SelectedElement)}"
                : "choose: W Wind · S Earth · A Water · D Storm";
            string held = NatureCharge.HasCharge
                ? $" — holding {NatureMath.ElementName(NatureCharge.CurrentElement)}"
                : "";
            Msg($"[ {chosen}{held} ]  (stand still: gather · Attack: force · Block: barrier{favourLine})", NatureColor);
        }

        private static readonly Color NatureColor = new Color(0.35f, 0.75f, 0.35f);

        private static void Msg(string text, Color color)
            => InformationManager.DisplayMessage(new InformationMessage(text, color));
    }
}
