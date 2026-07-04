// =============================================================================
// ASH AND EMBER — Magic/ElementMagicInput.cs
//
// The unified elemental magic input, merging the old fire and nature handlers.
//
//   Hold FOCUS (Left Alt; gamepad Left Bumper). The loaded element is FIRE by
//   default; tap W / S / A / D (or flick the left stick up / down / left / right)
//   to load a learned element — Wind / Earth / Water / Spirit. Stand still and
//   DRAW for at least ~3 s, then ATTACK (left mouse / right trigger) looses the
//   element's cone, or BLOCK (right mouse / left trigger) raises its wall.
//
//   The longer you draw (up to ~7 s) the STRONGER the working — a charged cone
//   reaches further and a charged wall thickens into a filled rectangle; the
//   aging toll is flat either way. Hold past ~15 s and the charge disperses.
//   Casting needs a free hand and light armour — unless
//   you know STEEL, which lets you cast with a weapon drawn and bears the weight.
//   Aging "burns through" your years exactly like the old fire magic (the Ashen
//   pay in criminal standing instead).
//
//   Battle only. On the campaign map, each element grants a prayer-style spell
//   (cast through the litany / memory-rite, like the old fire map spells).
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class ElementMagicInput
    {
        private static bool  _wasFocusing;
        private static float _drawTime;            // seconds drawn since focus / last cast
        private static bool  _prevAtk, _prevBlk;
        // The chord buffer (the Unbinding): a lone Attack/Block press waits
        // ChordWindowSeconds for its partner before it commits as a normal cast,
        // so Attack+Block pressed together can release the element's ULTIMATE.
        private static CastForm? _pendingForm;
        private static float     _pendingTimer;
        private static bool  _prevPadUp, _prevPadDown, _prevPadLeft, _prevPadRight;
        private static float _reminder;            // throttle for the "why can't I draw" hint
        private const  float ReminderInterval = 2.0f;
        private const  float StillSpeed       = 0.3f;
        private static bool  _readyAnnounced;
        private static bool  _fullAnnounced;       // "fully charged" said once per draw

        // Charging visual: element-specific particles engulf the caster while they
        // draw, refreshed on a short interval and re-emitted the instant the loaded
        // element changes (so switching W/S/A/D swaps flames for vines, splashes…).
        private static float _visualTimer;
        private static MagicElement? _lastVisualElement;
        private const  float VisualInterval  = 0.4f;
        private const  float VisualDuration  = 0.6f;

        public static bool InputSuppressed { get; private set; }

        // How many times an Ashen player has loosed the cold in the current (or
        // just-ended) battle. Reset once per new mission — NOT at mission end — so
        // the post-battle "Fading" encounter can still read it after the fight
        // closes, whatever order the engine ends the mission and the map event in.
        private static int _ashenBattleCasts;
        // Weak so the ended mission's object graph can be collected while we wait on
        // the map; if it is collected the next mission simply resets the tally.
        private static readonly WeakReference _countedMission = new WeakReference(null);
        public static int AshenBattleCastCount => _ashenBattleCasts;

        public static void ResetInputState()
        {
            _wasFocusing = false;
            _drawTime = 0f;
            _prevAtk = _prevBlk = false;
            _pendingForm = null;
            _pendingTimer = 0f;
            _prevPadUp = _prevPadDown = _prevPadLeft = _prevPadRight = false;
            _reminder = 0f;
            _readyAnnounced = false;
            _fullAnnounced = false;
            _visualTimer = 0f;
            _lastVisualElement = null;
            InputSuppressed = false;
        }

        public static void Tick(bool inMission, float dt = 0f)
        {
            // Combat casting only — the map grants each element its own litany spell.
            if (!inMission) { InputSuppressed = false; return; }
            // A new battle resets the Ashen cast tally (the previous battle's count is
            // left standing until now so the post-battle encounter could read it).
            try
            {
                object m = Mission.Current;
                if (m != null && !ReferenceEquals(m, _countedMission.Target))
                { _ashenBattleCasts = 0; _countedMission.Target = m; }
            }
            catch { }
            if (!MageKnowledge.IsMage) { InputSuppressed = false; return; }

            bool altHeld = Input.IsKeyDown(InputKey.LeftAlt);
            bool lbHeld  = Input.IsKeyDown(InputKey.ControllerLBumper);
            bool focusing = altHeld || lbHeld;
            InputSuppressed = focusing;

            if (focusing)
            {
                if (!_wasFocusing)
                {
                    MageElementKnowledge.ResetLoaded();
                    _drawTime = 0f;
                    _readyAnnounced = false;
                    _fullAnnounced = false;
                    _visualTimer = 0f;
                    _lastVisualElement = null;   // force an immediate first pulse
                    try { if (Agent.Main != null) SpellEffects.BeginCastLoop(Agent.Main); } catch { }
                    _wasFocusing = true;
                }

                ReadElementSelect(altHeld, lbHeld);

                // Draw the charge while standing still with hands and armour free
                // (Steel waives the hand and weight limits). The draw builds POWER,
                // not cost: release at once for a weak working, or hold to strengthen
                // it — full at ~7 s, but hold ~15 s and the charge disperses.
                string reason = ChannelBlockReason();
                if (reason == null)
                {
                    if (!_readyAnnounced)
                    {
                        _readyAnnounced = true;
                        Msg($"{MageElementKnowledge.LoadedName()} gathers — Attack: strike, Block: wall. Hold to strengthen.");
                    }
                    _drawTime += dt;
                    if (!_fullAnnounced && ElementMagicMath.IsFullyCharged(_drawTime))
                    {
                        _fullAnnounced = true;
                        Msg($"{MageElementKnowledge.LoadedName()} fully charged — release now, or press Attack and Block together to UNBIND it.");
                    }
                    if (_drawTime >= ElementMagicMath.MaxDrawSeconds)
                    {
                        _drawTime = 0f;
                        _readyAnnounced = false;
                        _fullAnnounced = false;
                        Msg("The charge slips away — draw again.");
                    }
                    TickChargeVisual(dt);
                }
                else
                {
                    _drawTime = 0f;
                    _readyAnnounced = false;
                    _fullAnnounced = false;
                    _lastVisualElement = null;   // re-emit when drawing resumes
                    _reminder -= dt;
                    if (_reminder <= 0f) { Msg(reason); _reminder = ReminderInterval; }
                }

                // Release: attack = cone, block = wall — and BOTH TOGETHER (the
                // chord) is the element's ULTIMATE, the Unbinding. A lone press is
                // buffered for ChordWindowSeconds so the chord's second button can
                // land before the single-form cast commits; the buffered form fires
                // when the window closes (a barely-perceptible release delay).
                bool atk = altHeld ? Input.IsKeyDown(InputKey.LeftMouseButton)  : Input.IsKeyDown(InputKey.ControllerRTrigger);
                bool blk = altHeld ? Input.IsKeyDown(InputKey.RightMouseButton) : Input.IsKeyDown(InputKey.ControllerLTrigger);
                bool atkEdge = atk && !_prevAtk;
                bool blkEdge = blk && !_prevBlk;
                if ((atkEdge && (blk || _pendingForm == CastForm.Wall))
                 || (blkEdge && (atk || _pendingForm == CastForm.Attack)))
                {
                    _pendingForm = null; _pendingTimer = 0f;
                    TryCastUltimate();
                }
                else if (atkEdge) { _pendingForm = CastForm.Attack; _pendingTimer = ElementUltimateMath.ChordWindowSeconds; }
                else if (blkEdge) { _pendingForm = CastForm.Wall;   _pendingTimer = ElementUltimateMath.ChordWindowSeconds; }
                else if (_pendingForm != null)
                {
                    _pendingTimer -= dt;
                    if (_pendingTimer <= 0f)
                    {
                        CastForm form = _pendingForm.Value;
                        _pendingForm = null;
                        TryCast(form);
                    }
                }
                _prevAtk = atk; _prevBlk = blk;
            }
            else if (_wasFocusing)
            {
                // Focus released with a press still buffered: commit it now, or a
                // tap in the chord window's last instant would be silently lost.
                if (_pendingForm != null)
                {
                    CastForm form = _pendingForm.Value;
                    _pendingForm = null; _pendingTimer = 0f;
                    TryCast(form);
                }
                _wasFocusing = false;
                _drawTime = 0f;
                _prevAtk = _prevBlk = false;
                _readyAnnounced = false;
                _fullAnnounced = false;
                _lastVisualElement = null;
                try { if (Agent.Main != null) SpellEffects.EndCastLoop(Agent.Main); } catch { }
                // Clears the body contour glow left by the charge visual.
                try { if (Agent.Main != null) SpellEffects.EndFocusVisual(Agent.Main); } catch { }
            }
        }

        // Emit the element's charging visual on a short interval, and immediately
        // whenever the loaded element changes, so the aura around the caster always
        // matches what they are drawing — flames for Fire, vines/earth for Earth,
        // splashes for Water, gusts for Wind, void-light for Spirit.
        private static void TickChargeVisual(float dt)
        {
            var caster = Agent.Main;
            if (caster == null || !caster.IsActive()) return;
            var el = MageElementKnowledge.Loaded;

            if (_lastVisualElement != el)
            {
                EmitChargeVisual(caster, el);
                _lastVisualElement = el;
                _visualTimer = VisualInterval;
                return;
            }
            _visualTimer -= dt;
            if (_visualTimer <= 0f)
            {
                EmitChargeVisual(caster, el);
                _visualTimer = VisualInterval;
            }
        }

        private static void EmitChargeVisual(Agent caster, MagicElement el)
        {
            Vec3 pos; try { pos = caster.Position; } catch { return; }
            bool ashen = false; try { ashen = MageKnowledge.IsAshen; } catch { }
            Vec3 up = new Vec3(0f, 0f, 0.8f);

            // Element-corresponding particles engulf the caster while charging.
            try
            {
                switch (el)
                {
                    case MagicElement.Fire:
                        // Flames, as before — but the Ashen burn cold (light only).
                        if (!ashen) SpellEffects.SpawnTempFireParticle(pos, VisualDuration);
                        break;
                    case MagicElement.Wind:
                        SpellEffects.SpawnNatureBurst(pos, NatureElement.Wind,  VisualDuration);
                        break;
                    case MagicElement.Earth:
                        SpellEffects.SpawnNatureBurst(pos, NatureElement.Earth, VisualDuration);
                        break;
                    case MagicElement.Water:
                        SpellEffects.SpawnNatureBurst(pos, NatureElement.Water, VisualDuration);
                        break;
                    case MagicElement.Spirit:
                        // No earthly debris for the void — faint wisps and the violet light carry it.
                        SpellEffects.SpawnNatureBurst(pos, NatureElement.Wind,  VisualDuration * 0.6f);
                        break;
                }
            }
            catch { }

            // Element-coloured light and a body contour glow, tinted to the element
            // (the Ashen cold mask is applied by ElementLightRgb).
            try { SpellEffects.SpawnTempLightRgb(pos + up, ElementSpellEffects.ElementLightRgb(el, ashen), 8f, VisualDuration + 0.2f); } catch { }
            try { SpellEffects.BeginAgentGlow(caster, FocusSchool(), VisualInterval + 0.3f); } catch { }
        }

        // W/S/A/D (or left-stick flicks) load a learned element; Fire needs no key.
        private static void ReadElementSelect(bool kb, bool pad)
        {
            if (kb)
            {
                if      (Input.IsKeyPressed(InputKey.W)) MageElementKnowledge.TryLoad(MagicElement.Wind);
                else if (Input.IsKeyPressed(InputKey.S)) MageElementKnowledge.TryLoad(MagicElement.Earth);
                else if (Input.IsKeyPressed(InputKey.A)) MageElementKnowledge.TryLoad(MagicElement.Water);
                else if (Input.IsKeyPressed(InputKey.D)) MageElementKnowledge.TryLoad(MagicElement.Spirit);
            }
            if (pad)
            {
                bool up    = Input.IsKeyDown(InputKey.ControllerLStickUp);
                bool down  = Input.IsKeyDown(InputKey.ControllerLStickDown);
                bool left  = Input.IsKeyDown(InputKey.ControllerLStickLeft);
                bool right = Input.IsKeyDown(InputKey.ControllerLStickRight);
                if (up    && !_prevPadUp)    MageElementKnowledge.TryLoad(MagicElement.Wind);
                if (down  && !_prevPadDown)  MageElementKnowledge.TryLoad(MagicElement.Earth);
                if (left  && !_prevPadLeft)  MageElementKnowledge.TryLoad(MagicElement.Water);
                if (right && !_prevPadRight) MageElementKnowledge.TryLoad(MagicElement.Spirit);
                _prevPadUp = up; _prevPadDown = down; _prevPadLeft = left; _prevPadRight = right;
            }
        }

        private static void TryCast(CastForm form)
        {
            // No minimum draw — an instant release is allowed (and weak). But the
            // still-hands-light gates still decide whether you can cast at all.
            string reason = ChannelBlockReason();
            if (reason != null) { Msg(reason); return; }
            var caster = Agent.Main;
            if (caster == null || !caster.IsActive()) return;

            float power = ElementMagicMath.PowerMult(_drawTime);
            var el = MageElementKnowledge.Loaded;
            try
            {
                if (form == CastForm.Attack) ElementSpellEffects.CastAttack(el, caster, power);
                else                         ElementSpellEffects.CastWall(el, caster, power);
            }
            catch { }

            // The toll is flat — the draw bought power, not a cheaper cast.
            int days = ElementMagicMath.CastAgingDays(form, MageElementKnowledge.HasNature);
            ApplyCastCost(days);
            _drawTime = 0f;        // the charge is spent — draw again
            _readyAnnounced = false;
            _fullAnnounced = false;
        }

        // The Attack+Block chord — THE UNBINDING, the loaded element's ultimate.
        // Gated three ways: the usual still-hands-light channel gates, a FULL draw
        // (7 s — the element resists a lesser one), and once per element per
        // battle. A refused chord leaves the drawn charge intact, so the player
        // can keep holding and release a normal cone/wall instead.
        private static void TryCastUltimate()
        {
            string reason = ChannelBlockReason();
            if (reason != null) { Msg(reason); return; }
            var caster = Agent.Main;
            if (caster == null || !caster.IsActive()) return;

            var el = MageElementKnowledge.Loaded;
            if (!ElementUltimateMath.CanUnbind(_drawTime))
            {
                Msg("The element resists — it must be drawn to its fullest before it can be unbound.");
                return;
            }
            if (!ElementUltimates.PlayerCanUnbind(el))
            {
                Msg($"{MageElementKnowledge.LoadedName()} has already been unbound this battle.");
                return;
            }

            bool cast = false;
            try { cast = ElementUltimates.CastPlayerUltimate(el, caster); } catch { }
            if (!cast) return;

            // The Unbinding's toll is flat and steep, like every other cast —
            // Nature halves it, the Ashen pay it in criminal standing (days × 5).
            ApplyCastCost(ElementUltimateMath.UltimateAgingDays(MageElementKnowledge.HasNature));
            _drawTime = 0f;        // the charge is spent — draw again
            _readyAnnounced = false;
            _fullAnnounced = false;
        }

        // Aging "burns through" like fire; the Ashen pay in criminal standing instead.
        private static void ApplyCastCost(int days)
        {
            if (days <= 0) return;
            try
            {
                if (MageKnowledge.IsAshen)
                {
                    _ashenBattleCasts++;
                    if (Hero.MainHero?.MapFaction is Kingdom k)
                        ChangeCrimeRatingAction.Apply(k, days * 5f, false);
                }
                else
                {
                    // The toll is paid in life expectancy — you die sooner, but the
                    // fire does not make you older here and now.
                    AgingSystem.SpendLifeExpectancy(Hero.MainHero, days);
                }
            }
            catch { }
        }

        // Null when drawing is allowed, else a short reason the player can act on.
        private static string ChannelBlockReason()
        {
            Agent c = Agent.Main;
            if (c == null || !c.IsActive()) return "There is no hand here to shape the fire.";
            try { if (c.GetCurrentVelocity().Length >= StillSpeed) return "Stand still to draw."; } catch { }
            bool steel = MageElementKnowledge.HasSteel;
            if (!steel && !SpellEffects.HasFreeHand(c))   return "Sheathe your weapon (X) to draw — or learn Steel.";
            if (!steel && NatureEffects.ArmourTooHeavy(c)) return "Armour too heavy — shed it, or learn Steel.";
            return null;
        }

        private static ColorSchool FocusSchool()
        {
            // The Ashen draw on the cold, whatever the element; otherwise Fire is red
            // and the other elements borrow the nature glow.
            try { if (MageKnowledge.IsAshen) return ColorSchool.Ashen; } catch { }
            return MageElementKnowledge.Loaded == MagicElement.Fire ? ColorSchool.Red : ColorSchool.Nature;
        }

        private static void Msg(string text)
            => InformationManager.DisplayMessage(new InformationMessage(text, new Color(0.95f, 0.55f, 0.25f)));
    }
}
