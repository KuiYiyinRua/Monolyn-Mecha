using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace GuardianAutonomyTests
{
    [StaticConstructorOnStartup]
    static class Entry
    {
        static bool requested;
        static Entry()
        {
            if (GenCommandLine.CommandLineArgPassed("mm-guardian-autonomy-cold"))
                new Harmony("local.mecha.guardianautonomy.cold").Patch(AccessTools.Method(typeof(Root_Entry), "Update"),
                    postfix: new HarmonyMethod(typeof(Entry), nameof(Load)));
            if (!GenCommandLine.CommandLineArgPassed("mm-guardian-autonomy-test")) return;
            var harmony = new Harmony("local.mecha.guardianautonomy.test");
            harmony.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"),
                prefix: new HarmonyMethod(typeof(Entry), nameof(Push)), finalizer: new HarmonyMethod(typeof(Entry), nameof(Pop)));
        }
        static void Push() => Rand.PushState(261018);
        static void Pop() => Rand.PopState();
        static void Load()
        {
            if (requested || LongEventHandler.AnyEventNowOrWaiting || Current.ProgramState != ProgramState.Entry) return;
            requested = true; GameDataSaveLoader.LoadGame("GuardianAutonomyState");
        }
    }

    public sealed class Probe : GameComponent
    {
        readonly bool active = GenCommandLine.CommandLineArgPassed("mm-guardian-autonomy-test");
        readonly bool cold = GenCommandLine.CommandLineArgPassed("mm-guardian-autonomy-cold");
        string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        Pawn owner, worker, enemy;
        readonly List<Pawn_Immortal> units = new List<Pawn_Immortal>();
        Pawn guardian, guardian2;
        CompGuardianField field, field2;
        int stage, started, firstFieldTick, index, startShots, frames;
        bool setup, done, seenRefuel, loadRequested;
        Hediff capacity;
        Thing seedStack, target;
        public Probe(Game game) { }
        void Assert(bool ok, string note)
        {
            File.AppendAllText(Path.Combine(Root, "evidence.txt"), (ok ? "PASS " : "FAILED ") + note + "\n");
            if (!ok) throw new Exception(note);
        }
        static string Hash(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        Pawn Make(string kind, Faction faction, IntVec3 cell)
        {
            var p = PawnGenerator.GeneratePawn(PawnKindDef.Named(kind), faction);
            GenSpawn.Spawn(p, cell, Find.CurrentMap);
            return p;
        }
        void Select(Thing thing) { Find.Selector.ClearSelection(); Find.Selector.Select(thing); }
        void Click(Pawn pawn, string key) => pawn.GetGizmos().OfType<Command_Action>().Single(g => g.defaultLabel == key.Translate()).action();
        void BindFromMenu(Pawn_Immortal unit)
        {
            Select(unit);
            for (int i = 0; i < 2; i++)
            {
                Click(unit, "MM_Autonomy");
                var menu = Find.WindowStack.Windows.OfType<FloatMenu>().Last();
                var options = (List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu), "options").GetValue(menu);
                Assert(options.Any(o => o.Label.StartsWith(owner.LabelShortCap)), unit.def.defName + " menu open=" + i);
                if (i == 1) options.First(o => o.Label.StartsWith(owner.LabelShortCap)).action();
                menu.Close();
            }
        }
        void Draft(Pawn pawn)
        {
            var command = pawn.GetGizmos().OfType<Command_Toggle>().Single(g => g.hotKey == KeyBindingDefOf.Command_ColonistDraft);
            Assert(!command.Disabled, pawn.def.defName + " native draft allowed");
            if (!pawn.Drafted) command.toggleAction();
        }
        void Setup()
        {
            var map = Find.CurrentMap;
            map.fogGrid.ClearAllFog();
            foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray()) p.DeSpawn();
            foreach (var c in new CellRect(30, 30, 65, 65))
            {
                foreach (var t in c.GetThingList(map).ToArray()) { if (t.def.destroyable) t.Destroy(); else t.DeSpawn(); }
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil); map.roofGrid.SetRoof(c, null);
            }
            Assert(Hash(typeof(Pawn_Immortal).Assembly.Location) == File.ReadAllText(Path.Combine(Root, "core.sha256")).Trim(), "loaded candidate hash");
            owner = Make("Colonist", Faction.OfPlayer, new IntVec3(60, 0, 50));
            owner.health.AddHediff(HediffDefOf.MechlinkImplant, owner.health.hediffSet.GetBrain());
            capacity = owner.health.AddHediff(DefDatabase<HediffDef>.GetNamed("MM_TestBandwidth"));
            owner.drafter.Drafted = true;
            worker = Make("Colonist", Faction.OfPlayer, new IntVec3(48, 0, 72));
            worker.workSettings.EnableAndInitialize();
            foreach (var work in DefDatabase<WorkTypeDef>.AllDefs) worker.workSettings.SetPriority(work, 0);
            worker.workSettings.SetPriority(WorkTypeDefOf.Hauling, 1);
            for (int hour = 0; hour < 24; hour++) worker.timetable.SetAssignment(hour, TimeAssignmentDefOf.Work);
            worker.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
            var names = new[] { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor" };
            var costs = new[] { 4, 6, 6, 5 };
            for (int i = 0; i < names.Length; i++)
            {
                var unit = (Pawn_Immortal)Make(names[i], Faction.OfPlayer, new IntVec3(44 + 10 * i, 0, 58));
                units.Add(unit); unit.Light.currentLight = unit.LightMax; unit.autoFire = false;
                if (unit is Pawn_Reaver r) { r.missiles = 10; r.autoFabricate = false; }
                Assert(!unit.Driven && unit.GetStatValue(StatDefOf.BandwidthCost) == costs[i], names[i] + " parked, cost=" + costs[i]);
                if (i == 1)
                {
                    owner.health.RemoveHediff(capacity); owner.mechanitor.Notify_BandwidthChanged();
                    Assert(!MechaAutonomy.CanBind(unit, owner), "insufficient free bandwidth rejects binding");
                    MechaAutonomy.Bind(map, unit, owner);
                    Assert(unit.GetOverseer() == null && !unit.Driven, "rejected bind cannot enable autonomy");
                    capacity = owner.health.AddHediff(DefDatabase<HediffDef>.GetNamed("MM_TestBandwidth")); owner.mechanitor.Notify_BandwidthChanged();
                }
                BindFromMenu(unit);
                Assert(unit.Autonomous && unit.Pilot == null && owner.mechanitor.ControlledPawns.Contains(unit)
                    && unit.GetMechControlGroup() != null, names[i] + " native relation/control group/autonomy");
                Draft(unit);
                var before = unit.Position;
                MechaActions.Move(map, unit, before + IntVec3.North * 3);
                Assert(unit.CurJobDef == JobDefOf.Goto, names[i] + " accepts movement without pilot");
            }
            Assert(owner.mechanitor.UsedBandwidth == 21, "four chassis use exactly 21 bandwidth");
            guardian = Make("MM_Guardian", Faction.OfPlayer, new IntVec3(50, 0, 77));
            guardian2 = Make("MM_Guardian", Faction.OfPlayer, new IntVec3(54, 0, 77));
            foreach (var g in new[] { guardian, guardian2 })
            {
                g.relations.AddDirectRelation(PawnRelationDefOf.Overseer, owner);
                g.GetComp<CompGuardian>().SetAutoCombat(false); g.GetComp<CompGuardian>().SetAutoRestore(false);
                g.needs.energy.CurLevel = g.needs.energy.MaxLevel;
                Draft(g);
            }
            field = guardian.GetComp<CompGuardianField>(); field2 = guardian2.GetComp<CompGuardianField>();
            Assert(guardian.GetStatValue(StatDefOf.BandwidthCost) == 2 && owner.mechanitor.UsedBandwidth == 25, "Guardian bandwidth=2 each");
            field.Activate(); Assert(!field.Active && field.Seeds.Fuel == 0, "empty seeds cannot activate field");
            var fuel = field.Seeds; fuel.TargetFuelLevel = 20; fuel.allowAutoRefuel = false;
            for (int i = 0; i < 3; i++)
            {
                seedStack = ThingMaker.MakeThing(ThingDef.Named("MN_Seed")); seedStack.stackCount = 10;
                GenSpawn.Spawn(seedStack, new IntVec3(47 + i, 0, 73), map);
            }
            var giver = (WorkGiver_Refuel)DefDatabase<WorkGiverDef>.GetNamed("Refuel").Worker;
            Assert(map.listerThings.ThingsInGroup(ThingRequestGroup.Refuelable).Contains(guardian), "native refuel work list contains Guardian pawn");
            Assert(!giver.HasJobOnThing(worker, guardian), "auto hauling off prevents job");
            fuel.allowAutoRefuel = true;
            Assert(giver.HasJobOnThing(worker, guardian), "native refuel work accepts Guardian pawn");
            field2.Seeds.allowAutoRefuel = false;
            worker.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            started = Find.TickManager.TicksGame;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
        }
        void TestReflections()
        {
            var map = guardian.Map;
            enemy = Make("Colonist", Faction.OfAncientsHostile, new IntVec3(32, 0, 77));
            enemy.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 99999), JobCondition.InterruptForced);
            var check = AccessTools.Method(typeof(Projectile), "CheckForFreeInterceptBetween");
            var origin = AccessTools.Field(typeof(Projectile), "origin");
            var destination = AccessTools.Field(typeof(Projectile), "destination");
            var from = new Vector3(41f, 0, 77.5f); var to = new Vector3(43f, 0, 77.5f);
            Assert(GuardianFieldMap.TryCrossing(from, to, guardian.Position.ToVector3Shifted(), 8, out float fraction) && fraction > 0 && fraction < 1, "segment intersects true radius-8 boundary");
            Assert(GuardianFieldMap.TryCrossing(new Vector3(30, 0, 77.5f), new Vector3(70, 0, 77.5f), guardian.Position.ToVector3Shifted(), 8, out _), "fast projectile crossing not skipped");
            Assert(!GuardianFieldMap.TryCrossing(new Vector3(30, 0, 90), new Vector3(70, 0, 90), guardian.Position.ToVector3Shifted(), 8, out _), "outside field is unaffected");
            int reflected = 0, passed = 0;
            for (int i = 0; i < 256; i++)
            {
                var bullet = (Projectile)ThingMaker.MakeThing(ThingDef.Named("Bullet_AssaultRifle"));
                GenSpawn.Spawn(bullet, enemy.Position, map);
                bullet.Launch(enemy, enemy.Position.ToVector3Shifted(), new IntVec3(70, 0, 77), new IntVec3(70, 0, 77), ProjectileHitFlags.All);
                Vector3 oldOrigin = (Vector3)origin.GetValue(bullet);
                bool intercepted = (bool)check.Invoke(bullet, new object[] { from, to });
                if (intercepted)
                {
                    reflected++;
                    if (i < 10) Assert(!bullet.Destroyed && bullet.Launcher == guardian && (Vector3)destination.GetValue(bullet) == oldOrigin.Yto0(), "reflection preserves projectile and reverses to exact launch origin");
                }
                else
                {
                    passed++;
                    Assert(!bullet.Destroyed && bullet.Launcher == enemy, "failed reflection passes projectile " + i);
                    ulong rand = (ulong)AccessTools.Property(typeof(Rand), "StateCompressed").GetValue(null);
                    check.Invoke(bullet, new object[] { from, to });
                    Assert(bullet.Launcher == enemy && (ulong)AccessTools.Property(typeof(Rand), "StateCompressed").GetValue(null) == rand, "same field cannot reroll projectile " + i);
                }
                bullet.Destroy();
            }
            Assert(reflected > 95 && reflected < 165 && passed > 90, "single-field distribution reflected=" + reflected + " passed=" + passed);
            field2.Seeds.Refuel(20); field2.Activate();
            guardian2.jobs.StopAll(); guardian2.pather.StopDead(); guardian2.Position = guardian.Position; guardian2.Notify_Teleported();
            int stacked = 0;
            for (int i = 0; i < 256; i++)
            {
                var bullet = (Projectile)ThingMaker.MakeThing(ThingDef.Named("Bullet_AssaultRifle"));
                GenSpawn.Spawn(bullet, enemy.Position, map);
                bullet.Launch(enemy, new IntVec3(70, 0, 77), new IntVec3(70, 0, 77), ProjectileHitFlags.All);
                if ((bool)check.Invoke(bullet, new object[] { from, to })) stacked++;
                bullet.Destroy();
            }
            Assert(stacked > 166 && stacked < 219, "overlapping fields independently stack reflected=" + stacked);
            var friendly = (Projectile)ThingMaker.MakeThing(ThingDef.Named("Bullet_AssaultRifle"));
            GenSpawn.Spawn(friendly, enemy.Position, map); friendly.Launch(owner, new IntVec3(70, 0, 77), new IntVec3(70, 0, 77), ProjectileHitFlags.All);
            Assert(!(bool)check.Invoke(friendly, new object[] { from, to }) && friendly.Launcher == owner, "friendly projectile unaffected"); friendly.Destroy();
            enemy.DeSpawn();
        }
        void StartAttack()
        {
            if (target != null && !target.Destroyed) target.Destroy();
            var unit = units[index];
            unit.jobs.StopAll(); unit.pather.StopDead();
            target = ThingMaker.MakeThing(ThingDef.Named("MM_TestTarget")); target.SetFaction(Faction.OfAncientsHostile);
            GenSpawn.Spawn(target, unit.Position + IntVec3.North * 7, unit.Map);
            startShots = unit.shotsFired; MechaActions.Attack(unit.Map, unit, target); started = Find.TickManager.TicksGame;
            Assert(unit.attackTarget == target, unit.def.defName + " autonomous attack accepted");
        }
        public override void GameComponentUpdate()
        {
            if ((!active && !cold) || done) return;
            try
            {
                if (File.Exists(Path.Combine(Root, "quit"))) { Application.Quit(); return; }
                if (cold && !loadRequested && Current.ProgramState == ProgramState.Entry && !LongEventHandler.AnyEventNowOrWaiting)
                { loadRequested = true; GameDataSaveLoader.LoadGame("GuardianAutonomyState"); return; }
                if (Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
                if (cold) { ColdCheck(); return; }
                if (!setup) { setup = true; Setup(); return; }
                int now = Find.TickManager.TicksGame;
                if (stage == 0)
                {
                    if (worker.CurJobDef == JobDefOf.Refuel) seenRefuel = true;
                    if (field.Seeds.Fuel < 20)
                    { if (now - started > 5000) Assert(false, "natural refuel timeout job=" + worker.CurJob + " fuel=" + field.Seeds.Fuel); return; }
                    Assert(seenRefuel && field.Seeds.Fuel == 20 && guardian.Map.listerThings.ThingsOfDef(ThingDef.Named("MN_Seed")).Sum(t => t.stackCount) == 10, "natural colonist hauling fills target 20 and consumes exactly 20 seeds");
                    Assert(units.All(u => u.Position.z >= 60), "all four autonomous chassis actually moved");
                    field.Seeds.allowAutoRefuel = false;
                    Select(guardian); Assert(guardian.GetGizmos().OfType<Gizmo_GuardianSeeds>().Count() == 1, "native integer seed slider visible");
                    Click(guardian, "MM_GuardianField");
                    Assert(field.remainingTicks == 1800 && field.Seeds.Fuel == 10, "field instantly activates for 1800 ticks costing 10 seeds");
                    field.Activate(); Assert(field.Seeds.Fuel == 10, "active field cannot charge twice");
                    firstFieldTick = now; TestReflections();
                    stage = 1; index = 0; StartAttack();
                }
                else if (stage == 1)
                {
                    var unit = units[index];
                    if ((unit.shotsFired <= startShots || target.HitPoints == target.MaxHitPoints) && now - started < 1200) return;
                    Assert(unit.shotsFired > startShots && target.HitPoints < target.MaxHitPoints, unit.def.defName + " real autonomous weapon fired and damaged target");
                    MechaActions.Stop(unit.Map, unit);
                    if (++index < units.Count) { StartAttack(); return; }
                    target.Destroy(); stage = 2;
                    foreach (var u in units) u.drafter.Drafted = false;
                    var group = units[0].GetMechControlGroup();
                    group.SetWorkMode(MechWorkModeDefOf.SelfShutdown);
                    Assert(units.All(u => !u.Driven), "native group self-shutdown stops all autonomous chassis");
                    group.SetWorkMode(MechWorkModeDefOf.Escort);
                    Assert(units.All(u => u.Driven), "native group escort enables autonomous drive");
                    group.SetWorkMode(MechWorkModeDefOf.Work);
                    foreach (var u in units) Draft(u);
                    owner.health.RemoveHediff(capacity); owner.mechanitor.Notify_BandwidthChanged();
                    Assert(units.Any(u => !u.Autonomous), "bandwidth loss disconnects native control");
                    foreach (var u in units.Where(u => !u.Autonomous))
                    { Assert(!u.Driven, u.def.defName + " insufficient bandwidth stops autonomous drive"); }
                    capacity = owner.health.AddHediff(DefDatabase<HediffDef>.GetNamed("MM_TestBandwidth")); owner.mechanitor.Notify_BandwidthChanged();
                    Assert(units.All(u => u.Autonomous), "restored bandwidth reconnects chassis");
                    var chassis = units[0]; int used = owner.mechanitor.UsedBandwidth;
                    MechaAutonomy.Unbind(chassis.Map, chassis);
                    Assert(owner.mechanitor.UsedBandwidth == used - 4 && !chassis.Driven && chassis.GetMechControlGroup() == null, "disconnect releases exact bandwidth and native group");
                    MechaAutonomy.Bind(chassis.Map, chassis, owner); Draft(chassis);
                    worker.jobs.StopAll(); worker.pather.StopDead(); worker.Position = chassis.Position + IntVec3.West; worker.Notify_Teleported();
                    MechaActions.OrderBoard(chassis.Map, chassis, worker); started = now;
                }
                else if (stage == 2)
                {
                    if (units[0].Pilot == null && now - started < 1200) return;
                    Assert(units[0].Pilot == worker && units[0].Driven && units[0].GetOverseer() == null && owner.mechanitor.UsedBandwidth == 21, "real boarding switches to pilot and frees 4 bandwidth");
                    stage = 3;
                }
                else if (stage == 3)
                {
                    if (now - firstFieldTick < 1800) return;
                    Assert(field.remainingTicks == 0 && field2.remainingTicks == 0, "both fields expire after 30 seconds of simulation");
                    field.Seeds.Refuel(30); field.Seeds.TargetFuelLevel = 30; field.Seeds.allowAutoRefuel = false;
                    Assert(field.Seeds.Fuel <= 30, "storage capacity clamps to 30");
                    field.Activate(); field2.Seeds.Refuel(30); field2.Activate();
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Select(guardian); CameraJumper.TryJump(guardian);
                    stage = 4;
                }
                else if (stage == 4 && ++frames > 12)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(Root, "GuardianField.png"));
                    File.WriteAllText(Path.Combine(Root, "expected-state.txt"), State());
                    GameDataSaveLoader.SaveGame("GuardianAutonomyState");
                    File.WriteAllText(Path.Combine(Root, "complete"), "PASS"); done = true; Application.Quit();
                }
            }
            catch (Exception e) { done = true; File.WriteAllText(Path.Combine(Root, "failed"), e.ToString()); Log.Error(e.ToString()); Application.Quit(); }
        }
        string State()
        {
            return string.Join("\n", Find.CurrentMap.mapPawns.AllPawnsSpawned.Where(p => p is Pawn_Immortal || p.GetComp<CompGuardianField>() != null).OrderBy(p => p.thingIDNumber).Select(p =>
            { var f = p.GetComp<CompGuardianField>(); return p.ThingID + ":" + p.GetOverseer()?.ThingID + ":" + p.GetMechControlGroup()?.Index
                + ":" + (p is Pawn_Immortal m ? m.Pilot?.ThingID + ":" + m.Driven : f.Seeds.Fuel + ":" + f.Seeds.TargetFuelLevel + ":" + f.Seeds.allowAutoRefuel + ":" + f.remainingTicks); }));
        }
        public override void FinalizeInit()
        { if (cold) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; }
        void ColdCheck()
        {
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            File.WriteAllText(Path.Combine(Root, "actual-state.txt"), State());
            Assert(State() == File.ReadAllText(Path.Combine(Root, "expected-state.txt")), "cold load preserves pilot, binding, groups, seed count/target/toggle and both active fields");
            Assert(Hash(typeof(Pawn_Immortal).Assembly.Location) == File.ReadAllText(Path.Combine(Root, "core.sha256")).Trim(), "cold load candidate hash");
            File.WriteAllText(Path.Combine(Root, "complete"), "PASS"); done = true; Application.Quit();
        }
    }
}
