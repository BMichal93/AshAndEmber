// =============================================================================
// ASH AND EMBER — AshenRuins/AshenRuinSystem.cs
// State management, challenge resolution, and NPC lord racing logic.
// Menus are in AshenRuinMenus.cs.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static class AshenRuinSystem
    {
        // ── Tuning ────────────────────────────────────────────────────────────
        private const int RevisitCooldownDays = 90;
        private const int LordRacingDays      = 7;
        private const float LordRaceChance    = 0.005f; // 0.5% per day per eligible ruin

        private const int AshenCrownFpBonus   = 3;     // focus pts per fragment (×3 bonus at completion)

        // ── State ─────────────────────────────────────────────────────────────
        // Ruins that were fully cleared this campaign (once per campaign floor).
        private static readonly HashSet<string> _cleared = new HashSet<string>();

        // Revisit cooldown: ruin village name → days remaining.
        private static readonly List<string> _cdKeys  = new List<string>();
        private static readonly List<int>    _cdDays  = new List<int>();

        // Dragon artifact
        private static bool _eyeFound = false;
        public  static bool EyeFound  => _eyeFound;

        // Ashen Crown fragments (3 = set complete)
        private static int _crownFragments = 0;

        // NPC lord racing
        private static string _lordRacingRuin  = null;   // ruin village name
        private static string _lordRacingId    = null;   // hero StringId
        private static int    _lordRacingDaysLeft = 0;

        // Guard spawn cooldown (ruin name → days until respawn)
        private static readonly List<string> _guardCdKeys  = new List<string>();
        private static readonly List<int>    _guardCdDays  = new List<int>();

        private static readonly Random _rng = new Random();

        // ── Public queries ─────────────────────────────────────────────────────
        public static bool IsCleared(string villageName) => _cleared.Contains(villageName);
        public static int  ClearedCount              => _cleared.Count;

        public static bool IsOnCooldown(string villageName)
        {
            int idx = _cdKeys.IndexOf(villageName);
            return idx >= 0 && _cdDays[idx] > 0;
        }

        public static int CooldownDays(string villageName)
        {
            int idx = _cdKeys.IndexOf(villageName);
            return idx >= 0 ? _cdDays[idx] : 0;
        }

        public static bool IsContested(string villageName) =>
            _lordRacingRuin == villageName && _lordRacingDaysLeft > 0;

        public static Hero ContestedBy(string villageName)
        {
            if (!IsContested(villageName) || _lordRacingId == null) return null;
            try { return Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _lordRacingId); }
            catch { return null; }
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        public static void DailyTick()
        {
            if (!MageKnowledge.IsMage) return;

            // Decrement revisit cooldowns
            for (int i = 0; i < _cdDays.Count; i++)
                if (_cdDays[i] > 0) _cdDays[i]--;

            // Decrement guard respawn cooldowns
            for (int i = 0; i < _guardCdDays.Count; i++)
                if (_guardCdDays[i] > 0) _guardCdDays[i]--;

            // Lord racing countdown
            if (_lordRacingDaysLeft > 0)
            {
                _lordRacingDaysLeft--;
                if (_lordRacingDaysLeft == 0)
                    ResolveLordTakesRuin();
                return;
            }

            // Roll for a new lord to start racing toward a Tier 3+ ruin
            TryStartLordRace();
        }

        private static void TryStartLordRace()
        {
            if (_rng.NextDouble() >= LordRaceChance) return;

            // Pick an eligible ruin (Tier 3+, not cleared this campaign, not on cooldown, not already contested)
            var eligible = AshenRuinDefs.All
                .Where(r => r.Tier >= RuinTier.Brutal
                         && !IsCleared(r.VillageName)
                         && !IsOnCooldown(r.VillageName)
                         && _lordRacingRuin != r.VillageName)
                .ToList();
            if (eligible.Count == 0) return;

            var ruin = eligible[_rng.Next(eligible.Count)];

            // Pick a mage lord that is alive and not the player
            var lords = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h != Hero.MainHero && h.IsAlive
                         && ColourLordRegistry.IsColourLord(h))
                .ToList();
            if (lords.Count == 0) return;

            Hero lord = lords[_rng.Next(lords.Count)];
            _lordRacingRuin     = ruin.VillageName;
            _lordRacingId       = lord.StringId;
            _lordRacingDaysLeft = LordRacingDays + _rng.Next(4); // 7–10 days

            InformationManager.DisplayMessage(new InformationMessage(
                $"Rumour reaches you: {lord.Name} has departed toward a place of old power.",
                new Color(0.55f, 0.45f, 0.75f)));
        }

        private static void ResolveLordTakesRuin()
        {
            if (_lordRacingRuin == null) return;
            var def = AshenRuinDefs.All.FirstOrDefault(r => r.VillageName == _lordRacingRuin);
            string lordName = _lordRacingId != null
                ? (Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _lordRacingId)?.Name?.ToString() ?? "A mage lord")
                : "A mage lord";

            if (def != null)
                SetCooldown(_lordRacingRuin, 30);

            InformationManager.DisplayMessage(new InformationMessage(
                $"{lordName} has returned from {(def?.RuinName ?? "the ruins")}. The opportunity has passed for now.",
                new Color(0.55f, 0.45f, 0.75f)));

            _lordRacingRuin  = null;
            _lordRacingId    = null;
            _lordRacingDaysLeft = 0;
        }

        // ── Weekly guard respawn ───────────────────────────────────────────────
        public static void WeeklyTick()
        {
            foreach (var def in AshenRuinDefs.All)
            {
                int idx = _guardCdKeys.IndexOf(def.VillageName);
                if (idx >= 0 && _guardCdDays[idx] > 0) continue; // still on cooldown
                SpawnGuardsForRuin(def);
            }
        }

        internal static void SpawnGuardsForRuin(RuinDef def)
        {
            try
            {
                // Respect the respawn cooldown. SpawnInitialGuards runs on every
                // OnSessionLaunched (including save loads), so without this gate a
                // fresh ambush party was spawned at every ruin on each reload,
                // stacking parties indefinitely on the campaign map.
                int cdIdx = _guardCdKeys.IndexOf(def.VillageName);
                if (cdIdx >= 0 && _guardCdDays[cdIdx] > 0) return;

                var village = Settlement.All.FirstOrDefault(s =>
                    s != null && s.IsVillage &&
                    string.Equals(s.Name?.ToString()?.Trim(), def.VillageName, StringComparison.OrdinalIgnoreCase));
                if (village == null) return;

                Vec2 pos = village.GetPosition2D;
                int tier = (int)def.Tier;
                int troops = tier switch
                {
                    1 => 12 + _rng.Next(7),
                    2 => 20 + _rng.Next(11),
                    3 => 30 + _rng.Next(16),
                    _ => 45 + _rng.Next(21),
                };
                float minStr = tier >= 3 ? 150f : 0f;
                try { CampaignMapEvents.SpawnAshenAmbushNear(pos, troops, minStr); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // Set respawn cooldown for this ruin
                SetGuardCooldown(def.VillageName, tier >= 3 ? 10 : 7);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Challenge resolution ───────────────────────────────────────────────
        // Called from the menu "Enter" option.
        public static void BeginExploration(RuinDef def, bool isSolo)
        {
            if (def == null) return;
            // Contest intercept
            if (IsContested(def.VillageName))
            {
                ShowContestDialog(def, isSolo);
                return;
            }
            ShowIntro(def, isSolo, 0, false);
        }

        private static void ShowContestDialog(RuinDef def, bool isSolo)
        {
            Hero lord = ContestedBy(def.VillageName);
            string lordName = lord?.Name?.ToString() ?? "a mage lord";

            InformationManager.ShowInquiry(new InquiryData(
                def.RuinName,
                $"{lordName} is already inside. You can hear movement deeper in.",
                true, true, "Confront them", "Wait outside",
                () =>
                {
                    // Confront: relation check
                    int rel = lord != null
                        ? CharacterRelationManager.GetHeroRelation(Hero.MainHero, lord)
                        : -10;
                    bool share = rel >= 0;
                    if (share)
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            def.RuinName,
                            $"{lordName} acknowledges you. You explore together — there is a strange camaraderie in it. The reward is halved, but neither of you walks alone into the dark.",
                            true, false, "Enter together", "",
                            () =>
                            {
                                ClearContest();
                                ShowIntro(def, isSolo, 0, true); // sharedReward = true
                            }, null), true);
                    }
                    else
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            def.RuinName,
                            $"{lordName} turns to face you with cold eyes. The fire between you is not warm. You can fight — or retreat.",
                            true, true, "Stand your ground (race them)", "Retreat",
                            () =>
                            {
                                ClearContest();
                                ShowIntro(def, isSolo, 0, false);
                            },
                            () => { }), true);
                    }
                },
                () => { }), true);
        }

        private static void ClearContest()
        {
            _lordRacingRuin  = null;
            _lordRacingId    = null;
            _lordRacingDaysLeft = 0;
        }

        private static void ShowIntro(RuinDef def, bool isSolo, int roomIdx, bool sharedReward)
        {
            string soloNote = isSolo ? "\n\nYou are alone. Some passages are quieter without an army. Others are harder." : "";
            InformationManager.ShowInquiry(new InquiryData(
                def.RuinName,
                def.EntryLore + soloNote,
                true, true, "Enter", "Turn back",
                () => RunRoom(def, isSolo, roomIdx, sharedReward),
                () => { }), true);
        }

        private static void RunRoom(RuinDef def, bool isSolo, int roomIdx, bool sharedReward)
        {
            if (roomIdx >= def.Challenges.Length)
            {
                // All rooms cleared
                MarkCleared(def.VillageName);
                GrantReward(def.MainReward, sharedReward, def.RuinName, full: true);
                return;
            }

            var challenge = def.Challenges[roomIdx];
            DispatchChallenge(challenge, def, isSolo, roomIdx, sharedReward);
        }

        // After successfully passing a room, advance to next room.
        private static void NextRoom(RuinDef def, bool isSolo, int roomIdx, bool sharedReward)
            => RunRoom(def, isSolo, roomIdx + 1, sharedReward);

        // On retreat, give partial reward if at least one room was passed.
        private static void OnRetreat(RuinDef def, int roomsPassed)
        {
            SetCooldown(def.VillageName, 14);
            if (roomsPassed > 0)
                GrantReward(def.PartialReward, false, def.RuinName, full: false);
            else
                InformationManager.DisplayMessage(new InformationMessage(
                    $"You leave {def.RuinName} — something for another day.",
                    new Color(0.6f, 0.55f, 0.5f)));
        }

        // ── Challenge dispatch ─────────────────────────────────────────────────
        private static void DispatchChallenge(RuinChallenge c, RuinDef def, bool isSolo, int roomIdx, bool sharedReward)
        {
            switch (c.Type)
            {
                case ChallengeType.BloodLock:
                    Ch_BloodLock(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.VisionChamber:
                    Ch_Vision(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.SoulHarvest:
                    Ch_SoulHarvest(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.VoidMaw:
                    Ch_VoidMaw(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.AncientTrap:
                    Ch_AncientTrap(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.SealedMemory:
                    Ch_SealedMemory(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.CollapsingChamber:
                    Ch_CollapsingChamber(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.SpectralGuardian:
                    Ch_SpectralGuardian(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.VoidWhisper:
                    Ch_VoidWhisper(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.RiddleGate:
                    Ch_RiddleGate(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.AshenSentinel:
                    Ch_AshenSentinel(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.SerpentNest:
                    Ch_SerpentNest(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.PoisonedAir:
                    Ch_PoisonedAir(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.CursedRelics:
                    Ch_CursedRelics(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.MirrorGate:
                    Ch_MirrorGate(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.TemporalCrack:
                    Ch_TemporalCrack(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.SleepingGiant:
                    Ch_SleepingGiant(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.NecromanticWard:
                    Ch_NecromanticWard(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.DragonEgg:
                    Ch_DragonEgg(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.AshenFlame:
                    Ch_AshenFlame(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.EmberWraith:
                    Ch_EmberWraith(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.WardstoneGate:
                    Ch_WardstoneGate(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.HollowChoir:
                    Ch_HollowChoir(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.EmberToll:
                    Ch_EmberToll(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.WeightOfAsh:
                    Ch_WeightOfAsh(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.ShiftingHall:
                    Ch_ShiftingHall(c, def, isSolo, roomIdx, sharedReward); break;
                case ChallengeType.TriuneReckoning:
                    Ch_TriuneReckoning(c, def, isSolo, roomIdx, sharedReward); break;
                default:
                    NextRoom(def, isSolo, roomIdx, sharedReward); break;
            }
        }

        // ── Individual challenge implementations ──────────────────────────────

        private static void Ch_BloodLock(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Blood Lock",
                "The door is sealed by a dried ring of dark matter. There is no key — only a price. The fire inside you already knows what it wants.",
                true, true, "Pay the toll (3 days)", "Retreat",
                () => { AgePlayer(3); NextRoom(def, isSolo, ri, sr); },
                () => OnRetreat(def, ri)), true);
        }

        private static void Ch_Vision(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Vision Chamber",
                "The chamber hums. Whatever lived here left an impression — not a ghost exactly, but the shape of one. It has nothing to say and everything to show.",
                true, false, "Bear witness", "",
                () => NextRoom(def, isSolo, ri, sr), null), true);
        }

        private static void Ch_SoulHarvest(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Harvest",
                "Something here is starving. It offers you passage — not freely. It takes 15 days of your fire as a toll. You cannot negotiate. You can only pay.",
                true, false, "Pay (15 days aging)", "",
                () => { AgePlayer(15); NextRoom(def, isSolo, ri, sr); }, null), true);
        }

        private static void Ch_VoidMaw(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Void Maw",
                "A passage that closes the moment you enter it. No retreat from here. The walls press close and cost you something. 6 days of your fire, gone into the dark.",
                true, false, "Pass through (6 days aging)", "",
                () => { AgePlayer(6); NextRoom(def, isSolo, ri, sr); }, null), true);
        }

        private static void Ch_AncientTrap(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            bool pass = _rng.Next(100) < 60;
            if (pass)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Ancient Trap",
                    "You step over something that would have been a trigger a hundred years ago. The mechanism has seized. Lucky.",
                    true, false, "Continue", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Ancient Trap",
                    "The floor gives way just enough. Something catches you — not a killing blow, but the price of carelessness. 5 days older.",
                    true, false, "Continue (5 days aging)", "",
                    () => { AgePlayer(5); NextRoom(def, isSolo, ri, sr); }, null), true);
            }
        }

        private static void Ch_SealedMemory(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Sealed Memory",
                "A room that holds the compressed weight of someone else's last moment. It forces its way in. You will carry 8 whispers of it with you, and you will see everything.",
                true, false, "Receive the memory (+8 whispers)", "",
                () =>
                {
                    MageKnowledge.AddWhispers(8);
                    NextRoom(def, isSolo, ri, sr);
                }, null), true);
        }

        private static void Ch_CollapsingChamber(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            if (isSolo)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Collapsing Chamber",
                    "The ceiling is unhappy. Without troops to brace the supports you must trust your own speed — and pay for it. 8 days aging.",
                    true, true, "Sprint through (8 days aging)", "Retreat",
                    () => { AgePlayer(8); NextRoom(def, isSolo, ri, sr); },
                    () => OnRetreat(def, ri)), true);
                return;
            }
            int troops = MobileParty.MainParty?.MemberRoster?.TotalHealthyCount ?? 0;
            bool pass = _rng.Next(100) < Math.Min(80, troops / 2 + 20);
            if (pass)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Collapsing Chamber",
                    "The ceiling groans and sheds dust. Your people brace the weakest points. It holds — barely.",
                    true, false, "Push on", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
            }
            else
            {
                int lost = Math.Max(3, troops / 6);
                InformationManager.ShowInquiry(new InquiryData(
                    "Collapsing Chamber",
                    $"Part of the ceiling comes down. {lost} men are buried or flee. You can dig or retreat.",
                    true, true, $"Dig through (lose {lost} troops)", "Retreat",
                    () => { LoseTroops(lost); NextRoom(def, isSolo, ri, sr); },
                    () => OnRetreat(def, ri)), true);
            }
        }

        private static void Ch_SpectralGuardian(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            int agingSpent = AgingSystem.LedgerDaysSpent;
            // More experienced casters have harder guardians
            bool tough = agingSpent > 40;
            bool pass = _rng.Next(100) < (tough ? 45 : 65);
            string desc = tough
                ? "The guardian recognises the weight you carry. It makes itself harder. Your own fire, bent back against you."
                : "A remnant, still on duty. It reads your fire and finds it... acceptable. Barely.";
            if (pass)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Spectral Guardian", desc + "\n\nIt lets you pass.",
                    true, false, "Continue", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Spectral Guardian", desc + "\n\nIt does not let you pass without a price. 8 days aging to force through, or retreat.",
                    true, true, "Force through (8 days aging)", "Retreat",
                    () => { AgePlayer(8); NextRoom(def, isSolo, ri, sr); },
                    () => OnRetreat(def, ri)), true);
            }
        }

        private static void Ch_VoidWhisper(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Void Whisper",
                "Something speaks from a crack in the wall. It does not use words. It uses your name. The fire inside you flinches. You can answer it — which costs you something — or resist, which may cost you more.",
                true, true, "Answer (+10 whispers, proceed)", "Resist (roll)",
                () => { MageKnowledge.AddWhispers(10); NextRoom(def, isSolo, ri, sr); },
                () =>
                {
                    bool resist = _rng.Next(100) < 55;
                    if (resist)
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Void Whisper",
                            "The fire holds. The voice retreats. You continue.",
                            true, false, "Continue", "",
                            () => NextRoom(def, isSolo, ri, sr), null), true);
                    }
                    else
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Void Whisper",
                            "The voice finds a crack anyway. 3 days of your fire, taken. But you pass.",
                            true, false, "Continue (3 days aging)", "",
                            () => { AgePlayer(3); NextRoom(def, isSolo, ri, sr); }, null), true);
                    }
                }), true);
        }

        private static void Ch_RiddleGate(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
            => ShowRiddle(def, isSolo, ri, sr);

        private static void ShowRiddle(RuinDef def, bool isSolo, int ri, bool sr)
        {
            var riddles = new[]
            {
                ("I am counted by kings and bled by mages. I have no colour but am spent in every fire. What am I?",
                 new[]{"Days","Gold","Time","Blood"}, 0),
                ("The cold carries it, the fire forgets it, the Ashen hoard it. What is it?",
                 new[]{"Memory","Ash","Shadow","Silence"}, 0),
                ("I am the price of the first spell and the shape of the last one. I grow without planting.",
                 new[]{"Aging","Power","Hunger","Grief"}, 0),
            };
            var (question, answers, correct) = riddles[_rng.Next(riddles.Length)];
            string correctAnswer = answers[correct];

            var options = answers.Select((a, i) =>
                new InquiryElement(i, a, null, true,
                    i == correct ? "This feels true." : "This might be right.")).ToList();
            // Add "Pay to pass" option
            options.Add(new InquiryElement(99, "Burn through it (2 days aging)", null, true, "Skip the puzzle with fire."));

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Riddle Gate",
                question,
                options, true, 1, 1, "Answer", "Retreat",
                chosen =>
                {
                    if (chosen == null || chosen.Count == 0) { OnRetreat(def, ri); return; }
                    int idx = (int)chosen[0].Identifier;
                    if (idx == 99)
                    { AgePlayer(2); NextRoom(def, isSolo, ri, sr); return; }
                    bool right = answers[idx] == correctAnswer;
                    if (right)
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "The Gate Opens", "The lock clicks. The door swings inward on silence.",
                            true, false, "Enter", "",
                            () => NextRoom(def, isSolo, ri, sr), null), true);
                    }
                    else
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Wrong Answer", "4 days aging for the error. You may try once more.",
                            true, true, "Try again", "Retreat",
                            () => { AgePlayer(4); ShowRiddle(def, isSolo, ri, sr); },
                            () => OnRetreat(def, ri)), true);
                    }
                },
                _ => OnRetreat(def, ri),
                "", false), false, true);
        }

        private static void Ch_AshenSentinel(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            if (MageKnowledge.IsAshen)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Ashen Sentinel",
                    "The sentinel turns its hollow gaze on you. Then it steps aside. The cold recognises the cold.",
                    true, false, "Pass through", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
                return;
            }
            bool pass = _rng.Next(100) < 40;
            if (pass)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Ashen Sentinel",
                    "The guardian reads your fire and finds it... insufficient threat. It does not bother. You pass.",
                    true, false, "Continue", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Ashen Sentinel",
                    "The guardian does not step aside for fire-bearers of your kind. You can fight your way through — 10 days aging — or lose 5 troops as a distraction.",
                    true, true, "Spend fire (10 days aging)", "Spend troops (5 men)",
                    () => { AgePlayer(10); NextRoom(def, isSolo, ri, sr); },
                    () =>
                    {
                        if (isSolo || (MobileParty.MainParty?.MemberRoster?.TotalHealthyCount ?? 0) < 5)
                        {
                            InformationManager.ShowInquiry(new InquiryData(
                                "Not Enough Men",
                                "You do not have the men to spend. The fire will have to do. 10 days aging.",
                                true, false, "Pay", "",
                                () => { AgePlayer(10); NextRoom(def, isSolo, ri, sr); }, null), true);
                        }
                        else
                        { LoseTroops(5); NextRoom(def, isSolo, ri, sr); }
                    }), true);
            }
        }

        private static void Ch_SerpentNest(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            int troopCost = 8 + _rng.Next(13);
            int agingCost = isSolo ? 9 : 6;

            InformationManager.ShowInquiry(new InquiryData(
                "Serpent Nest",
                $"The passage is occupied. You can burn them out with your inner fire ({agingCost} days aging) or send men through first ({troopCost} troops lost).",
                true, true, $"Burn them ({agingCost} days aging)", $"Send men ({troopCost} troops)",
                () => { AgePlayer(agingCost); NextRoom(def, isSolo, ri, sr); },
                () =>
                {
                    if (isSolo)
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Serpent Nest",
                            "You are alone. There are no men to send. The fire must serve. 9 days aging.",
                            true, true, "Pay (9 days aging)", "Retreat",
                            () => { AgePlayer(9); NextRoom(def, isSolo, ri, sr); },
                            () => OnRetreat(def, ri)), true);
                    }
                    else
                    { LoseTroops(troopCost); NextRoom(def, isSolo, ri, sr); }
                }), true);
        }

        private static void Ch_PoisonedAir(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            int proficiency = TalentSystem.PurchasedCount;
            bool pass = _rng.Next(100) < Math.Min(75, 30 + proficiency * 4);
            if (pass)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Poisoned Air",
                    "The air is thick with something that should not be breathable. Your fire burns it away before it reaches your lungs.",
                    true, false, "Continue", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Poisoned Air",
                    "The fumes take hold before your fire responds. Your party staggers through — the wound will heal, but some will not walk straight for days.",
                    true, true, "Push on (−15% party health)", "Retreat",
                    () => { ApplyPartyHealthPenalty(0.15f); NextRoom(def, isSolo, ri, sr); },
                    () => OnRetreat(def, ri)), true);
            }
        }

        private static void Ch_CursedRelics(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Cursed Relics",
                "Three objects on a shelf. One hums faintly with warmth. The other two are cold, and eager. You can only take one.",
                new List<InquiryElement>
                {
                    new InquiryElement(0, "The brass compass", null, true, "It points in a direction that does not match north."),
                    new InquiryElement(1, "The sealed jar", null, true, "Something inside shifts when you move it."),
                    new InquiryElement(2, "The folded cloth", null, true, "It is warm. Too warm for this room."),
                },
                true, 1, 1, "Take it", "Leave everything",
                chosen =>
                {
                    if (chosen == null || chosen.Count == 0) { OnRetreat(def, ri); return; }
                    int pick = (int)chosen[0].Identifier;
                    int safe = 2; // the warm cloth is always safe
                    if (pick == safe)
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "The Relic Holds",
                            "The cloth unfolds into something that should not fit inside it. You keep it. The room does not object.",
                            true, false, "Proceed", "",
                            () => NextRoom(def, isSolo, ri, sr), null), true);
                    }
                    else
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Cursed",
                            "Whatever was inside the relic has opinions about being moved. 10 days aging and a headache that lasts.",
                            true, false, "Continue (10 days aging)", "",
                            () => { AgePlayer(10); NextRoom(def, isSolo, ri, sr); }, null), true);
                    }
                },
                _ => OnRetreat(def, ri),
                "", false), false, true);
        }

        private static void Ch_MirrorGate(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            int spells = TalentSystem.PurchasedCount;
            bool pass = _rng.Next(100) < Math.Max(20, 70 - spells * 3);
            if (pass)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Mirror Gate",
                    "Your shadow precedes you through the archway and tries to block you. You are slightly less afraid of yourself than it expected.",
                    true, false, "Step through", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Mirror Gate",
                    "The shadow knows every spell you do. You pay for the impasse: 12 days aging, and then it steps aside.",
                    true, true, "Pay (12 days aging)", "Retreat",
                    () => { AgePlayer(12); NextRoom(def, isSolo, ri, sr); },
                    () => OnRetreat(def, ri)), true);
            }
        }

        private static void Ch_TemporalCrack(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            int roll = _rng.Next(100);
            if (roll < 40) // Gain aging back
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Temporal Crack",
                    "The crack in time runs the wrong way here. You walk through and come out younger. 6 days given back.",
                    true, false, "Continue", "",
                    () => { AgingSystem.RejuvenateHero(Hero.MainHero, 6); NextRoom(def, isSolo, ri, sr); }, null), true);
            }
            else if (roll < 75) // Aging cost
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Temporal Crack",
                    "Time skips. You come out the other side 8 days older. The room looks the same. You do not.",
                    true, false, "Continue (8 days aging)", "",
                    () => { AgePlayer(8); NextRoom(def, isSolo, ri, sr); }, null), true);
            }
            else // Troop wipe in that sector
            {
                int lost = isSolo ? 0 : Math.Max(5, (MobileParty.MainParty?.MemberRoster?.TotalHealthyCount ?? 10) / 5);
                string troopLine = isSolo || lost == 0
                    ? "The room reassembles around you. You are unharmed — there is no one else to lose."
                    : $"Your rearguard does not make it through. {lost} men are simply... not there anymore.";
                InformationManager.ShowInquiry(new InquiryData(
                    "Temporal Crack",
                    troopLine,
                    true, false, "Continue", "",
                    () => { if (!isSolo && lost > 0) LoseTroops(lost); NextRoom(def, isSolo, ri, sr); }, null), true);
            }
        }

        private static void Ch_SleepingGiant(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            if (isSolo)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Sleeping Giant",
                    "Something enormous rests here. Alone, you move like smoke. It does not stir.",
                    true, false, "Slip past", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
                return;
            }
            int troops = MobileParty.MainParty?.MemberRoster?.TotalHealthyCount ?? 0;
            bool pass = _rng.Next(100) < Math.Max(15, 60 - troops / 3);
            if (pass)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Sleeping Giant",
                    "Despite everything, you get your entire column through without a sound. It does not wake.",
                    true, false, "Continue", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
            }
            else
            {
                int lost = Math.Max(10, troops / 4);
                InformationManager.ShowInquiry(new InquiryData(
                    "Sleeping Giant",
                    $"It wakes. It is terrible. The retreat costs {lost} men. You are through, but not all of you.",
                    true, true, $"Fight retreat (lose {lost} troops)", "Full retreat",
                    () => { LoseTroops(lost); NextRoom(def, isSolo, ri, sr); },
                    () => OnRetreat(def, ri)), true);
            }
        }

        private static void Ch_NecromanticWard(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            if (isSolo)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Necromantic Ward",
                    "The ward was built to turn armies against themselves. Alone, it finds nothing to work with. It settles for pressing itself into you instead — 6 whispers, carried out.",
                    true, false, "Continue (+6 whispers)", "",
                    () => { MageKnowledge.AddWhispers(6); NextRoom(def, isSolo, ri, sr); }, null), true);
                return;
            }
            int troops = MobileParty.MainParty?.MemberRoster?.TotalHealthyCount ?? 0;
            bool held = _rng.Next(100) < 55;
            if (held)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Necromantic Ward",
                    "Your men look at each other strangely for a moment. Then the moment passes. The ward finds no purchase.",
                    true, false, "Continue", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
            }
            else
            {
                int lost = Math.Max(5, troops / 8);
                InformationManager.ShowInquiry(new InquiryData(
                    "Necromantic Ward",
                    $"The ward does something to the men at the back. {lost} men turn on each other before you can stop it. The rest push through.",
                    true, false, "Continue (friendly fire)", "",
                    () => { LoseTroops(lost); NextRoom(def, isSolo, ri, sr); }, null), true);
            }
        }

        private static void Ch_DragonEgg(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Dragon's Egg",
                "A black sphere, warm, the size of a man's head. It is not an egg — not exactly — but the fire inside you recognises the fire inside it. You can take it or leave it. Taking it will attract attention.",
                true, true, "Take it (Ashen notice you)", "Leave it",
                () =>
                {
                    MageKnowledge.AddWhispers(12);
                    InformationManager.DisplayMessage(new InformationMessage(
                        "You carry the Egg out. Something cold and distant takes note of your name.",
                        new Color(0.45f, 0.35f, 0.65f)));
                    NextRoom(def, isSolo, ri, sr);
                },
                () => NextRoom(def, isSolo, ri, sr)), true);
        }

        private static void Ch_AshenFlame(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            int agingCost = isSolo ? 10 : 6;
            int troopCost = 10;
            string desc = isSolo
                ? "The Ashen Flame fills the corridor. No troops to shield you. 10 days aging minimum to push through it."
                : "The Ashen Flame fills the corridor. You can shield yourself with troops (10 lost) but the fire will still take 6 days from you.";

            InformationManager.ShowInquiry(new InquiryData(
                "Ashen Flame",
                desc,
                true, true, "Push through", "Retreat",
                () =>
                {
                    AgePlayer(agingCost);
                    if (!isSolo) LoseTroops(troopCost);
                    NextRoom(def, isSolo, ri, sr);
                },
                () => OnRetreat(def, ri)), true);
        }

        private static void Ch_EmberWraith(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            float renown = 0f;
            try { renown = Hero.MainHero?.Clan?.Renown ?? 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            bool pass = _rng.Next(100) < AshenRuinMath.EmberWraithPassChance(renown);
            if (pass)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Ember Wraith",
                    "Something ember-eyed studies you from the dark and decides you are not worth the trouble. It withdraws.",
                    true, false, "Continue", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Ember Wraith",
                    "Your name has reached further than you thought. The wraith wants to test it — 8 days of your fire, or five men to test it instead.",
                    true, true, "Spend fire (8 days aging)", "Spend troops (5 men)",
                    () => { AgePlayer(8); NextRoom(def, isSolo, ri, sr); },
                    () =>
                    {
                        int troops = MobileParty.MainParty?.MemberRoster?.TotalHealthyCount ?? 0;
                        if (isSolo || troops < 5)
                        {
                            InformationManager.ShowInquiry(new InquiryData(
                                "Not Enough Men",
                                "You do not have the men to spend. The fire will have to do. 8 days aging.",
                                true, false, "Pay", "",
                                () => { AgePlayer(8); NextRoom(def, isSolo, ri, sr); }, null), true);
                        }
                        else
                        { LoseTroops(5); NextRoom(def, isSolo, ri, sr); }
                    }), true);
            }
        }

        private static void Ch_WardstoneGate(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            int roguery = 0;
            try { roguery = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            bool pass = _rng.Next(100) < AshenRuinMath.WardstoneGatePassChance(roguery);
            if (pass)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Wardstone Gate",
                    "The ward-carved stones expect a particular kind of trespasser. You read the pattern and step through the gaps meant for exactly your sort.",
                    true, false, "Continue", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Wardstone Gate",
                    "The pattern eludes you. You can force the wardstones apart — 6 days of your fire — or turn back.",
                    true, true, "Force it (6 days aging)", "Retreat",
                    () => { AgePlayer(6); NextRoom(def, isSolo, ri, sr); },
                    () => OnRetreat(def, ri)), true);
            }
        }

        private static void Ch_HollowChoir(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            int tier = MageKnowledge.IsMage ? MageKnowledge.WhisperTier : 0;
            if (tier >= 2)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Hollow Choir",
                    "Voices without mouths sing something that isn't quite a warning. The cold in you hums back at the same pitch. They let you pass, and leave a little more of themselves behind. +3 whispers.",
                    true, false, "Continue", "",
                    () => { MageKnowledge.AddWhispers(3); NextRoom(def, isSolo, ri, sr); }, null), true);
                return;
            }
            bool pass = _rng.Next(100) < AshenRuinMath.HollowChoirPassChance(tier);
            if (pass)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Hollow Choir",
                    "The singing rises around you, searching for a voice to match. It doesn't find one in you, and loses interest.",
                    true, false, "Continue", "",
                    () => NextRoom(def, isSolo, ri, sr), null), true);
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Hollow Choir",
                    "The singing finds a crack in you after all. 6 days of your fire, taken to quiet it — or retreat before it takes more.",
                    true, true, "Push through (6 days aging)", "Retreat",
                    () => { AgePlayer(6); NextRoom(def, isSolo, ri, sr); },
                    () => OnRetreat(def, ri)), true);
            }
        }

        private static void Ch_EmberToll(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            int unspentFp = 0;
            try { unspentFp = Hero.MainHero?.HeroDeveloper?.UnspentFocusPoints ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            bool canPayFp = unspentFp > 0;
            string toll = canPayFp ? "Pay with knowledge (1 focus point)" : "Pay with fire (4 days aging)";
            InformationManager.ShowInquiry(new InquiryData(
                "Ember Toll",
                "The room asks for a piece of what you know, not what you carry. The fire inside you already understands the exchange.",
                true, true, toll, "Retreat",
                () =>
                {
                    if (canPayFp)
                    {
                        try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints -= 1; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                    else AgePlayer(4);
                    NextRoom(def, isSolo, ri, sr);
                },
                () => OnRetreat(def, ri)), true);
        }

        private static void Ch_WeightOfAsh(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            int cost = AshenRuinMath.WeightOfAshCost(def.Tier);
            InformationManager.ShowInquiry(new InquiryData(
                "Weight of Ash",
                $"The scale in this room does not move for gold or blood. It moves for time. {cost} days of your fire, and no less, or the passage does not open.",
                true, false, $"Pay ({cost} days aging)", "",
                () => { AgePlayer(cost); NextRoom(def, isSolo, ri, sr); }, null), true);
        }

        private static void Ch_ShiftingHall(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            var outcome = AshenRuinMath.ResolveShiftingHall(_rng.Next(100));
            switch (outcome)
            {
                case ShiftingHallOutcome.RenownGain:
                    InformationManager.ShowInquiry(new InquiryData(
                        "Shifting Hall",
                        "The hall rearranges itself around you and, for reasons it does not share, remembers you kindly. Word of the passage will travel. (+15 renown)",
                        true, false, "Continue", "",
                        () =>
                        {
                            try { ClanRenown.Gain(Hero.MainHero.Clan, 15f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            NextRoom(def, isSolo, ri, sr);
                        }, null), true);
                    break;

                case ShiftingHallOutcome.Desertion:
                    int troops = MobileParty.MainParty?.MemberRoster?.TotalHealthyCount ?? 0;
                    int lost = isSolo ? 0 : AshenRuinMath.ShiftingHallDesertionLoss(troops);
                    string line = isSolo || lost == 0
                        ? "The hall's doubt has nothing to work with — you are alone, and unshaken."
                        : $"The hall plants a doubt at the back of the column. {lost} men slip away in the confusion and do not come back.";
                    InformationManager.ShowInquiry(new InquiryData(
                        "Shifting Hall", line,
                        true, false, "Continue", "",
                        () => { if (!isSolo && lost > 0) LoseTroops(lost); NextRoom(def, isSolo, ri, sr); }, null), true);
                    break;

                default: // WhisperGain
                    InformationManager.ShowInquiry(new InquiryData(
                        "Shifting Hall",
                        "The hall leaves something behind in you on the way through — not a wound, exactly. +5 whispers.",
                        true, false, "Continue", "",
                        () => { MageKnowledge.AddWhispers(5); NextRoom(def, isSolo, ri, sr); }, null), true);
                    break;
            }
        }

        private static void Ch_TriuneReckoning(RuinChallenge c, RuinDef def, bool isSolo, int ri, bool sr)
        {
            bool isAshen = MageKnowledge.IsAshen;
            bool natureAttuned = false, hasGrace = false;
            try { natureAttuned = NatureKnowledge.IsAttuned; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { hasGrace = MiracleInventory.HasGrace; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            if (isAshen)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Triune Reckoning",
                    "The room asks which fire you carry. The cold in you answers before you do. It recognises itself and steps aside. +4 whispers.",
                    true, false, "Continue", "",
                    () => { MageKnowledge.AddWhispers(4); NextRoom(def, isSolo, ri, sr); }, null), true);
            }
            else if (hasGrace)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Triune Reckoning",
                    "The room asks which fire you carry. Something in you answers with conviction rather than heat. That is enough. Word of it spreads. (+10 renown)",
                    true, false, "Continue", "",
                    () =>
                    {
                        try { ClanRenown.Gain(Hero.MainHero.Clan, 10f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        NextRoom(def, isSolo, ri, sr);
                    }, null), true);
            }
            else if (natureAttuned)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Triune Reckoning",
                    "The room asks which fire you carry. The living fire in you is quieter than the others, and older. The room eases, briefly, and gives back 3 days.",
                    true, false, "Continue", "",
                    () => { AgingSystem.RejuvenateHero(Hero.MainHero, 3); NextRoom(def, isSolo, ri, sr); }, null), true);
            }
            else
            {
                bool pass = _rng.Next(100) < AshenRuinMath.TriuneReckoningFallbackPassChance(TalentSystem.PurchasedCount);
                if (pass)
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        "Triune Reckoning",
                        "The room asks which fire you carry. You aren't entirely sure yourself — but the answer, whatever it was, is accepted.",
                        true, false, "Continue", "",
                        () => NextRoom(def, isSolo, ri, sr), null), true);
                }
                else
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        "Triune Reckoning",
                        "The room asks which fire you carry, and finds the answer wanting. 7 days of your fire settles the matter, or retreat.",
                        true, true, "Pay (7 days aging)", "Retreat",
                        () => { AgePlayer(7); NextRoom(def, isSolo, ri, sr); },
                        () => OnRetreat(def, ri)), true);
                }
            }
        }

        // ── Reward dispatch ───────────────────────────────────────────────────
        private static void GrantReward(RuinReward reward, bool sharedReward, string ruinName, bool full)
        {
            if (reward == null) return;
            float split = sharedReward ? 0.5f : 1f;

            string header = full
                ? $"You emerge from {ruinName} carrying something the darkness did not want you to have."
                : $"You retreat from {ruinName} with what you could carry.";

            switch (reward.Type)
            {
                case RewardType.GrimoireFragment:
                    GrantGrimoireFragment(header); break;

                case RewardType.AgingReclaim:
                    int days = Math.Max(1, (int)(reward.Points * split));
                    AgingSystem.RejuvenateHero(Hero.MainHero, days);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{header} The fire gives back {days} day{(days!=1?"s":"")}.",
                        new Color(0.9f, 0.6f, 0.3f)));
                    break;

                case RewardType.WhisperPurge:
                    int purge = Math.Max(1, (int)(reward.Points * split));
                    MageKnowledge.RemoveWhispers(purge);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{header} The cold recedes — {purge} whisper{(purge!=1?"s":"")} quieted.",
                        new Color(0.7f, 0.7f, 0.9f)));
                    break;

                case RewardType.WhisperBrand:
                    int brand = (int)(reward.Points * split);
                    MageKnowledge.AddWhispers(brand);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{header} The cold follows you out — {brand} whisper{(brand!=1?"s":"")} deeper.",
                        new Color(0.45f, 0.35f, 0.65f)));
                    break;

                case RewardType.FocusPoints:
                    int fp = Math.Max(1, (int)(reward.Points * split));
                    try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += fp; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{header} The knowledge crystallises into {fp} focus point{(fp!=1?"s":"")}.",
                        new Color(0.7f, 0.9f, 0.7f)));
                    break;

                case RewardType.RenownBurst:
                    float renown = reward.Points * split;
                    try { ClanRenown.Gain(Hero.MainHero.Clan, renown); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{header} Word of this spreads. (+{(int)renown} renown)",
                        new Color(0.9f, 0.78f, 0.25f)));
                    break;

                case RewardType.LoreVision:
                    InformationManager.ShowInquiry(new InquiryData(
                        ruinName, reward.LoreText ?? header,
                        true, false, "Leave", "", () => { }, null), true);
                    break;

                case RewardType.DragonArtifact:
                    _eyeFound = true;
                    InformationManager.ShowInquiry(new InquiryData(
                        "The Eye of Aenos",
                        "A sphere of obsidian that never cools. The last thing the First Drake saw before the Ashen sealed it. You hold it and the fire inside you pulls toward it like a compass finding north.\n\nSomething about the old mage's request makes a different kind of sense now.",
                        true, false, "Take it", "",
                        () => { }, null), true);
                    break;

                case RewardType.AshenCrownFragment:
                    _crownFragments++;
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{header} A piece of something older than any crown. Fragment {_crownFragments}/3.",
                        new Color(0.6f, 0.45f, 0.8f)));
                    if (_crownFragments >= 3)
                    {
                        try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += AshenCrownFpBonus; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The three fragments align. Something clicks in the fire. +3 focus points.",
                            new Color(0.9f, 0.6f, 0.9f)));
                    }
                    break;

                case RewardType.VoidCrystal:
                    InformationManager.ShowInquiry(new InquiryData(
                        "Void Crystal",
                        "A dark glass shard, cold even when held. It hums at the same frequency as your inner fire. You could sell it — 5000 denars, to the right buyer — or let it dissolve into you, which costs the fire nothing and gives back 20 days.",
                        true, true, "Dissolve it (20 days reclaimed)", "Sell it (5000 denars)",
                        () => AgingSystem.RejuvenateHero(Hero.MainHero, 20),
                        () => { try { Hero.MainHero.ChangeHeroGold(5000); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }), true);
                    break;

                case RewardType.AncientGrimoire:
                    GrantAllGrimoireFragments(header); break;

                case RewardType.MagicCrystal:
                    GrantMagicCrystal(header, Math.Max(1, (int)(reward.Points * split))); break;

                case RewardType.GoldCache:
                    int gold = Math.Max(50, (int)(reward.Points * split));
                    try { Hero.MainHero.ChangeHeroGold(gold); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{header} A cache of coin, still good — {gold} denars.",
                        new Color(0.9f, 0.78f, 0.25f)));
                    break;

                case RewardType.SkillTome:
                    GrantSkillTome(header, Math.Max(10f, reward.Points * split)); break;

                case RewardType.EmberBoon:
                    GrantEmberBoon(header, Math.Max(1, (int)(reward.Points * split))); break;
            }
        }

        private static readonly SkillObject[] _skillTomePool =
        {
            DefaultSkills.Roguery, DefaultSkills.Charm, DefaultSkills.Leadership,
            DefaultSkills.Medicine, DefaultSkills.Steward,
        };

        private static void GrantSkillTome(string header, float xp)
        {
            var skill = _skillTomePool[_rng.Next(_skillTomePool.Length)];
            try { Hero.MainHero?.HeroDeveloper?.AddSkillXp(skill, xp); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            InformationManager.DisplayMessage(new InformationMessage(
                $"{header} Old knowledge settles into a skill you already had — {(int)xp} {skill.Name} experience.",
                new Color(0.7f, 0.9f, 0.7f)));
        }

        // Reacts to the caster's own path — the ruins reward conviction differently
        // depending on what kind of fire the player actually carries.
        private static void GrantEmberBoon(string header, int points)
        {
            if (MageKnowledge.IsAshen)
            {
                MageKnowledge.RemoveWhispers(points);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{header} The cold recedes a little further than it should — {points} whisper{(points!=1?"s":"")} quieted.",
                    new Color(0.7f, 0.7f, 0.9f)));
                return;
            }
            bool hasGrace = false;
            try { hasGrace = MiracleInventory.HasGrace; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (hasGrace)
            {
                int added = MiracleInventory.AddGrace(points);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{header} Conviction answers conviction — {added} Grace.",
                    new Color(0.95f, 0.85f, 0.6f)));
                return;
            }
            int fp = Math.Max(1, points / 2);
            try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += fp; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            InformationManager.DisplayMessage(new InformationMessage(
                $"{header} Nothing claims the offering, so the fire simply sharpens. +{fp} focus point{(fp!=1?"s":"")}.",
                new Color(0.7f, 0.9f, 0.7f)));
        }

        private static void GrantMagicCrystal(string header, int count)
        {
            var defs = CrystalCatalog.All;
            string lastName = null;
            try
            {
                var roster = MobileParty.MainParty?.ItemRoster;
                for (int i = 0; i < count; i++)
                {
                    var def  = defs[_rng.Next(defs.Count)];
                    var item = TaleWorlds.ObjectSystem.MBObjectManager.Instance?.GetObject<ItemObject>(def.ItemId);
                    if (item != null && roster != null) roster.AddToCounts(item, 1);
                    lastName = def.Name;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            string body = count > 1
                ? $"{count} crystals rest among the ash, their lattices somehow unbroken."
                : $"A {lastName} rests among the ash, its lattice somehow unbroken.";
            InformationManager.DisplayMessage(new InformationMessage($"{header} {body}", new Color(0.75f, 0.55f, 0.85f)));
        }

        private static void GrantGrimoireFragment(string header)
        {
            var lostForms = new[] { TalentId.LostBlast, TalentId.LostMissile, TalentId.LostBarrier, TalentId.LostBurst };
            var available = lostForms.Where(id => !TalentSystem.Has(id)).ToArray();
            if (available.Length > 0)
            {
                TalentSystem.GrantFree(available[_rng.Next(available.Length)], Hero.MainHero);
                InformationManager.DisplayMessage(new InformationMessage(header, new Color(0.9f, 0.7f, 0.3f)));
            }
            else
            {
                try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 2; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{header} The knowledge was already yours — but the fire sharpens anyway. +2 focus points.",
                    new Color(0.7f, 0.9f, 0.7f)));
            }
        }

        private static void GrantAllGrimoireFragments(string header)
        {
            var lostForms = new[] { TalentId.LostBlast, TalentId.LostMissile, TalentId.LostBarrier, TalentId.LostBurst };
            int granted = 0;
            foreach (var id in lostForms)
                if (TalentSystem.GrantFree(id, Hero.MainHero)) granted++;
            if (granted > 0)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{header} {granted} lost form{(granted!=1?"s":"")} recovered from the dark.",
                    new Color(0.9f, 0.7f, 0.3f)));
            else
            {
                try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 4; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{header} Every lost form was already yours. The grimoire gives back 4 focus points instead.",
                    new Color(0.7f, 0.9f, 0.7f)));
            }
        }

        // ── Cooldown tracking ──────────────────────────────────────────────────
        private static void MarkCleared(string vname)
        {
            _cleared.Add(vname);
            SetCooldown(vname, RevisitCooldownDays);
        }

        private static void SetCooldown(string vname, int days)
        {
            int idx = _cdKeys.IndexOf(vname);
            if (idx >= 0) { _cdDays[idx] = days; return; }
            _cdKeys.Add(vname); _cdDays.Add(days);
        }

        private static void SetGuardCooldown(string vname, int days)
        {
            int idx = _guardCdKeys.IndexOf(vname);
            if (idx >= 0) { _guardCdDays[idx] = days; return; }
            _guardCdKeys.Add(vname); _guardCdDays.Add(days);
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static void AgePlayer(int days)
        {
            try { AgingSystem.AgeHero(Hero.MainHero, days); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void LoseTroops(int count)
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                int remaining = count;
                foreach (var entry in roster.GetTroopRoster().ToList())
                {
                    if (remaining <= 0) break;
                    if (entry.Character == null || entry.Character.IsHero) continue;
                    int healthy = entry.Number - entry.WoundedNumber;
                    if (healthy <= 0) continue;
                    int kill = Math.Min(remaining, healthy);
                    roster.AddToCounts(entry.Character, -kill);
                    remaining -= kill;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ApplyPartyHealthPenalty(float fraction)
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                foreach (var entry in roster.GetTroopRoster().ToList())
                {
                    if (entry.Character == null || entry.Character.IsHero) continue;
                    int healthy = entry.Number - entry.WoundedNumber;
                    int wound = Math.Max(1, (int)(healthy * fraction));
                    if (wound > 0) try { roster.AddToCounts(entry.Character, 0, false, wound); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Persistence ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            try
            {
                var clearedList = _cleared.ToList();
                var cdKeys      = _cdKeys.ToList();
                var cdDays      = _cdDays.ToList();
                var gCdKeys     = _guardCdKeys.ToList();
                var gCdDays     = _guardCdDays.ToList();

                store.SyncData("AR_Cleared",  ref clearedList);
                store.SyncData("AR_CdKeys",   ref cdKeys);
                store.SyncData("AR_CdDays",   ref cdDays);
                store.SyncData("AR_EyeFound", ref _eyeFound);
                store.SyncData("AR_Crown",    ref _crownFragments);
                store.SyncData("AR_LordRuin", ref _lordRacingRuin);
                store.SyncData("AR_LordId",   ref _lordRacingId);
                store.SyncData("AR_LordDays", ref _lordRacingDaysLeft);
                store.SyncData("AR_GCdKeys",  ref gCdKeys);
                store.SyncData("AR_GCdDays",  ref gCdDays);

                if (clearedList != null)
                {
                    _cleared.Clear();
                    foreach (var s in clearedList) _cleared.Add(s);
                }
                if (cdKeys != null && cdDays != null && cdKeys.Count == cdDays.Count)
                {
                    _cdKeys.Clear(); _cdDays.Clear();
                    for (int i = 0; i < cdKeys.Count; i++) { _cdKeys.Add(cdKeys[i]); _cdDays.Add(cdDays[i]); }
                }
                if (gCdKeys != null && gCdDays != null && gCdKeys.Count == gCdDays.Count)
                {
                    _guardCdKeys.Clear(); _guardCdDays.Clear();
                    for (int i = 0; i < gCdKeys.Count; i++) { _guardCdKeys.Add(gCdKeys[i]); _guardCdDays.Add(gCdDays[i]); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void ResetForNewGame()
        {
            _cleared.Clear();
            _cdKeys.Clear();
            _cdDays.Clear();
            _guardCdKeys.Clear();
            _guardCdDays.Clear();
            _eyeFound        = false;
            _crownFragments  = 0;
            _lordRacingRuin  = null;
            _lordRacingId    = null;
            _lordRacingDaysLeft = 0;
        }
    }
}
