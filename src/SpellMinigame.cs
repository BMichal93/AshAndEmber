// =============================================================================
// ASH AND EMBER — SpellMinigame.cs
// Ritual memory game for campaign map spell casting.
//
// The player is shown a 3-step ritual description — each step is a pair of
// sentences. Three variant phrasings exist per step; one is drawn at random
// each cast. The player must then identify each step from its variants.
//
//   3/3 → 1.50×  Resonance — the rite was perfect.
//   2/3 → 1.00×  The working takes hold.             (baseline)
//   1/3 → 0.75×  The words blur — the fire catches unevenly.
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
            [TalentId.Inspire] = new[]
            {
                new[] // Step 1 — opening: how you connect
                {
                    "Take a few deep breaths. Reach towards your inner fire.",
                    "Take exactly three long breaths. Reach towards the fires burning in your soul.",
                    "Hold your breath once. Then reach towards the warmth inside you.",
                },
                new[] // Step 2 — middle: how you feel and hold it
                {
                    "Feel its warmth spreading. Let it fill you with certainty.",
                    "Feel it burning steadily. Let it rise through your chest.",
                    "Feel its rhythm pulse. Let it flow from heart to hands.",
                },
                new[] // Step 3 — close: how you release it outward
                {
                    "Extend it outward in one swift motion. Let it find every soldier who marches with you.",
                    "Open your arms wide. Pour the warmth into the air your companions breathe.",
                    "Breathe it out gently. Let it drift to every hearth in your company.",
                },
            },

            [TalentId.BreakWills] = new[]
            {
                new[] // Step 1 — opening: how you focus
                {
                    "Narrow your focus to a single point. Find the thread that binds their courage.",
                    "Still your mind completely. Find the line where your fire ends and theirs begins.",
                    "Close your eyes for a moment. Locate the shape of their conviction.",
                },
                new[] // Step 2 — middle: how you reach toward them
                {
                    "Reach toward their resolve. Feel where it is thinnest.",
                    "Press gently against their will. Find the places where doubt already lives.",
                    "Extend toward their certainty. Search for what they are trying not to fear.",
                },
                new[] // Step 3 — close: how you release the working
                {
                    "Let the shadow in. Release it without hesitation.",
                    "Turn the fire cold at the edges. Send it forward like a held breath.",
                    "Release it all at once. Do not soften what you send.",
                },
            },

            [TalentId.Plague] = new[]
            {
                new[] // Step 1 — opening: sinking the fire low
                {
                    "Let the fire sink low in your body. Make it slow and patient.",
                    "Draw the fire downward, into the earth. Let it become heavy and deliberate.",
                    "Pull the fire close to the ground. Feel it thicken and slow.",
                },
                new[] // Step 2 — middle: reaching toward the target
                {
                    "Reach toward their roots. Follow what feeds and shelters them.",
                    "Extend toward their hearth fires. Feel the warmth that sustains them.",
                    "Press toward their foundations. Find what holds their life together.",
                },
                new[] // Step 3 — close: releasing the blight
                {
                    "Release it as a slow exhalation. Let it work without haste.",
                    "Open your palm toward the earth. Let it fall and take hold quietly.",
                    "Send it gently, like ash settling. It will do what you ask of it.",
                },
            },

            [TalentId.Clairvoyance] = new[]
            {
                new[] // Step 1 — opening: stilling yourself
                {
                    "Quiet the noise around you. Let the fire reach further than your eyes.",
                    "Still your thoughts completely. Let the fire travel where you cannot follow.",
                    "Close your eyes. Let the fire seek what is beyond your ordinary sight.",
                },
                new[] // Step 2 — middle: following the threads
                {
                    "Follow the threads of influence. They run warm where power truly gathers.",
                    "Trace the lines of loyalty. They burn brighter near those who are trusted.",
                    "Read the currents of intention. They shift near those who plan and scheme.",
                },
                new[] // Step 3 — close: drawing knowledge back
                {
                    "Gather what the fire finds. Draw it back into yourself as understanding.",
                    "Pull the knowledge inward. Let it settle into something you can use.",
                    "Bring the sight home. Let it harden into certainty you can act on.",
                },
            },

            [TalentId.Extinguish] = new[]
            {
                new[] // Step 1 — opening: compressing the fire
                {
                    "Compress the fire between your hands. Make it small, hot, and precise.",
                    "Harden the fire to a dense point. Let the pressure build until it aches.",
                    "Condense the fire down to nothing visible. Hold it until it is almost unbearable.",
                },
                new[] // Step 2 — middle: locking onto the target
                {
                    "Fix your gaze on the horizon where they march. Their fires burn there.",
                    "Lock your attention on their position. Feel the heat of their numbers.",
                    "Find the warmth of their presence across the distance. It is unmistakable.",
                },
                new[] // Step 3 — close: releasing the strike
                {
                    "Release it cleanly, all at once. Let it arrive before doubt can follow.",
                    "Send it in a single sharp motion. Do not hold any part of it back.",
                    "Let it go without hesitation. One strike — nothing softened.",
                },
            },

            [TalentId.Ashstorm] = new[]
            {
                new[] // Step 1 — opening: gathering the fire above
                {
                    "Raise your eyes to the sky. Pull the fire upward, away from the earth.",
                    "Look toward the horizon where they shelter. Draw the fire above you like a held breath.",
                    "Tilt your head back. Let the fire climb past your hands and gather at height.",
                },
                new[] // Step 2 — middle: focusing on the target
                {
                    "Lock onto the shape of their walls. Feel the warmth trapped inside them.",
                    "Fix the settlement in your mind. The fire already knows where it is needed.",
                    "Find the stone and mortar in your thoughts. The fire wants what hides within.",
                },
                new[] // Step 3 — close: releasing the storm
                {
                    "Open both hands at once. Let it fall without restraint.",
                    "Push it out — all of it — toward the distant walls. Do not pull any back.",
                    "Release your breath and the fire together. Let gravity do the rest.",
                },
            },

            [TalentId.Fade] = new[]
            {
                new[] // Step 1 — opening: thinning the fire
                {
                    "Thin the fire until it becomes translucent. Let the air absorb it.",
                    "Soften the fire's edges gently. Let it blur into the space around you.",
                    "Draw the fire inward until it barely registers. Make yourself quiet.",
                },
                new[] // Step 2 — middle: letting the veil settle
                {
                    "Breathe without sound. Let the veil come to rest over you naturally.",
                    "Stand very still. Let the mist find its way to you on its own.",
                    "Exhale slowly and completely. Let the ash drift down and cover your outline.",
                },
                new[] // Step 3 — close: holding the concealment
                {
                    "Hold the shape of yourself. Release only the light.",
                    "Keep the form intact. Let only the brightness go.",
                    "Maintain what you are. Extinguish only what can be seen.",
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
            _correctIdx = new[] { _rng.Next(3), _rng.Next(3), _rng.Next(3) };
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
                ShowRecall,
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

            // Build shuffled option list — identifier is "v0"/"v1"/"v2" (variant index)
            var variants = _steps[_position]
                .Select((text, idx) => (text, id: $"v{idx}"))
                .OrderBy(_ => _rng.Next())
                .ToList();

            var options = variants
                .Select(v => new InquiryElement(v.id, v.text, null, true, v.text))
                .ToList();

            string correctId = $"v{_correctIdx[_position]}";

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
                        ShowRecall();
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
                       : _hits == 2 ? 1.00f
                       : _hits == 1 ? 0.75f
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
