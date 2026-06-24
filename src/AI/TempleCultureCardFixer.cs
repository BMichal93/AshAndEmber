// =============================================================================
// ASH AND EMBER — AI/TempleCultureCardFixer.cs
// Rewrites the renamed culture cards on the character-creation culture-selection
// screen: Vlandia → The Holy Temple, and Khuzait → Tribes of the East.
//
// Why this exists: the culture cards read "str_culture_rich_name" and CACHE the
// resolved string when the card is built, and they build their feats list from
// the culture's native FeatObjects. Our language-file / game-text overrides land
// AFTER the card is built, so the displayed name stays "Vlandians"/"Khuzaits" and
// the feats panel shows the native cultural feats. The only reliable fix is to
// rewrite the bound view-model directly:
//   • NameText / ShortenedNameText / DescriptionText  (lore only — no feat block)
//   • the dedicated Feats list                        (our cultural feats)
//
// The culture view-models are not reachable through a public API (and the mod
// does not reference the ViewModelCollection assembly), so we walk the live
// screen object graph by reflection, find each renamed card, and rewrite it. The
// walk is strictly bounded (visited set, depth + node caps, TaleWorlds-only
// descent) and every access is guarded, so a failure or engine change degrades to
// a no-op — the worst case is the card still reads its native name, never a crash.
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

        // One renamed card per culture: display name, lore (feat-free), and the
        // cultural feats to show in the dedicated feats panel (positive first).
        private sealed class CultureCard
        {
            public string Id;
            public string Name;
            public string Desc;
            public (bool positive, string text)[] Feats;
        }

        private static readonly CultureCard[] Cards =
        {
            new CultureCard
            {
                Id   = "vlandia",
                Name = "The Holy Temple",
                Desc =
                    "Once they were lords of the western marches, bound to no altar and no god but conquest. "
                    + "When the grey march first came down from the north, the Empire turned east and left them to "
                    + "face it alone. They held. In the silence that followed, they made a covenant with the fire "
                    + "inside them — not as weapon, but as vow. The Templars are what that vow became.\n\n"
                    + "They bind throne to altar. They count the cost. They do not flinch at what the Light requires of them.",
                Feats = new (bool, string)[]
                {
                    (true,  "Dawn's Grace — Should your Grace run dry, each dawn the Light restores a measure of it. (+1 Grace at dawn, if empty)"),
                    (true,  "Oath of the Vigil — Your sworn discipline steadies those who follow. (+4 party morale per day)"),
                    (false, "The Order's Price — Dark gifts demand twice their cost, and the living ember answers your hand a breath slower. (Dark Gift ×2 cost; Nature channelling +1s)"),
                },
            },
            new CultureCard
            {
                Id   = "khuzait",
                Name = "Tribes of the East",
                Desc =
                    "They came from the eastern steppe — a hundred warring clans who forgot how to stop fighting "
                    + "until the God-King put his hand on the sky and turned three chieftains to ash. The rest knelt. "
                    + "Now the Tribes ride as one, not because they love their king, but because they love war, and "
                    + "he alone has shown them how to win it. He wields fire the way other men wield iron. He does not "
                    + "negotiate. He takes wives from every city his horsemen put to tribute. He is watching the Empire "
                    + "bleed itself empty, and he is patient. The Tribes do not seek peace. They seek the next horizon.",
                Feats = new (bool, string)[]
                {
                    (true,  "War Fever — The Tribes ride to war as if born to it; your clan's parties never lose heart. (party morale floor +15)"),
                    (true,  "Spoils of the Raid — A village put to the torch yields more than the usual plunder. (+50–150 gold per raid)"),
                    (false, "No Quarter — The God-King's word burns through any treaty; your wars do not end in peace."),
                },
            },
        };

        private static object _lastScreen;
        private static bool   _doneThisScreen;
        private static int    _throttle;

        // Called from the pre-game application tick. Cheap until the character-creation
        // screen is up; runs the bounded walk on a throttle and stops once it has
        // rewritten every renamed card for this screen instance.
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
                // Cultures still awaiting a rewrite this pass.
                var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in Cards) pending.Add(c.Id);

                Walk(screen, visited, 0, ref budget, pending);
                if (pending.Count == 0) _doneThisScreen = true;
            }
            catch { }
        }

        private static void Walk(object obj, HashSet<object> visited, int depth, ref int budget, HashSet<string> pending)
        {
            if (obj == null || depth > 12 || budget <= 0 || pending.Count == 0) return;
            Type type = obj.GetType();
            if (type.IsPrimitive || type.IsEnum || obj is string) return;
            if (!visited.Add(obj)) return;
            budget--;

            if (type.Name == "CharacterCreationCultureVM")
            {
                TryFixCultureVM(obj, type, pending);
                return;
            }

            // Collections: walk their items (covers MBBindingList, lists, dictionaries).
            if (obj is IEnumerable en)
            {
                try
                {
                    foreach (var item in en)
                    {
                        if (budget <= 0 || pending.Count == 0) break;
                        Walk(item, visited, depth + 1, ref budget, pending);
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
                if (budget <= 0 || pending.Count == 0) break;
                Type ft = f.FieldType;
                if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string)) continue;
                object val;
                try { val = f.GetValue(obj); } catch { continue; }
                Walk(val, visited, depth + 1, ref budget, pending);
            }
        }

        private static void TryFixCultureVM(object vm, Type type, HashSet<string> pending)
        {
            try
            {
                object culture = type.GetProperty("Culture", F)?.GetValue(vm);
                string id = null;
                try { id = culture?.GetType().GetProperty("StringId")?.GetValue(culture) as string; } catch { }
                if (id == null) return;

                CultureCard card = null;
                foreach (var c in Cards)
                    if (string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) { card = c; break; }
                if (card == null) return;

                SetStringProp(vm, type, "NameText",          card.Name);
                SetStringProp(vm, type, "ShortenedNameText", card.Name);
                SetStringProp(vm, type, "DescriptionText",   card.Desc);
                SetFeats(vm, type, card.Feats);
                pending.Remove(id);
            }
            catch { }
        }

        private static void SetStringProp(object vm, Type type, string prop, string value)
        {
            try { type.GetProperty(prop, F)?.SetValue(vm, value); } catch { }
        }

        // Replaces the dedicated feats list with our cultural feats. Done by reflection
        // because the mod does not reference the ViewModelCollection assembly that
        // declares CharacterCreationCultureFeatVM / the MBBindingList element type.
        private static void SetFeats(object vm, Type type, (bool positive, string text)[] feats)
        {
            try
            {
                var listObj = type.GetProperty("Feats", F)?.GetValue(vm);
                if (listObj == null) return;
                Type listType = listObj.GetType();
                Type featVmType = listType.IsGenericType ? listType.GetGenericArguments()[0] : null;
                if (featVmType == null) return;

                // Idempotent: skip if the list already holds our feats (avoids rebuilding
                // and re-firing change events on every throttled pass).
                var countProp = listType.GetProperty("Count");
                if (countProp?.GetValue(listObj) is int n && n == feats.Length) return;

                var clear = listType.GetMethod("Clear");
                var add   = listType.GetMethod("Add", new[] { featVmType });
                if (clear == null || add == null) return;

                clear.Invoke(listObj, null);
                foreach (var f in feats)
                {
                    object featVm = Activator.CreateInstance(featVmType, new object[] { f.positive, f.text });
                    add.Invoke(listObj, new[] { featVm });
                }
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
