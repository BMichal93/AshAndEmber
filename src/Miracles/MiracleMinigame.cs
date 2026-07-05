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
            // Mercy / The Mending Road — healing.
            // "Domine, non sum dignus... sed tantum dic verbo, et sanabitur anima mea."
            [MiracleType.MercyRelief] = new[]
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

            // Calculating / Far-Sight — guidance.
            // "Lead, kindly Light" / "Domine, ut videam" (Lord, that I may see).
            [MiracleType.InsightSight] = new[]
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

            // Valor / The Long March — courage on the road. "The Lord is my strength and my song."
            [MiracleType.ValorMarch] = new[]
            {
                new[]
                {
                    "The light is my strength and my song. I will go out, and I will not be afraid.",
                    "The light is my shield and my song. I will go out, and I will not be afraid.",
                    "The light is my strength and my song. I will march out, and I will not be afraid.",
                    "The light is my strength and my song. I will go out, and I will not be dismayed.",
                    "The light is my strength and my song. We will go out, and we will not be afraid.",
                },
                new[]
                {
                    "Strengthen the going-out and the coming-in. Let no heart fail upon the road.",
                    "Strengthen the going-out and the coming-in. Let no heart break upon the road.",
                    "Strengthen the going-out and the coming-in. Let no heart fail along the road.",
                    "Steady the going-out and the coming-in. Let no heart fail upon the road.",
                    "Strengthen the marching-out and the coming-in. Let no heart fail upon the road.",
                },
                new[]
                {
                    "Set our faces forward and our feet sure. We will be there before the light fails.",
                    "Set our faces forward and our feet sure. We will be there before the day fails.",
                    "Set our faces onward and our feet sure. We will be there before the light fails.",
                    "Set our faces forward and our feet swift. We will be there before the light fails.",
                    "Set our faces forward and our feet sure. We will arrive before the light fails.",
                },
            },

            // Honor / The Sworn Word — an oath before the light. "Let your yes be yes."
            [MiracleType.HonorOath] = new[]
            {
                new[]
                {
                    "Let my word be a stone, not a reed. What I swear, I swear before the light.",
                    "Let my word be a stone, not a reed. What I vow, I vow before the light.",
                    "Let my word be a stone, not a straw. What I swear, I swear before the light.",
                    "Let my word be iron, not a reed. What I swear, I swear before the light.",
                    "Let my word be a stone, not a reed. What I swear, I swear under the light.",
                },
                new[]
                {
                    "Bind me to what I have promised. Let my yes be yes, and my no be no.",
                    "Hold me to what I have promised. Let my yes be yes, and my no be no.",
                    "Bind me to what I have sworn. Let my yes be yes, and my no be no.",
                    "Bind me to what I have promised. Let my yes be yes, and my nay be nay.",
                    "Bind me to all I have promised. Let my yes be yes, and my no be no.",
                },
                new[]
                {
                    "Hold me to my oath when it costs me. Honour is the debt I mean to pay.",
                    "Hold me to my oath when it wounds me. Honour is the debt I mean to pay.",
                    "Hold me to my word when it costs me. Honour is the debt I mean to pay.",
                    "Hold me to my oath when it costs me. Honour is the debt I will yet pay.",
                    "Hold me to my oath though it costs me. Honour is the debt I mean to pay.",
                },
            },

            // Generosity / The Open Hand — daily bread and the open hand. "Give us this day."
            [MiracleType.GraceBounty] = new[]
            {
                new[]
                {
                    "Open my hand before it is asked. What I have, I hold only to give.",
                    "Open my hand before it is asked. What I keep, I hold only to give.",
                    "Open my hand before it is begged. What I have, I hold only to give.",
                    "Open my hand before it is asked. What I have, I keep only to give.",
                    "Open my hand before they ask. What I have, I hold only to give.",
                },
                new[]
                {
                    "Give us this day what the day requires. Let the stores be enough, and a little over.",
                    "Give us this day what the day requires. Let the stores be full, and a little over.",
                    "Grant us this day what the day requires. Let the stores be enough, and a little over.",
                    "Give us this day what the day demands. Let the stores be enough, and a little over.",
                    "Give us this day what the day requires. Let the stores be enough, and some to spare.",
                },
                new[]
                {
                    "Let no one at my fire go hungry. The hand that opens first is never empty.",
                    "Let no one at my hearth go hungry. The hand that opens first is never empty.",
                    "Let no one at my fire go wanting. The hand that opens first is never empty.",
                    "Let no one at my fire go hungry. The hand that gives first is never empty.",
                    "Let none at my fire go hungry. The hand that opens first is never empty.",
                },
            },
        };

        // True when the prayer has a memory rite (i.e. is a map-cast prayer).
        internal static bool HasRite(MiracleType type) => _prayers.ContainsKey(type);

        private static string[][] GetPrayer(MiracleType type) =>
            _prayers.TryGetValue(type, out var p) ? p : _prayers[MiracleType.MercyRelief];

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
                try { MiracleEffects.TryUseMiracle(type, inMission: false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                () => { try { MiracleEffects.TryUseMiracle(_type, inMission: false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }
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
                        try { MBInformationManager.AddQuickInformation(new TextObject(toast)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                        _position++;
                        MageKnowledge._deferredInquiry = ShowRecall;
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                try { MiracleInventory.SpendGrace(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            try { MiracleEffects.TryUseMiracle(_type, inMission: false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static string MiracleName(MiracleType type)
        {
            try { return MiracleCatalog.Get(type).Name; } catch { return "Prayer"; }
        }
    }
}
