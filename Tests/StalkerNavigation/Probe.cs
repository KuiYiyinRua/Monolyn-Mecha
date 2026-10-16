using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using ASEL;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace StalkerNavigationTests
{
    // Installed only in the isolated test game, and inert without the explicit flag.
    [StaticConstructorOnStartup]
    public static class Entry
    {
        public static readonly bool Navigation = GenCommandLine.CommandLineArgPassed("mm-navigation-test");
        public static readonly bool Mecha = GenCommandLine.CommandLineArgPassed("mm-mecha-test");
        public static readonly bool Cold = GenCommandLine.CommandLineArgPassed("mm-mecha-cold");
        public static readonly bool Active = Navigation || Mecha || Cold;
        public static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        public static Pawn Unit, Enemy;
        public static int Shots, Starts, Blinks, DoorBlinks, BadBlinks, Failures;
        public static int LastShot;
        public static string Case;
        static Entry()
        {
            if (!Active) return;
            var mod = LoadedModManager.RunningMods.Single(m => m.assemblies.loadedAssemblies.Contains(typeof(CompStalker).Assembly));
            string path = Path.Combine(mod.RootDir, "1.6/Assemblies/Monolyn.Mecha.dll");
            string hash;
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (hash != File.ReadAllText(Path.Combine(Root, "core.sha256")).Trim()) throw new Exception("Stale candidate");
            Log.Message("STALKER_NAV BOOT core=" + hash + " mvid=" + typeof(CompStalker).Module.ModuleVersionId);
            var harmony = new Harmony("local.monolyn.stalker.navigation.tests");
            harmony.PatchAll();
            harmony.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"),
                prefix: new HarmonyMethod(typeof(Entry), nameof(Push)), finalizer: new HarmonyMethod(typeof(Entry), nameof(Pop)));
            if (Cold) LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("MechaCombatState"));
        }
        static void Push() => Rand.PushState(261016);
        static void Pop() => Rand.PopState();
        public static void Trace(string note) => File.AppendAllText(Path.Combine(Root, "trace.txt"),
            Find.TickManager.TicksGame + " " + Case + " " + note + "\n");
        public static void Assert(bool ok, string note)
        {
            if (!ok) Failures++;
            string message = (ok ? "PASS " : "FAILED ") + Case + " " + note;
            File.AppendAllText(Path.Combine(Root, "evidence.txt"), message + "\n");
            Log.Message("STALKER_NAV " + message);
        }
    }

    public sealed class Probe : GameComponent
    {
        Map map;
        Pawn owner, unit, enemy;
        CompStalker ai;
        int scenario = -1, ticks, frames, doorStop, maxDoorStop, reversals, lastDx, lastDz;
        int shotGap, maxShotGap;
        IntVec3 previous, goal;
        bool done, prepared, screenshot;
        readonly CellRect area = new CellRect(30, 35, 86, 61);
        readonly string[] names = { "closed-door-patrol", "held-door-patrol", "door-pursuit-walk", "door-pursuit-blink",
            "elbow-pursuit-walk", "elbow-pursuit-blink", "closing-door-retreat-walk", "closing-door-retreat-blink", "deadend-close-attacker" };
        public Probe(Game game) { }
        void Run(Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                done = true;
                File.WriteAllText(Path.Combine(Entry.Root, "failed"), "HARNESS_OR_RUNTIME " + e);
                Log.Error("STALKER_NAV FAILED " + e);
                Application.Quit();
            }
        }
        public override void GameComponentUpdate()
        {
            if (!Entry.Navigation || done || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            Run(() => {
                if (File.Exists(Path.Combine(Entry.Root, "quit"))) { Application.Quit(); return; }
                foreach (var window in Find.WindowStack.Windows.Where(w => w.forcePause).ToArray()) window.Close();
                if (!prepared) { Setup(); prepared = true; Next(); }
                if (screenshot)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    if (++frames == 5) ScreenCapture.CaptureScreenshot(Path.Combine(Entry.Root, Entry.Case + ".png"));
                    if (frames >= 15) { screenshot = false; Next(); }
                }
                else Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            });
        }
        void Setup()
        {
            map = Find.CurrentMap;
            map.fogGrid.ClearAllFog();
            foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToArray())
            {
                if (pawn.IsFreeColonist && pawn.Faction == Faction.OfPlayer)
                {
                    pawn.jobs.StopAll(); pawn.drafter.Drafted = true;
                    pawn.Position = new IntVec3(23, 0, 25); pawn.Notify_Teleported();
                    if (owner == null) owner = pawn;
                }
                else pawn.DeSpawn();
            }
            ClearArea();
            owner.health.AddHediff(HediffDefOf.MechlinkImplant, owner.health.hediffSet.GetBrain());
            var factory = (Building_MonolynProducer)ThingMaker.MakeThing(ThingDef.Named("ConstructFormator"));
            factory.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(factory, new IntVec3(30, 0, 40), map);
            factory.GetComp<CompFormatorOverseer>().tunedTo = owner;
            factory.StartWork(DefDatabase<MonolynRecipeDef>.GetNamed("MM_PrintStalker"), 1);
            var seeds = ThingMaker.MakeThing(ThingDef.Named("MN_Seed")); seeds.stackCount = 30;
            factory.Notify_MaterialReceived(seeds); factory.Finish(); factory.Destroy();
            unit = map.mapPawns.AllPawnsSpawned.Single(p => p.def.defName == "MM_Stalker");
            Entry.Unit = unit; ai = unit.GetComp<CompStalker>(); unit.drafter.Drafted = true;
            enemy = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
            Entry.Enemy = enemy;
            Entry.Case = "setup";
            Entry.Assert(unit.IsColonyMechPlayerControlled && unit.GetOverseer() == owner, "native controlled Stalker");
            Entry.Trace("world=" + Find.World.info.seedString + " map=" + map.uniqueID);
        }
        void ClearArea()
        {
            foreach (var cell in area.Cells)
            {
                map.roofGrid.SetRoof(cell, null);
                foreach (var thing in cell.GetThingList(map).ToArray()) if (!(thing is Pawn))
                { if (thing.def.destroyable) thing.Destroy(); else thing.DeSpawn(); }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
            }
        }
        void Wall(int x, int z)
        {
            var cell = new IntVec3(x, 0, z);
            if (cell.GetEdifice(map) != null) return;
            var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDef.Named("BlocksGranite"));
            wall.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(wall, cell, map);
        }
        Building_Door Door(int x, int z, bool held, bool open)
        {
            var door = (Building_Door)ThingMaker.MakeThing(ThingDefOf.Door, ThingDef.Named("BlocksGranite"));
            door.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(door, new IntVec3(x, 0, z), map);
            Traverse.Create(door).Field("holdOpenInt").SetValue(held);
            if (open) door.StartManualOpenBy(unit);
            return door;
        }
        void Corridor()
        {
            for (int x = 34; x <= 110; x++) { Wall(x, 59); Wall(x, 61); }
            Wall(34, 60); Wall(110, 60);
        }
        void Put(Pawn pawn, IntVec3 cell)
        {
            if (!pawn.Spawned) GenSpawn.Spawn(pawn, cell, map);
            else { pawn.Position = cell; pawn.Notify_Teleported(); }
        }
        void Next()
        {
            if (++scenario == names.Length)
            {
                done = true;
                File.WriteAllText(Path.Combine(Entry.Root, Entry.Failures == 0 ? "complete" : "failed"),
                    "SCENARIOS=" + names.Length + " ASSERTION_FAILURES=" + Entry.Failures);
                Application.Quit(); return;
            }
            ai.SetAutoCombat(false); unit.jobs.StopAll(); unit.stances.CancelBusyStanceSoft();
            if (enemy.Spawned) enemy.DeSpawn();
            ClearArea();
            Entry.Case = names[scenario]; Entry.Shots = Entry.Starts = Entry.Blinks = Entry.DoorBlinks = Entry.BadBlinks = 0;
            Entry.LastShot = Find.TickManager.TicksGame;
            ticks = frames = reversals = maxDoorStop = doorStop = lastDx = lastDz = shotGap = maxShotGap = 0;
            unit.needs.energy.CurLevel = unit.needs.energy.MaxLevel;
            ai.Blink.ResetCooldown(); ai.autoBlink = scenario < 2 || scenario % 2 == 1;
            if (scenario < 2)
            {
                Corridor(); Door(60, 60, scenario == 1, scenario == 1);
                Put(unit, new IntVec3(40, 0, 60)); goal = new IntVec3(42, 0, 60);
                unit.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, new IntVec3(105, 0, 60)));
                unit.jobs.jobQueue.EnqueueLast(JobMaker.MakeJob(JobDefOf.Goto, goal));
            }
            else
            {
                if (scenario < 4)
                {
                    // Two enclosed rooms with a single slow, automatically closing door.
                    for (int x = 35; x <= 95; x++) { Wall(x, 40); Wall(x, 80); }
                    for (int z = 41; z < 80; z++) { Wall(35, z); Wall(95, z); if (z != 60) Wall(60, z); }
                    Door(60, 60, false, false);
                    Put(unit, new IntVec3(50, 0, 60)); Put(enemy, new IntVec3(69, 0, 60));
                }
                else if (scenario < 6)
                {
                    // One-cell L passage: neither a diagonal shortcut nor a firing lane through its wall exists.
                    var walk = new HashSet<IntVec3>();
                    for (int x = 40; x <= 60; x++) walk.Add(new IntVec3(x, 0, 50));
                    for (int z = 50; z <= 84; z++) walk.Add(new IntVec3(60, 0, z));
                    foreach (var cell in walk.ToArray()) foreach (var delta in GenAdj.AdjacentCells)
                        if (!walk.Contains(cell + delta)) Wall(cell.x + delta.x, cell.z + delta.z);
                    Put(unit, new IntVec3(40, 0, 50)); Put(enemy, new IntVec3(60, 0, 79));
                }
                else if (scenario < 8)
                {
                    Corridor(); Door(60, 60, false, true);
                    Put(unit, new IntVec3(64, 0, 60)); Put(enemy, new IntVec3(76, 0, 60));
                }
                else
                {
                    Corridor(); Wall(63, 60);
                    Put(unit, new IntVec3(64, 0, 60)); Put(enemy, new IntVec3(67, 0, 60));
                }
                enemy.jobs.StopAll();
                ai.SetAutoCombat(true); ai.enemy = enemy;
                unit.jobs.StartJob(JobMaker.MakeJob(StalkerDefOf.MM_StalkerCombat, enemy));
                Entry.Assert(ai.MayHunt && ai.ValidEnemy(enemy), "real autonomous combat enabled");
            }
            previous = unit.Position;
            Find.CameraDriver.SetRootPosAndSize(new Vector3(61.5f, 0f, 60.5f), 22f);
            Entry.Trace("BEGIN pos=" + unit.Position + " foe=" + (enemy.Spawned ? enemy.Position.ToString() : "none") + " blink=" + ai.autoBlink);
        }
        public override void GameComponentTick()
        {
            if (!Entry.Navigation || done || !prepared || screenshot) return;
            Run(() => {
                ticks++;
                if (unit.Position.GetDoor(map) != null && !unit.pather.Moving) doorStop++; else doorStop = 0;
                maxDoorStop = Math.Max(maxDoorStop, doorStop);
                if (unit.Position != previous)
                {
                    int dx = Math.Sign(unit.Position.x - previous.x), dz = Math.Sign(unit.Position.z - previous.z);
                    if (dx * lastDx + dz * lastDz < 0) reversals++;
                    lastDx = dx; lastDz = dz; previous = unit.Position;
                }
                if (scenario >= 2 && ticks > 600)
                {
                    shotGap = Find.TickManager.TicksGame - Entry.LastShot;
                    maxShotGap = Math.Max(maxShotGap, shotGap);
                }
                if (ticks % 15 == 0)
                    Entry.Trace("STATE age=" + ticks + " pos=" + unit.Position + " dest=" + unit.pather.Destination
                        + " moving=" + unit.pather.Moving + " mode=" + Traverse.Create(ai).Field("combatMove").GetValue()
                        + " busy=" + unit.stances.FullBodyBusy + " shot=" + (enemy.Spawned && ai.Laser.CanHitTarget(enemy))
                        + " shots=" + Entry.Shots + " path=" + unit.pather.curPath?.NodesLeftCount);
                if (ticks < 2400) return;
                Entry.Assert(Entry.BadBlinks == 0, "every blink follows remaining forward path; bad=" + Entry.BadBlinks);
                Entry.Assert(Entry.DoorBlinks == 0, "no blink lands in a doorway; actual=" + Entry.DoorBlinks);
                if (scenario < 2)
                {
                    Entry.Assert(unit.Position == goal && !unit.pather.Moving, "patrol and queued return complete; pos=" + unit.Position);
                    Entry.Assert(Entry.Blinks > 0, "automatic blink actually used; count=" + Entry.Blinks);
                }
                else
                {
                    Entry.Assert(Entry.Shots >= 8 && maxShotGap < 600, "sustained real fire; shots=" + Entry.Shots + " maxGap=" + maxShotGap);
                    Entry.Assert(reversals <= 8, "bounded direction reversals; actual=" + reversals);
                    Entry.Assert(maxDoorStop < 180, "no persistent firing stop in doorway; ticks=" + maxDoorStop);
                    if (scenario == 8) Entry.Assert(unit.Position == new IntVec3(64, 0, 60), "blocked retreat fires in place instead of crossing attacker");
                }
                Entry.Trace("END reversals=" + reversals + " starts=" + Entry.Starts + " blinks=" + Entry.Blinks + " doorStop=" + maxDoorStop);
                screenshot = true; frames = 0;
            });
        }
    }
    [HarmonyPatch(typeof(Verb_Shoot), "TryCastShot")]
    public static class Shots
    {
        static void Postfix(Verb_Shoot __instance, bool __result)
        { if (__result && __instance.CasterPawn == Entry.Unit) { Entry.Shots++; Entry.LastShot = Find.TickManager.TicksGame; } }
    }
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.JobTrackerTickInterval))]
    public static class StationaryEnemy { static bool Prefix(Pawn ___pawn) => ___pawn != Entry.Enemy; }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class DurableEnemy
    {
        static bool Prefix(Thing __instance, ref DamageWorker.DamageResult __result)
        { if (__instance != Entry.Enemy) return true; __result = new DamageWorker.DamageResult(); return false; }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    public static class Paths
    {
        static void Prefix(Pawn ___pawn, LocalTargetInfo dest)
        { if (___pawn == Entry.Unit) { Entry.Starts++; Entry.Trace("PATH pos=" + ___pawn.Position + " dest=" + dest); } }
    }
    [HarmonyPatch(typeof(CompStalker), nameof(CompStalker.TryMovementBlink))]
    public static class Blinks
    {
        static void Prefix(CompStalker __instance, out IntVec3[] __state)
        {
            var path = __instance.Pawn.pather?.curPath;
            __state = path == null ? new IntVec3[0] : Enumerable.Range(0, path.NodesLeftCount).Select(path.Peek).ToArray();
        }
        static void Postfix(CompStalker __instance, bool __result, IntVec3[] __state)
        {
            if (!__result || __instance.Pawn != Entry.Unit) return;
            Entry.Blinks++;
            if (!__state.Contains(__instance.Pawn.Position)) Entry.BadBlinks++;
            if (__instance.Pawn.Position.GetDoor(__instance.Pawn.Map) != null) Entry.DoorBlinks++;
            Entry.Trace("BLINK to=" + __instance.Pawn.Position + " dest=" + __instance.Pawn.pather.Destination);
        }
    }
}
