using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
namespace Monolyn.Mecha
{
    public static class ColossusFootprint
    {
        public static bool Clear(Map map, IntVec3 center, Pawn self = null, bool rejectPawns = false)
        {
            if (map == null || !center.InBounds(map)) return false;
            if (rejectPawns) foreach (var cell in CellRect.CenteredOn(center, 1))
                if (cell.InBounds(map)) foreach (var t in cell.GetThingList(map)) if (t is Pawn p && p != self) return false;
            return true;
        }
        // Native pather/jobs, direct phase-striding route. Ground registration remains 3x3.
        public static PawnPath Find(Pawn_Colossus pawn, IntVec3 start, LocalTargetInfo target, PathEndMode mode)
        {
            Map map = pawn.Map;
            if (map == null || !start.InBounds(map) || !target.IsValid || !target.Cell.InBounds(map)) return PawnPath.NotFound;
            var nodes = new System.Collections.Generic.List<IntVec3> { start };
            var current = start;
            while (!ReachabilityImmediate.CanReachImmediate(current, target, map, mode, pawn))
            {
                if (current == target.Cell) break;
                current += new IntVec3(Math.Sign(target.Cell.x - current.x), 0, Math.Sign(target.Cell.z - current.z));
                nodes.Add(current);
            }
            var path = map.pawnPathPool.GetPath();
            for (int i = nodes.Count - 1; i >= 0; i--) path.AddNode(nodes[i]);
            PathCost(path) = nodes.Count * 7.5f; PathCursor(path) = nodes.Count - 1; PathUsed(path) = true;
            return path;
        }
        static readonly AccessTools.FieldRef<PawnPath, float> PathCost = AccessTools.FieldRefAccess<PawnPath, float>("totalCostInt");
        static readonly AccessTools.FieldRef<PawnPath, int> PathCursor = AccessTools.FieldRefAccess<PawnPath, int>("curNodeIndex");
        static readonly AccessTools.FieldRef<PawnPath, bool> PathUsed = AccessTools.FieldRefAccess<PawnPath, bool>("inUse");
    }
    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.PushRequest))]
    static class ColossusPathRequest
    {
        static bool Prefix(PathRequest request)
        {
            if (!(request.pawn is Pawn_Colossus pawn)) return true;
            request.Resolve(ColossusFootprint.Find(pawn, request.Start, request.Target, request.EndMode)); return false;
        }
    }
    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.FindPathNow), new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathFinderCostTuning?), typeof(PathEndMode), typeof(PathRequest.IPathGridCustomizer) })]
    static class ColossusImmediatePath
    {
        static bool Prefix(IntVec3 start, LocalTargetInfo target, TraverseParms traverseParms, PathEndMode peMode, ref PawnPath __result)
        {
            if (!(traverseParms.pawn is Pawn_Colossus pawn)) return true;
            __result = ColossusFootprint.Find(pawn, start, target, peMode); return false;
        }
    }
    // StartPath's preliminary reachability drops its pawn argument. Scope only that call.
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    static class ColossusPathScope
    {
        [ThreadStatic] public static Pawn_Colossus Current;
        static void Prefix(Pawn ___pawn, out Pawn_Colossus __state) { __state = Current; Current = ___pawn as Pawn_Colossus; }
        static void Finalizer(Pawn_Colossus __state) { Current = __state; }
    }
    [HarmonyPatch(typeof(Reachability), nameof(Reachability.CanReach), new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })]
    static class ColossusReachability
    {
        static bool Prefix(Map ___map, IntVec3 start, LocalTargetInfo dest, TraverseParms traverseParams, ref bool __result)
        {
            var pawn = traverseParams.pawn as Pawn_Colossus ?? ColossusPathScope.Current;
            if (pawn == null || pawn.Map != ___map) return true;
            __result = start.InBounds(___map) && dest.IsValid && dest.Cell.InBounds(___map) && (!dest.HasThing || dest.Thing.Spawned && dest.Thing.Map == ___map); return false;
        }
    }
    [HarmonyPatch(typeof(GenGrid), nameof(GenGrid.WalkableBy))]
    static class ColossusWalkability
    {
        static bool Prefix(IntVec3 c, Map map, Pawn pawn, ref bool __result)
        { if (!(pawn is Pawn_Colossus)) return true; __result = c.InBounds(map); return false; }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), "PawnCanOccupy")]
    static class ColossusOccupancy
    {
        static bool Prefix(Pawn ___pawn, IntVec3 c, ref bool __result)
        { if (!(___pawn is Pawn_Colossus)) return true; __result = c.InBounds(___pawn.Map); return false; }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), "BuildingBlockingNextPathCell")]
    static class ColossusWalls
    {
        static bool Prefix(Pawn ___pawn, ref Building __result)
        { if (!(___pawn is Pawn_Colossus)) return true; __result = null; return false; }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.NextCellDoorToWaitForOrManuallyOpen))]
    static class ColossusDoors
    {
        static bool Prefix(Pawn ___pawn, ref Building_Door __result)
        { if (!(___pawn is Pawn_Colossus)) return true; __result = null; return false; }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), "WillCollideWithPawnAt")]
    static class ColossusPawnCollision
    {
        static bool Prefix(Pawn ___pawn, ref bool __result)
        { if (!(___pawn is Pawn_Colossus)) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), "NeedNewPath")]
    static class ColossusPathStability
    {
        static bool Prefix(Pawn ___pawn, PawnPath ___curPath, LocalTargetInfo ___destination, IntVec3 ___lastPathedTargetPosition, ref bool __result)
        {
            if (!(___pawn is Pawn_Colossus)) return true;
            __result = !___destination.IsValid || ___curPath == null || !___curPath.Found || ___curPath.NodesLeftCount == 0 || ___lastPathedTargetPosition != ___destination.Cell; return false;
        }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new Type[] { typeof(Pawn), typeof(IntVec3) })]
    static class ColossusMoveCost
    {
        static bool Prefix(Pawn pawn, IntVec3 c, ref float __result)
        { if (!(pawn is Pawn_Colossus)) return true; __result = c.x != pawn.Position.x && c.z != pawn.Position.z ? 10.606602f : 7.5f; return false; }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), "CostToPayThisTick")]
    static class ColossusIgnoresSlowdown
    {
        static bool Prefix(Pawn ___pawn, ref float __result)
        { if (!(___pawn is Pawn_Colossus)) return true; __result = 1f; return false; }
    }
    // Full footprint inside the map, clipped registration at caravan exit edges.
    [HarmonyPatch(typeof(ThingGrid), "RegisterInCell")]
    static class ColossusEdgeRegistration
    { static bool Prefix(Thing t, IntVec3 c, Map ___map) => !(t is Pawn_Colossus) || c.InBounds(___map); }
    [HarmonyPatch(typeof(ThingGrid), "DeregisterInCell")]
    static class ColossusEdgeDeregistration
    { static bool Prefix(Thing t, IntVec3 c, Map ___map) => !(t is Pawn_Colossus) || c.InBounds(___map); }
}
