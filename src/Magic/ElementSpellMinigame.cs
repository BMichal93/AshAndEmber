// =============================================================================
// ASH AND EMBER — Magic/ElementSpellMinigame.cs
//
// The memory-rite for the unified element map workings, mirroring the old
// SpellMinigame. A three-step rite is shown, then each step must be recalled
// from three options; the score scales the working's power:
//
//   3/3 → 1.50×  Resonance — the rite was perfect.
//   2/3 → 1.20×  The working takes hold.
//   1/3 → 0.80×  The words blur.
//   0/3 → 0.50×  The words scatter.
//
// "Cast without the rite" skips straight to a 1.00× cast. The daily aging toll
// is shared with the fire workings via TalentSystem (RegisterMapCast / cost), so
// the escalating cost cannot be sidestepped by mixing systems. The Ashen pay in
// criminal standing and risk possession, exactly as before.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    internal static class ElementSpellMinigame
    {
        private static readonly Random _rng = new Random();

        // [MagicElement][stepIndex 0–2][variantIndex] — each variant is two sentences.
        private static readonly Dictionary<MagicElement, string[][]> _rituals =
            new Dictionary<MagicElement, string[][]>
        {
            [MagicElement.Fire] = new[]
            {
                new[]
                {
                    "Tilt your head back. Pull the fire high above you and let it hold.",
                    "Tilt your head back. Pull the fire high above you and let it build.",
                    "Lift your head back. Pull the fire high above you and let it hold.",
                    "Tilt your head back. Drag the fire high above you and let it hold.",
                },
                new[]
                {
                    "Find the settlement in your mind. Lock onto the shape of its walls.",
                    "Hold the settlement in your mind. Lock onto the shape of its walls.",
                    "Find the settlement in your mind. Fix onto the shape of its roofs.",
                    "Mark the settlement in your mind. Lock onto the shape of its gates.",
                },
                new[]
                {
                    "Open both hands at once. Let it fall toward the stone.",
                    "Open both hands at once. Let it drop toward the walls.",
                    "Spread both hands at once. Let it fall toward the stone.",
                    "Open both hands at once. Let it rush toward the rooftops.",
                },
            },

            [MagicElement.Wind] = new[]
            {
                new[]
                {
                    "Loosen your shoulders. Let the air remember that it was always moving.",
                    "Loosen your shoulders. Let the air recall that it was always moving.",
                    "Open your hands wide. Let the air remember that it was always moving.",
                    "Loosen your shoulders. Let the wind remember that it was always moving.",
                },
                new[]
                {
                    "Reach for the host on the wind. Find the gaps between their ranks.",
                    "Reach for the host on the wind. Find the seams between their ranks.",
                    "Reach for the column on the wind. Find the gaps between their ranks.",
                    "Reach for the host on the breeze. Find the gaps between their lines.",
                },
                new[]
                {
                    "Let it fall all at once. Order comes apart where the wind walks.",
                    "Let it fall all at once. Order breaks apart where the wind walks.",
                    "Let it drop all at once. Order comes apart where the wind walks.",
                    "Let it fall all at once. Order scatters where the wind passes.",
                },
            },

            [MagicElement.Earth] = new[]
            {
                new[]
                {
                    "Lower the fire until it is slow. Patient. Close to the earth.",
                    "Lower the fire until it is cold. Patient. Close to the earth.",
                    "Press the fire until it is slow. Patient. Close to the earth.",
                    "Lower the fire until it is slow. Waiting. Close to the earth.",
                },
                new[]
                {
                    "Reach toward what feeds them. The fire knows where the roots run.",
                    "Reach toward what feeds them. The fire knows where the roots end.",
                    "Reach toward what holds them. The fire knows where the roots run.",
                    "Reach toward what binds them. The fire follows where the roots run.",
                },
                new[]
                {
                    "Let it fall like ash settling. Do not hurry what it does.",
                    "Let it sink like ash settling. Do not hurry what it does.",
                    "Let it fall like ash settling. Do not rush what it does.",
                    "Let it drift like ash settling. Do not force what it does.",
                },
            },

            [MagicElement.Water] = new[]
            {
                new[]
                {
                    "Slow your breath until it is level. Find the still water under everything.",
                    "Slow your breath until it is level. Find the quiet water under everything.",
                    "Slow your breath until it is even. Find the still water under everything.",
                    "Slow your breath until it is level. Find the calm water under everything.",
                },
                new[]
                {
                    "Draw it up through your own ranks. Let it close what is open in them.",
                    "Draw it up through your own ranks. Let it mend what is open in them.",
                    "Draw it up through your own column. Let it close what is open in them.",
                    "Pull it up through your own ranks. Let it close what is torn in them.",
                },
                new[]
                {
                    "Release it slowly. The tide leaves them steadier than it found them.",
                    "Release it slowly. The tide leaves them calmer than it found them.",
                    "Let it go slowly. The tide leaves them steadier than it found them.",
                    "Release it gently. The tide leaves them steadier than it found them.",
                },
            },

            [MagicElement.Spirit] = new[]
            {
                new[]
                {
                    "Close your eyes. Let the fire go further than your sight can follow.",
                    "Close your eyes. Let the fire go further than your sight can reach.",
                    "Shut your eyes. Let the fire go further than your sight can follow.",
                    "Close your eyes. Let the fire travel further than your sight can follow.",
                },
                new[]
                {
                    "Follow the warmth where it pools around hidden decisions.",
                    "Follow the warmth where it pools around hidden intentions.",
                    "Follow the warmth where it gathers around hidden decisions.",
                    "Follow the heat where it pools around hidden decisions.",
                },
                new[]
                {
                    "Draw back what you found. Let it settle into something you can use.",
                    "Draw back what you found. Let it settle into something you can name.",
                    "Pull back what you found. Let it settle into something you can use.",
                    "Draw back what you found. Let it harden into something you can read.",
                },
            },
        };

        private static string[][] GetRitual(MagicElement el) =>
            _rituals.TryGetValue(el, out var r) ? r : _rituals[MagicElement.Fire];

        // ── State ─────────────────────────────────────────────────────────────────
        private static MagicElement _element;
        private static string[][]   _steps;
        private static int[]        _correctIdx;
        private static int          _position;
        private static int          _hits;

        // ── Entry point ─────────────────────────────────────────────────────────────
        internal static void Begin(MagicElement el)
        {
            _element    = el;
            _position   = 0;
            _hits       = 0;
            _steps      = GetRitual(el);
            _correctIdx = new[]
            {
                _rng.Next(_steps[0].Length),
                _rng.Next(_steps[1].Length),
                _rng.Next(_steps[2].Length),
            };
            ShowRitual();
        }

        private static void ShowRitual()
        {
            string i1 = _steps[0][_correctIdx[0]];
            string i2 = _steps[1][_correctIdx[1]];
            string i3 = _steps[2][_correctIdx[2]];

            string body =
                "The fire stirs. The rite calls for:\n\n" +
                $"  I.   {i1}\n\n" +
                $"  II.  {i2}\n\n" +
                $"  III. {i3}\n\n" +
                "Commit it to memory.";

            InformationManager.ShowInquiry(new InquiryData(
                $"The Rite of {ElementMapSpells.Name(_element)}",
                body,
                true, true,
                "I am ready.", "Cast without the rite.",
                () => { MageKnowledge._deferredInquiry = ShowRecall; },
                () => { Resolve(1f); }
            ), true, true);
        }

        private static void ShowRecall()
        {
            if (_position >= _steps.Length) { Resolve(ScoreMult()); return; }

            string spellName = ElementMapSpells.Name(_element);
            string question  = _position == 0 ? "How did the rite begin?"
                             : _position == 1 ? "How did the rite continue?"
                             :                  "How did the rite end?";
            string progress  = _position == 0 ? "" : $"  {_hits} of {_position} correct so far.\n\n";

            int correctVariant = _correctIdx[_position];
            string correctId   = $"v{correctVariant}";

            var wrongIndices = Enumerable.Range(0, _steps[_position].Length)
                .Where(i => i != correctVariant)
                .OrderBy(_ => _rng.Next())
                .Take(2)
                .ToList();

            var shown = new[] { correctVariant }
                .Concat(wrongIndices)
                .OrderBy(_ => _rng.Next())
                .Select(idx => new InquiryElement($"v{idx}", _steps[_position][idx], null, true, _steps[_position][idx]))
                .ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"{spellName}  —  Step {_position + 1} of 3",
                $"{progress}{question}",
                shown,
                false, 1, 1,
                "Speak it.", "",
                chosen =>
                {
                    try
                    {
                        string spoken  = chosen?[0]?.Identifier as string ?? "";
                        bool   correct = spoken == correctId;
                        if (correct) _hits++;

                        string correctText = _steps[_position][_correctIdx[_position]];
                        string toast = correct ? "Correct." : $"The rite called for: {correctText}";
                        try { MBInformationManager.AddQuickInformation(new TextObject(toast)); } catch { }

                        _position++;
                        MageKnowledge._deferredInquiry = ShowRecall;
                    }
                    catch { }
                },
                null, "", false
            ), false, true);
        }

        private static float ScoreMult() =>
            _hits == 3 ? 1.50f : _hits == 2 ? 1.20f : _hits == 1 ? 0.80f : 0.50f;

        // ── Resolution: pay the toll, then loose the working ────────────────────────
        private static void Resolve(float mult)
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
                    // Resonance (Sorcerer): the first working each day costs nothing,
                    // and later ones are spared one time in four — the same grace the
                    // old fire map-spells carried.
                    bool spared = TalentSystem.Has(TalentId.Sorcerer)
                        && (TalentSystem.DailyCastCount == 0 || _rng.Next(4) == 0);
                    if (spared)
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Resonance — the fire gives back.", new Color(0.9f, 0.6f, 0.3f)));
                    else
                    {
                        AgingSystem.SpendLifeExpectancy(Hero.MainHero, cost);
                        if (cost > 1)
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"The fire demands more — {cost} days of life.", new Color(0.9f, 0.5f, 0.2f)));
                    }
                }
                TalentSystem.RegisterMapCast();
            }
            catch { }

            // Only announce resonance when the rite was actually attempted.
            if (mult != 1f)
            {
                Color colour = _hits == 3 ? new Color(1.0f, 0.85f, 0.3f)
                             : _hits >= 1 ? new Color(0.9f, 0.6f, 0.3f)
                             :              new Color(0.6f, 0.5f, 0.5f);
                string flavor = _hits == 3 ? "Resonance — the rite was perfect."
                              : _hits == 2 ? "The working takes hold."
                              : _hits == 1 ? "The words blur — the fire catches unevenly."
                              :              "The words scatter — the fire finds its own shape.";
                InformationManager.DisplayMessage(new InformationMessage(flavor, colour));
            }

            try { ElementMapSpells.Execute(_element, mult); } catch { }
        }
    }
}
