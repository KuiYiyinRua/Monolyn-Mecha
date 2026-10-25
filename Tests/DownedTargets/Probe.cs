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

namespace DownedTargetTests
{
    [StaticConstructorOnStartup]
    static class WorldSeed
    {
        static WorldSeed()
        {
            if (!GenCommandLine.CommandLineArgPassed("mm-downed-test")) return;
            new Harmony("local.monolyn.downedprobe.seed").Patch(AccessTools.Method(typeof(WorldGenerator), "GenerateWorld"),
                prefix: new HarmonyMethod(typeof(WorldSeed), nameof(Push)), finalizer: new HarmonyMethod(typeof(WorldSeed), nameof(Pop)));
        }
        static void Push(ref string seedString) { seedString = "ManualDownedTargets261025"; Rand.PushState(261025); }
        static void Pop() => Rand.PopState();
    }
    // The test DLL is loaded only in an isolated game and is inert without this flag.
    public sealed class Probe : GameComponent
    {
        readonly bool active = GenCommandLine.CommandLineArgPassed("mm-downed-test");
        readonly bool baseline = GenCommandLine.CommandLineArgPassed("mm-downed-baseline");
        readonly string[] kinds = { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor", "MM_Stalker", "MM_Guardian" };
        string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        Map map;
        Faction enemyFaction;
        Pawn unit, standing, downed, friendly;
        int index, started, expectedFailures;
        bool prepared, done;
        public Probe(Game game) { }

        void Check(bool ok, string message, bool missingManualAttack = false)
        {
            bool expected = !ok && baseline && missingManualAttack;
            string line = (ok ? "PASS " : expected ? "EXPECTED_BASELINE_FAILURE " : "FAIL ") + kinds[index] + " " + message;
            File.AppendAllText(Path.Combine(Root, "evidence.txt"), line + "\n");
            Log.Message("MM_DOWNED " + line);
            if (expected) expectedFailures++;
            else if (!ok) throw new Exception(message);
        }
        void Guard(Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                done = true;
                File.WriteAllText(Path.Combine(Root, "failed"), e.ToString());
                Log.Error("MM_DOWNED FAILED " + e);
                Application.Quit();
            }
        }
        public override void GameComponentUpdate()
        {
            if (!active || done || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            Guard(() => {
                if (File.Exists(Path.Combine(Root, "quit"))) { done = true; Application.Quit(); return; }
                foreach (var window in Find.WindowStack.Windows.Where(w => w.forcePause).ToArray()) window.Close();
                if (!prepared)
                {
                    prepared = true; map = Find.CurrentMap;
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                    using (var stream = File.OpenRead(typeof(Pawn_Immortal).Assembly.Location))
                        Check(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") == File.ReadAllText(Path.Combine(Root, "core.sha256")).Trim(), "loaded frozen candidate");
                    Log.Message("MM_DOWNED BOOT mvid=" + typeof(Pawn_Immortal).Module.ModuleVersionId + " game=" + VersionControl.CurrentVersionStringWithRev);
                    map.fogGrid.ClearAllFog();
                    foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray()) p.DeSpawn();
                    foreach (var cell in new CellRect(65, 65, 50, 40))
                    {
                        foreach (var thing in cell.GetThingList(map).ToArray()) if (thing.def.destroyable) thing.Destroy();
                        map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                        map.roofGrid.SetRoof(cell, null);
                    }
                    enemyFaction = Find.FactionManager.AllFactions.First(f => !f.IsPlayer && f.HostileTo(Faction.OfPlayer) && f.def.humanlikeFaction);
                    Setup();
                }
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            });
        }
        Pawn Spawn(string kind, Faction faction, int x, int z)
        {
            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named(kind), faction, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            GenSpawn.Spawn(pawn, new IntVec3(x, 0, z), map);
            pawn.jobs.StartJob(new Job(JobDefOf.Wait) { expiryInterval = 10000, playerForced = true }, JobCondition.InterruptForced);
            return pawn;
        }
        void Setup()
        {
            unit = Spawn(kinds[index], Faction.OfPlayer, 80, 80);
            if (unit is Pawn_Immortal warframe)
            {
                warframe.autoFire = false;
                warframe.Light.currentLight = warframe.Light.maxLightP;
                if (warframe is Pawn_Reaver reaver)
                {
                    reaver.AdvanceFabrication(Pawn_Reaver.ManufactureTicks * Pawn_Reaver.MagazineCapacity);
                    Check(reaver.missiles == Pawn_Reaver.MagazineCapacity, "native factory fills test magazine");
                }
                var pilot = Spawn("Colonist", Faction.OfPlayer, 79, 80);
                Check(warframe.Board(pilot) && warframe.Driven, "healthy pilot boarded");
                unit.jobs.StartJob(new Job(JobDefOf.Wait) { expiryInterval = 10000, playerForced = true }, JobCondition.InterruptForced);
            }
            else
            {
                var owner = Spawn("Colonist", Faction.OfPlayer, 76, 80);
                owner.health.AddHediff(HediffDefOf.MechlinkImplant, owner.health.hediffSet.GetBrain());
                unit.relations.AddDirectRelation(PawnRelationDefOf.Overseer, owner);
                unit.needs.energy.CurLevel = unit.needs.energy.MaxLevel;
                unit.GetComp<CompStalker>()?.SetAutoCombat(false);
                unit.GetComp<CompGuardian>()?.SetAutoCombat(false);
                unit.GetComp<CompGuardian>()?.SetAutoRestore(false);
            }
            downed = Spawn("Colonist", enemyFaction, 90, 80);
            standing = Spawn("Colonist", enemyFaction, 96, 80);
            friendly = Spawn("Colonist", Faction.OfPlayer, 88, 80);
            foreach (var other in map.attackTargetsCache.GetPotentialTargetsFor(unit).Select(t => t.Thing).ToArray())
                if (other != downed && other != standing && other.Spawned && other.HostileTo(unit))
                {
                    Log.Message("MM_DOWNED FIXTURE removes external hostile " + other.ThingID + " faction=" + other.Faction + " pos=" + other.Position);
                    other.DeSpawn();
                }
            Down(downed);
            Check(!standing.Downed && !standing.Dead && standing.HostileTo(unit) && downed.HostileTo(unit), "hostile standing and downed fixtures");
            Check(!friendly.HostileTo(unit), "closer friendly is non-hostile");
            started = Find.TickManager.TicksGame;
        }
        void Down(Pawn pawn)
        {
            pawn.health.AddHediff(HediffDef.Named("Anesthetic"));
            Check(pawn.Downed && !pawn.Dead && pawn.Spawned, "native anesthesia causes living downed pawn");
        }
        void Recover(Pawn pawn)
        {
            pawn.health.RemoveHediff(pawn.health.hediffSet.GetFirstHediffOfDef(HediffDef.Named("Anesthetic")));
            Check(!pawn.Downed && !pawn.Dead, "native recovery restores standing target");
        }
        void ResetWeapon(Pawn_Immortal pawn)
        {
            if (pawn is Pawn_Disruptor disruptor && disruptor.activeOrb != null) disruptor.activeOrb.Destroy();
            pawn.CancelWeapon(); pawn.weaponClock = pawn.shotClock = 0;
        }
        void WeaponTick(Pawn_Immortal pawn) => AccessTools.Method(pawn.GetType(), "TickWeapon").Invoke(pawn, null);
        void WarframeChecks(Pawn_Immortal pawn)
        {
            Check(pawn.CanShoot(standing), "standing enemy remains shootable");
            Check(pawn.CanShoot(downed), "downed enemy remains valid for explicit weapon command", true);
            pawn.Combat.SetEnabled(true);
            Check(pawn.Combat.FindEnemy() == standing, "combat movement chooses farther standing enemy");
            pawn.Combat.SetEnabled(false);
            ResetWeapon(pawn); pawn.autoFire = true;
            WeaponTick(pawn);
            Check(pawn.attackTarget == standing, "actual auto-fire scanner skips nearer downed and friendly targets");
            ResetWeapon(pawn); pawn.autoFire = false;
            pawn.attackTarget = standing;
            if (pawn.GetType() == typeof(Pawn_Immortal)) pawn.burstRemaining = 1;
            Down(standing);
            int shots = pawn.shotsFired;
            WeaponTick(pawn);
            Check(pawn.attackTarget == null && pawn.burstRemaining == 0 && pawn.shotsFired == shots, "newly downed automatic target drops lock and cancels pending shot");
            ResetWeapon(pawn); pawn.autoFire = true; shots = pawn.shotsFired;
            WeaponTick(pawn);
            Check(pawn.attackTarget == null && pawn.shotsFired == shots, "downed-only field produces no automatic target or shot");
            ResetWeapon(pawn); pawn.autoFire = false;
            pawn.Combat.SetEnabled(true);
            var extra = pawn.Combat.FindEnemy();
            Check(extra == null, "combat movement rejects downed-only field; actual=" + extra + " at=" + extra?.Position + " faction=" + extra?.Faction);
            pawn.Combat.SetEnabled(false);
            Recover(standing);
            ResetWeapon(pawn); pawn.autoFire = true;
            WeaponTick(pawn);
            Check(pawn.attackTarget == standing, "auto-fire can reacquire recovered enemy");
            if (pawn is Pawn_Disruptor orbCaster)
            {
                Check(orbCaster.activeOrb != null && orbCaster.activeOrb.target == standing, "orb starts on standing enemy");
                Down(standing);
                var orb = orbCaster.activeOrb;
                AccessTools.Method(typeof(Thing_DisruptorOrb), "Tick").Invoke(orb, null);
                Check(orb.target == null, "in-flight orb releases downed target");
            }
            ResetWeapon(pawn); pawn.autoFire = false;
            ManualWarframeChecks(pawn);
        }
        void ManualWarframeChecks(Pawn_Immortal pawn)
        {
            // Exercise the actual synchronized executor used by the Attack button/right click.
            MechaActions.Attack(map, pawn, downed);
            bool accepted = pawn.attackTarget == downed;
            Check(accepted, "manual Attack executor accepts downed enemy", true);
            if (!accepted) return; // Baseline failure: dependent firing assertions cannot run.
            pawn.autoFire = true;
            int shots = pawn.shotsFired;
            WeaponTick(pawn);
            if (pawn is Pawn_Colossus)
                for (int tick = 0; tick < Pawn_Colossus.AimTicks; tick++) WeaponTick(pawn);
            Check(pawn.shotsFired > shots && pawn.attackTarget == downed, "manual downed target fires while automatic fire is enabled");
            if (pawn.GetType() == typeof(Pawn_Immortal))
            {
                pawn.shotClock = 0; WeaponTick(pawn);
                Check(pawn.shotsFired == shots + 2, "manual downed target receives both burst shots");
            }
            string saveName = "ManualDowned-" + kinds[index];
            GameDataSaveLoader.SaveGame(saveName);
            var xml = new System.Xml.XmlDocument();
            xml.Load(Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves", saveName + ".rws"));
            Check(bool.Parse(xml.SelectSingleNode("//mmTargetManual")?.InnerText ?? "false"), "native save persists manual weapon intent");
            if (pawn is Pawn_Disruptor caster)
            {
                var orb = caster.activeOrb;
                Check(xml.SelectSingleNode("//mmOrbManualTarget")?.InnerText == downed.GetUniqueLoadID(), "native save persists orb manual target");
                MechaActions.Stop(map, pawn);
                AccessTools.Method(typeof(Thing_DisruptorOrb), "Tick").Invoke(orb, null);
                Check(orb.target == downed, "launched manual orb retains its own downed target after caster stop");
                downed.apparel?.DestroyAll();
                float injuries = downed.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity);
                orb.Position = downed.Position; orb.location = downed.Position.ToVector3Shifted();
                AccessTools.Method(typeof(Thing_DisruptorOrb), "Pulse").Invoke(orb, null);
                Check(downed.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity) > injuries, "manual orb actually damages downed target");
                downed.DeSpawn();
                if (standing.Downed) Recover(standing);
                for (int tick = 0; tick < 16; tick++) AccessTools.Method(typeof(Thing_DisruptorOrb), "Tick").Invoke(orb, null);
                Check(orb.target == standing, "manual orb falls back to a standing automatic target");
                Down(standing);
                AccessTools.Method(typeof(Thing_DisruptorOrb), "Tick").Invoke(orb, null);
                Check(orb.target == null, "orb fallback does not inherit permission to target downed pawns");
                GenSpawn.Spawn(downed, new IntVec3(90, 0, 80), map);
            }
            ResetWeapon(pawn);
            MechaActions.Attack(map, pawn, downed);
            pawn.Combat.SetEnabled(true); WeaponTick(pawn);
            Check(pawn.attackTarget != downed, "switching to automatic combat clears manual targeting permission");
            pawn.Combat.SetEnabled(false); ResetWeapon(pawn);
            MechaActions.Attack(map, pawn, downed); MechaActions.Stop(map, pawn);
            if (!standing.Downed) Down(standing);
            pawn.autoFire = true; WeaponTick(pawn);
            Check(pawn.attackTarget == null, "stop clears manual intent before subsequent automatic scan");
            ResetWeapon(pawn); pawn.autoFire = false;
        }
        void ConstructChecks()
        {
            var stalker = unit.GetComp<CompStalker>();
            var guardian = unit.GetComp<CompGuardian>();
            Func<Thing, bool> valid = t => stalker != null ? stalker.ValidEnemy(t) : guardian.ValidEnemy(t);
            Func<bool, Thing> find = force => stalker != null ? stalker.FindEnemy(force) : guardian.FindEnemy(force);
            Check(valid(standing) && !valid(downed) && !valid(friendly), "construct enemy filter excludes downed and friendly pawns");
            Check(find(true) == standing, "construct scanner selects farther standing enemy");
            Down(standing);
            Check(find(false) == null, "cached construct target invalidated immediately on downing");
            Check(find(true) == null, "construct scanner rejects downed-only field");
            Recover(standing);
            Check(find(true) == standing, "construct reacquires recovered enemy");
            var laser = stalker != null ? stalker.Laser : guardian.Laser;
            unit.drafter.Drafted = true;
            laser.OrderForceTarget(downed);
            Check(unit.CurJob?.def == JobDefOf.AttackStatic && unit.CurJob.playerForced
                && unit.CurJob.targetA.Thing == downed, "native force-target creates manual attack job on downed enemy");
            laser.Reset(); unit.stances.CancelBusyStanceSoft();
            int projectiles = map.listerThings.ThingsOfDef(ThingDef.Named("MM_StalkerLaser")).Count;
            Check(laser.TryStartCastOn(downed), "native verb starts manual cast on downed enemy");
            laser.WarmupComplete();
            if (stalker != null)
                Check(map.listerThings.ThingsOfDef(ThingDef.Named("MM_StalkerLaser")).Count > projectiles, "Stalker fires real projectile at manually selected downed enemy");
            else
            {
                bool ticked = guardian.AttackTick(downed);
                Check(ticked, "Guardian sustains manual beam on downed enemy", true);
                if (ticked)
                {
                    int pulses = guardian.attackPulses;
                    for (int tick = 0; tick < 12; tick++) guardian.AttackTick(downed);
                    Check(guardian.attackPulses > pulses, "Guardian manual downed beam produces damage pulses");
                }
                guardian.StopChannels();
                unit.jobs.StartJob(new Job(JobDefOf.Wait) { expiryInterval = 10000, playerForced = true }, JobCondition.InterruptForced);
                Check(guardian.BeginAttack(downed) && !guardian.AttackTick(downed), "Guardian non-attack job cannot bypass automatic downed filter");
            }
            laser.Reset(); guardian?.StopChannels();
        }
        public override void GameComponentTick()
        {
            if (!active || !prepared || done || unit == null) return;
            Guard(() => {
                if (Find.TickManager.TicksGame - started > 120) throw new Exception("Scan tick timeout");
                if (!unit.IsHashIntervalTick(30)) return;
                // Invoke the real weapon scanner on its native scan tick; no replica selector.
                if (unit is Pawn_Immortal pawn) WarframeChecks(pawn); else ConstructChecks();
                foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray()) p.DeSpawn();
                if (++index < kinds.Length) { Setup(); return; }
                if (baseline && expectedFailures != 9) throw new Exception("Expected exactly nine manual-target failures, got " + expectedFailures);
                File.WriteAllText(Path.Combine(Root, "complete"), baseline ? "EXPECTED_BASELINE_FAILURE" : "PASS_DOWNED_TARGETS");
                done = true; Application.Quit();
            });
        }
    }
}
