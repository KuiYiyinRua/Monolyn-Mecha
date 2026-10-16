using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Monolyn.Mecha
{
    internal static class MechaGeometry
    {
        // Pawn.DrawPos includes unsaved renderer jitter, leaning and interpolation.
        // Combat geometry must depend only on the entity's simulation state.
        public static Vector3 Center(Thing thing)
        {
            var rect = thing.OccupiedRect();
            return new Vector3((rect.minX + rect.maxX + 1) * .5f,
                thing.def.altitudeLayer.AltitudeFor(), (rect.minZ + rect.maxZ + 1) * .5f);
        }

        static readonly IComparer<Thing> ById = Comparer<Thing>.Create((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));

        public static Thing[] CellThings(IntVec3 cell, Map map)
        {
            // Damage can despawn things; snapshot before processing, and never use
            // thing-grid insertion order, which can change after a saved-game join.
            var things = cell.GetThingList(map).ToArray();
            Array.Sort(things, ById);
            return things;
        }
    }
}
