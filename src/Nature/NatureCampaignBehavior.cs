// =============================================================================
// ASH AND EMBER — Nature/NatureCampaignBehavior.cs
// Campaign wiring for The Living Ember system.
//
// Hermits:
//   Gwydion the Root-Listener  — Battania (any town, random)   — teaches Living Root
//   Birna of the Still Water   — Strugia  (any town, random)   — teaches Still Draw
//   Bekh the Open Hand         — Khuzait  (any town, random)   — teaches Open Grip
//   Tiryn of the High Root     — Marunath (village, always)    — teaches Deep Earth
//   Faruk the Patient          — Aserai   (any village, random) — teaches Dawn Call
//
// Gwydion, Birna, Bekh, and Faruk appear when the player enters a qualifying
// settlement with ≥100 renown and is attuned to the living world (25% chance).
// Tiryn is always available via the village menu at Marunath — no randomness.
// Each hermit is a one-time encounter; after teaching they are gone.
//
// Unit seeding:
//   Battanian Forest Listener  — melee troop, nature seer, rare in Battanian warbands
//   Strugian Storm-Reader      — ranged troop, nature seer, rare in Strugian warbands
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public class NatureCampaignBehavior : CampaignBehaviorBase
    {
        private static readonly Random _rng = new Random();

        // Settlement cooldown so the hermit doesn't spam on every enter.
        private readonly Dictionary<string, int> _hermitCooldowns = new Dictionary<string, int>();

        // Campaign-charge tracking: standing still for a few hours fills a charge.
        private Vec2 _lastHourPos = Vec2.Zero;
        private bool _haveHourPos = false;
        private int  _stillHours  = 0;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        // Standing still on the map fills a charge over a few hours; movement resets
        // the count. Cast it through the litany window (Shift+X). Dawn Call hastens it.
        private void OnHourlyTick()
        {
            try
            {
                if (!NatureKnowledge.IsAttuned) return;
                var party = MobileParty.MainParty;
                if (party == null) return;

                Vec2 pos = party.GetPosition2D;
                if (_haveHourPos && (pos - _lastHourPos).Length < 0.05f) _stillHours++;
                else _stillHours = 0;
                _lastHourPos = pos;
                _haveHourPos = true;

                int needed = TalentSystem.Has(TalentId.NatureDawnCall)
                    ? Math.Max(1, NatureMath.ChargeCampaignHours - 1)
                    : NatureMath.ChargeCampaignHours;

                if (_stillHours >= needed && !NatureCharge.IsFull)
                {
                    if (!NatureCharge.HasSelection)
                    {
                        // Nothing chosen to draw — nudge the player toward the litany.
                        InformationManager.DisplayMessage(new InformationMessage(
                            "You have stood still long enough to draw — but you have not chosen an element. " +
                            "Open the litany (Shift+X) to choose what to call from the land.",
                            new Color(0.4f, 0.75f, 0.4f)));
                        _stillHours = 0;
                        return;
                    }
                    if (NatureCharge.GrantCampaignCharge(NatureCharge.SelectedElement))
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"The land fills your hands with {NatureMath.ElementName(NatureCharge.SelectedElement)} — " +
                            "a charge waits. Open the litany (Shift+X) to spend it.",
                            new Color(0.4f, 0.75f, 0.4f)));
                        // Drawing from exhausted country bites back on the march, too.
                        if (NatureCharge.LastGatherOutcome.Soured)
                            NatureBacklash.ApplyMap(MobileParty.MainParty, Hero.MainHero, announce: true);
                    }
                    _stillHours = 0;
                }
            }
            catch { }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try { RegisterNatureMenus(starter); } catch { }
            try { MagicTeacherDialogue.Register(starter); } catch { }
        }

        public override void SyncData(IDataStore store)
        {
            try { NatureKnowledge.Save(store);              } catch { }
            try { NatureSeerRegistry.Save(store);           } catch { }
            try { LivingEnergy.Save(store);                 } catch { }
        }

        public static void ResetForNewGame()
        {
            NatureKnowledge.ResetForNewGame();
            NatureSeerRegistry.ResetForNewGame();
            LivingEnergy.ResetForNewGame();
        }

        public static void EstablishForNewCampaign()
        {
            NatureSeerRegistry.SeedInitialLords();
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            // Campaign charges come from standing still (see OnHourlyTick).

            // The living world mends a little each day it is left in peace.
            try { LivingEnergy.DailyRegen(); } catch { }

            // NPC Nature Seers draw on the land once a day when the chance fires.
            try
            {
                NatureElement[] allEls = { NatureElement.Wind, NatureElement.Earth,
                                           NatureElement.Water, NatureElement.Storm };
                foreach (Hero hero in Hero.AllAliveHeroes.ToList())
                {
                    if (hero == Hero.MainHero || hero.PartyBelongedTo == null) continue;
                    if (!NatureSeerRegistry.IsNatureSeer(hero)) continue;
                    if (_rng.NextDouble() >= NatureMath.NpcDailyUseChance()) continue;

                    // Prefer Earth Root-Mend when their party has many wounded.
                    int wounded = 0;
                    try { wounded = hero.PartyBelongedTo.MemberRoster
                            .GetTroopRoster().Sum(e => e.WoundedNumber); } catch { }
                    NatureElement el = allEls[_rng.Next(allEls.Length)];
                    NaturePower power = wounded > 3
                        ? NatureMath.SupportPower(NatureElement.Earth)
                        : NatureMath.SupportPower(el);

                    // An NPC seer's draw spends the living energy where they stand,
                    // silently (it is far from the player). A drained land may sour.
                    try
                    {
                        var drawEl = NatureMath.ElementOf(power);
                        var outcome = LivingEnergy.DrawNature(hero.PartyBelongedTo.GetPosition2D, drawEl, announce: false);
                        if (outcome.Soured)
                            NatureBacklash.ApplyMap(hero.PartyBelongedTo, hero, announce: false);
                    }
                    catch { }

                    string msg = NatureEffects.ApplyCampaignEffect(power, hero.PartyBelongedTo);
                    if (!string.IsNullOrEmpty(msg) && _rng.NextDouble() < 0.20)
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{hero.Name}: {msg}", new Color(0.35f, 0.75f, 0.35f)));
                }
            }
            catch { }

            // Tick hermit cooldowns
            foreach (string key in _hermitCooldowns.Keys.ToList())
            {
                _hermitCooldowns[key]--;
                if (_hermitCooldowns[key] <= 0)
                    _hermitCooldowns.Remove(key);
            }
        }

        // ── Settlement entered ─────────────────────────────────────────────────
        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (party != MobileParty.MainParty) return;
            if (!NatureKnowledge.IsAttuned) return;

            try { CheckHermitEncounter(settlement); } catch { }
        }

        private void CheckHermitEncounter(Settlement settlement)
        {
            if (settlement == null) return;
            if (_hermitCooldowns.ContainsKey(settlement.StringId)) return;
            if (MageKnowledge._deferredInquiry != null) return;

            // Renown gate — the hermits do not teach the untested
            float renown = 0f;
            try { renown = TaleWorlds.CampaignSystem.Hero.MainHero?.Clan?.Renown ?? 0f; } catch { }
            if (renown < 100f) return;

            string culture = settlement.Culture?.StringId ?? "";

            if (culture == "battania" && !NatureKnowledge.FoundBattaniaHermit
                && settlement.IsTown && _rng.Next(4) == 0)
            {
                _hermitCooldowns[settlement.StringId] = 3;
                MageKnowledge._deferredInquiry = ShowGwydion;
                return;
            }

            if (culture == "sturgia" && !NatureKnowledge.FoundStrugiaHermit
                && settlement.IsTown && _rng.Next(4) == 0)
            {
                _hermitCooldowns[settlement.StringId] = 3;
                MageKnowledge._deferredInquiry = ShowBirna;
                return;
            }

            if (culture == "khuzait" && !NatureKnowledge.FoundKhuzaitHermit
                && settlement.IsTown && _rng.Next(4) == 0)
            {
                _hermitCooldowns[settlement.StringId] = 3;
                MageKnowledge._deferredInquiry = ShowBekh;
                return;
            }

            // Faruk the Patient — Aserai villages (not towns) — teaches Dawn Call
            if (culture == "aserai" && !NatureKnowledge.FoundFringeHermit
                && settlement.IsVillage && _rng.Next(4) == 0)
            {
                _hermitCooldowns[settlement.StringId] = 3;
                MageKnowledge._deferredInquiry = ShowFaruk;
                return;
            }
        }

        // ── Gwydion — Battania — Living Root ──────────────────────────────────
        private static void ShowGwydion()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Gwydion the Root-Listener",
                "He is sitting at the edge of the market square with his back against a stripped tree, " +
                "bark still white from the axe. He does not look at you directly. He watches your hands.\n\n" +
                "\"You hear it,\" he says — not a question. \"Most carry it past without noticing. " +
                "The root-voice. The slow one, the patient one, the one that does not ask to be heard.\"\n\n" +
                "He turns something small over in his fingers. A seed, or a stone — you cannot tell which.\n\n" +
                "\"Listen longer. The land gives more when you are not in a hurry to take. " +
                "Reach twice before you let go once, and hold what you have until you mean to use it.\"\n\n" +
                "He stands, tucks the thing away, and is gone before you turn around.",
                true, false,
                "I listen.",
                null,
                () =>
                {
                    NatureKnowledge.RecordHermitFound(NatureHermitId.Battania);
                    TalentSystem.GrantFree(TalentId.NatureLivingRoot, Hero.MainHero);
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Gwydion's teaching settles in you. The root-voice speaks twice now, " +
                        "and the charges linger in both hands.",
                        new Color(0.35f, 0.75f, 0.35f)));
                },
                null), true, false);
        }

        // ── Birna — Strugia — Still Draw ──────────────────────────────────────
        private static void ShowBirna()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Birna of the Still Water",
                "She is washing something in the river by the mill, or appears to be. " +
                "You notice her hands are dry.\n\n" +
                "\"The river costs more than it should,\" she says, without looking up. " +
                "\"Because you are moving when you draw from it. " +
                "The water wants you still. Like ice, before it remembers how to flow.\"\n\n" +
                "She finally lifts her head. Her eyes are the colour of a lake before winter.\n\n" +
                "\"Stop moving. Feel the ground under you. Let the current come to you. " +
                "When you are still, the draw costs nothing — the land does not charge what it offers freely.\"\n\n" +
                "She goes back to her washing. Or whatever it was she was doing.",
                true, false,
                "I will learn to be still.",
                null,
                () =>
                {
                    NatureKnowledge.RecordHermitFound(NatureHermitId.Strugia);
                    TalentSystem.GrantFree(TalentId.NatureStillDraw, Hero.MainHero);
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Birna's teaching slows something in you. When you stand still, " +
                        "the draw costs no blood.",
                        new Color(0.35f, 0.75f, 0.35f)));
                },
                null), true, false);
        }

        // ── Bekh — Khuzait — Open Grip ────────────────────────────────────────
        private static void ShowBekh()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Bekh the Open Hand",
                "He is sleeping — or looks like it — in the shade of a market awning, " +
                "hat pulled over his face. He speaks before you reach him.\n\n" +
                "\"The steppe does not take back a gift,\" he says. " +
                "\"The wind does not call your name and change its mind. " +
                "What the land gives, it means you to have.\"\n\n" +
                "He pushes the hat up. He has the kind of eyes that have seen too much horizon.\n\n" +
                "\"You hold it too tightly. That is why it slips. Open the hand — " +
                "not wide, just open — and what the world gives you will stay as long as you carry it.\"\n\n" +
                "He drops the hat back. When you look again, he is gone. " +
                "His bedroll is there. He is not.",
                true, false,
                "I will open my hand.",
                null,
                () =>
                {
                    NatureKnowledge.RecordHermitFound(NatureHermitId.Khuzait);
                    TalentSystem.GrantFree(TalentId.NatureOpenGrip, Hero.MainHero);
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Bekh's teaching opens something in your grip. " +
                        "What the land gives, it gives to stay.",
                        new Color(0.35f, 0.75f, 0.35f)));
                },
                null), true, false);
        }

        // ── Retreat menu — Tiryn of the High Root (Marunath) ─────────────────
        // Always visible as a village menu option; no randomness — the retreat is
        // there for anyone attuned enough to find it.
        private const string RetreatVillageName = "Marunath";

        private static void RegisterNatureMenus(CampaignGameStarter starter)
        {
            // Entry option on the village menu
            try
            {
                starter.AddGameMenuOption("village", "nature_retreat_entry", "{NATURE_RETREAT_LABEL}",
                    args =>
                    {
                        try
                        {
                            var s = Settlement.CurrentSettlement;
                            if (s == null || !s.IsVillage) return false;
                            string sName = null;
                            try { sName = s.Name?.ToString()?.Trim(); } catch { }
                            if (!string.Equals(sName, RetreatVillageName,
                                    System.StringComparison.OrdinalIgnoreCase)) return false;
                            if (!NatureKnowledge.IsAttuned) return false;

                            bool taught = NatureKnowledge.HermitFound(NatureHermitId.Retreat);
                            MBTextManager.SetTextVariable("NATURE_RETREAT_LABEL",
                                taught ? "The mountain retreat [Tiryn has taught you what she knows]"
                                       : "Seek the hermit at the mountain retreat");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }
                            args.IsEnabled = !taught;
                            return true;
                        }
                        catch { return false; }
                    },
                    args =>
                    {
                        try { GameMenu.SwitchToMenu("nature_retreat_menu"); } catch { }
                    },
                    false, -1, false);
            }
            catch { }

            // Retreat submenu
            try
            {
                starter.AddGameMenu("nature_retreat_menu",
                    "A cairn of flat stones marks the high edge of the village, older than the " +
                    "settlement below it. Tiryn sits beside it — she is small and wind-dried, " +
                    "her hair braided with heather and old root. She does not seem surprised to see you.\n\n" +
                    "\"You listen,\" she says. \"Come, then.\"",
                    args => { });
            }
            catch { }

            try
            {
                starter.AddGameMenuOption("nature_retreat_menu", "nature_retreat_speak",
                    "I am listening.",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Continue; } catch { }
                        return true;
                    },
                    args =>
                    {
                        try
                        {
                            if (MageKnowledge._deferredInquiry == null)
                                MageKnowledge._deferredInquiry = ShowTiryn;
                        }
                        catch { }
                        try { GameMenu.SwitchToMenu("village"); } catch { }
                    },
                    false, -1, false);
            }
            catch { }

            try
            {
                starter.AddGameMenuOption("nature_retreat_menu", "nature_retreat_leave",
                    "Leave.",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { }
                        return true;
                    },
                    args =>
                    {
                        try { GameMenu.SwitchToMenu("village"); } catch { }
                    },
                    false, -1, false);
            }
            catch { }
        }

        // ── Tiryn — Marunath Retreat — Deep Earth ─────────────────────────────
        private static void ShowTiryn()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Tiryn of the High Root",
                "She has been sitting at this cairn for so long that the stones have started to lean " +
                "toward her. She does not introduce herself. She has been waiting for someone like you.\n\n" +
                "\"The wall does not stop the root-voice,\" she says, picking a flake of lichen " +
                "from the stone beside her. \"People think stone is dead. It is not dead. " +
                "It is only slow. You have been impatient with it.\"\n\n" +
                "She presses the flat of her palm against the cairn. Something in the stone settles.\n\n" +
                "\"Listen to a wall the same way you listen to soil. Give it the same silence you " +
                "would give a tree. You will find the land did not stop at the foundation — " +
                "it runs under the mortar, under the keep, under the whole of it. " +
                "Stone was earth once. It remembers.\"\n\n" +
                "She takes her hand away. The lichen is greener where she touched it.",
                true, false,
                "I will be patient with the stone.",
                null,
                () =>
                {
                    NatureKnowledge.RecordHermitFound(NatureHermitId.Retreat);
                    TalentSystem.GrantFree(TalentId.NatureDeepEarth, Hero.MainHero);
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Tiryn's patience settles in you. Stone walls no longer muffle the root-voice " +
                        "— the land speaks through them as freely as through open ground.",
                        new Color(0.35f, 0.75f, 0.35f)));
                },
                null), true, false);
        }

        // ── Faruk — Aserai villages — Dawn Call ────────────────────────────────
        private static void ShowFaruk()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Faruk the Patient",
                "He is old — older than the village, maybe. He is not from the Duneborn. " +
                "He is not from anywhere you could name. He speaks before you reach him, " +
                "without looking up from the ground he is studying.\n\n" +
                "\"You wait for it,\" he says. \"That is your mistake.\"\n\n" +
                "He turns a stone over with one finger. Under it, something is alive — " +
                "a beetle, a root-thread, you cannot tell.\n\n" +
                "\"The desert is loudest at dawn. Not because something changes — " +
                "because you change. The cold lifts. The dark lifts. " +
                "The ground lets out what it held all night. " +
                "You do not have to reach for it. You only have to be awake when it offers.\"\n\n" +
                "He does not look at you. He replaces the stone carefully, corner to corner.\n\n" +
                "\"Stop reaching. Be awake at dawn. Let the land find you.\"",
                true, false,
                "I will be awake.",
                null,
                () =>
                {
                    NatureKnowledge.RecordHermitFound(NatureHermitId.Fringe);
                    TalentSystem.GrantFree(TalentId.NatureDawnCall, Hero.MainHero);
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Faruk's lesson takes root before you leave the village. " +
                        "Each dawn the land offers a charge without being asked.",
                        new Color(0.35f, 0.75f, 0.35f)));
                },
                null), true, false);
        }

        // ── Hero death ─────────────────────────────────────────────────────────
        private void OnHeroKilled(Hero victim, Hero killer,
            TaleWorlds.CampaignSystem.Actions.KillCharacterAction.KillCharacterActionDetail detail,
            bool showNotification)
        {
            try { NatureSeerRegistry.OnLordDied(victim); } catch { }
        }
    }
}
