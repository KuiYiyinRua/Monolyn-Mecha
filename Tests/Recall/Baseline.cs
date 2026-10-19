using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RecallBaseline
{
    public sealed class Probe : GameComponent
    {
        bool done;
        public Probe(Game game) { }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("mm-baseline") || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            done = true;
            string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
            try
            {
                Map map = Find.CurrentMap;
                var owner = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                GenSpawn.Spawn(owner, map.Center, map);
                owner.health.AddHediff(HediffDefOf.MechlinkImplant, owner.health.hediffSet.GetBrain());
                owner.health.AddHediff(HediffDef.Named("MM_TestBandwidth"));
                string[] names = { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor", "MM_Stalker", "MM_Guardian" };
                var units = names.Select(n => PawnGenerator.GeneratePawn(PawnKindDef.Named(n), Faction.OfPlayer)).ToArray();
                foreach (var p in units) p.relations.AddDirectRelation(PawnRelationDefOf.Overseer, owner);
                owner.mechanitor.Notify_BandwidthChanged(); owner.DeSpawn();
                var caravan = CaravanMaker.MakeCaravan(units.Concat(new[] { owner }), Faction.OfPlayer, map.Tile, true);
                Log.Message("RECALL BASELINE core=" + typeof(Pawn_Immortal).Assembly.ManifestModule.ModuleVersionId);
                Log.Message("RECALL ENTRY before=" + string.Join(",", caravan.PawnsListForReading.Select(p => p.ThingID)));
                CaravanEnterMapUtility.Enter(caravan, map, p => IntVec3.Zero);
                var lost = units.Where(p => !p.Spawned).ToArray();
                if (!lost.Any(p => p is Pawn_Colossus) || !lost.Any(p => p is Pawn_Reaver) || !lost.Any(p => p is Pawn_Disruptor)) throw new Exception("baseline did not reproduce large chassis loss");
                string evidence = "REPRODUCED caravanDestroyed=" + caravan.Destroyed + " lost=" + string.Join(",", lost.Select(p => p.ThingID + ":world=" + p.IsWorldPawn() + ":owner=" + p.GetOverseer()?.ThingID + ":holder=" + p.ParentHolder));
                Log.Message("RECALL " + evidence); File.WriteAllText(Path.Combine(root, "baseline.complete"), evidence);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(root, "host.failed"), e.ToString()); Log.Error("RECALL FAILED " + e); }
            Application.Quit();
        }
    }
}
