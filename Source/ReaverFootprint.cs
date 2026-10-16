using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Monolyn.Mecha
{
    public static class ReaverFootprint
    {
        public static bool Applies(Thing thing) => thing is Pawn_Reaver || thing is Pawn_Disruptor;
        public static bool Clear(Map map, IntVec3 anchor, Rot4 facing, Pawn_Immortal self,
            bool rejectPawns = false, bool clipMapEdge = false)
        {
            if (map == null || !anchor.InBounds(map)) return false;
            foreach (var cell in GenAdj.OccupiedRect(anchor, facing, self.def.size))
            {
                if (!cell.InBounds(map)) { if (clipMapEdge) continue; return false; }
                if (!cell.WalkableBy(map, self)) return false;
                var building = cell.GetEdifice(map);
                if (building != null && (building.BlocksPawn(self) || building is Building_Door door && !door.FreePassage)) return false;
                if (rejectPawns)
                    foreach (var thing in cell.GetThingList(map)) if (thing is Pawn && thing != self) return false;
            }
            return true;
        }

        public static Rot4 Facing(IntVec3 from, IntVec3 to, Rot4 fallback)
            => from == to ? fallback : Rot4.FromAngleFlat((to - from).AngleFlat);

        public static bool NearEdge(IntVec3 c, Map map)
            => c.x < 2 || c.z < 2 || c.x >= map.Size.x - 2 || c.z >= map.Size.z - 2;

        // Path states include facing: a 2x3 chassis can use a two-cell passage in the matching direction.
        // Keep the native pather/jobs and terrain movement costs; only choose a route that fits the body.
        public static PawnPath Find(Pawn_Immortal pawn, IntVec3 start, LocalTargetInfo target, PathEndMode mode)
        {
            var map = pawn.Map;
            if (map == null || !start.InBounds(map) || !target.IsValid || !target.Cell.InBounds(map)) return PawnPath.NotFound;
            bool edge = NearEdge(target.Cell, map);
            int states = map.cellIndices.NumGridCells * 4;
            var costs = new int[states];
            var previous = new int[states];
            var clear = new byte[states];
            var queue = new SortedSet<SearchNode>(new SearchComparer());
            int serial = 0, root = map.cellIndices.CellToIndex(start) * 4 + pawn.Rotation.AsInt;
            costs[root] = 1; previous[root] = -1;
            queue.Add(new SearchNode(root, 1, Distance(start, target.Cell), serial++));
            while (queue.Count > 0)
            {
                var node = queue.Min; queue.Remove(node);
                if (costs[node.state] != node.cost) continue;
                var cell = map.cellIndices.IndexToCell(node.state / 4);
                if (ReachabilityImmediate.CanReachImmediate(cell, target, map, mode, pawn))
                {
                    var result = map.pawnPathPool.GetPath(); int count = 0;
                    for (int state = node.state; state >= 0; state = previous[state])
                    { result.AddNode(map.cellIndices.IndexToCell(state / 4)); count++; }
                    PathCost(result) = node.cost; PathCursor(result) = count - 1; PathUsed(result) = true;
                    return result;
                }
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var next = cell + new IntVec3(dx, 0, dz);
                    if (!next.InBounds(map)) continue;
                    var facing = Facing(cell, next, new Rot4(node.state % 4));
                    int id = map.cellIndices.CellToIndex(next) * 4 + facing.AsInt;
                    if (!Fits(cell, facing) || !Fits(next, facing)) continue;
                    if (dx != 0 && dz != 0 && (!Fits(cell + new IntVec3(dx, 0, 0), facing)
                        || !Fits(cell + new IntVec3(0, 0, dz), facing))) continue;
                    int cost = node.cost + (dx != 0 && dz != 0 ? 14 : 10);
                    if (costs[id] != 0 && costs[id] <= cost) continue;
                    costs[id] = cost; previous[id] = node.state;
                    queue.Add(new SearchNode(id, cost, cost + Distance(next, target.Cell), serial++));
                }
            }
            return PawnPath.NotFound;

            bool Fits(IntVec3 cell, Rot4 facing)
            {
                if (!cell.InBounds(map)) return false;
                int id = map.cellIndices.CellToIndex(cell) * 4 + facing.AsInt;
                if (clear[id] == 0) clear[id] = (byte)(Clear(map, cell, facing, pawn, clipMapEdge: edge) ? 1 : 2);
                return clear[id] == 1;
            }
        }

        static int Distance(IntVec3 a, IntVec3 b)
        { int x = Math.Abs(a.x - b.x), z = Math.Abs(a.z - b.z); return 10 * Math.Max(x, z) + 4 * Math.Min(x, z); }
        struct SearchNode
        {
            public int state, cost, priority, serial;
            public SearchNode(int state, int cost, int priority, int serial)
            { this.state = state; this.cost = cost; this.priority = priority; this.serial = serial; }
        }
        sealed class SearchComparer : IComparer<SearchNode>
        {
            public int Compare(SearchNode a, SearchNode b)
            { int result = a.priority.CompareTo(b.priority); return result == 0 ? a.serial.CompareTo(b.serial) : result; }
        }
        static readonly AccessTools.FieldRef<PawnPath, float> PathCost = AccessTools.FieldRefAccess<PawnPath, float>("totalCostInt");
        static readonly AccessTools.FieldRef<PawnPath, int> PathCursor = AccessTools.FieldRefAccess<PawnPath, int>("curNodeIndex");
        static readonly AccessTools.FieldRef<PawnPath, bool> PathUsed = AccessTools.FieldRefAccess<PawnPath, bool>("inUse");
    }

    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.PushRequest))]
    static class ReaverPathRequest
    {
        static bool Prefix(PathRequest request)
        {
            if (!(request.pawn is Pawn_Immortal pawn) || !ReaverFootprint.Applies(pawn)) return true;
            request.Resolve(ReaverFootprint.Find(pawn, request.Start, request.Target, request.EndMode)); return false;
        }
    }
    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.FindPathNow), new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathFinderCostTuning?), typeof(PathEndMode), typeof(PathRequest.IPathGridCustomizer) })]
    static class ReaverImmediatePath
    {
        static bool Prefix(IntVec3 start, LocalTargetInfo target, TraverseParms traverseParms, PathEndMode peMode, ref PawnPath __result)
        {
            if (!(traverseParms.pawn is Pawn_Immortal pawn) || !ReaverFootprint.Applies(pawn)) return true;
            __result = ReaverFootprint.Find(pawn, start, target, peMode); return false;
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.Rotation), MethodType.Setter)]
    static class ReaverTurnClearance
    {
        static bool Prefix(Thing __instance, Rot4 value)
            => !(__instance is Pawn_Immortal pawn) || !ReaverFootprint.Applies(pawn) || !pawn.Spawned || value == pawn.Rotation
            || ReaverFootprint.Clear(pawn.Map, pawn.Position, value, pawn, clipMapEdge: true);
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    static class ReaverStepClearance
    {
        static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn, IntVec3 ___nextCell)
        {
            if (!(___pawn is Pawn_Immortal pawn) || !ReaverFootprint.Applies(pawn) || ___nextCell == pawn.Position) return true;
            var facing = ReaverFootprint.Facing(pawn.Position, ___nextCell, pawn.Rotation);
            if (ReaverFootprint.Clear(pawn.Map, pawn.Position, facing, pawn, clipMapEdge: true)
                && ReaverFootprint.Clear(pawn.Map, ___nextCell, facing, pawn, clipMapEdge: true)) return true;
            // A new obstruction may have appeared after path calculation. Never enter it with half the chassis.
            __instance.StopDead(); pawn.jobs.EndCurrentJob(JobCondition.Incompletable); return false;
        }
    }
    // Six-cell registration is clipped only while reaching a map exit, just like the larger Colossus.
    [HarmonyPatch(typeof(ThingGrid), "RegisterInCell")]
    static class ReaverEdgeRegistration
    { static bool Prefix(Thing t, IntVec3 c, Map ___map) => !ReaverFootprint.Applies(t) || c.InBounds(___map); }
    [HarmonyPatch(typeof(ThingGrid), "DeregisterInCell")]
    static class ReaverEdgeDeregistration
    { static bool Prefix(Thing t, IntVec3 c, Map ___map) => !ReaverFootprint.Applies(t) || c.InBounds(___map); }
}
