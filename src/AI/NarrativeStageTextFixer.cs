// =============================================================================
// ASH AND EMBER — AI/NarrativeStageTextFixer.cs
// Keeps the character-creation BACKSTORY option labels in sync with the mod's
// narrative rewrites (CreationBackstoryRework).
//
// Why this exists: CharacterCreationOptionVM caches each option's resolved
// label/description STRING when the stage view-model is built, and reads its
// NarrativeMenuOption again only on user interaction (hover/select). Our
// backstory rewrites — the plain renames landing after the VM is built, and
// the culture-gated flavour that flips whenever the culture pick changes —
// therefore show their vanilla text until the player happens to hover the
// option. The fix mirrors TempleCultureCardFixer: walk the live screen graph
// (bounded, reflection, TaleWorlds-only descent), find every option VM, and
// re-resolve its cached strings from the Option it holds. Setting the VM
// property raises its change notification, so the label corrects on screen.
//
// Unlike the culture-card fixer this never stops while the creation screen is
// up (the gated flavour can change with every culture re-pick); the walk is
// throttled and budget-capped, and any failure degrades to the vanilla text.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    internal static class NarrativeStageTextFixer
    {
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo OptionTextField =
            typeof(NarrativeMenuOption).GetField("Text", BindingFlags.Instance | BindingFlags.Public);
        private static readonly FieldInfo OptionDescField =
            typeof(NarrativeMenuOption).GetField("DescriptionText", BindingFlags.Instance | BindingFlags.Public);

        private static int _throttle;

        // Called from the pre-game application tick, alongside
        // TempleCultureCardFixer.TickTryFix. Cheap until the character-creation
        // screen is up; then sweeps a few times a second for the screen's lifetime.
        public static void TickTryFix()
        {
            try
            {
                object screen = null;
                try { screen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen; } catch { }
                if (screen == null) return;
                if (screen.GetType().Name.IndexOf("CharacterCreation", StringComparison.OrdinalIgnoreCase) < 0) return;

                if (_throttle-- > 0) return;
                _throttle = 12;

                int budget = 8000;
                var visited = new HashSet<object>(RefComparer.Instance);
                Walk(screen, visited, 0, ref budget);
            }
            catch { }
        }

        private static void Walk(object obj, HashSet<object> visited, int depth, ref int budget)
        {
            if (obj == null || depth > 12 || budget <= 0) return;
            Type type = obj.GetType();
            if (type.IsPrimitive || type.IsEnum || obj is string) return;
            if (!visited.Add(obj)) return;
            budget--;

            if (type.Name == "CharacterCreationOptionVM")
            {
                TrySyncOptionVM(obj, type);
                return;
            }

            if (obj is IEnumerable en)
            {
                try
                {
                    foreach (var item in en)
                    {
                        if (budget <= 0) break;
                        Walk(item, visited, depth + 1, ref budget);
                    }
                }
                catch { }
                return;
            }

            string ns = type.Namespace ?? "";
            if (!(ns.StartsWith("TaleWorlds") || ns.StartsWith("SandBox") || ns.StartsWith("StoryMode")))
                return;

            foreach (FieldInfo f in type.GetFields(F))
            {
                if (budget <= 0) break;
                Type ft = f.FieldType;
                if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string)) continue;
                object val;
                try { val = f.GetValue(obj); } catch { continue; }
                Walk(val, visited, depth + 1, ref budget);
            }
        }

        // Re-resolves the VM's cached label/description from the option it holds.
        // Only writes on a real change, so the UI's change notifications (and any
        // layout they trigger) fire solely when a rewrite actually landed.
        private static void TrySyncOptionVM(object vm, Type type)
        {
            try
            {
                object option = type.GetField("Option", BindingFlags.Instance | BindingFlags.Public)?.GetValue(vm);
                if (option == null) return;

                string text = (OptionTextField?.GetValue(option) as TextObject)?.ToString();
                string desc = (OptionDescField?.GetValue(option) as TextObject)?.ToString();

                SyncStringProp(vm, type, "ActionText",      text);
                SyncStringProp(vm, type, "DescriptionText", desc);
            }
            catch { }
        }

        private static void SyncStringProp(object vm, Type type, string prop, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            try
            {
                PropertyInfo p = type.GetProperty(prop, F);
                if (p == null) return;
                if (p.GetValue(vm) as string == value) return;
                p.SetValue(vm, value);
            }
            catch { }
        }

        // Reference-identity comparer (.NET Framework 4.7.2 has no built-in one).
        private sealed class RefComparer : IEqualityComparer<object>
        {
            public static readonly RefComparer Instance = new RefComparer();
            bool IEqualityComparer<object>.Equals(object a, object b) => ReferenceEquals(a, b);
            int  IEqualityComparer<object>.GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
