using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ASEL;
using Monolyn.Mecha;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace StalkerNavigationTests
{
    public sealed class MechaProbe : GameComponent
    {
        readonly string[] kinds = { "MM_Immortal", "MM_Colossus", "MM_Reaver", "MM_Disruptor" };
        readonly List<Pawn_Immortal> finished = new List<Pawn_Immortal>();
        Map map; Pawn_Immortal unit; Pawn enemy, pilot;
        int index = -1, stage, ticks, frames, shots, movingFire; bool prepared, done, lockedChecked, visited;
        IntVec3 anchor, goal;
        public MechaProbe(Game game) { }
        public override void FinalizeInit() { if (Entry.Cold) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; }
        void Run(Action action)
        {
            try { action(); }
            catch (Exception e) { done = true; File.WriteAllText(Path.Combine(Entry.Root, "failed"), e.ToString()); Log.Error("MECHA_AI_TEST " + e); Application.Quit(); }
        }
        public override void GameComponentUpdate()
        {
            if (!(Entry.Mecha || Entry.Cold) || done || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            Run(() => {
                if (File.Exists(Path.Combine(Entry.Root, "quit"))) { Application.Quit(); return; }
                foreach (var window in Find.WindowStack.Windows.Where(w => w.forcePause).ToArray()) window.Close();
                if (!prepared)
                {
                    prepared = true; map = Find.CurrentMap; map.fogGrid.ClearAllFog();
                    if (Entry.Cold) { Cold(); return; }
                    foreach (var p in map.mapPawns.AllPawnsSpawned.ToArray()) p.DeSpawn();
                    Next();
                }
                if (stage == 9)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    if (++frames == 5) ScreenCapture.CaptureScreenshot(Path.Combine(Entry.Root, "mecha-final.png"));
                    if (frames >= 15) Finish();
                }
                else Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            });
        }
        void Clear()
        {
            foreach (var cell in new CellRect(30, 32, 108, 67))
            {
                map.roofGrid.SetRoof(cell, null);
                foreach (var t in cell.GetThingList(map).ToArray()) if (!(t is Pawn)) { if (t.def.destroyable) t.Destroy(); else t.DeSpawn(); }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
            }
        }
        void Put(Thing thing, IntVec3 cell)
        {
            if (!thing.Spawned) GenSpawn.Spawn(thing, cell, map);
            else { thing.Position = cell; if (thing is Pawn p) p.Notify_Teleported(); }
        }
        void Next()
        {
            if (unit != null)
            {
                MechaActions.Stop(map, unit); unit.autoFire = false;
                Put(unit, new IntVec3(35 + index * 12, 0, 112)); finished.Add(unit);
                if (enemy.Spawned) enemy.DeSpawn();
            }
            if (++index == kinds.Length) { Save(); return; }
            Clear(); Entry.Case = kinds[index];
            unit = (Pawn_Immortal)PawnGenerator.GeneratePawn(PawnKindDef.Named(kinds[index]), Faction.OfPlayer);
            GenSpawn.Spawn(unit, new IntVec3(40, 0, 60), map); Entry.Unit = unit;
            unit.Light.currentLight = unit.LightMax; unit.autoFire = false;
            if (unit is Pawn_Reaver reaver) { reaver.missiles = 8; reaver.autoFabricate = true; }
            pilot = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            Put(pilot, unit.OccupiedRect().ExpandedBy(1).Cells.First(c => !unit.OccupiedRect().Contains(c) && c.Standable(map)));
            Entry.Assert(unit.Board(pilot) && unit.Pilot == pilot && !pilot.Spawned, "real pilot boarded into holder");
            var tower = (ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("TowerOfLight")); tower.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(tower, new IntVec3(45, 0, 86), map);
            var distributor = tower.GetComp<CompLightDistributor>(); distributor.UpdateConnection();
            if (!ASELComponent.Distributors.Contains(distributor)) ASELComponent.Distributors.Add(distributor);
            ASELComponent.currentLight = 100000; Current.Game.GetComponent<ASELComponent>().conduitTraffic = 0;
            var cargo = ThingMaker.MakeThing(ThingDefOf.Steel); cargo.stackCount = 23; unit.inventory.innerContainer.TryAdd(cargo);
            goal = new IntVec3(50, 0, 80);
            var teleport = unit.GetGizmos().OfType<Command_Action>().Single(g => g.defaultLabel == "MM_Teleport".Translate());
            Entry.Assert(!teleport.Disabled && MechaActions.CanTeleport(unit, goal), "occupied cockpit enables real teleport command");
            Entry.Assert(!MechaActions.CanTeleport(unit, new IntVec3(130, 0, 35)), "teleport still requires network coverage");
            string id = unit.ThingID; float hull = unit.Hull, light = unit.Light.currentLight; int shield = unit.Shield.currentHitPoints;
            int ammunition = (unit as Pawn_Reaver)?.missiles ?? 0;
            MechaActions.Teleport(map, unit, goal);
            Entry.Assert(unit.Position == goal && unit.ThingID == id && unit.Pilot == pilot && !pilot.Spawned
                && pilot.ParentHolder == unit.pilotHolder && unit.pilots.Count == 1, "pilot and chassis teleport as same instances");
            Entry.Assert(unit.inventory.innerContainer.Contains(cargo) && cargo.stackCount == 23 && unit.Hull == hull
                && unit.Light.currentLight == light && unit.Shield.currentHitPoints == shield
                && ((unit as Pawn_Reaver)?.missiles ?? 0) == ammunition, "teleport preserves cargo, hull, shield, light and ammunition");
            Entry.Assert(unit.teleportCooldown == 1000 && !MechaActions.CanTeleport(unit, goal), "teleport starts cooldown and rejects repeat");
            Entry.Assert(unit.Eject() && pilot.Spawned && pilot.Position.DistanceTo(unit.Position) < 5, "pilot can disembark at destination");
            Entry.Assert(unit.Board(pilot), "same pilot reboards after teleport");
            Put(unit, new IntVec3(40, 0, 60)); unit.Rotation = Rot4.East;
            enemy = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
            Put(enemy, new IntVec3(120, 0, 60)); enemy.jobs.StopAll(); Entry.Enemy = enemy;
            for (int z = 38; z <= 77; z++)
            {
                var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel); wall.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(wall, new IntVec3(76, 0, z), map);
            }
            Entry.Assert(!unit.Combat.Enabled, "automatic combat defaults off for existing behavior");
            var toggle = unit.GetGizmos().OfType<Command_Toggle>().Single(g => g.defaultLabel == "MM_MechaAutoCombat".Translate());
            toggle.toggleAction();
            Entry.Assert(unit.Combat.Enabled && unit.Combat.MayHunt, "actual toggle enables autonomous pursuit");
            shots = unit.shotsFired; ticks = 0; stage = 1;
            Find.CameraDriver.SetRootPosAndSize(new Vector3(75, 0, 62), 28f);
        }
        public override void GameComponentTick()
        {
            if (!(Entry.Mecha || Entry.Cold) || done || !prepared || stage == 9) return;
            Run(() => {
                ticks++;
                if (ticks % 60 == 0) Entry.Trace("MECHA stage=" + stage + " age=" + ticks + " pos=" + unit.Position
                    + " moving=" + unit.pather.Moving + " dest=" + unit.pather.Destination + " job=" + unit.CurJob?.def.defName
                    + " target=" + unit.Combat.Enemy?.ThingID + " shots=" + unit.shotsFired + " locked=" + unit.MovementLocked);
                if (stage == 1 && ticks >= 2600)
                {
                    Entry.Assert(unit.shotsFired > shots && unit.Position.x > 70, "real pursuit around/through wall reaches firing line and attacks; pos=" + unit.Position + " shots=" + (unit.shotsFired-shots));
                    MechaActions.Stop(map, unit); stage = 2; ticks = 0;
                }
                else if (stage == 2 && !unit.MovementLocked)
                {
                    Clear(); Put(unit, new IntVec3(70, 0, 60)); Put(enemy, new IntVec3(82, 0, 60));
                    unit.Light.currentLight = unit.LightMax; unit.weaponClock = unit.shotClock = 0;
                    if (unit is Pawn_Reaver reaver) reaver.missiles = 8;
                    anchor = unit.Position; movingFire = 0; shots = unit.shotsFired; lockedChecked = false;
                    MechaActions.ToggleCombat(map, unit); stage = 3; ticks = 0;
                }
                else if (stage == 3)
                {
                    if (unit is Pawn_Colossus colossus && unit.pather.Moving && colossus.Sweeping) movingFire++;
                    if (unit is Pawn_Disruptor && unit.MovementLocked && !lockedChecked)
                    {
                        lockedChecked = true; var pos = unit.Position;
                        MechaActions.Move(map, unit, pos + IntVec3.North * 10);
                        Entry.Assert(!unit.pather.Moving && !MechaActions.CanTeleport(unit, pos + IntVec3.North * 10), "active orb still blocks movement and occupied teleport");
                    }
                    if (ticks >= 1000)
                    {
                        Entry.Assert(unit.shotsFired > shots, "close target receives real weapon fire");
                        if (unit is Pawn_Colossus)
                            Entry.Assert(movingFire > 0 && unit.Position.DistanceTo(enemy.Position) >= unit.WeaponRange * .8f, "Colossus retreats while actual sweep fires; movingFire=" + movingFire);
                        else Entry.Assert(unit.Position == anchor, "non-kiting chassis holds position against close target");
                        if (unit is Pawn_Disruptor) Entry.Assert(lockedChecked, "real sustained orb lock exercised");
                        MechaActions.Stop(map, unit); stage = unit is Pawn_Colossus ? 7 : 4; ticks = 0;
                    }
                }
                else if (stage == 7)
                {
                    Clear(); Put(unit, new IntVec3(70, 0, 60)); Put(enemy, new IntVec3(107, 0, 60));
                    unit.Light.currentLight = unit.LightMax; anchor = unit.Position; shots = unit.shotsFired;
                    MechaActions.ToggleCombat(map, unit); stage = 8; ticks = 0;
                }
                else if (stage == 8 && ticks >= 360)
                {
                    Entry.Assert(unit.Position.DistanceTo(anchor) >= 2 && unit.Position.DistanceTo(enemy.Position) >= unit.WeaponRange * .8f,
                        "Colossus commits non-retreat firing-position move; pos=" + unit.Position);
                    Entry.Assert(unit.shotsFired > shots, "Colossus fires during/after committed position adjustment");
                    MechaActions.Stop(map, unit); stage = 4; ticks = 0;
                }
                else if (stage == 4 && !unit.MovementLocked)
                {
                    unit.autoFire = false; MechaActions.ToggleCombat(map, unit);
                    goal = unit.Position + IntVec3.North * 10; visited = false;
                    MechaActions.Move(map, unit, goal);
                    Entry.Assert(unit.CurJob?.playerForced == true && !unit.Combat.MayHunt, "manual movement takes precedence over hunt");
                    stage = 5; ticks = 0;
                }
                else if (stage == 5)
                {
                    if (unit.Position == goal) visited = true;
                    if (visited || ticks >= 600)
                    {
                        Entry.Assert(visited, "manual destination reached without AI reversal");
                        MechaActions.Stop(map, unit); Entry.Assert(!unit.Combat.Enabled, "Stop disables autonomous pursuit");
                        stage = 6;
                    }
                }
                else if (stage == 6 && !unit.MovementLocked) Next();
            });
        }
        string State(Pawn_Immortal m) => m.ThingID + "|" + m.Pilot?.ThingID + "|" + m.Position + "|" + m.Combat.Enabled
            + "|" + m.inventory.innerContainer.TotalStackCountOfDef(ThingDefOf.Steel) + "|" + m.Hull.ToString("R") + "|" + m.teleportCooldown;
        void Save()
        {
            foreach (var m in finished) { m.autoFire = false; m.Combat.SetEnabled(true); }
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            File.WriteAllLines(Path.Combine(Entry.Root, "expected-state.txt"), finished.OrderBy(m => m.thingIDNumber).Select(State));
            GameDataSaveLoader.SaveGame("MechaCombatState");
            Entry.Assert(File.Exists(Path.Combine(GenFilePaths.SaveDataFolderPath, "Saves/MechaCombatState.rws")), "piloted combat state saved for cold load");
            stage = 9; frames = 0;
        }
        void Cold()
        {
            Entry.Case = "cold-load";
            var units = map.mapPawns.AllPawnsSpawned.OfType<Pawn_Immortal>().OrderBy(m => m.thingIDNumber).ToArray();
            Entry.Assert(units.Length == 4 && units.Select(State).SequenceEqual(File.ReadAllLines(Path.Combine(Entry.Root, "expected-state.txt"))), "four chassis preserve pilot IDs, cargo, health, teleport cooldown and combat toggles across restart");
            foreach (var m in units)
            {
                Entry.Assert(m.Driven && m.Pilot.ParentHolder == m.pilotHolder && !m.Pilot.Spawned && m.pilots.Count == 1, m.def.defName + " pilot holder intact after cold load");
                Entry.Assert(m.GetGizmos().OfType<Command_Toggle>().Any(g => g.defaultLabel == "MM_MechaAutoCombat".Translate() && g.isActive()), m.def.defName + " saved toggle visible and active");
                m.Combat.SetEnabled(false);
            }
            stage = 9; frames = 0;
        }
        void Finish()
        {
            done = true; File.WriteAllText(Path.Combine(Entry.Root, Entry.Failures == 0 ? "complete" : "failed"), "MECHA_ASSERTION_FAILURES=" + Entry.Failures);
            Application.Quit();
        }
    }
}
