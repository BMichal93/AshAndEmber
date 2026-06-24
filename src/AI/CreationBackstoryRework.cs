// =============================================================================
// ASH AND EMBER — AI/CreationBackstoryRework.cs
// Reworks specific Sandbox character-creation backstory options for the
// Templar (vlandia) and Tribal (khuzait) cultures.
//
//   Most changes are thematic renames; the bonuses are left untouched.
//   Two options change mechanically:
//     • Khuzait "A noyan's kinsfolk" → "Apostles of the God-King":
//         the Polearm skill grant is replaced by a random Dark Gift.
//     • Vlandia "A baron's groom"   → "A Lord Templar's squire":
//         the Charm skill grant is replaced by +3 Grace and +1 Honour.
//
// The vanilla backstory options live in the engine's generic
// CharacterCreationCampaignBehavior. We register as an
// ICharacterCreationContentHandler and rewrite the already-built narrative
// menus in AfterInitializeContent (the option's display text and, for the two
// reworked options, its skill-effect getter, are private/readonly — set by
// reflection, matching the reflection-light style of TempleCultureCardFixer).
//
// The two special boons cannot be granted during character creation: the
// engine runs OnCharacterCreationFinalize *before* the OnCharacterCreationIsOver
// event, and our own new-game reset (CampaignBehavior.OnNewGameCreated →
// MageKnowledge.ResetForNewGame) fires on that event and would wipe them. So we
// only *record* the player's final pick at finalize, and apply the boon from
// OnNewGameCreated, after the reset has run (see ApplyPendingBoons).
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

        // Pending boons recorded at finalize, applied after the new-game reset.
        private static bool _pendingApostleDarkGift;
        private static bool _pendingSquireBoon;

        // The generic option grants (read from the live content so we stay in
        // sync with the engine's defaults rather than hard-coding 1/10/1).
        private static int _focus = 1, _skill = 10, _attr = 1;

        private const BindingFlags FPriv = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags FPub  = BindingFlags.Instance | BindingFlags.Public;

        private static readonly FieldInfo TextField =
            typeof(NarrativeMenuOption).GetField("Text", FPub);
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
                    if (id == KhuzaitApostleOptionId)     _pendingApostleDarkGift = true;
                    else if (id == VlandiaSquireOptionId) _pendingSquireBoon      = true;
                }
            }
            catch { }
        }

        // ── Menu rewrites ────────────────────────────────────────────────────

        private static void RewriteMenus(CharacterCreationManager m)
        {
            // Stage 1 — Family.
            // Khuzait: A noyan's kinsfolk → Apostles of the God-King (Dark Gift for Polearm).
            ModifyOption(m, "narrative_parent_menu", KhuzaitApostleOptionId,
                "Apostles of the God-King", ApostleArgs);
            // Vlandia: A baron's retainers → Lower-rank Templars (same bonus).
            Rename(m, "narrative_parent_menu", "vlandia_retainer_option", "Lower-rank Templars");
            // Vlandia: Mercenaries → Footmen (same bonus).
            Rename(m, "narrative_parent_menu", "vlandia_mercenary_option", "Footmen");

            // Stage 3 — Adolescence.
            // Khuzait (urban): studied with your private tutor → attended the religious school.
            Rename(m, "narrative_education_menu", "education_tutor_option",
                "attended the religious school.");
            // Vlandia (urban): hung out with the gangs → denounced enemies of the faith.
            Rename(m, "narrative_education_menu", "education_ganger_option",
                "denounced enemies of the faith with your friends.");

            // Stage 4 — Youth.
            // Khuzait: a chieftain's servant → the God-King's bloodrider's servant.
            Rename(m, "narrative_youth_menu", "youth_servant_first_option",
                "were the God-King's bloodrider's servant.");
            // Khuzait: an envoy's entourage → the Tribe's emissary.
            Rename(m, "narrative_youth_menu", "youth_envoys_guard_first_option",
                "served as the Tribe's emissary.");
            // Vlandia: a baron's groom → a Lord Templar's squire (Grace + Honour for Charm).
            ModifyOption(m, "narrative_youth_menu", VlandiaSquireOptionId,
                "served as a Lord Templar's squire.", SquireArgs);
        }

        private static NarrativeMenuOption Find(CharacterCreationManager m, string menuId, string optionId)
        {
            var menu = m.GetNarrativeMenuWithId(menuId);
            if (menu == null) return null;
            foreach (var o in menu.CharacterCreationMenuOptions)
                if (o != null && o.StringId == optionId) return o;
            return null;
        }

        // Rename only — preserves the vanilla bonus and select/consequence logic.
        private static void Rename(CharacterCreationManager m, string menuId, string optionId, string newText)
        {
            var o = Find(m, menuId, optionId);
            if (o == null) return;
            try { TextField?.SetValue(o, new TextObject(newText)); } catch { }
        }

        // Rename and replace the skill-effect getter (used by the two reworks).
        private static void ModifyOption(CharacterCreationManager m, string menuId, string optionId,
            string newText, GetNarrativeMenuOptionArgsDelegate argsGetter)
        {
            var o = Find(m, menuId, optionId);
            if (o == null) return;
            try { TextField?.SetValue(o, new TextObject(newText)); } catch { }
            try { ArgsGetterField?.SetValue(o, argsGetter); } catch { }
        }

        // Riding only (the dropped Polearm is replaced by a random Dark Gift,
        // granted post-reset in ApplyPendingBoons); Endurance attribute unchanged.
        private static void ApostleArgs(NarrativeMenuOptionArgs args)
        {
            args.SetAffectedSkills(new SkillObject[] { DefaultSkills.Riding });
            args.SetFocusToSkills(_focus);
            args.SetLevelToSkills(_skill);
            args.SetLevelToAttribute(DefaultCharacterAttributes.Endurance, _attr);
        }

        // Tactics only (the dropped Charm is replaced by +3 Grace and +1 Honour,
        // granted post-reset in ApplyPendingBoons); Social attribute unchanged.
        private static void SquireArgs(NarrativeMenuOptionArgs args)
        {
            args.SetAffectedSkills(new SkillObject[] { DefaultSkills.Tactics });
            args.SetFocusToSkills(_focus);
            args.SetLevelToSkills(_skill);
            args.SetLevelToAttribute(DefaultCharacterAttributes.Social, _attr);
        }

        // ── Boon application ─────────────────────────────────────────────────

        // Called from CampaignBehavior.OnNewGameCreated, AFTER the new-game static
        // reset, so the grants survive into the campaign.
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
                try
                {
                    var hero = Hero.MainHero;
                    if (hero != null)
                    {
                        int cur = hero.GetTraitLevel(DefaultTraits.Honor);
                        hero.SetTraitLevel(DefaultTraits.Honor, Math.Min(cur + 1, 2));
                    }
                }
                catch { }
            }
        }
    }
}
