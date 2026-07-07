// =============================================================================
// ASH AND EMBER — Magic/ElementResonanceBar.cs
//
// The visual half of the Resonance Draw (see ElementSpellMinigame /
// ElementResonanceMath): a charge track on the campaign map whose fill sweeps
// back and forth, with a single highlighted band marking the element's own
// "true" point. Driven from the map tick; map only. Modelled directly on
// Nature/NatureChargeBar.cs (same prefab shape, same guarded show/hide) —
// the band reuses that exact single-overlay-widget trick rather than
// introducing a second, untested one, to keep the prefab's risk surface small.
//
// Purely additive overlay (BlankWhiteSquare_9 + tints). Everything is guarded —
// a missing prefab or engine change degrades to no bar, never a crash.
// =============================================================================

using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace AshAndEmber
{
    // Bound data for the bar prefab.
    public class ElementResonanceVM : ViewModel
    {
        private string _prompt = "";
        private int    _fillWidth;
        private string _fillColor   = "#D8BA86FF";
        private int    _zoneLeftPx;
        private int    _zoneWidthPx;
        private string _zoneColor = "#FFE9B0FF";

        [DataSourceProperty]
        public string Prompt
        {
            get => _prompt;
            set { if (value != _prompt) { _prompt = value; OnPropertyChangedWithValue(value, "Prompt"); } }
        }

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

        [DataSourceProperty]
        public int ZoneLeftPx
        {
            get => _zoneLeftPx;
            set { if (value != _zoneLeftPx) { _zoneLeftPx = value; OnPropertyChangedWithValue(value, "ZoneLeftPx"); } }
        }

        [DataSourceProperty]
        public int ZoneWidthPx
        {
            get => _zoneWidthPx;
            set { if (value != _zoneWidthPx) { _zoneWidthPx = value; OnPropertyChangedWithValue(value, "ZoneWidthPx"); } }
        }

        [DataSourceProperty]
        public string ZoneColor
        {
            get => _zoneColor;
            set { if (value != _zoneColor) { _zoneColor = value; OnPropertyChangedWithValue(value, "ZoneColor"); } }
        }
    }

    internal static class ElementResonanceBar
    {
        private const int TrackInnerWidth = 312;   // track 320 minus padding — matches NatureChargeBar

        private static bool _active;
        private static GauntletLayer            _layer;
        private static ScreenBase               _host;
        private static ElementResonanceVM       _vm;
        private static GauntletMovieIdentifier   _movie;

        public static void Show(MagicElement el)
        {
            try
            {
                if (_active) return;
                _host = ScreenManager.TopScreen;
                if (_host == null) return;

                _vm = new ElementResonanceVM
                {
                    Prompt    = Prompt(el),
                    FillColor = FillColorFor(el),
                    ZoneColor = "#FFE9B0AA",
                };
                float center   = ElementResonanceMath.ZoneCenter(el);
                float half     = ElementResonanceMath.PerfectHalfWidth(el);
                int   zoneLeft  = ClampPx((center - half) * TrackInnerWidth);
                int   zoneRight = ClampPx((center + half) * TrackInnerWidth);
                _vm.ZoneLeftPx  = zoneLeft;
                _vm.ZoneWidthPx = zoneRight - zoneLeft;

                _layer = new GauntletLayer("AshEmberResonanceBar", 1000, false);
                _movie = _layer.LoadMovie("AshEmberResonanceBar", _vm);
                _host.AddLayer(_layer);
                _active = true;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); Hide(); }
        }

        // sweepPos01 is the live 0..1 position of the resonance sweep.
        public static void UpdateFill(float sweepPos01)
        {
            if (!_active || _vm == null) return;
            _vm.FillWidth = ClampPx(sweepPos01 * TrackInnerWidth);
        }

        public static void Hide()
        {
            _active = false;
            try
            {
                if (_host != null && _layer != null && _host.HasLayer(_layer))
                    _host.RemoveLayer(_layer);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { _layer?.ReleaseMovie(_movie); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            _layer = null;
            _host  = null;
            _vm    = null;
        }

        private static int ClampPx(float px)
        {
            if (px < 0f) return 0;
            if (px > TrackInnerWidth) return TrackInnerWidth;
            return (int)px;
        }

        private static string FillColorFor(MagicElement el)
        {
            switch (el)
            {
                case MagicElement.Wind:   return "#BFE6FFFF";
                case MagicElement.Earth:  return "#5BCB4BFF";
                case MagicElement.Water:  return "#3F8BE0FF";
                case MagicElement.Spirit: return "#B79BFFFF";
                default:                  return "#E08A3DFF"; // Fire
            }
        }

        private static string Prompt(MagicElement el)
        {
            switch (el)
            {
                case MagicElement.Wind:   return "Let the gust rise and fall. Loose it at its crest. [ALT]";
                case MagicElement.Earth:  return "Let it settle like ash. Loose it once it steadies. [ALT]";
                case MagicElement.Water:  return "Slow your breath. Loose it when the tide is calm. [ALT]";
                case MagicElement.Spirit: return "Send it beyond sight. Loose it at the farthest reach. [ALT]";
                default:                  return "Pull the fire high. Loose it at its height. [ALT]";
            }
        }
    }
}
