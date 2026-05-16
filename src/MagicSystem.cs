// =============================================================================
// COLOURS OF CALRADIA — MagicSystem.cs
// Mount & Blade II: Bannerlord Mod  v2.0
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
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Engine;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.CampaignSystem.MapEvents;

namespace ColoursOfCalradia
{
    // =========================================================================
    // 1. MODULE ENTRY POINT
    // =========================================================================
    public class MainSubModule : MBSubModuleBase
    {
        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            if (game.GameType is Campaign &&
                gameStarterObject is CampaignGameStarter campaignStarter)
                campaignStarter.AddBehavior(new MagicCampaignBehavior());
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            mission.AddMissionBehavior(new MagicMissionBehavior());
        }

        protected override void OnApplicationTick(float dt)
        {
            if (Campaign.Current == null || Mission.Current != null) return;
            MagicInputHandler.Tick(inMission: false);
            ActiveEffectManager.MapTick(dt);
        }
    }

    // =========================================================================
    // 1b. MISSION BEHAVIOR
    // =========================================================================
    public class MagicMissionBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private bool _orderHookRegistered = false;
        private static readonly Random _orderRng = new Random();
        private static readonly MovementOrder[] _madnessOrders =
        {
            MovementOrder.MovementOrderStop,
            MovementOrder.MovementOrderCharge,
        };

        public override void OnMissionTick(float dt)
        {
            TryRegisterOrderHook();
            MagicInputHandler.Tick(inMission: true);
            ActiveEffectManager.MissionTick(dt);
            ColourLordAI.MissionTick(dt);
            ColourUnitRegistry.MissionTick(dt);
            SpellEffects.TickGlows(dt);
            SpellEffects.TickMoves(dt);
            SpellEffects.TickAreaEffects(dt);
            SpellEffects.TickHollowGaze(dt);
            SpellEffects.TickHUDConfusion(dt);
            SpellEffects.TickBlueWeight(dt);
            SpellEffects.TickHaltedAgents(dt);
            SpellEffects.TickRandomUnitMagic(dt);
        }

        private void TryRegisterOrderHook()
        {
            if (_orderHookRegistered) return;
            try
            {
                var ctrl = Mission.Current?.PlayerTeam?.PlayerOrderController;
                if (ctrl == null) return;
                ctrl.OnOrderIssued += OnPlayerOrderIssued;
                _orderHookRegistered = true;
            }
            catch { }
        }

        private void OnPlayerOrderIssued(OrderType orderType,
            MBReadOnlyList<Formation> formations, OrderController orderController, object[] delegateParams)
        {
            int chance = ColourKnowledge.GetMadnessOrderChance();
            if (chance <= 0 || _orderRng.Next(100) >= chance) return;

            bool charge = _orderRng.Next(2) == 0;
            MovementOrder replacement = charge
                ? MovementOrder.MovementOrderCharge
                : MovementOrder.MovementOrderStop;
            string name = charge ? "Charge" : "Halt";

            foreach (Formation f in formations)
                try { f.SetMovementOrder(replacement); } catch { }

            InformationManager.DisplayMessage(new InformationMessage(
                $"Madness: Your command slips — {name} issued instead.",
                Color.FromUint(0xFFCC44FF)));
        }

        protected override void OnEndMission()
        {
            if (!_orderHookRegistered) return;
            try
            {
                var ctrl = Mission.Current?.PlayerTeam?.PlayerOrderController;
                if (ctrl != null) ctrl.OnOrderIssued -= OnPlayerOrderIssued;
            }
            catch { }
            _orderHookRegistered = false;
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent,
            in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            // Scarlet Ward: first physical blow against the player shatters the ward and undoes the damage
            if (affectedAgent == Agent.Main && affectorAgent != Agent.Main
                && affectorAgent != null && SpellEffects.ScarletWardActive)
                SpellEffects.AbsorbScarletWard(blow.InflictedDamage);
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent,
            AgentState agentState, KillingBlow blow)
        {
            ColourUnitRegistry.OnAgentRemoved(affectedAgent);

            // Green — Gentle Burden: dealing a killing blow wounds the caster
            if (affectorAgent == Agent.Main && affectorAgent.IsActive()
                && ColourKnowledge.HasSchool(ColorSchool.Green)
                && affectedAgent != Agent.Main)
            {
                try
                {
                    Agent.Main.Health = Math.Max(1f, Agent.Main.Health - 8f);
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Gentle Burden: A life ends by your hand. The cost falls on you.",
                        ColorSchoolData.GetMessageColor(ColorSchool.Green)));
                }
                catch { }
            }
        }
    }

    // =========================================================================
    // 2. COLOUR SCHOOLS
    // =========================================================================
    public enum ColorSchool { Red, Orange, Yellow, Green, Blue, Purple }

    public static class ColorSchoolData
    {
        public struct SchoolInfo
        {
            public string Name;
            public string FlavorText;
            public string PersonalityEffect;
            public string LimitationA;
            public string LimitationB;
            public string AttributePenalty;
        }

        public static readonly Dictionary<ColorSchool, SchoolInfo> Info =
            new Dictionary<ColorSchool, SchoolInfo>
        {
            [ColorSchool.Red] = new SchoolInfo
            {
                Name             = "Red",
                FlavorText       = "Blood Price — Violent, fiery magic of war and ruin. Red mages channel rage into devastating waves and burning bursts. " +
                                   "The art demands payment in pain: each working scorches the caster, and every cast drives your soldiers into a frenzy.",
                PersonalityEffect= "Repeated casting makes you less Calculating — more instinctive, more impulsive.",
                LimitationA      = "Furious: Each Red spell automatically issues a Charge order to your formations.",
                LimitationB      = "Blood Price: Each Red spell opens a wound on the caster — magic always takes its due.",
                AttributePenalty = "-1 Control"
            },
            [ColorSchool.Orange] = new SchoolInfo
            {
                Name             = "Orange",
                FlavorText       = "Generous Hunger — Joyful, generous magic of warmth and plenty. Orange mages inspire and conjure allies from nothing. " +
                                   "Their indulgent nature, however, devours resources and sends the caster lurching with each casting.",
                PersonalityEffect= "Repeated casting increases your Generosity — open-handed and free with what you have.",
                LimitationA      = "Overindulgent: Your party consumes food faster and army upkeep is higher.",
                LimitationB      = "Generous Flood: Each Orange spell briefly seizes your body — you stagger in random directions for a moment, lurching unpredictably across the field.",
                AttributePenalty = "-1 Intellect"
            },
            [ColorSchool.Yellow] = new SchoolInfo
            {
                Name             = "Yellow",
                FlavorText       = "The Fearful Eye — Visceral, stomach-turning magic of dread and revulsion. Yellow mages poison courage and stir the deep animal panic beneath every soldier's composure. " +
                                   "The cost is insidious: those who spread fear begin to feel it — their judgment frays and their nerve hollows from within.",
                PersonalityEffect= "Repeated casting erodes your Mercy — disgust curdles into indifference; pity becomes revulsion.",
                LimitationA      = "Paranoia: Each Yellow spell costs party morale — the fear bleeds inward as well as outward.",
                LimitationB      = "Blurred Judgment: Yellow magic clouds the caster's mind — each cast increases your criminal rating as you begin to see threats everywhere.",
                AttributePenalty = "-1 Social"
            },
            [ColorSchool.Green] = new SchoolInfo
            {
                Name             = "Green",
                FlavorText       = "Gentle Burden — Kind, mending magic of life and restoration. Green mages sustain their companions through battle. " +
                                   "Their pacifist heart cannot act while holding a blade, and the weight of nearby violence seeps back into their body.",
                PersonalityEffect= "Repeated casting increases your Mercy — slow to strike, quick to spare.",
                LimitationA      = "Pacifist: You cannot use Green magic while wielding a weapon.",
                LimitationB      = "Gentle Burden: Each killing blow you land costs you — Green magic does not forgive the taking of life.",
                AttributePenalty = "-1 Endurance"
            },
            [ColorSchool.Blue] = new SchoolInfo
            {
                Name             = "Blue",
                FlavorText       = "Scholar's Weight — Cold, distanced magic of order and stillness. Blue mages freeze formations and conjure spectral shields. " +
                                   "But knowledge is heavy — each casting strains the body, adding invisible weight to armour and limb.",
                PersonalityEffect= "Repeated casting increases your Calculating trait — measured, deliberate, distant.",
                LimitationA      = "Scholar's Weight: Each Blue spell makes your equipment feel heavier — movement slows with every cast and does not recover until the battle ends. Six stacks will slow you to a crawl.",
                LimitationB      = "Heavy Knowledge: Cerulean Mirror shields you from spells and magic effects for 40 seconds — but steel still finds you.",
                AttributePenalty = "-1 Vigor"
            },
            [ColorSchool.Purple] = new SchoolInfo
            {
                Name             = "Purple",
                FlavorText       = "The Waning Art — Melancholic, fading magic of grief and hollow quietude. Purple mages touch the deep sadness beneath living things, drawing on resignation and loss. " +
                                   "The grey does not take violently — it takes slowly, steadily. Each working bleeds away a little of the mage's time, presence, and will to be.",
                PersonalityEffect= "Repeated casting drains your Valor — grief and resignation make it harder to believe anything is worth the fight.",
                LimitationA      = "Waning Cost: Each Purple spell ages the caster by ~2 days — the grey draws time inward, quietly.",
                LimitationB      = "The Slow Unravelling: Each Purple cast quietly reduces the caster's fertility — something within grows dimmer with every working. It never reaches zero, but it never comes back.",
                AttributePenalty = "-1 Cunning"
            }
        };

        // ARGB hex glow colors per school
        public static uint GetGlowColor(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return 0xFFFF2200u;
                case ColorSchool.Orange: return 0xFFFF8800u;
                case ColorSchool.Yellow: return 0xFFFFFF00u;
                case ColorSchool.Green:  return 0xFF00CC44u;
                case ColorSchool.Blue:   return 0xFF2244FFu;
                case ColorSchool.Purple: return 0xFF8800CCu;
                default:                 return 0xFFFFFFFFu;
            }
        }

        // Attribute that receives -1 penalty when school is chosen
        public static CharacterAttribute GetPenaltyAttribute(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return DefaultCharacterAttributes.Control;
                case ColorSchool.Orange: return DefaultCharacterAttributes.Intelligence;
                case ColorSchool.Yellow: return DefaultCharacterAttributes.Social;
                case ColorSchool.Green:  return DefaultCharacterAttributes.Endurance;
                case ColorSchool.Blue:   return DefaultCharacterAttributes.Vigor;
                case ColorSchool.Purple: return DefaultCharacterAttributes.Cunning;
                default:                 return DefaultCharacterAttributes.Vigor;
            }
        }

        // Trait affected by casting and direction (+1 = increase, -1 = decrease)
        public static (TraitObject trait, int direction) GetTraitEffect(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return (DefaultTraits.Calculating, -1);
                case ColorSchool.Orange: return (DefaultTraits.Generosity,  +1);
                case ColorSchool.Yellow: return (DefaultTraits.Mercy,       -1);
                case ColorSchool.Green:  return (DefaultTraits.Mercy,       +1);
                case ColorSchool.Blue:   return (DefaultTraits.Calculating, +1);
                case ColorSchool.Purple: return (DefaultTraits.Valor,       -1);
                default:                 return (DefaultTraits.Valor,        0);
            }
        }

        // Informational color for messages about each school
        public static Color GetMessageColor(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return new Color(1.0f, 0.13f, 0.0f);
                case ColorSchool.Orange: return new Color(1.0f, 0.53f, 0.0f);
                case ColorSchool.Yellow: return new Color(1.0f, 1.0f,  0.0f);
                case ColorSchool.Green:  return new Color(0.0f, 0.8f,  0.27f);
                case ColorSchool.Blue:   return new Color(0.13f, 0.27f, 1.0f);
                case ColorSchool.Purple: return new Color(0.53f, 0.0f, 0.8f);
                default:                 return Color.White;
            }
        }
    }

    // =========================================================================
    // 3. SPELL DATABASE  (18 battle spells, 4-char combos)
    // =========================================================================
    public enum SpellContext { Mission, Map }

    public class SpellEntry
    {
        public string      Name;
        public string      Combo;      // exactly 4 chars: U / L / R
        public ColorSchool School;
        public SpellContext Context;
        public string      Flavour;
    }

    public static class SpellDatabase
    {
        // Combos follow a strict structure: first 2 chars = Form, last 2 chars = Colour.
        // Forms: UU = Blast (cone), RL = Self (aura), LR = Create (area effect)
        // Colours: RR = Red, RU = Orange, LU = Yellow, LL = Green, UL = Blue, UR = Purple
        public static readonly IReadOnlyList<SpellEntry> All = new List<SpellEntry>
        {
            // ── BLAST (UU prefix) — medium cone in front of the caster ──────────
            new SpellEntry { Name="Crimson Torrent",  Combo="UURR", School=ColorSchool.Red,
                Context=SpellContext.Mission,
                Flavour="The rage of a thousand battles channelled into a single, devastating wave." },
            new SpellEntry { Name="Golden Tide",      Combo="UURU", School=ColorSchool.Orange,
                Context=SpellContext.Mission,
                Flavour="Wash over your foes with jubilant force; even enemies cannot resist the urge to advance." },
            new SpellEntry { Name="Tide of Dread",    Combo="UULU", School=ColorSchool.Yellow,
                Context=SpellContext.Mission,
                Flavour="A wave of creeping, nameless wrongness — it strips the nerve from all it touches and leaves behind only the urge to run." },
            new SpellEntry { Name="Verdant Surge",    Combo="UULL", School=ColorSchool.Green,
                Context=SpellContext.Mission,
                Flavour="A tide of living energy that flows toward your own — allies in the cone are mended, enemies and the caster untouched." },
            new SpellEntry { Name="Azure Arrest",     Combo="UUUL", School=ColorSchool.Blue,
                Context=SpellContext.Mission,
                Flavour="A freezing wave of scholarly force. All before you halt; riders are unseated." },
            new SpellEntry { Name="Grey Harvest",     Combo="UUUR", School=ColorSchool.Purple,
                Context=SpellContext.Mission,
                Flavour="The grey settles over one soul in the cone. They simply stop. The body follows the spirit out like a slow tide." },

            // ── SELF (RL prefix) — creates a glowing aura around the caster ─────
            // Note: DD prefix cannot be used — S with empty buffer opens the spellbook.
            new SpellEntry { Name="Scarlet Ward",     Combo="RLRR", School=ColorSchool.Red,
                Context=SpellContext.Mission,
                Flavour="The next blow lands on iron. One strike. The ward then shatters." },
            new SpellEntry { Name="Warm Beacon",      Combo="RLRU", School=ColorSchool.Orange,
                Context=SpellContext.Mission,
                Flavour="A golden light calls your companions from across the field to your side." },
            new SpellEntry { Name="Nausea Bloom",     Combo="RLLU", School=ColorSchool.Yellow,
                Context=SpellContext.Mission,
                Flavour="Something deeply wrong radiates from you. All who linger nearby feel it in their stomach before they know it in their mind." },
            new SpellEntry { Name="Verdant Touch",    Combo="RLLL", School=ColorSchool.Green,
                Context=SpellContext.Mission,
                Flavour="You lay hands upon yourself. The wounds knit closed." },
            new SpellEntry { Name="Cerulean Mirror",  Combo="RLUL", School=ColorSchool.Blue,
                Context=SpellContext.Mission,
                Flavour="Spells pass through you for 40 seconds. Steel does not." },
            new SpellEntry { Name="Grief's Veil",     Combo="RLUR", School=ColorSchool.Purple,
                Context=SpellContext.Mission,
                Flavour="The grey folds you from sight for 12 seconds. Nearby enemies lose track of you and pause. You cannot be touched while the veil holds." },

            // ── CREATE (LR prefix) — special area effect, specific to each colour ─
            new SpellEntry { Name="Cinder Burst",     Combo="LRRR", School=ColorSchool.Red,
                Context=SpellContext.Mission,
                Flavour="The world around you ignites. All nearby pay the price of your fury." },
            new SpellEntry { Name="Golden Snare",     Combo="LRRU", School=ColorSchool.Orange,
                Context=SpellContext.Mission,
                Flavour="A bright patch of golden earth waits for the first soul to step into it — then gives their formation a random, chaotic order and vanishes. Cast again to dismiss." },
            new SpellEntry { Name="Creeping Dread",   Combo="LRLU", School=ColorSchool.Yellow,
                Context=SpellContext.Mission,
                Flavour="A cloud of formless revulsion drifts across the field. Those it passes through feel their skin crawl and their courage hollow out. Cast again to dismiss." },
            new SpellEntry { Name="Emerald Font",     Combo="LRLL", School=ColorSchool.Green,
                Context=SpellContext.Mission,
                Flavour="A blessed circle of earth. All who stand within are slowly mended — friend and foe alike. Cast again to dismiss." },
            new SpellEntry { Name="Sapphire Bastion", Combo="LRUL", School=ColorSchool.Blue,
                Context=SpellContext.Mission,
                Flavour="A wall of solid blue force rises from the earth, repelling all who approach. Fades after three minutes." },
            new SpellEntry { Name="Hollow Gaze",      Combo="LRUR", School=ColorSchool.Purple,
                Context=SpellContext.Mission,
                Flavour="One nearby enemy empties out. They stand. They do not move. They wait for nothing. Cast again to release them." },
        };

        public static SpellEntry Find(string combo) =>
            All.FirstOrDefault(s => s.Combo == combo);

        public static IEnumerable<SpellEntry> BySchool(ColorSchool school) =>
            All.Where(s => s.School == school);
    }

    // =========================================================================
    // 4. COLOUR KNOWLEDGE  — tracks chosen schools, cast counts, grimoire
    // =========================================================================
    public static class ColourKnowledge
    {
        private static readonly HashSet<ColorSchool> _chosenSchools = new HashSet<ColorSchool>();
        // Accumulated casts per school — every 25 casts shifts the associated trait
        private static readonly Dictionary<ColorSchool, int> _castCounters =
            new Dictionary<ColorSchool, int>
            {
                [ColorSchool.Red]    = 0,
                [ColorSchool.Orange] = 0,
                [ColorSchool.Yellow] = 0,
                [ColorSchool.Green]  = 0,
                [ColorSchool.Blue]   = 0,
                [ColorSchool.Purple] = 0,
            };
        private static readonly HashSet<string> _giftedChildIds = new HashSet<string>();

        private static int   _grimoirePage         = 0;
        private const  int   GrimoirePageSize       = 4;
        private static float _purpleFertilityLevel  = 1.0f;

        public static bool  HasAnySchool            => _chosenSchools.Count > 0;
        public static bool  HasSchool(ColorSchool school) => _chosenSchools.Contains(school);
        public static IEnumerable<ColorSchool> AllSchools => _chosenSchools;
        public static float PurpleFertilityLevel    => _purpleFertilityLevel;

        // Returns the % chance that a player formation order is scrambled by madness.
        // 5% for non-contiguous colours, 10% for 5 colours, 20% for 6 colours.
        public static int GetMadnessOrderChance()
        {
            int count = _chosenSchools.Count;
            if (count == 6) return 20;
            if (count == 5) return 10;
            if (count >= 2)
            {
                var positions = _chosenSchools.Select(s => (int)s).ToHashSet();
                const int n = 6;
                foreach (int start in positions)
                {
                    bool ok = true;
                    for (int step = 0; step < positions.Count; step++)
                        if (!positions.Contains((start + step) % n)) { ok = false; break; }
                    if (ok) return 0; // contiguous — no order madness
                }
                return 5; // non-contiguous
            }
            return 0;
        }

        public static bool ReducePurpleFertility()
        {
            if (_purpleFertilityLevel <= 0.01f) return false;
            _purpleFertilityLevel = Math.Max(0.01f, _purpleFertilityLevel - 0.05f);
            return true;
        }

        public static void AddSchool(ColorSchool school)
        {
            _chosenSchools.Add(school);
        }

        public static bool IsChildGifted(string heroId) => _giftedChildIds.Contains(heroId);
        public static void AddGiftedChild(string heroId) => _giftedChildIds.Add(heroId);

        // Called after each successful cast to drift personality and track stats
        public static void RecordCast(ColorSchool school)
        {
            if (!_castCounters.ContainsKey(school)) return;
            _castCounters[school]++;

            if (_castCounters[school] % 25 != 0) return;

            // Shift personality trait
            try
            {
                var (trait, direction) = ColorSchoolData.GetTraitEffect(school);
                if (trait == null || direction == 0 || Hero.MainHero == null) return;
                int current = Hero.MainHero.GetTraitLevel(trait);
                int next = Math.Max(-2, Math.Min(2, current + direction));
                if (next != current)
                {
                    Hero.MainHero.SetTraitLevel(trait, next);
                    string dir = direction > 0 ? "increased" : "decreased";
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Your {trait.StringId} trait has {dir} from repeated {ColorSchoolData.Info[school].Name} casting.",
                        ColorSchoolData.GetMessageColor(school)));
                }
            }
            catch { }
        }

        public static void ShowGrimoire()
        {
            if (!HasAnySchool)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "No colour calls to you.", Color.FromUint(0xFFAAAAAA)));
                return;
            }

            var known = SpellDatabase.All
                .Where(s => HasSchool(s.School))
                .ToList();

            int totalPages = (known.Count + GrimoirePageSize - 1) / GrimoirePageSize;
            _grimoirePage  = _grimoirePage % totalPages;
            var page       = known.Skip(_grimoirePage * GrimoirePageSize).Take(GrimoirePageSize).ToList();

            InformationManager.DisplayMessage(new InformationMessage(
                $"══ Spellbook — Page {_grimoirePage + 1}/{totalPages} ══",
                new Color(0.8f, 0.8f, 0.8f)));

            foreach (SpellEntry s in page)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"  [{s.Combo}]  {s.Name}  ({ColorSchoolData.Info[s.School].Name})",
                    ColorSchoolData.GetMessageColor(s.School)));
            }

            if (totalPages > 1)
                InformationManager.DisplayMessage(new InformationMessage(
                    "[ Press S / L3 again for next page ]",
                    new Color(0.5f, 0.5f, 0.5f)));

            _grimoirePage = (_grimoirePage + 1) % totalPages;
        }

        public static void Save(IDataStore store)
        {
            var schoolList = _chosenSchools.Select(s => (int)s).ToList();
            var ccKeys     = _castCounters.Keys.Select(k => (int)k).ToList();
            var ccVals     = _castCounters.Values.ToList();
            var giftedList = _giftedChildIds.ToList();

            store.SyncData("COC_ChosenSchools",    ref schoolList);
            store.SyncData("COC_CastCounterKeys",  ref ccKeys);
            store.SyncData("COC_CastCounterVals",  ref ccVals);
            store.SyncData("COC_GiftedChildIds",   ref giftedList);
            store.SyncData("COC_PurpleFertility",  ref _purpleFertilityLevel);

            _chosenSchools.Clear();
            if (schoolList != null)
                foreach (int v in schoolList) _chosenSchools.Add((ColorSchool)v);

            if (ccKeys != null && ccVals != null)
                for (int i = 0; i < Math.Min(ccKeys.Count, ccVals.Count); i++)
                    _castCounters[(ColorSchool)ccKeys[i]] = ccVals[i];

            _giftedChildIds.Clear();
            if (giftedList != null)
                foreach (var id in giftedList) _giftedChildIds.Add(id);
        }
    }

    // =========================================================================
    // 5. ACTIVE EFFECT MANAGER  — timed buffs & debuffs (unchanged from v1)
    // =========================================================================
    public class ActiveEffect
    {
        public string   Name;
        public float    Duration;
        public float    Elapsed;
        public bool     IsMissionEffect;
        public Action<float> OnTick;
        public Action   OnExpire;
        public bool IsExpired => Elapsed >= Duration;
    }

    public static class ActiveEffectManager
    {
        private static readonly List<ActiveEffect> _effects = new List<ActiveEffect>();
        private const int MaxEffects = 20;

        public static void Add(ActiveEffect e)
        {
            if (_effects.Count >= MaxEffects) return;
            _effects.Add(e);
        }

        public static bool Has(string name) =>
            _effects.Any(e => e.Name == name && !e.IsExpired);

        public static void Remove(string name) =>
            _effects.RemoveAll(e => e.Name == name);

        public static void MissionTick(float dt) => Tick(dt, true);
        public static void MapTick(float dt)     => Tick(dt, false);

        private static void Tick(float dt, bool inMission)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                var e = _effects[i];
                if (e.IsMissionEffect != inMission) continue;
                e.Elapsed += dt;
                try { e.OnTick?.Invoke(dt); } catch { }
                if (!e.IsExpired) continue;
                try { e.OnExpire?.Invoke(); } catch { }
                _effects.RemoveAt(i);
            }
        }

        public static void ClearMissionEffects() =>
            _effects.RemoveAll(e => e.IsMissionEffect);
    }

    // =========================================================================
    // 6. CAMPAIGN BEHAVIOR  — save/load, character creation, daily events
    // =========================================================================
    public class MagicCampaignBehavior : CampaignBehaviorBase
    {
        private bool _selectionDone;

        private static readonly ColorSchool[] _allSchoolsOrdered =
        {
            ColorSchool.Red, ColorSchool.Orange, ColorSchool.Yellow,
            ColorSchool.Green, ColorSchool.Blue, ColorSchool.Purple
        };

        // ── Daily overindulgent food drain (Orange B1) ───────────────────────
        // How many extra food units consumed per day per Orange caster
        private const int OverindulgentFoodDrain = 2;
        // Party size soft-penalty threshold (Yellow C1 Uncharismatic)
        private const int UncharismaticPenaltyThreshold = 3;

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnMissionEnded);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.HeroCreated.AddNonSerializedListener(this, OnHeroCreated);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, OnCompanionAdded);
            CampaignEvents.HeroLevelledUp.AddNonSerializedListener(this, OnHeroLevelledUp);
        }

        // ── New game: present colour selection ───────────────────────────────
        private void OnNewGameCreated()
        {
            var elements = _allSchoolsOrdered.Select(school =>
            {
                var info = ColorSchoolData.Info[school];
                string hint = $"{info.FlavorText}\n\nPenalty: {info.AttributePenalty}\n{info.LimitationA}\n{info.LimitationB}";
                return new InquiryElement(school, info.Name, null, true, hint);
            }).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Colours of Calradia",
                "Magic flows through the world in colours. Choose which colours call to you — or choose none to walk an uncoloured path. Hover each colour to read its penalties.",
                elements,
                false, 0, 6,
                "These colours call to me.",
                "No colour calls to me.",
                chosen =>
                {
                    var schools = chosen?.Select(e => (ColorSchool)e.Identifier).ToList()
                                  ?? new List<ColorSchool>();
                    if (schools.Count == 0)
                        InformationManager.DisplayMessage(new InformationMessage(
                            "No colour calls to you. You walk an uncoloured path.", Color.FromUint(0xFFAAAAAA)));
                    else
                    {
                        foreach (var s in schools) ColourKnowledge.AddSchool(s);
                        ApplySchoolPenalties(schools);
                        ShowStartingSpells(schools);
                    }
                    _selectionDone = true;
                    ColourLordRegistry.SeedInitialLords();
                },
                _ =>
                {
                    _selectionDone = true;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "No colour calls to you. You walk an uncoloured path.", Color.FromUint(0xFFAAAAAA)));
                    ColourLordRegistry.SeedInitialLords();
                },
                "", false
            ), false, true);
        }

        private void ApplySchoolPenalties(List<ColorSchool> schools)
        {
            Hero player = Hero.MainHero;
            if (player == null) return;

            foreach (ColorSchool school in schools)
            {
                var info = ColorSchoolData.Info[school];
                try
                {
                    CharacterAttribute attr = ColorSchoolData.GetPenaltyAttribute(school);
                    player.HeroDeveloper.AddAttribute(attr, -1, false);
                }
                catch { }

                InformationManager.DisplayMessage(new InformationMessage(
                    $"{info.Name} — {info.AttributePenalty}. The mark your gift left on your body.",
                    ColorSchoolData.GetMessageColor(school)));
            }

            // Madness — colours that are not adjacent on the ring (Red-Orange-Yellow-Green-Blue-Purple)
            // create an internal conflict that fractures the mage's personality.
            if (schools.Count >= 2 && !AreColoursContiguous(schools))
            {
                ApplyMadness(player);
                MBInformationManager.AddQuickInformation(
                    new TextObject("MADNESS — The colours you wield are incompatible. Their conflicting natures fracture your sense of self. Two personality traits have shifted. You will not recover them."),
                    8000, null, null, "");
            }
            // Five or more colours — madness regardless of adjacency, and the fracture never heals.
            if (schools.Count > 4)
            {
                ApplyMadness(player);
                MBInformationManager.AddQuickInformation(
                    new TextObject("THE FRACTURE — No mind can hold five colours whole. Something in you breaks — and it will keep breaking, week after week, for as long as you walk with this burden."),
                    8000, null, null, "");
            }
        }

        // Returns true if the chosen schools form a contiguous arc on the colour ring.
        // Ring order: Red(0)-Orange(1)-Yellow(2)-Green(3)-Blue(4)-Purple(5), wraps around.
        private bool AreColoursContiguous(List<ColorSchool> schools)
        {
            var positions = schools.Select(s => (int)s).ToHashSet();
            int n = 6;
            // Try each chosen colour as arc start; walk forward checking all are present
            foreach (int start in positions)
            {
                bool allPresent = true;
                for (int step = 0; step < positions.Count; step++)
                {
                    if (!positions.Contains((start + step) % n)) { allPresent = false; break; }
                }
                if (allPresent) return true;
            }
            return false;
        }

        private static readonly TraitObject[] MadnessTraits =
        {
            DefaultTraits.Mercy, DefaultTraits.Valor, DefaultTraits.Honor,
            DefaultTraits.Generosity, DefaultTraits.Calculating
        };

        private void ApplyMadness(Hero player)
        {
            var rng = new Random();
            var shuffled = MadnessTraits.OrderBy(_ => rng.Next()).Take(2).ToList();
            foreach (TraitObject trait in shuffled)
            {
                try
                {
                    int current = player.GetTraitLevel(trait);
                    int shift   = rng.Next(2) == 0 ? 1 : -1;
                    int next    = Math.Max(-2, Math.Min(2, current + shift));
                    if (next != current)
                    {
                        player.SetTraitLevel(trait, next);
                        string dir = shift > 0 ? "increased" : "decreased";
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Madness: Your {trait.StringId} trait has {dir} ({next:+0;-0}).",
                            Color.FromUint(0xFFCC44FF)));
                    }
                }
                catch { }
            }
        }

        private void ShowStartingSpells(List<ColorSchool> schools)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "Your colours are chosen. Hold Left Alt and type a 4-key combo (WASD) then release to cast. S opens spellbook.",
                new Color(0.8f, 0.8f, 0.8f)));

            foreach (ColorSchool school in schools)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"── {ColorSchoolData.Info[school].Name} spells ──",
                    ColorSchoolData.GetMessageColor(school)));

                foreach (SpellEntry s in SpellDatabase.BySchool(school))
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"  [{s.Combo}]  {s.Name}",
                        ColorSchoolData.GetMessageColor(school)));
                }
            }
        }

        // ── Daily tick ───────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (!_selectionDone)
                _selectionDone = true;

            ColourLordRegistry.SeedInitialLords();
            ColourLordRegistry.FlushAnnouncements();
            ColourLordRegistry.DailyMapCast();
            ColourUnitRegistry.SeedInitialUnits();
            ColourUnitRegistry.DailyMaintenance();
            ColourUnitRegistry.DailyMapCast();
            ApplyDailyLimitations();
        }

        private void ApplyDailyLimitations()
        {
            if (!ColourKnowledge.HasAnySchool) return;
            Hero player = Hero.MainHero;
            MobileParty party = MobileParty.MainParty;
            if (player == null || party == null) return;

            // B1 Overindulgent (Orange) — extra food drain
            if (ColourKnowledge.HasSchool(ColorSchool.Orange))
            {
                try
                {
                    ItemObject food = MBObjectManager.Instance.GetObject<ItemObject>("grain")
                                   ?? MBObjectManager.Instance.GetObject<ItemObject>("fish");
                    if (food != null && party.ItemRoster.FindIndexOfItem(food) >= 0)
                    {
                        party.ItemRoster.AddToCounts(food, -Math.Min(OverindulgentFoodDrain,
                            party.ItemRoster.GetItemNumber(food)));
                    }
                }
                catch { }
            }

            // C1 Uncharismatic (Yellow) — morale drain if party is near its limit
            if (ColourKnowledge.HasSchool(ColorSchool.Yellow))
            {
                try
                {
                    int partySize = party.MemberRoster.TotalManCount;
                    int limit     = party.Party.PartySizeLimit;
                    // If party within 3 of its natural limit, impose morale penalty
                    if (limit - partySize <= UncharismaticPenaltyThreshold)
                        party.RecentEventsMorale -= 3f;
                }
                catch { }
            }
        }

        // ── Hourly tick ──────────────────────────────────────────────────────
        private void OnHourlyTick()
        {
            ColourLordRegistry.CheckRespawnTimers();
        }

        // ── Weekly tick ──────────────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            if (ColourKnowledge.AllSchools.Count() <= 4) return;
            Hero player = Hero.MainHero;
            if (player == null) return;
            // Five or more colours — the fracture never heals; traits shift without warning each week
            var rng = new Random();
            bool anyChanged = false;
            foreach (TraitObject trait in MadnessTraits)
            {
                try
                {
                    int next    = rng.Next(5) - 2; // −2 to +2
                    int current = player.GetTraitLevel(trait);
                    if (next != current)
                    {
                        player.SetTraitLevel(trait, next);
                        anyChanged = true;
                    }
                }
                catch { }
            }
            if (anyChanged)
                InformationManager.DisplayMessage(new InformationMessage(
                    "The Fracture: Five colours tear at your sense of self — your personality shifts without warning.",
                    Color.FromUint(0xFFCC44FF)));
        }

        // ── Mission ended ────────────────────────────────────────────────────
        private void OnMissionEnded(IMission mission)
        {
            ActiveEffectManager.ClearMissionEffects();
            ColourLordAI.ClearCooldowns();
            SpellEffects.ClearAreaEffects();   // removes all engine lights + particle emitters
            SpellEffects.ClearSelfEffects();
            SpellEffects.ClearGlows();
            SpellEffects.ClearMoves();         // releases stale agent refs from Bastion pushes
            ColourUnitRegistry.OnMissionEnded();
        }

        // ── Hero killed ──────────────────────────────────────────────────────
        private void OnHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (!ColourLordRegistry.IsColourLord(victim)) return;
            ColourLordRegistry.OnLordDied(victim);
        }

        // ── Children inherit colours ─────────────────────────────────────────
        private void OnHeroCreated(Hero hero, bool bornNaturally)
        {
            if (!bornNaturally || !ColourKnowledge.HasAnySchool) return;
            try
            {
                bool parentIsPlayer = hero.Mother == Hero.MainHero || hero.Father == Hero.MainHero;
                if (!parentIsPlayer) return;
                if (MBRandom.RandomInt(100) >= 30) return;

                var parentSchools = ColourKnowledge.AllSchools.ToList();
                int parentCount   = parentSchools.Count;

                // Child gets parent count ± 1, clamped to [1, 6]
                int delta      = MBRandom.RandomInt(3) - 1; // -1, 0, or +1
                int childCount = Math.Max(1, Math.Min(6, parentCount + delta));

                // Always share at least one colour with parent
                var childSchools = new List<ColorSchool>();
                childSchools.Add(parentSchools[MBRandom.RandomInt(parentSchools.Count)]);

                // Fill remaining slots from schools the child doesn't yet have
                var pool = ((ColorSchool[])Enum.GetValues(typeof(ColorSchool)))
                    .Where(s => !childSchools.Contains(s)).ToList();
                while (childSchools.Count < childCount && pool.Count > 0)
                {
                    int idx = MBRandom.RandomInt(pool.Count);
                    childSchools.Add(pool[idx]);
                    pool.RemoveAt(idx);
                }

                ColourLordRegistry.GrantChildColours(hero, childSchools);
                ColourKnowledge.AddGiftedChild(hero.StringId);

                string schoolNames = string.Join(", ",
                    childSchools.Select(s => ColorSchoolData.Info[s].Name));
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{hero.Name} was born carrying {childCount} colour{(childCount > 1 ? "s" : "")}: {schoolNames}.",
                    new Color(0.75f, 0.75f, 0.85f)));
            }
            catch { }
        }

        // ── Level-up colour learning ──────────────────────────────────────────
        private bool _levelUpPickActive;

        private void OnHeroLevelledUp(Hero hero, bool shouldNotify)
        {
            if (hero != Hero.MainHero) return;
            if (!ColourKnowledge.HasAnySchool) return;
            if (hero.Level % 10 != 0) return;
            if (ColourKnowledge.AllSchools.Count() >= 6) return;
            if (_levelUpPickActive) return;

            var available = _allSchoolsOrdered.Where(s => !ColourKnowledge.HasSchool(s)).ToList();
            if (available.Count == 0) return;

            _levelUpPickActive = true;

            var elements = available.Select(school =>
            {
                var info = ColorSchoolData.Info[school];
                string hint = $"{info.FlavorText}\n\nPenalty: {info.AttributePenalty}\n{info.LimitationA}\n{info.LimitationB}";
                return new InquiryElement(school, info.Name, null, true, hint);
            }).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Level {hero.Level} — A Colour Stirs",
                "Your growth opens new channels. Choose one colour to learn — or decline. Hover each colour to read its penalties.",
                elements,
                false, 0, 1,
                "This colour calls to me.",
                "Not now.",
                chosen =>
                {
                    _levelUpPickActive = false;
                    if (chosen?.Count > 0)
                    {
                        ColorSchool school = (ColorSchool)chosen[0].Identifier;
                        ColourKnowledge.AddSchool(school);
                        ApplySchoolPenalties(new List<ColorSchool> { school });
                        ShowStartingSpells(new List<ColorSchool> { school });
                    }
                    else
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The colour recedes. No new school learned.", Color.FromUint(0xFFAAAAAA)));
                },
                _ =>
                {
                    _levelUpPickActive = false;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The colour recedes. No new school learned.", Color.FromUint(0xFFAAAAAA)));
                },
                "", false
            ), false, true);
        }

        // ── Companions may have colours ──────────────────────────────────────
        private void OnCompanionAdded(Hero companion)
        {
            ColourLordRegistry.TryGrantCompanionColours(companion);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("COC_SelectionDone", ref _selectionDone);
            ColourKnowledge.Save(dataStore);
            ColourLordRegistry.Save(dataStore);
            ColourUnitRegistry.Save(dataStore);
        }
    }

    // =========================================================================
    // 7. INPUT HANDLER  — 4-key combo system (focus = Left Alt / LT)
    // =========================================================================
    public static class MagicInputHandler
    {
        private static string _buffer              = "";
        private static bool   _wasFocusing         = false;
        private static string _lastDisplayedBuffer = "";
        private const  int    MaxLen               = 10;

        // Previous-frame state for R-stick directions (IsKeyPressed unreliable for analog axes)
        private static bool _prevRUp, _prevRDown, _prevRLeft, _prevRRight;

        public static bool InputSuppressed { get; private set; }

        public static void Tick(bool inMission)
        {
            if (!ColourKnowledge.HasAnySchool) { InputSuppressed = false; return; }

            // ControllerLTrigger takes priority — Bannerlord maps L-trigger to LeftAlt at the
            // OS level, so both can be true simultaneously when using a controller. Checking
            // the controller key first and excluding it from the keyboard path prevents face
            // buttons (Y→W, X→A, A→S) from bleeding into the spell buffer.
            bool focusingPad = Input.IsKeyDown(InputKey.ControllerLTrigger);
            bool focusingKb  = Input.IsKeyDown(InputKey.LeftAlt) && !focusingPad;
            bool focusing    = focusingKb || focusingPad;

            InputSuppressed = focusing;

            if (focusing)
            {
                _wasFocusing = true;

                if (!inMission && Campaign.Current != null)
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.UnstoppablePlay;

                // Keyboard path: only when Left Alt is the focus key, so controller face-button
                // virtual keys (Y→W, X→A, A→S) don't bleed into the spell buffer.
                if (focusingKb)
                {
                    if      (Input.IsKeyPressed(InputKey.W)) Append("U");
                    else if (Input.IsKeyPressed(InputKey.A)) Append("L");
                    else if (Input.IsKeyPressed(InputKey.D)) Append("R");
                    else if (Input.IsKeyPressed(InputKey.S))
                    {
                        if (_buffer.Length == 0) ColourKnowledge.ShowGrimoire();
                        else Append("D");
                    }
                }

                // Gamepad: R-stick directions via manual edge detection (IsKeyDown + prev-state)
                // Only runs when L-trigger is the focus key.
                if (focusingPad)
                {
                    bool rUp    = Input.IsKeyDown(InputKey.ControllerRStickUp);
                    bool rDown  = Input.IsKeyDown(InputKey.ControllerRStickDown);
                    bool rLeft  = Input.IsKeyDown(InputKey.ControllerRStickLeft);
                    bool rRight = Input.IsKeyDown(InputKey.ControllerRStickRight);

                    if (rUp    && !_prevRUp)   Append("U");
                    if (rDown  && !_prevRDown) { if (_buffer.Length == 0) ColourKnowledge.ShowGrimoire(); else Append("D"); }
                    if (rLeft  && !_prevRLeft) Append("L");
                    if (rRight && !_prevRRight) Append("R");

                    _prevRUp = rUp; _prevRDown = rDown; _prevRLeft = rLeft; _prevRRight = rRight;

                    if (Input.IsKeyPressed(InputKey.ControllerLThumb)) ColourKnowledge.ShowGrimoire();
                }

                if (_buffer.Length > 0 && _buffer != _lastDisplayedBuffer)
                {
                    _lastDisplayedBuffer = _buffer;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "[ " + _buffer + " ]", new Color(0.7f, 0.7f, 0.7f)));
                }
            }
            else if (_wasFocusing)
            {
                _wasFocusing = false;
                _prevRUp = _prevRDown = _prevRLeft = _prevRRight = false;

                if (!inMission && Campaign.Current != null)
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;

                _lastDisplayedBuffer = "";

                if (_buffer.Length >= 4)
                    TryCast(_buffer, inMission);
                else if (_buffer.Length > 0)
                    Fizzle("Incantation too short — colour magic requires four keys.");

                _buffer = "";
            }
        }

        private static void Append(string dir)
        {
            if (_buffer.Length < MaxLen) _buffer += dir;
        }

        private static void TryCast(string combo, bool inMission)
        {
            SpellEntry spell = SpellDatabase.Find(combo);
            if (spell == null) { Fizzle("The colours do not answer."); return; }

            // School access check
            if (!ColourKnowledge.HasSchool(spell.School))
            {
                Fizzle($"You have not chosen the {ColorSchoolData.Info[spell.School].Name} school.");
                return;
            }

            // Context check
            if (spell.Context == SpellContext.Mission && !inMission)
            { Fizzle($"{spell.Name} can only be cast in battle."); return; }
            if (spell.Context == SpellContext.Map && inMission)
            { Fizzle($"{spell.Name} can only be cast on the campaign map."); return; }

            // ── Pre-cast limitation checks ────────────────────────────────────

            // Global: all magic requires sunlight — the mage acts as a prism
            if (!SpellEffects.IsDaytime())
            {
                Fizzle("The colours require sunlight. Magic cannot be cast at night or underground.");
                return;
            }


            // Green — Pacifist: no weapon in hand
            if (spell.School == ColorSchool.Green && inMission && Agent.Main != null)
            {
                try
                {
                    var wielded = Agent.Main.WieldedWeapon;
                    bool hasWeapon = !wielded.IsEmpty &&
                        wielded.CurrentUsageItem?.WeaponClass != WeaponClass.Boulder &&
                        wielded.CurrentUsageItem?.IsShield != true;
                    if (hasWeapon)
                    {
                        Fizzle("Pacifist: Sheathe your weapon before casting Green magic.");
                        return;
                    }
                }
                catch { }
            }

            // ── Tournament guard ──────────────────────────────────────────────
            if (inMission && IsInTournament())
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Sorcery in the tournament — you are disqualified!",
                    Color.FromUint(0xFFFF4444)));
                try { SpellEffects.KillAgent(Agent.Main); } catch { }
                return;
            }

            // ── Cast ─────────────────────────────────────────────────────────
            InformationManager.DisplayMessage(new InformationMessage(
                $"[{ColorSchoolData.Info[spell.School].Name} — {spell.Name}]  {spell.Flavour}",
                ColorSchoolData.GetMessageColor(spell.School)));

            bool success = SpellEffects.Execute(combo);

            // Visual: caster glow + charge animation + sound
            if (inMission && Agent.Main != null)
                SpellEffects.CastGlow(Agent.Main, spell.School);

            if (!success) return;

            // ── Post-cast limitation side effects ─────────────────────────────

            // Red A1 — Furious: issue Charge to own formations
            if (spell.School == ColorSchool.Red && inMission && Agent.Main != null)
                SpellEffects.IssueChargeToOwnFormations(Agent.Main);

            // Red A2 — Blood Price: small self-damage
            if (spell.School == ColorSchool.Red && inMission && Agent.Main != null)
            {
                try { Agent.Main.Health = Math.Max(1f, Agent.Main.Health - 8f); }
                catch { }
            }

            // Yellow — Suspicious: party morale loss + criminal rating increase
            if (spell.School == ColorSchool.Yellow)
            {
                try { if (MobileParty.MainParty != null) MobileParty.MainParty.RecentEventsMorale -= 8f; }
                catch { }
                // Increase criminal rating in the kingdom where the spell is used
                try
                {
                    Kingdom crimKingdom = null;
                    if (inMission && Mission.Current != null)
                    {
                        foreach (Agent a in Mission.Current.Agents)
                        {
                            if (a.Character is CharacterObject co && co.IsHero && co.HeroObject != null
                                && a.Team != Mission.Current.PlayerTeam)
                            {
                                crimKingdom = co.HeroObject.Clan?.Kingdom;
                                if (crimKingdom != null) break;
                            }
                        }
                    }
                    crimKingdom = crimKingdom ?? Hero.MainHero?.Clan?.Kingdom;
                    if (crimKingdom != null)
                        ChangeCrimeRatingAction.Apply(crimKingdom, 3f, true);
                }
                catch { }
            }


            // Orange — Generous Hunger: stagger the caster in random directions
            if (spell.School == ColorSchool.Orange && inMission)
                SpellEffects.TriggerConfusion();

            // Blue — Scholar's Weight: equipment grows heavier each cast, limiting speed
            if (spell.School == ColorSchool.Blue && inMission)
                SpellEffects.ApplyBlueWeight();

            // Purple — Waning Cost: ages the caster ~2 days; also quietly reduces fertility
            if (spell.School == ColorSchool.Purple)
            {
                try
                {
                    Hero.MainHero?.SetBirthDay(Hero.MainHero.BirthDay - CampaignTime.Years(1f / 365f));
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Waning Cost: The grey takes its years. | Age: {(int)(Hero.MainHero?.Age ?? 0)}",
                        ColorSchoolData.GetMessageColor(ColorSchool.Purple)));
                }
                catch { }
                if (ColourKnowledge.ReducePurpleFertility())
                {
                    int pct = (int)(ColourKnowledge.PurpleFertilityLevel * 100f);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The Slow Unravelling: Something within grows quieter. Fertility: {pct}%",
                        ColorSchoolData.GetMessageColor(ColorSchool.Purple)));
                }
            }

            // Personality drift
            ColourKnowledge.RecordCast(spell.School);
        }

        private static bool IsInTournament()
        {
            if (Mission.Current == null) return false;
            foreach (MissionBehavior b in Mission.Current.MissionBehaviors)
                if (b.GetType().Name.Contains("Tournament")) return true;
            return false;
        }

        private static void Fizzle(string msg) =>
            InformationManager.DisplayMessage(new InformationMessage(
                msg, Color.FromUint(0xFF996644)));
    }

    // =========================================================================
    // 8. SPELL EFFECTS  — implementations for all 18 battle spells
    // =========================================================================
    public static class SpellEffects
    {
        private static readonly Random _rng = new Random();


        public static bool IsDaytime()
        {
            try
            {
                if (Campaign.Current == null) return true;
                float hour = (float)(CampaignTime.Now.ToHours % 24.0);
                return hour >= 6f && hour < 20f;
            }
            catch { return true; }
        }

        // Returns true only during actual combat missions (battles, sieges).
        // Town/village visits have no active enemy agents, so this reliably excludes them.
        public static bool IsBattleMission()
        {
            try
            {
                if (Mission.Current == null || Mission.Current.PlayerTeam == null) return false;
                Team playerTeam = Mission.Current.PlayerTeam;
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount || a.Team == null) continue;
                    if (a.Team != playerTeam && playerTeam.IsEnemyOf(a.Team)) return true;
                }
            }
            catch { }
            return false;
        }

        // ── Smooth movement system ────────────────────────────────────────────
        // Moves agents gradually toward a target over a set duration (lerp).
        // Used by push/pull spells for fluid visual movement.
        private struct PendingMove
        {
            public Agent Agent; public Vec3 Start; public Vec3 Target;
            public float Duration; public float Elapsed;
        }
        private static readonly List<PendingMove> _pendingMoves = new List<PendingMove>();

        public static void QueueMove(Agent a, Vec3 target, float duration)
        {
            if (a == null) return;
            for (int i = _pendingMoves.Count - 1; i >= 0; i--)
                if (_pendingMoves[i].Agent == a) _pendingMoves.RemoveAt(i);
            _pendingMoves.Add(new PendingMove { Agent = a, Start = a.Position, Target = target, Duration = duration, Elapsed = 0f });
        }

        public static void TickMoves(float dt)
        {
            for (int i = _pendingMoves.Count - 1; i >= 0; i--)
            {
                var m = _pendingMoves[i];
                if (m.Agent == null || !m.Agent.IsActive()) { _pendingMoves.RemoveAt(i); continue; }
                float elapsed = m.Elapsed + dt;
                float t = Math.Min(elapsed / m.Duration, 1f);
                float smooth = t * t * (3f - 2f * t); // smoothstep
                Vec3 pos = m.Start + (m.Target - m.Start) * smooth;
                pos.z = m.Agent.Position.z;
                try { m.Agent.TeleportToPosition(pos); } catch { }
                if (elapsed >= m.Duration) _pendingMoves.RemoveAt(i);
                else _pendingMoves[i] = new PendingMove { Agent = m.Agent, Start = m.Start, Target = m.Target, Duration = m.Duration, Elapsed = elapsed };
            }
        }

        public static void ClearMoves()
        {
            _pendingMoves.Clear();
        }

        // ── Persistent area effects ────────────────────────────────────────────
        // Create spells place lasting effects on the field; each ticks on its own interval.
        internal class AreaEffect
        {
            public string Id;           // Unique ID for toggling (e.g. "create_orange")
            public Vec3   Position;
            public Vec3   Velocity;     // For moving effects (Create Yellow)
            public float  Radius;
            public ColorSchool School;
            public float  TickInterval;
            public float  TickTimer;
            public float  Remaining;    // negative = no expiry (toggle-only)
            public float  DirTimer;     // Create Yellow direction-change timer
            public GameEntity LightEntity; // coloured point light marking the effect area
        }
        private static readonly List<AreaEffect> _areaEffects = new List<AreaEffect>();
        private static readonly HashSet<int> _bastionPrevInside = new HashSet<int>();
        private static readonly HashSet<int> _snarePrevInside   = new HashSet<int>();
        // AgentIndex → (seconds remaining, position frozen at cast time) for Azure Arrest halt
        private static readonly Dictionary<int, (float Remaining, Vec3 FrozenPos)> _haltedAgents
            = new Dictionary<int, (float, Vec3)>();
        private static float _haltTeleportTimer = 0f;
        private const  float HaltTeleportInterval = 0.25f;

        // If an effect with this id exists, remove it. Otherwise add newEffect (if not null).
        internal static void ToggleAreaEffect(string id, AreaEffect newEffect)
        {
            int idx = _areaEffects.FindIndex(e => e.Id == id);
            if (idx >= 0)
            {
                try { _areaEffects[idx].LightEntity?.Remove(0); } catch { }
                _areaEffects.RemoveAt(idx);
                if (id == "create_blue")   _bastionPrevInside.Clear();
                if (id == "create_orange") _snarePrevInside.Clear();
                return;
            }
            if (newEffect != null)
            {
                newEffect.LightEntity = SpawnAreaLight(newEffect.Position, newEffect.School, newEffect.Radius);
                _areaEffects.Add(newEffect);
            }
        }

        public static void RemoveAreaEffect(string id)
        {
            foreach (var e in _areaEffects.Where(e => e.Id == id).ToList())
                try { e.LightEntity?.Remove(0); } catch { }
            _areaEffects.RemoveAll(e => e.Id == id);
        }

        public static bool HasAreaEffect(string id) => _areaEffects.Any(e => e.Id == id);

        private static GameEntity SpawnAreaLight(Vec3 position, ColorSchool school, float radius)
        {
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene == null) return null;
                var entity = GameEntity.CreateEmpty(scene, true, false, false);
                var frame  = new MatrixFrame(Mat3.Identity, position + new Vec3(0f, 0f, 0.5f));
                entity.SetGlobalFrame(ref frame, true);
                var light = Light.CreatePointLight(radius);
                light.Radius        = radius;
                light.Intensity     = 10000f;
                light.LightColor    = SchoolToLightColor(school);
                light.ShadowEnabled = false;
                entity.AddLight(light);
                // Particles are safe now that ClearAreaEffects() is called on mission end.
                // Previously they accumulated across missions and caused GPU overheating.
                try { entity.AddParticleSystemComponent(SchoolToParticle(school)); } catch { }
                return entity;
            }
            catch { return null; }
        }

        private static string SchoolToParticle(ColorSchool school)
        {
            // Named particle systems from the base game — wrapped in try/catch at call site
            switch (school)
            {
                case ColorSchool.Red:    return "psys_campfire_a";
                case ColorSchool.Orange: return "psys_campfire_a";
                case ColorSchool.Yellow: return "psys_torch_fire_small_a";
                case ColorSchool.Green:  return "psys_torch_fire_small_a";
                case ColorSchool.Blue:   return "psys_torch_fire_small_a";
                case ColorSchool.Purple: return "psys_campfire_a";
                default:                 return "psys_campfire_a";
            }
        }

        private static Vec3 SchoolToLightColor(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return new Vec3(1f,    0.15f, 0.05f);
                case ColorSchool.Orange: return new Vec3(1f,    0.50f, 0.05f);
                case ColorSchool.Yellow: return new Vec3(1f,    0.90f, 0.10f);
                case ColorSchool.Green:  return new Vec3(0.15f, 0.80f, 0.15f);
                case ColorSchool.Blue:   return new Vec3(0.10f, 0.35f, 1f);
                case ColorSchool.Purple: return new Vec3(0.60f, 0.10f, 0.90f);
                default:                 return new Vec3(1f,    1f,    1f);
            }
        }

        public static void TickAreaEffects(float dt)
        {
            if (Mission.Current == null) return;
            for (int i = _areaEffects.Count - 1; i >= 0; i--)
            {
                var e = _areaEffects[i];
                if (e.Remaining >= 0f)
                {
                    e.Remaining -= dt;
                    if (e.Remaining <= 0f)
                    {
                        try { e.LightEntity?.Remove(0); } catch { }
                        _areaEffects.RemoveAt(i);
                        continue;
                    }
                }

                // Yellow clouds drift randomly from their spawn position
                if (e.Id == "create_yellow" || e.Id == "self_yellow")
                {
                    e.DirTimer -= dt;
                    if (e.DirTimer <= 0f)
                    {
                        float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                        e.Velocity = new Vec3((float)Math.Cos(angle) * 2f, (float)Math.Sin(angle) * 2f, 0f);
                        e.DirTimer = 3f + (float)_rng.NextDouble() * 4f;
                    }
                    e.Position += e.Velocity * dt;
                }

                // Keep light anchored to current effect position (matters for moving effects)
                if (e.LightEntity != null)
                {
                    try
                    {
                        var lf = new MatrixFrame(Mat3.Identity, e.Position + new Vec3(0f, 0f, 0.5f));
                        e.LightEntity.SetGlobalFrame(ref lf, true);
                    }
                    catch { }
                }

                e.TickTimer -= dt;
                if (e.TickTimer > 0f) continue;
                e.TickTimer = e.TickInterval;

                // Apply the area effect this tick
                switch (e.Id)
                {
                    case "create_orange": // Golden Snare — one-shot random command on first contact
                    {
                        // Glow agents inside this node
                        foreach (Agent a in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount &&
                                        a.Position.Distance(e.Position) <= e.Radius).ToList())
                        {
                            try { BeginAgentGlow(a, e.School, 1.5f); } catch { }
                        }

                        // Find the first enemy inside this node not exempted at cast time.
                        // _snarePrevInside is set once at cast and never updated — one-shot trap.
                        Agent contact = null;
                        foreach (Agent a in Mission.Current.Agents)
                        {
                            if (!a.IsActive() || a.IsMount || a == Player) continue;
                            if (a.Team == Player?.Team) continue;
                            if (a.Position.Distance(e.Position) > e.Radius) continue;
                            if (_snarePrevInside.Contains(a.Index)) continue;
                            contact = a; break;
                        }
                        if (contact == null) break;

                        // Apply a random command to their formation
                        Formation f = contact.Formation;
                        var formationAgents = f == null ? new List<Agent>() :
                            Mission.Current.Agents.Where(a => a.IsActive() && a.Formation == f).ToList();
                        string cmdName;
                        switch (_rng.Next(4))
                        {
                            case 0: // Panic — drain morale (enemy AI cannot override morale)
                                foreach (Agent a in formationAgents) try { a.SetMorale(0f); } catch { }
                                cmdName = "Panic";
                                break;
                            case 1: // Enrage — max morale + charge order
                                foreach (Agent a in formationAgents) try { a.SetMorale(100f); } catch { }
                                if (f != null) try { f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
                                cmdName = "Enrage";
                                break;
                            case 2: // Dismount — unseat riders
                                foreach (Agent a in Mission.Current.Agents
                                    .Where(a => a.IsActive() && a.Formation == f && a.MountAgent != null).ToList())
                                {
                                    Vec3 dest = a.Position + new Vec3(1.5f, 0f, 0f);
                                    dest.z = a.Position.z;
                                    try { a.TeleportToPosition(dest); } catch { }
                                }
                                cmdName = "Dismount";
                                break;
                            default: // Scatter — full morale drain
                                foreach (Agent a in formationAgents) try { a.SetMorale(0f); } catch { }
                                cmdName = "Scatter";
                                break;
                        }
                        BeginAgentGlow(contact, e.School, 2f);
                        Msg($"Golden Snare — {contact.Name}'s formation receives a sudden command: {cmdName}!", ColorSchool.Orange);
                        // Expire all orange nodes (not just this one — 2x2 grid shares one ID)
                        foreach (var oe in _areaEffects.Where(x => x.Id == "create_orange").ToList())
                            oe.Remaining = 0.001f;
                        break;
                    }

                    case "create_yellow": // Creeping Dread — damage agents in cloud
                    {
                        int dreadHit = 0;
                        foreach (Agent a in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount &&
                                        a.Position.Distance(e.Position) <= e.Radius).ToList())
                        {
                            if (ProtectedByMirror(a)) continue;
                            try
                            {
                                const float DreadDmg = 30f;
                                if (a.Health <= DreadDmg)
                                {
                                    KillAgent(a);
                                }
                                else
                                {
                                    a.Health -= DreadDmg;
                                    try { a.SetMorale(Math.Max(0f, a.GetMorale() - 10f)); } catch { }
                                }
                                BeginAgentGlow(a, e.School, 1.5f);
                                dreadHit++;
                            }
                            catch { }
                        }
                        if (dreadHit > 0)
                            Msg($"Creeping Dread: {dreadHit} caught in the cloud. (−25 HP)", ColorSchool.Yellow);
                        break;
                    }

                    case "create_green": // Emerald Font — heal all agents in area
                        foreach (Agent a in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount &&
                                        a.Position.Distance(e.Position) <= e.Radius).ToList())
                        {
                            try
                            {
                                float h = Math.Min(10f, a.HealthLimit - a.Health);
                                if (h > 0f) { a.Health += h; BeginAgentGlow(a, e.School, 1.5f); }
                            }
                            catch { }
                        }
                        break;

                    case "create_blue": // Sapphire Bastion — push all agents inside the radius every tick
                    {
                        foreach (Agent a in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount &&
                                        a.Position.Distance(e.Position) <= e.Radius).ToList())
                        {
                            try
                            {
                                Vec3 dir = (a.Position - e.Position);
                                if (dir.Length < 0.01f) dir = new Vec3(1f, 0f, 0f);
                                else dir = dir.NormalizedCopy();
                                Vec3 dest = e.Position + dir * (e.Radius + 1f);
                                dest.z = a.Position.z;
                                QueueMove(a, dest, 0.3f);
                                BeginAgentGlow(a, e.School, 1.5f);
                            }
                            catch { }
                        }
                        break;
                    }

                    case "self_yellow": // Nausea Bloom — drifting toxic cloud
                    {
                        int bloomHit = 0;
                        foreach (Agent a in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount && a != Player &&
                                        a.Position.Distance(e.Position) <= e.Radius).ToList())
                        {
                            if (ProtectedByMirror(a)) continue;
                            try
                            {
                                float before = a.Health;
                                DamageAgent(a, 15f);
                                if (a.Health < before || a.Health <= 0f) bloomHit++;
                                BeginAgentGlow(a, e.School, 1.5f);
                            }
                            catch { }
                        }
                        if (bloomHit > 0)
                            Msg($"Nausea Bloom: {bloomHit} caught in the cloud. (−15 HP)", ColorSchool.Yellow);
                        break;
                    }
                }
            }
        }

        public static void ClearAreaEffects()
        {
            foreach (var e in _areaEffects)
                try { e.LightEntity?.Remove(0); } catch { }
            _areaEffects.Clear();
            _bastionPrevInside.Clear();
            _snarePrevInside.Clear();
            _haltedAgents.Clear();
        }

        // ── Duration self-effects ────────────────────────────────────────────
        // Invulnerability states for Self Red (Scarlet Ward) and Self Blue (Cerulean Mirror)
        private static bool  _scarletWardActive   = false;
        public  static bool  ScarletWardActive    => _scarletWardActive;
        private static bool  _ceruleanMirrorActive = false;
        private static bool  _shadowVeilActive     = false;
        private static Agent _hollowGazeTarget     = null;
        private static float _hollowGazeTimer      = 0f;
        private const  float HollowGazeInterval    = 0.3f;

        // Returns true when the agent is the player and Cerulean Mirror is blocking magic
        public static bool ProtectedByMirror(Agent a) => a == Player && _ceruleanMirrorActive;

        public static void TickHollowGaze(float dt)
        {
            if (_hollowGazeTarget == null) return;
            if (!_hollowGazeTarget.IsActive()) { _hollowGazeTarget = null; return; }
            _hollowGazeTimer -= dt;
            if (_hollowGazeTimer > 0f) return;
            _hollowGazeTimer = HollowGazeInterval;
            Vec3 pos = _hollowGazeTarget.Position;
            try { _hollowGazeTarget.TeleportToPosition(pos); } catch { }
            try { _hollowGazeTarget.SetMorale(0f); } catch { }
        }

        // ── Orange: confusion stagger ─────────────────────────────────────────
        private static float _confusionRemaining  = 0f;
        private static float _confusionTickTimer  = 0f;
        private const  float ConfusionDuration    = 1.5f;
        private const  float ConfusionTickRate    = 0.35f;
        private const  float ConfusionStaggerDist = 1.5f;

        public static void TriggerConfusion()
        {
            _confusionRemaining = ConfusionDuration;
            _confusionTickTimer = 0f;
            Msg("Generous Hunger seizes you — your body lurches as the magic tears through.", ColorSchool.Orange);
        }

        public static void TickHUDConfusion(float dt)
        {
            if (_confusionRemaining <= 0f) return;
            _confusionRemaining -= dt;
            _confusionTickTimer -= dt;
            if (_confusionTickTimer > 0f) return;
            _confusionTickTimer = ConfusionTickRate;
            if (Player == null || !Player.IsActive()) return;
            float angle = (float)(_rng.NextDouble() * Math.PI * 2);
            Vec3 dest = Player.Position + new Vec3(
                (float)Math.Cos(angle) * ConfusionStaggerDist,
                (float)Math.Sin(angle) * ConfusionStaggerDist,
                0f);
            dest.z = Player.Position.z;
            try { Player.TeleportToPosition(dest); } catch { }
        }

        // ── Blue: accumulating weight stacks ─────────────────────────────────
        private static int   _blueWeightStacks = 0;
        private const  float BlueBaseSpeed     = 6.5f;
        private const  float BlueSpeedPenalty  = 0.4f;
        private const  int   BlueMaxStacks     = 6;

        public static void ApplyBlueWeight()
        {
            if (Player == null || !Player.IsActive()) return;
            _blueWeightStacks = Math.Min(_blueWeightStacks + 1, BlueMaxStacks);
            EnforceBlueWeightCap();
            InformationManager.DisplayMessage(new InformationMessage(
                $"Scholar's Weight: The knowledge settles in your limbs. [{_blueWeightStacks}/{BlueMaxStacks}]",
                ColorSchoolData.GetMessageColor(ColorSchool.Blue)));
        }

        private static void EnforceBlueWeightCap()
        {
            if (Player == null || !Player.IsActive() || _blueWeightStacks <= 0) return;
            float cap = Math.Max(1.5f, BlueBaseSpeed - _blueWeightStacks * BlueSpeedPenalty);
            try { Player.SetMaximumSpeedLimit(cap, false); } catch { }
        }

        public static void TickBlueWeight(float dt)
        {
            if (_blueWeightStacks > 0) EnforceBlueWeightCap();
        }

        public static void TickHaltedAgents(float dt)
        {
            if (_haltedAgents.Count == 0 || Mission.Current == null) return;
            _haltTeleportTimer -= dt;
            bool doTeleport = _haltTeleportTimer <= 0f;
            if (doTeleport) _haltTeleportTimer = HaltTeleportInterval;

            var expired = new List<int>();
            foreach (int idx in _haltedAgents.Keys.ToList())
            {
                var (remaining, frozenPos) = _haltedAgents[idx];
                remaining -= dt;
                Agent a = Mission.Current.Agents.FirstOrDefault(x => x.Index == idx);
                if (a == null || !a.IsActive() || a.Health <= 0f)
                {
                    expired.Add(idx);
                    continue;
                }
                if (remaining <= 0f)
                {
                    expired.Add(idx);
                    try { a.SetMaximumSpeedLimit(10f, false); } catch { }
                }
                else
                {
                    _haltedAgents[idx] = (remaining, frozenPos);
                    try { a.SetMaximumSpeedLimit(0f, false); } catch { }
                    if (doTeleport) try { a.TeleportToPosition(frozenPos); } catch { }
                }
            }
            foreach (int idx in expired) _haltedAgents.Remove(idx);
        }

        public static void ClearSelfEffects()
        {
            if (_scarletWardActive)   { _scarletWardActive = false; }
            if (_ceruleanMirrorActive) { _ceruleanMirrorActive = false; }
            _shadowVeilActive  = false;
            _hollowGazeTarget  = null;
            _confusionRemaining = 0f;
            if (_blueWeightStacks > 0)
            {
                _blueWeightStacks = 0;
                if (Player?.IsActive() == true)
                    try { Player.SetMaximumSpeedLimit(float.MaxValue, false); } catch { }
            }
            _haltedAgents.Clear();
        }

        // Issue Charge to own formations (Red post-cast limitation)
        public static void IssueChargeToOwnFormations(Agent caster)
        {
            if (caster == null || Mission.Current == null || caster.Team == null) return;
            try
            {
                foreach (Team t in Mission.Current.Teams)
                {
                    if (t != caster.Team) continue;
                    foreach (Formation f in t.FormationsIncludingSpecialAndEmpty)
                    {
                        if (f.CountOfUnits <= 0) continue;
                        try { f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
                    }
                }
            }
            catch { }
        }

        // Random unit magic timer
        private static float _randomMagicTimer = 0f;
        private const  float RandomMagicInterval = 60f;

        public static void TickRandomUnitMagic(float dt)
        {
            if (Mission.Current == null) return;
            _randomMagicTimer += dt;
            if (_randomMagicTimer < RandomMagicInterval) return;
            _randomMagicTimer = 0f;

            if (!IsBattleMission()) return;

            // 0.7% chance per minute
            if (_rng.Next(1000) >= 7) return;

            var candidates = Mission.Current.Agents
                .Where(a => a.IsActive() && !a.IsMount && !a.IsHero).ToList();
            if (candidates.Count == 0) return;

            Agent unit  = candidates[_rng.Next(candidates.Count)];
            var schools = Enum.GetValues(typeof(ColorSchool));
            ColorSchool school = (ColorSchool)schools.GetValue(_rng.Next(schools.Length));

            try
            {
                BeginAgentGlow(unit, school, 3f);
                // Simple effect: small area damage if it's a combat school
                if (school == ColorSchool.Red || school == ColorSchool.Purple)
                {
                    foreach (Agent near in Mission.Current.Agents
                        .Where(a => a != unit && a.IsActive() && !a.IsMount &&
                                    a.Team != unit.Team &&
                                    a.Position.Distance(unit.Position) <= 5f).ToList())
                    {
                        DamageAgent(near, 20f);
                        BeginAgentGlow(near, school, 1.5f);
                    }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"A flicker of {ColorSchoolData.Info[school].Name} colour — {unit.Name} unleashes something wild.",
                        ColorSchoolData.GetMessageColor(school)));
                }
                else
                {
                    // Supportive schools: small morale or HP burst
                    if (unit.Team != null)
                    {
                        foreach (Agent near in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount && a.Team == unit.Team &&
                                        a.Position.Distance(unit.Position) <= 5f).ToList())
                        {
                            near.Health = Math.Min(near.HealthLimit, near.Health + 15f);
                            BeginAgentGlow(near, school, 1.5f);
                        }
                    }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"A flicker of {ColorSchoolData.Info[school].Name} colour ripples through {unit.Name}.",
                        ColorSchoolData.GetMessageColor(school)));
                }
            }
            catch { }
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private static Agent Player => Agent.Main;

        private static IEnumerable<Agent> Enemies()
        {
            if (Mission.Current == null || Player == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != Player && !a.IsMount && a.IsActive() &&
                    a.Team != null && a.Team != Player.Team)
                    yield return a;
        }

        private static IEnumerable<Agent> Allies()
        {
            if (Mission.Current == null || Player == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != Player && !a.IsMount && a.IsActive() &&
                    a.Team != null && a.Team == Player.Team)
                    yield return a;
        }

        public static void KillAgent(Agent target)
        {
            if (target == null || !target.IsActive()) return;
            if (target.IsHero)
            {
                // Hero agents go "unconscious" via normal battle logic; calling Die() on them
                // with OwnerId=-1 crashes Bannerlord's death attribution. Wound them instead.
                try
                {
                    Blow blow = BuildBlow(target, DamageTypes.Blunt, 2f);
                    AttackCollisionData acd = default;
                    target.RegisterBlow(blow, in acd);
                }
                catch { try { target.Health = 1f; } catch { } }
                return;
            }
            try
            {
                Blow blow = BuildBlow(target, DamageTypes.Cut, 2000f);
                target.Die(blow, (Agent.KillInfo)0);
                return;
            }
            catch { }
            try { target.MakeDead(true, ActionIndexCache.Create("act_strike_walk_right_stance"), 0); }
            catch { }
        }

        public static void DamageAgent(Agent target, float damage)
        {
            if (target == null || !target.IsActive()) return;
            if (target.Health <= damage)
            {
                // RegisterBlow with OwnerId=-1 crashes the engine's kill attribution pipeline
                // when the blow is lethal. Route through KillAgent (Die()) which handles -1 safely.
                if (!target.IsHero) { KillAgent(target); return; }
                target.Health = 1f; // heroes: clamp to 1, let battle system handle incapacitation
                return;
            }
            try
            {
                Blow blow = BuildBlow(target, DamageTypes.Blunt, damage);
                AttackCollisionData acd = default;
                target.RegisterBlow(blow, in acd);
            }
            catch
            {
                target.Health = Math.Max(1f, target.Health - damage);
            }
        }

        private static Blow BuildBlow(Agent target, DamageTypes type, float magnitude)
        {
            Blow blow = new Blow();
            blow.OwnerId         = -1; // no owner — prevents weapon/riding XP being awarded to the player
            blow.DamageType      = type;
            blow.BaseMagnitude   = magnitude;
            blow.InflictedDamage = (int)magnitude;
            blow.GlobalPosition  = target.Position;
            blow.Direction       = new Vec3(0f, 0f, 1f);
            blow.WeaponRecord    = new BlowWeaponRecord();
            blow.DamageCalculated = true;
            blow.NoIgnore        = true;
            return blow;
        }

        // ── Execute switch ───────────────────────────────────────────────────
        // Combos: first 2 chars = form (UU=Blast, RL=Self, LR=Create),
        //         last 2 chars = colour (RR=Red, RU=Orange, LU=Yellow, LL=Green, UL=Blue, UR=Purple)
        public static bool Execute(string combo)
        {
            switch (combo)
            {
                // BLAST (UU)
                case "UURR": SpellBlastRed();    break;
                case "UURU": SpellBlastOrange(); break;
                case "UULU": SpellBlastYellow(); break;
                case "UULL": SpellBlastGreen();  break;
                case "UUUL": SpellBlastBlue();   break;
                case "UUUR": SpellBlastPurple(); break;
                // SELF (RL)
                case "RLRR": SpellSelfRed();     break;
                case "RLRU": SpellSelfOrange();  break;
                case "RLLU": SpellSelfYellow();  break;
                case "RLLL": SpellSelfGreen();   break;
                case "RLUL": SpellSelfBlue();    break;
                case "RLUR": SpellSelfPurple();  break;
                // CREATE (LR)
                case "LRRR": SpellCreateRed();    break;
                case "LRRU": SpellCreateOrange(); break;
                case "LRLU": SpellCreateYellow(); break;
                case "LRLL": SpellCreateGreen();  break;
                case "LRUL": SpellCreateBlue();   break;
                case "LRUR": SpellCreatePurple(); break;
                default: return false;
            }
            return true;
        }

        // =================================================================
        // BLAST SPELLS — medium cone in front of the caster
        // Cone: 17m range, dot >= 0.6 (≈53° half-angle)
        // Glow applied to all affected agents to show the area of effect.
        // =================================================================

        // Crimson Torrent — moderate damage + pushback in cone
        private static void SpellBlastRed()
        {
            if (Player == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            var inCone = ConeAgents(Player.Position, fwd, 15f, 0.6f);
            if (inCone.Count == 0) { Msg("No one in the cone.", ColorSchool.Red); return; }
            int affected = 0;
            foreach (Agent a in inCone)
            {
                try
                {
                    DamageAgent(a, 40f);
                    if (a.IsActive() && a.Health > 0f)
                    {
                        Vec3 dir = (a.Position - Player.Position).NormalizedCopy();
                        Vec3 dest = a.Position + dir * 6f; dest.z = a.Position.z;
                        QueueMove(a, dest, 0.4f);
                    }
                    BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                    affected++;
                }
                catch { }
            }
            Msg($"Crimson Torrent tears through {affected} {(affected == 1 ? "creature" : "creatures")}.", ColorSchool.Red);
        }

        // Golden Tide — tiny damage + force enemies in cone to charge
        private static void SpellBlastOrange()
        {
            if (Player == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            var inCone = ConeAgents(Player.Position, fwd, 15f, 0.6f);
            if (inCone.Count == 0) { Msg("No one in the cone.", ColorSchool.Orange); return; }
            var formations = new HashSet<Formation>();
            foreach (Agent a in inCone)
            {
                try
                {
                    DamageAgent(a, 12f);
                    try { a.SetMorale(100f); } catch { }
                    BeginAgentGlow(a, ColorSchool.Orange, 1.5f);
                    if (a.Formation != null) formations.Add(a.Formation);
                }
                catch { }
            }
            foreach (Formation f in formations)
                try { f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
            Msg($"Golden Tide washes over {inCone.Count} {(inCone.Count == 1 ? "creature" : "creatures")} — they surge forward!", ColorSchool.Orange);
        }

        // Tide of Dread — tiny damage + morale reduction in cone
        private static void SpellBlastYellow()
        {
            if (Player == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            var inCone = ConeAgents(Player.Position, fwd, 15f, 0.6f);
            if (inCone.Count == 0) { Msg("No one in the cone.", ColorSchool.Yellow); return; }
            foreach (Agent a in inCone)
            {
                try
                {
                    DamageAgent(a, 14f);
                    try { a.SetMorale(Math.Max(0f, a.GetMorale() - 55f)); } catch { }
                    BeginAgentGlow(a, ColorSchool.Yellow, 1.5f);
                }
                catch { }
            }
            Msg($"Tide of Dread — {inCone.Count} {(inCone.Count == 1 ? "creature loses" : "creatures lose")} their nerve.", ColorSchool.Yellow);
        }

        // Verdant Surge — heal allies in cone (player and enemies excluded)
        private static void SpellBlastGreen()
        {
            if (Player == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            var inCone = ConeAgents(Player.Position, fwd, 15f, 0.6f);
            int healed = 0;
            foreach (Agent a in inCone)
            {
                if (a == Player) continue;
                if (a.Team != Player.Team) continue; // enemies excluded
                try
                {
                    float h = Math.Min(15f, a.HealthLimit - a.Health);
                    if (h > 0f) { a.Health += h; healed++; }
                    BeginAgentGlow(a, ColorSchool.Green, 1.5f);
                }
                catch { }
            }
            if (healed == 0) Msg("Verdant Surge — no allies in the cone to mend.", ColorSchool.Green);
            else Msg($"Verdant Surge mends {healed} {(healed == 1 ? "ally" : "allies")} in the cone.", ColorSchool.Green);
        }

        // Azure Arrest — damage + 2.5 s per-agent speed halt + dismount riders in cone
        private static void SpellBlastBlue()
        {
            if (Player == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            var inCone = ConeAgents(Player.Position, fwd, 15f, 0.6f);
            if (inCone.Count == 0) { Msg("No one in the cone.", ColorSchool.Blue); return; }
            var formations = new HashSet<Formation>();
            foreach (Agent a in inCone)
            {
                try
                {
                    DamageAgent(a, 12f);
                    try { a.SetMorale(Math.Max(0f, a.GetMorale() - 25f)); } catch { }
                    // Speed limit + per-tick position lock (like Hollow Gaze). Formation orders
                    // are overridden by enemy battle AI within one AI tick, so per-agent is needed.
                    try { a.SetMaximumSpeedLimit(0f, false); } catch { }
                    _haltedAgents[a.Index] = (2.5f, a.Position);
                    BeginAgentGlow(a, ColorSchool.Blue, 1.5f);
                    if (a.Formation != null) formations.Add(a.Formation);
                }
                catch { }
            }
            foreach (Formation f in formations)
            {
                try { f.SetMovementOrder(MovementOrder.MovementOrderStop); } catch { }
                try { if (f.HasAnyMountedUnit) f.SetRidingOrder(RidingOrder.RidingOrderDismount); } catch { }
            }
            Msg($"Azure Arrest freezes {inCone.Count} {(inCone.Count == 1 ? "creature" : "creatures")} for 2 seconds and unseats riders.", ColorSchool.Blue);
        }

        // Grey Harvest — one random non-hero creature in cone fades and dies
        private static void SpellBlastPurple()
        {
            if (Player == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            // Exclude heroes — Die() with OwnerId=-1 on a hero crashes Bannerlord's death processing
            var inCone = ConeAgents(Player.Position, fwd, 15f, 0.6f).Where(a => !a.IsHero).ToList();
            if (inCone.Count == 0) { Msg("No common souls in the cone — the grey passes over champions.", ColorSchool.Purple); return; }
            Agent target = inCone[_rng.Next(inCone.Count)];
            BeginAgentGlow(target, ColorSchool.Purple, 1.5f);
            KillAgent(target);
            Msg($"Grey Harvest — {target.Name} fades. The grey was always going to take them.", ColorSchool.Purple);
        }

        // =================================================================
        // SELF SPELLS — glowing aura around the caster
        // =================================================================

        // Scarlet Ward — absorbs the next single blow; expires after 8 s if nothing hits
        private static void SpellSelfRed()
        {
            if (Player == null || !Player.IsActive()) return;
            if (_scarletWardActive) { Msg("Scarlet Ward is already active.", ColorSchool.Red); return; }
            const float Duration = 8f;
            _scarletWardActive = true;
            BeginAgentGlow(Player, ColorSchool.Red, Duration);
            ActiveEffectManager.Add(new ActiveEffect
            {
                Name = "_scarlet_ward", Duration = Duration, IsMissionEffect = true,
                OnExpire = () =>
                {
                    if (_scarletWardActive)
                    {
                        _scarletWardActive = false;
                        Msg("The Scarlet Ward fades — no blow came to claim it.", ColorSchool.Red);
                    }
                }
            });
            Msg("Scarlet Ward — the next blow will find iron, not flesh.", ColorSchool.Red);
        }

        // Called from OnAgentHit — restores the damage from the triggering blow
        public static void AbsorbScarletWard(int absorbed)
        {
            if (!_scarletWardActive) return;
            _scarletWardActive = false;
            if (Player?.IsActive() == true && absorbed > 0)
                try { Player.Health = Math.Min(Player.HealthLimit, Math.Max(1f, Player.Health) + absorbed); } catch { }
            Msg($"Scarlet Ward shatters — iron turned the blow ({absorbed} absorbed).", ColorSchool.Red);
        }

        // Warm Beacon — teleport all nearby allies to your side
        private static void SpellSelfOrange()
        {
            if (Mission.Current == null || Player == null) return;
            const float Radius = 18f;
            const float LandDist = 3f;
            var nearAllies = Allies().Where(a => a.Position.Distance(Player.Position) <= Radius).ToList();
            if (nearAllies.Count == 0) { Msg("No allies within range.", ColorSchool.Orange); return; }
            float angle = 0f;
            float step = nearAllies.Count > 0 ? (2f * (float)Math.PI / nearAllies.Count) : 0f;
            foreach (Agent a in nearAllies)
            {
                try
                {
                    Vec3 offset = new Vec3((float)Math.Cos(angle) * LandDist, (float)Math.Sin(angle) * LandDist, 0f);
                    Vec3 dest   = Player.Position + offset; dest.z = Player.Position.z;
                    QueueMove(a, dest, 0.4f);
                    BeginAgentGlow(a, ColorSchool.Orange, 1.5f);
                }
                catch { }
                angle += step;
            }
            BeginAgentGlow(Player, ColorSchool.Orange, 1.5f);
            Msg($"Warm Beacon — {nearAllies.Count} {(nearAllies.Count == 1 ? "ally slides" : "allies slide")} to your side.", ColorSchool.Orange);
        }

        // Nausea Bloom — 30-second aura that slowly damages everything nearby
        private static void SpellSelfYellow()
        {
            if (Player == null) return;
            if (HasAreaEffect("self_yellow")) { Msg("Nausea Bloom is already active.", ColorSchool.Yellow); return; }
            ToggleAreaEffect("self_yellow", new AreaEffect
            {
                Id = "self_yellow", School = ColorSchool.Yellow,
                Position = Player.Position, Radius = 8f,
                Velocity = new Vec3(1f, 0f, 0f), DirTimer = 3f,
                TickInterval = 2f, TickTimer = 2f, Remaining = 30f
            });
            BeginAgentGlow(Player, ColorSchool.Yellow, 2f);
            ActiveEffectManager.Add(new ActiveEffect
            {
                Name = "_nausea_bloom", Duration = 30f, IsMissionEffect = true,
                OnExpire = () =>
                {
                    RemoveAreaEffect("self_yellow");
                    Msg("The Nausea Bloom passes. The wrongness fades.", ColorSchool.Yellow);
                }
            });
            Msg("Nausea Bloom — something deeply wrong radiates from you for 30 seconds. All nearby will feel it.", ColorSchool.Yellow);
        }

        // Verdant Touch — heal self 20 HP
        private static void SpellSelfGreen()
        {
            if (Player == null) return;
            float heal = Math.Min(20f, Player.HealthLimit - Player.Health);
            Player.Health = Math.Min(Player.Health + 20f, Player.HealthLimit);
            BeginAgentGlow(Player, ColorSchool.Green, 1.5f);
            Msg($"Verdant Touch — you restore {heal:F0} HP.", ColorSchool.Green);
        }

        // Cerulean Mirror — 40-second magic immunity (physical attacks still connect)
        private static void SpellSelfBlue()
        {
            if (Player == null || !Player.IsActive()) return;
            if (_ceruleanMirrorActive) { Msg("Cerulean Mirror is already active.", ColorSchool.Blue); return; }
            const float Duration = 40f;
            _ceruleanMirrorActive = true;
            BeginAgentGlow(Player, ColorSchool.Blue, Duration);
            ActiveEffectManager.Add(new ActiveEffect
            {
                Name = "_cerulean_mirror", Duration = Duration, IsMissionEffect = true,
                OnExpire = () =>
                {
                    _ceruleanMirrorActive = false;
                    Msg("The Cerulean Mirror dims. Spells find you again.", ColorSchool.Blue);
                }
            });
            Msg("Cerulean Mirror — spells pass through you for 40 seconds. Steel does not.", ColorSchool.Blue);
        }

        // Grief's Veil — the grey folds you from sight; nearby enemies lose nerve
        private static void SpellSelfPurple()
        {
            if (Player == null || Mission.Current == null) return;
            const float Radius = 20f;
            const float Duration = 12f;
            // Drain morale of nearby enemies — they falter and lose aggression
            var halted = new HashSet<Formation>();
            foreach (Agent a in Enemies().Where(a => a.Position.Distance(Player.Position) <= Radius).ToList())
            {
                try
                {
                    BeginAgentGlow(a, ColorSchool.Purple, 1.5f);
                    try { a.SetMorale(0f); } catch { }
                    if (a.Formation != null) halted.Add(a.Formation);
                } catch { }
            }
            // The grey hides the caster — invulnerable while unseen
            if (!_shadowVeilActive)
            {
                try { Player.ToggleInvulnerable(); _shadowVeilActive = true; } catch { }
                ActiveEffectManager.Add(new ActiveEffect
                {
                    Name = "_griefs_veil", Duration = Duration, IsMissionEffect = true,
                    OnExpire = () =>
                    {
                        if (_shadowVeilActive)
                        {
                            try { if (Player?.IsActive() == true) Player.ToggleInvulnerable(); } catch { }
                            _shadowVeilActive = false;
                        }
                        Msg("Grief's Veil lifts. The grey recedes. They find you again.", ColorSchool.Purple);
                    }
                });
            }
            BeginAgentGlow(Player, ColorSchool.Purple, 2f);
            string haltedMsg = halted.Count > 0
                ? $" {halted.Count} nearby {(halted.Count == 1 ? "formation pauses" : "formations pause")}."
                : string.Empty;
            Msg($"Grief's Veil — the grey folds you from sight for {(int)Duration}s.{haltedMsg}", ColorSchool.Purple);
        }

        // =================================================================
        // CREATE SPELLS — persistent area effects on the battlefield
        // Casting again toggles off the existing effect (marked "Cast again to dismiss").
        // Glow is re-applied on each effect tick (~every 2s) — no per-frame FPS cost.
        // Visual note: particle effects not available; glow on affected agents indicates
        //              the area. Actual ground patches cannot be rendered by this mod.
        // =================================================================

        // Cinder Burst — moderate explosion around the caster (no toggle, instant)
        private static void SpellCreateRed()
        {
            if (Player == null || Mission.Current == null) return;
            const float Radius = 10f;
            int count = 0;
            foreach (Agent a in Mission.Current.Agents
                .Where(a => a.IsActive() && !a.IsMount && a != Player &&
                            a.Position.Distance(Player.Position) <= Radius).ToList())
            {
                try
                {
                    DamageAgent(a, 50f);
                    BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                    count++;
                }
                catch { }
            }
            BeginAgentGlow(Player, ColorSchool.Red, 1.5f);
            Msg(count > 0 ? $"Cinder Burst scorches {count} {(count == 1 ? "creature" : "creatures")} within {Radius}m."
                          : "The burst finds nothing nearby.", ColorSchool.Red);
        }

        // Golden Snare — one-shot patch; triggers on first contact with a random formation command
        private static void SpellCreateOrange()
        {
            if (Player == null) return;
            if (HasAreaEffect("create_orange"))
            {
                RemoveAreaEffect("create_orange");
                _snarePrevInside.Clear();
                Msg("The Golden Snare fades before it could spring.", ColorSchool.Orange);
                return;
            }
            const float NodeRadius  = 6f;
            const float HalfSpacing = 4f;
            Vec3 fwd   = Player.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();
            Vec3 centre = Player.Position;
            Vec3[] nodePos = {
                centre - right * HalfSpacing - fwd * HalfSpacing,
                centre + right * HalfSpacing - fwd * HalfSpacing,
                centre - right * HalfSpacing + fwd * HalfSpacing,
                centre + right * HalfSpacing + fwd * HalfSpacing,
            };
            _snarePrevInside.Clear();
            foreach (Vec3 pos in nodePos)
            {
                var node = new AreaEffect
                {
                    Id = "create_orange", School = ColorSchool.Orange,
                    Position = pos, Radius = NodeRadius,
                    TickInterval = 0.5f, TickTimer = 0.5f, Remaining = 60f
                };
                node.LightEntity = SpawnAreaLight(node.Position, node.School, node.Radius);
                _areaEffects.Add(node);
            }
            // Snapshot agents inside any node so they don't trigger the snare immediately.
            // _snarePrevInside is set once at cast time and not updated during ticks (one-shot trap).
            if (Mission.Current != null)
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    foreach (Vec3 pos in nodePos)
                        if (a.Position.Distance(pos) <= NodeRadius) { _snarePrevInside.Add(a.Index); break; }
                }
            BeginAgentGlow(Player, ColorSchool.Orange, 2f);
            Msg("Golden Snare laid — a 2×2 grid of arcane pressure. The first formation to step in receives a random command and the trap vanishes. Cast again to dismiss.", ColorSchool.Orange);
        }

        // Creeping Dread — moving cloud of revulsion that damages agents it passes through
        private static void SpellCreateYellow()
        {
            if (Player == null) return;
            if (HasAreaEffect("create_yellow"))
            {
                RemoveAreaEffect("create_yellow");
                Msg("The Creeping Dread dissipates. The air settles.", ColorSchool.Yellow);
                return;
            }
            const float NodeRadius  = 5f;
            const float HalfSpacing = 4f;
            Vec3 fwd   = Player.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();
            Vec3 centre = Player.Position;
            Vec3[] nodePos = {
                centre - right * HalfSpacing - fwd * HalfSpacing,
                centre + right * HalfSpacing - fwd * HalfSpacing,
                centre - right * HalfSpacing + fwd * HalfSpacing,
                centre + right * HalfSpacing + fwd * HalfSpacing,
            };
            foreach (Vec3 pos in nodePos)
            {
                var node = new AreaEffect
                {
                    Id = "create_yellow", School = ColorSchool.Yellow,
                    Position = pos, Radius = NodeRadius,
                    Velocity = new Vec3(1f, 0f, 0f),
                    DirTimer = 3f,
                    TickInterval = 2f, TickTimer = 2f, Remaining = -1f
                };
                node.LightEntity = SpawnAreaLight(node.Position, node.School, node.Radius);
                _areaEffects.Add(node);
            }
            BeginAgentGlow(Player, ColorSchool.Yellow, 2f);
            Msg("Creeping Dread takes shape — four clouds of formless revulsion drift across the field. Cast again to dismiss.", ColorSchool.Yellow);
        }

        // Emerald Font — two healing pools side by side, perpendicular to caster's look direction
        private static void SpellCreateGreen()
        {
            if (Player == null) return;
            if (HasAreaEffect("create_green"))
            {
                RemoveAreaEffect("create_green");
                Msg("The Emerald Font closes.", ColorSchool.Green);
                return;
            }
            const float NodeRadius  = 6f;
            const float NodeSpacing = 5f;
            Vec3 fwd   = Player.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();
            Vec3 centre = Player.Position;
            Vec3[] nodePos = {
                centre - right * (NodeSpacing * 0.5f),
                centre + right * (NodeSpacing * 0.5f),
            };
            foreach (Vec3 pos in nodePos)
            {
                var node = new AreaEffect
                {
                    Id = "create_green", School = ColorSchool.Green,
                    Position = pos, Radius = NodeRadius,
                    TickInterval = 2f, TickTimer = 2f, Remaining = -1f
                };
                node.LightEntity = SpawnAreaLight(node.Position, node.School, node.Radius);
                _areaEffects.Add(node);
            }
            BeginAgentGlow(Player, ColorSchool.Green, 2f);
            Msg("The Emerald Font opens — two pools of living light, mending all who stand within. Cast again to dismiss.", ColorSchool.Green);
        }

        // Sapphire Bastion — three repulsion nodes in a line perpendicular to the caster's look direction,
        // forming a wall of force across the battlefield.
        private static void SpellCreateBlue()
        {
            if (Player == null) return;
            if (HasAreaEffect("create_blue"))
            {
                RemoveAreaEffect("create_blue");
                _bastionPrevInside.Clear();
                Msg("The Sapphire Bastion crumbles.", ColorSchool.Blue);
                return;
            }

            const float Duration     = 120f;
            const float NodeRadius   = 3f;
            const float NodeSpacing  = 4.5f; // distance between adjacent node centres

            // Wall runs perpendicular to the player's look direction
            Vec3 fwd   = Player.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();

            Vec3 centre = Player.Position;
            Vec3[] nodePos = {
                centre - right * NodeSpacing,
                centre,
                centre + right * NodeSpacing
            };

            foreach (Vec3 pos in nodePos)
            {
                var node = new AreaEffect
                {
                    Id = "create_blue", School = ColorSchool.Blue,
                    Position = pos, Radius = NodeRadius,
                    TickInterval = 0.5f, TickTimer = 0.5f, Remaining = Duration
                };
                node.LightEntity = SpawnAreaLight(node.Position, node.School, node.Radius);
                _areaEffects.Add(node);
            }

            BeginAgentGlow(Player, ColorSchool.Blue, 2f);
            Msg("Sapphire Bastion rises — three pillars of force seal the line. None shall cross. Fades in 2 minutes.", ColorSchool.Blue);
        }

        // Hollow Gaze — one random nearby enemy becomes catatonic; casting again cancels the effect
        private static void SpellCreatePurple()
        {
            if (Player == null || Mission.Current == null) return;
            if (_hollowGazeTarget != null)
            {
                string name = _hollowGazeTarget.IsActive() ? _hollowGazeTarget.Name : "them";
                _hollowGazeTarget = null;
                Msg($"The Hollow Gaze releases. {name} stirs back into themselves.", ColorSchool.Purple);
                return;
            }
            const float Radius = 15f;
            var candidates = Enemies()
                .Where(a => !a.IsHero && a.IsActive() && a.Position.Distance(Player.Position) <= Radius)
                .ToList();
            if (candidates.Count == 0) { Msg("No one nearby to hollow out.", ColorSchool.Purple); return; }
            _hollowGazeTarget = candidates[_rng.Next(candidates.Count)];
            _hollowGazeTimer  = 0f;
            BeginAgentGlow(_hollowGazeTarget, ColorSchool.Purple, 3f);
            Msg($"Hollow Gaze — {_hollowGazeTarget.Name} empties out. They stand and wait for nothing.", ColorSchool.Purple);
        }

        // Helper: returns all active non-mount agents in a cone (both allies and enemies)
        private static List<Agent> ConeAgents(Vec3 origin, Vec3 fwd, float range, float dot)
        {
            if (Mission.Current == null) return new List<Agent>();
            var result = new List<Agent>();
            foreach (Agent a in Mission.Current.Agents)
            {
                if (!a.IsActive() || a.IsMount || a == Player) continue;
                Vec3 to = a.Position - origin;
                if (to.Length > range) continue;
                if (Vec3.DotProduct(fwd, to.NormalizedCopy()) < dot) continue;
                result.Add(a);
            }
            return result;
        }

        // Recruit helpers (used by Calling and NPC AI)
        private static CharacterObject FindImperialRecruit()
        {
            CharacterObject r =
                MBObjectManager.Instance.GetObject<CharacterObject>("imperial_recruit")
             ?? MBObjectManager.Instance.GetObject<CharacterObject>("imperial_levy_infantryman")
             ?? MBObjectManager.Instance.GetObject<CharacterObject>("imperial_levy");
            if (r != null) return r;
            foreach (CharacterObject c in CharacterObject.All)
                if (!c.IsHero && c.Tier == 1 && c.Culture?.StringId?.Contains("empire") == true)
                    return c;
            return null;
        }

        public static CharacterObject FindRecruit(Agent agent)
        {
            string cultureId = agent?.Character?.Culture?.StringId;
            if (!string.IsNullOrEmpty(cultureId))
                foreach (CharacterObject c in CharacterObject.All)
                    if (!c.IsHero && c.Tier == 1 && c.Culture?.StringId == cultureId) return c;
            foreach (CharacterObject c in CharacterObject.All)
                if (!c.IsHero && c.Tier == 1) return c;
            return null;
        }

        // =================================================================
        // BATTLE COMMAND HELPER  (shared with lord AI)
        // =================================================================
        public enum BattleCommandKind { Halt, Enrage, Dismount, StopArrows }

        public static void IssueBattleCommand(Agent source, BattleCommandKind kind,
                                              string successText, ColorSchool school)
        {
            if (source == null || Mission.Current == null || Mission.Current.Scene == null)
            {
                Msg("No battle active.", school);
                return;
            }

            var formations = new HashSet<Formation>();
            var scene = Mission.Current.Scene;

            foreach (Agent a in Enemies().ToList())
            {
                if (a.Formation == null) continue;
                if (a.Position.Distance(source.Position) > 500f) continue;
                bool visible = true;
                try { visible = scene.CheckPointCanSeePoint(source.Position, a.Position, 500f); }
                catch { }
                if (!visible) continue;
                formations.Add(a.Formation);
                BeginAgentGlow(a, school, 1.5f);
            }

            if (formations.Count == 0) { Msg("No visible enemy formations.", school); return; }

            int affected = 0;
            foreach (Formation f in formations)
            {
                try
                {
                    switch (kind)
                    {
                        case BattleCommandKind.Halt:
                            foreach (Agent fa in Mission.Current.Agents
                                .Where(a => a.IsActive() && a.Formation == f).ToList())
                                try { fa.SetMorale(0f); } catch { }
                            affected++; break;
                        case BattleCommandKind.Enrage:
                            foreach (Agent fa in Mission.Current.Agents
                                .Where(a => a.IsActive() && a.Formation == f).ToList())
                                try { fa.SetMorale(100f); } catch { }
                            try { f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
                            affected++; break;
                        case BattleCommandKind.Dismount:
                            if (f.HasAnyMountedUnit) { f.SetRidingOrder(RidingOrder.RidingOrderDismount); affected++; } break;
                        case BattleCommandKind.StopArrows:
                            if (f.GetCountOfUnitsBelongingToLogicalClass(TaleWorlds.Core.FormationClass.Ranged) > 0 ||
                                f.GetCountOfUnitsBelongingToLogicalClass(TaleWorlds.Core.FormationClass.HorseArcher) > 0)
                            { f.SetFiringOrder(FiringOrder.FiringOrderHoldYourFire); affected++; }
                            break;
                    }
                }
                catch { }
            }

            Msg(affected > 0
                ? string.Format(successText, affected, affected == 1 ? "" : "s")
                : "No matching formations responded.", school);
        }

        // =================================================================
        // VISUAL SYSTEM  — per-school coloured glow
        // =================================================================
        private static readonly List<(Agent agent, float remaining)> _glowTimers
            = new List<(Agent, float)>();

        // Called every mission tick — only clears expired timers, never re-applies colour.
        public static void TickGlows(float dt)
        {
            for (int i = _glowTimers.Count - 1; i >= 0; i--)
            {
                float t = _glowTimers[i].remaining - dt;
                if (t <= 0f)
                {
                    var a = _glowTimers[i].agent;
                    if (a != null && a.IsActive())
                        try { a.AgentVisuals?.GetEntity()?.SetContourColor(null, false); } catch { }
                    _glowTimers.RemoveAt(i);
                }
                else
                {
                    _glowTimers[i] = (_glowTimers[i].agent, t);
                }
            }
        }

        public static void ClearGlows()
        {
            foreach (var (agent, _) in _glowTimers)
                if (agent != null && agent.IsActive())
                    try { agent.AgentVisuals?.GetEntity()?.SetContourColor(null, false); } catch { }
            _glowTimers.Clear();
        }


        public static void BeginAgentGlow(Agent agent, ColorSchool school, float duration)
        {
            if (agent == null || !agent.IsActive()) return;
            try
            {
                agent.AgentVisuals?.GetEntity()
                    ?.SetContourColor(ColorSchoolData.GetGlowColor(school), true);
                int idx = _glowTimers.FindIndex(x => x.agent == agent);
                if (idx >= 0) _glowTimers.RemoveAt(idx);
                _glowTimers.Add((agent, 1f)); // always clear after 1 seconds
            }
            catch { }
        }

        public static void CastGlow(Agent caster, ColorSchool school)
        {
            if (caster == null) return;
            try
            {
                BeginAgentGlow(caster, school, 3.0f);
                TryCastSound(caster.Position, school);
            }
            catch { }
        }

        // Sound event cache
        private static MethodInfo _soundGetId;

        private static bool TryResolveSoundEvent()
        {
            if (_soundGetId != null) return true;
            try
            {
                foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (string candidate in new[] { "TaleWorlds.MountAndBlade.SoundEvent", "TaleWorlds.Engine.SoundEvent" })
                    {
                        Type t = asm.GetType(candidate);
                        if (t == null) continue;
                        MethodInfo m = t.GetMethod("GetEventIdFromString", BindingFlags.Public | BindingFlags.Static);
                        if (m == null) continue;
                        _soundGetId = m;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public static void TryCastSound(Vec3 position, ColorSchool school)
        {
            if (Mission.Current == null || !TryResolveSoundEvent()) return;
            string[] candidates = school == ColorSchool.Red || school == ColorSchool.Purple
                ? new[] { "event:/mission/ambient/detail/wind_hit", "event:/ui/panels/open" }
                : new[] { "event:/ui/notifications/quest_update", "event:/ui/panels/open" };

            foreach (string path in candidates)
            {
                try
                {
                    object idObj = _soundGetId.Invoke(null, new object[] { path });
                    if (idObj == null) continue;
                    int soundId = (int)idObj;
                    if (soundId < 0) continue;
                    Mission.Current.MakeSound(soundId, position, false, false, -1, -1);
                    return;
                }
                catch { }
            }
        }

        // Battery multiplier: 1.0 normally (kept for compatibility, unused in colour system)
        private static float MageUnitBattery(Agent agent) => 1.0f;

        private static void Msg(string text, ColorSchool school) =>
            InformationManager.DisplayMessage(new InformationMessage(
                text, ColorSchoolData.GetMessageColor(school)));

        // Militia reflection helper (reused from v1)
        private static MethodInfo _setMilitiaSetter;
        private static bool _setMilitiaResolved;

        public static bool TrySetMilitia(Village v, float value)
        {
            if (!_setMilitiaResolved)
            {
                _setMilitiaResolved = true;
                PropertyInfo prop = typeof(Village).GetProperty("Militia",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _setMilitiaSetter = prop?.GetSetMethod(nonPublic: true);
            }
            if (_setMilitiaSetter == null) return false;
            try { _setMilitiaSetter.Invoke(v, new object[] { value }); return true; }
            catch { return false; }
        }
    }

    // =========================================================================
    // 9. COLOUR LORD REGISTRY
    //    Lords have colour schools. Distribution per faction:
    //      ~10% of lords → 1 random colour
    //       ~7%          → 2 colours
    //       ~5%          → 3 colours
    //        1 lord      → 4 colours (faction archmage)
    // =========================================================================
    public static class ColourLordRegistry
    {
        private static bool _seeded;
        private static readonly Random _rng = new Random();

        // lord StringId → list of colour schools
        private static readonly Dictionary<string, List<ColorSchool>> _lordColors
            = new Dictionary<string, List<ColorSchool>>();

        // Respawn timers: factionId → hours remaining
        private static readonly Dictionary<string, int> _respawnHours
            = new Dictionary<string, int>();

        // Campaign map cast cooldowns: lord StringId → days until next cast
        private static readonly Dictionary<string, int> _campaignCooldowns
            = new Dictionary<string, int>();

        // Pending mage announcements — flushed on first daily tick so they're visible to the player
        private static readonly List<(string message, Color color)> _pendingAnnouncements
            = new List<(string, Color)>();

        // Desired trait level per colour school — applied when colours are assigned
        // Key: trait to set; Value: target level (positive or negative)
        private static readonly Dictionary<ColorSchool, List<(TraitObject Trait, int Level)>> _colourTraits =
            new Dictionary<ColorSchool, List<(TraitObject, int)>>
        {
            [ColorSchool.Red]    = new List<(TraitObject, int)> { (DefaultTraits.Calculating, -1), (DefaultTraits.Valor, +1) },
            [ColorSchool.Orange] = new List<(TraitObject, int)> { (DefaultTraits.Generosity, +1) },
            [ColorSchool.Yellow] = new List<(TraitObject, int)> { (DefaultTraits.Mercy, -1), (DefaultTraits.Honor, -1) },
            [ColorSchool.Green]  = new List<(TraitObject, int)> { (DefaultTraits.Mercy, +1), (DefaultTraits.Honor, +1) },
            [ColorSchool.Blue]   = new List<(TraitObject, int)> { (DefaultTraits.Calculating, +1) },
            [ColorSchool.Purple] = new List<(TraitObject, int)> { (DefaultTraits.Valor, -1) },
        };

        // ── Public access ─────────────────────────────────────────────────────
        public static bool IsColourLord(Hero hero) =>
            hero != null && _lordColors.ContainsKey(hero.StringId);

        public static IReadOnlyList<ColorSchool> GetColors(Hero hero)
        {
            if (hero == null || !_lordColors.TryGetValue(hero.StringId, out var list))
                return Array.Empty<ColorSchool>();
            return list;
        }

        public static bool HasColor(Hero hero, ColorSchool school) =>
            _lordColors.TryGetValue(hero?.StringId ?? "", out var list) && list.Contains(school);

        // ── Seeding ───────────────────────────────────────────────────────────
        public static void SeedInitialLords()
        {
            if (_seeded) return;
            _seeded = true;
            try
            {
                foreach (Kingdom kingdom in Campaign.Current.Kingdoms)
                    SeedFaction(kingdom);
            }
            catch { }
        }

        private static readonly string[] _archmageFlavour =
        {
            "Four colours burn in {0} of {1}. Do not face them lightly.",
            "They say {0} of {1} bleeds in four colours. The field bends around them.",
            "{0} of {1} carries four schools. Soldiers speak of them in hushed tones.",
            "Four marks — four colours scar {0} of {1}. The most dangerous kind.",
            "{0} of {1} wears four colours like armour. None who faced them alone lived to boast.",
        };
        private static readonly string[] _multiFlavour =
        {
            "{0} of {1} carries {2}.",
            "The colours {2} have taken root in {0} of {1}.",
            "{0} of {1} is touched by {2} — a rare combination.",
            "Scouts report {0} of {1} wielding {2}.",
            "Two schools stir in {0} of {1}: {2}.",
        };
        private static readonly string[] _singleFlavour =
        {
            "{0} of {1} walks with the {2}.",
            "The {2} has chosen {0} of {1}.",
            "{0} of {1} — marked by {2}.",
            "A single colour, but sharp: {0} of {1} carries {2}.",
            "The {2} stirs in {0} of {1}.",
        };

        private static string FormatAnnouncement(Hero lord, IReadOnlyList<ColorSchool> colors)
        {
            string name    = lord.Name.ToString();
            string faction = lord.MapFaction?.Name?.ToString() ?? "the world";
            string colourList = string.Join(", ", colors.Select(c => ColorSchoolData.Info[c].Name));

            if (colors.Count >= 4)
            {
                string tmpl = _archmageFlavour[_rng.Next(_archmageFlavour.Length)];
                return string.Format(tmpl, name, faction);
            }
            else if (colors.Count >= 2)
            {
                string tmpl = _multiFlavour[_rng.Next(_multiFlavour.Length)];
                return string.Format(tmpl, name, faction, colourList);
            }
            else
            {
                string colour = ColorSchoolData.Info[colors[0]].Name;
                string tmpl   = _singleFlavour[_rng.Next(_singleFlavour.Length)];
                return string.Format(tmpl, name, faction, colour);
            }
        }

        public static void FlushAnnouncements()
        {
            if (_pendingAnnouncements.Count == 0) return;
            InformationManager.DisplayMessage(new InformationMessage(
                $"The colours have awoken. {_pendingAnnouncements.Count} mages walk among the lords of Calradia.",
                new Color(0.85f, 0.65f, 1.0f)));
            foreach (var (msg, col) in _pendingAnnouncements)
                InformationManager.DisplayMessage(new InformationMessage(msg, col));
            _pendingAnnouncements.Clear();
        }

        private static void SeedFaction(IFaction faction)
        {
            var lords = Hero.AllAliveHeroes
                .Where(h => h.MapFaction == faction && h.IsLord && !IsColourLord(h))
                .ToList();
            if (lords.Count == 0) return;

            // Shuffle
            for (int i = lords.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var tmp = lords[i]; lords[i] = lords[j]; lords[j] = tmp;
            }

            // Assign one archmage (4 colours)
            Hero archmage = lords[0];
            _lordColors[archmage.StringId] = PickColors(4);
            try { ApplyColourTraits(archmage, _lordColors[archmage.StringId]); } catch { }
            _pendingAnnouncements.Add((
                FormatAnnouncement(archmage, _lordColors[archmage.StringId]),
                new Color(0.9f, 0.7f, 0.9f)));

            for (int i = 1; i < lords.Count; i++)
            {
                int roll = _rng.Next(100);
                int colorCount;
                if      (roll <  5) colorCount = 3;
                else if (roll < 12) colorCount = 2;
                else if (roll < 22) colorCount = 1;
                else                continue;

                _lordColors[lords[i].StringId] = PickColors(colorCount);
                try { ApplyColourTraits(lords[i], _lordColors[lords[i].StringId]); } catch { }
                _pendingAnnouncements.Add((
                    FormatAnnouncement(lords[i], _lordColors[lords[i].StringId]),
                    new Color(0.7f, 0.4f, 0.8f)));
            }
        }

        private static List<ColorSchool> PickColors(int count)
        {
            var all = new List<ColorSchool>(
                (ColorSchool[])Enum.GetValues(typeof(ColorSchool)));
            var result = new List<ColorSchool>();
            count = Math.Min(count, all.Count);
            for (int i = 0; i < count; i++)
            {
                int idx = _rng.Next(all.Count);
                result.Add(all[idx]);
                all.RemoveAt(idx);
            }
            return result;
        }

        // Applies personality traits to a hero based on their colour schools.
        // Existing traits in the same direction are kept or made more extreme.
        // Conflicting traits (opposite sign) are resolved by coin flip.
        public static void ApplyColourTraits(Hero hero, IReadOnlyList<ColorSchool> colors)
        {
            if (hero == null || colors == null || colors.Count == 0) return;

            // Merge desired trait levels across all assigned colours
            var desired = new Dictionary<TraitObject, int>();
            foreach (ColorSchool school in colors)
            {
                if (!_colourTraits.TryGetValue(school, out var entries)) continue;
                foreach (var (trait, level) in entries)
                {
                    if (!desired.ContainsKey(trait))
                    {
                        desired[trait] = level;
                    }
                    else
                    {
                        int existing = desired[trait];
                        if (Math.Sign(existing) == Math.Sign(level))
                            // Same direction: keep the more extreme value
                            desired[trait] = Math.Abs(level) > Math.Abs(existing) ? level : existing;
                        else
                            // Colours disagree on this trait: pick randomly
                            desired[trait] = _rng.Next(2) == 0 ? existing : level;
                    }
                }
            }

            // Apply to hero, respecting existing trait values
            foreach (var kvp in desired)
            {
                try
                {
                    TraitObject trait  = kvp.Key;
                    int         target = kvp.Value;
                    int         current = hero.GetTraitLevel(trait);

                    int chosen;
                    if (current == 0 || Math.Sign(current) == Math.Sign(target))
                        // No conflict: use whichever is more extreme
                        chosen = Math.Abs(target) >= Math.Abs(current) ? target : current;
                    else
                        // Conflict (hero already leans the other way): pick randomly
                        chosen = _rng.Next(2) == 0 ? current : target;

                    // Clamp to valid trait range
                    chosen = Math.Max(-2, Math.Min(2, chosen));
                    hero.SetTraitLevel(trait, chosen);
                }
                catch { }
            }
        }

        // ── Children ──────────────────────────────────────────────────────────
        public static void GrantChildColours(Hero hero, List<ColorSchool> schools)
        {
            if (hero == null || schools == null || schools.Count == 0) return;
            _lordColors[hero.StringId] = new List<ColorSchool>(schools);
            ApplyColourTraits(hero, schools);
        }

        // ── Companions ────────────────────────────────────────────────────────
        public static void TryGrantCompanionColours(Hero companion)
        {
            if (companion == null || IsColourLord(companion)) return;
            int roll = _rng.Next(100);
            int count;
            if      (roll < 10) count = 2;
            else if (roll < 30) count = 1;
            else                return;

            _lordColors[companion.StringId] = PickColors(count);
            ApplyColourTraits(companion, _lordColors[companion.StringId]);
            string colorNames = string.Join(", ", _lordColors[companion.StringId]
                .Select(c => ColorSchoolData.Info[c].Name));
            InformationManager.DisplayMessage(new InformationMessage(
                $"{companion.Name} carries colour magic: {colorNames}.",
                new Color(0.7f, 0.7f, 0.5f)));
        }

        // ── Death / Respawn ───────────────────────────────────────────────────
        public static void OnLordDied(Hero hero)
        {
            _lordColors.Remove(hero.StringId);
            string factionId = (hero.MapFaction as Kingdom)?.StringId;
            if (factionId == null) return;

            _respawnHours[factionId] = 168; // 7 days
            InformationManager.DisplayMessage(new InformationMessage(
                $"The colours of {hero.Name} are extinguished. They will pass to another in one week.",
                Color.FromUint(0xFFAA6644)));
        }

        public static void CheckRespawnTimers()
        {
            foreach (string factionId in _respawnHours.Keys.ToList())
            {
                _respawnHours[factionId]--;
                if (_respawnHours[factionId] > 0) continue;
                _respawnHours.Remove(factionId);

                // Seed one new colour lord in this faction
                Kingdom kingdom = Campaign.Current?.Kingdoms
                    .FirstOrDefault(k => k.StringId == factionId);
                if (kingdom == null) continue;

                var candidates = Hero.AllAliveHeroes
                    .Where(h => h.MapFaction == kingdom && h.IsLord && !IsColourLord(h) &&
                                h.Age < 50f) // prefer younger lords
                    .ToList();
                if (candidates.Count == 0)
                {
                    _respawnHours[factionId] = 24; // no candidates yet — try again in 1 day
                    continue;
                }

                Hero chosen = candidates[_rng.Next(candidates.Count)];
                _lordColors[chosen.StringId] = PickColors(1 + _rng.Next(2));
                ApplyColourTraits(chosen, _lordColors[chosen.StringId]);
                InformationManager.DisplayMessage(new InformationMessage(
                    FormatAnnouncement(chosen, _lordColors[chosen.StringId]),
                    new Color(0.7f, 0.5f, 0.8f)));
            }
        }

        // ── Campaign map effects (lord-only) ──────────────────────────────────
        private const int MaxLordMapCastsPerDay = 3;

        public static void DailyMapCast()
        {
            int castsToday = 0;
            foreach (var kvp in _lordColors.ToList())
            {
                if (castsToday >= MaxLordMapCastsPerDay) break;

                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == kvp.Key);
                if (hero == null || !hero.IsAlive) continue;

                if (_campaignCooldowns.TryGetValue(kvp.Key, out int cd) && cd > 0)
                { _campaignCooldowns[kvp.Key] = cd - 1; continue; }

                if (_rng.Next(100) >= 20) continue; // 20% chance per day

                _campaignCooldowns[kvp.Key] = 6 + _rng.Next(4);
                ColorSchool school = kvp.Value[_rng.Next(kvp.Value.Count)];
                CastLordMapSpell(hero, school);
                castsToday++;
            }
        }

        // Pick a random element from a filtered list without sorting the whole collection.
        private static T PickRandom<T>(IEnumerable<T> source) where T : class
        {
            var list = source.ToList();
            return list.Count > 0 ? list[_rng.Next(list.Count)] : null;
        }

        private static void CastLordMapSpell(Hero lord, ColorSchool school)
        {
            string msg = null;
            string spellName = null;

            try
            {
                switch (school)
                {
                    case ColorSchool.Red:
                        if (_rng.Next(2) == 0)
                        {
                            spellName = "Bloodlust";
                            if (lord.PartyBelongedTo != null)
                            { lord.PartyBelongedTo.RecentEventsMorale += 10f; msg = $"{lord.Name}'s warband burns with purpose."; }
                        }
                        else
                        {
                            spellName = "Carnage";
                            var target = PickRandom(Settlement.All.Where(s => s.IsVillage && s.Village != null));
                            if (target?.Village != null)
                            {
                                target.Village.Hearth = Math.Max(10f, target.Village.Hearth * 0.8f);
                                msg = $"Fire and ruin — {target.Name} diminishes under {lord.Name}'s rage.";
                            }
                        }
                        break;

                    case ColorSchool.Orange:
                        if (_rng.Next(2) == 0)
                        {
                            spellName = "Celebrate";
                            var ownVillage = PickRandom(Settlement.All
                                .Where(s => s.IsVillage && s.MapFaction == lord.MapFaction && s.Village != null));
                            if (ownVillage?.Village != null)
                            {
                                ownVillage.Village.Hearth = Math.Min(2000f, ownVillage.Village.Hearth * 1.05f);
                                msg = $"{ownVillage.Name} flourishes under {lord.Name}'s blessing.";
                            }
                        }
                        else
                        {
                            spellName = "Bribe";
                            var rival = PickRandom(Hero.AllAliveHeroes.Where(
                                h => h.IsLord && h.MapFaction != lord.MapFaction
                                     && h.PartyBelongedTo != null && h.IsAlive));
                            if (rival?.PartyBelongedTo != null && lord.PartyBelongedTo != null)
                            {
                                int take  = 1 + _rng.Next(2);
                                var troops = rival.PartyBelongedTo.MemberRoster.GetTroopRoster()
                                    .Where(e => !e.Character.IsHero && e.Number > 0).ToList();
                                if (troops.Count > 0)
                                {
                                    var troop  = troops[_rng.Next(troops.Count)];
                                    int actual = Math.Min(take, troop.Number);
                                    rival.PartyBelongedTo.MemberRoster.RemoveTroop(troop.Character, actual);
                                    lord.PartyBelongedTo.MemberRoster.AddToCounts(troop.Character, actual);
                                    msg = $"{actual} soldier{(actual > 1 ? "s" : "")} desert {rival.Name} for {lord.Name}'s generosity.";
                                }
                            }
                        }
                        break;

                    case ColorSchool.Yellow:
                        if (_rng.Next(2) == 0)
                        {
                            spellName = "Fade";
                            var target = PickRandom(Hero.AllAliveHeroes.Where(
                                h => h.IsLord && h.MapFaction != lord.MapFaction && h.Clan != null && h.IsAlive));
                            if (target?.Clan != null)
                            {
                                try { target.Clan.AddRenown(-10f); } catch { }
                                msg = $"{target.Name}'s name grows quieter — {lord.Name}'s melancholy reaches far.";
                            }
                        }
                        else
                        {
                            spellName = "Melancholy";
                            var target = PickRandom(Hero.AllAliveHeroes.Where(
                                h => h.IsLord && h.MapFaction != lord.MapFaction
                                     && h.PartyBelongedTo != null && h.IsAlive));
                            if (target?.PartyBelongedTo != null)
                            {
                                target.PartyBelongedTo.RecentEventsMorale -= 15f;
                                msg = $"Fear settles over {target.Name}'s ranks — {lord.Name} breathes terror into the world.";
                            }
                        }
                        break;

                    case ColorSchool.Green:
                        if (_rng.Next(2) == 0)
                        {
                            spellName = "Rejuvenate";
                            if (lord.PartyBelongedTo != null)
                            {
                                int healed = 0;
                                foreach (var e in lord.PartyBelongedTo.MemberRoster.GetTroopRoster().ToList())
                                {
                                    int h = Math.Min(e.WoundedNumber, 3);
                                    if (h > 0) { lord.PartyBelongedTo.MemberRoster.AddToCounts(e.Character, 0, false, -h); healed += h; }
                                }
                                if (healed > 0) msg = $"{lord.Name} tends the wounds. {healed} soldiers recover.";
                            }
                        }
                        else
                        {
                            spellName = "Crops";
                            if (lord.PartyBelongedTo != null)
                            {
                                ItemObject grain = MBObjectManager.Instance.GetObject<ItemObject>("grain");
                                if (grain != null) lord.PartyBelongedTo.ItemRoster.AddToCounts(grain, 5);
                                msg = $"Grain ripens in {lord.Name}'s stores.";
                            }
                        }
                        break;

                    case ColorSchool.Blue:
                        if (_rng.Next(2) == 0)
                        {
                            spellName = "Schemes";
                            if (lord.Clan != null)
                            {
                                try { ChangeClanInfluenceAction.Apply(lord.Clan, 8f); } catch { }
                                msg = $"{lord.Name}'s schemes bear fruit — their clan's influence grows.";
                            }
                        }
                        else
                        {
                            spellName = "Plots";
                            var target = PickRandom(Hero.AllAliveHeroes.Where(
                                h => h.IsLord && h.MapFaction != lord.MapFaction && h.Clan != null && h.IsAlive));
                            if (target?.Clan != null)
                            {
                                try { ChangeClanInfluenceAction.Apply(target.Clan, -8f); } catch { }
                                msg = $"{lord.Name}'s cold plots undermine {target.Name}'s standing.";
                            }
                        }
                        break;

                    case ColorSchool.Purple:
                        if (_rng.Next(2) == 0)
                        {
                            spellName = "Curse";
                            var target = PickRandom(Hero.AllAliveHeroes.Where(
                                h => h.IsLord && h.MapFaction != lord.MapFaction
                                     && h.Clan != null && h.IsAlive && h.PartyBelongedTo != null));
                            if (target != null)
                            {
                                try { ChangeClanInfluenceAction.Apply(target.Clan, -5f); } catch { }
                                try { target.PartyBelongedTo.RecentEventsMorale -= 10f; } catch { }
                                msg = $"A curse falls on {target.Name} — their influence wanes and their soldiers are unsettled.";
                            }
                        }
                        else
                        {
                            spellName = "Bind";
                            if (lord.PartyBelongedTo != null)
                            {
                                CharacterObject recruit = GetFactionRecruit(lord);
                                if (recruit != null)
                                {
                                    lord.PartyBelongedTo.MemberRoster.AddToCounts(recruit, 10);
                                    msg = $"10 soldiers are bound to {lord.Name}'s will.";
                                }
                            }
                        }
                        break;
                }
            }
            catch { }

            if (msg == null || spellName == null) return;

            InformationManager.DisplayMessage(new InformationMessage(
                $"✦ {lord.Name} channels {spellName} ({ColorSchoolData.Info[school].Name}). {msg} ✦",
                ColorSchoolData.GetMessageColor(school)));
        }

        private static CharacterObject GetFactionRecruit(Hero lord)
        {
            string culture = (lord.MapFaction as Kingdom)?.Culture?.StringId ?? "";
            CharacterObject r = null;
            foreach (CharacterObject c in CharacterObject.All)
                if (!c.IsHero && c.Tier == 1 && c.Culture?.StringId == culture)
                { r = c; break; }
            return r;
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            // Flatten List<ColorSchool> per lord into parallel count + flat-int lists —
            // IDataStore cannot serialize List<List<T>>.
            var lordIds        = _lordColors.Keys.ToList();
            var lordSchoolCnts = _lordColors.Values.Select(v => v.Count).ToList();
            var lordSchoolFlat = _lordColors.Values.SelectMany(v => v.Select(s => (int)s)).ToList();
            var rKeys          = _respawnHours.Keys.ToList();
            var rVals          = _respawnHours.Values.ToList();
            var ccKeys         = _campaignCooldowns.Keys.ToList();
            var ccVals         = _campaignCooldowns.Values.ToList();
            bool seeded        = _seeded;

            store.SyncData("COC_LordIds",        ref lordIds);
            store.SyncData("COC_LordSchoolCnts", ref lordSchoolCnts);
            store.SyncData("COC_LordSchoolFlat", ref lordSchoolFlat);
            store.SyncData("COC_RespawnKeys",    ref rKeys);
            store.SyncData("COC_RespawnVals",    ref rVals);
            store.SyncData("COC_CdKeys",         ref ccKeys);
            store.SyncData("COC_CdVals",         ref ccVals);
            store.SyncData("COC_LordSeeded",     ref seeded);

            _seeded = seeded;

            _lordColors.Clear();
            if (lordIds != null && lordSchoolCnts != null && lordSchoolFlat != null)
            {
                int si = 0;
                for (int i = 0; i < lordIds.Count; i++)
                {
                    int cnt = i < lordSchoolCnts.Count ? lordSchoolCnts[i] : 0;
                    var schools = new List<ColorSchool>();
                    for (int j = 0; j < cnt && si < lordSchoolFlat.Count; j++, si++)
                        schools.Add((ColorSchool)lordSchoolFlat[si]);
                    if (schools.Count > 0)
                        _lordColors[lordIds[i]] = schools;
                }
            }

            _respawnHours.Clear();
            if (rKeys != null && rVals != null)
                for (int i = 0; i < Math.Min(rKeys.Count, rVals.Count); i++)
                    _respawnHours[rKeys[i]] = rVals[i];

            _campaignCooldowns.Clear();
            if (ccKeys != null && ccVals != null)
                for (int i = 0; i < Math.Min(ccKeys.Count, ccVals.Count); i++)
                    _campaignCooldowns[ccKeys[i]] = ccVals[i];
        }
    }

    // =========================================================================
    // 10. COLOUR LORD AI
    //     Finds hero agents with colour schools and has them cast spells in battle.
    //     NPC limitations apply: E1 Grounded (Blue, no horse),
    //     D1 Pacifist (Green, no weapon), F1 Proud (Purple, needs allies).
    // =========================================================================
    public static class ColourLordAI
    {
        private const float CastInterval = 12f;
        private static readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();
        private static readonly Random _rng = new Random();

        private static float _tickAccum = 0f;
        private const  float TickInterval = 0.5f;

        public static void ClearCooldowns() => _cooldowns.Clear();

        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;

            _tickAccum += dt;
            if (_tickAccum < TickInterval) return;
            _tickAccum = 0f;

            if (!SpellEffects.IsBattleMission()) return;

            foreach (string key in _cooldowns.Keys.ToList())
            {
                _cooldowns[key] -= TickInterval;
                if (_cooldowns[key] <= 0f) _cooldowns.Remove(key);
            }

            foreach (Agent agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount || !agent.IsHero) continue;
                if (agent == Agent.Main) continue;

                Hero hero = (agent.Character as CharacterObject)?.HeroObject;
                if (hero == null || !ColourLordRegistry.IsColourLord(hero)) continue;
                if (_cooldowns.ContainsKey(hero.StringId)) continue;

                var colors = ColourLordRegistry.GetColors(hero);
                if (colors.Count == 0) continue;

                // Green lords fight unarmed — sheathe weapon every tick so CanUseGreen passes
                if (colors.Contains(ColorSchool.Green))
                    TrySheathWeapon(agent);

                DecideAndCast(agent, hero, colors);
            }
        }

        private static void DecideAndCast(Agent agent, Hero hero, IReadOnlyList<ColorSchool> colors)
        {
            // All NPC casts require daylight (global magic restriction)
            if (!CanCastAny(agent)) return;

            float hpPct = agent.Health / Math.Max(agent.HealthLimit, 1f);

            // Self-heal with Green (Verdant Touch) when badly hurt
            if (hpPct < 0.35f && colors.Contains(ColorSchool.Green) && CanUseGreen(agent))
            {
                CastWithGlow(agent, hero, ColorSchool.Green, "Verdant Touch", () =>
                {
                    agent.Health = Math.Min(agent.Health + 20f, agent.HealthLimit);
                });
                return;
            }

            // 8% random wild cast
            if (_rng.Next(100) < 8) { TryCastRandom(agent, hero, colors); return; }

            // Enemies swarming — Cinder Burst (Red) or Grey Harvest (Purple)
            int closeEnemies = CountEnemiesNear(agent, 8f);
            if (closeEnemies >= 3)
            {
                if (colors.Contains(ColorSchool.Purple))
                {
                    CastWithGlow(agent, hero, ColorSchool.Purple, "Cinder Burst", () =>
                    {
                        foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 8f).ToList())
                        {
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            SpellEffects.DamageAgent(a, 45f);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Purple, 1.5f);
                        }
                    });
                    ApplyPurpleAging(hero);
                    return;
                }
                if (colors.Contains(ColorSchool.Red))
                {
                    CastWithGlow(agent, hero, ColorSchool.Red, "Crimson Torrent", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            SpellEffects.DamageAgent(a, 40f);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                        }
                    });
                    ApplyRedA1(agent); ApplyRedA2(agent);
                    return;
                }
            }

            // Cone enemies — Crimson Torrent (Red) or Azure Arrest (Blue)
            int coneEnemies = CountEnemiesInCone(agent, 15f, 0.6f);
            if (coneEnemies >= 2)
            {
                if (colors.Contains(ColorSchool.Red))
                {
                    CastWithGlow(agent, hero, ColorSchool.Red, "Crimson Torrent", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            SpellEffects.DamageAgent(a, 40f);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                        }
                    });
                    ApplyRedA1(agent); ApplyRedA2(agent);
                    return;
                }
                if (colors.Contains(ColorSchool.Blue))
                {
                    CastWithGlow(agent, hero, ColorSchool.Blue, "Azure Arrest", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        var formations = new System.Collections.Generic.HashSet<Formation>();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            try { a.SetMorale(0f); } catch { }
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Blue, 1.5f);
                            if (a.Formation != null) formations.Add(a.Formation);
                        }
                        foreach (Formation f in formations)
                            try { f.SetMovementOrder(MovementOrder.MovementOrderStop); } catch { }
                    });
                    return;
                }
            }

            // Ally support — Verdant Surge (Green) cone heal
            if (colors.Contains(ColorSchool.Green) && CanUseGreen(agent))
            {
                bool allyHurt = AlliesOf(agent).Any(a => a.Health < a.HealthLimit * 0.6f &&
                                                    a.Position.Distance(agent.Position) <= 15f);
                if (allyHurt)
                {
                    CastWithGlow(agent, hero, ColorSchool.Green, "Verdant Surge", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in AlliesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            float h = Math.Min(15f, a.HealthLimit - a.Health);
                            if (h > 0f) { a.Health += h; SpellEffects.BeginAgentGlow(a, ColorSchool.Green, 1.5f); }
                        }
                    });
                    return;
                }
            }

            // Yellow — Tide of Dread morale drain
            if (colors.Contains(ColorSchool.Yellow))
            {
                CastWithGlow(agent, hero, ColorSchool.Yellow, "Tide of Dread", () =>
                {
                    Vec3 fwd = agent.LookDirection.NormalizedCopy();
                    foreach (Agent a in EnemiesOf(agent).ToList())
                    {
                        Vec3 to = a.Position - agent.Position;
                        if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                        try { a.SetMorale(Math.Max(0f, a.GetMorale() - 30f)); } catch { }
                        SpellEffects.BeginAgentGlow(a, ColorSchool.Yellow, 1.5f);
                    }
                });
                SetCooldown(hero);
                return;
            }

            // Orange — Calling (summon) if outnumbered, else Warm Beacon (ally pull)
            if (colors.Contains(ColorSchool.Orange))
            {
                int nearAllies  = AlliesOf(agent).Count(a => a.Position.Distance(agent.Position) <= 20f);
                int nearEnemies = EnemiesOf(agent).Count(a => a.Position.Distance(agent.Position) <= 20f);
                if (nearEnemies > nearAllies)
                {
                    CastWithGlow(agent, hero, ColorSchool.Orange, "Calling", () =>
                    {
                        CharacterObject recruit = SpellEffects.FindRecruit(agent);
                        if (recruit == null || Mission.Current == null) return;
                        int count = _rng.Next(2, 4);
                        Vec3 back = -agent.LookDirection.NormalizedCopy(); back.z = 0f;
                        if (back.Length < 0.01f) back = new Vec3(-1f, 0f, 0f); else back = back.NormalizedCopy();
                        Vec3 perp = new Vec3(-back.y, back.x, 0f);
                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                float spread = (i - count / 2f) * 1.5f;
                                Vec3 pos = agent.Position + back * 3f + perp * spread;
                                pos.z = agent.Position.z;
                                Vec2 facing = (-back).AsVec2;
                                AgentBuildData abd = new AgentBuildData(recruit)
                                    .Team(agent.Team).InitialPosition(in pos).InitialDirection(in facing);
                                Agent spawned = Mission.Current.SpawnAgent(abd, false);
                                if (spawned == null) continue;
                                spawned.SetWatchState(Agent.WatchState.Alarmed);
                                SpellEffects.BeginAgentGlow(spawned, ColorSchool.Orange, 2f);
                            }
                            catch { }
                        }
                    });
                    return;
                }
                CastWithGlow(agent, hero, ColorSchool.Orange, "Warm Beacon", () =>
                {
                    foreach (Agent a in AlliesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 20f).ToList())
                    {
                        try { a.SetMorale(Math.Min(a.GetMorale() + 20f, 100f)); } catch { }
                        SpellEffects.BeginAgentGlow(a, ColorSchool.Orange, 1.5f);
                    }
                });
            }
        }

        private static void TryCastRandom(Agent agent, Hero hero, IReadOnlyList<ColorSchool> colors)
        {
            if (!CanCastAny(agent)) return;
            ColorSchool school = colors[_rng.Next(colors.Count)];
            switch (school)
            {
                case ColorSchool.Red:
                    CastWithGlow(agent, hero, ColorSchool.Red, "Crimson Torrent", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            SpellEffects.DamageAgent(a, 40f);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                        }
                    });
                    ApplyRedA1(agent); ApplyRedA2(agent);
                    break;
                case ColorSchool.Orange:
                    CastWithGlow(agent, hero, ColorSchool.Orange, "Warm Beacon", () =>
                    {
                        foreach (Agent a in AlliesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 20f).ToList())
                        {
                            try { a.SetMorale(Math.Min(a.GetMorale() + 20f, 100f)); } catch { }
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Orange, 1.5f);
                        }
                    });
                    break;
                case ColorSchool.Green when CanUseGreen(agent):
                    CastWithGlow(agent, hero, ColorSchool.Green, "Verdant Surge", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in AlliesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            float h = Math.Min(15f, a.HealthLimit - a.Health);
                            if (h > 0f) { a.Health += h; SpellEffects.BeginAgentGlow(a, ColorSchool.Green, 1.5f); }
                        }
                    });
                    break;
                case ColorSchool.Blue:
                    CastWithGlow(agent, hero, ColorSchool.Blue, "Azure Arrest", () =>
                    {
                        foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 30f).ToList())
                            try { a.SetMorale(0f); SpellEffects.BeginAgentGlow(a, ColorSchool.Blue, 1.5f); } catch { }
                    });
                    break;
                case ColorSchool.Yellow:
                    CastWithGlow(agent, hero, ColorSchool.Yellow, "Tide of Dread", () =>
                    {
                        foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 17f).ToList())
                            try { a.SetMorale(Math.Max(0f, a.GetMorale() - 30f)); SpellEffects.BeginAgentGlow(a, ColorSchool.Yellow, 1.5f); } catch { }
                    });
                    SetCooldown(hero);
                    break;
                case ColorSchool.Purple:
                    CastWithGlow(agent, hero, ColorSchool.Purple, "Grey Harvest", () =>
                    {
                        var targets = EnemiesOf(agent).Where(a => !a.IsHero).ToList();
                        if (targets.Count > 0)
                        {
                            Agent t = targets[_rng.Next(targets.Count)];
                            SpellEffects.BeginAgentGlow(t, ColorSchool.Purple, 1.5f);
                            SpellEffects.KillAgent(t);
                        }
                    });
                    ApplyPurpleAging(hero);
                    break;
            }
        }

        // ── Limitation checks ─────────────────────────────────────────────────
        // All magic now requires daylight (global rule); NPCs respect this too.
        private static bool CanCastAny(Agent agent) => SpellEffects.IsDaytime();

        private static bool CanUseGreen(Agent agent)
        {
            if (agent == null || !CanCastAny(agent)) return false;
            try { return agent.WieldedWeapon.IsEmpty || agent.WieldedWeapon.CurrentUsageItem?.IsShield == true; }
            catch { return true; }
        }

        private static void TrySheathWeapon(Agent agent)
        {
            try
            {
                if (agent.WieldedWeapon.IsEmpty || agent.WieldedWeapon.CurrentUsageItem?.IsShield == true) return;
                agent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand,
                    Agent.WeaponWieldActionType.WithAnimationUninterruptible);
            }
            catch { }
        }

        // ── Post-cast limitation side effects ─────────────────────────────────
        private static void ApplyRedA1(Agent agent)
        {
            if (agent?.Team == null || Mission.Current == null) return;
            foreach (Formation f in agent.Team.FormationsIncludingSpecialAndEmpty)
                try { if (f.CountOfUnits > 0) f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
        }
        private static void ApplyRedA2(Agent agent)
        {
            if (agent == null) return;
            try { agent.Health = Math.Max(1f, agent.Health - 8f); } catch { }
        }
        private static void ApplyPurpleAging(Hero hero)
        {
            if (hero == null) return;
            try { hero.SetBirthDay(hero.BirthDay - CampaignTime.Years(1f / 365f)); } catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static int CountEnemiesNear(Agent agent, float radius) =>
            EnemiesOf(agent).Count(a => a.Position.Distance(agent.Position) < radius);

        private static int CountEnemiesInCone(Agent agent, float radius, float dot) =>
            EnemiesOf(agent).Count(a =>
            {
                Vec3 to = a.Position - agent.Position;
                return to.Length < radius &&
                       Vec3.DotProduct(agent.LookDirection.NormalizedCopy(), to.NormalizedCopy()) > dot;
            });

        private static IEnumerable<Agent> EnemiesOf(Agent agent)
        {
            if (Mission.Current == null || agent?.Team == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != agent && !a.IsMount && a.IsActive() && a.Team != null && a.Team != agent.Team)
                    yield return a;
        }

        private static IEnumerable<Agent> AlliesOf(Agent agent)
        {
            if (Mission.Current == null || agent?.Team == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != agent && !a.IsMount && a.IsActive() && a.Team != null && a.Team == agent.Team)
                    yield return a;
        }

        private static void CastWithGlow(Agent agent, Hero hero, ColorSchool school,
                                          string spellName, Action effect)
        {
            try { effect?.Invoke(); } catch { }
            SetCooldown(hero);
            SpellEffects.BeginAgentGlow(agent, school, 3.0f);
            SpellEffects.TryCastSound(agent.Position, school);

            InformationManager.DisplayMessage(new InformationMessage(
                $"{agent.Name} channels {spellName} ({ColorSchoolData.Info[school].Name}).",
                ColorSchoolData.GetMessageColor(school)));
        }

        private static void SetCooldown(Hero hero) =>
            _cooldowns[hero.StringId] = CastInterval;
    }

    // =========================================================================
    // 11. COLOUR UNIT ENTRY  — data for one persistent magical common soldier
    // =========================================================================
    public class ColourUnitEntry
    {
        public string            Id;
        public string            DisplayName;
        public List<ColorSchool> Schools;
        public string            PartyStringId;
        public bool              IsAlive;
    }

    public class RespawnEntry
    {
        public string            PartyStringId;
        public List<ColorSchool> Schools;
        public int               DaysLeft;
    }

    // =========================================================================
    // 12. COLOUR UNIT REGISTRY
    //     Single-school mage soldiers embedded in armies and bandit groups.
    //     Lord parties (200+ troops): 1 unit; (350+ troops): 2 units. Daily 5% reseed.
    //     Bandit parties (40+ troops): 4% initial chance, 1% daily reseed.
    //     Units are persistent: they survive between battles and die permanently.
    //     On death, they re-queue (3–5 days) and respawn in a party of the same type.
    // =========================================================================
    public static class ColourUnitRegistry
    {
        private static readonly Dictionary<string, ColourUnitEntry> _units
            = new Dictionary<string, ColourUnitEntry>();

        // Per-mission: agentIndex → unitId
        private static readonly Dictionary<int, string> _missionAgents
            = new Dictionary<int, string>();
        private static readonly HashSet<string> _diedThisMission
            = new HashSet<string>();
        private static readonly Dictionary<string, float> _cooldowns
            = new Dictionary<string, float>();

        private static bool  _missionInitialized;
        private static bool  _seeded;
        private static int   _nextId = 1;
        private static float _aiAccum;
        private static readonly Random _rng = new Random();
        private static readonly Dictionary<string, int> _mapCooldowns = new Dictionary<string, int>();
        private static readonly List<RespawnEntry>      _respawnQueue  = new List<RespawnEntry>();

        private const float CastCooldown   = 20f;
        private const float AiTickInterval = 0.5f;

        // ── Name generation ───────────────────────────────────────────────────
        private static readonly string[] _firstNames =
        {
            "Mira", "Aldric", "Sena", "Corvin", "Thessa", "Boran", "Lirien",
            "Garath", "Vessa", "Doran", "Amael", "Corith", "Neva", "Rhuel",
            "Kessa", "Aldren", "Tireth", "Morva", "Selith", "Davan"
        };

        private static readonly Dictionary<ColorSchool, string[]> _schoolSuffixes =
            new Dictionary<ColorSchool, string[]>
            {
                [ColorSchool.Red]    = new[] { "the Ember",   "Bloodhanded", "Pyremark"    },
                [ColorSchool.Orange] = new[] { "the Bright",  "Goldenvoiced","the Warm"    },
                [ColorSchool.Yellow] = new[] { "the Pale",    "of Dread",    "the Craven"  },
                [ColorSchool.Green]  = new[] { "the Tender",  "Root-spoken", "the Verdant" },
                [ColorSchool.Blue]   = new[] { "the Still",   "Coldwater",   "the Patient" },
                [ColorSchool.Purple] = new[] { "the Hollow",  "the Grey",    "the Fading"  },
            };

        private static string NewId() => $"cou_{_nextId++}";

        private static string GenerateName(ColorSchool primary)
        {
            string first    = _firstNames[_rng.Next(_firstNames.Length)];
            string[] sfx    = _schoolSuffixes[primary];
            return $"{first} {sfx[_rng.Next(sfx.Length)]}";
        }

        // ── Seeding ───────────────────────────────────────────────────────────
        public static void SeedInitialUnits()
        {
            if (_seeded) return;
            _seeded = true;
            try
            {
                foreach (MobileParty party in MobileParty.All.ToList())
                {
                    if      (party.IsLordParty)              SeedLordParty(party);
                    else if (party.IsBandit) TrySeedBanditParty(party);
                }
            }
            catch { }
        }

        private static void SeedLordParty(MobileParty party)
        {
            // Only very large armies get 1-2 single-school mage soldiers
            int size = party.MemberRoster.TotalManCount;
            if (size < 200) return;
            int count = size >= 350 ? 2 : 1;
            for (int i = 0; i < count; i++)
                CreateUnit(party, 1);
        }

        private static void TrySeedBanditParty(MobileParty party)
        {
            if (party.MemberRoster.TotalManCount < 40) return;
            if (_rng.Next(100) >= 4) return;
            CreateUnit(party, 1);
        }

        private static void CreateUnit(MobileParty party, int schoolCount)
        {
            var pool   = new List<ColorSchool>((ColorSchool[])Enum.GetValues(typeof(ColorSchool)));
            var chosen = new List<ColorSchool>();
            for (int i = 0; i < Math.Min(schoolCount, pool.Count); i++)
            {
                int idx = _rng.Next(pool.Count);
                chosen.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            if (chosen.Count == 0) return;

            string id = NewId();
            _units[id] = new ColourUnitEntry
            {
                Id            = id,
                DisplayName   = GenerateName(chosen[0]),
                Schools       = chosen,
                PartyStringId = party.StringId,
                IsAlive       = true
            };
        }

        // Called every game day: clean up dead parties, process respawns, seed new ones.
        public static void DailyMaintenance()
        {
            try
            {
                // Decrement map cooldowns
                foreach (string key in _mapCooldowns.Keys.ToList())
                {
                    _mapCooldowns[key]--;
                    if (_mapCooldowns[key] <= 0) _mapCooldowns.Remove(key);
                }

                // Snapshot MobileParty.All once — repeated enumeration is the main perf cost.
                var allParties  = MobileParty.All.ToList();
                var allPartyIds = new HashSet<string>(allParties.Select(p => p.StringId));
                // O(1) lookup: which party IDs already have a living colour unit
                var occupiedIds = new HashSet<string>(
                    _units.Values.Where(u => u.IsAlive).Select(u => u.PartyStringId));

                // Process respawn queue
                foreach (RespawnEntry entry in _respawnQueue.ToList())
                {
                    entry.DaysLeft--;
                    if (entry.DaysLeft > 0) continue;

                    _respawnQueue.Remove(entry);

                    MobileParty origin = allParties.FirstOrDefault(p => p.StringId == entry.PartyStringId);
                    bool wasLord = origin?.IsLordParty ?? false;

                    MobileParty target;
                    if (wasLord)
                    {
                        var candidates = allParties
                            .Where(p => p.IsLordParty && p.MemberRoster.TotalManCount >= 200
                                        && !occupiedIds.Contains(p.StringId)).ToList();
                        target = candidates.Count > 0 ? candidates[_rng.Next(candidates.Count)] : null;
                    }
                    else
                    {
                        target = allParties.FirstOrDefault(p =>
                            p.StringId == entry.PartyStringId && p.IsBandit
                            && p.MemberRoster.TotalManCount >= 40);
                        if (target == null)
                        {
                            var candidates = allParties
                                .Where(p => p.IsBandit && p.MemberRoster.TotalManCount >= 40
                                            && !occupiedIds.Contains(p.StringId)).ToList();
                            target = candidates.Count > 0 ? candidates[_rng.Next(candidates.Count)] : null;
                        }
                    }

                    if (target == null)
                    {
                        entry.DaysLeft = 5;
                        _respawnQueue.Add(entry);
                        continue;
                    }

                    CreateUnit(target, entry.Schools.Count);
                    occupiedIds.Add(target.StringId);
                }

                // Remove units whose party disbanded — O(1) per unit using the HashSet
                foreach (var entry in _units.Values.ToList())
                {
                    if (!entry.IsAlive) continue;
                    if (!allPartyIds.Contains(entry.PartyStringId))
                        entry.IsAlive = false;
                }

                // Seed newly-qualifying parties
                foreach (MobileParty party in allParties)
                {
                    if (occupiedIds.Contains(party.StringId)) continue;

                    if (party.IsLordParty && party.MemberRoster.TotalManCount >= 200
                        && _rng.Next(100) < 5)
                    {
                        CreateUnit(party, 1);
                        occupiedIds.Add(party.StringId);
                    }
                    else if (party.IsBandit && party.MemberRoster.TotalManCount >= 40
                        && _rng.Next(100) < 1)
                    {
                        CreateUnit(party, 1);
                        occupiedIds.Add(party.StringId);
                    }
                }
            }
            catch { }
        }

        private const int MaxUnitMapCastsPerDay = 2;

        public static void DailyMapCast()
        {
            int castsToday = 0;
            foreach (ColourUnitEntry unit in _units.Values.ToList())
            {
                if (castsToday >= MaxUnitMapCastsPerDay) break;
                if (!unit.IsAlive || unit.Schools.Count == 0) continue;
                if (_mapCooldowns.ContainsKey(unit.Id)) continue;
                if (_rng.Next(100) >= 10) continue;

                ColorSchool school = unit.Schools[_rng.Next(unit.Schools.Count)];
                string      name   = unit.DisplayName;
                string      msg    = null;

                try
                {
                    switch (school)
                    {
                        case ColorSchool.Green:
                        {
                            MobileParty p = MobileParty.All.FirstOrDefault(x => x.StringId == unit.PartyStringId);
                            if (p != null) { p.RecentEventsMorale += 5f; msg = $"{name} channels Green and mends the weary."; }
                            break;
                        }
                        case ColorSchool.Orange:
                        {
                            MobileParty p = MobileParty.All.FirstOrDefault(x => x.StringId == unit.PartyStringId);
                            if (p != null) { p.RecentEventsMorale += 8f; msg = $"{name} stirs Orange fire — the column marches with purpose."; }
                            break;
                        }
                        case ColorSchool.Yellow:
                        {
                            MobileParty p = MobileParty.All.FirstOrDefault(x => x.StringId == unit.PartyStringId);
                            if (p?.LeaderHero != null)
                            {
                                p.LeaderHero.AddSkillXp(DefaultSkills.Leadership, 20f);
                                msg = $"{name} invokes Yellow resonance — wisdom flows through the ranks.";
                            }
                            break;
                        }
                        default:
                            msg = $"{name} murmurs in the tongue of {school} — something stirs.";
                            break;
                    }
                }
                catch { }

                if (msg != null)
                {
                    InformationManager.DisplayMessage(new InformationMessage(msg,
                        ColorSchoolData.GetMessageColor(school)));
                    _mapCooldowns[unit.Id] = 3 + _rng.Next(3);
                    castsToday++;
                }
            }
        }

        // ── Mission AI ────────────────────────────────────────────────────────
        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;

            _aiAccum += dt;
            if (_aiAccum < AiTickInterval) return;
            _aiAccum = 0f;

            if (!SpellEffects.IsBattleMission()) return;

            if (!_missionInitialized)
            {
                InitializeMissionAgents();
                _missionInitialized = true;
            }

            if (_missionAgents.Count == 0) return;

            foreach (string key in _cooldowns.Keys.ToList())
            {
                _cooldowns[key] -= AiTickInterval;
                if (_cooldowns[key] <= 0f) _cooldowns.Remove(key);
            }

            foreach (var kvp in _missionAgents.ToList())
            {
                if (_cooldowns.ContainsKey(kvp.Value)) continue;
                if (!_units.TryGetValue(kvp.Value, out ColourUnitEntry unit)) continue;

                Agent agent = Mission.Current.Agents.FirstOrDefault(a => a.Index == kvp.Key);
                if (agent == null || !agent.IsActive()) continue;

                CastUnitSpell(agent, unit);
            }
        }

        private static void InitializeMissionAgents()
        {
            _missionAgents.Clear();
            if (Mission.Current == null) return;

            // Collect non-hero agents per team
            var teamPool = new Dictionary<Team, List<Agent>>();
            foreach (Agent a in Mission.Current.Agents)
            {
                if (a.IsMount || a.IsHero || !a.IsActive() || a.Team == null) continue;
                if (!teamPool.ContainsKey(a.Team)) teamPool[a.Team] = new List<Agent>();
                teamPool[a.Team].Add(a);
            }

            // Map party StringId → team via hero agents
            var partyTeam = new Dictionary<string, Team>();
            foreach (Agent a in Mission.Current.Agents)
            {
                if (!a.IsHero || a.Team == null) continue;
                Hero hero = (a.Character as CharacterObject)?.HeroObject;
                if (hero?.PartyBelongedTo != null && !partyTeam.ContainsKey(hero.PartyBelongedTo.StringId))
                    partyTeam[hero.PartyBelongedTo.StringId] = a.Team;
            }
            if (Agent.Main?.Team != null && MobileParty.MainParty != null)
                partyTeam[MobileParty.MainParty.StringId] = Agent.Main.Team;

            // Assign one agent per live colour unit whose party is in this battle
            foreach (ColourUnitEntry unit in _units.Values)
            {
                if (!unit.IsAlive) continue;
                if (!partyTeam.TryGetValue(unit.PartyStringId, out Team team)) continue;
                if (!teamPool.TryGetValue(team, out var pool) || pool.Count == 0) continue;

                int   idx   = _rng.Next(pool.Count);
                Agent agent = pool[idx];
                pool.RemoveAt(idx);

                _missionAgents[agent.Index] = unit.Id;
                SpellEffects.BeginAgentGlow(agent, unit.Schools[0], 3f);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"✦ {unit.DisplayName} stirs with {ColorSchoolData.Info[unit.Schools[0]].Name} colour.",
                    ColorSchoolData.GetMessageColor(unit.Schools[0])));
            }
        }

        private static void CastUnitSpell(Agent agent, ColourUnitEntry unit)
        {
            ColorSchool school = unit.Schools[_rng.Next(unit.Schools.Count)];
            bool cast = false;

            try
            {
                switch (school)
                {
                    case ColorSchool.Red:
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 8f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.35f) continue;
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            SpellEffects.DamageAgent(a, 40f);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                            cast = true;
                        }
                        break;

                    case ColorSchool.Orange:
                        foreach (Agent a in AlliesOf(agent)
                            .Where(a => a.Position.Distance(agent.Position) <= 17f).ToList())
                            try { a.SetMorale(Math.Min(a.GetMorale() + 15f, 100f)); cast = true; } catch { }
                        break;

                    case ColorSchool.Yellow:
                        SpellEffects.IssueBattleCommand(agent, SpellEffects.BattleCommandKind.Halt,
                            "{0} formation{1} halted.", ColorSchool.Yellow);
                        cast = true;
                        break;

                    case ColorSchool.Green:
                        foreach (Agent a in AlliesOf(agent)
                            .Where(a => a.Health < a.HealthLimit * 0.7f
                                     && a.Position.Distance(agent.Position) <= 10f).ToList())
                        {
                            a.Health = Math.Min(a.Health + 20f, a.HealthLimit);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Green, 1.5f);
                            cast = true;
                        }
                        break;

                    case ColorSchool.Blue:
                        foreach (Agent a in EnemiesOf(agent)
                            .Where(a => a.Position.Distance(agent.Position) <= 17f).ToList())
                        {
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            try
                            {
                                SpellEffects.DamageAgent(a, 10f);
                                SpellEffects.BeginAgentGlow(a, ColorSchool.Blue, 1.5f);
                                cast = true;
                            }
                            catch { }
                        }
                        break;

                    case ColorSchool.Purple:
                        foreach (Agent a in EnemiesOf(agent)
                            .Where(a => a.Position.Distance(agent.Position) <= 6f).ToList())
                        {
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            SpellEffects.DamageAgent(a, 40f);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Purple, 1.5f);
                            cast = true;
                        }
                        break;
                }
            }
            catch { }

            if (!cast) return;

            SpellEffects.BeginAgentGlow(agent, school, 3f);
            SpellEffects.TryCastSound(agent.Position, school);

            InformationManager.DisplayMessage(new InformationMessage(
                $"{unit.DisplayName} channels {ColorSchoolData.Info[school].Name}!",
                ColorSchoolData.GetMessageColor(school)));

            _cooldowns[unit.Id] = CastCooldown;
        }

        // ── Death tracking ────────────────────────────────────────────────────
        public static void OnAgentRemoved(Agent agent)
        {
            if (!_missionAgents.TryGetValue(agent.Index, out string unitId)) return;
            _diedThisMission.Add(unitId);
            _missionAgents.Remove(agent.Index);
            if (_units.TryGetValue(unitId, out ColourUnitEntry unit))
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{unit.DisplayName} has fallen — their colour fades from the world.",
                    Color.FromUint(0xFF888888)));
        }

        public static void OnMissionEnded()
        {
            foreach (string id in _diedThisMission)
            {
                if (!_units.TryGetValue(id, out ColourUnitEntry u)) continue;
                u.IsAlive = false;
                _respawnQueue.Add(new RespawnEntry
                {
                    PartyStringId = u.PartyStringId,
                    Schools       = new List<ColorSchool>(u.Schools),
                    DaysLeft      = 3 + _rng.Next(3) // 3–5 days
                });
            }
            _diedThisMission.Clear();
            _missionAgents.Clear();
            _missionInitialized = false;
            _cooldowns.Clear();
            _aiAccum = 0f;
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static IEnumerable<Agent> EnemiesOf(Agent agent)
        {
            if (Mission.Current == null || agent?.Team == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != agent && !a.IsMount && a.IsActive() && a.Team != null && a.Team != agent.Team)
                    yield return a;
        }

        private static IEnumerable<Agent> AlliesOf(Agent agent)
        {
            if (Mission.Current == null || agent?.Team == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != agent && !a.IsMount && a.IsActive() && a.Team != null && a.Team == agent.Team)
                    yield return a;
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var entries     = _units.Values.ToList();
            var ids         = entries.Select(u => u.Id).ToList();
            var names       = entries.Select(u => u.DisplayName).ToList();
            var partyIds    = entries.Select(u => u.PartyStringId).ToList();
            var aliveFlags  = entries.Select(u => u.IsAlive ? 1 : 0).ToList();
            var schoolCnts  = entries.Select(u => u.Schools.Count).ToList();
            var flatSchools = entries.SelectMany(u => u.Schools.Select(s => (int)s)).ToList();
            bool seeded     = _seeded;
            int  nextId     = _nextId;

            var rqPartyIds  = _respawnQueue.Select(r => r.PartyStringId).ToList();
            var rqDays      = _respawnQueue.Select(r => r.DaysLeft).ToList();
            var rqSchoolCnt = _respawnQueue.Select(r => r.Schools.Count).ToList();
            var rqSchools   = _respawnQueue.SelectMany(r => r.Schools.Select(s => (int)s)).ToList();

            var cdKeys = _mapCooldowns.Keys.ToList();
            var cdVals = _mapCooldowns.Values.ToList();

            store.SyncData("COU_Ids",        ref ids);
            store.SyncData("COU_Names",       ref names);
            store.SyncData("COU_PartyIds",    ref partyIds);
            store.SyncData("COU_Alive",       ref aliveFlags);
            store.SyncData("COU_SchoolCnts",  ref schoolCnts);
            store.SyncData("COU_Schools",     ref flatSchools);
            store.SyncData("COU_Seeded",      ref seeded);
            store.SyncData("COU_NextId",      ref nextId);
            store.SyncData("COU_RqPartyIds",  ref rqPartyIds);
            store.SyncData("COU_RqDays",      ref rqDays);
            store.SyncData("COU_RqSchoolCnt", ref rqSchoolCnt);
            store.SyncData("COU_RqSchools",   ref rqSchools);
            store.SyncData("COU_CdKeys",      ref cdKeys);
            store.SyncData("COU_CdVals",      ref cdVals);

            _seeded = seeded;
            _nextId = Math.Max(_nextId, nextId);

            _units.Clear();
            if (ids == null) return;

            int si = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                int cnt = schoolCnts?[i] ?? 0;
                var schools = new List<ColorSchool>();
                for (int j = 0; j < cnt && si < (flatSchools?.Count ?? 0); j++, si++)
                    schools.Add((ColorSchool)flatSchools[si]);

                _units[ids[i]] = new ColourUnitEntry
                {
                    Id            = ids[i],
                    DisplayName   = names?[i]    ?? "Unknown",
                    PartyStringId = partyIds?[i] ?? "",
                    IsAlive       = (aliveFlags?[i] ?? 1) != 0,
                    Schools       = schools
                };
            }

            _respawnQueue.Clear();
            if (rqPartyIds != null)
            {
                int rsi = 0;
                for (int i = 0; i < rqPartyIds.Count; i++)
                {
                    int cnt = rqSchoolCnt?[i] ?? 0;
                    var schools = new List<ColorSchool>();
                    for (int j = 0; j < cnt && rsi < (rqSchools?.Count ?? 0); j++, rsi++)
                        schools.Add((ColorSchool)rqSchools[rsi]);
                    _respawnQueue.Add(new RespawnEntry
                    {
                        PartyStringId = rqPartyIds[i],
                        DaysLeft      = rqDays?[i] ?? 3,
                        Schools       = schools
                    });
                }
            }

            _mapCooldowns.Clear();
            if (cdKeys != null)
                for (int i = 0; i < cdKeys.Count && i < (cdVals?.Count ?? 0); i++)
                    _mapCooldowns[cdKeys[i]] = cdVals[i];
        }
    }
}
