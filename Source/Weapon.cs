using System.Linq;
using ASEL;
using RimWorld;
using Verse;

namespace Monolyn.Mecha
{
    // Retain the native monochrome projectile mesh/trail. Only the impact footprint changes.
    public sealed class Projectile_ImmortalMissile : TrailProjectileNewB
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (blockedByShield) { Destroy(); return; }
            Map map = Map;
            IntVec3 cell = Position;
            var target = intendedTarget.Thing;
            Destroy();
            if (map == null || !cell.InBounds(map)) return;
            var cells = CellRect.CenteredOn(cell, 1).Cells.Where(c => (c.x == cell.x || c.z == cell.z) && c.InBounds(map)).ToList();
            def.GetModExtension<ASELModExtension>()?.impactEffecter?.Spawn(new TargetInfo(cell, map), new TargetInfo(cell, map)).Cleanup();
            GenExplosion.DoExplosion(cell, map, 1f, def.projectile.damageDef, launcher,
                damAmount: 10, armorPenetration: 1f, projectile: def, intendedTarget: target,
                chanceToStartFire: 0f, damageFalloff: false, applyDamageToExplosionCellsNeighbors: false,
                overrideCells: cells);
        }
    }
}
