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
using System.Collections.Generic;
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

        // Pending boons recorded at finalize, applied after the new-game reset.
        private static bool _pendingApostleDarkGift;
        private static bool _pendingSquireBoon;

        // The generic option grants (read from the live content so we stay in
        // sync with the engine's defaults rather than hard-coding 1/10/1).
        private static int _focus = 1, _skill = 10, _attr = 1;

        // The engine's ApplyFinalEffects calls Args.AffectedTraits.ToList() with no
        // null guard, so the reworked Apostle option (which grants no trait) must still
        // hand it a non-null list.
        private static readonly TraitObject[] _noTraits = new TraitObject[0];

        // Stored at AfterInitializeContent so OnStageCompleted can read the LIVE
        // selected culture (unknown when the menus are first built, before selection).
        private static CharacterCreationManager _manager;

        // Culture-flavoured renames of SHARED (cross-culture) narrative options. These
        // options appear for every culture, so their flavour must only show for the
        // matching culture — otherwise an Empire youth reads "the Tribe's emissary".
        // We capture each option's vanilla text/desc/args, then on every stage
        // transition restore vanilla or apply the flavour depending on the live pick.
        private sealed class GatedRename
        {
            public string MenuId, OptionId, Culture;
            public TextObject FlavText, FlavDesc, BaseText, BaseDesc;
            public GetNarrativeMenuOptionArgsDelegate FlavArgs, BaseArgs;
        }
        private static readonly List<GatedRename> _gated = new List<GatedRename>();

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
            _gated.Clear();
            _manager = null;
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
                _manager = m;
                RewriteMenus(m);
                ApplyGatedRenames();   // set initial state for the current (or no) selection
            }
            catch { }
        }

        // Culture is chosen partway through creation; re-evaluate the shared-option
        // flavour each time a stage completes so it tracks the player's live pick.
        void ICharacterCreationContentHandler.OnStageCompleted(CharacterCreationStageBase stage)
        {
            try { ApplyGatedRenames(); } catch { }
        }

        void ICharacterCreationContentHandler.OnCharacterCreationFinalize(CharacterCreationManager m)
        {
            // Record the final pick only; the grant itself happens post-reset.
            // The Apostle/Squire options are SHARED across cultures (their flavour is
            // culture-gated), so their boons must be gated by culture too — otherwise an
            // Empire character who picks the "groom" option would receive Templar Grace.
            try
            {
                string sel = m?.CharacterCreationContent?.SelectedCulture?.StringId;
                foreach (var pair in m.SelectedOptions)
                {
                    string id = pair.Value?.StringId;
                    if      (id == KhuzaitApostleOptionId && sel == "khuzait") _pendingApostleDarkGift = true;
                    else if (id == VlandiaSquireOptionId  && sel == "vlandia") _pendingSquireBoon      = true;
                }
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
            // These education/youth options are SHARED across cultures, so the flavour
            // is culture-gated (see RegisterGatedRename) — only the matching culture
            // sees it; everyone else keeps the vanilla wording.
            // Khuzait (urban): studied with your private tutor → attended the religious school.
            RegisterGatedRename(m, "narrative_education_menu", "education_tutor_option", "khuzait",
                "attended the religious school.",
                "While other children worked the herds, you were sent to the God-King's schoolmen, who drilled "
                + "scripture, numbers, and the disciplines of the faithful into you by rote and by rod.");
            // Vlandia (urban): hung out with the gangs → denounced enemies of the faith.
            RegisterGatedRename(m, "narrative_education_menu", "education_ganger_option", "vlandia",
                "denounced enemies of the faith with your friends.",
                "You and your fellows made a sport of rooting out heresy in the back streets — naming the "
                + "lapsed, the foreign, and the merely unlucky to the Order's wardens. Some of it was zeal. "
                + "Some of it was knowing whom to threaten, and when.");

            // ── Stage 4 — Youth ──────────────────────────────────────────────
            // Khuzait: a chieftain's servant → the God-King's bloodrider's servant.
            RegisterGatedRename(m, "narrative_youth_menu", "youth_servant_first_option", "khuzait",
                "were the God-King's bloodrider's servant.",
                "You waited on one of the God-King's bloodriders — his chosen lancers — fetching and scouting "
                + "and listening at the edges of councils you were never meant to hear.");
            // Khuzait: an envoy's entourage → the Tribe's emissary.
            RegisterGatedRename(m, "narrative_youth_menu", "youth_envoys_guard_first_option", "khuzait",
                "served as the Tribe's emissary.",
                "You rode ahead of the horde, carrying the God-King's terms to cities that still believed they "
                + "could bargain. You learned to read a room full of frightened men — and to be gone before the "
                + "knives came out.");
            // Vlandia: a baron's groom → a Lord Templar's squire (Grace + Honour for Charm).
            RegisterGatedRename(m, "narrative_youth_menu", VlandiaSquireOptionId, "vlandia",
                "served as a Lord Templar's squire.",
                "You served a Lord Templar as his squire — tending his arms and his horse, kneeling through the "
                + "long vigils, and learning that the Order's strength is bought with discipline and faith.\n\n"
                + "(You will begin with 3 Grace.)",
                new GetNarrativeMenuOptionArgsDelegate(SquireArgs));
        }

        // Registers a culture-gated rename of a shared narrative option: captures the
        // vanilla text/description/args first, then ApplyGatedRenames swaps in the
        // flavour only while the matching culture is selected.
        private static void RegisterGatedRename(CharacterCreationManager m, string menuId, string optionId,
            string culture, string flavText, string flavDesc, GetNarrativeMenuOptionArgsDelegate flavArgs = null)
        {
            var o = Find(m, menuId, optionId);
            if (o == null) return;
            try
            {
                _gated.Add(new GatedRename
                {
                    MenuId = menuId, OptionId = optionId, Culture = culture,
                    FlavText = new TextObject(flavText), FlavDesc = new TextObject(flavDesc),
                    BaseText = TextField?.GetValue(o) as TextObject,
                    BaseDesc = DescField?.GetValue(o) as TextObject,
                    FlavArgs = flavArgs,
                    BaseArgs = ArgsGetterField?.GetValue(o) as GetNarrativeMenuOptionArgsDelegate,
                });
            }
            catch { }
        }

        // Applies each gated rename's flavour when its culture is the live selection,
        // and restores the vanilla wording/args otherwise.
        private static void ApplyGatedRenames()
        {
            if (_manager == null || _gated.Count == 0) return;
            string sel = null;
            try { sel = _manager.CharacterCreationContent?.SelectedCulture?.StringId; } catch { }
            foreach (var gr in _gated)
            {
                var o = Find(_manager, gr.MenuId, gr.OptionId);
                if (o == null) continue;
                bool match = sel == gr.Culture;
                try { TextField?.SetValue(o, match ? gr.FlavText : gr.BaseText); } catch { }
                try { DescField?.SetValue(o, match ? gr.FlavDesc : gr.BaseDesc); } catch { }
                if (gr.FlavArgs != null)
                    try { ArgsGetterField?.SetValue(o, match ? gr.FlavArgs : gr.BaseArgs); } catch { }
            }
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
            args.SetAffectedTraits(_noTraits);   // non-null or ApplyFinalEffects throws
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
        }
    }
}
