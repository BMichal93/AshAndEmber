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
    // 3. SPELL DATABASE  (18 battle spells + 12 campaign map spells (6 Affect + 6 Invoke), 4-char combos)
    // =========================================================================
    public enum SpellContext { Mission, Map }

    public class SpellEntry
    {
        public string      Name;
        public string      Combo;      // exactly 4 chars: U / L / R / D
        public ColorSchool School;
        public SpellContext Context;
        public string      ShortDesc;
        public string      Flavour;
    }

    public static class SpellDatabase
    {
        // Combos follow a strict structure: first 2 chars = Form, last 2 chars = Colour.
        // Forms: UU = Blast (cone), RL = Self (aura), LR = Create (area effect),
        //        UL = Affect (campaign map), LU = Invoke (campaign map, advanced)
        // Note: D (S key) is only registered when the buffer is non-empty and opens the spellbook.
        // Colours: RR = Red, LD = Orange, DD = Yellow, LL = Green, RU = Blue, DU = Purple
        // Note: D (S key) is valid mid-combo — only the first buffer character cannot be D.
        public static readonly IReadOnlyList<SpellEntry> All = new List<SpellEntry>
        {
            // ── BLAST (UU prefix) — medium cone in front of the caster ──────────
            new SpellEntry { Name="Crimson Torrent",  Combo="UURR", School=ColorSchool.Red,
                Context=SpellContext.Mission,
                ShortDesc="Fire cone, damages all ahead.",
                Flavour="The rage of a thousand battles channelled into a single, devastating wave." },
            new SpellEntry { Name="Golden Tide",      Combo="UULD", School=ColorSchool.Orange,
                Context=SpellContext.Mission,
                ShortDesc="Force cone; enemies advance.",
                Flavour="Wash over your foes with jubilant force; even enemies cannot resist the urge to advance." },
            new SpellEntry { Name="Tide of Dread",    Combo="UUDD", School=ColorSchool.Yellow,
                Context=SpellContext.Mission,
                ShortDesc="Fear cone, breaks nerve.",
                Flavour="A wave of creeping, nameless wrongness — it strips the nerve from all it touches and leaves behind only the urge to run." },
            new SpellEntry { Name="Verdant Surge",    Combo="UULL", School=ColorSchool.Green,
                Context=SpellContext.Mission,
                ShortDesc="Heals allies in cone.",
                Flavour="A tide of living energy that flows toward your own — allies in the cone are mended, enemies and the caster untouched." },
            new SpellEntry { Name="Azure Arrest",     Combo="UURU", School=ColorSchool.Blue,
                Context=SpellContext.Mission,
                ShortDesc="Freezes and unseats all ahead.",
                Flavour="A freezing wave of scholarly force. All before you halt; riders are unseated." },
            new SpellEntry { Name="Grey Harvest",     Combo="UUDU", School=ColorSchool.Purple,
                Context=SpellContext.Mission,
                ShortDesc="Kills 1–3 in the cone (scales with Vigor).",
                Flavour="The grey settles over the weak souls in the cone. They simply stop. The body follows the spirit out like a slow tide." },

            // ── SELF (RL prefix) — creates a glowing aura around the caster ─────
            new SpellEntry { Name="Scarlet Ward",     Combo="RLRR", School=ColorSchool.Red,
                Context=SpellContext.Mission,
                ShortDesc="Blocks next blow.",
                Flavour="The next blow lands on iron. One strike. The ward then shatters." },
            new SpellEntry { Name="Warm Beacon",      Combo="RLLD", School=ColorSchool.Orange,
                Context=SpellContext.Mission,
                ShortDesc="Calls allies to your side.",
                Flavour="A golden light calls your companions from across the field to your side." },
            new SpellEntry { Name="Nausea Bloom",     Combo="RLDD", School=ColorSchool.Yellow,
                Context=SpellContext.Mission,
                ShortDesc="Sickening aura, weakens nearby.",
                Flavour="Something deeply wrong radiates from you. All who linger nearby feel it in their stomach before they know it in their mind." },
            new SpellEntry { Name="Verdant Touch",    Combo="RLLL", School=ColorSchool.Green,
                Context=SpellContext.Mission,
                ShortDesc="Heals yourself.",
                Flavour="You lay hands upon yourself. The wounds knit closed." },
            new SpellEntry { Name="Cerulean Mirror",  Combo="RLRU", School=ColorSchool.Blue,
                Context=SpellContext.Mission,
                ShortDesc="Deflects missiles, 18s or 4 volleys.",
                Flavour="Missiles pass through you — four volleys at most, eighteen seconds at most. Steel does not." },
            new SpellEntry { Name="Grey Reaping",     Combo="RLDU", School=ColorSchool.Purple,
                Context=SpellContext.Mission,
                ShortDesc="Kills 1–2 nearby enemies; drains nerve from the rest.",
                Flavour="The grey does not take time to be elegant. It simply removes what is nearest — and leaves those who remain hollow." },

            // ── CREATE (LR prefix) — special area effect, specific to each colour ─
            new SpellEntry { Name="Cinder Burst",     Combo="LRRR", School=ColorSchool.Red,
                Context=SpellContext.Mission,
                ShortDesc="Explosion, damages all nearby.",
                Flavour="The world around you ignites. All nearby pay the price of your fury." },
            new SpellEntry { Name="Golden Recoil",    Combo="LRLD", School=ColorSchool.Orange,
                Context=SpellContext.Mission,
                ShortDesc="Retribution zone, toggle.",
                Flavour="A zone of warm retribution settles over the earth. Any soul that strikes while standing within it will feel a portion of the blow returned upon them. Cast again to dismiss." },
            new SpellEntry { Name="Creeping Dread",   Combo="LRDD", School=ColorSchool.Yellow,
                Context=SpellContext.Mission,
                ShortDesc="Drifting fear cloud, toggle.",
                Flavour="A cloud of formless revulsion drifts across the field. Those it passes through feel their skin crawl and their courage hollow out. Cast again to dismiss." },
            new SpellEntry { Name="Emerald Font",     Combo="LRLL", School=ColorSchool.Green,
                Context=SpellContext.Mission,
                ShortDesc="Healing pool, toggle.",
                Flavour="A blessed circle of earth. All who stand within are slowly mended — friend and foe alike. Cast again to dismiss." },
            new SpellEntry { Name="Sapphire Bastion", Combo="LRRU", School=ColorSchool.Blue,
                Context=SpellContext.Mission,
                ShortDesc="Force wall, toggle.",
                Flavour="A wall of solid blue force rises from the earth, repelling all who approach. Cast again to dismiss." },
            new SpellEntry { Name="Hollow Gaze",      Combo="LRDU", School=ColorSchool.Purple,
                Context=SpellContext.Mission,
                ShortDesc="Freezes one enemy, toggle.",
                Flavour="One nearby enemy empties out. They stand. They do not move. They wait for nothing. Cast again to release them." },

            // ── INVOKE (LU prefix) — campaign map only, advanced forms ───────────
            new SpellEntry { Name="Withering Strike",  Combo="LURR", School=ColorSchool.Red,
                Context=SpellContext.Map,
                ShortDesc="Wounds a random enemy soldier.",
                Flavour="The red reaches across the distance and finds a weakness. One soldier in the nearest enemy host falls before the battle even begins." },
            new SpellEntry { Name="Inspired Word",     Combo="LULD", School=ColorSchool.Orange,
                Context=SpellContext.Map,
                ShortDesc="Grants XP to a random ally.",
                Flavour="Words of warmth and purpose flow through your ranks. One soldier feels them land — and grows." },
            new SpellEntry { Name="Creeping Fear",     Combo="LUDD", School=ColorSchool.Yellow,
                Context=SpellContext.Map,
                ShortDesc="Nearest enemy party loses 3 morale.",
                Flavour="You do not shout. You do not threaten. Something quieter moves through the air toward them — and they feel it." },
            new SpellEntry { Name="Green's Bounty",    Combo="LULL", School=ColorSchool.Green,
                Context=SpellContext.Map,
                ShortDesc="Gain 1 grain.",
                Flavour="The green asks nothing of the soil. A handful of grain ripens in your pack, conjured from patience and living magic." },
            new SpellEntry { Name="Scholar's Word",    Combo="LURU", School=ColorSchool.Blue,
                Context=SpellContext.Map,
                ShortDesc="Gain 1 influence. Kingdom only.",
                Flavour="Insight spoken at the right moment in the right hall earns more than a season of battles. The court listens." },
            new SpellEntry { Name="Wither's Touch",    Combo="LUDU", School=ColorSchool.Purple,
                Context=SpellContext.Map,
                ShortDesc="Random enemy lord loses 2 renown.",
                Flavour="The grey reaches into another life and takes something small. They will not know what is gone — only that it is." },

            // ── AFFECT (UL prefix) — campaign map only, situation-dependent ─────
            new SpellEntry { Name="Pillager's Brand",  Combo="ULRR", School=ColorSchool.Red,
                Context=SpellContext.Map,
                ShortDesc="Curses a random enemy village. Hearth −20%.",
                Flavour="The red does not need to be present to destroy. It reaches across the distance and finds something worth ruining." },
            new SpellEntry { Name="Rallying Call",     Combo="ULLD", School=ColorSchool.Orange,
                Context=SpellContext.Map,
                ShortDesc="Party morale +3.",
                Flavour="A warmth moves through the ranks. They stand a little straighter. The road ahead looks a little shorter." },
            new SpellEntry { Name="Press Gang",        Combo="ULDD", School=ColorSchool.Yellow,
                Context=SpellContext.Map,
                ShortDesc="Conscripts a random prisoner. Own morale −2.",
                Flavour="One prisoner is made to understand that their options have narrowed. They join the march." },
            new SpellEntry { Name="Mending Touch",     Combo="ULLL", School=ColorSchool.Green,
                Context=SpellContext.Map,
                ShortDesc="Heals one wounded soldier.",
                Flavour="The green finds the nearest wound and closes it. One soldier walks away whole." },
            new SpellEntry { Name="Scholar's Blueprint", Combo="ULRU", School=ColorSchool.Blue,
                Context=SpellContext.Map,
                ShortDesc="Faster siege engines. Siege only.",
                Flavour="Only usable while besieging. The Scholar's eye finds the inefficiency in every joint and lever. The machines rise faster." },
            new SpellEntry { Name="Grey Veil",         Combo="ULDU", School=ColorSchool.Purple,
                Context=SpellContext.Map,
                ShortDesc="Scatters nearby enemies.",
                Flavour="The grey settles over the field. Those who were hunting you suddenly forget where they were going." },
        };

        public static SpellEntry Find(string combo) =>
            All.FirstOrDefault(s => s.Combo == combo);

        public static IEnumerable<SpellEntry> BySchool(ColorSchool school) =>
            All.Where(s => s.School == school);
    }
}
