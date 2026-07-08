// =============================================================================
// ASH AND EMBER — GreatAwakening/GreatAwakeningQuestLog.cs
// Journal entry for the Great Awakening. One log serves both mirror paths —
// which text applies is decided by whichever branch actually completes.
// Registered in SaveDefiner.cs (id 13) so a save taken mid-quest can write.
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public sealed class GreatAwakeningQuestLog : QuestBase
    {
        private static GreatAwakeningQuestLog _questLog;

        public GreatAwakeningQuestLog()
            : base("grawk_the_great_awakening", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("The Great Awakening");
        public override bool IsRemainingTimeHidden => true;

        protected override void InitializeQuestOnGameLoad() { _questLog = this; }
        protected override void RegisterEvents() { }
        protected override void SetDialogs() { }

        private JournalLog _objProgress;

        internal static void Start()
        {
            try
            {
                var log = new GreatAwakeningQuestLog();
                log.StartQuest();
                _questLog = log;

                bool duneborn = GreatAwakeningCampaignBehavior.IsPlayerOnDunebornPath();
                log.AddLog(new TextObject(duneborn
                    ? "You have heard it from the Sheikh's own mouth: Duneborn has reached beyond the Sands and " +
                      "touched something that answered. Ten thousand lives, poured out at the Dark Altar, and it " +
                      "will stand on Calradian soil and rule it. You mean to see it done."
                    : "You have heard it from the Sheikh's own mouth: Duneborn means to drag something ancient and " +
                      "dark out of the deep desert on ten thousand sacrificed lives. If it arrives, nothing that " +
                      "follows will be undone. Duneborn's kingdom must fall before the count is paid in full."));

                log._objProgress = log.AddDiscreteLog(
                    new TextObject(duneborn
                        ? "Feed the Dark Altar until the Great Summoning is complete."
                        : "Destroy Duneborn's kingdom before the Great Summoning completes."),
                    new TextObject("Prisoners Sacrificed"), 0, GreatAwakeningMath.PrisonerTarget, null, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void UpdateProgress(int sacrificed, int target)
        {
            try { _questLog?._objProgress?.UpdateCurrentProgress(System.Math.Min(sacrificed, target)); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void CompleteSummoningControlled()
        {
            try
            {
                _questLog?.AddLog(new TextObject(
                    "The count is paid. The Dark Altar drinks the last of it, and something vast steps out of the " +
                    "space behind the stone — The Great Other has come, and it stands at Duneborn's side."));
                _questLog?.CompleteQuestWithSuccess();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void CompleteSummoningUncontrolled()
        {
            try
            {
                _questLog?.AddLog(new TextObject(
                    "The count is paid — but whatever answered the Dark Altar answers to no one. The Great Other " +
                    "has come, and it belongs to nothing and no one, least of all Duneborn."));
                _questLog?.CompleteQuestWithFail();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void CompleteOppositionWon()
        {
            try
            {
                _questLog?.AddLog(new TextObject(
                    "Duneborn's kingdom is broken and gone. Whatever waited beyond the Sands waits still — the " +
                    "count was never paid, and never will be."));
                _questLog?.CompleteQuestWithSuccess();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
