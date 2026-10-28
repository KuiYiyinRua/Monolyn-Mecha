using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Monolyn.Mecha;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace CombatNavigationTests
{
    [StaticConstructorOnStartup]
    static class Entry
    {
        public static readonly bool Cold = GenCommandLine.CommandLineArgPassed("mm-combat-navigation-cold");
        public static readonly bool Seed = GenCommandLine.CommandLineArgPassed("mm-combat-navigation-seed");
        public static readonly bool Active = Cold || GenCommandLine.CommandLineArgPassed("mm-combat-navigation-test");
        public static readonly bool Baseline = GenCommandLine.CommandLineArgPassed("mm-combat-navigation-baseline");
        public static Pawn Unit, Target;
        public static int Hits, Starts;
        public static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        static Entry()
        {
            if (!Active) return;
            var h = new Harmony("local.monolyn.combatnavigation.probe");
            h.PatchAll();
            h.Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"), prefix: new HarmonyMethod(typeof(Entry), nameof(Push)),
                finalizer: new HarmonyMethod(typeof(Entry), nameof(Pop)));
            if (Cold) LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("CombatNavigationPursuit"));
        }
        static void Push(ref string seedString) { seedString = "CombatNavigation261028"; Rand.PushState(261028); }
        static void Pop() => Rand.PopState();
    }

    public sealed class Probe : GameComponent
    {
        readonly string[] kinds = { "MM_Guardian", "MM_Stalker", "MM_Immortal", "MM_Reaver", "MM_Disruptor", "MM_Colossus" };
        Map map;
        Faction faction;
        Pawn unit, target;
        int scenario = -1, ticks, shotsAt, reversals, lastDx, lastDz, expected, assertions, frames;
        IntVec3 previous;
        bool prepared, done, capture, manualReached;
        int closeHits;
        string Case => Entry.Cold ? "guardian-cold-pursuit" : scenario < 6 ? kinds[scenario] + "-wall-detour" : new[] { "guardian-closing-door", "guardian-deadend", "guardian-lost-target", "guardian-support", "guardian-manual-order", "guardian-channel-retreat", "guardian-failed-path" }[scenario - 6];
        public Probe(Game game) { }
        public override void FinalizeInit() { if (Entry.Cold) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; }
        string MovementState(CompGuardian ai)
        {
            var fields = new[] { "combatMove", "retreating", "lastCombatPosition", "lastCombatProgressTick", "nextCombatRepath", "blockedEnemy", "blockedEnemyUntil", "nextDecision", "nextScan" };
            return unit.ThingID + "|" + unit.Position + "|" + unit.CurJob?.def.defName + "|" + ai.enemy?.ThingID + "|"
                + string.Join("|", fields.Select(f => AccessTools.Field(typeof(CompGuardian), f).GetValue(ai)?.ToString() ?? "null"));
        }
        void Trace(string note) => File.AppendAllText(Path.Combine(Entry.Root, "trace.txt"), Find.TickManager.TicksGame + " " + Case + " " + note + "\n");
        void Check(bool ok, string note, bool regression = false)
        {
            bool oldFailure = !ok && Entry.Baseline && regression;
            File.AppendAllText(Path.Combine(Entry.Root, "evidence.txt"), (ok ? "PASS " : oldFailure ? "EXPECTED_BASELINE_FAILURE " : "FAILED ") + Case + " " + note + "\n");
            assertions++;
            if (oldFailure) expected++;
            else if (!ok) throw new Exception(Case + " " + note);
        }
        void Guard(Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                done = true; File.WriteAllText(Path.Combine(Entry.Root, "failed"), e.ToString());
                Log.Error("MM_NAV FAILED " + e); Application.Quit();
            }
        }
        public override void GameComponentUpdate()
        {
            if (!Entry.Active || done || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            Guard(() => {
                if (File.Exists(Path.Combine(Entry.Root, "quit"))) { done = true; Application.Quit(); return; }
                foreach (var w in Find.WindowStack.Windows.Where(w => w.forcePause).ToArray()) w.Close();
                if (!prepared)
                {
                    map = Find.CurrentMap; map.fogGrid.ClearAllFog();
                    var mod = LoadedModManager.RunningMods.Single(m => m.assemblies.loadedAssemblies.Contains(typeof(Pawn_Immortal).Assembly));
                    string hash;
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                    using (var s = File.OpenRead(Path.Combine(mod.RootDir, "1.6/Assemblies/Monolyn.Mecha.dll"))) hash = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "");
                    if (hash != File.ReadAllText(Path.Combine(Entry.Root, "core.sha256")).Trim()) throw new Exception("stale candidate");
                    Log.Message("MM_NAV BOOT hash=" + hash + " mvid=" + typeof(Pawn_Immortal).Module.ModuleVersionId + " version=" + VersionControl.CurrentVersionStringWithRev);
                    faction = Find.FactionManager.AllFactions.First(f => f.def.defName == "UrielCult" && f.HostileTo(Faction.OfPlayer));
                    prepared = true;
                    if (Entry.Cold)
                    {
                        unit = map.mapPawns.AllPawnsSpawned.Single(p => p.def.defName == "MM_Guardian");
                        target = (Pawn)unit.GetComp<CompGuardian>().enemy; Entry.Unit = unit; Entry.Target = target;
                        Check(MovementState(unit.GetComp<CompGuardian>()) == File.ReadAllText(Path.Combine(Entry.Root, "expected-state.txt")), "movement, enemy and timers survive cold load");
                        Check(unit.pather.Moving && target?.Spawned == true, "cold load resumes an active pursuit");
                    }
                    else { if (Entry.Seed) scenario = 5; Next(); }
                }
                if (capture)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    if (++frames == 5) ScreenCapture.CaptureScreenshot(Path.Combine(Entry.Root, Case + ".png"));
                    if (frames >= 12) { capture = false; Next(); }
                }
                else Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            });
        }
        Pawn Spawn(string kind, Faction f, int x, int z)
        {
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named(kind), f, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            GenSpawn.Spawn(p, new IntVec3(x, 0, z), map); return p;
        }
        void Wall(int x, int z)
        {
            if (new IntVec3(x, 0, z).GetEdifice(map) != null) return;
            var w = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel); w.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(w, new IntVec3(x, 0, z), map);
        }
        void Next()
        {
            foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray())
            { p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ExitedMap); p.jobs.StopAll(); p.DeSpawn(); }
            if (++scenario == 13 || (Entry.Seed && scenario == 7))
            {
                if (Entry.Baseline && expected == 0) throw new Exception("baseline did not reproduce regression");
                File.WriteAllText(Path.Combine(Entry.Root, "complete"), "assertions=" + assertions + " expectedFailures=" + expected);
                done = true; Application.Quit(); return;
            }
            foreach (var c in new CellRect(30, 30, 120, 100))
            {
                map.roofGrid.SetRoof(c, null);
                foreach (var t in c.GetThingList(map).ToArray()) { if (t.def.destroyable) t.Destroy(); else t.DeSpawn(); }
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
            }
            unit = Spawn(scenario < 6 ? kinds[scenario] : "MM_Guardian", scenario == 10 ? Faction.OfPlayer : faction, 65, 80);
            target = Spawn("Colonist", scenario == 10 ? faction : Faction.OfPlayer, 89, 80);
            Entry.Unit = unit; Entry.Target = target; Entry.Hits = Entry.Starts = 0;
            if (unit.GetComp<CompGuardian>() is CompGuardian guardian) { guardian.autoRestore = false; guardian.autoCombat = true; }
            if (unit.GetComp<CompStalker>() is CompStalker stalker) { stalker.autoBlink = false; stalker.autoCombat = true; }
            if (scenario < 6)
            {
                // The nearest enemy is within nominal range but a tall wall requires a
                // detour outside the Guardian's old ten-cell local scoring radius.
                for (int z = 49; z <= 111; z++) Wall(77, z);
            }
            else if (scenario == 6)
            {
                for (int x = 48; x <= 109; x++) { Wall(x, 60); Wall(x, 100); }
                for (int z = 61; z < 100; z++) { Wall(48, z); Wall(109, z); if (z != 80) Wall(77, z); }
                var door = (Building_Door)ThingMaker.MakeThing(ThingDefOf.Door, ThingDef.Named("BlocksGranite"));
                door.SetFaction(faction); GenSpawn.Spawn(door, new IntVec3(77, 0, 80), map);
            }
            else if (scenario == 7)
            {
                for (int x = 64; x <= 69; x++) { Wall(x, 79); Wall(x, 81); }
                Wall(64, 80); Wall(69, 80); target.Position = new IntVec3(68, 0, 80); target.Notify_Teleported();
            }
            else if (scenario == 9)
            {
                target.DeSpawn();
                target = Spawn("MM_Immortal", faction, 89, 80); Entry.Target = target;
                var ally = (Pawn_Immortal)target; ally.Shield.currentHitPoints = 5; ally.autoFire = false;
                unit.GetComp<CompGuardian>().autoRestore = true;
                for (int z = 70; z <= 90; z++) Wall(77, z);
            }
            if (scenario == 10)
            {
                var owner = Spawn("Colonist", Faction.OfPlayer, 56, 74);
                owner.health.AddHediff(HediffDefOf.MechlinkImplant, owner.health.hediffSet.GetBrain());
                unit.relations.AddDirectRelation(PawnRelationDefOf.Overseer, owner); unit.drafter.Drafted = true;
                owner.jobs.StartJob(new Job(JobDefOf.Wait) { expiryInterval = 10000, playerForced = true });
                Check(unit.IsColonyMechPlayerControlled && unit.GetComp<CompGuardian>().MayAct, "real controlled and drafted player Guardian");
            }
            else
            {
                LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false, canTimeoutOrFlee: false, canSteal: false), map, new[] { unit });
                Check(EnemyMechaRaids.MayFight(unit), "real assault lord permits combat");
            }
            Check(unit is Pawn_Immortal m ? m.Driven && m.Powered : unit.GetComp<CompGuardian>()?.Operational ?? unit.GetComp<CompStalker>().Operational, "operational native raid unit");
            ticks = shotsAt = reversals = lastDx = lastDz = frames = closeHits = 0; manualReached = false; previous = unit.Position;
            Find.CameraDriver.SetRootPosAndSize(new Vector3(77, 0, 80), 38f);
            Trace("BEGIN id=" + unit.ThingID + " target=" + target.ThingID);
        }
        public override void GameComponentTick()
        {
            if (!Entry.Active || !prepared || done || capture) return;
            Guard(() => {
                ticks++;
                foreach (var p in new[] { unit, target })
                {
                    if (p.needs.energy != null) p.needs.energy.CurLevel = p.needs.energy.MaxLevel;
                    if (p is Pawn_Immortal m) { m.Light.currentLight = m.LightMax; if (m is Pawn_Reaver r) r.missiles = Pawn_Reaver.MagazineCapacity; }
                }
                if (Entry.Hits > 0 && shotsAt == 0) shotsAt = ticks;
                if (Entry.Cold)
                {
                    if (ticks < 2400) return;
                    Check(Entry.Hits >= 5, "cold-loaded pursuit reaches real beam damage hits=" + Entry.Hits);
                    Check(Entry.Starts <= 8, "cold-loaded pursuit stays committed paths=" + Entry.Starts);
                    File.WriteAllText(Path.Combine(Entry.Root, "complete"), "assertions=" + assertions);
                    done = true; Application.Quit(); return;
                }
                if (unit.Position != previous)
                {
                    int dx = Math.Sign(unit.Position.x - previous.x), dz = Math.Sign(unit.Position.z - previous.z);
                    if (dx * lastDx + dz * lastDz < 0) reversals++;
                    lastDx = dx; lastDz = dz; previous = unit.Position;
                }
                if (ticks % 60 == 0) Trace("STATE age=" + ticks + " pos=" + unit.Position + " dest=" + unit.pather.Destination + " moving=" + unit.pather.Moving + " hits=" + Entry.Hits + " starts=" + Entry.Starts + " job=" + unit.CurJob?.def.defName + " busy=" + unit.stances.FullBodyBusy);
                if (scenario == 8 && ticks == 240) { target.DeSpawn(); Trace("TARGET_REMOVED"); }
                if (scenario == 10 && ticks == 240)
                {
                    unit.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, new IntVec3(50, 0, 80)));
                    Check(!unit.GetComp<CompGuardian>().MayAct, "player-forced Goto suspends automatic combat");
                }
                if (scenario == 10 && ticks > 240 && !manualReached)
                {
                    if (unit.Position == new IntVec3(50, 0, 80)) manualReached = true;
                    else if (unit.CurJob?.def != JobDefOf.Goto) throw new Exception("manual movement interrupted before arrival");
                }
                if (scenario == 6 && ticks == 120 && !Entry.Baseline)
                {
                    Check(unit.pather.Moving, "snapshot captured during pursuit");
                    // Earlier fixture actors were deliberately despawned, rather than
                    // placed in WorldPawns. Their scenario roster/log references do not
                    // belong in the isolated cold-load input.
                    Current.Game.Info.startingAndOptionalPawns.Clear();
                    Find.BattleLog.Battles.Clear();
                    foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead.ToArray()) p.relations?.ClearAllRelations();
                    File.WriteAllText(Path.Combine(Entry.Root, "expected-state.txt"), MovementState(unit.GetComp<CompGuardian>()));
                    GameDataSaveLoader.SaveGame("CombatNavigationPursuit");
                }
                if (scenario == 11 && ticks == 240)
                {
                    Check(unit.GetComp<CompGuardian>().Laser.Bursting, "attacker approaches during continuous beam");
                    closeHits = Entry.Hits; target.Position = unit.Position + new IntVec3(8, 0, 0); target.Notify_Teleported();
                }
                if (scenario == 12 && ticks == 120)
                {
                    unit.jobs.curDriver.Notify_PatherFailed();
                    Check(unit.GetComp<CompGuardian>().FindEnemy(true) != target, "failed path temporarily excludes target", true);
                }
                if (scenario == 12 && ticks == 430)
                    Check(unit.GetComp<CompGuardian>().FindEnemy(true) == target, "failed target becomes eligible after bounded retry");
                int duration = scenario < 6 ? 1800 : scenario == 6 ? 2400 : 1200;
                if (ticks < duration) return;
                if (scenario < 8)
                {
                    Check(Entry.Hits >= 5, "actual weapon hits=" + Entry.Hits + " first=" + shotsAt + " pos=" + unit.Position, scenario == 0 || scenario == 6);
                    Check(Entry.Starts <= 24, "bounded committed paths=" + Entry.Starts, scenario == 0 || scenario == 6);
                    Check(reversals <= 12, "bounded reversals=" + reversals, scenario == 0 || scenario == 6);
                    if (scenario == 7) Check(unit.Position == new IntVec3(65, 0, 80), "no retreat walks toward attacker", true);
                }
                else if (scenario == 8)
                {
                    var ai = unit.GetComp<CompGuardian>();
                    Trace("REMOVED_TARGET old=" + target.ThingID + " enemy=" + ai.enemy?.ThingID + " beam=" + ai.beamTarget?.ThingID
                        + " validReplacement=" + (ai.enemy != null && ai.ValidEnemy(ai.enemy)));
                    Check(ai.enemy != target && ai.beamTarget != target
                        && (ai.enemy != null ? ai.ValidEnemy(ai.enemy) : unit.CurJob?.def != GuardianDefOf.MM_GuardianCombat),
                        "removed target releases beam and permits valid replacement");
                }
                else if (scenario == 9) Check(unit.GetComp<CompGuardian>().totalRestored >= 25 && unit.GetComp<CompGuardian>().raidSupportLight < 600, "support detours and uses real projector reserve");
                else if (scenario == 10) Check(manualReached, "manual movement completes before automatic combat resumes");
                else if (scenario == 11)
                {
                    Check(unit.Position.DistanceTo(target.Position) >= 19f, "continuous beam yields to retreat distance=" + unit.Position.DistanceTo(target.Position));
                    Check(Entry.Hits - closeHits >= 10, "beam resumes after retreat hits=" + (Entry.Hits - closeHits));
                    Check(Entry.Starts <= 8 && reversals <= 4, "retreat thresholds prevent repeated switching");
                }
                else Check(Entry.Hits > 5, "combat resumes after failed-target delay");
                Trace("END hits=" + Entry.Hits + " starts=" + Entry.Starts + " reversals=" + reversals);
                capture = true;
            });
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    static class TargetDamage
    {
        static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (!Entry.Active || __instance != Entry.Target) return true;
            if (dinfo.Instigator == Entry.Unit) Entry.Hits++;
            __result = new DamageWorker.DamageResult(); return false;
        }
    }
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.JobTrackerTickInterval))]
    static class HoldTarget { static bool Prefix(Pawn ___pawn) => !Entry.Active || ___pawn != Entry.Target; }
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    static class CountPaths { static void Prefix(Pawn ___pawn) { if (Entry.Active && ___pawn == Entry.Unit) Entry.Starts++; } }
    [HarmonyPatch(typeof(Storyteller), nameof(Storyteller.StorytellerTick))]
    static class HoldStoryteller { static bool Prefix() => !Entry.Active; }
    [HarmonyPatch(typeof(GenTemperature), nameof(GenTemperature.GetTemperatureForCell))]
    static class Temperature { static bool Prefix(ref float __result) { if (!Entry.Active) return true; __result = 21; return false; } }
}
