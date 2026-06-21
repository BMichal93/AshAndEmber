// =============================================================================
// LIFE & DEATH MAGIC — MageKnowledge.cs
// Tracks whether the player carries the gift, manages the grimoire UI,
// and provides the talent learning menu.
// ColourKnowledge is a legacy alias kept for backward-compatible call sites.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static partial class MageKnowledge
    {
        private static bool   _isMage            = false;
        private static bool   _isAshen          = false;
        private static bool   _isKnownMage      = false;

        // Deferred map-layer dialog queue. Historically a single Action slot:
        // when two systems queued a popup the same day, one was silently lost —
        // including main-quest beats. The property keeps the old field contract
        // (read = peek, assign = enqueue, assign null = pop) so the ~90 existing
        // call sites work unchanged, but unguarded writers now queue behind the
        // pending dialog instead of overwriting it. The flush drains one per tick.
        private static readonly Queue<Action> _inquiryQueue = new Queue<Action>();
        internal static Action _deferredInquiry
        {
            get => _inquiryQueue.Count > 0 ? _inquiryQueue.Peek() : null;
            set
            {
                if (value == null) { if (_inquiryQueue.Count > 0) _inquiryQueue.Dequeue(); }
                else _inquiryQueue.Enqueue(value);
            }
        }
        private static readonly HashSet<string> _giftedChildIds = new HashSet<string>();
        private static readonly Random _rng = new Random();

        private static int  _dreamCooldown = 0;
        private static int  _dreamLastIdx  = -1;

        public static bool IsKnownMage => _isKnownMage;

        public static void BecomeKnown()
        {
            if (_isKnownMage || !_isMage) return;
            _isKnownMage = true;
            _deferredInquiry = ShowKnownMageEvent;
        }

        // ── Whisper System ────────────────────────────────────────────────────
        // Tracks how deeply the cold has seeped into the player's fire.
        // Incremented by dark acts; decremented slowly by virtuous ones.
        // At 100+ the Cold Calls Your Name.
        private static int _whisperCount        = 0;
        private static int _coldCallCountdown   = 0;  // 0 = not pending
        private static int _daysSinceWhisperGain = 0; // quiet conduct lets the cold lose interest
        private static int _lastAmbientIdx      = -1;

        public static int WhisperCount => _whisperCount;

        // Whisper tier: 0 = quiet, 1 = 25+ (the cold has noticed), 2 = 50+
        // (the cold favours you), 3 = 75+ (the cold is close). Tiers pull both
        // ways: altar rites accelerate, sanctuary rites resist you.
        public static int WhisperTier =>
            _whisperCount >= 75 ? 3 : _whisperCount >= 50 ? 2 : _whisperCount >= 25 ? 1 : 0;

        public static void AddWhispers(int n)
        {
            if (n <= 0 || !_isMage) return;
            int tierBefore = WhisperTier;
            _whisperCount += n;
            _daysSinceWhisperGain = 0;
            if (_whisperCount >= 100 && _coldCallCountdown == 0)
                _coldCallCountdown = 7; // fires in 7 days
            if (WhisperTier > tierBefore)
                try { AnnounceWhisperTier(WhisperTier); } catch { }
        }

        public static void RemoveWhispers(int n)
        {
            _whisperCount = Math.Max(0, _whisperCount - n);
        }

        private static void AnnounceWhisperTier(int tier)
        {
            string msg = tier switch
            {
                1 => "Something at the edge of your fire has begun to listen. (The cold has noticed you.)",
                2 => "The whispers no longer wait for the dark. The grey altars will open faster for you now — and the sanctuary flame leans away. (The cold favours you.)",
                3 => "You catch yourself answering before they speak. The sanctuary flame gutters when you kneel. (The cold is very close.)",
                _ => null,
            };
            if (msg != null)
                InformationManager.DisplayMessage(new InformationMessage(msg, new Color(0.45f, 0.45f, 0.65f)));
        }

        private static readonly string[] _ambientWhispers =
        {
            "A voice in the wind says your name the way an old friend would. There is no one there.",
            "The campfire bends north for a moment. No wind blows.",
            "In the morning frost you find one set of footprints circling your tent. They end mid-stride.",
            "You wake with ash on your fingertips. Your fire burned clean last night.",
            "Someone in the column is humming a tune you have only heard in dreams. When you turn, the humming stops.",
        };

        public static void DailyWhisperTick()
        {
            if (!_isMage) return;

            if (_coldCallCountdown > 0)
            {
                _coldCallCountdown--;
                if (_coldCallCountdown == 0)
                    _deferredInquiry = ShowColdCallsEvent; // queued — never lost to a busy day
            }

            // Ambient flavour: once the cold has noticed (25+), it occasionally
            // speaks — rarely (tier/20 per day), never the same line twice in a
            // row, and one whisper in three carries something true: the bearing
            // of the nearest Ashen warband.
            try
            {
                if (!_isAshen && WhisperTier >= 1 && _rng.Next(20) < WhisperTier)
                {
                    string line = null;
                    if (_rng.Next(3) == 0) line = UsefulWhisper();
                    if (line == null)
                    {
                        int idx;
                        do { idx = _rng.Next(_ambientWhispers.Length); } while (idx == _lastAmbientIdx);
                        _lastAmbientIdx = idx;
                        line = _ambientWhispers[idx];
                    }
                    InformationManager.DisplayMessage(new InformationMessage(
                        line, new Color(0.45f, 0.45f, 0.65f)));
                }
            }
            catch { }

            // Passive decay: honourable, merciful players shed whispers slowly
            try
            {
                if (_whisperCount > 0 && Hero.MainHero != null)
                {
                    int mercy  = Hero.MainHero.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Mercy);
                    int honor  = Hero.MainHero.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Honor);
                    if (mercy + honor >= 2 && _rng.Next(7) == 0)
                        _whisperCount = Math.Max(0, _whisperCount - 1);
                }
            }
            catch { }

            // Possession strain heals with time.
            if (_possessionStrainDays > 0) _possessionStrainDays--;

            // Quiet-conduct decay: 10 clean days and the cold starts losing
            // interest — roughly 1 whisper every 3 days regardless of traits.
            // Whispers reflect recent conduct, not a permanent stain.
            try
            {
                _daysSinceWhisperGain++;
                if (_whisperCount > 0 && _daysSinceWhisperGain >= 10 && _rng.Next(3) == 0)
                    _whisperCount = Math.Max(0, _whisperCount - 1);
            }
            catch { }
        }

        // A whisper that is also intelligence: the compass bearing of the
        // nearest Ashen lord's warband. Returns null if none is in the field.
        private static string UsefulWhisper()
        {
            try
            {
                if (MobileParty.MainParty == null) return null;
                Vec2 pos = MobileParty.MainParty.GetPosition2D;
                Hero nearest = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && ColourLordRegistry.IsAshenLord(h)
                             && h.PartyBelongedTo != null)
                    .OrderBy(h => (h.PartyBelongedTo.GetPosition2D - pos).Length)
                    .FirstOrDefault();
                if (nearest?.PartyBelongedTo == null) return null;
                Vec2 d = nearest.PartyBelongedTo.GetPosition2D - pos;
                string dir = Math.Abs(d.y) > Math.Abs(d.x)
                    ? (d.y > 0 ? "north" : "south")
                    : (d.x > 0 ? "east" : "west");
                return $"The whisper is almost kind tonight. It says one of the cold ones rides to the {dir} of you. It does not say why it tells you.";
            }
            catch { return null; }
        }

        public static void DailyDreamTick()
        {
            if (!_isMage) return;
            if (_dreamCooldown > 0) { _dreamCooldown--; return; }
            if (_rng.Next(100) >= 2) return;
            _dreamCooldown = 7;
            _deferredInquiry = ShowDreamEvent;
        }

        public static bool IsMage         => _isMage;
        public static bool IsAshen         => _isAshen;
        // Backward-compat shims used by old call sites
        public static bool HasAnySchool   => _isMage;
        public static IEnumerable<ColorSchool> AllSchools => System.Array.Empty<ColorSchool>();
        public static bool HasSchool(ColorSchool s) => false;
        public static int  GetMadnessOrderChance() => 0;
        public static bool ReducePurpleFertility() => false;
        public static float PurpleFertilityLevel   => 1f;

        public static void SetMage(bool value)   { _isMage = value; }
        public static void SetAshen(bool value) { _isAshen = value; }

        public static void ResetForNewGame()
        {
            _isMage           = false;
            _isAshen          = false;
            _isKnownMage      = false;
            _dreamCooldown    = 0;
            _dreamLastIdx     = -1;
            _inquiryQueue.Clear();
            _whisperCount     = 0;
            _coldCallCountdown = 0;
            _daysSinceWhisperGain = 0;
            _lastAmbientIdx   = -1;
            _possessionStrainDays = 0;
            _giftedChildIds.Clear();
            TalentSystem.ResetForNewGame();
            ColourLordRegistry.ResetForNewGame();
            AshenCitySystem.ResetForNewGame();
            AgingSystem.ResetForNewGame();
            TempleCovenant.ResetForNewGame();
            DarkGiftSystem.ResetForNewGame();
        }

        public static bool IsChildGifted(string id) => _giftedChildIds.Contains(id);
        public static void AddGiftedChild(string id) => _giftedChildIds.Add(id);

        public static void FlushDeferredInquiry()
        {
            Action pending = _deferredInquiry;
            _deferredInquiry = null;
            if (pending == null) return;
            if (Campaign.Current != null)
                Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            pending.Invoke();
        }

        // Legacy no-op kept for CampaignBehavior references
        public static void AddSchool(ColorSchool s) { }
        public static void ClearAllSchools() { }
        public static void RecordCast(ColorSchool s) { }

        // Working the fire is exertion of body and will alike — each successful
        // player cast grants a little Athletics or Leadership XP (chosen at random).
        private const float CastSkillXp = 10f;
        public static void RewardCastSkill()
        {
            try
            {
                var h = Hero.MainHero;
                if (h?.HeroDeveloper == null) return;
                var skill = _rng.Next(2) == 0 ? DefaultSkills.Athletics : DefaultSkills.Leadership;
                h.HeroDeveloper.AddSkillXp(skill, CastSkillXp);
            }
            catch { }
        }
    }

    // Legacy alias — keeps old call-sites compiling without breaking changes
    public static class ColourKnowledge
    {
        public static bool HasAnySchool   => MageKnowledge.IsMage;
        public static bool HasSchool(ColorSchool s) => false;
        public static IEnumerable<ColorSchool> AllSchools => System.Array.Empty<ColorSchool>();
        public static int  GetMadnessOrderChance() => 0;
        public static bool ReducePurpleFertility() => false;
        public static float PurpleFertilityLevel   => 1f;
        public static void AddSchool(ColorSchool s) { }
        public static void ClearAllSchools() { }
        public static void RecordCast(ColorSchool s) { }
        public static bool IsChildGifted(string id) => MageKnowledge.IsChildGifted(id);
        public static void AddGiftedChild(string id) => MageKnowledge.AddGiftedChild(id);
        public static void FlushDeferredInquiry()    => MageKnowledge.FlushDeferredInquiry();
        public static void ResetForNewGame()         => MageKnowledge.ResetForNewGame();
        public static void ShowGrimoire(bool inMission, bool usingController)
            => MageKnowledge.ShowGrimoire(inMission, usingController);
        public static void ShowCampaignCastMenu()    => MageKnowledge.ShowCampaignCastMenu();
        public static void Save(IDataStore store)    => MageKnowledge.Save(store);
    }
}
