// =============================================================================
// ASH AND EMBER — SpellMinigame.cs
// Arcane sequence memory game for campaign map spell casting.
//
// The player is shown a 3-word incantation drawn from a spell-specific word
// pool, then asked to identify each word in order from a shuffled list of four.
// Score scales the spell's output power; the aging cost is always paid.
//
//   3/3 → 1.25×  Resonance — the incantation was perfect.
//   2/3 → 1.00×  The working takes hold.             (baseline)
//   1/3 → 0.85×  The words blur — the fire catches unevenly.
//   0/3 → 0.75×  The words scatter — the fire finds its own shape.
//
// A "Cast without the rite" button on the sequence screen skips the minigame
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

        // ── Per-spell word pools ──────────────────────────────────────────────

        private static readonly Dictionary<TalentId, string[]> _pools =
            new Dictionary<TalentId, string[]>
            {
                [TalentId.BreakWills]   = new[] { "SHADOW", "DREAD", "VEIL", "SILENCE", "HOLLOW", "COLD" },
                [TalentId.Inspire]      = new[] { "FLAME",  "RISE",  "SURGE", "BREATH", "WARMTH", "KINDLE" },
                [TalentId.Plague]       = new[] { "ROOT",   "ASH",   "SLOW",  "STONE",  "BLIGHT", "DUST" },
                [TalentId.Clairvoyance] = new[] { "EYE",    "THREAD","WIND",  "FAR",    "STILL",  "OPEN" },
                [TalentId.Extinguish]   = new[] { "EMBER",  "STRIKE","BREAK", "DARK",   "CUT",    "END" },
                [TalentId.Fade]         = new[] { "VEIL",   "STILL", "MIST",  "SOFT",   "GONE",   "DARK" },
            };

        private static string[] GetPool(TalentId id) =>
            _pools.TryGetValue(id, out var p)
                ? p
                : new[] { "FIRE", "ASH", "EMBER", "VEIL", "STONE", "WIND" };

        // ── State ─────────────────────────────────────────────────────────────

        private static TalentId _spellId;
        private static string[] _sequence;  // 3 words shown to the player
        private static int      _position;  // 0–2, current recall step
        private static int      _hits;      // correct picks so far

        // ── Entry point (called instead of TalentSystem.ExecuteMapSpell) ─────

        internal static void Begin(TalentId id)
        {
            _spellId  = id;
            _position = 0;
            _hits     = 0;
            _sequence = GetPool(id).OrderBy(_ => _rng.Next()).Take(3).ToArray();
            ShowSequence();
        }

        // ── Phase 1: display the sequence ─────────────────────────────────────

        private static void ShowSequence()
        {
            string seq = string.Join("  —  ", _sequence);

            InformationManager.ShowInquiry(new InquiryData(
                "The Words Arise",
                $"Reach into the fire. The incantation forms:\n\n{seq}\n\nCommit them to memory.",
                true, true,
                "I am ready.", "Cast without the rite.",
                ShowRecall,
                () => { try { TalentSystem.ExecuteMapSpell(_spellId, 1f); } catch { } }
            ), true, true);
        }

        // ── Phase 2–4: one recall dialog per word position ────────────────────

        private static void ShowRecall()
        {
            if (_position >= _sequence.Length)
            {
                Resolve();
                return;
            }

            string ordinal     = _position == 0 ? "first" : _position == 1 ? "second" : "third";
            string spellName   = TalentSystem.GetDef(_spellId).Name;
            string progress    = _position == 0 ? "" : $"  {_hits} of {_position} correct so far.\n\n";
            string correctWord = _sequence[_position];

            // 3 distractors from the full word pool, excluding words already in the sequence
            var distractors = _pools.SelectMany(kv => kv.Value)
                .Where(w => !_sequence.Contains(w))
                .Distinct()
                .OrderBy(_ => _rng.Next())
                .Take(3)
                .ToList();

            var choices = new List<string> { correctWord }
                .Concat(distractors)
                .OrderBy(_ => _rng.Next())
                .ToList();

            var options = choices
                .Select(w => new InquiryElement(w, w, null, true, ""))
                .ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"{spellName}  —  Word {_position + 1} of 3",
                $"{progress}What was the {ordinal} word of the incantation?",
                options,
                false, 1, 1,
                "Speak it.", "",
                chosen =>
                {
                    try
                    {
                        string spoken = chosen?[0]?.Identifier as string ?? "";
                        bool correct  = spoken == correctWord;
                        if (correct) _hits++;

                        string toast = correct
                            ? $"Correct — {correctWord}."
                            : $"The word was {correctWord}.";
                        try { MBInformationManager.AddQuickInformation(new TextObject(toast)); } catch { }

                        _position++;
                        ShowRecall();
                    }
                    catch { }
                },
                null, "", false
            ), false, true);
        }

        // ── Resolution: apply multiplier and fire the spell ───────────────────

        private static void Resolve()
        {
            float mult = _hits == 3 ? 1.25f
                       : _hits == 2 ? 1.00f
                       : _hits == 1 ? 0.85f
                       :              0.75f;

            Color colour = _hits == 3
                ? new Color(1.0f, 0.85f, 0.3f)
                : new Color(0.9f, 0.6f, 0.3f);

            string flavor = _hits switch
            {
                3 => "Resonance — the incantation was perfect.",
                2 => "The working takes hold.",
                1 => "The words blur — the fire catches unevenly.",
                _ => "The words scatter — the fire finds its own shape.",
            };
            InformationManager.DisplayMessage(new InformationMessage(flavor, colour));

            try { TalentSystem.ExecuteMapSpell(_spellId, mult); } catch { }
        }
    }
}
