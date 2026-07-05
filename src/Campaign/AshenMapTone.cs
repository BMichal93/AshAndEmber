// =============================================================================
// ASH AND EMBER — Campaign/AshenMapTone.cs
// Ambient flavour messages when the player is in or near Ashen-held territory.
// Settlement messages fire on entry; ambient messages fire daily near Ashen land.
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public static class AshenMapTone
    {
        private static int _settlementCooldown = 0;
        private static int _ambientCooldown    = 0;
        private static readonly Random _rng    = new Random();

        private static readonly string[] _settlementMessages =
        {
            "The gate guards look at you too long. This place knows the cold.",
            "The market smells of old ash and cautious silence.",
            "The banners here have changed. No one speaks of who holds this town now.",
            "The people do not meet your eyes when you ask about their lords. Fear is a kind of silence.",
            "Something in this settlement has grown cold. Not the stones.",
            "The inn hearth burns low despite the firewood. Even flames are cautious here.",
            "Children watch you from windows. They have learned to study strangers carefully.",
            "The temple doors are sealed. Faith costs more where the cold holds sway.",
        };

        private static readonly string[] _ambientMessages =
        {
            "The road carries ash this far from any fire. The cold holds this land.",
            "Nearby villages have stopped lighting fires at night.",
            "You pass a field left untended. The harvest rotted where it stood.",
            "A merchant on the road turns back when he sees the banners ahead.",
        };

        public static void ResetForNewGame()
        {
            _settlementCooldown = 0;
            _ambientCooldown    = 0;
        }

        public static void OnSettlementEntered(Settlement settlement)
        {
            if (!MageKnowledge.IsMage || _settlementCooldown > 0 || settlement == null) return;
            try
            {
                Hero owner = settlement.OwnerClan?.Leader;
                if (owner == null || !ColourLordRegistry.IsAshenLord(owner)) return;

                string msg = _settlementMessages[_rng.Next(_settlementMessages.Length)];
                InformationManager.DisplayMessage(new InformationMessage(msg, new Color(0.45f, 0.45f, 0.65f)));
                _settlementCooldown = 3;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void DailyTick()
        {
            if (_settlementCooldown > 0) _settlementCooldown--;
            if (_ambientCooldown    > 0) _ambientCooldown--;

            if (!MageKnowledge.IsMage || _ambientCooldown > 0) return;

            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 pos = MobileParty.MainParty.GetPosition2D;

                bool nearAshen = Settlement.All.Any(s =>
                    (s.IsTown || s.IsCastle) &&
                    s.OwnerClan?.Leader != null &&
                    ColourLordRegistry.IsAshenLord(s.OwnerClan.Leader) &&
                    (s.GetPosition2D - pos).Length <= 30f);

                if (!nearAshen) return;
                if (_rng.Next(5) != 0) return;

                string msg = _ambientMessages[_rng.Next(_ambientMessages.Length)];
                InformationManager.DisplayMessage(new InformationMessage(msg, new Color(0.45f, 0.45f, 0.65f)));
                _ambientCooldown = 5;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
