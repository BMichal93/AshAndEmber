// =============================================================================
// LIFE & DEATH MAGIC — TalentSystem.cs
// Talent definitions, purchase logic, lore text, and save/load.
// 21 talents: 7 passive, 8 enchantment (4 damage / 4 restore), 6 campaign spell.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public enum TalentId
    {
        Gift        = 0,   // Passive — starting talent
        // 1 reserved (Subjugate — moved to Ashen Altar rites)
        Rejuvenate  = 2,   // REMOVED — kept for save compatibility (healing merged into Kindle)
        PlantGrowth = 3,   // REMOVED — kept for save compatibility
        BreakWills  = 4,   // Spell
        Inspire     = 5,   // Spell
        Plague      = 6,   // Spell
        Clairvoyance= 7,   // Spell
        Extinguish  = 8,   // Spell
        DevourLife  = 9,   // REMOVED — kept for save compatibility (merged into Reap)
        BattleMage  = 10,  // Passive
        Sorcerer    = 11,  // Passive
        Camaraderie = 12,  // Passive
        Reap        = 13,  // Passive
        Ember       = 14,  // Passive
        // ── Damage enchantments ───────────────────────────────────────────────
        Scatter     = 15,  // Enchantment — Damage: push back + slow (absorbed Char)
        Smoulder    = 16,  // Enchantment — Damage: morale drain + bewilder (absorbed Bewilder)
        Bewilder    = 17,  // REMOVED — kept for save compatibility (merged into Smoulder)
        Waver       = 21,  // REMOVED — kept for save compatibility
        // ── Restore enchantments ─────────────────────────────────────────────
        Ashveil     = 18,  // Enchantment — Restore: magic immunity
        CinderShell = 19,  // Enchantment — Restore: armour boost + overheal shield (absorbed Overflow)
        Hearthlight = 20,  // Enchantment — Restore: morale boost
        Rouse       = 22,  // REMOVED — kept for save compatibility
        // ── Damage enchantments (continued) ──────────────────────────────────
        Sunder      = 23,  // Enchantment — Damage: armor shred + attack reduction (absorbed Sear)
        Consume     = 24,  // REMOVED — kept for save compatibility
        Char        = 25,  // REMOVED — kept for save compatibility (merged into Scatter)
        // ── Restore enchantments (continued) ─────────────────────────────────
        Overflow    = 26,  // REMOVED — kept for save compatibility (merged into Cinder Shell)
        Renewal     = 27,  // REMOVED — kept for save compatibility
        Reflect     = 28,  // Enchantment — Restore: melee damage reflection
        // ── Passives ──────────────────────────────────────────────────────────
        Flashfire   = 29,  // Passive — 10% chance to echo a battle spell
        VeteranAsh  = 30,  // REMOVED — kept for save compatibility (merged into Tempered)
        // ── Campaign spells ────────────────────────────────────────────────────
        Ashfall     = 31,  // REMOVED — kept for save compatibility
        Fade        = 32,  // Spell — conceal party from enemy scouts
        AshenGift   = 33,  // Info — status card shown when player is Ashen (not purchasable)
        Immolate    = 34,  // Enchantment — Damage: guaranteed kill at 3+ inputs
        ArmedCasting = 35, // Passive — cast without sheathing weapons
    }

    public enum TalentCategory { Passive, Enchantment, Spell, Info }

    public class TalentDef
    {
        public TalentId      Id;
        public string        Name;
        public bool          IsSpell;        // true = campaign map spell
        public bool          IsEnchantment;  // true = battle enchantment
        public bool          IsInfo;         // true = display-only, not purchasable
        public TalentCategory Category;
        public string        Lore;
        public string        MechanicDesc;
    }

    public static class TalentSystem
    {
        private static readonly Random _rng = new Random();

        public static readonly IReadOnlyList<TalentDef> All = new List<TalentDef>
        {
            // ── Passive ──────────────────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Gift, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Gift",
                Lore = "The fire ran in your blood before you understood what fire was. Not warmth — something older. The kind that burns without consuming, and holds the world together at its edges.",
                MechanicDesc = "You carry the inner fire. In battle: form keys, Break, effect keys. U = Damage (enemies). D = Restore (allies)."
            },
            new TalentDef
            {
                Id = TalentId.BattleMage, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Tempered",
                Lore = "The forge teaches patience. A slow hand draws more from less; a careful reach into the fire takes without burning.",
                MechanicDesc = "Passive. Each battle cast costs 1 fewer day (minimum 1). Beyond age 40, each year further reduces cast cost by 0.5%, up to 30% total."
            },
            new TalentDef
            {
                Id = TalentId.Sorcerer, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Resonance",
                Lore = "Some days the fire gives back what it takes. You cannot predict it — only listen for it.",
                MechanicDesc = "Passive. One in four campaign map castings costs no days."
            },
            new TalentDef
            {
                Id = TalentId.Ember, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Ember",
                Lore = "In the moment of killing, when fire passes from one vessel to another, some scatters. Sometimes a spark finds you. You have learned, not to seek it, but to cup your hands.",
                MechanicDesc = "Passive. Each kill on the battlefield has a 5% chance to restore 1 day of youth."
            },
            new TalentDef
            {
                Id = TalentId.Reap, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Reap",
                Lore = "Every life spent in your shadow leaves something behind — a warmth, a residue, the last gasp of a flame that burned for your purpose. You have learned to hold a vessel for it.",
                MechanicDesc = "Passive. Raiding a village restores 5 days of youth (7-day cooldown). Each prisoner discarded has a 5% chance to restore 1 day. Executing a captured lord restores 100 days of youth. Learning this marks you."
            },
            new TalentDef
            {
                Id = TalentId.Camaraderie, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Kinship",
                Lore = "Those who carry the fire recognise each other from across a room. There is something almost like trust in that. Almost.",
                MechanicDesc = "Passive. +10 relations with those who carry the fire. Never falls below 0 with them."
            },
            new TalentDef
            {
                Id = TalentId.Flashfire, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Flashfire",
                Lore = "Sometimes the fire does not wait to be asked twice. It finds the shape again on its own — the same working, the same reach, the same burn. You do not question it. You simply let it.",
                MechanicDesc = "Passive. Each battle spell has a 10% chance to echo — firing again instantly at no aging cost."
            },
            new TalentDef
            {
                Id = TalentId.ArmedCasting, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Warcast",
                Lore = "Most who carry the fire release it through open hands — shape first, reach second. You discovered, not by learning but by surviving, that the flame does not ask what you are holding. Only whether you are willing.",
                MechanicDesc = "Passive. You may cast battle spells without sheathing your weapons. The fire flows through you, not only from you."
            },
            // ── Enchantments (Damage) ─────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Scatter, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Scatter",
                Lore = "The fire does not merely burn — it expels. What it touches, it unmakes and flings aside. You have learned to aim that expulsion.",
                MechanicDesc = "Enchantment. Damage blasts enemies backward (4m per Damage input) and sears their limbs, reducing movement speed by 25% per Damage input (max 75%) for 4s + 1s per input."
            },
            new TalentDef
            {
                Id = TalentId.Smoulder, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Smoulder",
                Lore = "The fire knows what frightens. It does not need to kill a man to defeat him — only to let him feel how little warmth he carries. The courage drains out with the heat.",
                MechanicDesc = "Enchantment. Damage scorches enemy morale (−12 per Damage input) and bewilders non-hero enemies with a random effect — instant rout, force charge, dismount, or morale fractured to 25%."
            },
            new TalentDef
            {
                Id = TalentId.Sunder, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Sunder",
                Lore = "Fire does not merely wound the surface — it reaches inward, finding the joins and seams of what they wear and what they carry. What holds together begins to separate. Not quickly. But enough.",
                MechanicDesc = "Enchantment. Damage tears at enemy defences and scorches their weapon arm for 8 seconds. Vulnerability to incoming damage = 5% per Damage input (max 40%). Attack power reduction = 8% per Damage input (max 40%)."
            },
            new TalentDef
            {
                Id = TalentId.Immolate, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Immolate",
                Lore = "Three times the fire has been called. Twice it asked. The third time, it takes. Not the wound — the whole. The body, the heat that kept it standing. The fire does not return what it has already claimed.",
                MechanicDesc = "Enchantment. Damage sets enemies alight — additional burn damage scales with inputs. At 3 or more Damage inputs, the fire consumes utterly: one target is guaranteed to die."
            },
            // ── Enchantments (Restore) ────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Ashveil, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Ashveil",
                Lore = "Ash does not burn twice. Coat something in it, and the fire cannot find purchase. For a few seconds, what you kindle becomes untouchable.",
                MechanicDesc = "Enchantment. Restore grants allies brief magic immunity. Duration = 3s per Restore input."
            },
            new TalentDef
            {
                Id = TalentId.CinderShell, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Cinder Shell",
                Lore = "Fire hardens what it doesn't consume. The skin does not become stone — it becomes something older. Whatever falls on them will not find the same flesh.",
                MechanicDesc = "Enchantment. Restore hardens allies, reducing incoming damage for 8 seconds. Protection = 5% per Restore input, max 50%. When an ally is near full health, excess fire adds a damage shield of 15 HP per Restore input for 5s."
            },
            new TalentDef
            {
                Id = TalentId.Hearthlight, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Hearthlight",
                Lore = "The fire in them has not gone out — it has only dimmed. You reach in and remind it what it is for. They remember, for a moment, that the fire is their friend.",
                MechanicDesc = "Enchantment. Restore lifts allied morale. Morale boost = 12 per Restore input."
            },
            new TalentDef
            {
                Id = TalentId.Reflect, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Reflect",
                Lore = "The fire you give is not passive. It waits in the body like an ember under ash, and when something cold strikes — it answers.",
                MechanicDesc = "Enchantment. Restore wraps allies in a retaliating flame. Melee hits against them reflect 8% of damage per Restore input back at the attacker, max 40%. Lasts 3s + 1s per input."
            },
            // ── Campaign map spells ──────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Inspire, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Kindle",
                Lore = "You let them feel it briefly — the warmth that says the world cares whether they live. It may be a lie. The fire does not ask.",
                MechanicDesc = "Your party gains 40 morale. Up to 8 wounded soldiers of each troop type recover. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.BreakWills, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Unsettle",
                Lore = "You let them feel how thin their fire is. Most men have never faced that knowledge directly. Courage is easier when you cannot see the dark.",
                MechanicDesc = "The nearest enemy party within 100m loses 35 morale. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Plague, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Wither",
                Lore = "Fire leaves places slowly, or quickly, depending on who tends it. You remove the tender.",
                MechanicDesc = "The nearest enemy village loses a fifth of its hearth. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Clairvoyance, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Clairvoyance",
                Lore = "The lines of fire connect every living thing to every other. You read them the way a navigator reads stars — imperfectly, but well enough.",
                MechanicDesc = "Gain 40 influence. Without a kingdom, the insight becomes gold instead. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Extinguish, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Extinguish",
                Lore = "You reach into the fire burning in an enemy and close your hand. Not slowly — like snuffing a candle. The body does not understand at first. Then it does.",
                MechanicDesc = "5–12 soldiers in the nearest enemy party are wounded or killed, and their courage breaks. Costs 1 day."
            },
            // ── Campaign spells (continued) ────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Fade, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Fade",
                Lore = "You draw your fire inward — not out, not away, but down into the marrow, down past what can be seen or felt. For a time you are still there. You simply stop being visible to those looking for you.",
                MechanicDesc = "Your party is concealed from enemy scouts for 2 days. Enemy parties will not pursue you. Costs 1 day."
            },
            // ── Ashen status (info-only, not purchasable) ─────────────────────
            new TalentDef
            {
                Id = TalentId.AshenGift, IsSpell = false, IsEnchantment = false, IsInfo = true,
                Category = TalentCategory.Info, Name = "The Cold Within",
                Lore = "The fire is gone. What remains is older, colder, and far more patient. It is not warmth you carry now — it is the memory of warmth and the hollow that followed.",
                MechanicDesc = "You are Ashen. You do not age. Each casting costs criminal rating instead of years. After your first working each day, each further cast risks the cold stirring against you — a possession that may claim your life."
            },
        };

        // ── Fade spell state ───────────────────────────────────────────────────
        private static int _fadeDaysRemaining = 0;
        private static PropertyInfo _ignoreByOtherPartiesProp = null;
        private static bool _ignoreByOtherPartiesResolved = false;

        private static bool TrySetIgnoreByOtherParties(MobileParty party, bool value)
        {
            if (!_ignoreByOtherPartiesResolved)
            {
                _ignoreByOtherPartiesResolved = true;
                _ignoreByOtherPartiesProp = typeof(MobileParty).GetProperty("IgnoreByOtherParties",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            if (_ignoreByOtherPartiesProp == null) return false;
            try { _ignoreByOtherPartiesProp.SetValue(party, value); return true; } catch { return false; }
        }

        /// <summary>Call from the daily tick to count down and clear the Fade effect.</summary>
        public static void DailyFadeTick()
        {
            if (_fadeDaysRemaining <= 0) return;
            _fadeDaysRemaining--;
            if (_fadeDaysRemaining <= 0)
            {
                try { if (MobileParty.MainParty != null) TrySetIgnoreByOtherParties(MobileParty.MainParty, false); } catch { }
                Msg("Fade — the ash settles. Your party is visible once more.");
            }
        }

        // ── Daily map cast counter ────────────────────────────────────────────
        private static int _dailyMapCastCount = 0;
        public static int DailyCastCount => _dailyMapCastCount;
        public static int GetDailyCastCost() => _dailyMapCastCount == 0 ? 1 : _dailyMapCastCount * 7;
        public static void ResetDailyCastCount()
        {
            if (_dailyMapCastCount > 0 && MageKnowledge.IsMage)
                InformationManager.DisplayMessage(new InformationMessage(
                    "Midnight — the toll of your workings resets.", new Color(0.5f, 0.5f, 0.7f)));
            _dailyMapCastCount = 0;
        }

        // ── Player talent tracking ─────────────────────────────────────────────
        private static readonly HashSet<TalentId> _purchased = new HashSet<TalentId>();

        public static bool Has(TalentId id) => _purchased.Contains(id);
        public static IEnumerable<TalentId> AllPurchased => _purchased;
        public static int PurchasedCount => _purchased.Count;

        public static void ResetForNewGame()
        {
            _purchased.Clear();
            _purchased.Add(TalentId.Gift);
            _dailyMapCastCount = 0;
        }

        public static void UnlockAll()
        {
            foreach (TalentId id in Enum.GetValues(typeof(TalentId)))
                _purchased.Add(id);
            InformationManager.DisplayMessage(new InformationMessage(
                "All talents unlocked.",
                new Color(1f, 0.8f, 0.2f)));
        }

        // Cost curve: 1 pt until you hold 10 or more talents, 2 pts from that point on.
        public static int PurchaseCost() => _purchased.Count < 10 ? 1 : 2;

        // Grant a talent for free (no focus-point cost). Returns false if already owned.
        public static bool GrantFree(TalentId id, Hero hero)
        {
            if (_purchased.Contains(id)) return false;
            _purchased.Add(id);
            if (id == TalentId.Camaraderie) ApplyCamaraderie(hero);
            if (id == TalentId.Reap)        ApplyReapTraits(hero);
            var def = GetDef(id);
            if (def != null)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Talent learned: {def.Name}.", new Color(0.90f, 0.60f, 0.20f)));
            return true;
        }

        public static bool TryPurchase(TalentId id, Hero hero)
        {
            if (_purchased.Contains(id)) return false;
            if (hero == null) return false;

            var defCheck = All.FirstOrDefault(d => d.Id == id);
            if (defCheck?.IsInfo == true)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "This cannot be learned.", Color.FromUint(0xFFAAAAAA)));
                return false;
            }

            int cost = PurchaseCost();

            bool spent = false;
            try
            {
                if (hero.HeroDeveloper.UnspentFocusPoints >= cost)
                {
                    hero.HeroDeveloper.UnspentFocusPoints -= cost;
                    spent = true;
                }
            }
            catch { }

            if (!spent)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Not enough focus points. Cost: {cost} point{(cost != 1 ? "s" : "")}.",
                    new Color(0.8f, 0.5f, 0.2f)));
                return false;
            }

            _purchased.Add(id);

            if (id == TalentId.Camaraderie) ApplyCamaraderie(hero);
            if (id == TalentId.Reap)        ApplyReapTraits(hero);

            var def = GetDef(id);
            InformationManager.DisplayMessage(new InformationMessage(
                $"You have learned {def.Name}. {def.MechanicDesc}",
                new Color(0.7f, 0.9f, 0.7f)));
            return true;
        }

        private static void ApplyReapTraits(Hero hero)
        {
            try
            {
                int mercy = hero.GetTraitLevel(DefaultTraits.Mercy);
                if (mercy > -3) hero.SetTraitLevel(DefaultTraits.Mercy, mercy - 1);
                int honor = hero.GetTraitLevel(DefaultTraits.Honor);
                if (honor > -3) hero.SetTraitLevel(DefaultTraits.Honor, honor - 1);
                if (hero.MapFaction is Kingdom k)
                    try { ChangeCrimeRatingAction.Apply(k, 30f, true); } catch { }
                InformationManager.DisplayMessage(new InformationMessage(
                    "The fire darkens with hunger. Those who witness what you do will remember.",
                    new Color(0.8f, 0.4f, 0.2f)));
            }
            catch { }
        }

        private static void ApplyCamaraderie(Hero player)
        {
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (h == player || !ColourLordRegistry.IsColourLord(h)) continue;
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(player, h, 10, false); } catch { }
                }
            }
            catch { }
        }

        public static void EnforceCaramaraderieLimits(Hero player, Hero mage)
        {
            if (!Has(TalentId.Camaraderie)) return;
            try
            {
                int rel = CharacterRelationManager.GetHeroRelation(player, mage);
                if (rel < 0) CharacterRelationManager.SetHeroRelation(player, mage, 0);
            }
            catch { }
        }

        // Enforce Kinship floor for all living mage lords — call from daily tick.
        public static void EnforceKinship()
        {
            if (!Has(TalentId.Camaraderie) || Hero.MainHero == null) return;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes)
                {
                    if (h == Hero.MainHero || !h.IsAlive || !ColourLordRegistry.IsColourLord(h)) continue;
                    EnforceCaramaraderieLimits(Hero.MainHero, h);
                }
            }
            catch { }
        }

        public static TalentDef GetDef(TalentId id) =>
            All.FirstOrDefault(d => d.Id == id) ?? All[0];

        // ── Campaign map spell execution ──────────────────────────────────────
        public static void ExecuteMapSpell(TalentId id)
        {
            if (!Has(id)) return;
            var def = GetDef(id);
            if (!def.IsSpell) return;

            if (MageKnowledge.IsAshen)
            {
                if (_dailyMapCastCount > 0 && _rng.Next(3) == 0)
                    MageKnowledge.QueuePossessionEvent();
                try
                {
                    if (Hero.MainHero?.MapFaction is Kingdom ashenK)
                    {
                        ChangeCrimeRatingAction.Apply(ashenK, GetBlightCrimeCost(id), false);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The ash spreads.", new Color(0.3f, 0.35f, 0.7f)));
                    }
                }
                catch { }
            }
            else
            {
                int cost = GetDailyCastCost();
                bool skipAging = Has(TalentId.Sorcerer) && _rng.Next(4) == 0;
                if (skipAging)
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The fire gives back.", new Color(0.9f, 0.6f, 0.3f)));
                else
                {
                    AgingSystem.AgeHero(Hero.MainHero, cost);
                    if (cost > 1)
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"The fire demands more — {cost} days.", new Color(0.9f, 0.5f, 0.2f)));
                }
            }
            _dailyMapCastCount++;

            switch (id)
            {
                case TalentId.BreakWills:   CastBreakWills();   break;
                case TalentId.Inspire:      CastInspire();      break;
                case TalentId.Plague:       CastPlague();       break;
                case TalentId.Clairvoyance: CastClairvoyance(); break;
                case TalentId.Extinguish:   CastExtinguish();   break;
                case TalentId.Fade:         CastFade();         break;
            }
        }

        private static void CastBreakWills()
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                var target = MobileParty.All
                    .Where(p => p.IsActive && FactionManager.IsAtWarAgainstFaction(p.MapFaction, MobileParty.MainParty.MapFaction)
                             && p.LeaderHero != null
                             && (p.GetPosition2D - playerPos).Length < 100f)
                    .OrderBy(p => (p.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Unsettle — no enemy party in range."); return; }
                target.RecentEventsMorale -= 35f;
                Msg($"Unsettle — dread settles over {target.Name}. -35 morale.");
            }
            catch { }
        }

        private static void CastInspire()
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                MobileParty.MainParty.RecentEventsMorale += 40f;
                var roster = MobileParty.MainParty.MemberRoster;
                var wounded = roster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.WoundedNumber > 0).ToList();
                int roused = 0;
                foreach (var entry in wounded)
                {
                    int heal = Math.Min(entry.WoundedNumber, 8);
                    try { roster.AddToCounts(entry.Character, 0, false, -heal); roused += heal; } catch { }
                }
                string msg = roused > 0
                    ? $"Kindle — warmth floods your ranks. +40 morale, {roused} soldier{(roused != 1 ? "s" : "")} rise from their wounds."
                    : "Kindle — warmth floods your ranks. +40 morale.";
                Msg(msg);
            }
            catch { }
        }

        private static void CastPlague()
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                var playerFaction = MobileParty.MainParty.MapFaction;
                var target = Settlement.All
                    .Where(s => s.IsVillage && s.Village != null && s.MapFaction != null
                             && s.MapFaction != playerFaction
                             && FactionManager.IsAtWarAgainstFaction(s.MapFaction, playerFaction))
                    .OrderBy(s => (s.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Wither — no enemy villages found."); return; }
                float before = target.Village.Hearth;
                target.Village.Hearth = Math.Max(10f, before * 0.80f);
                Msg($"Wither — something old settles over {target.Name}. Hearth reduced by 20%.");
            }
            catch { }
        }

        private static void CastClairvoyance()
        {
            try
            {
                if (Hero.MainHero?.Clan?.Kingdom != null)
                {
                    Hero.MainHero.Clan.Influence += 40f;
                    Msg("Clairvoyance — the threads of power revealed. +40 influence.");
                }
                else
                {
                    Hero.MainHero.ChangeHeroGold(1000);
                    Msg("Clairvoyance — no throne to bend, but the fire finds other currents. +1000 gold.");
                }
            }
            catch { Msg("Clairvoyance — insight granted."); }
        }

        private static void CastExtinguish()
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                var target = MobileParty.All
                    .Where(p => p.IsActive && FactionManager.IsAtWarAgainstFaction(p.MapFaction, MobileParty.MainParty.MapFaction)
                             && p.MemberRoster.TotalRegulars > 0
                             && (p.GetPosition2D - playerPos).Length < 60f)
                    .OrderBy(p => (p.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Extinguish — no enemy party in range."); return; }
                int count = 5 + _rng.Next(8);
                int actual = 0;
                var troops = target.MemberRoster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
                for (int i = 0; i < count && troops.Count > 0; i++)
                {
                    int idx = _rng.Next(troops.Count);
                    int wound = _rng.Next(2) == 0 ? 1 : 0;
                    try { target.MemberRoster.AddToCounts(troops[idx].Character, wound == 1 ? 0 : -1, false, wound); actual++; } catch { }
                }
                target.RecentEventsMorale -= 25f;
                Msg($"Extinguish — {actual} fire{(actual != 1 ? "s" : "")} snuffed in {target.Name}. Their courage breaks. -25 morale.");
            }
            catch { }
        }

        private static void CastFade()
        {
            try
            {
                if (MobileParty.MainParty == null) { Msg("Fade — no party to conceal."); return; }
                _fadeDaysRemaining = 2;
                bool applied = TrySetIgnoreByOtherParties(MobileParty.MainParty, true);
                if (applied)
                {
                    Msg("Fade — ash wraps your party. For two days, enemy scouts will not find you.");
                }
                else
                {
                    // Fallback when IgnoreByOtherParties is not accessible: scatter nearby enemies.
                    Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                    int scattered = 0;
                    foreach (MobileParty p in MobileParty.All.ToList())
                    {
                        if (!p.IsActive || p == MobileParty.MainParty) continue;
                        try { if (!FactionManager.IsAtWarAgainstFaction(p.MapFaction, MobileParty.MainParty.MapFaction)) continue; } catch { continue; }
                        if ((p.GetPosition2D - playerPos).Length > 80f) continue;
                        try { p.RecentEventsMorale -= 40f; scattered++; } catch { }
                    }
                    string tail = scattered > 0
                        ? $" {scattered} nearby enemy {(scattered == 1 ? "party is" : "parties are")} thrown into confusion."
                        : "";
                    Msg($"Fade — the ash rises around you.{tail}");
                }
            }
            catch { }
        }

        private static float GetBlightCrimeCost(TalentId id)
        {
            switch (id)
            {
                case TalentId.Extinguish:
                case TalentId.Clairvoyance: return 15f;
                case TalentId.BreakWills:
                case TalentId.Plague:       return 10f;
                case TalentId.Fade:         return 5f;
                default:                    return 5f;
            }
        }

        // ── NPC campaign map spell execution ─────────────────────────────────
        public static void ExecuteNpcMapSpell(Hero caster, TalentId id)
        {
            if (caster == null) return;
            string blurb = null;
            try
            {
                switch (id)
                {
                    case TalentId.BreakWills:  NpcBreakWills(caster);  blurb = "casts Unsettle — dread spreads through an enemy party."; break;
                    case TalentId.Inspire:     NpcInspire(caster);     blurb = "kindles their warband — morale rises."; break;
                    case TalentId.Plague:      NpcPlague(caster);      blurb = "works a Wither — a village's hearth fades."; break;
                    case TalentId.Extinguish:  NpcExtinguish(caster);  blurb = "casts Extinguish — fires snuffed in a distant party."; break;
                    case TalentId.Clairvoyance:NpcClairvoyance(caster);blurb = "reads the threads — power flows to them."; break;
                    default: break;
                }
            }
            catch { }

            if (blurb != null)
            {
                bool isAshen = ColourLordRegistry.IsAshenLord(caster);
                Color c = isAshen ? new Color(0.38f, 0.50f, 0.75f) : new Color(0.65f, 0.45f, 0.8f);
                InformationManager.DisplayMessage(new InformationMessage($"{caster.Name} — {blurb}", c));
            }

            if (ColourLordRegistry.IsAshenLord(caster)) ApplyBlightDrain(caster);
            else AgingSystem.AgeHero(caster, 1);
        }

        private static void ApplyBlightDrain(Hero caster)
        {
            try
            {
                var party = caster.PartyBelongedTo;
                if (_rng.Next(2) == 0 && party != null)
                {
                    var troops = party.MemberRoster.GetTroopRoster()
                        .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
                    if (troops.Count > 0)
                    {
                        var entry = troops[_rng.Next(troops.Count)];
                        try { party.MemberRoster.AddToCounts(entry.Character, 0, false, 1); } catch { }
                        return;
                    }
                }
                Vec2 pos = party?.GetPosition2D ?? Vec2.Zero;
                var village = Settlement.All
                    .Where(s => s.IsVillage && s.Village != null)
                    .OrderBy(s => (s.GetPosition2D - pos).Length)
                    .FirstOrDefault();
                if (village != null)
                    village.Village.Hearth = Math.Max(10f, village.Village.Hearth * 0.97f);
            }
            catch { }
        }

        private static void NpcBreakWills(Hero caster)
        {
            Vec2 pos = caster.PartyBelongedTo?.GetPosition2D ?? Vec2.Zero;
            var target = MobileParty.All
                .Where(p => p.IsActive && FactionManager.IsAtWarAgainstFaction(p.MapFaction, caster.PartyBelongedTo?.MapFaction)
                         && (p.GetPosition2D - pos).Length < 50f)
                .OrderBy(p => (p.GetPosition2D - pos).Length).FirstOrDefault();
            if (target == null) return;
            target.RecentEventsMorale -= 20f;
        }

        private static void NpcInspire(Hero caster)
        {
            var party = caster.PartyBelongedTo;
            if (party == null) return;
            party.RecentEventsMorale += 20f;
        }

        private static void NpcPlague(Hero caster)
        {
            var villages = Settlement.All
                .Where(s => s.IsVillage && s.Village != null && s.MapFaction != caster.MapFaction).ToList();
            if (villages.Count == 0) return;
            var v = villages[_rng.Next(villages.Count)];
            v.Village.Hearth = Math.Max(10f, v.Village.Hearth * 0.80f);
        }

        private static void NpcExtinguish(Hero caster)
        {
            Vec2 pos = caster.PartyBelongedTo?.GetPosition2D ?? Vec2.Zero;
            var target = MobileParty.All
                .Where(p => p.IsActive && FactionManager.IsAtWarAgainstFaction(p.MapFaction, caster.PartyBelongedTo?.MapFaction)
                         && p.MemberRoster.TotalRegulars > 2
                         && (p.GetPosition2D - pos).Length < 60f)
                .OrderBy(p => (p.GetPosition2D - pos).Length).FirstOrDefault();
            if (target == null) return;
            var troops = target.MemberRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
            if (troops.Count == 0) return;
            try { target.MemberRoster.AddToCounts(troops[_rng.Next(troops.Count)].Character, 0, false, 1); } catch { }
        }

        private static void NpcClairvoyance(Hero caster)
        {
            try
            {
                if (caster.Clan != null)
                {
                    caster.Clan.Renown    += 10f;
                    caster.Clan.Influence += 15f;
                }
                if (caster.PartyBelongedTo != null)
                    caster.PartyBelongedTo.RecentEventsMorale += 10f;
            }
            catch { }
        }

        private static void Msg(string text) =>
            MBInformationManager.AddQuickInformation(new TextObject(text));

        // ── Save / Load ────────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var list = _purchased.Select(t => (int)t).ToList();
            store.SyncData("LDM_Talents", ref list);
            _purchased.Clear();
            if (list != null)
                foreach (int v in list) _purchased.Add((TalentId)v);

            // Fade is intentionally not persisted — on load the effect resets.
            // Explicitly clear IgnoreByOtherParties so a save/load while faded
            // does not leave the party permanently invisible.
            _fadeDaysRemaining = 0;
            try { if (MobileParty.MainParty != null) TrySetIgnoreByOtherParties(MobileParty.MainParty, false); } catch { }
        }
    }

    internal static class MobilePartyExtensions
    {
        public static void Let(this MobileParty p, Action<MobileParty> action)
        {
            if (p != null) action(p);
        }
    }
}
