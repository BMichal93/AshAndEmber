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
        private static readonly Random _rng = new Random();
        private static bool  _wasFocusing;
        private static float _drawTime;            // seconds drawn since focus / last cast
        private static bool  _prevAtk, _prevBlk;
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
                        Msg($"{MageElementKnowledge.LoadedName()} fully charged — release now.");
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

                // Release: attack = cone, block = wall.
                bool atk = altHeld ? Input.IsKeyDown(InputKey.LeftMouseButton)  : Input.IsKeyDown(InputKey.ControllerRTrigger);
                bool blk = altHeld ? Input.IsKeyDown(InputKey.RightMouseButton) : Input.IsKeyDown(InputKey.ControllerLTrigger);
                if (atk && !_prevAtk) TryCast(CastForm.Attack);
                if (blk && !_prevBlk) TryCast(CastForm.Wall);
                _prevAtk = atk; _prevBlk = blk;
            }
            else if (_wasFocusing)
            {
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

            // Sorcery in the tournament is death and disqualification — the arena
            // crowd does not forgive the fire (restored from the retired input path).
            if (IsInTournament())
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Sorcery in the tournament — you are disqualified!",
                    Color.FromUint(0xFFFF4444)));
                try { SpellEffects.QueueKill(caster); } catch { }
                return;
            }

            float power = ElementMagicMath.PowerMult(_drawTime);
            var el = MageElementKnowledge.Loaded;
            try
            {
                if (form == CastForm.Attack) ElementSpellEffects.CastAttack(el, caster, power);
                else                         ElementSpellEffects.CastWall(el, caster, power);
            }
            catch { }

            // Flashfire — sometimes the fire finds the shape again on its own:
            // 10% chance the working echoes at once, at no additional toll.
            try
            {
                if (TalentSystem.Has(TalentId.Flashfire) && _rng.Next(10) == 0)
                {
                    if (form == CastForm.Attack) ElementSpellEffects.CastAttack(el, caster, power);
                    else                         ElementSpellEffects.CastWall(el, caster, power);
                    Msg("Flashfire — the working echoes!");
                }
            }
            catch { }

            // The toll is flat — the draw bought power, not a cheaper cast — but the
            // old pipeline's cost talents and rites still keep their word: Tempered,
            // Kinship, the Temple covenant and the Unbroken Ward all lighten it.
            int days = ElementMagicMath.CastAgingDays(form, MageElementKnowledge.HasNature);
            days = ApplyCostTalents(days);
            ApplyCastCost(days);
            _drawTime = 0f;        // the charge is spent — draw again
            _readyAnnounced = false;
            _fullAnnounced = false;
        }

        // Gather the cost-talent state and defer to the pure math (testable).
        private static int ApplyCostTalents(int baseDays)
        {
            try
            {
                bool tempered = TalentSystem.Has(TalentId.BattleMage);
                float age = 0f;
                try { if (Hero.MainHero != null) age = (float)Hero.MainHero.Age; } catch { }
                int allies = 0;
                try { if (TalentSystem.Has(TalentId.Camaraderie)) allies = CountAlliedMageLords(); } catch { }
                bool covenant = false;
                try { covenant = TempleCovenant.CovenantActive; } catch { }
                bool ward = false;
                try { ward = TalentSystem.Has(TalentId.UnbrokenWard) && CampaignMapEvents.ProtectedDaysRemaining > 0; } catch { }
                return ElementMagicMath.AdjustedCastDays(baseDays, tempered, age, allies, covenant, ward);
            }
            catch { return baseDays; }
        }

        // Kinship: allied mage lords fighting beside the player in this mission.
        private static int CountAlliedMageLords()
        {
            if (Mission.Current == null || Agent.Main == null) return 0;
            int count = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || !a.IsHero || a == Agent.Main || a.Team != Agent.Main.Team) continue;
                    Hero h = (a.Character as CharacterObject)?.HeroObject;
                    if (h != null && ColourLordRegistry.IsColourLord(h)) count++;
                }
            }
            catch { }
            return count;
        }

        private static bool IsInTournament()
        {
            try
            {
                if (Mission.Current == null) return false;
                foreach (MissionBehavior b in Mission.Current.MissionBehaviors)
                    if (b.GetType().Name.Contains("Tournament")) return true;
            }
            catch { }
            return false;
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
            // Warcast (legacy talent): the fire flows through a full hand — waives the
            // free-hand gate, though only Steel also bears the armour's weight.
            bool armed = false;
            try { armed = TalentSystem.Has(TalentId.ArmedCasting); } catch { }
            if (!steel && !armed && !SpellEffects.HasFreeHand(c)) return "Sheathe your weapon (X) to draw — or learn Steel.";
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
