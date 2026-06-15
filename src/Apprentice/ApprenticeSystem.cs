// =============================================================================
// ASH AND EMBER — Apprentice/ApprenticeSystem.cs
// A mage player can discover a young noble with latent talent during
// peacetime. Mentoring takes 14–30 days; the rival shadow may corrupt
// the apprentice mid-training.
//
// Trigger : 1% per village entry (independent of normal encounter gate)
// Gate    : IsMage, no current apprentice, total < 3 per campaign, 180-day
//           search cooldown between completions
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public static class ApprenticeSystem
    {
        // ── Tuning ────────────────────────────────────────────────────────────
        public const float  TriggerChance        = 0.01f;  // 1% per village entry
        public const int    MinDaysBetweenSearch = 180;    // search cooldown after completion
        public const int    MaxApprenticesEver   = 3;      // hard cap per campaign
        public const int    CorruptionChancePct  = 2;      // % daily chance rival corrupts

        // ── State ─────────────────────────────────────────────────────────────
        private static string _apprenticeId        = null;  // AP_Id
        private static int    _trainingDaysLeft    = 0;     // AP_DaysLeft
        private static int    _trainingProgress    = 0;     // AP_Progress (0-100)
        private static bool   _rivalCorrupted      = false; // AP_Corrupted
        private static int    _searchCooldown      = 0;     // AP_SearchCD
        private static int    _totalEver           = 0;     // AP_Total

        private static readonly Random _rng = new Random();

        // ── Public queries ─────────────────────────────────────────────────────
        public static bool HasActiveApprentice => _apprenticeId != null;
        public static bool CanSearch =>
            _apprenticeId == null &&
            _searchCooldown == 0 &&
            _totalEver < MaxApprenticesEver;

        // ── Discovery event ───────────────────────────────────────────────────
        public static void ShowDiscovery(Settlement s)
        {
            if (!CanSearch) return;

            // Pick a random young noble from the settlement or surroundings
            Hero candidate = FindCandidate(s);
            string candidateName = candidate?.Name?.ToString() ?? "a young nobleman";
            string culture = s.Culture?.StringId ?? "";
            string flavor = culture switch
            {
                "battania" => "A young woman stands at the edge of the training ground, not watching the fighters — watching the fire in the torches.",
                "sturgia"  => "A pale-haired youth lingers at the harbour gate, staring south at something you cannot see.",
                "aserai"   => "A boy is drawing patterns in the sand with a stick. Not childish patterns. Precise ones.",
                "khuzait"  => "A rider dismounts at the edge of camp and stares at your banner with eyes that are doing more than looking.",
                _          => "A young person near the market square freezes when you pass — not from fear. From recognition.",
            };

            InformationManager.ShowInquiry(new InquiryData(
                "A Latent Fire",
                $"{flavor}\n\n{candidateName} carries the gift. Untrained, unguided — dangerous to themselves and others. You could take them on. The training would take weeks and cost you nothing but time. The risks are yours to carry.",
                true, true, "Take them on", "Not now",
                () => BeginTraining(candidate, candidateName),
                () => { }), true);
        }

        private static Hero FindCandidate(Settlement s)
        {
            try
            {
                // Look for a young noble not already a mage and not the player
                return Hero.AllAliveHeroes
                    .Where(h => h != Hero.MainHero && h.IsAlive
                             && !ColourLordRegistry.IsColourLord(h)
                             && h.Age is > 16 and < 30
                             && h.Clan != null)
                    .OrderBy(_ => _rng.Next())
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        private static void BeginTraining(Hero candidate, string name)
        {
            _apprenticeId      = candidate?.StringId ?? ("anon_" + _rng.Next(99999));
            _trainingDaysLeft  = 14 + _rng.Next(17); // 14–30 days
            _trainingProgress  = 0;
            _rivalCorrupted    = false;
            _totalEver++;
            _searchCooldown    = MinDaysBetweenSearch;

            InformationManager.DisplayMessage(new InformationMessage(
                $"You begin training {name}. It will take {_trainingDaysLeft} day(s). Keep them close.",
                new Color(0.7f, 0.9f, 0.5f)));
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        public static void DailyTick()
        {
            if (_searchCooldown > 0) _searchCooldown--;

            if (_apprenticeId == null) return;

            // Progress gain every 3 days
            _trainingDaysLeft--;
            if (_trainingDaysLeft % 3 == 0)
                _trainingProgress += 5 + _rng.Next(11); // 5–15 pts per 3-day period

            // Rival Shadow corruption check
            if (!_rivalCorrupted && _rng.Next(100) < CorruptionChancePct)
                ShowCorruptionEvent();

            if (_trainingDaysLeft <= 0)
                Graduate();
        }

        private static void ShowCorruptionEvent()
        {
            _rivalCorrupted = true; // tentatively mark; player can clear by dismissing

            string apprenticeName = GetApprenticeName();
            if (MageKnowledge._deferredInquiry != null) return; // don't clobber

            MageKnowledge._deferredInquiry = () =>
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "A Shadow on the Flame",
                    $"{apprenticeName}'s fire has a new quality to it — grey at the edges. Something has been speaking to them in the nights, and they have been listening. You can push through the training regardless, or dismiss them before the corruption runs deeper.",
                    true, true, "Continue training (they may emerge Ashen)", "Dismiss them (no reward, training ends)",
                    () =>
                    {
                        // Stay corrupted; graduation will make them Ashen
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"You continue with {apprenticeName}. The cold watches with interest.",
                            new Color(0.45f, 0.4f, 0.65f)));
                    },
                    () =>
                    {
                        // Dismiss — no cap penalty, but 90-day cooldown only
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"You send {apprenticeName} away before the corruption sets. The fire inside them is their own problem now.",
                            new Color(0.6f, 0.55f, 0.5f)));
                        _apprenticeId      = null;
                        _trainingDaysLeft  = 0;
                        _trainingProgress  = 0;
                        _rivalCorrupted    = false;
                        _searchCooldown    = 90;
                        _totalEver         = Math.Max(0, _totalEver - 1); // doesn't count toward cap
                    }), true);
            };
        }

        // ── Graduation ────────────────────────────────────────────────────────
        private static void Graduate()
        {
            string name = GetApprenticeName();
            Hero apprentice = FindApprenticeHero();
            int progress = Math.Min(100, _trainingProgress);

            if (progress >= 70 && !_rivalCorrupted)
            {
                // Full success: companion becomes a mage
                if (apprentice != null)
                    MakeCompanionMage(apprentice, name, ashen: false);
                else
                    ShowGraduationMessage(name, "fire", full: true);
            }
            else if (progress >= 70 && _rivalCorrupted)
            {
                // Corrupted success: Ashen mage
                if (apprentice != null)
                    MakeCompanionMage(apprentice, name, ashen: true);
                else
                    ShowGraduationMessage(name, "cold fire", full: true);
            }
            else if (progress >= 40)
            {
                // Partial: skill buff only
                try { Hero.MainHero?.HeroDeveloper?.UnspentFocusPoints += 1; } catch { }
                InformationManager.ShowInquiry(new InquiryData(
                    "Training Complete",
                    $"{name} is not ready to carry the fire fully — but the weeks of work sharpened your own teaching instincts. +1 focus point.",
                    true, false, "Release them", "", () => { }, null), true);
            }
            else
            {
                // Failed
                InformationManager.ShowInquiry(new InquiryData(
                    "Training Failed",
                    $"{name} could not sustain it. The fire found no purchase. They leave with a terse goodbye and a talent for avoiding eye contact.",
                    true, false, "Let them go", "", () => { }, null), true);
            }

            _apprenticeId      = null;
            _trainingDaysLeft  = 0;
            _trainingProgress  = 0;
            _rivalCorrupted    = false;
        }

        private static void MakeCompanionMage(Hero h, string name, bool ashen)
        {
            try
            {
                ColourLordRegistry.SetMage(h, true);
                if (ashen) ColourLordRegistry.SetAshen(h, true);
                string kind = ashen ? "Ashen" : "mage";
                InformationManager.ShowInquiry(new InquiryData(
                    "A New Fire in the World",
                    $"{name} carries it now. Not a student anymore. The {kind}'s fire burns in them — smaller than yours, rougher around the edges, but real. What they do with it is their own story.",
                    true, false, "Release them to their fate", "",
                    () => { }, null), true);
            }
            catch
            {
                ShowGraduationMessage(name, ashen ? "cold fire" : "fire", full: true);
            }
        }

        private static void ShowGraduationMessage(string name, string type, bool full)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                full
                    ? $"{name} graduates. The {type} burns in them now — not yours to control."
                    : $"The training with {name} concludes without a lasting gift.",
                new Color(0.7f, 0.9f, 0.5f)));
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static string GetApprenticeName()
        {
            if (_apprenticeId == null) return "the apprentice";
            try
            {
                var h = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == _apprenticeId);
                return h?.Name?.ToString() ?? "the apprentice";
            }
            catch { return "the apprentice"; }
        }

        private static Hero FindApprenticeHero()
        {
            if (_apprenticeId == null) return null;
            try { return Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _apprenticeId); }
            catch { return null; }
        }

        // ── Persistence ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            try
            {
                store.SyncData("AP_Id",        ref _apprenticeId);
                store.SyncData("AP_DaysLeft",  ref _trainingDaysLeft);
                store.SyncData("AP_Progress",  ref _trainingProgress);
                store.SyncData("AP_Corrupted", ref _rivalCorrupted);
                store.SyncData("AP_SearchCD",  ref _searchCooldown);
                store.SyncData("AP_Total",     ref _totalEver);
            }
            catch { }
        }

        public static void ResetForNewGame()
        {
            _apprenticeId      = null;
            _trainingDaysLeft  = 0;
            _trainingProgress  = 0;
            _rivalCorrupted    = false;
            _searchCooldown    = 0;
            _totalEver         = 0;
        }
    }
}
