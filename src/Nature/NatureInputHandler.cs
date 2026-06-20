// =============================================================================
// ASH AND EMBER — Nature/NatureInputHandler.cs
// The Living Ember input: Right Alt held as modifier.
//
// KEYS (while holding Right Alt):
//   S = Draw a charge from the land beneath you.
//   W = Release (cast) the oldest held charge.
//
// The land gives what it gives — element is terrain-determined, power is
// random from the element's two options. Charges persist between presses.
//
// Campaign map: same keys. Draws passive charge first if empty; W = cast.
//
// Gamepad: hold R3 (right stick click):
//   L-stick Down = Draw.
//   L-stick Up   = Cast.
//
// Restrictions:
//   - No wielded weapons, shields.
//   - Total armour weight ≤ NatureMath.ArmourWeightCap.
//   - Verdant draws in combat: no HP cost. All others: see NatureMath.DrawHpCost.
//   - Still Draw talent: no HP cost when stationary in combat.
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class NatureInputHandler
    {
        private static bool  _wasHolding   = false;
        private static bool  _pendingDraw  = false;
        private static bool  _pendingCast  = false;
        private static bool  _prevS        = false;
        private static bool  _prevW        = false;
        private static bool  _prevPadDown  = false;
        private static bool  _prevPadUp    = false;

        public static void ResetInputState()
        {
            _wasHolding  = false;
            _pendingDraw = false;
            _pendingCast = false;
            _prevS       = false;
            _prevW       = false;
            _prevPadDown = false;
            _prevPadUp   = false;
        }

        public static void Tick(bool inMission)
        {
            if (!NatureKnowledge.IsAttuned) return;

            // Guard: Right Alt must not conflict with existing modifiers.
            bool leftAltHeld  = Input.IsKeyDown(InputKey.LeftAlt);
            bool leftCtrlHeld = Input.IsKeyDown(InputKey.LeftControl)
                             || Input.IsKeyDown(InputKey.RightControl);
            bool lbHeld       = Input.IsKeyDown(InputKey.ControllerLBumper);
            bool rbHeld       = Input.IsKeyDown(InputKey.ControllerRBumper);

            bool holdKb  = Input.IsKeyDown(InputKey.RightAlt)
                        && !leftAltHeld && !leftCtrlHeld;
            bool holdPad = Input.IsKeyDown(InputKey.ControllerRThumb)
                        && !lbHeld && !rbHeld;
            bool holding = holdKb || holdPad;

            if (holding)
            {
                if (!_wasHolding)
                {
                    // Show current terrain so the player knows what they might draw.
                    try { ShowTerrainHint(inMission); } catch { }
                    try
                    {
                        if (inMission && Agent.Main != null)
                            SpellEffects.BeginFocusVisual(Agent.Main, ColorSchool.Nature);
                    }
                    catch { }
                    _wasHolding = true;
                }

                // ── Keyboard ──────────────────────────────────────────────────
                if (holdKb)
                {
                    bool sNow = Input.IsKeyDown(InputKey.S);
                    bool wNow = Input.IsKeyDown(InputKey.W);
                    if (sNow && !_prevS) _pendingDraw = true;
                    if (wNow && !_prevW) _pendingCast = true;
                    _prevS = sNow;
                    _prevW = wNow;
                }

                // ── Gamepad ───────────────────────────────────────────────────
                if (holdPad)
                {
                    bool dNow = Input.IsKeyDown(InputKey.ControllerLStickDown);
                    bool uNow = Input.IsKeyDown(InputKey.ControllerLStickUp);
                    if (dNow && !_prevPadDown) _pendingDraw = true;
                    if (uNow && !_prevPadUp)   _pendingCast = true;
                    _prevPadDown = dNow;
                    _prevPadUp   = uNow;
                }

                // Execute immediately on edge: draw first, then cast if both pressed.
                if (_pendingDraw) { TryDraw(inMission); _pendingDraw = false; }
                if (_pendingCast) { TryCast(inMission); _pendingCast = false; }
            }
            else if (_wasHolding)
            {
                _wasHolding  = false;
                _pendingDraw = false;
                _pendingCast = false;
                _prevS       = false;
                _prevW       = false;
                _prevPadDown = false;
                _prevPadUp   = false;

                try
                {
                    if (inMission && Agent.Main != null)
                        SpellEffects.EndFocusVisual(Agent.Main);
                }
                catch { }
            }
        }

        // ── Draw ──────────────────────────────────────────────────────────────
        private static void TryDraw(bool inMission)
        {
            Agent caster = inMission ? Agent.Main : null;

            // Weapon check (battle only)
            if (inMission && caster != null)
            {
                if (!SpellEffects.HasFreeHand(caster))
                {
                    Msg("Both hands are occupied. Sheathe your weapons before drawing from the land.",
                        NatureColor);
                    return;
                }
                if (NatureEffects.ArmourTooHeavy(caster))
                {
                    Msg("Your armour smothers the channel. The land cannot reach through iron.",
                        NatureColor);
                    return;
                }
            }

            string failReason;
            NaturePower drawn;
            if (!NatureCharge.TryDraw(inMission, out drawn, out failReason))
            {
                if (failReason != null)
                    Msg(failReason, NatureColor);
                return;
            }

            // HP cost for combat draws (except Verdant)
            if (inMission && caster != null)
            {
                NatureElement el = NatureMath.ElementOf(drawn);
                float cost = NatureMath.DrawHpCost(el);

                // Still Draw talent: no cost while stationary
                bool stillDraw = TalentSystem.Has(TalentId.NatureStillDraw);
                if (stillDraw)
                {
                    try
                    {
                        bool isStationary = caster.GetCurrentVelocity().Length < 0.3f;
                        if (isStationary) cost = 0f;
                    }
                    catch { }
                }

                if (cost > 0f)
                {
                    if (caster.Health <= cost + 5f)
                    {
                        Msg("You do not have enough left to give. The land does not take the dying.",
                            NatureColor);
                        // Refund the charge
                        NatureCharge.Release();
                        return;
                    }
                    caster.Health -= cost;
                }
            }

            string elementName = NatureMath.ElementName(NatureMath.ElementOf(drawn));
            Msg($"You draw from the {elementName.ToLower()} — {NatureMath.PowerName(drawn)} stirs in your hands.",
                NatureColor);

            if (NatureCharge.ChargeCount > 1)
                Msg($"({NatureCharge.ChargeCount}/{(TalentSystem.Has(TalentId.NatureLivingRoot) ? 2 : 1)} charges held)",
                    new Color(0.35f, 0.65f, 0.35f));
        }

        // ── Cast ──────────────────────────────────────────────────────────────
        private static void TryCast(bool inMission)
        {
            if (!NatureCharge.HasCharge)
            {
                Msg("You hold nothing. Draw from the land first.", NatureColor);
                return;
            }

            NaturePower power = NatureCharge.Release();
            if (power == NaturePower.None) return;

            Agent caster = inMission ? Agent.Main : null;
            NatureEffects.Execute(power, caster, inMission);
        }

        // ── Terrain hint ──────────────────────────────────────────────────────
        private static void ShowTerrainHint(bool inMission)
        {
            NatureElement[] elements = NatureCharge.PeekTerrainElements(inMission);
            if (elements == null || elements.Length == 0) return;

            string names = string.Join(" / ", System.Array.ConvertAll(
                elements, e => NatureMath.ElementName(e)));

            string chargeStr = NatureCharge.HasCharge
                ? $" — holding: {NatureMath.PowerName(NatureCharge.CurrentPower)}"
                : "";

            Msg($"[ {names}{chargeStr} ]  (S=draw  W=cast)", NatureColor);
        }

        private static readonly Color NatureColor = new Color(0.35f, 0.75f, 0.35f);

        private static void Msg(string text, Color color)
            => InformationManager.DisplayMessage(new InformationMessage(text, color));
    }
}
