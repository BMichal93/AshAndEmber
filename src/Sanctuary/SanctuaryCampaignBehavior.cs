// =============================================================================
// ASH AND EMBER — Sanctuary/SanctuaryCampaignBehavior.cs
//
// Ritual-based prayer system. When a player selects a prayer/spell, a hidden
// target number is rolled. Each round of Meditation inflicts a cost on the
// party (troops wounded or days aging). A hidden number of points — scaled
// by alignment traits — is added to the player's accumulated pool. The player
// chooses to stop or continue each round. If they stop once accumulated points
// meet or exceed the target, the prayer fires. Stopping short wastes the cost.
//
// Sanctuaries: Temple-owned towns + 4 random Empire towns.
// Any hero may approach. Alignment (Mercy+Honor+Generosity)/6 determines yield per
// round and effect strength. Zero alignment gives 1 pt/round — success is possible
// but requires many painful rounds for little reward.
// Temple members reduce all rite cooldowns by 40%.
//
// NPC effects (daily tick):
//   Honourable + Merciful lords in a sanctuary city: 0.3% chance/day.
//   Temple faction lords: 3% chance/day to heal; 2% to Turn nearby Ashen.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
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
    public class SanctuaryCampaignBehavior : CampaignBehaviorBase
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        private const int MoralePrayerBoost    = 40;
        private const int ProtectiveDays       = 14;
        private const int BlessingRejuvDays    = 365;
        private const int BlessingMinAge       = 20;
        private const int BlessedDays          = 3;
        private const int SteadyLineDays       = 5;
        private const int TraitBoostDays       = 60;
        private const int TraitDriftThreshold  = 10;
        private const int CrossInterferenceDays= 30;
        private const int PermanentSanctuaryCount = 4;

        // Cooldowns (base days; Temple reduces by 40%)
        private const int PrayerCooldownBase    =  7;
        private const int ProtectiveCooldownBase= 10;
        private const int TurnAshenCooldownBase = 10;
        private const int HealingCooldownBase   =  7;
        private const int BlessingCooldownBase  = 30;

        // Location depletion: after DepletionThreshold ritual starts the flame rests
        private const int DepletionThreshold    =  5;
        private const int DepletionCooldown     = 30;

        // Ritual target ranges (hidden from player — lo inclusive, hi inclusive)
        private const int PrayerTargetLo   = 10; private const int PrayerTargetHi   = 18;
        private const int HealTargetLo     = 18; private const int HealTargetHi     = 30;
        private const int ProtectTargetLo  = 22; private const int ProtectTargetHi  = 35;
        private const int TurnTargetLo     = 26; private const int TurnTargetHi     = 40;
        private const int BlessTargetLo    = 35; private const int BlessTargetHi    = 55;

        private const string TempleKingdomId = "the_temple";
        private const string AshenKingdomId  = "ashen_kingdom";

        private static readonly List<string> _permanentSanctuaryIds = new List<string>();

        // Cross-system state (read by AshenAltarsCampaignBehavior)
        internal static int _lastSanctuaryUseDay = -999;
        private static int  _sanctuaryUseCount   = 0;

        private static int  _blessedUntilDay    = -1;
        private static int  _steadyLineUntilDay = -1;
        private static int  _traitBoostUntilDay = -1;
        private static float _traitBoostAmount  = 0f;

        // Per-rite cooldown tracking
        private static int  _lastPrayerDay     = -999;
        private static int  _lastProtectiveDay = -999;
        private static int  _lastTurnAshenDay  = -999;
        private static int  _lastHealingDay    = -999;
        private static int  _lastBlessingDay   = -999;

        private static readonly Dictionary<string, int> _locationUses          = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _locationDepletedUntil = new Dictionary<string, int>();

        private static readonly Random _rng = new Random();

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try
            {
                var ids = _permanentSanctuaryIds.ToList();
                store.SyncData("SANCT_PermanentIds", ref ids);
                if (ids != null) { _permanentSanctuaryIds.Clear(); foreach (var id in ids) _permanentSanctuaryIds.Add(id); }
            }
            catch { }
            try { store.SyncData("SANCT_LastUseDay", ref _lastSanctuaryUseDay); } catch { }
            try { store.SyncData("SANCT_UseCount", ref _sanctuaryUseCount); } catch { }
            try { store.SyncData("SANCT_BlessedUntilDay", ref _blessedUntilDay); } catch { }
            try { store.SyncData("SANCT_SteadyLineUntilDay", ref _steadyLineUntilDay); } catch { }
            try { store.SyncData("SANCT_TraitBoostUntilDay", ref _traitBoostUntilDay); } catch { }
            try { store.SyncData("SANCT_TraitBoostAmount", ref _traitBoostAmount); } catch { }
            try { store.SyncData("SANCT_LastPrayerDay", ref _lastPrayerDay); } catch { }
            try { store.SyncData("SANCT_LastProtectiveDay", ref _lastProtectiveDay); } catch { }
            try { store.SyncData("SANCT_LastTurnAshenDay", ref _lastTurnAshenDay); } catch { }
            try { store.SyncData("SANCT_LastHealingDay", ref _lastHealingDay); } catch { }
            try { store.SyncData("SANCT_LastBlessingDay", ref _lastBlessingDay); } catch { }
            try
            {
                var luKeys = _locationUses.Keys.ToList();
                var luVals = _locationUses.Values.ToList();
                store.SyncData("SANCT_LocUseKeys", ref luKeys);
                store.SyncData("SANCT_LocUseVals", ref luVals);
                if (luKeys != null && luVals != null)
                { _locationUses.Clear(); for (int i = 0; i < Math.Min(luKeys.Count, luVals.Count); i++) _locationUses[luKeys[i]] = luVals[i]; }
            } catch { }
            try
            {
                var ldKeys = _locationDepletedUntil.Keys.ToList();
                var ldVals = _locationDepletedUntil.Values.ToList();
                store.SyncData("SANCT_LocDepKeys", ref ldKeys);
                store.SyncData("SANCT_LocDepVals", ref ldVals);
                if (ldKeys != null && ldVals != null)
                { _locationDepletedUntil.Clear(); for (int i = 0; i < Math.Min(ldKeys.Count, ldVals.Count); i++) _locationDepletedUntil[ldKeys[i]] = ldVals[i]; }
            } catch { }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsurePermanentSanctuaries();
            RegisterSanctuaryMenus(starter);
        }

        // ── Permanent sanctuary selection ──────────────────────────────────────
        private static void EnsurePermanentSanctuaries()
        {
            if (_permanentSanctuaryIds.Count >= PermanentSanctuaryCount) return;
            try
            {
                var empireIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "empire_w", "empire", "empire_s", "empire_n" };
                var picks = Settlement.All
                    .Where(s => s.IsTown && s.OwnerClan?.Kingdom != null
                             && empireIds.Contains(s.OwnerClan.Kingdom.StringId))
                    .OrderBy(_ => _rng.Next()).Take(PermanentSanctuaryCount).ToList();
                _permanentSanctuaryIds.Clear();
                foreach (var s in picks) _permanentSanctuaryIds.Add(s.StringId);
                if (picks.Count > 0)
                {
                    var names = picks.Select(s => s.Name.ToString()).ToList();
                    string joined = names.Count == 1 ? names[0]
                        : string.Join(", ", names.Take(names.Count - 1)) + ", and " + names.Last();
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Sanctuaries of the Flame have been established in {joined}. " +
                        "Honourable and Merciful travellers may seek their rites there."));
                }
            }
            catch { }
        }

        internal static bool AddPermanentSanctuary(string id)
        {
            if (string.IsNullOrEmpty(id) || _permanentSanctuaryIds.Contains(id)) return false;
            _permanentSanctuaryIds.Add(id);
            return true;
        }

        internal static bool HasSanctuary(Settlement s)
        {
            if (s == null || !s.IsTown) return false;
            if (s.OwnerClan?.Kingdom?.StringId == TempleKingdomId) return true;
            return _permanentSanctuaryIds.Contains(s.StringId);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static int CurrentCampaignDay()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        private static bool IsRiteOnCooldown(int lastDay, int baseCooldown, float mult)
        {
            float absM   = Math.Min(1f, Math.Abs(mult));
            float factor = IsTempleMember() ? 0.60f : 1f;
            int cooldown = Math.Max(1, (int)(baseCooldown * (2f - absM) * factor));
            return CurrentCampaignDay() - lastDay < cooldown;
        }

        private static int CooldownDaysLeft(int lastDay, int baseCooldown, float mult)
        {
            float absM   = Math.Min(1f, Math.Abs(mult));
            float factor = IsTempleMember() ? 0.60f : 1f;
            int cooldown = Math.Max(1, (int)(baseCooldown * (2f - absM) * factor));
            return cooldown - (CurrentCampaignDay() - lastDay);
        }

        private static bool IsLocationDepleted()
        {
            string id = Settlement.CurrentSettlement?.StringId ?? "";
            if (string.IsNullOrEmpty(id)) return false;
            return _locationDepletedUntil.TryGetValue(id, out int until) && CurrentCampaignDay() <= until;
        }

        private static int LocationDepletedDaysLeft()
        {
            string id = Settlement.CurrentSettlement?.StringId ?? "";
            if (!_locationDepletedUntil.TryGetValue(id, out int until)) return 0;
            return Math.Max(0, until - CurrentCampaignDay());
        }

        private static void RecordLocationUse()
        {
            string id = Settlement.CurrentSettlement?.StringId ?? "";
            if (string.IsNullOrEmpty(id)) return;
            if (!_locationUses.TryGetValue(id, out int count)) count = 0;
            count++;
            if (count >= DepletionThreshold)
            {
                _locationDepletedUntil[id] = CurrentCampaignDay() + DepletionCooldown;
                _locationUses[id] = 0;
                MBInformationManager.AddQuickInformation(new TextObject(
                    "The flame here is spent. This sanctuary needs time to recover."));
            }
            else _locationUses[id] = count;
        }

        // +1.0 = full flame blessing; 0 = no effect; negative = penalty.
        internal static float SanctuaryTraitMultiplier()
        {
            var h = Hero.MainHero;
            if (h == null) return 0f;
            try
            {
                int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                int honor = h.GetTraitLevel(DefaultTraits.Honor);
                int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                float raw = (mercy + honor + gen) / 6f;
                if (CurrentCampaignDay() - AshenAltarsCampaignBehavior._lastAltarUseDay < CrossInterferenceDays)
                    raw *= 0.5f;
                raw += _traitBoostAmount;
                return Math.Max(-1f, Math.Min(1f, raw));
            }
            catch { return 0f; }
        }

        private static float NpcSanctuaryMult(Hero h)
        {
            try
            {
                int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                int honor = h.GetTraitLevel(DefaultTraits.Honor);
                int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                return Math.Max(-1f, Math.Min(1f, (mercy + honor + gen) / 6f));
            }
            catch { return 0f; }
        }

        private static string SanctuaryTraitNote(float mult)
        {
            if (mult >= 0.8f)  return "  [Flame knows you — full blessing]";
            if (mult >= 0.4f)  return "  [Partial blessing]";
            if (mult >= 0.01f) return "  [Faint blessing — cold soul]";
            if (mult >= -0.01f)return "  [Stranger — many rounds needed; weak reward]";
            if (mult >= -0.5f) return "  [PENALTY — the flame recoils; great cost, lesser yield]";
            return "  [HEAVY PENALTY — every round will bleed you; reward barely flickers]";
        }

        private static bool NpcCanUseSanctuary(Hero h)
        {
            try
            {
                return h.GetTraitLevel(DefaultTraits.Honor) >= 1
                    && h.GetTraitLevel(DefaultTraits.Mercy)  >= 1;
            }
            catch { return false; }
        }

        private static bool IsTempleMember()
            => Hero.MainHero?.Clan?.Kingdom?.StringId == TempleKingdomId;

        private static void AgeHero(Hero h, int days)
        {
            if (h == null || days <= 0) return;
            try { AgingSystem.AgeHero(h, days); } catch { }
        }

        // ── Ritual core ────────────────────────────────────────────────────────
        // Points gained per meditation round. Floor of 1 so unaligned heroes can still succeed — slowly.
        private static int RollRoundPoints(float mult)
        {
            if (mult <= 0f) return 1; // 1 pt/round regardless; alignment accelerates yield
            int raw = 3 + _rng.Next(8); // 3–10
            return Math.Max(1, (int)Math.Round(raw * mult));
        }

        // Hint text to show after each round. Deliberately vague to hide the target.
        private static string FlameProgressHint(int accumulated, int target)
        {
            float pct = target > 0 ? (float)accumulated / target : 0f;
            if (accumulated <= 0) return "The flame does not stir. The candles burn as they did before you knelt.";
            if (pct < 0.30f) return "Something has been noticed. The flame flickers once, then settles.";
            if (pct < 0.60f) return "The flame reaches toward you. It is considering you.";
            if (pct < 0.90f) return "The warmth is building. The priest takes a half-step forward.";
            return "The fire is almost ready. One more push and it answers completely.";
        }

        // Applies one round of meditation cost. Returns narrative string.
        // The hero bleeds for the flame — never killed (clamped to 1 HP).
        private static string ApplyMeditationCost_SelfHP(int minHP, int maxHP)
        {
            var hero = Hero.MainHero;
            if (hero == null) return "The fire inside you finds nothing to burn.";
            int damage = minHP + _rng.Next(maxHP - minHP + 1);
            int actual = Math.Min(damage, hero.HitPoints - 1);
            try { hero.HitPoints = Math.Max(1, hero.HitPoints - actual); } catch { }
            if (actual <= 0)
                return "You have almost nothing left to give. The flame takes what it can find — a heartbeat, a shiver.";
            return hero.HitPoints <= 15
                ? $"You give {actual} of your own blood to the flame. You are barely standing. The fire is very close to the skin now."
                : $"You give {actual} of your own blood to the flame. The wound is yours. The fire drinks it.";
        }

        private static string ApplyMeditationCost_Aging(int minDays, int maxDays)
        {
            int days = minDays + _rng.Next(maxDays - minDays + 1);
            AgeHero(Hero.MainHero, days);
            return $"Time passes differently here. You have given {days} day{(days != 1 ? "s" : "")} of your life to the flame.";
        }

        // HP drain + years of life — for the heaviest rites.
        private static string ApplyMeditationCost_SelfHP_Aging(int minHP, int maxHP, int minDays, int maxDays)
        {
            string hpNarr    = ApplyMeditationCost_SelfHP(minHP, maxHP);
            string agingNarr = ApplyMeditationCost_Aging(minDays, maxDays);
            return hpNarr + " " + agingNarr;
        }

        // Core ritual loop (shared by all five rites).
        // target: hidden from player. accumulated/round: updated each call via closures.
        // applyRoundCost: called once per round, returns narrative.
        // onSuccess/onFailure: called when player stops.
        private static void RunSanctuaryRitual(
            string riteName,
            int target,
            float mult,
            Func<string> applyRoundCost,
            Action onSuccess,
            Action onFailure)
        {
            int accumulated = 0;
            int round       = 0;

            void DoRound()
            {
                string costNarr = applyRoundCost();
                int pts = RollRoundPoints(mult);
                accumulated += pts;
                round++;

                string hint = FlameProgressHint(accumulated, target);
                string header = $"{riteName} — Meditation ({round})";
                string body   = $"{costNarr}\n\n{hint}";

                try
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        header, body, true, true,
                        "Continue the meditation",
                        "Step back — claim what the flame offers",
                        () => DoRound(),
                        () =>
                        {
                            if (accumulated >= target) onSuccess();
                            else onFailure();
                        }));
                }
                catch
                {
                    // Fallback: resolve immediately
                    if (accumulated >= target) onSuccess();
                    else onFailure();
                }
            }

            DoRound();
        }

        private static void ShowRitualFailure(string riteName)
        {
            string msg = "The threshold was not reached. The rite is unfinished. " +
                "The candles burn on as if you were never here. The cost you paid does not return.";
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    riteName, msg, true, false, "The price is paid.", "", null, null));
            }
            catch { MBInformationManager.AddQuickInformation(new TextObject($"{riteName} — ritual incomplete.")); }
        }

        // ── Menu registration ──────────────────────────────────────────────────
        private static void RegisterSanctuaryMenus(CampaignGameStarter starter)
        {
            // Entry in town menu
            try
            {
                starter.AddGameMenuOption("town", "sanctuary_enter", "{SANCT_ENTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!HasSanctuary(Settlement.CurrentSettlement)) return false;
                            float mult = SanctuaryTraitMultiplier();
                            MBTextManager.SetTextVariable("SANCT_ENTER_TEXT",
                                "Visit the Sanctuary" + SanctuaryTraitNote(mult));
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Sub-menu header
            try
            {
                starter.AddGameMenu("sanctuary_menu", "{SANCT_MENU_HEADER}", args =>
                {
                    try
                    {
                        int today = CurrentCampaignDay();
                        int rem   = CampaignMapEvents.ProtectedDaysRemaining;
                        string protNote  = rem > 0 ? $"  [Protective ward: {rem} day(s) remaining]" : "";
                        string blessNote = _blessedUntilDay >= today ? $"  [Blessed: {_blessedUntilDay - today + 1} day(s)]" : "";
                        string steadNote = _steadyLineUntilDay >= today ? $"  [Steady the Line: {_steadyLineUntilDay - today + 1} day(s)]" : "";
                        string traitNote = _traitBoostUntilDay >= today ? $"  [Flame Mark: {_traitBoostUntilDay - today + 1} day(s)]" : "";
                        string deplNote = IsLocationDepleted()
                            ? $"  [SPENT — returns in {LocationDepletedDaysLeft()} day(s)]" : "";
                        string hdr = IsTempleMember()
                            ? $"The Sanctuary of The Temple. The flame knows you. Cooldowns reduced.{protNote}{blessNote}{steadNote}{traitNote}{deplNote}"
                            : $"The Sanctuary. Candles burn in rows that stretch further than the room should allow.{protNote}{blessNote}{steadNote}{traitNote}{deplNote}";
                        MBTextManager.SetTextVariable("SANCT_MENU_HEADER", hdr);
                    }
                    catch { }
                });
            }
            catch { }

            // ── Prayer of Strength ──────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_prayer", "{SANCT_PRAY_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = SanctuaryTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastPrayerDay, PrayerCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                cd = $"  [On cooldown: {CooldownDaysLeft(_lastPrayerDay, PrayerCooldownBase, mult)} day(s)]";
                            }
                            MBTextManager.SetTextVariable("SANCT_PRAY_TEXT",
                                $"Prayer of Strength (8–15 hero HP/round) — fortify party morale (+{MoralePrayerBoost}){cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartPrayerOfStrength());
            }
            catch { }

            // ── Protective Rites ────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_rites", "{SANCT_RITES_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = SanctuaryTraitMultiplier();
                            int cur = CampaignMapEvents.ProtectedDaysRemaining;
                            string cd = "", active = cur > 0 ? $"  [active: {cur} day(s) left]" : "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastProtectiveDay, ProtectiveCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                cd = $"  [On cooldown: {CooldownDaysLeft(_lastProtectiveDay, ProtectiveCooldownBase, mult)} day(s)]";
                            }
                            MBTextManager.SetTextVariable("SANCT_RITES_TEXT",
                                $"Protective Rites (12–20 hero HP + 1 day aging/round) — ward against Ashen events for {ProtectiveDays} days{active}{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartProtectiveRites());
            }
            catch { }

            // ── Turn the Ashen ──────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_turn", "{SANCT_TURN_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = SanctuaryTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastTurnAshenDay, TurnAshenCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                cd = $"  [On cooldown: {CooldownDaysLeft(_lastTurnAshenDay, TurnAshenCooldownBase, mult)} day(s)]";
                            }
                            MBTextManager.SetTextVariable("SANCT_TURN_TEXT",
                                $"Turn the Ashen (15–25 hero HP/round) — banish and wound nearby Ashen parties{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartTurnAshen());
            }
            catch { }

            // ── Prayer of Healing ───────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_healing", "{SANCT_HEAL_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = SanctuaryTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastHealingDay, HealingCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                cd = $"  [On cooldown: {CooldownDaysLeft(_lastHealingDay, HealingCooldownBase, mult)} day(s)]";
                            }
                            MBTextManager.SetTextVariable("SANCT_HEAL_TEXT",
                                $"Prayer of Healing (12–20 hero HP/round) — heal the wounded or steady the line{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartHealing());
            }
            catch { }

            // ── Prayer for a Blessing ───────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_blessing", "{SANCT_BLESS_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = SanctuaryTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastBlessingDay, BlessingCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                cd = $"  [On cooldown: {CooldownDaysLeft(_lastBlessingDay, BlessingCooldownBase, mult)} day(s)]";
                            }
                            MBTextManager.SetTextVariable("SANCT_BLESS_TEXT",
                                $"Prayer for a Blessing (15–25 hero HP + 2–4 days aging/round) — shed a year or receive the flame's mark{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartBlessing());
            }
            catch { }

            // ── Leave ───────────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_leave", "Leave the Sanctuary",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Ritual initiators ─────────────────────────────────────────────────

        private static void StartPrayerOfStrength()
        {
            float mult = SanctuaryTraitMultiplier();
            int target = PrayerTargetLo + _rng.Next(PrayerTargetHi - PrayerTargetLo + 1);

            _lastPrayerDay       = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();
            _sanctuaryUseCount++;
            RecordLocationUse();

            RunSanctuaryRitual(
                "Prayer of Strength",
                target, mult,
                () => ApplyMeditationCost_SelfHP(8, 15),
                () =>
                {
                    // Success: boost morale and start blessed status
                    int boost = Math.Max(1, (int)(MoralePrayerBoost * mult));
                    try { MobileParty.MainParty.RecentEventsMorale += boost; } catch { }
                    _blessedUntilDay = CurrentCampaignDay() + BlessedDays;
                    string msg = $"The flame answers. The weight your men carry is the same, but it sits differently. " +
                        $"Morale +" + boost + $". For the next {BlessedDays} days your surgeons will find their patients cooperative.";
                    try { InformationManager.ShowInquiry(new InquiryData("Prayer of Strength", msg, true, false, "So be it.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject($"Prayer of Strength — +{boost} morale.")); }
                    try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                },
                () => { ShowRitualFailure("Prayer of Strength"); try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } });
        }

        private static void StartProtectiveRites()
        {
            float mult = SanctuaryTraitMultiplier();
            int target = ProtectTargetLo + _rng.Next(ProtectTargetHi - ProtectTargetLo + 1);

            _lastProtectiveDay   = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();
            _sanctuaryUseCount++;
            RecordLocationUse();

            RunSanctuaryRitual(
                "Protective Rites",
                target, mult,
                () => ApplyMeditationCost_SelfHP_Aging(12, 20, 1, 1),
                () =>
                {
                    // Success: ward active
                    int days = Math.Max(7, (int)(ProtectiveDays * mult));
                    CampaignMapEvents.StartProtection(days);

                    float px = 0f, py = 0f;
                    try { px = MobileParty.MainParty.GetPosition2D.x; py = MobileParty.MainParty.GetPosition2D.y; } catch { }
                    var nearbyAshen = MobileParty.All
                        .Where(p => { if (!p.IsActive || p.MapFaction?.StringId != AshenKingdomId) return false;
                                      float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py;
                                      return dx * dx + dy * dy < 200f * 200f; })
                        .Take(3).ToList();
                    string ashenNote = nearbyAshen.Count > 0
                        ? $"\n\nThe flame shows what hunts nearby: {string.Join(", ", nearbyAshen.Select(p => p.Name?.ToString() ?? "?"))}."
                        : "\n\nNo grey things stir within sight of the flame right now.";

                    string msg = $"The priest draws symbols in ash across your palms. The flame burns hotter for a moment. " +
                        $"You carry this for {days} days. The grey things will find your scent harder to follow." + ashenNote;
                    try { InformationManager.ShowInquiry(new InquiryData("Protective Rites", msg, true, false, "I understand.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject($"Protective Rites — ward active for {days} days.")); }
                    try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                },
                () => { ShowRitualFailure("Protective Rites"); try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } });
        }

        private static void StartTurnAshen()
        {
            float mult = SanctuaryTraitMultiplier();
            int target = TurnTargetLo + _rng.Next(TurnTargetHi - TurnTargetLo + 1);

            _lastTurnAshenDay    = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();
            _sanctuaryUseCount++;
            RecordLocationUse();

            RunSanctuaryRitual(
                "Turn the Ashen",
                target, mult,
                () => ApplyMeditationCost_SelfHP(15, 25),
                () =>
                {
                    float px = 0f, py = 0f;
                    try { px = MobileParty.MainParty.GetPosition2D.x; py = MobileParty.MainParty.GetPosition2D.y; } catch { }
                    float rng2 = 200f * 200f;
                    var targets = MobileParty.All
                        .Where(p => p.IsActive && !p.IsMainParty && p.MapFaction?.StringId == AshenKingdomId)
                        .Select(p => { float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py; return (party: p, dist2: dx*dx+dy*dy); })
                        .Where(t => t.dist2 < rng2).OrderBy(t => t.dist2).Take(3).Select(t => t.party).ToList();

                    int totalWounded = 0;
                    foreach (var p in targets)
                    {
                        int toWound = Math.Max(1, (int)((12 + _rng.Next(9)) * mult));
                        int w = 0;
                        foreach (var e in p.MemberRoster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            int healthy = e.Number - e.WoundedNumber;
                            int n = Math.Min(healthy, toWound - w); if (n <= 0) continue;
                            try { p.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                            if (w >= toWound) break;
                        }
                        totalWounded += w;
                        try { p.RecentEventsMorale -= 35f * mult; } catch { }
                    }
                    string msg = targets.Count == 0
                        ? "The rite is spoken. The flame surges and settles. No grey things are close enough to feel it."
                        : $"{targets.Count} Ashen part{(targets.Count > 1 ? "ies" : "y")} recoil from the flame. {totalWounded} cold soldiers are on their knees.";
                    try { InformationManager.ShowInquiry(new InquiryData("Turn the Ashen", msg, true, false, "The flame holds.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(msg.Length > 80 ? msg.Substring(0,80)+"…" : msg)); }
                    try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                },
                () => { ShowRitualFailure("Turn the Ashen"); try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } });
        }

        private static void StartHealing()
        {
            float mult = SanctuaryTraitMultiplier();
            int target = HealTargetLo + _rng.Next(HealTargetHi - HealTargetLo + 1);

            _lastHealingDay      = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();
            _sanctuaryUseCount++;
            RecordLocationUse();

            RunSanctuaryRitual(
                "Prayer of Healing",
                target, mult,
                () => ApplyMeditationCost_SelfHP(12, 20),
                () =>
                {
                    // Success: offer two options
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "The Flame Offers Two Gifts",
                            $"The priest's hands are steady. The flame has enough for what you need. Choose how it spends itself.\n\n" +
                            "Heal the Wounded — the injured rise from their cots.\n\n" +
                            $"Steady the Line — for {SteadyLineDays} days the fallen are carried back rather than left. Troops count as wounded more often than dead.",
                            true, true,
                            "Heal the Wounded",
                            "Steady the Line",
                            () =>
                            {
                                int healed = 0;
                                var roster = MobileParty.MainParty?.MemberRoster;
                                if (roster != null)
                                    foreach (var e in roster.GetTroopRoster().ToList())
                                    {
                                        if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                                        int heal = Math.Max(1, (int)(e.WoundedNumber * Math.Max(0.3f, mult)));
                                        try { roster.AddToCounts(e.Character, 0, false, -heal); healed += heal; } catch { }
                                    }
                                try { if (Hero.MainHero.HitPoints < Hero.MainHero.MaxHitPoints) Hero.MainHero.HitPoints = Hero.MainHero.MaxHitPoints; } catch { }
                                string narr = healed > 0
                                    ? $"By the time the priest finishes speaking, the bandaged men in your camp are sitting up. " +
                                      $"{healed} soldier{(healed != 1 ? "s" : "")} who should have needed another week are folding their blankets."
                                    : "The flame heals what it can. Nothing remains to be healed.";
                                try { InformationManager.ShowInquiry(new InquiryData("Prayer of Healing", narr, true, false, "It is enough.", "", null, null)); }
                                catch { MBInformationManager.AddQuickInformation(new TextObject(healed > 0 ? $"+{healed} healed." : "Nothing to heal.")); }
                                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                            },
                            () =>
                            {
                                _steadyLineUntilDay = CurrentCampaignDay() + SteadyLineDays;
                                string narr = $"The priest stands at the altar and speaks to the flame alone. The candles burn steadier. " +
                                    $"For the next {SteadyLineDays} days, the flame will carry your fallen back from the edge.";
                                try { InformationManager.ShowInquiry(new InquiryData("Steady the Line", narr, true, false, "The line holds.", "", null, null)); }
                                catch { MBInformationManager.AddQuickInformation(new TextObject($"Steady the Line active for {SteadyLineDays} days.")); }
                                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                            }));
                    }
                    catch
                    {
                        // Fallback: heal directly
                        int healed = 0;
                        var roster = MobileParty.MainParty?.MemberRoster;
                        if (roster != null)
                            foreach (var e in roster.GetTroopRoster().ToList())
                            {
                                if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                                int heal = Math.Max(1, (int)(e.WoundedNumber * Math.Max(0.3f, mult)));
                                try { roster.AddToCounts(e.Character, 0, false, -heal); healed += heal; } catch { }
                            }
                        MBInformationManager.AddQuickInformation(new TextObject(healed > 0 ? $"Prayer of Healing — {healed} restored." : "Prayer of Healing — nothing to heal."));
                        try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                    }
                },
                () => { ShowRitualFailure("Prayer of Healing"); try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } });
        }

        private static void StartBlessing()
        {
            float mult = SanctuaryTraitMultiplier();
            var   hero = Hero.MainHero;
            if (hero == null) { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } return; }
            int target = BlessTargetLo + _rng.Next(BlessTargetHi - BlessTargetLo + 1);

            _lastBlessingDay     = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();
            _sanctuaryUseCount++;
            RecordLocationUse();

            RunSanctuaryRitual(
                "Prayer for a Blessing",
                target, mult,
                () => ApplyMeditationCost_SelfHP_Aging(15, 25, 2, 4),
                () =>
                {
                    // Success: offer two choices
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "What Will You Ask of the Flame?",
                            "The flame waits. It has enough in it for one thing.",
                            true, true,
                            "Shed a year",
                            "The flame marks you",
                            () =>
                            {
                                float currentAge = hero.Age;
                                int maxRejuv  = (int)Math.Max(0f, (currentAge - BlessingMinAge) * 365.25f);
                                int actualDays= Math.Min((int)(BlessingRejuvDays * Math.Max(0.5f, mult)), maxRejuv);
                                if (actualDays > 0) try { AgingSystem.RejuvenateHero(hero, actualDays); } catch { }
                                int yrs = actualDays / 365;
                                string narr = yrs > 0
                                    ? $"The priest places both hands on the altar. When he finishes, the candles are shorter. " +
                                      $"So are you, in a way that doesn't show in a mirror but shows in how your joints feel. " +
                                      $"{yrs} year{(yrs != 1 ? "s" : "")} paid back."
                                    : "The priest completes the rite. The flame does what it can, but finds very little left to return.";
                                try { InformationManager.ShowInquiry(new InquiryData("Prayer for a Blessing", narr, true, false, "So be it.", "", null, null)); }
                                catch { MBInformationManager.AddQuickInformation(new TextObject(yrs > 0 ? $"+{yrs} year(s) returned." : "Nothing returned.")); }
                                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                            },
                            () =>
                            {
                                _traitBoostUntilDay = CurrentCampaignDay() + TraitBoostDays;
                                _traitBoostAmount = 1f / 6f;
                                string narr = "The priest presses his thumb to your brow. The candles burn brighter for a breath. " +
                                    $"The mark will fade in {TraitBoostDays} days.";
                                try { InformationManager.ShowInquiry(new InquiryData("The Flame Marks You", narr, true, false, "I carry it now.", "", null, null)); }
                                catch { MBInformationManager.AddQuickInformation(new TextObject($"Flame Mark active for {TraitBoostDays} days.")); }
                                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                            }));
                    }
                    catch
                    {
                        // Fallback: rejuvenation
                        float currentAge = hero.Age;
                        int maxRejuv   = (int)Math.Max(0f, (currentAge - BlessingMinAge) * 365.25f);
                        int actualDays = Math.Min((int)(BlessingRejuvDays * Math.Max(0.5f, mult)), maxRejuv);
                        if (actualDays > 0) try { AgingSystem.RejuvenateHero(hero, actualDays); } catch { }
                        MBInformationManager.AddQuickInformation(new TextObject(actualDays > 0 ? $"Prayer for a Blessing — {actualDays/365} year(s) returned." : "Prayer for a Blessing — no effect."));
                        try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                    }
                },
                () => { ShowRitualFailure("Prayer for a Blessing"); try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } });
        }

        // ── NPC daily tick ─────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
            int today = CurrentCampaignDay();

            // Blessed status: 10% healing per day
            if (_blessedUntilDay >= today && MobileParty.MainParty?.MemberRoster != null)
                foreach (var e in MobileParty.MainParty.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                    int heal = Math.Max(1, (int)(e.WoundedNumber * 0.10f));
                    try { MobileParty.MainParty.MemberRoster.AddToCounts(e.Character, 0, false, -heal); } catch { }
                }

            // Trait boost expiry
            if (_traitBoostUntilDay >= 0 && today > _traitBoostUntilDay)
            { _traitBoostAmount = 0f; _traitBoostUntilDay = -1; }

            // Trait drift: every 10 sanctuary uses, nudge highest-deficit trait up
            if (_sanctuaryUseCount > 0 && _sanctuaryUseCount % TraitDriftThreshold == 0)
            {
                try
                {
                    var h = Hero.MainHero;
                    if (h != null)
                    {
                        int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                        int honor = h.GetTraitLevel(DefaultTraits.Honor);
                        int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                        if (mercy <= honor && mercy <= gen && mercy < 2)       h.SetTraitLevel(DefaultTraits.Mercy, mercy + 1);
                        else if (honor <= mercy && honor <= gen && honor < 2)  h.SetTraitLevel(DefaultTraits.Honor, honor + 1);
                        else if (gen < 2)                                       h.SetTraitLevel(DefaultTraits.Generosity, gen + 1);
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "The flame has changed you. A virtue has deepened in you without your noticing."));
                    }
                }
                catch { }
                _sanctuaryUseCount = 0;
            }

            // Steady the Line: extra healing while active
            if (_steadyLineUntilDay >= today && MobileParty.MainParty?.MemberRoster != null)
            {
                int healed = 0;
                foreach (var e in MobileParty.MainParty.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                    int heal = Math.Min(e.WoundedNumber, 2);
                    try { MobileParty.MainParty.MemberRoster.AddToCounts(e.Character, 0, false, -heal); healed += heal; } catch { }
                    if (healed >= 3) break;
                }
            }

            // ── Honourable + Merciful lords in sanctuary cities ──────────────
            try
            {
                foreach (var hero in Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero && h.CurrentSettlement != null
                             && HasSanctuary(h.CurrentSettlement) && NpcCanUseSanctuary(h))
                    .OrderBy(_ => _rng.Next()).Take(8))
                {
                    if (_rng.NextDouble() > 0.003) continue;

                    float mult = NpcSanctuaryMult(hero);
                    // Simulate ritual: 3 rounds, check if succeeds
                    bool success = SimulateNpcRitual(PrayerTargetLo, PrayerTargetHi, mult, 3);
                    if (!success) continue;

                    string city = hero.CurrentSettlement?.Name?.ToString() ?? "the sanctuary";
                    switch (_rng.Next(3))
                    {
                        case 0:
                            NpcHealPartyFull(hero.PartyBelongedTo);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} — miracle at the sanctuary in {city}. The wounded rose before sunrise.",
                                new Color(0.80f, 0.72f, 0.45f)));
                            break;
                        case 1:
                            NpcBoostMorale(hero.PartyBelongedTo);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} — miracle at the sanctuary in {city}. A renewal of spirit was reported.",
                                new Color(0.80f, 0.72f, 0.45f)));
                            break;
                        case 2:
                            NpcHealPartyPartial(hero.PartyBelongedTo, 0.20f);
                            NpcHealPartyPartial(hero.PartyBelongedTo, 0.20f);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} — ward from the sanctuary in {city}. Their injuries knit faster than they should.",
                                new Color(0.80f, 0.72f, 0.45f)));
                            break;
                    }
                }
            }
            catch { }

            // ── Temple lords: partial healing + Turn ──────────────────────────
            try
            {
                var temple = Kingdom.All.FirstOrDefault(k => k.StringId == TempleKingdomId && !k.IsEliminated);
                if (temple == null) return;
                foreach (var lord in Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero && h.Clan?.Kingdom == temple
                             && h.PartyBelongedTo?.IsActive == true))
                {
                    float mult = NpcSanctuaryMult(lord);
                    if (_rng.NextDouble() < 0.03 && SimulateNpcRitual(HealTargetLo, HealTargetHi, mult, 4))
                        try { NpcHealPartyPartial(lord.PartyBelongedTo, 0.30f); } catch { }
                    if (_rng.NextDouble() < 0.02 && SimulateNpcRitual(TurnTargetLo, TurnTargetHi, mult, 4))
                        try { NpcTurnAshenNear(lord.PartyBelongedTo); } catch { }
                }
            }
            catch { }
        }

        // Simulates rounds rounds of a ritual for an NPC. Returns true if accumulated >= target.
        private static bool SimulateNpcRitual(int targetLo, int targetHi, float mult, int rounds)
        {
            int target = targetLo + _rng.Next(Math.Max(1, targetHi - targetLo + 1));
            int acc = 0;
            for (int i = 0; i < rounds; i++) acc += RollRoundPoints(mult);
            return acc >= target;
        }

        // ── NPC effect helpers ─────────────────────────────────────────────────
        private static void NpcHealPartyFull(MobileParty party)
        {
            if (party?.MemberRoster == null) return;
            foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
            {
                if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                try { party.MemberRoster.AddToCounts(e.Character, 0, false, -e.WoundedNumber); } catch { }
            }
        }

        private static void NpcHealPartyPartial(MobileParty party, float fraction)
        {
            if (party?.MemberRoster == null) return;
            foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
            {
                if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                int heal = Math.Max(1, (int)(e.WoundedNumber * fraction));
                try { party.MemberRoster.AddToCounts(e.Character, 0, false, -heal); } catch { }
            }
        }

        private static void NpcBoostMorale(MobileParty party)
        {
            if (party == null) return;
            try { party.RecentEventsMorale += 20f; } catch { }
        }

        private static void NpcTurnAshenNear(MobileParty source)
        {
            if (source == null) return;
            float sx = 0f, sy = 0f;
            try { sx = source.GetPosition2D.x; sy = source.GetPosition2D.y; } catch { return; }
            const float rangeSquared = 100f * 100f;
            var target = MobileParty.All
                .Where(p => { if (!p.IsActive || p.MapFaction?.StringId != AshenKingdomId) return false;
                              float dx = p.GetPosition2D.x - sx, dy = p.GetPosition2D.y - sy;
                              return dx * dx + dy * dy < rangeSquared; })
                .OrderBy(p => { float dx = p.GetPosition2D.x - sx, dy = p.GetPosition2D.y - sy; return dx * dx + dy * dy; })
                .FirstOrDefault();
            if (target == null) return;
            int toWound = 5 + _rng.Next(6), w = 0;
            foreach (var e in target.MemberRoster.GetTroopRoster().ToList())
            {
                if (e.Character.IsHero) continue;
                int healthy = e.Number - e.WoundedNumber;
                int n = Math.Min(healthy, toWound - w); if (n <= 0) continue;
                try { target.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                if (w >= toWound) break;
            }
            try { target.RecentEventsMorale -= 20f; } catch { }
        }
    }
}
