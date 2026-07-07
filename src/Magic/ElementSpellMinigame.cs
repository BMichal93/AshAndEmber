// =============================================================================
// ASH AND EMBER — Magic/ElementSpellMinigame.cs
//
// The RESONANCE DRAW for the unified element map workings. Where Miracles keep
// the word-recall prayer (fitting devotion and memory), the elements are about
// FEEL — hold, charge, release — so the map-cast rite mirrors that instead of
// reusing Grace's mechanic: a charge sweeps a track (ElementResonanceBar) and
// the caster looses it with a keypress. How close the release lands to the
// element's own "true point" sets the power tier:
//
//   Perfect → 1.50×  Resonance — the charge broke exactly where it should.
//   Good    → 1.20×  The working takes hold.
//   Wide    → 0.80×  The charge slips — it catches unevenly.
//   Miss    → 0.50×  The charge scatters — it finds its own shape.
//
// Fast/volatile elements (Fire, Spirit) sweep quickly with a narrow perfect
// band; slow/patient ones (Earth, Water) sweep gently with a wide, forgiving
// one — see ElementResonanceMath for the tuning, kept pure and unit-tested.
//
// "Cast without the rite" skips straight to a 1.00× cast, same escape hatch
// the old memory-rite offered. If the draw is never released it disperses on
// its own after a few seconds, so nobody can stall for a "guaranteed" pass —
// and if a mission starts mid-draw (an ambush interrupts the rite) it is
// cancelled outright: no cost, no cast, nothing lost.
//
// The daily aging toll is shared with the fire workings via TalentSystem
// (RegisterMapCast / cost), so the escalating cost cannot be sidestepped by
// mixing systems. The Ashen pay in criminal standing and risk possession,
// exactly as before.
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
    internal static class ElementSpellMinigame
    {
        private static readonly Random _rng = new Random();

        // A draw left untended this long disperses on its own (auto-releases
        // wherever the sweep happens to sit) — nobody can stall for a lucky spot.
        private const float TimeoutSeconds = 6.0f;

        // ── State ─────────────────────────────────────────────────────────────
        private static bool          _active;
        private static MagicElement  _element;
        private static float         _phase;
        private static float         _elapsed;
        private static bool          _prevRelease;

        // ── Entry point ─────────────────────────────────────────────────────────
        internal static void Begin(MagicElement el)
        {
            string name = ElementMapSpells.Name(el);
            string body =
                $"{ElementMapSpells.Lore(el)}\n\n" +
                "Draw the charge and loose it when the fire finds its own true point — " +
                "hold the rhythm of it in your mind before you begin.";

            InformationManager.ShowInquiry(new InquiryData(
                $"The Rite of {name}",
                body,
                true, true,
                "Draw the charge.", "Cast without the rite.",
                () => { MageKnowledge._deferredInquiry = () => StartDraw(el); },
                () => { Resolve(el, 1f, false); }
            ), true, true);
        }

        private static void StartDraw(MagicElement el)
        {
            _element     = el;
            _active      = true;
            _phase       = 0f;
            _elapsed     = 0f;
            // Avoid a same-frame edge: a held key from the prior dialog's
            // confirm shouldn't immediately count as the release press.
            _prevRelease = ReleaseHeld();
            ElementResonanceBar.Show(el);
        }

        // ── Ticked every map frame while a draw is active ───────────────────────
        internal static void Tick(float dt)
        {
            if (!_active) return;
            try
            {
                // An ambush or any other mission start interrupts the rite outright.
                if (Mission.Current != null) { Cancel(); return; }

                _elapsed += dt;
                _phase   += dt * ElementResonanceMath.SweepSpeed(_element);
                float pos01 = ElementResonanceMath.TrianglePosition01(_phase);
                ElementResonanceBar.UpdateFill(pos01);

                bool heldNow    = ReleaseHeld();
                bool pressedNow = heldNow && !_prevRelease;
                _prevRelease    = heldNow;

                bool timedOut = _elapsed >= TimeoutSeconds;
                if (pressedNow || timedOut)
                {
                    float mult = ElementResonanceMath.ResolveMult(_element, pos01);
                    MagicElement el = _element;
                    _active = false;
                    ElementResonanceBar.Hide();
                    Resolve(el, mult, timedOut);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); Cancel(); }
        }

        private static bool ReleaseHeld()
            => Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.ControllerLBumper);

        private static void Cancel()
        {
            _active = false;
            try { ElementResonanceBar.Hide(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Guards against a stale overlay surviving a save-load in the same process.
        internal static void ResetState() => Cancel();

        // ── Resolution: pay the toll, then loose the working ────────────────────
        private static void Resolve(MagicElement el, float mult, bool timedOut)
        {
            // The toll — shared daily counter with the fire workings.
            try
            {
                if (MageKnowledge.IsAshen)
                {
                    if (TalentSystem.DailyCastCount > 0 && _rng.Next(3) == 0)
                        MageKnowledge.QueuePossessionEvent();
                    if (Hero.MainHero?.MapFaction is Kingdom ashenK)
                    {
                        ChangeCrimeRatingAction.Apply(ashenK, 12f, false);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The ash spreads.", new Color(0.3f, 0.35f, 0.7f)));
                    }
                }
                else
                {
                    int cost = TalentSystem.GetDailyCastCost();
                    AgingSystem.SpendLifeExpectancy(Hero.MainHero, cost);
                    if (cost > 1)
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"The fire demands more — {cost} days of life.", new Color(0.9f, 0.5f, 0.2f)));
                }
                TalentSystem.RegisterMapCast();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Only announce a tier when the rite was actually attempted.
            if (mult != 1f)
            {
                if (timedOut)
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The charge slips loose on its own — untended too long.", new Color(0.6f, 0.5f, 0.5f)));

                Color colour = mult >= 1.50f ? new Color(1.0f, 0.85f, 0.3f)
                             : mult >= 1.20f ? new Color(0.9f, 0.7f, 0.35f)
                             : mult >= 0.80f ? new Color(0.9f, 0.6f, 0.3f)
                             :                 new Color(0.6f, 0.5f, 0.5f);
                string flavor = mult >= 1.50f ? "Resonance — the charge broke exactly where it should."
                              : mult >= 1.20f ? "The working takes hold."
                              : mult >= 0.80f ? "The charge slips — the fire catches unevenly."
                              :                 "The charge scatters — the fire finds its own shape.";
                InformationManager.DisplayMessage(new InformationMessage(flavor, colour));
            }

            try { ElementMapSpells.Execute(el, mult); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
