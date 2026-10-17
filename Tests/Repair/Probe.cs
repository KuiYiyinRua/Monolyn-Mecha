using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RepairTests
{
    public sealed class Probe : GameComponent
    {
        readonly bool active = GenCommandLine.CommandLineArgPassed("mm-repair-test");
        string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        readonly string[] kinds = { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor" };
        Pawn worker;
        Pawn_Immortal mech;
        int index = -1, started;
        bool setup, done, observed;
        public Probe(Game game) { }
        void Assert(bool ok, string message)
        {
            File.AppendAllText(Path.Combine(Root, "evidence.txt"), (ok ? "PASS " : "FAILED ") + message + "\n");
            if (!ok) throw new Exception(message);
        }
        public override void GameComponentUpdate()
        {
            if (!active || done || Current.ProgramState != ProgramState.Playing) return;
            try
            {
                if (!setup)
                {
                    setup = true;
                    var map = Find.CurrentMap;
                    map.fogGrid.ClearAllFog();
                    foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray()) p.DeSpawn();
                    foreach (var c in new CellRect(40, 40, 30, 30))
                    {
                        foreach (var t in c.GetThingList(map).ToArray()) { if (t.def.destroyable) t.Destroy(); else t.DeSpawn(); }
                        map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
                    }
                    worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    GenSpawn.Spawn(worker, new IntVec3(50, 0, 50), map);
                    worker.health.AddHediff(HediffDefOf.MechlinkImplant, worker.health.hediffSet.GetBrain());
                    worker.workSettings.EnableAndInitialize();
                    foreach (var w in DefDatabase<WorkTypeDef>.AllDefs) worker.workSettings.SetPriority(w, 0);
                    worker.workSettings.SetPriority(WorkTypeDefOf.Smithing, 1);
                    worker.timetable.SetAssignment(GenLocalDate.HourOfDay(worker), TimeAssignmentDefOf.Work);
                    Assert(MechanitorUtility.IsMechanitor(worker), "worker is a real mechanitor");
                    Assert(File.ReadAllText(Path.Combine(Root, "core.sha256")).Trim() == Hash(typeof(Pawn_Immortal).Assembly.Location), "loaded candidate hash");
                    Next();
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                }
                if (done) return;
                if (worker.CurJobDef?.defName == "MM_RepairWarframe") observed = true;
                if (!MechRepairUtility.CanRepair(mech))
                {
                    Assert(observed, kinds[index] + " automatically selected repair job");
                    Assert(mech.Hull == mech.HullMax, kinds[index] + " fully repaired with no battery");
                    Next();
                }
                else AssertTimeout();
            }
            catch (Exception e) { done = true; File.WriteAllText(Path.Combine(Root, "failed"), e.ToString()); Application.Quit(); }
        }
        void AssertTimeout()
        {
            if (Find.TickManager.TicksGame - started > 6000)
                Assert(false, kinds[index] + " timed out job=" + worker.CurJobDef + " hull=" + mech.Hull);
        }
        void Next()
        {
            if (mech != null) mech.DeSpawn();
            if (++index == kinds.Length)
            {
                done = true; File.WriteAllText(Path.Combine(Root, "complete"), "PASS automatic repair four warframes"); Application.Quit(); return;
            }
            mech = (Pawn_Immortal)PawnGenerator.GeneratePawn(PawnKindDef.Named(kinds[index]), Faction.OfPlayer);
            GenSpawn.Spawn(mech, new IntVec3(54, 0, 50), Find.CurrentMap);
            var injury = HediffMaker.MakeHediff(HediffDefOf.Cut, mech, mech.RaceProps.body.corePart);
            injury.Severity = 3; mech.health.AddHediff(injury);
            Assert(mech.needs.energy == null, kinds[index] + " has no vanilla energy need");
            var giver = (WorkGiver_RepairMech)DefDatabase<WorkGiverDef>.GetNamed("RepairMech").Worker;
            var repair = mech.GetComp<CompMechRepairable>();
            repair.autoRepair = false;
            Assert(!giver.HasJobOnThing(worker, mech), kinds[index] + " respects disabled auto repair");
            Assert(giver.HasJobOnThing(worker, mech, true), kinds[index] + " manual repair allowed");
            repair.autoRepair = true;
            Assert(!giver.ShouldSkip(worker) && giver.HasJobOnThing(worker, mech), kinds[index] + " auto repair eligible");
            mech.SetFaction(Faction.OfMechanoids);
            Assert(!giver.HasJobOnThing(worker, mech), kinds[index] + " enemy target excluded");
            mech.SetFaction(Faction.OfPlayer);
            worker.jobs.EndCurrentJob(JobCondition.InterruptForced);
            started = Find.TickManager.TicksGame; observed = false;
        }
        static string Hash(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
