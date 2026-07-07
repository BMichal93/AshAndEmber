// =============================================================================
// ASH AND EMBER — Soldier/SoldierServiceCampaignBehavior.Dialogue.cs
// Dialogue for taking (and ending) service as a sworn sword. Hooks into
// "lord_pretalk" for any eligible commander. Partial of
// SoldierServiceCampaignBehavior (state and tick logic live in the base file).
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class SoldierServiceCampaignBehavior
    {
        // Priority 105 — sits just above vanilla lord_pretalk lines (and the
        // clan-orders line at 101) so the option reads near the top.
        private const int P = 105;

        private static void RegisterDialogue(CampaignGameStarter starter)
        {
            // ── Offer service ─────────────────────────────────────────────────
            try
            {
                starter.AddPlayerLine(
                    "soldier_join_open", "lord_pretalk", "soldier_join_terms",
                    "I would take your coin and march under your banner, until this war is done.",
                    CondCanOffer, OnOfferChosen, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddDialogLine(
                    "soldier_terms_npc", "soldier_join_terms", "soldier_terms_choose",
                    "The realm bleeds and we've need of blades. {SOLDIER_PAY}{GOLD_ICON} the week from my coffers, "
                        + "so long as you ride where I ride. How long will you serve?",
                    null, null, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddPlayerLine(
                    "soldier_term_0", "soldier_terms_choose", "soldier_sealed",
                    "A season — {SOLDIER_T0} days.",
                    null, () => DoJoin(0), P);
                starter.AddPlayerLine(
                    "soldier_term_1", "soldier_terms_choose", "soldier_sealed",
                    "A half-year — {SOLDIER_T1} days.",
                    null, () => DoJoin(1), P);
                starter.AddPlayerLine(
                    "soldier_term_2", "soldier_terms_choose", "soldier_sealed",
                    "A campaign year — {SOLDIER_T2} days.",
                    null, () => DoJoin(2), P);
                starter.AddPlayerLine(
                    "soldier_term_no", "soldier_terms_choose", "lord_pretalk",
                    "On second thought — not yet.",
                    null, null, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddDialogLine(
                    "soldier_sealed_npc", "soldier_sealed", "close_window",
                    "Then it is sealed. Stay close, soldier, and my coin is yours.",
                    null, null, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── End service (talking to your own commander) ───────────────────
            try
            {
                starter.AddPlayerLine(
                    "soldier_disc_open", "lord_pretalk", "soldier_disc",
                    "About my service under your banner...",
                    CondIsMyCommander, OnDischargeChosen, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddDialogLine(
                    "soldier_disc_npc", "soldier_disc", "soldier_disc_choose",
                    "{SOLDIER_DISC_TEXT}",
                    null, null, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Term already served — clean release.
            try
            {
                starter.AddPlayerLine(
                    "soldier_disc_done", "soldier_disc_choose", "soldier_disc_bye",
                    "My term is served. I'll take my leave, and my due.",
                    CondTermServed, () => HonourableRelease(true), P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Term unfinished — leaving now is desertion.
            try
            {
                starter.AddPlayerLine(
                    "soldier_disc_desert", "soldier_disc_choose", "soldier_disc_bye",
                    "I'm done. I break the oath and ride on.",
                    CondTermNotServed, () => Desert(), P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddPlayerLine(
                    "soldier_disc_stay", "soldier_disc_choose", "lord_pretalk",
                    "Nothing. I keep my word.",
                    null, null, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddDialogLine(
                    "soldier_disc_bye_npc", "soldier_disc_bye", "close_window",
                    "Go, then.",
                    null, null, P);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Conditions ────────────────────────────────────────────────────────
        private static bool CondCanOffer()
        {
            try { return CanOfferService(Hero.OneToOneConversationHero); }
            catch { return false; }
        }

        private static bool CondIsMyCommander()
        {
            try { return InService && Hero.OneToOneConversationHero?.StringId == _lordId; }
            catch { return false; }
        }

        private static bool CondTermServed()
        {
            try { return CondIsMyCommander() && DaysRemaining() <= 0.0; }
            catch { return false; }
        }

        private static bool CondTermNotServed()
        {
            try { return CondIsMyCommander() && DaysRemaining() > 0.0; }
            catch { return false; }
        }

        // ── Consequences / text ───────────────────────────────────────────────
        private static void OnOfferChosen()
        {
            try
            {
                int pay = SoldierServiceMath.WeeklyPay(MobileParty.MainParty?.TotalWage ?? 0, ClanTier());
                MBTextManager.SetTextVariable("SOLDIER_PAY", pay);
                MBTextManager.SetTextVariable("SOLDIER_T0", SoldierServiceMath.TermDays[0]);
                MBTextManager.SetTextVariable("SOLDIER_T1", SoldierServiceMath.TermDays[1]);
                MBTextManager.SetTextVariable("SOLDIER_T2", SoldierServiceMath.TermDays[2]);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void DoJoin(int termIndex)
        {
            try
            {
                var lord = Hero.OneToOneConversationHero;
                if (lord == null) return;
                int days = SoldierServiceMath.TermDays[
                    Math.Max(0, Math.Min(SoldierServiceMath.TermDays.Length - 1, termIndex))];
                JoinService(lord, days);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void OnDischargeChosen()
        {
            try
            {
                double rem = DaysRemaining();
                string text;
                if (rem <= 0.0)
                {
                    text = "You've served your term in full. Say the word and you're free to go, with my thanks.";
                }
                else
                {
                    int crime = SoldierServiceMath.DesertionCrime(rem, _termDays);
                    int rel   = SoldierServiceMath.DesertionRelationLoss(rem, _termDays);
                    text = $"You still owe me {Math.Max(0, (int)Math.Ceiling(rem))} days. Ride off before that "
                         + $"and you're a deserter — the realm will hunt you (+{crime} crime), and I'll not forget it ({rel} between us).";
                }
                MBTextManager.SetTextVariable("SOLDIER_DISC_TEXT", text);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
