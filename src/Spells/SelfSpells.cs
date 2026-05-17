// =============================================================================
// COLOURS OF CALRADIA — SelfSpells.cs
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
using TaleWorlds.Engine;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.CampaignSystem.MapEvents;

namespace ColoursOfCalradia
{
    public static partial class SpellEffects
    {
        // =================================================================
        // SELF SPELLS — glowing aura around the caster
        // =================================================================

        // Scarlet Ward — absorbs the next single blow; expires after 8 s if nothing hits
        private static void SpellSelfRed()
        {
            if (Player == null || !Player.IsActive()) return;
            if (_scarletWardActive) { Msg("Scarlet Ward is already active.", ColorSchool.Red); return; }
            const float Duration = 8f;
            _scarletWardActive = true;
            BeginAgentGlow(Player, ColorSchool.Red, Duration);
            ActiveEffectManager.Add(new ActiveEffect
            {
                Name = "_scarlet_ward", Duration = Duration, IsMissionEffect = true,
                OnExpire = () =>
                {
                    if (_scarletWardActive)
                    {
                        _scarletWardActive = false;
                        Msg("The Scarlet Ward fades.", ColorSchool.Red);
                    }
                }
            });
            Msg("Scarlet Ward — the next blow will find iron, not flesh.", ColorSchool.Red);
        }

        // Called from OnAgentHit — restores the damage from the triggering blow
        public static void AbsorbScarletWard(int absorbed)
        {
            if (!_scarletWardActive) return;
            _scarletWardActive = false;
            if (Player?.IsActive() == true && absorbed > 0)
                try { Player.Health = Math.Min(Player.HealthLimit, Math.Max(1f, Player.Health) + absorbed); } catch { }
            Msg($"Scarlet Ward shatters — iron turned the blow ({absorbed} absorbed).", ColorSchool.Red);
        }

        // Warm Beacon — teleport all nearby allies to your side
        private static void SpellSelfOrange()
        {
            if (Mission.Current == null || Player == null) return;
            const float Radius = 18f;
            const float LandDist = 3f;
            var nearAllies = Allies().Where(a => a.Position.Distance(Player.Position) <= Radius).ToList();
            if (nearAllies.Count == 0) { Msg("No allies within range.", ColorSchool.Orange); return; }
            float angle = 0f;
            float step = nearAllies.Count > 0 ? (2f * (float)Math.PI / nearAllies.Count) : 0f;
            foreach (Agent a in nearAllies)
            {
                try
                {
                    Vec3 offset = new Vec3((float)Math.Cos(angle) * LandDist, (float)Math.Sin(angle) * LandDist, 0f);
                    Vec3 dest   = Player.Position + offset; dest.z = Player.Position.z;
                    QueueMove(a, dest, 0.4f);
                    BeginAgentGlow(a, ColorSchool.Orange, 1.5f);
                }
                catch { }
                angle += step;
            }
            BeginAgentGlow(Player, ColorSchool.Orange, 1.5f);
            Msg($"Warm Beacon — {nearAllies.Count} {(nearAllies.Count == 1 ? "ally slides" : "allies slide")} to your side.", ColorSchool.Orange);
        }

        // Nausea Bloom — 30-second aura that slowly damages everything nearby
        private static void SpellSelfYellow()
        {
            if (Player == null) return;
            if (HasAreaEffect("self_yellow")) { Msg("Nausea Bloom is already active.", ColorSchool.Yellow); return; }
            ToggleAreaEffect("self_yellow", new AreaEffect
            {
                Id = "self_yellow", School = ColorSchool.Yellow,
                Position = Player.Position, Radius = 8f,
                Velocity = new Vec3(1f, 0f, 0f), DirTimer = 3f,
                TickInterval = 2f, TickTimer = 2f, Remaining = 30f,
                Power = SpellPower(ColorSchool.Yellow)
            });
            BeginAgentGlow(Player, ColorSchool.Yellow, 2f);
            ActiveEffectManager.Add(new ActiveEffect
            {
                Name = "_nausea_bloom", Duration = 30f, IsMissionEffect = true,
                OnExpire = () =>
                {
                    RemoveAreaEffect("self_yellow");
                    Msg("The Nausea Bloom passes. The wrongness fades.", ColorSchool.Yellow);
                }
            });
            Msg("Nausea Bloom — something deeply wrong radiates from you for 30 seconds. All nearby will feel it.", ColorSchool.Yellow);
        }

        // Verdant Touch — heal self
        private static void SpellSelfGreen()
        {
            if (Player == null) return;
            float power = SpellPower(ColorSchool.Green);
            float heal = Math.Min(20f * power, Player.HealthLimit - Player.Health);
            Player.Health = Math.Min(Player.Health + 20f * power, Player.HealthLimit);
            BeginAgentGlow(Player, ColorSchool.Green, 1.5f);
            Msg($"Verdant Touch — you restore {heal:F0} HP.", ColorSchool.Green);
        }

        // Cerulean Mirror — 40-second magic immunity (physical attacks still connect)
        private static void SpellSelfBlue()
        {
            if (Player == null || !Player.IsActive()) return;
            if (_ceruleanMirrorActive) { Msg("Cerulean Mirror is already active.", ColorSchool.Blue); return; }
            const float Duration = 40f;
            _ceruleanMirrorActive = true;
            BeginAgentGlow(Player, ColorSchool.Blue, Duration);
            ActiveEffectManager.Add(new ActiveEffect
            {
                Name = "_cerulean_mirror", Duration = Duration, IsMissionEffect = true,
                OnExpire = () =>
                {
                    _ceruleanMirrorActive = false;
                    Msg("The Cerulean Mirror dims. Spells find you again.", ColorSchool.Blue);
                }
            });
            Msg("Cerulean Mirror — spells pass through you for 40 seconds.", ColorSchool.Blue);
        }

        // Grief's Veil — the grey folds you from sight; nearby enemies lose nerve
        private static void SpellSelfPurple()
        {
            if (Player == null || Mission.Current == null) return;
            const float Radius = 20f;
            const float Duration = 12f;
            // Drain morale of nearby enemies — they falter and lose aggression
            var halted = new HashSet<Formation>();
            foreach (Agent a in Enemies().Where(a => a.Position.Distance(Player.Position) <= Radius).ToList())
            {
                try
                {
                    BeginAgentGlow(a, ColorSchool.Purple, 1.5f);
                    try { a.SetMorale(0f); } catch { }
                    if (a.Formation != null) halted.Add(a.Formation);
                } catch { }
            }
            // The grey hides the caster — invulnerable while unseen
            if (!_shadowVeilActive)
            {
                try { Player.ToggleInvulnerable(); _shadowVeilActive = true; } catch { }
                ActiveEffectManager.Add(new ActiveEffect
                {
                    Name = "_griefs_veil", Duration = Duration, IsMissionEffect = true,
                    OnExpire = () =>
                    {
                        if (_shadowVeilActive)
                        {
                            try { if (Player?.IsActive() == true) Player.ToggleInvulnerable(); } catch { }
                            _shadowVeilActive = false;
                        }
                        Msg("Grief's Veil lifts. The purple recedes. They see again.", ColorSchool.Purple);
                    }
                });
            }
            BeginAgentGlow(Player, ColorSchool.Purple, 2f);
            string haltedMsg = halted.Count > 0
                ? $" {halted.Count} nearby {(halted.Count == 1 ? "formation pauses" : "formations pause")}."
                : string.Empty;
            Msg($"Grief's Veil — the purple folds you from sight for {(int)Duration}s.{haltedMsg}", ColorSchool.Purple);
        }
    }
}
