// =============================================================================
// ASH AND EMBER — Nature/NatureChargeBar.cs
// The Living Ember channel bar: a small element-coloured bar above the ability
// row that fills while you stand still and focus, then vanishes the moment the
// charge is gathered. Driven from the mission tick; battle only.
//
// Purely additive overlay (reuses BlankWhiteSquare_9 + a tint, like the splash).
// Everything is guarded — a missing prefab or engine change degrades to no bar,
// never a crash.
// =============================================================================

using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace AshAndEmber
{
    // Bound data for the bar prefab.
    public class NatureChargeBarVM : ViewModel
    {
        private int    _fillWidth;
        private string _fillColor = "#7BC86BFF";

        [DataSourceProperty]
        public int FillWidth
        {
            get => _fillWidth;
            set { if (value != _fillWidth) { _fillWidth = value; OnPropertyChangedWithValue(value, "FillWidth"); } }
        }

        [DataSourceProperty]
        public string FillColor
        {
            get => _fillColor;
            set { if (value != _fillColor) { _fillColor = value; OnPropertyChangedWithValue(value, "FillColor"); } }
        }
    }

    internal static class NatureChargeBar
    {
        private const int TrackInnerWidth = 312;   // track 320 minus padding

        private static bool _active;
        private static GauntletLayer           _layer;
        private static ScreenBase              _host;
        private static NatureChargeBarVM       _vm;
        private static GauntletMovieIdentifier _movie;

        public static void Tick(float dt)
        {
            try
            {
                bool channelling = NatureKnowledge.IsAttuned
                                && Mission.Current != null
                                && NatureCharge.IsChannelling;

                if (channelling)
                {
                    if (!_active) Show();
                    if (_vm != null)
                    {
                        _vm.FillWidth = (int)(NatureCharge.FillProgress01 * TrackInnerWidth);
                        _vm.FillColor = ElementColor(NatureCharge.PreviewElement(true));
                    }
                }
                else if (_active) Hide();
            }
            catch
            {
                try { Hide(); } catch { }
            }
        }

        private static string ElementColor(NatureElement el)
        {
            switch (el)
            {
                case NatureElement.Wind:  return "#BFE6FFFF";  // pale sky
                case NatureElement.Earth: return "#5BCB4BFF";  // living green
                case NatureElement.Water: return "#3F8BE0FF";  // river blue
                case NatureElement.Storm: return "#B79BFFFF";  // violet charge
                default:                  return "#CFCFCFFF";
            }
        }

        private static void Show()
        {
            _host = ScreenManager.TopScreen;
            if (_host == null) return;
            _vm    = new NatureChargeBarVM();
            _layer = new GauntletLayer("AshEmberNatureBar", 1000, false);  // above the mission HUD
            _movie = _layer.LoadMovie("AshEmberNatureBar", _vm);
            _host.AddLayer(_layer);
            _active = true;
        }

        private static void Hide()
        {
            _active = false;
            try
            {
                if (_host != null && _layer != null && _host.HasLayer(_layer))
                    _host.RemoveLayer(_layer);
            }
            catch { }
            try { _layer?.ReleaseMovie(_movie); } catch { }
            _layer = null;
            _host  = null;
            _vm    = null;
        }

        public static void Reset() => Hide();
    }
}
