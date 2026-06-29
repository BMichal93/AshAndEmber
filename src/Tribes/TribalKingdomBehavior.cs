// =============================================================================
// ASH AND EMBER — Tribes/TribalKingdomBehavior.cs
//
// Manages the Tribes of the East (khuzait) as a faction ruled by the God-King:
// a fire-wielding despot who brokers no peace and collects wives from
// conquered lands, keeping his lords in deliberate submission.
//
// Mechanics:
//   God-King Setup     — marked as Pyrelord mage with dark gifts each session.
//   Divine Rule        — a dominance policy is enforced weekly.
//   God-King Dominance — ruling clan influence pinned high; other lords capped.
//   Wives of Conquest  — each conquered town adds a woman to the God-King's
//                        household (capped at TribalWifeMax).
//   Endless War        — any peace involving the Tribes is immediately reversed.
//   Blood Succession   — on the God-King's death, the oldest living son inherits.
//   Self-Immolation    — a captured God-King sets himself ablaze rather than submit.
//   Free Recruitment   — Tribal player can recruit tier-1 tribesmen at no cost
//                        from Tribal towns (global 7-day cooldown — the tribes
//                        answer the champion only once a week, not once per town).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;         // DeclareWarAction, MakePeaceAction
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public class TribalKingdomBehavior : CampaignBehaviorBase
    {
        internal  const string KhuzaitId          = "khuzait";
        private   const float  GodKingInfluenceMin = 4000f;
        private   const float  LordInfluenceCap    = 50f;
        private   const int    TribalWifeMax        = 8;
        private   const int    FreeRecruitCount     = 6;
        private   const int    FreeRecruitCooldown  = 7; // days

        // Persisted: StringIds of consort heroes added to the God-King's clan.
        private static readonly List<string> _consortIds = new List<string>();
        // Persisted: settlement StringIds already processed (so starting towns are skipped).
        private static readonly HashSet<string> _processedSettlements = new HashSet<string>();
        // Persisted: flag that the starting-town snapshot has been taken.
        private static bool _initialSettlementsRecorded = false;
        // Session-only: settlement StringId → last day free recruits were taken.
        private static readonly Dictionary<string, int> _recruitCooldowns
            = new Dictionary<string, int>();
        // Persisted: the day the Call to the Tribes was last answered, anywhere. This
        // is a GLOBAL weekly cap — without it the player could hop between tribal towns
        // and pull FreeRecruitCount fresh troops from each, every visit.
        private static int _lastFreeRecruitDay = -1000;

        private static readonly Random _rng = new Random();

        public static void ResetForNewGame()
        {
            _consortIds.Clear();
            _processedSettlements.Clear();
            _initialSettlementsRecorded = false;
            _recruitCooldowns.Clear();
            _lastFreeRecruitDay = -1000;
        }

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        public override void SyncData(IDataStore store)
        {
            try
            {
                var ids = _consortIds.ToList();
                store.SyncData("TRIBES_ConsortIds", ref ids);
                if (ids != null)
                {
                    _consortIds.Clear();
                    foreach (var id in ids) _consortIds.Add(id);
                }
            }
            catch { }
            try
            {
                var settled = _processedSettlements.ToList();
                store.SyncData("TRIBES_ProcessedSettlements", ref settled);
                if (settled != null)
                {
                    _processedSettlements.Clear();
                    foreach (var id in settled) _processedSettlements.Add(id);
                }
            }
            catch { }
            try { store.SyncData("TRIBES_InitialRecorded", ref _initialSettlementsRecorded); }
            catch { }
            try { store.SyncData("TRIBES_LastFreeRecruitDay", ref _lastFreeRecruitDay); }
            catch { }
        }

        // ── Session launch ─────────────────────────────────────────────────────
        private static void OnSessionLaunched(CampaignGameStarter starter)
        {
            _recruitCooldowns.Clear();
            try { SetupGodKing();        } catch { }
            try { EnforceDivineRule();   } catch { }
            try { RegisterMenus(starter); } catch { }
        }

        // ── Daily tick ─────────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
            try { CheckGodKingCapture(); } catch { }
        }

        // ── Weekly tick ────────────────────────────────────────────────────────
        private static void OnWeeklyTick()
        {
            try { SetupGodKing();             } catch { }
            try { EnforceDivineRule();        } catch { }
            try { MaintainGodKingInfluence(); } catch { }
            try { CapLordInfluence();         } catch { }
            try { CheckConquestWives();       } catch { }
        }

        // ── Hero killed — God-King succession ─────────────────────────────────
        private static void OnHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            try { EnforceGodKingSuccession(victim); } catch { }
        }

        // ── No Quarter ────────────────────────────────────────────────────────
        private static void OnMakePeace(IFaction faction1, IFaction faction2,
            MakePeaceAction.MakePeaceDetail detail)
        {
            try
            {
                bool tribesInvolved =
                    (faction1 as Kingdom)?.StringId == KhuzaitId ||
                    (faction2 as Kingdom)?.StringId == KhuzaitId;
                if (!tribesInvolved) return;

                var tribes = (faction1 as Kingdom)?.StringId == KhuzaitId
                    ? faction1 : faction2;
                var other  = tribes == faction1 ? faction2 : faction1;
                if (tribes == null || other == null) return;
                if ((tribes as Kingdom)?.IsEliminated == true) return;
                if ((other  as Kingdom)?.IsEliminated == true) return;

                // Re-declare war immediately — the God-King does not parley.
                try { DeclareWarAction.ApplyByDefault(tribes, other); } catch { }

                if (TribalCulture.IsPlayerTribal)
                    InformationManager.DisplayMessage(new InformationMessage(
                        "No Quarter — the God-King's word burns through any treaty. The war endures.",
                        new Color(0.85f, 0.35f, 0.15f)));
            }
            catch { }
        }

        // ── Self-Immolation — God-King dies rather than be taken prisoner ────────
        // Checked daily: if the God-King is a prisoner, he immediately sets himself
        // ablaze. Capture is not a fate the divine fire permits.
        private static void CheckGodKingCapture()
        {
            try
            {
                var khuzait = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == KhuzaitId && !k.IsEliminated);
                if (khuzait == null) return;

                var godKing = khuzait.Leader;
                if (godKing == null || !godKing.IsAlive || !godKing.IsPrisoner) return;

                try { KillCharacterAction.ApplyByMurder(godKing, null, false); } catch { }

                InformationManager.DisplayMessage(new InformationMessage(
                    "The God-King would not kneel. He set himself ablaze before his captors could savour the victory.",
                    new Color(0.85f, 0.35f, 0.15f)));
            }
            catch { }
        }

        // ── Blood Succession — oldest son inherits the divine fire ────────────
        // Fires on HeroKilledEvent. If the dead hero was the ruling clan's leader,
        // the oldest living male child (or oldest male clan member) is installed as
        // the new head. SetupGodKing() will re-apply the Pyrelord gifts next week.
        private static void EnforceGodKingSuccession(Hero deadHero)
        {
            try
            {
                var khuzait = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == KhuzaitId && !k.IsEliminated);
                if (khuzait == null) return;

                var rulingClan = khuzait.RulingClan;
                if (rulingClan == null || rulingClan.Leader != deadHero) return;

                // Prefer oldest biological son; fall back to oldest male clan member.
                Hero heir = Hero.AllAliveHeroes
                    .Where(h => h.IsAlive && !h.IsChild && !h.IsFemale
                             && h.Clan == rulingClan && h != deadHero
                             && h.Father?.StringId == deadHero.StringId)
                    .OrderByDescending(h => h.Age)
                    .FirstOrDefault();

                if (heir == null)
                    heir = Hero.AllAliveHeroes
                        .Where(h => h.IsAlive && !h.IsChild && !h.IsFemale
                                 && h.Clan == rulingClan && h != deadHero)
                        .OrderByDescending(h => h.Age)
                        .FirstOrDefault();

                if (heir == null) return;

                try { ChangeClanLeaderAction.ApplyWithSelectedNewLeader(rulingClan, heir); } catch { }

                InformationManager.DisplayMessage(new InformationMessage(
                    $"The God-King is dead. His heir {heir.Name} rises — the divine fire passes to new hands.",
                    new Color(0.85f, 0.45f, 0.2f)));
            }
            catch { }
        }

        // ── Wives of conquest ──────────────────────────────────────────────────
        // Checked weekly: any Khuzait town not yet in _processedSettlements is a
        // new conquest. The first call records the starting towns silently so the
        // God-King doesn't retroactively claim wives for his own homeland.
        private static void CheckConquestWives()
        {
            try
            {
                var tribalTowns = Settlement.All
                    .Where(s => s.IsTown && s.OwnerClan?.Kingdom?.StringId == KhuzaitId)
                    .ToList();

                if (!_initialSettlementsRecorded)
                {
                    foreach (var t in tribalTowns) _processedSettlements.Add(t.StringId);
                    _initialSettlementsRecorded = true;
                    return;
                }

                foreach (var town in tribalTowns)
                {
                    if (_processedSettlements.Contains(town.StringId)) continue;
                    _processedSettlements.Add(town.StringId);
                    if (_consortIds.Count >= TribalWifeMax) continue;
                    AcquireConsort(town);
                }
            }
            catch { }
        }

        // ── God-King setup ─────────────────────────────────────────────────────
        private static void SetupGodKing()
        {
            try
            {
                var khuzait = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == KhuzaitId && !k.IsEliminated);
                if (khuzait == null) return;

                var godKing = khuzait.Leader;
                if (godKing == null || !godKing.IsAlive) return;

                // Mark as Pyrelord — fire and ruin archetype.
                if (!ColourLordRegistry.IsColourLord(godKing))
                    ColourLordRegistry.SetGodKing(godKing);

                // Ensure the evil alignment that activates dark gifts.
                try
                {
                    if (godKing.GetTraitLevel(DefaultTraits.Mercy) > -1)
                        godKing.SetTraitLevel(DefaultTraits.Mercy, -2);
                    if (godKing.GetTraitLevel(DefaultTraits.Honor) > -1)
                        godKing.SetTraitLevel(DefaultTraits.Honor, -2);
                }
                catch { }

                // Seed dark gifts (uses the evil-lord path: DreadPresence + BloodPact
                // are the most thematically appropriate).
                try { DarkGiftSystem.SeedNpcGifts(godKing, isAshenLord: false, isEvilLord: true); }
                catch { }
            }
            catch { }
        }

        // ── Divine Rule — represented through influence dominance ─────────────
        // The God-King's absolute rule is enforced by pinning his clan's influence
        // at GodKingInfluenceMin and capping all other lords. A formal policy hook
        // can be added once the PolicyObject API is confirmed against the game DLLs.
        private static void EnforceDivineRule() { /* placeholder — influence dominance suffices */ }

        // ── Influence management ───────────────────────────────────────────────
        private static void MaintainGodKingInfluence()
        {
            try
            {
                var khuzait = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == KhuzaitId && !k.IsEliminated);
                var ruling = khuzait?.RulingClan ?? khuzait?.Leader?.Clan;
                if (ruling == null) return;
                if (ruling.Influence < GodKingInfluenceMin)
                    ruling.Influence = GodKingInfluenceMin;
            }
            catch { }
        }

        private static void CapLordInfluence()
        {
            try
            {
                var khuzait = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == KhuzaitId && !k.IsEliminated);
                if (khuzait == null) return;

                var rulingClan = khuzait.RulingClan;
                foreach (var clan in khuzait.Clans.ToList())
                {
                    if (clan == null || clan.IsEliminated || clan == rulingClan) continue;
                    if (clan == Clan.PlayerClan) continue;
                    if (clan.Influence > LordInfluenceCap)
                        clan.Influence = LordInfluenceCap;
                }
            }
            catch { }
        }

        // ── Consort acquisition ────────────────────────────────────────────────
        private static void AcquireConsort(Settlement capturedTown)
        {
            try
            {
                var khuzait   = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == KhuzaitId && !k.IsEliminated);
                var godKingClan = khuzait?.RulingClan ?? khuzait?.Leader?.Clan;
                if (godKingClan == null) return;

                string cultureId = capturedTown.Culture?.StringId ?? "";

                // Prefer a female template from the conquered culture.
                CharacterObject template = CharacterObject.All.FirstOrDefault(c =>
                    c != null && !c.IsHero && c.IsFemale && c.Culture?.StringId == cultureId);
                if (template == null)
                    template = CharacterObject.All.FirstOrDefault(c =>
                        c != null && !c.IsHero && c.IsFemale);
                if (template == null) return;

                int age = 18 + _rng.Next(9); // 18–26
                Hero consort = HeroCreator.CreateChild(template, capturedTown, godKingClan, age);
                if (consort == null) return;

                _consortIds.Add(consort.StringId);

                InformationManager.DisplayMessage(new InformationMessage(
                    $"A woman of {capturedTown.Name} is claimed for the God-King's household. His dominion grows.",
                    new Color(0.85f, 0.45f, 0.2f)));
            }
            catch { }
        }

        // ── Free tribal recruitment menu ───────────────────────────────────────
        private static void RegisterMenus(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption(
                    "town",
                    "tribal_free_recruit",
                    "{TRIBAL_RECRUIT_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!TribalCulture.IsPlayerTribal) return false;
                            var s = Settlement.CurrentSettlement;
                            if (s == null || !s.IsTown) return false;
                            if (s.OwnerClan?.Kingdom?.StringId != KhuzaitId) return false;

                            int day = (int)CampaignTime.Now.ToDays;
                            // Global weekly cap: the tribes answer the champion only once
                            // a week, no matter which town the call goes out from.
                            int sinceCall  = day - _lastFreeRecruitDay;
                            bool onCooldown = sinceCall < FreeRecruitCooldown;

                            string status = onCooldown
                                ? $"  [The tribes have answered lately — ready in {FreeRecruitCooldown - sinceCall} day(s)]"
                                : "  [Free — tribesmen answer your call]";
                            MBTextManager.SetTextVariable("TRIBAL_RECRUIT_TEXT",
                                "Call to the Tribes" + status);

                            try { args.optionLeaveType = GameMenuOption.LeaveType.Continue; } catch { }
                            args.IsEnabled = !onCooldown;
                            return true;
                        }
                        catch { return false; }
                    },
                    args =>
                    {
                        try
                        {
                            var s = Settlement.CurrentSettlement;
                            if (s == null) return;

                            // Find tier-1 Khuzait troops and add to the player's party.
                            var tier1 = CharacterObject.All.FirstOrDefault(c =>
                                !c.IsHero && c.Culture?.StringId == KhuzaitId && c.Tier == 1);
                            if (tier1 != null && MobileParty.MainParty != null)
                            {
                                MobileParty.MainParty.AddElementToMemberRoster(tier1, FreeRecruitCount);
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{FreeRecruitCount} tribesmen answer the call of the God-King's champion.",
                                    new Color(0.85f, 0.55f, 0.2f)));
                            }

                            _recruitCooldowns[s.StringId] = (int)CampaignTime.Now.ToDays;
                            _lastFreeRecruitDay           = (int)CampaignTime.Now.ToDays;
                        }
                        catch { }
                    },
                    false, -1, false);
            }
            catch { }
        }
    }
}
