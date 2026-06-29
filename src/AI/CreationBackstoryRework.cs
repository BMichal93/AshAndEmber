// =============================================================================
// ASH AND EMBER — AI/CreationBackstoryRework.cs
// Reworks specific Sandbox character-creation backstory options for the
// Templar (vlandia), Tribal (khuzait), and Ashen (sturgia) cultures.
//
//   Most changes are thematic renames; the bonuses are left untouched.
//   Two options change mechanically:
//     • Khuzait "A noyan's kinsfolk" → "Apostles of the God-King":
//         the Polearm skill grant is replaced by a random Dark Gift.
//     • Vlandia "A baron's groom"   → "A Lord Templar's squire":
//         the Charm skill grant is replaced by +3 Grace and +1 Honour.
//
//   Ashen (Sturgia) characters gain a unique "I don't remember my past" option
//   on every backstory stage (parent, childhood, education, youth, adulthood)
//   plus an "I don't know how old I am" age option (equivalent to age 40).
//   Each forgotten-past option grants +1 attribute and +2 focus to a
//   thematically fitting skill — no skill level, since the character has no
//   memory of training for anything in particular.
//
// The vanilla backstory options live in the engine's generic
// CharacterCreationCampaignBehavior. We register as an
// ICharacterCreationContentHandler and rewrite the already-built narrative
// menus in AfterInitializeContent (the option's display text and, for the two
// reworked options, its skill-effect getter, are private/readonly — set by
// reflection, matching the reflection-light style of TempleCultureCardFixer).
//
// Special boons (Dark Gift, Grace, age override) cannot be granted during
// character creation: the engine runs OnCharacterCreationFinalize *before* the
// OnCharacterCreationIsOver event, and our own new-game reset
// (CampaignBehavior.OnNewGameCreated → MageKnowledge.ResetForNewGame) fires on
// that event and would wipe them. So we only *record* the player's final pick
// at finalize, and apply the boon from OnNewGameCreated, after the reset has
// run (see ApplyPendingBoons).
// =============================================================================

