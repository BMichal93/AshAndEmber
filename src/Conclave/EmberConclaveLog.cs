// =============================================================================
// ASH AND EMBER — EmberConclaveLog.cs
// Journal quest logs for the Ember Conclave quest system.
//   EmberConclaveMainLog      — the full arc; stays open until culmination
//   EmberConclaveEliminateLog — "The First Binding" mission
//   EmberConclaveVisitLog     — "The Sealed Accord" mission
//   EmberConclaveProtectLog   — "The Kindling Pact" mission
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    // ── Shared base for mission logs ───────────────────────────────────────────
    public abstract class EmberConclaveMissionLogBase : QuestBase
    {
        protected EmberConclaveMissionLogBase(string questId)
            : base(questId, Hero.MainHero, CampaignTime.Never, 0) { }

        public override bool IsRemainingTimeHidden => true;
        protected override void RegisterEvents() { }
        protected override void SetDialogs() { }

        internal abstract void LogOpened(string targetName, int days);
        internal void LogAccepted() =>
            AddLog(new TextObject("You have accepted the task. The Conclave will be watching for results."));
        internal void LogDeclined() =>
            AddLog(new TextObject("You declined. The Conclave noted your answer."));
        internal void LogSuccess() =>
            AddLog(new TextObject("The task is complete. The Conclave has taken note."));
        internal void LogFailed(string reason) =>
            AddLog(new TextObject($"The task went unfinished. {reason}"));
        internal void CompleteSuccess() { try { CompleteQuestWithSuccess(); } catch { } }
        internal void CompleteFail()    { try { CompleteQuestWithFail();    } catch { } }
    }

    // ── Main arc log ───────────────────────────────────────────────────────────
    public sealed class EmberConclaveMainLog : QuestBase
    {
        public EmberConclaveMainLog()
            : base("ec_ember_conclave_main", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("The Ember Conclave");
        public override bool IsRemainingTimeHidden => true;

        protected override void InitializeQuestOnGameLoad()
        {
            EmberConclaveSystem._mainLog = this;
        }
        protected override void RegisterEvents() { }
        protected override void SetDialogs() { }

        internal void LogOpened() =>
            AddLog(new TextObject(
                "Somewhere in Calradia, a circle of mage lords has formed in secret. " +
                "They call themselves the Ember Conclave. They believe the Ashen can be controlled."));

        internal void LogFirstContact() =>
            AddLog(new TextObject(
                "The Ember Conclave has made contact. They believe they have discovered a way to direct the cold — " +
                "to use the Ashen as a weapon rather than resist them. They are seeking allies."));

        internal void LogPlayerAllied() =>
            AddLog(new TextObject(
                "You have pledged to hear the Conclave out. Their design is ambitious. " +
                "Whether it is possible is another question."));

        internal void LogPlayerOpposed() =>
            AddLog(new TextObject(
                "You have made your position clear. The Conclave are either deluded or something worse. " +
                "You will be watching them."));

        internal void LogPlayerIgnored() =>
            AddLog(new TextObject(
                "You burned the letter. Whatever the Conclave is building, it continues without your answer. " +
                "Silence is its own kind of response."));

        internal void LogRisingPhase() =>
            AddLog(new TextObject(
                "The Conclave has grown in strength and confidence. Their preparations are advancing. " +
                "They are beginning to move pieces into place."));

        internal void LogMemberLost(string name) =>
            AddLog(new TextObject(
                $"A Conclave member is dead — {name}. The circle grows smaller. " +
                $"Their power weakens with each ember lost."));

        internal void LogPuppetChosen(string name) =>
            AddLog(new TextObject(
                $"The Conclave has chosen their candidate: {name}. " +
                $"He is to be the vessel through which fire and cold are reconciled. " +
                $"He does not yet know what that truly means."));

        internal void LogCorruptionWarning(string warning) =>
            AddLog(new TextObject(warning));

        internal void LogAllyEnding() =>
            AddLog(new TextObject(
                "The binding worked. The throne stands occupied. The Conclave believed they had mastered the cold. " +
                "What sits in the high seat is not their candidate. " +
                "The cold simply found a better container than they intended to provide."));

        internal void LogNeutralEnding() =>
            AddLog(new TextObject(
                "The Ember Conclave is gone. Their candidate is Ashen — hollowed out and inhabited by something " +
                "that chose him long before their rite. The remaining members did not return from the hall. " +
                "The cold does not negotiate. It waits, and then it takes."));

        internal void LogDefeat() =>
            AddLog(new TextObject(
                "The Ember Conclave has collapsed. Too few embers remain to sustain the circle. " +
                "Their records survive them — the full design is readable now. " +
                "What they planned to control would have consumed them regardless."));

        internal void CompleteSuccess() { try { CompleteQuestWithSuccess(); } catch { } }
        internal void CompleteFail()    { try { CompleteQuestWithFail();    } catch { } }
    }

    // ── Mission: The First Binding (eliminate a lord) ──────────────────────────
    public sealed class EmberConclaveEliminateLog : EmberConclaveMissionLogBase
    {
        public EmberConclaveEliminateLog()
            : base("ec_conclave_eliminate") { }

        public override TextObject Title => new TextObject("The First Binding");

        protected override void InitializeQuestOnGameLoad()
        {
            EmberConclaveSystem._eliminateLog = this;
        }

        internal override void LogOpened(string targetName, int days) =>
            AddLog(new TextObject(
                $"The Conclave has asked you to eliminate {targetName} — a lord who has become an obstacle " +
                $"to their preparations. He has spoken to too many ears. " +
                $"You have {days} days."));
    }

    // ── Mission: The Sealed Accord (visit a settlement) ────────────────────────
    public sealed class EmberConclaveVisitLog : EmberConclaveMissionLogBase
    {
        public EmberConclaveVisitLog()
            : base("ec_conclave_visit") { }

        public override TextObject Title => new TextObject("The Sealed Accord");

        protected override void InitializeQuestOnGameLoad()
        {
            EmberConclaveSystem._visitLog = this;
        }

        internal override void LogOpened(string targetName, int days) =>
            AddLog(new TextObject(
                $"The Conclave has asked you to attend a gathering at {targetName}. " +
                $"There are things they want you to witness before the design advances further. " +
                $"You have {days} days. Ask for no one by name when you arrive."));
    }

    // ── Mission: The Kindling Pact (protect the puppet candidate) ──────────────
    public sealed class EmberConclaveProtectLog : EmberConclaveMissionLogBase
    {
        public EmberConclaveProtectLog()
            : base("ec_conclave_protect") { }

        public override TextObject Title => new TextObject("The Kindling Pact");

        protected override void InitializeQuestOnGameLoad()
        {
            EmberConclaveSystem._protectLog = this;
        }

        internal override void LogOpened(string targetName, int days) =>
            AddLog(new TextObject(
                $"The Conclave has asked you to ensure {targetName} survives long enough to fulfil his role. " +
                $"He has been noticed by those who would rather see the design fail. " +
                $"Keep him alive for {days} days."));
    }
}
