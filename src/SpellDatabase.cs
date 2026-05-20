// =============================================================================
// COLOURS OF CALRADIA — SpellDatabase.cs
// Mount & Blade II: Bannerlord Mod  v1.2.0.0
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
    // 3. SPELL DATABASE  (18 battle spells + 12 campaign map spells (6 Affect + 6 Invoke), 5-char combos)
    // =========================================================================
    public enum SpellContext { Mission, Map }

    public class SpellEntry
    {
        public string      Name;
        public string      Combo;      // exactly 5 chars: U / L / R / D
        public ColorSchool School;
        public SpellContext Context;
        public string      ShortDesc;
        public string      Flavour;
    }

    public static class SpellDatabase
    {
        // Combos follow a strict structure: first 2 chars = Form, last 3 chars = Colour.
        // Forms: UU = Blast (cone), RL = Self (aura), LR = Create (area effect),
        //        UL = Affect (campaign map), LU = Invoke (campaign map, advanced)
        // Note: D (S key) is only registered when the buffer is non-empty and opens the spellbook.
        // No spell combo uses D — UL = W then A (mirror of LU = A then W).
        // Colours: RRR = Red, RLR = Orange, DLD = Yellow, LDL = Green, ULU = Blue, DDD = Purple
        // Note: D (S key) is valid mid-combo — only the first buffer character cannot be D.
        public static readonly IReadOnlyList<SpellEntry> All = new List<SpellEntry>
        {
            // ── BLAST (UU prefix) — medium cone in front of the caster ──────────
            new SpellEntry { Name="Crimson Torrent",  Combo="UURRR", School=ColorSchool.Red,
                Context=SpellContext.Mission,
                ShortDesc="Fire cone, damages all ahead.",
                Flavour="The rage of a thousand battles channelled into a single, devastating wave." },
            new SpellEntry { Name="Golden Tide",      Combo="UURLR", School=ColorSchool.Orange,
                Context=SpellContext.Mission,
                ShortDesc="Force cone; enemies advance.",
                Flavour="Wash over your foes with jubilant force; even enemies cannot resist the urge to advance." },
            new SpellEntry { Name="Tide of Dread",    Combo="UUDLD", School=ColorSchool.Yellow,
                Context=SpellContext.Mission,
                ShortDesc="Fear cone, breaks nerve.",
                Flavour="A wave of creeping, nameless wrongness — it strips the nerve from all it touches and leaves behind only the urge to run." },
            new SpellEntry { Name="Verdant Surge",    Combo="UULDL", School=ColorSchool.Green,
                Context=SpellContext.Mission,
                ShortDesc="Heals allies in cone.",
                Flavour="A tide of living energy that flows toward your own — allies in the cone are mended, enemies and the caster untouched." },
            new SpellEntry { Name="Azure Arrest",     Combo="UUULU", School=ColorSchool.Blue,
                Context=SpellContext.Mission,
                ShortDesc="Freezes and unseats all ahead.",
                Flavour="A freezing wave of scholarly force. All before you halt; riders are unseated." },
            new SpellEntry { Name="Grey Harvest",     Combo="UUDDD", School=ColorSchool.Purple,
                Context=SpellContext.Mission,
                ShortDesc="Kills 1–3 in the cone (scales with Vigor).",
                Flavour="The grey settles over the weak souls in the cone. They simply stop. The body follows the spirit out like a slow tide." },

            // ── SELF (RL prefix) — creates a glowing aura around the caster ─────
            // Note: DD prefix cannot be used — S with empty buffer opens the spellbook.
            new SpellEntry { Name="Scarlet Ward",     Combo="RLRRR", School=ColorSchool.Red,
                Context=SpellContext.Mission,
                ShortDesc="Blocks next blow.",
                Flavour="The next blow lands on iron. One strike. The ward then shatters." },
            new SpellEntry { Name="Warm Beacon",      Combo="RLRLR", School=ColorSchool.Orange,
                Context=SpellContext.Mission,
                ShortDesc="Calls allies to your side.",
                Flavour="A golden light calls your companions from across the field to your side." },
            new SpellEntry { Name="Nausea Bloom",     Combo="RLDLD", School=ColorSchool.Yellow,
                Context=SpellContext.Mission,
                ShortDesc="Sickening aura, weakens nearby.",
                Flavour="Something deeply wrong radiates from you. All who linger nearby feel it in their stomach before they know it in their mind." },
            new SpellEntry { Name="Verdant Touch",    Combo="RLLDL", School=ColorSchool.Green,
                Context=SpellContext.Mission,
                ShortDesc="Heals yourself.",
                Flavour="You lay hands upon yourself. The wounds knit closed." },
            new SpellEntry { Name="Cerulean Mirror",  Combo="RLULU", School=ColorSchool.Blue,
                Context=SpellContext.Mission,
                ShortDesc="Deflects missiles, 18s or 4 volleys.",
                Flavour="Missiles pass through you — four volleys at most, eighteen seconds at most. Steel does not." },
            new SpellEntry { Name="Grief's Veil",     Combo="RLDDD", School=ColorSchool.Purple,
                Context=SpellContext.Mission,
                ShortDesc="Invisible, 12s. Enemies near you lose nerve.",
                Flavour="The grey folds you from sight for 12 seconds. Nearby enemies lose track of you and pause. You cannot be touched while the veil holds — but it fades fast." },

            // ── CREATE (LR prefix) — special area effect, specific to each colour ─
            new SpellEntry { Name="Cinder Burst",     Combo="LRRRR", School=ColorSchool.Red,
                Context=SpellContext.Mission,
                ShortDesc="Explosion, damages all nearby.",
                Flavour="The world around you ignites. All nearby pay the price of your fury." },
            new SpellEntry { Name="Golden Recoil",    Combo="LRRLR", School=ColorSchool.Orange,
                Context=SpellContext.Mission,
                ShortDesc="Retribution zone, toggle.",
                Flavour="A zone of warm retribution settles over the earth. Any soul that strikes while standing within it will feel a portion of the blow returned upon them. Cast again to dismiss." },
            new SpellEntry { Name="Creeping Dread",   Combo="LRDLD", School=ColorSchool.Yellow,
                Context=SpellContext.Mission,
                ShortDesc="Drifting fear cloud, toggle.",
                Flavour="A cloud of formless revulsion drifts across the field. Those it passes through feel their skin crawl and their courage hollow out. Cast again to dismiss." },
            new SpellEntry { Name="Emerald Font",     Combo="LRLDL", School=ColorSchool.Green,
                Context=SpellContext.Mission,
                ShortDesc="Healing pool, toggle.",
                Flavour="A blessed circle of earth. All who stand within are slowly mended — friend and foe alike. Cast again to dismiss." },
            new SpellEntry { Name="Sapphire Bastion", Combo="LRULU", School=ColorSchool.Blue,
                Context=SpellContext.Mission,
                ShortDesc="Force wall, toggle.",
                Flavour="A wall of solid blue force rises from the earth, repelling all who approach. Cast again to dismiss." },
            new SpellEntry { Name="Hollow Gaze",      Combo="LRDDD", School=ColorSchool.Purple,
                Context=SpellContext.Mission,
                ShortDesc="Freezes one enemy, toggle.",
                Flavour="One nearby enemy empties out. They stand. They do not move. They wait for nothing. Cast again to release them." },

            // ── INVOKE (LU prefix) — campaign map only, advanced forms ───────────
            new SpellEntry { Name="Crimson March",     Combo="LURRR", School=ColorSchool.Red,
                Context=SpellContext.Map,
                ShortDesc="Speed boost, costs HP.",
                Flavour="A small wound, left open. Not enough to slow you — just enough to remind the body what is at stake. The pace holds for as long as the blood does." },
            new SpellEntry { Name="Golden Word",       Combo="LURLR", School=ColorSchool.Orange,
                Context=SpellContext.Map,
                ShortDesc="Gold → influence.",
                Flavour="Gold spent openly, before witnesses, in generous excess — it speaks louder than any title. The standing it buys is real." },
            new SpellEntry { Name="Whispered Ruin",   Combo="LUDLD", School=ColorSchool.Yellow,
                Context=SpellContext.Map,
                ShortDesc="Drains nearby lord's renown.",
                Flavour="A name repeated in the wrong ears becomes a wound. The target's standing bleeds quietly." },
            new SpellEntry { Name="Tend the Fallen",  Combo="LULDL", School=ColorSchool.Green,
                Context=SpellContext.Map,
                ShortDesc="Heals wounded in party.",
                Flavour="The green does not ask who deserves healing. It simply flows to where life is thin." },
            new SpellEntry { Name="Scholar's Blueprint", Combo="LUULU", School=ColorSchool.Blue,
                Context=SpellContext.Map,
                ShortDesc="Faster siege engines. Siege only.",
                Flavour="Only usable while besieging. The Scholar's eye finds the inefficiency in every joint and lever. The machines rise faster." },
            new SpellEntry { Name="Wither's Touch",   Combo="LUDDD", School=ColorSchool.Purple,
                Context=SpellContext.Map,
                ShortDesc="Drains enemy morale and renown.",
                Flavour="The grey reaches into another life and takes something small. They will not know what is gone — only that it is." },

            // ── AFFECT (UL prefix) — campaign map only, situation-dependent ─────
            new SpellEntry { Name="Ember Drive",       Combo="ULRRR", School=ColorSchool.Red,
                Context=SpellContext.Map,
                ShortDesc="Raid/hideout boost. Raid only.",
                Flavour="Only usable while raiding a village or clearing a hideout. The red drives your soldiers forward — the plunder comes easier." },
            new SpellEntry { Name="Shared Feast",      Combo="ULRLR", School=ColorSchool.Orange,
                Context=SpellContext.Map,
                ShortDesc="Food → morale.",
                Flavour="Consume one unit of food from your supplies to lift the spirit of those who march with you." },
            new SpellEntry { Name="Dread Whisper",     Combo="ULDLD", School=ColorSchool.Yellow,
                Context=SpellContext.Map,
                ShortDesc="Projects fear at nearest enemy.",
                Flavour="Send fear forward. Your party feels it too — but the closest enemy feels it more." },
            new SpellEntry { Name="Verdant Hour",      Combo="ULLDL", School=ColorSchool.Green,
                Context=SpellContext.Map,
                ShortDesc="Creates food.",
                Flavour="The green works quietly. A small harvest of grain appears, coaxed from the earth by patience and living magic." },
            new SpellEntry { Name="Scholar's Gaze",    Combo="ULULU", School=ColorSchool.Blue,
                Context=SpellContext.Map,
                ShortDesc="Reveals nearby lords and armies.",
                Flavour="The Scholar turns the mind's eye outward. All allied and enemy parties within range snap into focus — their leaders, numbers, and directions laid bare." },
            new SpellEntry { Name="Grey Veil",         Combo="ULDDD", School=ColorSchool.Purple,
                Context=SpellContext.Map,
                ShortDesc="Scatters nearby enemies; costs fertility and age.",
                Flavour="The grey hides you — at a permanent price. Nearby enemies lose your trail. Something of your future bleeds away with it." },
        };

        public static SpellEntry Find(string combo) =>
            All.FirstOrDefault(s => s.Combo == combo);

        public static IEnumerable<SpellEntry> BySchool(ColorSchool school) =>
            All.Where(s => s.School == school);
    }
}