using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    internal sealed class CreationBackstoryRework : CampaignBehaviorBase, ICharacterCreationContentHandler
    {
        // Engine-defined option ids for the two mechanically-changed options.
        private const string KhuzaitApostleOptionId = "khuzait_retainer_option";
        private const string VlandiaSquireOptionId  = "youth_groom_option";

        // Option ids injected for the Ashen (Sturgia) forgotten-past branch.
        private const string AshenForgottenFamilyId    = "ashen_forgotten_family";
        private const string AshenForgottenChildhoodId = "ashen_forgotten_childhood";
        private const string AshenForgottenEducationId = "ashen_forgotten_education";
        private const string AshenForgottenYouthId     = "ashen_forgotten_youth";
        private const string AshenForgottenAdulthoodId = "ashen_forgotten_adulthood";
        private const string AshenForgottenAgeId       = "ashen_forgotten_age";

        // Option ids injected for the basic Strugian (warrior) branch.
        private const string SturgianWarriorFamilyId    = "strugian_warrior_family";
        private const string SturgianWarriorChildhoodId = "strugian_warrior_childhood";
        private const string SturgianWarriorEducationId = "strugian_warrior_education";
        private const string SturgianWarriorYouthId     = "strugian_warrior_youth";
        private const string SturgianWarriorAdulthoodId = "strugian_warrior_adulthood";

        // Pending boons recorded at finalize, applied after the new-game reset.
        private static bool _pendingApostleDarkGift;
        private static bool _pendingSquireBoon;
        private static bool _pendingForgottenAge;

        // The generic option grants (read from the live content so we stay in
        // sync with the engine's defaults rather than hard-coding 1/10/1).
        private static int _focus = 1, _skill = 10, _attr = 1;

        private const BindingFlags FPriv = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags FPub  = BindingFlags.Instance | BindingFlags.Public;

        private static readonly FieldInfo TextField =
            typeof(NarrativeMenuOption).GetField("Text", FPub);
        private static readonly FieldInfo DescField =
            typeof(NarrativeMenuOption).GetField("DescriptionText", FPub);
        private static readonly FieldInfo ArgsGetterField =
            typeof(NarrativeMenuOption).GetField("_getNarrativeMenuOptionArgs", FPriv);

        // ── CampaignBehaviorBase ─────────────────────────────────────────────

        public override void RegisterEvents()
        {
            CampaignEvents.OnCharacterCreationInitializedEvent.AddNonSerializedListener(this, OnInitialized);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnInitialized(CharacterCreationManager manager)
        {
            // Clear any stale pending state from an abandoned creation this session.
            _pendingApostleDarkGift = false;
            _pendingSquireBoon      = false;
            _pendingForgottenAge    = false;
            try { manager.RegisterCharacterCreationContentHandler(this, 1000); } catch { }
        }

        // ── ICharacterCreationContentHandler ─────────────────────────────────

        void ICharacterCreationContentHandler.InitializeContent(CharacterCreationManager m) { }

        void ICharacterCreationContentHandler.AfterInitializeContent(CharacterCreationManager m)
        {
            try
            {
                var content = m.CharacterCreationContent;
                if (content != null)
                {
                    _focus = content.FocusToAdd;
                    _skill = content.SkillLevelToAdd;
                    _attr  = content.AttributeLevelToAdd;
                }
                RewriteMenus(m);
                InjectAshenForgottenPastOptions(m);
                InjectBasicSturgianOptions(m);
            }
            catch { }
        }

        void ICharacterCreationContentHandler.OnStageCompleted(CharacterCreationStageBase stage) { }

        void ICharacterCreationContentHandler.OnCharacterCreationFinalize(CharacterCreationManager m)
        {
            // Record the final pick only; the grant itself happens post-reset.
            try
            {
                foreach (var pair in m.SelectedOptions)
                {
                    string id = pair.Value?.StringId;
                    if      (id == KhuzaitApostleOptionId)  _pendingApostleDarkGift = true;
                    else if (id == VlandiaSquireOptionId)   _pendingSquireBoon      = true;
                    else if (id == AshenForgottenAgeId)     _pendingForgottenAge    = true;
                }
            }
            catch { }
        }

        // ── Ashen forgotten-past injection ───────────────────────────────────

        // Adds one "I don't remember" option to each narrative stage for Sturgia
        // (Ashen) characters. Each grants +1 to a thematic attribute and +2 focus
        // to a matching skill, but no skill level — the character has no memory of
        // how they learned anything.
        private static readonly string[] SturgiaSpecificOptionIds =
        {
            "sturgia_companion_option",
            "sturgia_trader_option",
            "sturgia_farmer_option",
            "sturgia_artisan_option",
        };

        private static void InjectAshenForgottenPastOptions(CharacterCreationManager m)
        {
            // Strip the Sturgia-specific parent options — the Ashen have no known
            // lineage; only the shared cross-culture options (and the forgotten-past
            // option below) will remain available for this culture.
            try
            {
                var parentMenu = m.GetNarrativeMenuWithId("narrative_parent_menu");
                parentMenu?.CharacterCreationMenuOptions
                    .RemoveAll(o => o != null && System.Array.IndexOf(SturgiaSpecificOptionIds, o.StringId) >= 0);
            }
            catch { }

            AddAshenForgotten(m, "narrative_parent_menu", AshenForgottenFamilyId,
                "came from nowhere.",
                "You carry no name, no family crest, no memory of the faces that shaped you. There are "
                + "flashes — cold mud, a fire going out, a hand that let go of yours — but nothing that "
                + "would count as memory. You woke, at some point, knowing how to ride and how to bleed. "
                + "Everything before that is ash.",
                DefaultCharacterAttributes.Vigor, DefaultSkills.TwoHanded);

            AddAshenForgotten(m, "narrative_childhood_menu", AshenForgottenChildhoodId,
                "have no memory of those years.",
                "There may have been games, or lessons, or a village that no longer stands. You cannot say. "
                + "The years before you could truly think for yourself are a sealed room — and something "
                + "behind the door does not want it opened.",
                DefaultCharacterAttributes.Endurance, DefaultSkills.Athletics);

            AddAshenForgotten(m, "narrative_education_menu", AshenForgottenEducationId,
                "cannot say what you studied.",
                "You know things you were never taught — at least, you cannot remember being taught them. "
                + "They are simply there when you reach for them: the shape of terrain, the arithmetic of "
                + "war, the weight of a silence in a room full of men with weapons.",
                DefaultCharacterAttributes.Intelligence, DefaultSkills.Tactics);

            AddAshenForgotten(m, "narrative_youth_menu", AshenForgottenYouthId,
                "have no account of your younger years.",
                "You did not serve. You did not follow. You were somewhere else, in ways you cannot explain "
                + "to yourself, let alone to others. The scars you carry from those years have no stories "
                + "attached to them. They are simply there.",
                DefaultCharacterAttributes.Cunning, DefaultSkills.Scouting);

            AddAshenForgotten(m, "narrative_adulthood_menu", AshenForgottenAdulthoodId,
                "remember nothing you would speak of.",
                "You can lead. You do not know when you learned to. Men follow your silences as readily as "
                + "your orders, as if they sense something in you that you yourself cannot name — something "
                + "old, and patient, and not entirely yours.",
                DefaultCharacterAttributes.Social, DefaultSkills.Leadership);

            AddAshenForgottenAge(m);
        }

        // Adds a single forgotten-past option to the named menu, visible only for
        // Sturgia (Ashen) characters. Grants +1 to the given attribute and +2 focus
        // to the given skill (no skill level).
        private static void AddAshenForgotten(CharacterCreationManager m, string menuId,
            string optionId, string label, string description,
            CharacterAttribute attribute, SkillObject skill)
        {
            var menu = m.GetNarrativeMenuWithId(menuId);
            if (menu == null) return;
            try
            {
                var option = new NarrativeMenuOption(
                    optionId,
                    new TextObject(label),
                    new TextObject(description),
                    new GetNarrativeMenuOptionArgsDelegate((NarrativeMenuOptionArgs args) =>
                    {
                        args.SetAffectedSkills(new[] { skill });
                        args.SetFocusToSkills(2);
                        args.SetLevelToAttribute(attribute, 1);
                    }),
                    new NarrativeMenuOptionOnConditionDelegate((CharacterCreationManager mgr) =>
                        mgr?.CharacterCreationContent?.SelectedCulture?.StringId == "sturgia"),
                    null,
                    null);
                menu.AddNarrativeMenuOption(option);
            }
            catch { }
        }

        // Adds the "I don't know how old I am" age option for Ashen characters.
        // No attribute or focus grant — the age itself (40) is the consequence,
        // applied post-reset in ApplyPendingBoons to override the sandbox's default.
        private static void AddAshenForgottenAge(CharacterCreationManager m)
        {
            var menu = m.GetNarrativeMenuWithId("narrative_age_selection_menu");
            if (menu == null) return;
            try
            {
                var option = new NarrativeMenuOption(
                    AshenForgottenAgeId,
                    new TextObject("I don't know how old I am."),
                    new TextObject(
                        "The years do not stack for you the way they do for others. You could be thirty, "
                        + "or fifty, or something in between that the calendar cannot account for. You feel "
                        + "old in ways the body does not show — and young in others, as though part of you "
                        + "has not aged at all, or aged too fast, or never started."),
                    new GetNarrativeMenuOptionArgsDelegate((NarrativeMenuOptionArgs args) => { }),
                    new NarrativeMenuOptionOnConditionDelegate((CharacterCreationManager mgr) =>
                        mgr?.CharacterCreationContent?.SelectedCulture?.StringId == "sturgia"),
                    null,
                    null);
                menu.AddNarrativeMenuOption(option);
            }
            catch { }
        }

        // ── Basic Strugian injection ─────────────────────────────────────────

        // Adds a "born a Strugian" option to each narrative stage for Sturgia
        // characters who remember exactly who they are. Each grants the standard
        // engine attribute + focus + skill level bonuses for a warrior background.
        private static void InjectBasicSturgianOptions(CharacterCreationManager m)
        {
            AddBasicSturgian(m, "narrative_parent_menu", SturgianWarriorFamilyId,
                "were born to a warlord's household.",
                "Your family served a Strugian warlord — not among the lowest levied spearmen, "
                + "but in the inner ring: the household guards, the sword-companions, the men who "
                + "ate at the same table and slept close enough to hear the boyar breathe. Violence "
                + "was spoken about the way other families spoke of trade. You learned to hold a "
                + "blade before you learned to write.",
                DefaultCharacterAttributes.Vigor, DefaultSkills.TwoHanded);

            AddBasicSturgian(m, "narrative_childhood_menu", SturgianWarriorChildhoodId,
                "grew up fighting.",
                "Your childhood was long on bruises and short on softness. The boys of the "
                + "settlement settled everything with fists, staves, or the occasional blade — "
                + "overseen, loosely, by men who thought toughness the only lesson worth teaching. "
                + "You won more often than you lost. The losses taught you more.",
                DefaultCharacterAttributes.Endurance, DefaultSkills.Athletics);

            AddBasicSturgian(m, "narrative_education_menu", SturgianWarriorEducationId,
                "were schooled in the arts of the warband.",
                "Your education was tactical — not books and letters, but the reading of ground, "
                + "the pacing of supply, the discipline of men who would rather eat and sleep than "
                + "hold a line. A veteran drummed it into you with the patient weariness of someone "
                + "who had learned it the hard way and intended you to learn it the easier one.",
                DefaultCharacterAttributes.Intelligence, DefaultSkills.Tactics);

            AddBasicSturgian(m, "narrative_youth_menu", SturgianWarriorYouthId,
                "rode with a raiding party.",
                "Before you were anyone's sworn sword, you rode with the summer parties — light, "
                + "fast, unannounced. You learned the difference between a scout and a spy, how to "
                + "find what was worth taking and what was worth leaving, and the arithmetic of "
                + "coming home with fewer horses than you left with.",
                DefaultCharacterAttributes.Cunning, DefaultSkills.Scouting);

            AddBasicSturgian(m, "narrative_adulthood_menu", SturgianWarriorAdulthoodId,
                "led men who followed because you were worth following.",
                "You did not inherit command — you accumulated it. Men watched how you handled the "
                + "hard moments and decided, one by one, that they would rather be behind you than "
                + "in front of you. You learned that authority is not a rank but a reputation that "
                + "requires constant upkeep.",
                DefaultCharacterAttributes.Social, DefaultSkills.Leadership);
        }

        // Adds a single basic Strugian option to the named menu, visible only for
        // Sturgia characters. Grants standard bonuses: +1 attribute, +focus, +skill level.
        private static void AddBasicSturgian(CharacterCreationManager m, string menuId,
            string optionId, string label, string description,
            CharacterAttribute attribute, SkillObject skill)
        {
            var menu = m.GetNarrativeMenuWithId(menuId);
            if (menu == null) return;
            try
            {
                var option = new NarrativeMenuOption(
                    optionId,
                    new TextObject(label),
                    new TextObject(description),
                    new GetNarrativeMenuOptionArgsDelegate((NarrativeMenuOptionArgs args) =>
                    {
                        args.SetAffectedSkills(new[] { skill });
                        args.SetFocusToSkills(_focus);
                        args.SetLevelToSkills(_skill);
                        args.SetLevelToAttribute(attribute, _attr);
                    }),
                    new NarrativeMenuOptionOnConditionDelegate((CharacterCreationManager mgr) =>
                        mgr?.CharacterCreationContent?.SelectedCulture?.StringId == "sturgia"),
                    null,
                    null);
                menu.AddNarrativeMenuOption(option);
            }
            catch { }
        }

        // ── Menu rewrites ────────────────────────────────────────────────────

        private static void RewriteMenus(CharacterCreationManager m)
        {
            // Each Edit updates the option's label and its lore description. The
            // skill/attribute/trait effects shown in the dedicated effect panel come
            // from the option's args getter; for the two reworked options we replace
            // that getter so the panel reflects the new grant. Effects that the panel
            // cannot express (Grace, a Dark Gift) are noted in the description and
            // granted post-reset in ApplyPendingBoons.

            // ── Stage 1 — Family ─────────────────────────────────────────────
            // Khuzait: A noyan's kinsfolk → Apostles of the God-King (Dark Gift for Polearm).
            Edit(m, "narrative_parent_menu", KhuzaitApostleOptionId,
                "Apostles of the God-King",
                "Your family were sworn to the God-King's inner rites — the marked few who carry his fire in "
                + "miniature and speak his word where his horsemen have not yet ridden. You were raised among "
                + "them, and the dark took its measure of you before you were old enough to refuse it.\n\n"
                + "(You will begin bearing one random Dark Gift.)",
                ApostleArgs);
            // Vlandia: A baron's retainers → Lower-rank Templars (same bonus).
            Edit(m, "narrative_parent_menu", "vlandia_retainer_option",
                "Lower-rank Templars",
                "Your father served in the lower ranks of the Templar order — a sworn man-at-arms who rode "
                + "under the banner and answered to the Lord Templars above him. He kept his oaths, drilled the "
                + "village levy, and fought as an armoured knight when the Order called.");
            // Vlandia: Mercenaries → Footmen (same bonus).
            Edit(m, "narrative_parent_menu", "vlandia_mercenary_option",
                "Footmen",
                "Your family marched as common footmen in the Templar host — spear and crossbow, paid in coin "
                + "and plunder, the rank and file who held the line while the knights broke it. Your mother "
                + "followed the column from siege to siege, and you grew up in the wake of its campaigns.");

            // ── Stage 3 — Adolescence ────────────────────────────────────────
            // Khuzait (urban): studied with your private tutor → attended the religious school.
            Edit(m, "narrative_education_menu", "education_tutor_option",
                "attended the religious school.",
                "While other children worked the herds, you were sent to the God-King's schoolmen, who drilled "
                + "scripture, numbers, and the disciplines of the faithful into you by rote and by rod.");
            // Vlandia (urban): hung out with the gangs → denounced enemies of the faith.
            Edit(m, "narrative_education_menu", "education_ganger_option",
                "denounced enemies of the faith with your friends.",
                "You and your fellows made a sport of rooting out heresy in the back streets — naming the "
                + "lapsed, the foreign, and the merely unlucky to the Order's wardens. Some of it was zeal. "
                + "Some of it was knowing whom to threaten, and when.");

            // ── Stage 4 — Youth ──────────────────────────────────────────────
            // Khuzait: a chieftain's servant → the God-King's bloodrider's servant.
            Edit(m, "narrative_youth_menu", "youth_servant_first_option",
                "were the God-King's bloodrider's servant.",
                "You waited on one of the God-King's bloodriders — his chosen lancers — fetching and scouting "
                + "and listening at the edges of councils you were never meant to hear.");
            // Khuzait: an envoy's entourage → the Tribe's emissary.
            Edit(m, "narrative_youth_menu", "youth_envoys_guard_first_option",
                "served as the Tribe's emissary.",
                "You rode ahead of the horde, carrying the God-King's terms to cities that still believed they "
                + "could bargain. You learned to read a room full of frightened men — and to be gone before the "
                + "knives came out.");
            // Vlandia: a baron's groom → a Lord Templar's squire (Grace + Honour for Charm).
            Edit(m, "narrative_youth_menu", VlandiaSquireOptionId,
                "served as a Lord Templar's squire.",
                "You served a Lord Templar as his squire — tending his arms and his horse, kneeling through the "
                + "long vigils, and learning that the Order's strength is bought with discipline and faith.\n\n"
                + "(You will begin with 3 Grace.)",
                SquireArgs);
        }

        private static NarrativeMenuOption Find(CharacterCreationManager m, string menuId, string optionId)
        {
            var menu = m.GetNarrativeMenuWithId(menuId);
            if (menu == null) return null;
            foreach (var o in menu.CharacterCreationMenuOptions)
                if (o != null && o.StringId == optionId) return o;
            return null;
        }

        // Updates an option's label and description; optionally replaces its skill-effect
        // getter (passed for the two reworked options, null for rename-only ones).
        private static void Edit(CharacterCreationManager m, string menuId, string optionId,
            string newText, string newDesc, GetNarrativeMenuOptionArgsDelegate argsGetter = null)
        {
            var o = Find(m, menuId, optionId);
            if (o == null) return;
            try { TextField?.SetValue(o, new TextObject(newText)); } catch { }
            try { DescField?.SetValue(o, new TextObject(newDesc)); } catch { }
            if (argsGetter != null)
                try { ArgsGetterField?.SetValue(o, argsGetter); } catch { }
        }

        // Riding only (the dropped Polearm is replaced by a random Dark Gift, granted
        // post-reset in ApplyPendingBoons); Endurance attribute unchanged.
        private static void ApostleArgs(NarrativeMenuOptionArgs args)
        {
            args.SetAffectedSkills(new SkillObject[] { DefaultSkills.Riding });
            args.SetFocusToSkills(_focus);
            args.SetLevelToSkills(_skill);
            args.SetLevelToAttribute(DefaultCharacterAttributes.Endurance, _attr);
        }

        // Tactics + a point of Honour (both shown in the dedicated effect panel) in place
        // of the dropped Charm; the +3 Grace cannot be expressed there and is granted
        // post-reset in ApplyPendingBoons. Social attribute unchanged.
        private static void SquireArgs(NarrativeMenuOptionArgs args)
        {
            args.SetAffectedSkills(new SkillObject[] { DefaultSkills.Tactics });
            args.SetFocusToSkills(_focus);
            args.SetLevelToSkills(_skill);
            args.SetLevelToAttribute(DefaultCharacterAttributes.Social, _attr);
            args.SetAffectedTraits(new TraitObject[] { DefaultTraits.Honor });
            args.SetLevelToTraits(1);
        }

        // ── Boon application ─────────────────────────────────────────────────

        // Called from CampaignBehavior.OnNewGameCreated, AFTER the new-game static
        // reset, so the grants survive into the campaign. Only the effects that the
        // creation effect panel cannot express live here — the Honour point is applied
        // through the squire's args (and so survives on the hero already).
        public static void ApplyPendingBoons()
        {
            if (_pendingApostleDarkGift)
            {
                _pendingApostleDarkGift = false;
                try { DarkGiftSystem.GrantRandomGift(); } catch { }
            }

            if (_pendingSquireBoon)
            {
                _pendingSquireBoon = false;
                try { MiracleInventory.AddGrace(3); } catch { }
            }

            // Override whatever age the sandbox set — the Ashen character does not
            // know how old they are; we place them at 40, the "middle-age" slot.
            if (_pendingForgottenAge)
            {
                _pendingForgottenAge = false;
                try { Hero.MainHero?.SetBirthDay(CampaignTime.YearsFromNow(-40f)); } catch { }
            }
        }
    }
}
