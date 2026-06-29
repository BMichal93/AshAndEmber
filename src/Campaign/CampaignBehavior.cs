// =============================================================================
// LIFE & DEATH MAGIC — CampaignBehavior.cs
// New game prompt, inheritance, population regulation, aging, save/load.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public partial class MagicCampaignBehavior : CampaignBehaviorBase
    {
        private bool _selectionDone;
        private int  _prisonerCountSnapshot = -1;
        private int  _dayCounter            = 0;
        private int  _reapRaidCooldown      = 0;
        private int  _lordAnnounceCountdown = -1;
        private bool _lordAnnouncementDone  = false;
        private static readonly Random _rng = new Random();
        // Guard against HeroKilledEvent firing multiple times for the same execution
        // (Bannerlord can fire the event twice under certain load conditions).
        private readonly HashSet<string> _executedLordIds = new HashSet<string>();
        // Tracks how many days each Ashen lord has been in captivity (StringId → days).
        // Ashen lords auto-escape after 3 days — the cold does not yield to chains.
        private readonly Dictionary<string, int> _ashenCaptiveDays = new Dictionary<string, int>();
        private bool _pendingAppearanceRefresh = false;

        private static readonly string[] _premonitions =
        {
            "The fire in you whispers tonight — something distant is ending.",
            "On the road, you pass the ruins of a great pyre. The air still carries old smoke. Something in you recognises it.",
            "You wake with the taste of ash on your tongue. The inner fire is restless.",
            "Your shadow moves a half-step behind you. The fire inside is watching something you cannot see.",
            "You watch a forge-fire die to coals. For a moment, you understand exactly what you are.",
            "A child in the village stares at your hands as you pass. She sees something there that you do not show others.",
            "The fire does not sleep when you do. You feel it turning in its sleep, searching.",
            "You smell smoke where there is none. An old instinct — the fire recognising itself in the distance.",
            "Rain falls, but where you stand the ground stays dry. You notice. You always notice.",
            "The torches in the hall burn a shade too orange tonight. The innkeeper doesn't see it. You do.",
            "Someone is watching you from across the market — no, not watching. Sensing. You feel it the same way they do.",
            "A wound on your hand heals overnight. You have stopped being surprised by this.",
            "The dying man reaches for your hand. You let him. The fire passes between you — not much, but some.",
            "You dream of a battlefield long before it happens. The details are wrong. The ending is not.",
            "An old mage-lord rides past on the road. Neither of you slows. Both of you know.",
            "Animals grow quiet when you enter the stable. Not frightened — still. As if listening.",
            "You stand at the edge of a river and the water pulls slightly toward you. You step back.",
            "The fire shows you a face tonight. Someone you haven't met yet. Or someone you have, changed by time.",
            "You press your palm against cold stone and feel it remember warmth. Every stone remembers something.",
            "A soldier dies in the battle beside you. For a moment, his fire is visible — a last guttering. Then nothing.",
            "The stars are wrong tonight. Not the positions — the light. Too old. Too far. The fire knows things you don't, and it is not telling you.",
            "You catch yourself speaking to the campfire, asking nothing in particular. It doesn't answer. But it listens.",
        };

        public override void RegisterEvents()
        {
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnMissionEnded);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.HeroCreated.AddNonSerializedListener(this, OnHeroCreated);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, OnCompanionAdded);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.MobilePartyCreated.AddNonSerializedListener(this, OnMobilePartyCreated);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
        }

    }
}
