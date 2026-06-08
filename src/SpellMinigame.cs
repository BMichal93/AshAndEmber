// =============================================================================
// ASH AND EMBER — SpellMinigame.cs
// Ritual memory game for campaign map spell casting.
//
// The player is shown a 3-step ritual description — each step is a pair of
// sentences. Three variant phrasings exist per step; one is drawn at random
// each cast. The player must then identify each step from its variants.
//
//   3/3 → 1.50×  Resonance — the rite was perfect.
//   2/3 → 1.20×  The working takes hold, amplified.
//   1/3 → 0.80×  The words blur — the fire catches unevenly.
//   0/3 → 0.50×  The words scatter — the fire finds its own shape.
//
// A "Cast without the rite" button on the sequence screen skips the game
// and casts at 1.00× for players who prefer the direct route.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    internal static class SpellMinigame
    {
        private static readonly Random _rng = new Random();

        // ── Ritual step pools ─────────────────────────────────────────────────
        // [TalentId][stepIndex (0–2)][variantIndex (0–2)]
        // Each variant is exactly two sentences.

        private static readonly Dictionary<TalentId, string[][]> _rituals =
            new Dictionary<TalentId, string[][]>
        {
            // Each step has 6 variants. Within each pool the varying word shifts
            // position across entries — end, middle, start — so the player cannot
            // exploit a fixed "check word N" strategy. ShowRecall always shows
            // 3 options: the correct variant plus 2 randomly drawn wrong ones.

            [TalentId.Inspire] = new[]
            {
                new[] // Step 1 — what you lift/draw the fire toward, or what stirs
                {
                    "Breathe until the fire stirs. Lift it toward your hands.",
                    "Breathe until the fire stirs. Lift it toward your voice.",
                    "Breathe until the fire stirs. Lift it toward your chest.",
                    "Breathe until the warmth stirs. Lift it toward your hands.",
                    "Breathe until the fire wakes. Lift it toward your hands.",
                    "Breathe until the fire stirs. Draw it toward your hands.",
                },
                new[] // Step 2 — how the warmth widens, or what verb moves it
                {
                    "Let the warmth widen outward. Do not direct it — let it find the men beside you.",
                    "Let the warmth widen inward. Do not direct it — let it find the men beside you.",
                    "Let the warmth widen upward. Do not direct it — let it find the men beside you.",
                    "Let the warmth spread outward. Do not direct it — let it find the men beside you.",
                    "Let the warmth widen outward. Do not guide it — let it find the men beside you.",
                    "Let the warmth widen outward. Do not direct it — let it find the men around you.",
                },
                new[] // Step 3 — what it passes through, or what you do with it
                {
                    "Release it all at once. What you carry passes to them like heat through stone.",
                    "Release it all at once. What you carry passes to them like light through cloud.",
                    "Release it all at once. What you carry passes to them like wind through grass.",
                    "Release it all at once. What you carry passes to them like fire through wood.",
                    "Release it all at once. What you give passes to them like heat through stone.",
                    "Pour it all at once. What you carry passes to them like heat through stone.",
                },
            },

            [TalentId.BreakWills] = new[]
            {
                new[] // Step 1 — the thread you find, or the word you use to still yourself
                {
                    "Still yourself completely. Find the thread of doubt that already runs through them.",
                    "Still yourself completely. Find the thread of fear that already runs through them.",
                    "Still yourself completely. Find the thread of cold that already runs through them.",
                    "Empty yourself completely. Find the thread of doubt that already runs through them.",
                    "Still yourself completely. Find the line of doubt that already runs through them.",
                    "Still yourself completely. Find the thread of doubt that already lives in them.",
                },
                new[] // Step 2 — where you press, or what you reach toward
                {
                    "Reach toward their resolve. Press where it is thinnest.",
                    "Reach toward their resolve. Press where it is coldest.",
                    "Reach toward their resolve. Press where it is darkest.",
                    "Reach toward their courage. Press where it is thinnest.",
                    "Reach toward their resolve. Strike where it is thinnest.",
                    "Press toward their resolve. Find where it is thinnest.",
                },
                new[] // Step 3 — what form you release it as, or what verb you use
                {
                    "Release it as shadow. Do not draw any part of it back.",
                    "Release it as cold. Do not draw any part of it back.",
                    "Release it as weight. Do not draw any part of it back.",
                    "Release it as shadow. Do not pull any part of it back.",
                    "Send it as shadow. Do not draw any part of it back.",
                    "Release it as shadow. Do not call any part of it back.",
                },
            },

            [TalentId.Plague] = new[]
            {
                new[] // Step 1 — quality the fire takes, or how you lower it
                {
                    "Lower the fire until it is slow. Patient. Close to the earth.",
                    "Lower the fire until it is cold. Patient. Close to the earth.",
                    "Lower the fire until it is still. Patient. Close to the earth.",
                    "Lower the fire until it is slow. Deliberate. Close to the earth.",
                    "Draw the fire until it is slow. Patient. Close to the earth.",
                    "Lower the fire until it is slow. Patient. Heavy with the earth.",
                },
                new[] // Step 2 — what you reach toward, or what knows the roots
                {
                    "Reach toward what feeds them. The fire knows where the roots run.",
                    "Reach toward what shelters them. The fire knows where the roots run.",
                    "Reach toward what holds them. The fire knows where the roots run.",
                    "Reach toward what feeds them. The fire knows where the roots end.",
                    "Reach toward what feeds them. The cold knows where the roots run.",
                    "Reach toward what feeds them. The fire follows where the roots run.",
                },
                new[] // Step 3 — what it falls like, or how you release it
                {
                    "Let it fall like ash settling. Do not hurry what it does.",
                    "Let it fall like rain settling. Do not hurry what it does.",
                    "Let it fall like cold settling. Do not hurry what it does.",
                    "Let it fall like ash settling. Do not rush what it does.",
                    "Let it sink like ash settling. Do not hurry what it does.",
                    "Let it fall like ash landing. Do not hurry what it does.",
                },
            },

            [TalentId.Clairvoyance] = new[]
            {
                new[] // Step 1 — what the fire outpaces, or what you close
                {
                    "Close your eyes. Let the fire go further than your sight can follow.",
                    "Close your eyes. Let the fire go further than your voice can follow.",
                    "Close your eyes. Let the fire go further than your reach can follow.",
                    "Close your eyes. Let the fire go further than your sight can reach.",
                    "Close your mind. Let the fire go further than your sight can follow.",
                    "Close your eyes. Let the fire travel further than your sight can follow.",
                },
                new[] // Step 2 — what gathers in the warmth, or what verb it uses
                {
                    "Follow the warmth where it pools around hidden decisions.",
                    "Follow the warmth where it pools around hidden intentions.",
                    "Follow the warmth where it pools around hidden loyalties.",
                    "Follow the warmth where it gathers around hidden decisions.",
                    "Follow the heat where it pools around hidden decisions.",
                    "Follow the warmth where it pools around buried decisions.",
                },
                new[] // Step 3 — what it settles into, or how you draw it back
                {
                    "Draw back what you found. Let it settle into something you can use.",
                    "Draw back what you found. Let it settle into something you can trust.",
                    "Draw back what you found. Let it settle into something you can name.",
                    "Draw back what you found. Let it harden into something you can use.",
                    "Pull back what you found. Let it settle into something you can use.",
                    "Draw back what you found. Let it settle into something you can act on.",
                },
            },

            [TalentId.Extinguish] = new[]
            {
                new[] // Step 1 — final quality of the compressed fire, or the verb
                {
                    "Compress the fire between your hands until it is small and hard and still.",
                    "Compress the fire between your hands until it is small and hard and dense.",
                    "Compress the fire between your hands until it is small and hard and bright.",
                    "Compress the fire between your palms until it is small and hard and still.",
                    "Hold the fire between your hands until it is small and hard and still.",
                    "Compress the fire between your hands until it is tight and hard and still.",
                },
                new[] // Step 2 — where you fix your attention, or what you find
                {
                    "Find the warmth of their position across the distance. Fix it in your eyes.",
                    "Find the warmth of their position across the distance. Fix it in your mind.",
                    "Find the warmth of their position across the distance. Fix it in your will.",
                    "Find the heat of their position across the distance. Fix it in your eyes.",
                    "Find the warmth of their position across the distance. Lock it in your eyes.",
                    "Feel the warmth of their position across the distance. Fix it in your eyes.",
                },
                new[] // Step 3 — the character of the motion, or what you do not do
                {
                    "Send it in one sharp motion. Do not soften any part of what you release.",
                    "Send it in one clean motion. Do not soften any part of what you release.",
                    "Send it in one swift motion. Do not soften any part of what you release.",
                    "Send it in one sharp motion. Do not hold any part of what you release.",
                    "Cast it in one sharp motion. Do not soften any part of what you release.",
                    "Send it in one sharp strike. Do not soften any part of what you release.",
                },
            },

            [TalentId.Ashstorm] = new[]
            {
                new[] // Step 1 — what the fire does above you, or how you move
                {
                    "Tilt your head back. Pull the fire high above you and let it hold.",
                    "Tilt your head back. Pull the fire high above you and let it build.",
                    "Tilt your head back. Pull the fire high above you and let it turn.",
                    "Tilt your head up. Pull the fire high above you and let it hold.",
                    "Tilt your head back. Drive the fire high above you and let it hold.",
                    "Tilt your head back. Pull the fire far above you and let it hold.",
                },
                new[] // Step 2 — what you lock onto, or what verb you use
                {
                    "Find the settlement in your mind. Lock onto the shape of its walls.",
                    "Find the settlement in your mind. Lock onto the shape of its roofs.",
                    "Find the settlement in your mind. Lock onto the shape of its gates.",
                    "Find the settlement in your mind. Fix onto the shape of its walls.",
                    "Hold the settlement in your mind. Lock onto the shape of its walls.",
                    "Find the settlement in your mind. Lock onto the weight of its walls.",
                },
                new[] // Step 3 — what it falls toward, or how it falls
                {
                    "Open both hands at once. Let it fall toward the stone.",
                    "Open both hands at once. Let it fall toward the walls.",
                    "Open both hands at once. Let it fall toward the fires.",
                    "Open both hands at once. Let it drop toward the stone.",
                    "Open both hands at once. Let it fall into the stone.",
                    "Open both hands at once. Let it fall toward the smoke.",
                },
            },

            [TalentId.Fade] = new[]
            {
                new[] // Step 1 — what absorbs your outline, or how the fire changes
                {
                    "Thin the fire until it barely registers. Let the air absorb your outline.",
                    "Thin the fire until it barely registers. Let the dark absorb your outline.",
                    "Thin the fire until it barely registers. Let the cold absorb your outline.",
                    "Thin the fire until it barely flickers. Let the air absorb your outline.",
                    "Dim the fire until it barely registers. Let the air absorb your outline.",
                    "Thin the fire until it barely registers. Let the air dissolve your outline.",
                },
                new[] // Step 2 — what settles around you, or how it comes
                {
                    "Stand still. Let the veil come down around you rather than moving toward it.",
                    "Stand still. Let the mist come down around you rather than moving toward it.",
                    "Stand still. Let the ash come down around you rather than moving toward it.",
                    "Stand still. Let the veil fall down around you rather than moving toward it.",
                    "Be still. Let the veil come down around you rather than moving toward it.",
                    "Stand still. Let the veil come down around you rather than reaching for it.",
                },
                new[] // Step 3 — what you release, or what verb you use
                {
                    "Release only the light. Keep everything else exactly as it is.",
                    "Release only the heat. Keep everything else exactly as it is.",
                    "Release only the shape. Keep everything else exactly as it is.",
                    "Release only the light. Hold everything else exactly as it is.",
                    "Let go of only the light. Keep everything else exactly as it is.",
                    "Release only the light. Keep everything else still as it is.",
                },
            },
        };

        private static string[][] GetRitual(TalentId id) =>
            _rituals.TryGetValue(id, out var r) ? r : _rituals[TalentId.Inspire];

        // ── State ─────────────────────────────────────────────────────────────

        private static TalentId _spellId;
        private static string[][] _steps;         // [stepIndex][variantIndex]
        private static int[]      _correctIdx;    // which variant is correct for each step
        private static int        _position;      // 0–2, current recall step
        private static int        _hits;          // correct answers so far

        // ── Entry point ───────────────────────────────────────────────────────

        internal static void Begin(TalentId id)
        {
            _spellId    = id;
            _position   = 0;
            _hits       = 0;
            _steps      = GetRitual(id);
            _correctIdx = new[]
            {
                _rng.Next(_steps[0].Length),
                _rng.Next(_steps[1].Length),
                _rng.Next(_steps[2].Length),
            };
            ShowRitual();
        }

        // ── Phase 1: show the full ritual ─────────────────────────────────────

        private static void ShowRitual()
        {
            string i1 = _steps[0][_correctIdx[0]];
            string i2 = _steps[1][_correctIdx[1]];
            string i3 = _steps[2][_correctIdx[2]];

            string body =
                $"The fire stirs. The rite calls for:\n\n" +
                $"  I.   {i1}\n\n" +
                $"  II.  {i2}\n\n" +
                $"  III. {i3}\n\n" +
                $"Commit it to memory.";

            InformationManager.ShowInquiry(new InquiryData(
                "The Rite",
                body,
                true, true,
                "I am ready.", "Cast without the rite.",
                () => { MageKnowledge._deferredInquiry = ShowRecall; },
                () => { try { TalentSystem.ExecuteMapSpell(_spellId, 1f); } catch { } }
            ), true, true);
        }

        // ── Phase 2–4: one recall dialog per step ────────────────────────────

        private static void ShowRecall()
        {
            if (_position >= _steps.Length)
            {
                Resolve();
                return;
            }

            string spellName = TalentSystem.GetDef(_spellId).Name;
            string question  = _position == 0 ? "How did the rite begin?"
                             : _position == 1 ? "How did the rite continue?"
                             :                  "How did the rite end?";
            string progress  = _position == 0 ? "" : $"  {_hits} of {_position} correct so far.\n\n";

            // Always show exactly 3 options: the correct variant plus 2 random wrong ones.
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

            var options = shown;

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"{spellName}  —  Step {_position + 1} of 3",
                $"{progress}{question}",
                options,
                false, 1, 1,
                "Speak it.", "",
                chosen =>
                {
                    try
                    {
                        string spoken  = chosen?[0]?.Identifier as string ?? "";
                        bool   correct = spoken == correctId;
                        if (correct) _hits++;

                        string correct_text = _steps[_position][_correctIdx[_position]];
                        string toast = correct
                            ? "Correct."
                            : $"The rite called for: {correct_text}";
                        try { MBInformationManager.AddQuickInformation(new TextObject(toast)); } catch { }

                        _position++;
                        MageKnowledge._deferredInquiry = ShowRecall;
                    }
                    catch { }
                },
                null, "", false
            ), false, true);
        }

        // ── Resolution ────────────────────────────────────────────────────────

        private static void Resolve()
        {
            float mult = _hits == 3 ? 1.50f
                       : _hits == 2 ? 1.20f
                       : _hits == 1 ? 0.80f
                       :              0.50f;

            Color colour = _hits == 3
                ? new Color(1.0f, 0.85f, 0.3f)
                : _hits >= 1
                    ? new Color(0.9f, 0.6f, 0.3f)
                    : new Color(0.6f, 0.5f, 0.5f);

            string flavor = _hits switch
            {
                3 => "Resonance — the rite was perfect.",
                2 => "The working takes hold.",
                1 => "The words blur — the fire catches unevenly.",
                _ => "The words scatter — the fire finds its own shape.",
            };
            InformationManager.DisplayMessage(new InformationMessage(flavor, colour));

            try { TalentSystem.ExecuteMapSpell(_spellId, mult); } catch { }
        }
    }
}
