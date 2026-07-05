// =============================================================================
// ASH AND EMBER — Visual/BattleWhispers.cs
// Ambient Ashen-atmosphere messages during battles involving mages or cold-fire lords.
// Fires at most 4 times per battle, spaced ~45 s apart, never repeating.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class BattleWhispers
    {
        private static float _timer             = 40f;
        private static int   _whispersFired     = 0;
        private static bool  _enabled           = false;
        private static readonly HashSet<int> _used = new HashSet<int>();
        private static readonly Random _rng       = new Random();
        private const  int MaxWhispersPerBattle   = 4;

        private static readonly string[] _whispers =
        {
            "Their cold gaze finds you through the smoke.",
            "The ash on the wind is not from the battle.",
            "You hear your name — no one's lips are moving.",
            "The fire in you pulls toward something cold on the other side.",
            "One of them is not here to win. They are here to remember what you look like afraid.",
            "The cold one studies you between blows. Learning.",
            "You smell winter. It is midsummer.",
            "Their commander's eyes find yours across the field. They hold.",
            "The ash gathers thicker around the cold-fire ones. The battle is secondary to them.",
            "Something in the enemy line is not afraid of you. That is the one to watch.",
            "You feel watched from inside your own shadow.",
            "A familiar cold brushes the back of your neck.",
            "One of their soldiers glances at you — not with hate, but with recognition.",
            "The dead on their side do not fall the way dead men should.",
            "Something beyond the treeline watches with familiar eyes.",
        };

        // Only reached at WhisperTier 3 — the cold now speaks directly to you.
        private static readonly string[] _whispersPersonal =
        {
            "The voice says your name the way someone says the name of a thing they already own.",
            "You know which one of them carries the cold. You knew before you saw them.",
            "The fire in you leans toward their line. You have to remind it which side you are on.",
            "One of them smiles at you across the field. You almost smile back.",
            "The ash drifts toward you, not away. As if it remembers where it came from.",
        };

        public static void Reset()
        {
            _timer         = 40f;
            _whispersFired = 0;
            _enabled       = false;
            _used.Clear();
        }

        public static void MissionTick(float dt)
        {
            if (_whispersFired >= MaxWhispersPerBattle) return;
            if (!SpellEffects.IsBattleMission()) return;

            _timer -= dt;
            if (_timer > 0f) return;
            _timer = 45f + _rng.Next(30);

            if (!_enabled)
            {
                bool trigger = MageKnowledge.IsAshen;
                if (!trigger)
                {
                    try
                    {
                        if (Agent.Main != null && Mission.Current != null)
                            trigger = Mission.Current.Agents.Any(a =>
                                a != null && a.IsActive() && !a.IsMount && a.IsHero &&
                                a.Team != null && Agent.Main.Team != null && a.Team != Agent.Main.Team &&
                                (a.Character as CharacterObject)?.HeroObject is Hero h &&
                                ColourLordRegistry.IsAshenLord(h));
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                if (!trigger) return;
                _enabled = true;
            }

            // Skip probability scales with how deeply the cold has seeped in.
            // Tier 3: no skip — the voice does not wait for permission.
            int whisperTier = MageKnowledge.IsMage ? MageKnowledge.WhisperTier : 0;
            int skipDenominator = whisperTier >= 3 ? 0 : whisperTier >= 2 ? 4 : 3;
            if (skipDenominator > 0 && _rng.Next(skipDenominator) == 0) return;

            // At Tier 3 the cold speaks personally — draw from the combined pool.
            bool usePersonal = whisperTier >= 3 && _rng.Next(2) == 0;
            string[] pool = usePersonal ? _whispersPersonal : _whispers;

            int idx = -1;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                // Offset personal-pool indices to keep them separate from _used for the base pool.
                int candidate = usePersonal ? _whispers.Length + _rng.Next(pool.Length) : _rng.Next(pool.Length);
                if (!_used.Contains(candidate)) { idx = candidate; break; }
            }
            if (idx < 0) return;

            _used.Add(idx);
            _whispersFired++;
            string line = usePersonal ? pool[idx - _whispers.Length] : pool[idx];

            try
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    line, new Color(0.35f, 0.4f, 0.6f)));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
