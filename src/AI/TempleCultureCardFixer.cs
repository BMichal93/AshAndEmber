// =============================================================================
// ASH AND EMBER — AI/TempleCultureCardFixer.cs
// Rewrites the renamed culture cards on the character-creation culture-selection
// screen: Vlandia → The Holy Temple, Khuzait → Tribes of the East, Sturgia →
// Northmen, and Aserai → the Duneborn.
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
        // cultural feats shown in the dedicated feats panel. Feats are listed
        // { positive, positive, negative } in display order, mirroring
        // AshenCitySystem._templeFeats / _tribalFeats — the engine builds the card's
        // feat view-models (and CACHES their description strings) from the culture's
        // FeatObjects, so relabelling the data is not enough; we rewrite the bound VM.
        private sealed class CultureCard
        {
            public string   Id;
            public string   Name;
            public string   Desc;
            public string[] Feats;
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
                Feats = new[]
                {
                    "Dawn's Grace — Should your Grace run dry, each dawn the Light restores a measure of it. (+1 Grace at dawn, if empty)",
                    "Oath of the Vigil — Your sworn discipline steadies those who follow. (+4 party morale per day)",
                    "The Order's Price — Dark gifts demand twice their cost, and the living ember answers your hand a breath slower. (Dark Gift ×2 cost; Nature channelling +1s)",
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
                Feats = new[]
                {
                    "War Fever — The Tribes ride to war as if born to it; your clan's parties never lose heart. (party morale floor +15)",
                    "Spoils of the Raid — A village put to the torch yields more than the usual plunder. (+50–150 gold per raid)",
                    "No Quarter — The God-King's word burns through any treaty; your wars do not end in peace.",
                },
            },
            // Sturgia → the Northmen. Name + blurb only; the feats are LEFT as vanilla
            // Sturgian (empty list ⇒ RewriteFeats is a no-op), since the Northmen are
            // mechanically ordinary Sturgia — only renamed.
            new CultureCard
            {
                Id   = "sturgia",
                Name = "Northmen",
                Desc =
                    "The Northmen hold the cold edge of the world, where the forest gives way to ice and the "
                    + "winter nights run longest. They are a hard folk — raiders and shipwrights, sworn to oath, "
                    + "blood-feud, and the long memory of their kings.\n\n"
                    + "But what truly shapes them is the war that never ends. Out of the deeper north press the "
                    + "Ashen — the dead-cold lords who neither age nor tire — and it falls to the Northmen to "
                    + "stand in the gap. Every hall keeps its watch-fires burning; every child learns the axe "
                    + "before the plough. They do not expect to break the cold. They expect to hold the line.",
                Feats = new string[0],
            },
            // Aserai → the Duneborn. Name, blurb AND feats: the caravan bonus is
            // replaced by the Blood Tithe (zeroed + relabelled in
            // AshenCitySystem.RelabelCulturalFeats), but the card VM caches the
            // vanilla descriptions before that lands — mirror _dunebornFeats here.
            new CultureCard
            {
                Id   = "aserai",
                Name = "Duneborn",
                Desc =
                    "The desert does not forgive, and the Duneborn stopped asking it to. Once they kept the same "
                    + "covenant with the inner fire as every tribe beneath the sun — a warmth earned, a debt honoured. "
                    + "Then came the long drought: three generations of cracked wells and a sun that gave nothing "
                    + "back for what it took, and the fire-covenant went dry along with everything else.\n\n"
                    + "In the black-glass caverns beneath the dunes, where no torch had ever burned, the first "
                    + "Duneborn found something older than fire and far hungrier — a power that asked no devotion, "
                    + "only blood, and did not care what was done with what it gave. They do not call it a god. "
                    + "They call it patient. Every great house keeps its bargain quiet and its knives quieter still.",
                Feats = new[]
                {
                    "Blood Tithe — the thing beneath the dunes takes a fifth less of every offering. (Dark Altar sacrifices −20%)",
                    "Children of the Sand — the deep desert neither slows nor wearies your kin. (No speed penalty on desert)",
                    "Hungry Knives — hired blades smell the old bargain on you, and charge for it. (Daily troop wages +5%)",
                },
            },
            // Battania → the Forest Clans. Name, blurb AND feats: the Forest Clans'
            // own faction skills (ForestClansCulture.cs) replace whatever vanilla
            // Battanian feats the card would otherwise cache.
            new CultureCard
            {
                Id   = "battania",
                Name = "Forest Clan",
                Desc =
                    "The clans of the deep wood never knelt easily to any crown, and least of all to their own. "
                    + "What binds them is older than any throne: a pact struck long before memory with the roots "
                    + "beneath the leaf-mould and the standing stones the forest has not yet swallowed.\n\n"
                    + "Where a Templar kneels to a flame and a Duneborn bargains with a hunger under the sand, "
                    + "the Forest Clans do neither — they simply listen. At certain trees, at certain stones, "
                    + "something answers back, and what wakes there does not forget who woke it. Cross their "
                    + "border uninvited, and you will learn what the old wood keeps watch with.",
                Feats = new[]
                {
                    "Kinship of Root and Stone — the old grove knows its own kin. (Sacred-site binding costs 15% less and succeeds 10% more often for the clan-born)",
                    "The Green Roads — the clans know the wood's hidden paths and cross forest ground faster than any other host. (+20% party speed on forest terrain)",
                    "Wild and Few — untamed folk close to the land, slow to the drilled ways of great armies; their warbands run leaner and cost more to keep. (Party size −5%, troop wages +5%)",
                },
            },
        };

        private static object _lastScreen;
        private static bool   _doneThisScreen;
        private static int    _throttle;
        // Failsafe: how many gating passes we will hold "Next" disabled before giving
        // up and releasing it regardless. A card whose VM is never reachable by the
        // walk (or whose name is corrected via the game-text path instead of the VM)
        // would otherwise keep the gate shut forever and the player could never start.
        private static int        _gateAttempts;
        private const  int        MaxGateAttempts = 8;   // ≈ 1.5 s at the walk throttle
        // The culture-stage view-model found during the current walk, used to gate
        // "Next" until every renamed card reads its corrected name/feats.
        private static object _stageVmThisPass;

        // Called from the pre-game application tick. Cheap until the character-creation
        // screen is up; runs the bounded walk on a throttle and stops once it has
        // rewritten every renamed card for this screen instance.
        public static void TickTryFix()
        {
            try
            {
                object screen = null;
                try { screen = TaleWorlds.ScreenSystem.ScreenManager.TopScreen; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                if (screen == null) { _lastScreen = null; _doneThisScreen = false; _gateAttempts = 0; return; }
                if (!ReferenceEquals(screen, _lastScreen)) { _lastScreen = screen; _doneThisScreen = false; _gateAttempts = 0; }
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

                _stageVmThisPass = null;
                // Direct path first: screen._currentStageView._dataSource IS the live
                // stage VM (verified against SandBox.View/SandBox.GauntletUI). Walking
                // from the screen root starves the node budget in the Gauntlet
                // layer/sprite graphs before ever reaching the data source — which is
                // exactly why the cards used to stay vanilla until a hover refreshed
                // them. The full-screen walk remains only as a fallback.
                object ds = CurrentStageDataSource(screen);
                if (ds != null) Walk(ds, visited, 0, ref budget, pending);
                else            Walk(screen, visited, 0, ref budget, pending);

                // Gate the stage while any card still shows its stale (vanilla) text:
                // the rename lands a few frames after the screen is built, and we do
                // not want the player committing to a faction reading "Vlandians".
                // Once every card is corrected we release the gate (restoring the
                // engine's own selection-driven state) and stop interfering.
                if (pending.Count == 0)
                {
                    // Every card corrected — release the gate and stop sweeping.
                    SetCanAdvanceFromSelection(_stageVmThisPass);
                    _doneThisScreen = true;
                }
                else
                {
                    // Cards still pending — KEEP sweeping every pass (do not mark the
                    // screen done): the culture view-models may not be reachable until
                    // several frames after the screen is built, so the cosmetic rename
                    // must keep retrying. But only HOLD "Next" for a short window, then
                    // stop blocking so the player can never be trapped on this screen.
                    if (++_gateAttempts < MaxGateAttempts)
                        SetStageBool(_stageVmThisPass, "CanAdvance", false);
                }
                _stageVmThisPass = null;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // The current stage's view-model, straight off the screen:
        // CharacterCreationScreen._currentStageView (SandBox.View) holds the active
        // stage view, and every concrete stage view keeps its VM in _dataSource.
        // Fields are searched up the type hierarchy; any miss returns null and the
        // caller falls back to the bounded full-screen walk.
        internal static object CurrentStageDataSource(object screen)
        {
            try
            {
                object view = GetFieldUpHierarchy(screen, "_currentStageView");
                if (view == null) return null;
                return GetFieldUpHierarchy(view, "_dataSource");
            }
            catch { return null; }
        }

        private static object GetFieldUpHierarchy(object obj, string name)
        {
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, F);
                if (f != null) { try { return f.GetValue(obj); } catch { return null; } }
            }
            return null;
        }

        private static void Walk(object obj, HashSet<object> visited, int depth, ref int budget, HashSet<string> pending)
        {
            if (obj == null || depth > 12 || budget <= 0 || pending.Count == 0) return;
            Type type = obj.GetType();
            if (type.IsPrimitive || type.IsEnum || obj is string) return;
            if (!visited.Add(obj)) return;
            budget--;

            // Remember the stage VM so we can gate "Next" after the walk; keep
            // descending through it to reach the culture cards it owns.
            if (type.Name == "CharacterCreationCultureStageVM")
                _stageVmThisPass = obj;

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
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                try { id = culture?.GetType().GetProperty("StringId")?.GetValue(culture) as string; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (id == null) return;

                CultureCard card = null;
                foreach (var c in Cards)
                    if (string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) { card = c; break; }
                if (card == null) return;

                SetStringProp(vm, type, "NameText",          card.Name);
                SetStringProp(vm, type, "ShortenedNameText", card.Name);
                SetStringProp(vm, type, "DescriptionText",   card.Desc);
                // The card caches each feat's description string into its Feats VM list
                // when built, so relabelling the FeatObject data alone never reaches the
                // panel — rewrite the bound list here too. Match by sign, positives in
                // order then the single negative, mirroring RelabelCulturalFeats.
                RewriteFeats(vm, type, card.Feats);
                pending.Remove(id);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void SetStringProp(object vm, Type type, string prop, string value)
        {
            try { type.GetProperty(prop, F)?.SetValue(vm, value); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Rewrites the card's bound feat descriptions: positives in order, then the
        // single negative. feats is { positive, …, negative } in display order.
        private static void RewriteFeats(object vm, Type type, string[] feats)
        {
            if (feats == null || feats.Length == 0) return;
            try
            {
                if (!(type.GetProperty("Feats", F)?.GetValue(vm) is IEnumerable list)) return;

                int lastPositive = feats.Length - 1;   // entries [0..lastPositive) are positive
                string negative  = feats[feats.Length - 1];
                int posIdx = 0;

                foreach (var feat in list)
                {
                    if (feat == null) continue;
                    var ft = feat.GetType();
                    bool isPositive = (bool)(ft.GetProperty("IsPositive", F)?.GetValue(feat) ?? true);
                    string desc = isPositive
                        ? (posIdx < lastPositive ? feats[posIdx++] : feats[Math.Max(0, lastPositive - 1)])
                        : negative;
                    try { ft.GetProperty("Description", F)?.SetValue(feat, desc); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Sets a bool property on the stage VM (used to gate "Next"); no-op if absent.
        private static void SetStageBool(object stageVm, string prop, bool value)
        {
            if (stageVm == null) return;
            try { stageVm.GetType().GetProperty(prop, F)?.SetValue(stageVm, value); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Releases the gate: restore CanAdvance to whatever the player's current
        // selection implies, so the engine's normal state takes over again.
        private static void SetCanAdvanceFromSelection(object stageVm)
        {
            if (stageVm == null) return;
            try
            {
                var t = stageVm.GetType();
                bool anySelected = (bool)(t.GetProperty("AnyItemSelected", F)?.GetValue(stageVm) ?? false);
                t.GetProperty("CanAdvance", F)?.SetValue(stageVm, anySelected);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
