// =============================================================================
// ASH AND EMBER — NorthmenStones/NorthmenStonesQuestLog.cs
// Journal entry for The Bonefire Circle. Registered in SaveDefiner.cs (id 14)
// so a save taken mid-quest can write.
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public sealed class NorthmenStonesQuestLog : QuestBase
    {
        private static NorthmenStonesQuestLog _questLog;

        public NorthmenStonesQuestLog()
            : base("nstones_the_bonefire_circle", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("The Bonefire Circle");
        // Exempts the quest from the engine's cancel-on-load sweep (see GreatAwakeningQuestLog).
        public override string SpecialQuestType => "AshAndEmberQuest";
        public override bool IsRemainingTimeHidden => true;

        protected override void InitializeQuestOnGameLoad() { _questLog = this; }
        protected override void RegisterEvents() { }
        protected override void SetDialogs() { }

        private JournalLog _objProgress;

        internal static void Start()
        {
            try
            {
                var log = new NorthmenStonesQuestLog();
                log.StartQuest();
                _questLog = log;

                log.AddLog(new TextObject(
                    "The Ruler of the Northmen has told you the seers' plan: raise standing stones at " +
                    "Varcheg and bind them with Fire, so nothing Ashen crosses there living again. Iron, " +
                    "hardwood, tools, silver, coin for the masons — and Kindled, bound and given up, one " +
                    "of every kind the Forest Clans' sacred sites can wake. The Forest Clans must stand " +
                    "truly allied with the Northmen when the working closes, and Varcheg must still be " +
                    "theirs. You have agreed to see it done."));

                log._objProgress = log.AddDiscreteLog(
                    new TextObject("Donate materials for the standing stones at Varcheg."),
                    new TextObject("The Working"), 0, 100, null, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void UpdateProgress(int blendedPercent)
        {
            try { _questLog?._objProgress?.UpdateCurrentProgress(System.Math.Min(blendedPercent, 100)); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void LogInvasion(string text)
        {
            try { _questLog?.AddLog(new TextObject(text)); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void CompleteDisagree()
        {
            try
            {
                _questLog?.AddLog(new TextObject(
                    "You refused the seers the final spark. The stones stand unfinished and cold. The " +
                    "Northmen will not forget who turned from them at the last."));
                _questLog?.CompleteQuestWithFail();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void CompleteSelfSacrifice()
        {
            try
            {
                _questLog?.AddLog(new TextObject(
                    "You gave your own fire to the working. The Bonefire Circle stands at Varcheg, and " +
                    "burns still, though you do not live to see it."));
                _questLog?.CompleteQuestWithSuccess();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void CompleteChildSacrifice()
        {
            try
            {
                _questLog?.AddLog(new TextObject(
                    "Your own blood fed the working instead of yours. The Bonefire Circle stands at " +
                    "Varcheg. Not everyone in your hall has forgiven you for it."));
                _questLog?.CompleteQuestWithSuccess();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
