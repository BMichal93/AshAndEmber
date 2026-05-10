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

        public override void OnMissionTick(float dt)
        {
            MagicInputHandler.Tick(inMission: true);
            ActiveEffectManager.MissionTick(dt);
            ColourLordAI.MissionTick(dt);
            SpellEffects.TickGlows(dt);
            SpellEffects.TickSteadyFreeze(dt);
            SpellEffects.TickRepel(dt);
            SpellEffects.TickRandomUnitMagic(dt);
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
                FlavorText       = "Angry, fiery magic of war and destruction. Red mages channel rage into devastating bursts of power. " +
                                   "Their spells tear through formations and drag enemies into blade range. " +
                                   "But the fire burns both ways — each working scorches the caster.",
                PersonalityEffect= "Repeated casting makes you less Calculating — more instinctive, more impulsive.",
                LimitationA      = "Furious: Each Red spell cast automatically issues a Charge order to your formations.",
                LimitationB      = "Fiery: Each Red spell deals a small wound to the caster.",
                AttributePenalty = "-1 Control"
            },
            [ColorSchool.Orange] = new SchoolInfo
            {
                Name             = "Orange",
                FlavorText       = "Joyful, jolly magic of generosity and camaraderie. Orange mages inspire those around them and conjure " +
                                   "allies from nothing. Their indulgent nature, however, drains resources at an alarming rate.",
                PersonalityEffect= "Repeated casting increases your Generosity — open-handed and free with what you have.",
                LimitationA      = "Overindulgent: Your party consumes food 50% faster.",
                LimitationB      = "Lighthearted: Each Orange spell costs gold (5% of total or 50 coins, whichever is higher). Cannot cast without coins.",
                AttributePenalty = "-1 Intellect"
            },
            [ColorSchool.Yellow] = new SchoolInfo
            {
                Name             = "Yellow",
                FlavorText       = "Careful, melancholic magic of caution and battlefield control. Yellow mages bend armies to their will — " +
                                   "grounding cavalry, silencing archers — but their cautious nature drains those around them of hope.",
                PersonalityEffect= "Repeated casting diminishes your Valor — careful where others are bold.",
                LimitationA      = "Uncharismatic: Your effective party size limit is always 3 lower.",
                LimitationB      = "Hopeless: Each Yellow spell causes your party to lose morale.",
                AttributePenalty = "-1 Vigor"
            },
            [ColorSchool.Green] = new SchoolInfo
            {
                Name             = "Green",
                FlavorText       = "Kind, caring magic of healing and restoration. Green mages sustain their companions through battle " +
                                   "and hardship. Their pacifist heart, however, cannot act while holding a blade.",
                PersonalityEffect= "Repeated casting increases your Mercy — slow to strike, quick to spare.",
                LimitationA      = "Pacifist: You cannot use Green magic while wielding a weapon in hand.",
                LimitationB      = "Steady: Each Green spell roots you in place for 3 seconds after casting.",
                AttributePenalty = "-1 Endurance"
            },
            [ColorSchool.Blue] = new SchoolInfo
            {
                Name             = "Blue",
                FlavorText       = "Cold, distanced magic of order and stillness. Blue mages freeze formations, conjure spectral protection, " +
                                   "and shock entire battlefields. But their scholarly nature exacts a toll in years.",
                PersonalityEffect= "Repeated casting increases your Calculating trait — measured, deliberate, distant.",
                LimitationA      = "Grounded: You cannot use Blue magic while on horseback.",
                LimitationB      = "Scholar: Each Blue spell costs a few days of your life.",
                AttributePenalty = "-1 Social"
            },
            [ColorSchool.Purple] = new SchoolInfo
            {
                Name             = "Purple",
                FlavorText       = "Lordly, cruel magic of dominance and sacrifice. Purple mages tear lives from the field, wither " +
                                   "entire groups, and bend unwilling minds. But their pride demands a retinue — and their power feeds on allies.",
                PersonalityEffect= "Repeated casting decreases your Mercy — lordly, proud, and unmoved.",
                LimitationA      = "Proud: You cannot cast without allied soldiers in your party.",
                LimitationB      = "Sacrifice: Each Purple spell deals significant damage to one of your allied units.",
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
                case ColorSchool.Yellow: return DefaultCharacterAttributes.Vigor;
                case ColorSchool.Green:  return DefaultCharacterAttributes.Endurance;
                case ColorSchool.Blue:   return DefaultCharacterAttributes.Social;
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
                case ColorSchool.Yellow: return (DefaultTraits.Valor,       -1);
                case ColorSchool.Green:  return (DefaultTraits.Mercy,       +1);
                case ColorSchool.Blue:   return (DefaultTraits.Calculating, +1);
                case ColorSchool.Purple: return (DefaultTraits.Mercy,       -1);
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
    // 3. SPELL DATABASE  (18 battle spells, 6-char combos)
    // =========================================================================
    public enum SpellContext { Mission, Map }

    public class SpellEntry
    {
        public string      Name;
        public string      Combo;      // exactly 6 chars: U / L / R
        public ColorSchool School;
        public SpellContext Context;
        public string      Flavour;
    }

    public static class SpellDatabase
    {
        public static readonly IReadOnlyList<SpellEntry> All = new List<SpellEntry>
        {
            // ── RED ────────────────────────────────────────────────────────────
            new SpellEntry { Name="Crush",       Combo="UUURRR", School=ColorSchool.Red,
                Context=SpellContext.Mission,
                Flavour="Anger made manifest. Everything in the cone bends." },
            new SpellEntry { Name="Vortex",      Combo="LRLRLU", School=ColorSchool.Red,
                Context=SpellContext.Mission,
                Flavour="You become the heaviest thing on the field." },
            new SpellEntry { Name="Fury",        Combo="URUURR", School=ColorSchool.Red,
                Context=SpellContext.Mission,
                Flavour="The urge to fight reaches everyone before their caution does." },

            // ── ORANGE ─────────────────────────────────────────────────────────
            new SpellEntry { Name="Encourage",   Combo="RLLRLL", School=ColorSchool.Orange,
                Context=SpellContext.Mission,
                Flavour="Not courage. Certainty. They feel — briefly — that you cannot lose." },
            new SpellEntry { Name="Calling",     Combo="UULRLU", School=ColorSchool.Orange,
                Context=SpellContext.Mission,
                Flavour="The call carries further than a voice. Those who hear it do not know why they march." },
            new SpellEntry { Name="March",       Combo="RRLLUU", School=ColorSchool.Orange,
                Context=SpellContext.Mission,
                Flavour="The road opens ahead. Your steps are light as the wind." },

            // ── YELLOW ─────────────────────────────────────────────────────────
            new SpellEntry { Name="Hold Arrows", Combo="LURLUR", School=ColorSchool.Yellow,
                Context=SpellContext.Mission,
                Flavour="The bowstrings slacken. The enemy remembers steel exists." },
            new SpellEntry { Name="Repel",       Combo="ULUURR", School=ColorSchool.Yellow,
                Context=SpellContext.Mission,
                Flavour="A pulse, every three seconds. The first time is a warning. The rest are a statement." },
            new SpellEntry { Name="Dismount",    Combo="RRUULL", School=ColorSchool.Yellow,
                Context=SpellContext.Mission,
                Flavour="Horses are told to be elsewhere. Riders comply the hard way." },

            // ── GREEN ──────────────────────────────────────────────────────────
            new SpellEntry { Name="Restore",     Combo="UULLUR", School=ColorSchool.Green,
                Context=SpellContext.Mission,
                Flavour="The body knows how to be whole. The Gift tells it to hurry." },
            new SpellEntry { Name="Aid",         Combo="ULLRUU", School=ColorSchool.Green,
                Context=SpellContext.Mission,
                Flavour="Life flows from you into them. Brief, but enough." },
            new SpellEntry { Name="Nurture",     Combo="UURLUL", School=ColorSchool.Green,
                Context=SpellContext.Mission,
                Flavour="Weariness lifts. The earth remembers how to give." },

            // ── BLUE ───────────────────────────────────────────────────────────
            new SpellEntry { Name="Shield",      Combo="LULURU", School=ColorSchool.Blue,
                Context=SpellContext.Mission,
                Flavour="Something cold and bright settles around you. Wounds feel far away." },
            new SpellEntry { Name="Stasis",      Combo="LLRRLU", School=ColorSchool.Blue,
                Context=SpellContext.Mission,
                Flavour="The enemy line remembers what it was doing and decides to stop." },
            new SpellEntry { Name="Stun",        Combo="RULRUL", School=ColorSchool.Blue,
                Context=SpellContext.Mission,
                Flavour="One point of contact. Every mind on the field staggers." },

            // ── PURPLE ─────────────────────────────────────────────────────────
            new SpellEntry { Name="Severe Life", Combo="UURRLL", School=ColorSchool.Purple,
                Context=SpellContext.Mission,
                Flavour="Somewhere on the field, someone stops. You did not choose who." },
            new SpellEntry { Name="Wither",      Combo="RLLURR", School=ColorSchool.Purple,
                Context=SpellContext.Mission,
                Flavour="Everything around you breaks. You are the still point." },
            new SpellEntry { Name="Subjugate",   Combo="LURRUL", School=ColorSchool.Purple,
                Context=SpellContext.Mission,
                Flavour="The will tears loose. They run — not from you, but from themselves." },
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
        // Accumulated casts per school — every 10 casts shifts the associated trait
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

        private static int _grimoirePage = 0;
        private const  int GrimoirePageSize = 4;

        public static bool HasAnySchool => _chosenSchools.Count > 0;
        public static bool HasSchool(ColorSchool school) => _chosenSchools.Contains(school);
        public static IEnumerable<ColorSchool> AllSchools => _chosenSchools;

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

            if (_castCounters[school] % 10 != 0) return;

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

            store.SyncData("COC_ChosenSchools",  ref schoolList);
            store.SyncData("COC_CastCounterKeys", ref ccKeys);
            store.SyncData("COC_CastCounterVals", ref ccVals);
            store.SyncData("COC_GiftedChildIds",  ref giftedList);

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

        private static readonly List<ColorSchool> _pendingSchools = new List<ColorSchool>();
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
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnMissionEnded);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.HeroCreated.AddNonSerializedListener(this, OnHeroCreated);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, OnCompanionAdded);
        }

        // ── New game: present colour selection ───────────────────────────────
        private void OnNewGameCreated()
        {
            _pendingSchools.Clear();
            AskAboutColor(0);
        }

        private void AskAboutColor(int index)
        {
            if (index >= _allSchoolsOrdered.Length)
            {
                FinishColorSelection();
                return;
            }

            ColorSchool school = _allSchoolsOrdered[index];
            var info = ColorSchoolData.Info[school];

            InformationManager.ShowInquiry(new InquiryData(
                titleText: $"The Colour of {info.Name}",
                text: $"{info.FlavorText}\n\n" +
                      $"Attribute penalty: {info.AttributePenalty}\n" +
                      $"Personality: {info.PersonalityEffect}\n\n" +
                      $"Limitation I — {info.LimitationA}\n" +
                      $"Limitation II — {info.LimitationB}",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown:    true,
                affirmativeText: $"{info.Name} calls to me.",
                negativeText:    "Not this colour.",
                affirmativeAction: () =>
                {
                    _pendingSchools.Add(school);
                    AskAboutColor(index + 1);
                },
                negativeAction: () => AskAboutColor(index + 1),
                soundEventPath: ""
            ));
        }

        private void FinishColorSelection()
        {
            _selectionDone = true;

            if (_pendingSchools.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "No colour calls to you. You walk an uncoloured path.",
                    Color.FromUint(0xFFAAAAAA)));
                _pendingSchools.Clear();
                return;
            }

            foreach (ColorSchool school in _pendingSchools)
                ColourKnowledge.AddSchool(school);

            ApplySchoolPenalties(_pendingSchools);
            ShowStartingSpells(_pendingSchools);

            _pendingSchools.Clear();
            ColourLordRegistry.SeedInitialLords();
        }

        private void ApplySchoolPenalties(List<ColorSchool> schools)
        {
            Hero player = Hero.MainHero;
            if (player == null) return;

            foreach (ColorSchool school in schools)
            {
                var info = ColorSchoolData.Info[school];
                // Attempt actual attribute reduction
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
        }

        private void ShowStartingSpells(List<ColorSchool> schools)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "Your colours are chosen. Hold Left Alt and type a 6-key combo (WASD) then release to cast. S opens spellbook.",
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
            {
                _selectionDone = true;
                // Fallback: ensure lords are seeded even on loaded saves
            }

            ColourLordRegistry.SeedInitialLords();
            ColourLordRegistry.DailyMapCast();
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

        // ── Mission ended ────────────────────────────────────────────────────
        private void OnMissionEnded(IMission mission)
        {
            ActiveEffectManager.ClearMissionEffects();
            ColourLordAI.ClearCooldowns();
            SpellEffects.ClearShieldHp();
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
                bool parentIsPlayer =
                    hero.Mother == Hero.MainHero || hero.Father == Hero.MainHero;
                if (!parentIsPlayer) return;
                if (MBRandom.RandomInt(100) < 30)
                {
                    ColourKnowledge.AddGiftedChild(hero.StringId);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{hero.Name} was born carrying a faint echo of colour.",
                        new Color(0.7f, 0.7f, 0.7f)));
                }
            }
            catch { }
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
        }
    }

    // =========================================================================
    // 7. INPUT HANDLER  — 6-key combo system (focus = Left Alt / LT)
    // =========================================================================
    public static class MagicInputHandler
    {
        private static string _buffer              = "";
        private static bool   _wasFocusing         = false;
        private static string _lastDisplayedBuffer = "";
        private const  int    MaxLen               = 10;

        public static bool InputSuppressed { get; private set; }

        public static void Tick(bool inMission)
        {
            if (!ColourKnowledge.HasAnySchool) { InputSuppressed = false; return; }

            bool focusing = Input.IsKeyDown(InputKey.LeftAlt)
                         || Input.IsKeyDown(InputKey.ControllerLTrigger);

            InputSuppressed = focusing;

            if (focusing)
            {
                _wasFocusing = true;

                if (!inMission && Campaign.Current != null)
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.UnstoppablePlay;

                if      (Input.IsKeyPressed(InputKey.W)) Append("U");
                else if (Input.IsKeyPressed(InputKey.A)) Append("L");
                else if (Input.IsKeyPressed(InputKey.D)) Append("R");
                else if (Input.IsKeyPressed(InputKey.S)) ColourKnowledge.ShowGrimoire();

                else if (Input.IsKeyPressed(InputKey.ControllerRUp))    Append("U");
                else if (Input.IsKeyPressed(InputKey.ControllerRLeft))  Append("L");
                else if (Input.IsKeyPressed(InputKey.ControllerRDown))  Append("R");
                else if (Input.IsKeyPressed(InputKey.ControllerLThumb)) ColourKnowledge.ShowGrimoire();

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

                if (!inMission && Campaign.Current != null)
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;

                _lastDisplayedBuffer = "";

                if (_buffer.Length >= 6)
                    TryCast(_buffer, inMission);
                else if (_buffer.Length > 0)
                    Fizzle("Incantation too short — colour magic requires six keys.");

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

            // B2 Lighthearted (Orange) — coin cost
            if (spell.School == ColorSchool.Orange)
            {
                Hero player = Hero.MainHero;
                if (player != null)
                {
                    int cost = Math.Max(50, (int)(player.Gold * 0.05f));
                    if (player.Gold < cost)
                    {
                        Fizzle($"Lighthearted: You need at least {cost} gold to cast an Orange spell.");
                        return;
                    }
                    // Deduct coins and remember for Calling
                    try { GiveGoldAction.ApplyBetweenCharacters(player, null, cost, true); }
                    catch { }
                    SpellEffects.LastOrangeCoinCost = cost;
                }
            }

            // D1 Pacifist (Green) — no weapon in hand
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

            // E1 Grounded (Blue) — no horseback
            if (spell.School == ColorSchool.Blue && inMission && Agent.Main?.MountAgent != null)
            {
                Fizzle("Grounded: Dismount before casting Blue magic.");
                return;
            }

            // F1 Proud (Purple) — need allied soldiers
            if (spell.School == ColorSchool.Purple)
            {
                MobileParty party = MobileParty.MainParty;
                int soldiers = party?.MemberRoster?.TotalManCount ?? 0;
                // Player counts as 1, so need at least 2
                if (soldiers < 2)
                {
                    Fizzle("Proud: You cannot cast without allied soldiers at your side.");
                    return;
                }
            }

            // ── Cast ─────────────────────────────────────────────────────────
            InformationManager.DisplayMessage(new InformationMessage(
                $"You channel {ColorSchoolData.Info[spell.School].Name} — {spell.Name}.",
                ColorSchoolData.GetMessageColor(spell.School)));

            bool success = SpellEffects.Execute(combo);

            // Visual: caster glow + charge animation + particles + sound
            if (inMission && Agent.Main != null)
                SpellEffects.CastGlow(Agent.Main, spell.School);

            if (!success) return;

            // ── Post-cast limitation side effects ─────────────────────────────

            // A1 Furious (Red) — issue Charge to own formations
            if (spell.School == ColorSchool.Red && inMission && Agent.Main != null)
            {
                SpellEffects.IssueChargeToOwnFormations(Agent.Main);
            }

            // A2 Fiery (Red) — small damage to caster
            if (spell.School == ColorSchool.Red && inMission && Agent.Main != null)
            {
                try { Agent.Main.Health = Math.Max(1f, Agent.Main.Health - 8f); }
                catch { }
            }

            // C2 Hopeless (Yellow) — party morale loss
            if (spell.School == ColorSchool.Yellow)
            {
                try { if (MobileParty.MainParty != null) MobileParty.MainParty.RecentEventsMorale -= 8f; }
                catch { }
            }

            // D2 Steady (Green) — freeze caster for 3 seconds
            if (spell.School == ColorSchool.Green && inMission)
                SpellEffects.ApplySteadyFreeze(Agent.Main, 3f);

            // E2 Scholar (Blue) — age caster
            if (spell.School == ColorSchool.Blue)
            {
                try
                {
                    Hero.MainHero?.SetBirthDay(
                        Hero.MainHero.BirthDay - CampaignTime.Years(1f / 12f)); // ~30 days per cast
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Scholar: The working ages you. | Age: {(int)(Hero.MainHero?.Age ?? 0)}",
                        ColorSchoolData.GetMessageColor(ColorSchool.Blue)));
                }
                catch { }
            }

            // F2 Sacrifice (Purple) — damage one allied unit
            if (spell.School == ColorSchool.Purple && inMission && Agent.Main != null)
                SpellEffects.ApplySacrifice(Agent.Main);

            // Personality drift
            ColourKnowledge.RecordCast(spell.School);
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

        // Set by Orange Calling so Calling's effect knows how many coins were spent
        public static int LastOrangeCoinCost { get; set; } = 0;

        // Shield HP bonus tracking (removed on mission end)
        private static float _shieldBonusHp = 0f;
        public static void ClearShieldHp()
        {
            if (_shieldBonusHp > 0f && Agent.Main != null && Agent.Main.IsActive())
            {
                try { Agent.Main.Health = Math.Max(1f, Agent.Main.Health - _shieldBonusHp); }
                catch { }
            }
            _shieldBonusHp = 0f;
        }

        // Repel state
        private static bool  _repelActive  = false;
        private static float _repelTimer   = 0f;
        private static float _repelElapsed = 0f;
        private const  float RepelInterval = 3f;
        private const  float RepelDuration = 60f;

        public static void TickRepel(float dt)
        {
            if (!_repelActive) return;
            _repelElapsed += dt;
            if (_repelElapsed >= RepelDuration)
            {
                _repelActive = false;
                _repelElapsed = 0f;
                InformationManager.DisplayMessage(new InformationMessage(
                    "Repel fades.", new Color(0.5f, 0.5f, 0.7f)));
                return;
            }
            _repelTimer += dt;
            if (_repelTimer < RepelInterval) return;
            _repelTimer -= RepelInterval;

            if (Player == null || Mission.Current == null) return;
            foreach (Agent a in Enemies().Where(a => a.Position.Distance(Player.Position) <= 5f).ToList())
            {
                Vec3 dir  = (a.Position - Player.Position).NormalizedCopy();
                Vec3 dest = a.Position + dir * 5f;
                dest.z = a.Position.z;
                try { a.TeleportToPosition(dest); BeginAgentGlow(a, ColorSchool.Yellow, 1.5f); }
                catch { }
            }
        }

        // Steady freeze (Green D2)
        private static float _steadyFreezeRemaining = 0f;
        private static ActionIndexCache _freezeCache = ActionIndexCache.Create("act_stand_1");

        public static void TickSteadyFreeze(float dt)
        {
            if (_steadyFreezeRemaining <= 0f) return;
            _steadyFreezeRemaining -= dt;
            if (Player == null || !Player.IsActive()) { _steadyFreezeRemaining = 0f; return; }
            try { Player.SetActionChannel(0, _freezeCache, true); } catch { }
        }

        public static void ApplySteadyFreeze(Agent caster, float duration)
        {
            if (caster == null || !caster.IsActive()) return;
            _steadyFreezeRemaining = duration;
        }

        // Sacrifice (Purple F2)
        public static void ApplySacrifice(Agent caster)
        {
            if (caster == null || Mission.Current == null) return;
            var ally = Mission.Current.Agents
                .Where(a => a.IsActive() && !a.IsMount && a != caster &&
                            caster.Team != null && a.Team == caster.Team)
                .OrderBy(a => _rng.Next())
                .FirstOrDefault();
            if (ally == null) return;
            try
            {
                ally.Health = Math.Max(0f, ally.Health - 60f);
                if (ally.Health <= 0f) KillAgent(ally);
                else BeginAgentGlow(ally, ColorSchool.Purple, 1.5f);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Sacrifice: {ally.Name} pays the price of your working.",
                    ColorSchoolData.GetMessageColor(ColorSchool.Purple)));
            }
            catch { }
        }

        // Issue Charge to own formations (Red A1)
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

            // 3% chance per minute
            if (_rng.Next(100) >= 3) return;

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
                        near.Health = Math.Max(0f, near.Health - 20f);
                        if (near.Health <= 0f) KillAgent(near);
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

        private static Blow BuildBlow(Agent target, DamageTypes type, float magnitude)
        {
            Blow blow = new Blow();
            blow.OwnerId         = Agent.Main?.Index ?? 0;
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
        public static bool Execute(string combo)
        {
            switch (combo)
            {
                // Red
                case "UUURRR": SpellCrush();     break;
                case "LRLRLU": SpellVortex();    break;
                case "URUURR": SpellFury();      break;
                // Orange
                case "RLLRLL": SpellEncourage(); break;
                case "UULRLU": SpellCalling();   break;
                case "RRLLUU": SpellMarch();     break;
                // Yellow
                case "LURLUR": SpellHoldArrows();break;
                case "ULUURR": SpellRepel();     break;
                case "RRUULL": SpellDismount();  break;
                // Green
                case "UULLUR": SpellRestore();   break;
                case "ULLRUU": SpellAid();       break;
                case "UURLUL": SpellNurture();   break;
                // Blue
                case "LULURU": SpellShield();    break;
                case "LLRRLU": SpellStasis();    break;
                case "RULRUL": SpellStun();      break;
                // Purple
                case "UURRLL": SpellSevereLife();break;
                case "RLLURR": SpellWither();    break;
                case "LURRUL": SpellSubjugate(); break;
                default: return false;
            }
            return true;
        }

        // =================================================================
        // RED SPELLS
        // =================================================================

        private static void SpellCrush()
        {
            if (Player == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            ActionIndexCache stagger = ActionIndexCache.Create("act_struck_from_back_medium_left_staff");
            int count = 0;
            foreach (Agent a in Enemies().ToList())
            {
                Vec3 toAgent = a.Position - Player.Position;
                if (toAgent.Length > 14f) continue;
                float dot = Vec3.DotProduct(fwd, toAgent.NormalizedCopy());
                if (dot < 0.25f) continue;

                float dmg = 80f * MageUnitBattery(Player);
                a.Health = Math.Max(0f, a.Health - dmg);
                if (a.Health <= 0f) KillAgent(a);
                else { try { a.SetActionChannel(0, stagger, false); } catch { } }
                count++;
                BeginAgentGlow(a, ColorSchool.Red, 1.5f);
            }
            Msg(count > 0 ? $"Crush tears through {count} {(count==1?"enemy":"enemies")} in the cone."
                          : "No enemies in range.", ColorSchool.Red);
        }

        private static void SpellVortex()
        {
            if (Player == null) return;
            ActionIndexCache trip = ActionIndexCache.Create("act_fall_back_on_ground");
            int count = 0;
            foreach (Agent a in Enemies().ToList())
            {
                float dist = a.Position.Distance(Player.Position);
                if (dist > 20f || dist < 0.5f) continue;
                Vec3 dir  = (Player.Position - a.Position).NormalizedCopy();
                float pull = Math.Min(dist - 0.5f, 8f);
                Vec3 dest  = a.Position + dir * pull;
                dest.z = a.Position.z;
                try
                {
                    a.TeleportToPosition(dest);
                    if (trip.Index >= 0) a.SetActionChannel(0, trip, true, (ulong)0);
                    count++;
                    BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                }
                catch { }
            }
            Msg(count > 0 ? $"{count} {(count==1?"enemy":"enemies")} dragged toward you."
                          : "No enemies within 20m.", ColorSchool.Red);
        }

        private static void SpellFury()
        {
            if (Player == null || Mission.Current == null) return;
            int count = 0;
            // Issue Charge to ALL formations on the battlefield (both sides)
            foreach (Team team in Mission.Current.Teams)
            {
                foreach (Formation f in team.FormationsIncludingSpecialAndEmpty)
                {
                    if (f.CountOfUnits <= 0) continue;
                    try
                    {
                        f.SetMovementOrder(MovementOrder.MovementOrderCharge);
                        count++;
                    }
                    catch { }
                }
                // Glow nearby agents
                foreach (Agent a in team.ActiveAgents.ToList())
                {
                    if (!a.IsMount && a.Position.Distance(Player.Position) <= 50f)
                        BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                }
            }
            Msg($"Fury ripples across the field. {count} formation{(count==1?"":"s")} surge to charge.",
                ColorSchool.Red);
        }

        // =================================================================
        // ORANGE SPELLS
        // =================================================================

        private static void SpellEncourage()
        {
            if (Mission.Current == null || Player == null) return;
            // Morale boost to all allied agents by simulating battle cheering
            int affected = 0;
            foreach (Agent a in Allies().ToList())
            {
                try
                {
                    // Cannot directly set agent morale in battle easily; use SetMorale capped up
                    float cur = a.GetMorale();
                    a.SetMorale(Math.Min(cur + 30f, 100f));
                    BeginAgentGlow(a, ColorSchool.Orange, 1.5f);
                    affected++;
                }
                catch { }
            }
            // Also boost campaign morale for after battle
            try { if (MobileParty.MainParty != null) MobileParty.MainParty.RecentEventsMorale += 15f; }
            catch { }
            Msg($"Your voice carries across the field. {affected} allies are emboldened.", ColorSchool.Orange);
        }

        private static void SpellCalling()
        {
            int cost    = LastOrangeCoinCost;
            int recruits = Math.Max(1, cost / 25);

            CharacterObject recruit = FindImperialRecruit();
            if (recruit == null)
            {
                Msg("The call finds no ears.", ColorSchool.Orange);
                return;
            }
            try
            {
                MobileParty.MainParty?.MemberRoster.AddToCounts(recruit, recruits);
                Msg($"{recruits} Imperial soldier{(recruits==1?"":"s")} answer your call — {cost} gold spent.",
                    ColorSchool.Orange);
            }
            catch { Msg("The call was heard but none came.", ColorSchool.Orange); }
        }

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

        private static void SpellMarch()
        {
            if (Player == null || !Player.IsActive()) return;
            if (ActiveEffectManager.Has("_march")) { Msg("Already marching.", ColorSchool.Orange); return; }

            const float SpeedMult = 1.5f;
            const float Duration  = 90f; // ~1.5 minutes

            ActiveEffectManager.Add(new ActiveEffect
            {
                Name = "_march", Duration = Duration, IsMissionEffect = true,
                OnTick = _ =>
                {
                    if (Player == null || !Player.IsActive()) return;
                    try
                    {
                        Player.AgentDrivenProperties.MaxSpeedMultiplier = SpeedMult;
                        Player.UpdateAgentStats();
                        if (Player.MountAgent != null)
                        {
                            Player.MountAgent.AgentDrivenProperties.MaxSpeedMultiplier = SpeedMult;
                            Player.MountAgent.UpdateAgentStats();
                        }
                    }
                    catch { }
                },
                OnExpire = () =>
                {
                    if (Player == null || !Player.IsActive()) return;
                    try
                    {
                        Player.AgentDrivenProperties.MaxSpeedMultiplier = 1f;
                        Player.UpdateAgentStats();
                        if (Player.MountAgent != null)
                        {
                            Player.MountAgent.AgentDrivenProperties.MaxSpeedMultiplier = 1f;
                            Player.MountAgent.UpdateAgentStats();
                        }
                        Msg("March fades. Your pace returns to normal.", ColorSchool.Orange);
                    }
                    catch { }
                }
            });
            Msg("Your strides lengthen. Speed increased for 90 seconds.", ColorSchool.Orange);
        }

        // =================================================================
        // YELLOW SPELLS
        // =================================================================

        private static void SpellHoldArrows()
        {
            IssueBattleCommand(Player, BattleCommandKind.StopArrows,
                "{0} enemy formation{1} ordered to hold fire.", ColorSchool.Yellow);
        }

        private static void SpellRepel()
        {
            if (_repelActive) { Msg("Repel is already active.", ColorSchool.Yellow); return; }
            _repelActive  = true;
            _repelElapsed = 0f;
            _repelTimer   = 0f;
            Msg($"A repelling pulse surrounds you — every {(int)RepelInterval} seconds for {(int)RepelDuration}s.",
                ColorSchool.Yellow);
        }

        private static void SpellDismount()
        {
            IssueBattleCommand(Player, BattleCommandKind.Dismount,
                "{0} enemy formation{1} forced from their mounts.", ColorSchool.Yellow);
        }

        // =================================================================
        // GREEN SPELLS
        // =================================================================

        private static void SpellRestore()
        {
            if (Player == null) return;
            float heal = Math.Min(40f, Player.HealthLimit - Player.Health);
            Player.Health = Math.Min(Player.Health + 40f, Player.HealthLimit);
            Msg($"You restore yourself. +{heal:F0} HP.", ColorSchool.Green);
        }

        private static void SpellAid()
        {
            if (Player == null || Mission.Current == null) return;
            int healed = 0;
            float radius = 12f;
            foreach (Agent a in Allies().Where(a => a.Position.Distance(Player.Position) <= radius).ToList())
            {
                float h = Math.Min(25f, a.HealthLimit - a.Health);
                if (h <= 0f) continue;
                a.Health += h;
                BeginAgentGlow(a, ColorSchool.Green, 1.5f);
                healed++;
            }
            Msg(healed > 0 ? $"Aid heals {healed} allies within {radius}m."
                           : "No allies in range.", ColorSchool.Green);
        }

        private static void SpellNurture()
        {
            if (Player == null || Mission.Current == null) return;
            float radius = 25f;
            int affected = 0;
            foreach (Agent a in Allies().Where(a => a.Position.Distance(Player.Position) <= radius).ToList())
            {
                try
                {
                    // Restore morale and a small HP
                    a.SetMorale(Math.Min(a.GetMorale() + 40f, 100f));
                    a.Health = Math.Min(a.Health + 10f, a.HealthLimit);
                    BeginAgentGlow(a, ColorSchool.Green, 1.5f);
                    affected++;
                }
                catch { }
            }
            try { if (MobileParty.MainParty != null) MobileParty.MainParty.RecentEventsMorale += 10f; }
            catch { }
            Msg($"Nurture refreshes {affected} allies — weariness lifts.", ColorSchool.Green);
        }

        // =================================================================
        // BLUE SPELLS
        // =================================================================

        private static void SpellShield()
        {
            if (Player == null) return;
            const float BonusHp = 80f;
            _shieldBonusHp += BonusHp;
            Player.Health = Math.Min(Player.Health + BonusHp, Player.HealthLimit + BonusHp);
            Msg("A shield of blue light forms around you, absorbing injury. (+80 HP)", ColorSchool.Blue);
        }

        private static void SpellStasis()
        {
            IssueBattleCommand(Player, BattleCommandKind.Halt,
                "{0} enemy formation{1} frozen in place.", ColorSchool.Blue);
        }

        private static void SpellStun()
        {
            if (Player == null || Mission.Current == null) return;
            const float StunRadius = 30f;
            int count = 0;
            foreach (Agent a in Enemies().Where(a => a.Position.Distance(Player.Position) <= StunRadius).ToList())
            {
                try
                {
                    a.Health = Math.Max(1f, a.Health - 15f);
                    BeginAgentGlow(a, ColorSchool.Blue, 1.5f);
                    count++;
                }
                catch { }
            }
            Msg($"{count} {(count == 1 ? "enemy" : "enemies")} within {StunRadius}m stunned for 15 damage.", ColorSchool.Blue);
        }

        // =================================================================
        // PURPLE SPELLS
        // =================================================================

        private static void SpellSevereLife()
        {
            if (Player == null) return;
            var targets = Enemies().Where(a => !a.IsHero).ToList();
            if (targets.Count == 0) { Msg("No valid targets.", ColorSchool.Purple); return; }
            Agent target = targets[_rng.Next(targets.Count)];
            BeginAgentGlow(target, ColorSchool.Purple, 1.5f);
            KillAgent(target);
            Msg($"Somewhere on the field, {target.Name} simply stops.", ColorSchool.Purple);
        }

        private static void SpellWither()
        {
            if (Player == null || Mission.Current == null) return;
            float radius = 10f;
            int count = 0;
            foreach (Agent a in Enemies().Where(a => a.Position.Distance(Player.Position) <= radius).ToList())
            {
                float dmg = 60f * MageUnitBattery(Player);
                a.Health = Math.Max(0f, a.Health - dmg);
                if (a.Health <= 0f) KillAgent(a);
                BeginAgentGlow(a, ColorSchool.Purple, 1.5f);
                count++;
            }
            Msg(count > 0 ? $"Wither tears through {count} {(count==1?"enemy":"enemies")} around you."
                          : "No enemies nearby.", ColorSchool.Purple);
        }

        private static void SpellSubjugate()
        {
            if (Player == null || Mission.Current == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            Agent target = Enemies()
                .Where(a => !a.IsHero && a.Position.Distance(Player.Position) <= 15f &&
                            Vec3.DotProduct(fwd, (a.Position - Player.Position).NormalizedCopy()) >= 0.4f)
                .OrderBy(a => _rng.Next())
                .FirstOrDefault();
            if (target == null) { Msg("No target in forward cone.", ColorSchool.Purple); return; }

            BeginAgentGlow(target, ColorSchool.Purple, 1.5f);
            try { target.SetMorale(0f); target.Retreat(true); }
            catch { }
            Msg($"You shatter {target.Name}'s will. They flee.", ColorSchool.Purple);
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
                bool visible = false;
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
                        case BattleCommandKind.Halt:       f.SetMovementOrder(MovementOrder.MovementOrderStop); affected++; break;
                        case BattleCommandKind.Enrage:     f.SetMovementOrder(MovementOrder.MovementOrderCharge); affected++; break;
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

        public static void TickGlows(float dt)
        {
            for (int i = _glowTimers.Count - 1; i >= 0; i--)
            {
                float t = _glowTimers[i].remaining - dt;
                if (t <= 0f)
                {
                    try { _glowTimers[i].agent?.AgentVisuals?.GetEntity()
                              ?.SetContourColor(null, false); } catch { }
                    _glowTimers.RemoveAt(i);
                }
                else _glowTimers[i] = (_glowTimers[i].agent, t);
            }
        }

        public static void BeginAgentGlow(Agent agent, ColorSchool school, float duration)
        {
            if (agent == null) return;
            try
            {
                uint col = ColorSchoolData.GetGlowColor(school);
                agent.AgentVisuals?.GetEntity()?.SetContourColor(col, true);
                _glowTimers.RemoveAll(x => x.agent == agent);
                _glowTimers.Add((agent, duration));
            }
            catch { }
        }

        public static void CastGlow(Agent caster, ColorSchool school)
        {
            if (caster == null) return;
            try
            {
                BeginAgentGlow(caster, school, 3.0f); // caster = 3 seconds
                PlayChargeAnimation(caster);
                TrySpawnCastParticle(caster.Position, school);
                TryCastSound(caster.Position, school);
            }
            catch { }
        }

        private static void PlayChargeAnimation(Agent caster)
        {
            // Charge gesture on spellcast
            string[] candidates =
            {
                "act_start_general_charge_infantry",
                "act_general_charge_cavalry",
                "act_yield_hard",
                "act_pickup_boulder_begin",
                "act_struck_from_back_medium_left_staff" // confirmed working fallback
            };
            foreach (string name in candidates)
            {
                try
                {
                    ActionIndexCache cache = ActionIndexCache.Create(name);
                    if (cache.Index < 0) continue;
                    caster.SetActionChannel(0, cache, false);
                    return;
                }
                catch { }
            }
        }

        private static void TrySpawnCastParticle(Vec3 position, ColorSchool school)
        {
            try
            {
                var mission = Mission.Current;
                if (mission == null) return;

                // Pick particle by school family
                string particleName;
                switch (school)
                {
                    case ColorSchool.Red:
                    case ColorSchool.Purple: particleName = "psys_fire_field_1m";   break;
                    case ColorSchool.Green:  particleName = "psys_spark_shimmer";   break;
                    default:                 particleName = "psys_env_ghost_dust";  break;
                }

                Type psmType = Type.GetType("TaleWorlds.Engine.ParticleSystemManager, TaleWorlds.Engine");
                MethodInfo getId = psmType?.GetMethod("GetRuntimeIdByName", BindingFlags.Public | BindingFlags.Static);
                object idObj = getId?.Invoke(null, new object[] { particleName });
                if (idObj == null || (int)idObj < 0) return;

                MethodInfo addEffect = typeof(Mission).GetMethod("AddParticleEffectAtFrame",
                    new Type[] { typeof(int), typeof(MatrixFrame) });
                if (addEffect == null) return;

                MatrixFrame frame = MatrixFrame.Identity;
                frame.origin = position + new Vec3(0, 0, 1.0f);
                addEffect.Invoke(mission, new object[] { idObj, frame });
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

        private static void TryCastSound(Vec3 position, ColorSchool school)
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
    //      ~15% of lords → 1 random colour
    //      ~10%          → 2 colours
    //      ~10%          → 3 colours
    //       1 lord       → 4 colours (faction archmage)
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
            InformationManager.DisplayMessage(new InformationMessage(
                $"Four colours shine in {archmage.Name} of {faction.Name}.",
                new Color(0.9f, 0.7f, 0.9f)));

            for (int i = 1; i < lords.Count; i++)
            {
                int roll = _rng.Next(100);
                int colorCount;
                if      (roll < 10) colorCount = 3;
                else if (roll < 20) colorCount = 2;
                else if (roll < 35) colorCount = 1;
                else                continue;

                _lordColors[lords[i].StringId] = PickColors(colorCount);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{colorCount} colour{(colorCount>1?"s":"")} stir in {lords[i].Name} of {faction.Name}.",
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
            InformationManager.DisplayMessage(new InformationMessage(
                $"{companion.Name} carries {count} colour{(count>1?"s":"")} — they will fight with them.",
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
                if (candidates.Count == 0) continue;

                Hero chosen = candidates[_rng.Next(candidates.Count)];
                _lordColors[chosen.StringId] = PickColors(1 + _rng.Next(2));
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Colour magic stirs anew in {chosen.Name} of {kingdom.Name}.",
                    new Color(0.7f, 0.5f, 0.8f)));
            }
        }

        // ── Campaign map effects (lord-only) ──────────────────────────────────
        public static void DailyMapCast()
        {
            foreach (var kvp in _lordColors.ToList())
            {
                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == kvp.Key);
                if (hero == null || !hero.IsAlive) continue;

                if (_campaignCooldowns.TryGetValue(kvp.Key, out int cd) && cd > 0)
                { _campaignCooldowns[kvp.Key] = cd - 1; continue; }

                if (_rng.Next(100) >= 20) continue; // 20% chance per day

                _campaignCooldowns[kvp.Key] = 6 + _rng.Next(4);
                ColorSchool school = kvp.Value[_rng.Next(kvp.Value.Count)];
                CastLordMapSpell(hero, school);
            }
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
                            // Bloodlust — own party morale
                            spellName = "Bloodlust";
                            if (lord.PartyBelongedTo != null)
                            { lord.PartyBelongedTo.RecentEventsMorale += 10f; msg = $"{lord.Name}'s warband burns with purpose."; }
                        }
                        else
                        {
                            // Carnage — nearby village loses prosperity
                            spellName = "Carnage";
                            Settlement target = Settlement.All
                                .Where(s => s.IsVillage && s.Village != null)
                                .OrderBy(s => _rng.Next()).FirstOrDefault();
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
                            // Celebrate — own town/village prosperity
                            spellName = "Celebrate";
                            var ownVillage = Settlement.All
                                .Where(s => s.IsVillage && s.MapFaction == lord.MapFaction && s.Village != null)
                                .OrderBy(s => _rng.Next()).FirstOrDefault();
                            if (ownVillage?.Village != null)
                            {
                                ownVillage.Village.Hearth = Math.Min(2000f, ownVillage.Village.Hearth * 1.05f);
                                msg = $"{ownVillage.Name} flourishes under {lord.Name}'s blessing.";
                            }
                        }
                        else
                        {
                            // Bribe — random lord loses 1-2 troops, they join orange lord
                            spellName = "Bribe";
                            Hero rival = Hero.AllAliveHeroes
                                .Where(h => h.IsLord && h.MapFaction != lord.MapFaction && h.PartyBelongedTo != null && h.IsAlive)
                                .OrderBy(h => _rng.Next()).FirstOrDefault();
                            if (rival?.PartyBelongedTo != null && lord.PartyBelongedTo != null)
                            {
                                int take = 1 + _rng.Next(2);
                                var troop = rival.PartyBelongedTo.MemberRoster.GetTroopRoster()
                                    .Where(e => !e.Character.IsHero && e.Number > 0)
                                    .OrderBy(e => _rng.Next()).FirstOrDefault();
                                if (troop.Character != null)
                                {
                                    int actual = Math.Min(take, troop.Number);
                                    rival.PartyBelongedTo.MemberRoster.RemoveTroop(troop.Character, actual);
                                    lord.PartyBelongedTo.MemberRoster.AddToCounts(troop.Character, actual);
                                    msg = $"{actual} soldier{(actual>1?"s":"")} desert {rival.Name} for {lord.Name}'s generosity.";
                                }
                            }
                        }
                        break;

                    case ColorSchool.Yellow:
                        if (_rng.Next(2) == 0)
                        {
                            // Fade — random enemy lord loses renown
                            spellName = "Fade";
                            Hero target = Hero.AllAliveHeroes
                                .Where(h => h.IsLord && h.MapFaction != lord.MapFaction && h.Clan != null && h.IsAlive)
                                .OrderBy(h => _rng.Next()).FirstOrDefault();
                            if (target?.Clan != null)
                            {
                                try { target.Clan.AddRenown(-10f); } catch { }
                                msg = $"{target.Name}'s name grows quieter — {lord.Name}'s melancholy reaches far.";
                            }
                        }
                        else
                        {
                            // Melancholy — random lord's army loses morale
                            spellName = "Melancholy";
                            Hero target = Hero.AllAliveHeroes
                                .Where(h => h.IsLord && h.MapFaction != lord.MapFaction && h.PartyBelongedTo != null && h.IsAlive)
                                .OrderBy(h => _rng.Next()).FirstOrDefault();
                            if (target?.PartyBelongedTo != null)
                            {
                                target.PartyBelongedTo.RecentEventsMorale -= 15f;
                                msg = $"Despair settles over {target.Name}'s ranks — {lord.Name} breathes sadness into the world.";
                            }
                        }
                        break;

                    case ColorSchool.Green:
                        if (_rng.Next(2) == 0)
                        {
                            // Rejuvenate — heal own party
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
                            // Crops — add grain
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
                            // Schemes — own clan influence
                            spellName = "Schemes";
                            if (lord.Clan != null)
                            {
                                try { ChangeClanInfluenceAction.Apply(lord.Clan, 8f); }
                                catch { }
                                msg = $"{lord.Name}'s schemes bear fruit — their clan's influence grows.";
                            }
                        }
                        else
                        {
                            // Plots — reduce enemy clan influence
                            spellName = "Plots";
                            Hero target = Hero.AllAliveHeroes
                                .Where(h => h.IsLord && h.MapFaction != lord.MapFaction && h.Clan != null && h.IsAlive)
                                .OrderBy(h => _rng.Next()).FirstOrDefault();
                            if (target?.Clan != null)
                            {
                                try { ChangeClanInfluenceAction.Apply(target.Clan, -8f); }
                                catch { }
                                msg = $"{lord.Name}'s cold plots undermine {target.Name}'s standing.";
                            }
                        }
                        break;

                    case ColorSchool.Purple:
                        if (_rng.Next(2) == 0)
                        {
                            // Curse — reduce influence and morale of random other lord
                            spellName = "Curse";
                            Hero target = Hero.AllAliveHeroes
                                .Where(h => h.IsLord && h.MapFaction != lord.MapFaction && h.Clan != null && h.IsAlive && h.PartyBelongedTo != null)
                                .OrderBy(h => _rng.Next()).FirstOrDefault();
                            if (target != null)
                            {
                                try { ChangeClanInfluenceAction.Apply(target.Clan, -5f); } catch { }
                                try { target.PartyBelongedTo.RecentEventsMorale -= 10f; } catch { }
                                msg = $"A curse falls on {target.Name} — their influence wanes and their soldiers are unsettled.";
                            }
                        }
                        else
                        {
                            // Bind — gain 10 free recruits
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
            var lordIds   = _lordColors.Keys.ToList();
            var lordVals  = _lordColors.Values.Select(v => v.Select(s => (int)s).ToList()).ToList();
            var rKeys     = _respawnHours.Keys.ToList();
            var rVals     = _respawnHours.Values.ToList();
            var ccKeys    = _campaignCooldowns.Keys.ToList();
            var ccVals    = _campaignCooldowns.Values.ToList();
            bool seeded   = _seeded;

            store.SyncData("COC_LordIds",      ref lordIds);
            store.SyncData("COC_LordVals",     ref lordVals);
            store.SyncData("COC_RespawnKeys",  ref rKeys);
            store.SyncData("COC_RespawnVals",  ref rVals);
            store.SyncData("COC_CdKeys",       ref ccKeys);
            store.SyncData("COC_CdVals",       ref ccVals);
            store.SyncData("COC_LordSeeded",   ref seeded);

            _seeded = seeded;

            _lordColors.Clear();
            if (lordIds != null && lordVals != null)
                for (int i = 0; i < Math.Min(lordIds.Count, lordVals.Count); i++)
                    if (lordVals[i] != null)
                        _lordColors[lordIds[i]] = lordVals[i].Select(v => (ColorSchool)v).ToList();

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

                DecideAndCast(agent, hero, colors);
            }
        }

        private static void DecideAndCast(Agent agent, Hero hero, IReadOnlyList<ColorSchool> colors)
        {
            float hpPct = agent.Health / Math.Max(agent.HealthLimit, 1f);

            // Self-heal with Green if available and hurt
            if (hpPct < 0.35f && colors.Contains(ColorSchool.Green))
            {
                if (!CanUseGreen(agent)) goto SkipGreen;
                CastWithGlow(agent, hero, ColorSchool.Green, "Restore", () =>
                {
                    agent.Health = Math.Min(agent.Health + 40f, agent.HealthLimit);
                });
                ApplyGreenD2(agent);
                return;
                SkipGreen:;
            }

            // Random 8% chance to cast something unusual
            if (_rng.Next(100) < 8)
            {
                TryCastRandom(agent, hero, colors);
                return;
            }

            // Enemies swarming — Red Wither or Purple Wither
            int closeEnemies = CountEnemiesNear(agent, 8f);
            if (closeEnemies >= 3)
            {
                if (colors.Contains(ColorSchool.Purple) && CanUsePurple(agent))
                {
                    CastWithGlow(agent, hero, ColorSchool.Purple, "Wither", () =>
                    {
                        foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 8f).ToList())
                        {
                            a.Health = Math.Max(0f, a.Health - 50f);
                            if (a.Health <= 0f) SpellEffects.KillAgent(a);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Purple, 1.5f);
                        }
                    });
                    ApplyPurpleF2(agent);
                    return;
                }
                if (colors.Contains(ColorSchool.Red))
                {
                    CastWithGlow(agent, hero, ColorSchool.Red, "Vortex", () =>
                    {
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            float dist = a.Position.Distance(agent.Position);
                            if (dist > 12f || dist < 0.5f) continue;
                            Vec3 dir  = (agent.Position - a.Position).NormalizedCopy();
                            Vec3 dest = a.Position + dir * Math.Min(dist - 0.5f, 6f);
                            dest.z = a.Position.z;
                            try { a.TeleportToPosition(dest); SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f); }
                            catch { }
                        }
                    });
                    ApplyRedA1(agent);
                    ApplyRedA2(agent);
                    return;
                }
            }

            // Cone enemies — Red Crush or Blue Stun
            int coneEnemies = CountEnemiesInCone(agent, 12f, 0.5f);
            if (coneEnemies >= 2)
            {
                if (colors.Contains(ColorSchool.Red))
                {
                    CastWithGlow(agent, hero, ColorSchool.Red, "Crush", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 12f) continue;
                            if (Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.4f) continue;
                            a.Health = Math.Max(0f, a.Health - 60f);
                            if (a.Health <= 0f) SpellEffects.KillAgent(a);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                        }
                    });
                    ApplyRedA1(agent);
                    ApplyRedA2(agent);
                    return;
                }
                if (colors.Contains(ColorSchool.Blue) && CanUseBlue(agent))
                {
                    CastWithGlow(agent, hero, ColorSchool.Blue, "Stun", () =>
                    {
                        foreach (Agent a in EnemiesOf(agent).ToList())
                            try { a.Health = Math.Max(a.Health - 1f, 0f); } catch { }
                    });
                    return;
                }
            }

            // Ally support — Green Aid
            if (colors.Contains(ColorSchool.Green) && CanUseGreen(agent))
            {
                bool allyHurt = AlliesOf(agent).Any(a => a.Health < a.HealthLimit * 0.6f &&
                                                    a.Position.Distance(agent.Position) <= 12f);
                if (allyHurt)
                {
                    CastWithGlow(agent, hero, ColorSchool.Green, "Aid", () =>
                    {
                        foreach (Agent a in AlliesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 12f).ToList())
                        {
                            a.Health = Math.Min(a.Health + 20f, a.HealthLimit);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Green, 1.5f);
                        }
                    });
                    ApplyGreenD2(agent);
                    return;
                }
            }

            // Yellow battlefield control
            if (colors.Contains(ColorSchool.Yellow))
            {
                SpellEffects.IssueBattleCommand(agent, SpellEffects.BattleCommandKind.Halt,
                    "{0} formation{1} brought to halt.", ColorSchool.Yellow);
                SetCooldown(hero);
                return;
            }

            // Orange fallback — lords with only Orange still cast Encourage in battle
            if (colors.Contains(ColorSchool.Orange))
            {
                CastWithGlow(agent, hero, ColorSchool.Orange, "Encourage", () =>
                {
                    foreach (Agent a in AlliesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 20f).ToList())
                        try { a.SetMorale(Math.Min(a.GetMorale() + 20f, 100f)); } catch { }
                });
            }
        }

        private static void TryCastRandom(Agent agent, Hero hero, IReadOnlyList<ColorSchool> colors)
        {
            ColorSchool school = colors[_rng.Next(colors.Count)];
            switch (school)
            {
                case ColorSchool.Red:
                    CastWithGlow(agent, hero, ColorSchool.Red, "Crush", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 12f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.4f) continue;
                            a.Health = Math.Max(0f, a.Health - 60f);
                            if (a.Health <= 0f) SpellEffects.KillAgent(a);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                        }
                    });
                    ApplyRedA1(agent); ApplyRedA2(agent);
                    break;
                case ColorSchool.Orange:
                    CastWithGlow(agent, hero, ColorSchool.Orange, "Encourage", () =>
                    {
                        foreach (Agent a in AlliesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 20f).ToList())
                            try { a.SetMorale(Math.Min(a.GetMorale() + 20f, 100f)); } catch { }
                    });
                    break;
                case ColorSchool.Green when CanUseGreen(agent):
                    CastWithGlow(agent, hero, ColorSchool.Green, "Aid", () =>
                    {
                        foreach (Agent a in AlliesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 12f).ToList())
                        {
                            a.Health = Math.Min(a.Health + 20f, a.HealthLimit);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Green, 1.5f);
                        }
                    });
                    ApplyGreenD2(agent);
                    break;
                case ColorSchool.Blue when CanUseBlue(agent):
                    CastWithGlow(agent, hero, ColorSchool.Blue, "Stun", () =>
                    {
                        foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 30f).ToList())
                            try { a.Health = Math.Max(1f, a.Health - 15f); SpellEffects.BeginAgentGlow(a, ColorSchool.Blue, 1.5f); } catch { }
                    });
                    break;
                case ColorSchool.Yellow:
                    SpellEffects.IssueBattleCommand(agent, SpellEffects.BattleCommandKind.Halt,
                        "{0} formation{1} halted.", ColorSchool.Yellow);
                    SetCooldown(hero);
                    break;
                case ColorSchool.Purple when CanUsePurple(agent):
                    CastWithGlow(agent, hero, ColorSchool.Purple, "Severe Life", () =>
                    {
                        var targets = EnemiesOf(agent).Where(a => !a.IsHero).ToList();
                        if (targets.Count > 0) SpellEffects.KillAgent(targets[_rng.Next(targets.Count)]);
                    });
                    ApplyPurpleF2(agent);
                    break;
            }
        }

        // ── Limitation checks ─────────────────────────────────────────────────
        private static bool CanUseBlue(Agent agent)  => agent?.MountAgent == null;
        private static bool CanUseGreen(Agent agent)
        {
            if (agent == null) return false;
            try { return agent.WieldedWeapon.IsEmpty || agent.WieldedWeapon.CurrentUsageItem?.IsShield == true; }
            catch { return true; }
        }
        private static bool CanUsePurple(Agent agent)
        {
            if (agent == null || Mission.Current == null) return false;
            return AlliesOf(agent).Any(a => a.Position.Distance(agent.Position) <= 50f);
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
        private static void ApplyGreenD2(Agent agent)
        {
            if (agent == null) return;
            var freeze = ActionIndexCache.Create("act_stand_1");
            int idx = agent.Index;
            ActiveEffectManager.Add(new ActiveEffect
            {
                Name = $"_steady_npc_{idx}", Duration = 3f, IsMissionEffect = true,
                OnTick = _ =>
                {
                    Agent a = Mission.Current?.Agents.FirstOrDefault(x => x.Index == idx);
                    if (a != null && a.IsActive()) try { a.SetActionChannel(0, freeze, true); } catch { }
                }
            });
        }
        private static void ApplyPurpleF2(Agent agent)
        {
            if (agent == null || Mission.Current == null) return;
            Agent ally = AlliesOf(agent).OrderBy(a => _rng.Next()).FirstOrDefault();
            if (ally == null) return;
            ally.Health = Math.Max(0f, ally.Health - 60f);
            if (ally.Health <= 0f) SpellEffects.KillAgent(ally);
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

            // Play charge animation on NPC caster
            string[] candidates = { "act_yield_hard", "act_pickup_boulder_begin",
                                     "act_struck_from_back_medium_left_staff" };
            foreach (string anim in candidates)
            {
                try
                {
                    var cache = ActionIndexCache.Create(anim);
                    if (cache.Index >= 0) { agent.SetActionChannel(0, cache, false); break; }
                }
                catch { }
            }

            InformationManager.DisplayMessage(new InformationMessage(
                $"{agent.Name} channels {spellName} ({ColorSchoolData.Info[school].Name}).",
                ColorSchoolData.GetMessageColor(school)));
        }

        private static void SetCooldown(Hero hero) =>
            _cooldowns[hero.StringId] = CastInterval;
    }
}
