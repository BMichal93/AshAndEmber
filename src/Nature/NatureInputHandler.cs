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
        private const float StillSpeed = 0.3f;   // below this the caster counts as still

        public static void ResetInputState()
        {
            _wasHolding = false;
            _prevAtk = _prevBlk = _prevPadAtk = _prevPadBlk = false;
        }

        public static void Tick(bool inMission, float dt = 0f)
        {
            if (!NatureKnowledge.IsAttuned) return;
            if (DarkGiftSystem.HasAnyGift) return;   // the darkness silences the living world
            // On the campaign map, casting is done through the miracle window
            // (ShowNatureMenu) and charges come from standing still for hours, so the
            // direct hold-and-click input runs in battle only.
            if (!inMission) return;

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

                // Channel: stand still, hands empty, armour light → fill a charge.
                if (!NatureCharge.IsFull && CanChannel(inMission))
                {
                    if (NatureCharge.ChannelTick(dt, inMission))
                        Msg($"A charge of {NatureMath.ElementName(NatureCharge.CurrentElement)} gathers — " +
                            $"Attack looses its force, Block calls its grace.", NatureColor);
                }
                else
                {
                    NatureCharge.ResetFill();
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

        // Channelling requires standing still; in battle also empty hands + light armour.
        private static bool CanChannel(bool inMission)
        {
            if (!inMission) return true;   // map handled separately (Part 4)
            try
            {
                Agent c = Agent.Main;
                if (c == null) return false;
                if (c.GetCurrentVelocity().Length >= StillSpeed) return false;
                if (!SpellEffects.HasFreeHand(c)) return false;
                if (NatureEffects.ArmourTooHeavy(c)) return false;
                return true;
            }
            catch { return false; }
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
                Msg(inBattle
                    ? "You carry no charge. Stand still and focus (hold Ctrl) to gather one from the land."
                    : "You carry no charge. Halt in open country and let the land fill your hands over a few hours.",
                    NatureColor);
                return;
            }

            NatureElement el  = NatureCharge.CurrentElement;
            NaturePower    sup = NatureMath.SupportPower(el);
            NaturePower    atk = NatureMath.AttackPower(el);

            var options = new List<InquiryElement>
            {
                new InquiryElement(sup, $"{NatureMath.PowerName(sup)} — support", null, true, ""),
            };
            if (inBattle)
                options.Add(new InquiryElement(atk, $"{NatureMath.PowerName(atk)} — attack", null, true, ""));

            string title = $"The Living Ember — {NatureMath.ElementName(el)}";
            string body  = inBattle
                ? "Spend your charge. (In battle you may also cast directly: hold Ctrl, then Attack or Block.)"
                : "Spend your charge on the land's gift.";

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    title, body, options, true, 1, 1, "Invoke", "Close",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var power = (NaturePower)chosen[0].Identifier;
                        if (NatureCharge.Release() == NatureElement.None) return;
                        NatureEffects.Execute(power, inBattle ? Agent.Main : null, inBattle);
                    },
                    null, "", false), false, true);
            }
            catch
            {
                // Fallback: spend the charge on the support power.
                if (NatureCharge.Release() == NatureElement.None) return;
                NatureEffects.Execute(sup, inBattle ? Agent.Main : null, inBattle);
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
            NatureElement[] els = NatureCharge.PeekTerrainElements(inMission);
            if (els == null || els.Length == 0) return;
            string names = els.Length > 1
                ? "mixed ground (random)"
                : NatureMath.ElementName(els[0]);
            string held = NatureCharge.HasCharge
                ? $" — holding {NatureMath.ElementName(NatureCharge.CurrentElement)}"
                : "";
            Msg($"[ {names}{held} ]  (stand still: gather · Attack / Block: cast)", NatureColor);
        }

        private static readonly Color NatureColor = new Color(0.35f, 0.75f, 0.35f);

        private static void Msg(string text, Color color)
            => InformationManager.DisplayMessage(new InformationMessage(text, color));
    }
}
