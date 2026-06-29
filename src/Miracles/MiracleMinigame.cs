// =============================================================================
// ASH AND EMBER — Miracles/MiracleMinigame.cs
// Ritual memory game for campaign-map PRAYER casting — the Grace counterpart of
// SpellMinigame (which serves fire magic). Modelled on it directly.
//
// The player is shown a 3-step prayer — each step a pair of sentences. Several
// variant phrasings exist per step; one is drawn at random each casting. The
// player must then recall each step from 3 shown options.
//
//   ≥1 of 3 → the prayer is heard; the miracle is cast.
//   0  of 3 → the words scatter; the light does not answer (the Grace is spent).
//
// A "Speak it plainly" button on the prayer screen skips the recall and casts
// directly, for players who would rather not play the rite.
//
// Only the four map-usable prayers have rites here; battle prayers are cast by
// the Ctrl-sequence on the battlefield and never reach this game.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    internal static class MiracleMinigame
    {
        private static readonly Random _rng = new Random();

        // ── Prayer step pools ─────────────────────────────────────────────────
        // [MiracleType][stepIndex (0–2)][variantIndex] — each variant two sentences,
        // differing from its siblings by a single word so no fixed "check word N"
        // strategy works. ShowRecall always shows the correct variant plus 2 wrong.
        private static readonly Dictionary<MiracleType, string[][]> _prayers =
            new Dictionary<MiracleType, string[][]>
        {
            // Deliverance — "Libera nos a malo." A prayer of protection against the cold.
            [MiracleType.RepelAshen] = new[]
            {
                new[]
                {
                    "Light everlasting, stand between me and the cold. Be my wall where the dark would enter.",
                    "Light everlasting, stand between me and the grave. Be my wall where the dark would enter.",
                    "Light unfailing, stand between me and the cold. Be my wall where the dark would enter.",
                    "Light everlasting, stand between me and the cold. Be my shield where the dark would enter.",
                    "Light everlasting, stand before me and the cold. Be my wall where the dark would enter.",
                },
                new[]
                {
                    "By the warmth that made me, I deny you. Go back to the silence that bore you.",
                    "By the warmth that made me, I refuse you. Go back to the silence that bore you.",
                    "By the fire that made me, I deny you. Go back to the silence that bore you.",
                    "By the warmth that made me, I deny you. Go back to the darkness that bore you.",
                    "By the warmth that made me, I deny you. Return to the silence that bore you.",
                },
                new[]
                {
                    "Deliver me from the hollow and the grey. Let nothing cold remain where the light has passed.",
                    "Deliver me from the hollow and the grey. Let nothing cold abide where the light has passed.",
                    "Deliver me from the hollow and the cold. Let nothing cold remain where the light has passed.",
                    "Deliver me from the empty and the grey. Let nothing cold remain where the light has passed.",
                    "Deliver me from the hollow and the grey. Let nothing dark remain where the light has passed.",
                },
            },

            // Healing — "Domine, non sum dignus... sed tantum dic verbo, et sanabitur anima mea."
            [MiracleType.RadiantMending] = new[]
            {
                new[]
                {
                    "Lord, I am not worthy, yet speak the word. Lay your hand where the wound is deepest.",
                    "Lord, I am not worthy, yet say the word. Lay your hand where the wound is deepest.",
                    "Lord, I am not worthy, yet speak the word. Lay your hand where the hurt is deepest.",
                    "Lord, I am not worthy, yet speak the word. Set your hand where the wound is deepest.",
                    "Lord, I am not worthy, yet speak the word. Lay your hand where the wound is darkest.",
                },
                new[]
                {
                    "Pour your mercy into the broken place. Knit what is torn as a mother mends.",
                    "Pour your mercy into the wounded place. Knit what is torn as a mother mends.",
                    "Pour your mercy into the broken place. Bind what is torn as a mother mends.",
                    "Pour your grace into the broken place. Knit what is torn as a mother mends.",
                    "Pour your mercy into the broken place. Knit what is torn as a healer mends.",
                },
                new[]
                {
                    "Let the body be whole and the soul be healed. Take nothing back that you have given.",
                    "Let the body be whole and the soul be healed. Keep nothing back that you have given.",
                    "Let the flesh be whole and the soul be healed. Take nothing back that you have given.",
                    "Let the body be whole and the heart be healed. Take nothing back that you have given.",
                    "Let the body be sound and the soul be healed. Take nothing back that you have given.",
                },
            },

            // Guidance — "Lead, kindly Light" / "Domine, ut videam" (Lord, that I may see).
            [MiracleType.LightOfGuidance] = new[]
            {
                new[]
                {
                    "Kindly Light, lead me through the dark I cannot read. I do not ask to see the whole road.",
                    "Kindly Light, guide me through the dark I cannot read. I do not ask to see the whole road.",
                    "Kindly Light, lead me through the night I cannot read. I do not ask to see the whole road.",
                    "Kindly Light, lead me through the dark I cannot read. I do not ask to see the whole path.",
                    "Gentle Light, lead me through the dark I cannot read. I do not ask to see the whole road.",
                },
                new[]
                {
                    "One step of truth is enough for me. Keep my feet from the easy and the false.",
                    "One step of truth is enough for me. Keep my feet from the wide and the false.",
                    "One step of light is enough for me. Keep my feet from the easy and the false.",
                    "One step of truth is enough for me. Turn my feet from the easy and the false.",
                    "One step of truth is enough for me. Keep my feet from the easy and the wrong.",
                },
                new[]
                {
                    "Lord, that I may see. Open the way that is right and hide the way that is loud.",
                    "Lord, that I may see. Open the way that is true and hide the way that is loud.",
                    "Lord, that I may see. Show the way that is right and hide the way that is loud.",
                    "Lord, that I may see. Open the way that is right and veil the way that is loud.",
                    "Lord, that I may see. Open the way that is right and hide the way that is bright.",
                },
            },

            // Purification — "Asperges me" / "Cor mundum crea in me" (Create in me a clean heart).
            [MiracleType.CleansingRite] = new[]
            {
                new[]
                {
                    "Wash me, and I shall be whiter than snow. Find the stain I have hidden even from myself.",
                    "Wash me, and I shall be cleaner than snow. Find the stain I have hidden even from myself.",
                    "Wash me, and I shall be whiter than snow. Find the sin I have hidden even from myself.",
                    "Cleanse me, and I shall be whiter than snow. Find the stain I have hidden even from myself.",
                    "Wash me, and I shall be whiter than snow. Find the stain I have buried even from myself.",
                },
                new[]
                {
                    "Create in me a clean heart. Do not break what is sick — gently lift it out.",
                    "Create in me a clean heart. Do not break what is sick — gently draw it out.",
                    "Create in me a pure heart. Do not break what is sick — gently lift it out.",
                    "Create in me a clean heart. Do not crush what is sick — gently lift it out.",
                    "Make in me a clean heart. Do not break what is sick — gently lift it out.",
                },
                new[]
                {
                    "Let what does not belong depart in peace. Restore to me the joy that was clean.",
                    "Let what does not belong depart in peace. Return to me the joy that was clean.",
                    "Let what does not belong pass in peace. Restore to me the joy that was clean.",
                    "Let what does not belong depart in peace. Restore to me the peace that was clean.",
                    "Let what does not belong depart in quiet. Restore to me the joy that was clean.",
                },
            },
        };

        // True when the prayer has a memory rite (i.e. is a map-cast prayer).
        internal static bool HasRite(MiracleType type) => _prayers.ContainsKey(type);

        private static string[][] GetPrayer(MiracleType type) =>
            _prayers.TryGetValue(type, out var p) ? p : _prayers[MiracleType.RadiantMending];

        // ── State ─────────────────────────────────────────────────────────────

        private static MiracleType _type;
        private static string[][]  _steps;
        private static int[]       _correctIdx;
        private static int         _position;
        private static int         _hits;

        // ── Entry point ───────────────────────────────────────────────────────

        internal static void Begin(MiracleType type)
        {
            // No rite defined (or somehow a battle-only prayer) — cast plainly.
            if (!HasRite(type))
            {
                try { MiracleEffects.TryUseMiracle(type, inMission: false); } catch { }
                return;
            }

            _type       = type;
            _position   = 0;
            _hits       = 0;
            _steps      = GetPrayer(type);
            _correctIdx = new[]
            {
                _rng.Next(_steps[0].Length),
                _rng.Next(_steps[1].Length),
                _rng.Next(_steps[2].Length),
            };
            ShowPrayer();
        }

        // ── Phase 1: show the full prayer ─────────────────────────────────────

        private static void ShowPrayer()
        {
            string i1 = _steps[0][_correctIdx[0]];
            string i2 = _steps[1][_correctIdx[1]];
            string i3 = _steps[2][_correctIdx[2]];

            string body =
                "The light waits to be asked. The prayer runs:\n\n" +
                $"  I.   {i1}\n\n" +
                $"  II.  {i2}\n\n" +
                $"  III. {i3}\n\n" +
                "Commit it to memory.";

            InformationManager.ShowInquiry(new InquiryData(
                MiracleName(_type),
                body,
                true, true,
                "I am ready.", "Speak it plainly.",
                () => { MageKnowledge._deferredInquiry = ShowRecall; },
                () => { try { MiracleEffects.TryUseMiracle(_type, inMission: false); } catch { } }
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

            string name     = MiracleName(_type);
            string question  = _position == 0 ? "How did the prayer begin?"
                             : _position == 1 ? "How did the prayer continue?"
                             :                  "How did the prayer end?";
            string progress  = _position == 0 ? "" : $"  {_hits} of {_position} correct so far.\n\n";

            int correctVariant = _correctIdx[_position];
            string correctId   = $"v{correctVariant}";

            var wrongIndices = Enumerable.Range(0, _steps[_position].Length)
                .Where(i => i != correctVariant)
                .OrderBy(_ => _rng.Next())
                .Take(2)
                .ToList();

            var options = new[] { correctVariant }
                .Concat(wrongIndices)
                .OrderBy(_ => _rng.Next())
                .Select(idx => new InquiryElement($"v{idx}", _steps[_position][idx], null, true, _steps[_position][idx]))
                .ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"{name}  —  Step {_position + 1} of 3",
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

                        string toast = correct
                            ? "Correct."
                            : $"The prayer called for: {_steps[_position][_correctIdx[_position]]}";
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
            if (_hits <= 0)
            {
                // The words scatter — the light does not answer, but the Grace is spent,
                // just as a botched battle sequence always costs it.
                try { MiracleInventory.SpendGrace(); } catch { }
                InformationManager.DisplayMessage(new InformationMessage(
                    "The words scatter — the light does not answer. [Grace spent]",
                    new Color(0.6f, 0.5f, 0.5f)));
                return;
            }

            string flavor = _hits == 3
                ? "Resonance — the prayer is heard, clear and whole."
                : _hits == 2
                    ? "The prayer is heard."
                    : "The words waver, but the light answers.";
            Color colour = _hits == 3
                ? new Color(1.0f, 0.9f, 0.5f)
                : new Color(0.95f, 0.82f, 0.4f);
            InformationManager.DisplayMessage(new InformationMessage(flavor, colour));

            // The prayer answers — TryUseMiracle handles the gate, spends the Grace, and casts.
            try { MiracleEffects.TryUseMiracle(_type, inMission: false); } catch { }
        }

        private static string MiracleName(MiracleType type)
        {
            try { return MiracleCatalog.Get(type).Name; } catch { return "Prayer"; }
        }
    }
}
