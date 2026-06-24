// =============================================================================
// ASH AND EMBER — AI/TempleCultureCardFixer.cs
// Renames the Vlandian culture card to the Templar order on the character-creation
// screen.
//
// Why this exists: the character-creation culture cards read "str_culture_rich_name"
// and CACHE the resolved string when the card is built. Our language-file override
// (8HMyTKF6 → "Templars") and our runtime game-text override both update the text
// AFTER the card is already built, so the displayed name stays "Vlandians". The only
// reliable fix is to set the bound view-model strings directly.
//
// The culture view-models are not reachable through a public API, so we walk the live
// screen object graph (screen → Gauntlet layers → movie data sources → the culture
// stage VM → its culture cards) by reflection, find the Vlandian card and rewrite it.
// The walk is strictly bounded (visited set, depth + node caps, TaleWorlds-only
// descent) and every access is guarded, so a failure or engine change degrades to a
// no-op — the worst case is the card still reads "Vlandians", never a crash.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace AshAndEmber
{
    internal static class TempleCultureCardFixer
    {
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private const string TempleName  = "Templars";
        private const string TempleDesc  =
            "The Templars are a holy order founded to stand against the Ashen and the eternal cold they "
            + "carry. Where lesser folk let their inner fire gutter, the Templars keep it burning as a sacred "
            + "trust. Descended from western lords who once served the Empire, they have bound throne to altar "
            + "and meet the grey march with disciplined lances and unbending faith.\n\n"
            + "SPECIAL FEATS\n"
            + "Dawn's Grace — The faithful are never left wanting. Should your Grace run dry, "
            + "each dawn the Light restores a measure of it. (+1 Grace at the start of each day, if empty)\n\n"
            + "Oath of the Vigil — Bound by sacred vow, your devotion steadies those who follow. "
            + "The order's discipline holds where lesser courage breaks. (+4 party morale each day)\n\n"
            + "THE ORDER'S PRICE — The Templars abhor the cold and the old pagan ways. "
            + "Dark gifts demand twice their price, and the living ember answers your hand a heartbeat slower. "
            + "(Dark Gift costs ×2; Nature channelling takes +1 second)";

        private static object _lastScreen;
        private static bool   _doneThisScreen;
        private static int    _throttle;

        // Called from the pre-game application tick. Cheap until the character-creation
        // screen is up; runs the bounded walk on a throttle and stops once it has
        // renamed the card for this screen instance.
        public static void TickTryFix()
        {
            try
            {
                object screen = null;
                try { screen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen; } catch { }

                if (screen == null) { _lastScreen = null; _doneThisScreen = false; return; }
                if (!ReferenceEquals(screen, _lastScreen)) { _lastScreen = screen; _doneThisScreen = false; }
                if (_doneThisScreen) return;
                if (screen.GetType().Name.IndexOf("CharacterCreation", StringComparison.OrdinalIgnoreCase) < 0) return;

                // Throttle: the walk is not free, so only sweep a few times a second.
                if (_throttle-- > 0) return;
                _throttle = 12;

                int budget = 8000;
                var visited = new HashSet<object>(RefComparer.Instance);
                bool fixedAny = false;
                Walk(screen, visited, 0, ref budget, ref fixedAny);
                if (fixedAny) _doneThisScreen = true;
            }
            catch { }
        }

        private static void Walk(object obj, HashSet<object> visited, int depth, ref int budget, ref bool fixedAny)
        {
            if (obj == null || depth > 12 || budget <= 0) return;
            Type type = obj.GetType();
            if (type.IsPrimitive || type.IsEnum || obj is string) return;
            if (!visited.Add(obj)) return;
            budget--;

            if (type.Name == "CharacterCreationCultureVM")
            {
                if (TryFixCultureVM(obj, type)) fixedAny = true;
                return;
            }

            // Collections: walk their items (covers MBBindingList, lists, dictionaries).
            if (obj is IEnumerable en)
            {
                try
                {
                    foreach (var item in en)
                    {
                        if (budget <= 0) break;
                        Walk(item, visited, depth + 1, ref budget, ref fixedAny);
                        if (fixedAny) return;
                    }
                }
                catch { }
                return;
            }

            // Bound the search: only descend into the game's own objects (the path runs
            // screen → Gauntlet layer → movie → view-models, all TaleWorlds/SandBox).
            string ns = type.Namespace ?? "";
            if (!(ns.StartsWith("TaleWorlds") || ns.StartsWith("SandBox") || ns.StartsWith("StoryMode")))
                return;

            foreach (FieldInfo f in type.GetFields(F))
            {
                if (budget <= 0 || fixedAny) break;
                Type ft = f.FieldType;
                if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string)) continue;
                object val;
                try { val = f.GetValue(obj); } catch { continue; }
                Walk(val, visited, depth + 1, ref budget, ref fixedAny);
            }
        }

        private static bool TryFixCultureVM(object vm, Type type)
        {
            try
            {
                object culture = type.GetProperty("Culture", F)?.GetValue(vm);
                string id = null;
                try { id = culture?.GetType().GetProperty("StringId")?.GetValue(culture) as string; } catch { }
                if (!string.Equals(id, "vlandia", StringComparison.OrdinalIgnoreCase)) return false;

                SetStringProp(vm, type, "NameText",          TempleName);
                SetStringProp(vm, type, "ShortenedNameText", TempleName);
                SetStringProp(vm, type, "DescriptionText",   TempleDesc);
                return true;
            }
            catch { return false; }
        }

        private static void SetStringProp(object vm, Type type, string prop, string value)
        {
            try { type.GetProperty(prop, F)?.SetValue(vm, value); } catch { }
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
