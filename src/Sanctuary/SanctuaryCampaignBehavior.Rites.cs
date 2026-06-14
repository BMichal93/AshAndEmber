// =============================================================================
// ASH AND EMBER — SanctuaryCampaignBehavior.Rites.cs
// Rite math, cooldowns, alignment, and the shared meditation loop.
// Partial of SanctuaryCampaignBehavior (shared static state lives in SanctuaryCampaignBehavior.cs).
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
    public partial class SanctuaryCampaignBehavior
    {
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

        // XP for performing a rite — tending the flame is a study in command.
        private const float RiteLeadershipXp = 20f;

        private static void RecordLocationUse()
        {
            try { Hero.MainHero?.HeroDeveloper?.AddSkillXp(DefaultSkills.Leadership, RiteLeadershipXp); } catch { }
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
        // The flame recoils from whisper-heavy souls: at tier 2 (50+) −1 pt/round,
        // at tier 3 (75+) −2 — but never below the floor of 1.
        private static int RollRoundPoints(float mult)
        {
            int whisperDrag = 0;
            try { whisperDrag = Math.Max(0, MageKnowledge.WhisperTier - 1); } catch { }
            if (mult <= 0f) return 1; // 1 pt/round regardless; alignment accelerates yield
            int raw = 3 + _rng.Next(8); // 3–10
            return Math.Max(1, (int)Math.Round(raw * mult) - whisperDrag);
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
        // Each round the player chooses HOW to continue:
        //   steady  — normal roll.
        //   fervent — roll ×1.5, but one round in three the flame lashes out and
        //             the round cost is paid a second time.
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

            void Finish()
            {
                if (accumulated >= target) onSuccess();
                else
                {
                    if (!MageKnowledge.IsAshen) try { MageKnowledge.AddWhispers(2); } catch { }
                    onFailure();
                }
            }

            void DoRound(bool fervent)
            {
                string costNarr = applyRoundCost();
                if (fervent && _rng.Next(3) == 0)
                {
                    applyRoundCost();
                    costNarr += "\n\nThe flame surges past what you offered — it takes a second portion, uninvited.";
                }
                int pts = RollRoundPoints(mult);
                if (fervent) pts = Math.Max(1, (int)Math.Round(pts * 1.5f));
                accumulated += pts;
                round++;

                string hint = FlameProgressHint(accumulated, target);
                string header = $"{riteName} — Meditation ({round})";
                string body   = $"{costNarr}\n\n{hint}";

                try
                {
                    var options = new List<InquiryElement>
                    {
                        new InquiryElement("steady", "Continue — steady devotion", null, true,
                            "Meditate as taught. A measured offering, a measured answer."),
                        new InquiryElement("fervent", "Continue — fervent devotion", null, true,
                            "Pour yourself into the flame. Progress builds half again as fast — but one round in three, the flame takes a second helping of your offering."),
                        new InquiryElement("stop", "Step back — claim what the flame offers", null, true,
                            "End the meditation. If the flame has been given enough, the prayer fires; if not, the cost is lost."),
                    };
                    MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                        header, body, options, false, 1, 1, "Decide", "",
                        chosen =>
                        {
                            string pick = chosen?[0]?.Identifier as string ?? "stop";
                            if      (pick == "steady")  DoRound(false);
                            else if (pick == "fervent") DoRound(true);
                            else Finish();
                        },
                        null, "", false), false, true);
                }
                catch
                {
                    // Fallback: resolve immediately
                    Finish();
                }
            }

            DoRound(false);
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
    }
}
