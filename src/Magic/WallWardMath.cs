// =============================================================================
// ASH AND EMBER — Magic/WallWardMath.cs
//
// Pure logic for elemental wall WARDING — what a standing wall stops, beyond
// its bite. No TaleWorlds types; covered by tests/PureLogicTests.cs.
//
// The elements answer one another the way the world does:
//   Fire wall  — the updraft devours a gale (blocks WIND magic); horses will
//                not face open flame (fear is handled by the fire patches).
//   Wind wall  — turns arrows and bolts aside, and scatters flung stone
//                (blocks MISSILES and EARTH magic).
//   Earth wall — a standing dam of stone (blocks MISSILES and WATER magic).
//   Water wall — quenches flame to steam and drinks the wind's force
//                (blocks FIRE and WIND magic).
//   Spirit     — a ward of the unseen: it stops nothing physical, and no
//                earthly wall stops IT (dread passes stone and steam alike).
//
// The Ashen cold masks (Cold/Storm/Ash/Snow/Void) are the same workings under
// another name and ward identically.
// =============================================================================

namespace AshAndEmber
{
    public static class WallWardMath
    {
        // Walls of driven wind and standing stone stop physical missiles.
        public static bool WallBlocksMissiles(MagicElement wall)
            => wall == MagicElement.Wind || wall == MagicElement.Earth;

        // But they do not stop them EQUALLY. Standing stone is the absolute
        // shield — nothing loosed by hand crosses it. Driven wind only wrestles
        // the shafts aside: most die in the gust, but a heavy few punch through.
        // (Rolled ONCE per arrow at the wall's edge, never re-rolled per tick.)
        public const float WindwallMissileStopChance = 0.65f;
        public static bool WindStopsMissile(double roll)
            => roll < WindwallMissileStopChance;

        // Does a wall of `wall` stop an incoming working of `incoming`?
        public static bool WallBlocksMagic(MagicElement wall, MagicElement incoming)
        {
            switch (wall)
            {
                case MagicElement.Fire:  return incoming == MagicElement.Wind;
                case MagicElement.Wind:  return incoming == MagicElement.Earth;
                case MagicElement.Earth: return incoming == MagicElement.Water;
                case MagicElement.Water: return incoming == MagicElement.Fire
                                             || incoming == MagicElement.Wind;
                default:                 return false;   // Spirit wards, never walls
            }
        }

        // The wall that answers a given element — what a mage raises AGAINST it.
        // (Water stops fire; fire eats wind; wind scatters stone; stone dams water.
        // Nothing walls out the Spirit's dread.)
        public static MagicElement? CounterWallFor(MagicElement incoming)
        {
            switch (incoming)
            {
                case MagicElement.Fire:  return MagicElement.Water;
                case MagicElement.Wind:  return MagicElement.Water; // drinks the gale (fire also serves)
                case MagicElement.Earth: return MagicElement.Wind;
                case MagicElement.Water: return MagicElement.Earth;
                default:                 return null;               // Spirit cannot be walled out
            }
        }

        // Whether a fire missile (a burning crystal shard) is QUENCHED (fizzles,
        // no blast) rather than merely stopped when it meets this wall.
        public static bool QuenchesFireMissile(MagicElement wall) => wall == MagicElement.Water;

        // ── Geometry ─────────────────────────────────────────────────────────────
        // Does the 2D segment A→B pass within `radius` of point C? Used to test a
        // working's line-of-effect (caster → target) against a wall node's circle.
        public static bool SegmentNearPoint(float ax, float ay, float bx, float by,
                                            float cx, float cy, float radius)
        {
            float abx = bx - ax, aby = by - ay;
            float acx = cx - ax, acy = cy - ay;
            float len2 = abx * abx + aby * aby;
            float t = len2 <= 0.0001f ? 0f : (acx * abx + acy * aby) / len2;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            float px = ax + abx * t, py = ay + aby * t;
            float dx = cx - px, dy = cy - py;
            return dx * dx + dy * dy <= radius * radius;
        }

        // The parametric point (0..1) along A→B closest to C — where the working
        // visibly dies against the wall (for the fizzle effect).
        public static float SegmentNearestT(float ax, float ay, float bx, float by,
                                            float cx, float cy)
        {
            float abx = bx - ax, aby = by - ay;
            float len2 = abx * abx + aby * aby;
            if (len2 <= 0.0001f) return 0f;
            float t = ((cx - ax) * abx + (cy - ay) * aby) / len2;
            return t < 0f ? 0f : t > 1f ? 1f : t;
        }
    }
}
