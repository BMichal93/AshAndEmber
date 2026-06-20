// =============================================================================
// ASH AND EMBER — Nature/NatureCampaignBehavior.cs
// Campaign wiring for The Living Ember system.
//
// Hermits:
//   Gwydion the Root-Listener  — Battania  — teaches Living Root
//   Birna of the Still Water   — Strugia   — teaches Still Draw
//   Bekh the Open Hand         — Khuzait   — teaches Open Grip
//
// Each hermit appears when the player enters a qualifying settlement in their
// region with sufficient renown (≥100) and is attuned to the living world.
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
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public class NatureCampaignBehavior : CampaignBehaviorBase
    {
        private static readonly Random _rng = new Random();

        // Settlement cooldown so the hermit doesn't spam on every enter.
        private readonly Dictionary<string, int> _hermitCooldowns = new Dictionary<string, int>();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        public override void SyncData(IDataStore store)
        {
            try { NatureKnowledge.Save(store);              } catch { }
            try { NatureSeerRegistry.Save(store);           } catch { }
        }

        public static void ResetForNewGame()
        {
            NatureKnowledge.ResetForNewGame();
            NatureSeerRegistry.ResetForNewGame();
        }

        public static void EstablishForNewCampaign()
        {
            NatureSeerRegistry.SeedInitialLords();
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            try { NatureCharge.DailyTick(); } catch { }

            // Passive charge accumulation (33% chance per day for attuned players)
            if (NatureKnowledge.IsAttuned && _rng.Next(3) == 0)
                try { NatureCharge.TryCampaignAccumulate(); } catch { }

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

        // ── Hero death ─────────────────────────────────────────────────────────
        private void OnHeroKilled(Hero victim, Hero killer,
            TaleWorlds.CampaignSystem.KillCharacterAction.KillCharacterActionDetail detail,
            bool showNotification)
        {
            try { NatureSeerRegistry.OnLordDied(victim); } catch { }
        }
    }
}
