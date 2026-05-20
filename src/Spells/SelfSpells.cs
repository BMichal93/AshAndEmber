// =============================================================================
// COLOURS OF CALRADIA — SelfSpells.cs
// Mount & Blade II: Bannerlord Mod  v1.2.0.0.0
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

        // Scarlet Ward — absorbs the next single blow; expires after 12 s if nothing hits
        private static void SpellSelfRed()
        {
            if (Player == null || !Player.IsActive()) return;
            if (_scarletWardActive) { Msg("Scarlet Ward is already active.", ColorSchool.Red); return; }
            const float Duration = 12f;
            _scarletWardActive = true;
            BeginAgentGlow(Player, ColorSchool.Red, Duration);
            SpawnTempLight(Player.Position, ColorSchool.Red, 6f, 4f);
            ActiveEffectManager.Add(new ActiveEffect
            {
                Name = "_scarlet_ward", Duration = Duration, IsMissionEffect = true,
                OnExpire = () =>
                {
                    if (_scarletWardActive)
                    {
                        _scarletWardActive = false;
                        Msg("The Scarlet Ward dissipates.", ColorSchool.Red);
                    }
                }
            });
            Msg("Scarlet Ward — the next blow will find iron, not flesh. Lasts 12 seconds.", ColorSchool.Red);
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

        // Called from OnAgentHit — deflects one missile; shatters after 3 blocks
        public static void AbsorbCeruleanMissile(int absorbed)
        {
            if (!_ceruleanMirrorActive || Player == null || !Player.IsActive()) return;
            if (absorbed > 0)
                try { Player.Health = Math.Min(Player.HealthLimit, Player.Health + absorbed); } catch { }
            _ceruleanMirrorBlocks--;
            if (_ceruleanMirrorBlocks <= 0)
            {
                _ceruleanMirrorActive = false;
                _ceruleanMirrorBlocks = 0;
                BeginAgentGlow(Player, ColorSchool.Blue, 1f);
                Msg("The Cerulean Mirror shatters — four volleys turned.", ColorSchool.Blue);
            }
            else
            {
                BeginAgentGlow(Player, ColorSchool.Blue, 0.5f);
            }
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
                    SpawnTempLight(a.Position, ColorSchool.Orange, 6f, 1.5f);
                }
                catch { }
                angle += step;
            }
            BeginAgentGlow(Player, ColorSchool.Orange, 1.5f);
            SpawnTempLight(Player.Position, ColorSchool.Orange, 6f, 1.5f);
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
            SpawnTempLight(Player.Position, ColorSchool.Yellow, 6f, 1.5f);
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
            float heal = Math.Min(28f * power, Player.HealthLimit - Player.Health);
            Player.Health = Math.Min(Player.Health + 28f * power, Player.HealthLimit);
            BeginAgentGlow(Player, ColorSchool.Green, 1.5f);
            SpawnTempLight(Player.Position, ColorSchool.Green, 6f, 1.5f);
            Msg($"Verdant Touch — you restore {heal:F0} HP.", ColorSchool.Green);
        }

        // Cerulean Mirror — 18-second magic immunity + up to 4 missile blocks, then shatters
        private static void SpellSelfBlue()
        {
            if (Player == null || !Player.IsActive()) return;
            if (_ceruleanMirrorActive) { Msg("Cerulean Mirror is already active.", ColorSchool.Blue); return; }
            const float Duration = 18f;
            _ceruleanMirrorActive = true;
            _ceruleanMirrorBlocks = 4;
            BeginAgentGlow(Player, ColorSchool.Blue, Duration);
            SpawnTempLight(Player.Position, ColorSchool.Blue, 6f, 4f);
            ActiveEffectManager.Add(new ActiveEffect
            {
                Name = "_cerulean_mirror", Duration = Duration, IsMissionEffect = true,
                OnExpire = () =>
                {
                    if (_ceruleanMirrorActive)
                    {
                        _ceruleanMirrorActive = false;
                        _ceruleanMirrorBlocks = 0;
                        Msg("The Cerulean Mirror dims. Volleys find you again.", ColorSchool.Blue);
                    }
                }
            });
            Msg("Cerulean Mirror — missiles deflected for 18 seconds or 4 volleys. Steel still finds flesh.", ColorSchool.Blue);
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
                    SpawnTempLight(a.Position, ColorSchool.Purple, 6f, 1.5f);
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
                            // Post-veil: 2s stagger — the world snaps back hard
                            try { if (Player?.IsActive() == true) Player.SetMaximumSpeedLimit(0f, false); } catch { }
                            try { if (Player?.IsActive() == true) DamageAgent(Player, 1f); } catch { }
                            ActiveEffectManager.Add(new ActiveEffect
                            {
                                Name = "_veil_aftermath", Duration = 2f, IsMissionEffect = true,
                                OnExpire = () =>
                                {
                                    try { if (Player?.IsActive() == true) Player.SetMaximumSpeedLimit(10f, false); } catch { }
                                    Msg("The grey recedes. Your limbs return to you.", ColorSchool.Purple);
                                }
                            });
                            Msg("Grief's Veil lifts. They see you again — and for a moment, you cannot move.", ColorSchool.Purple);
                        }
                        else
                        {
                            Msg("Grief's Veil lifts. The purple recedes.", ColorSchool.Purple);
                        }
                    }
                });
            }
            BeginAgentGlow(Player, ColorSchool.Purple, 12f);
            SpawnTempLight(Player.Position, ColorSchool.Purple, 6f, 1.5f);
            string haltedMsg = halted.Count > 0
                ? $" {halted.Count} nearby {(halted.Count == 1 ? "formation pauses" : "formations pause")}."
                : string.Empty;
            Msg($"Grief's Veil — the purple folds you from sight for 12s.{haltedMsg}", ColorSchool.Purple);
        }
    }
}
