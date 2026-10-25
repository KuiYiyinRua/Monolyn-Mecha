using System;
using System.Collections.Generic;
using System.Linq;
using ASEL;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Monolyn.Mecha
{
    public static class MechaActions
    {
        static bool Owned(Map map, Pawn_Immortal m) => m != null && !m.Dead && m.Spawned && m.Map == map && m.Faction == Faction.OfPlayer;
        static IEnumerable<Pawn> Colonists(Pawn_Immortal m) => m.Map.mapPawns.FreeColonistsSpawned
            .Where(p => !p.Downed && !p.InMentalState && p.RaceProps.Humanlike).OrderBy(p => p.Position.DistanceToSquared(m.Position)).ThenBy(p => p.thingIDNumber);

        public static void OrderBoard(Map map, Pawn_Immortal m, Pawn pilot)
        {
            if (!Owned(map, m) || m.Pilot != null || pilot == null || !pilot.Spawned || pilot.Map != map || pilot.Faction != m.Faction) return;
            pilot.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("MM_Board"), m), JobTag.Misc);
        }
        public static void Eject(Map map, Pawn_Immortal m) { if (Owned(map, m)) m.Eject(); }
        public static void Move(Map map, Pawn_Immortal m, IntVec3 cell)
        {
            if (!Owned(map, m) || !m.Driven || !MechaAutonomy.CanCommand(m, cell) || !m.Powered || m.MovementLocked || !cell.InBounds(map) || cell.Fogged(map) || (!(m is Pawn_Colossus) && !cell.Standable(map))
                || !m.CanReach(cell, PathEndMode.OnCell, Danger.Deadly) || (m is Pawn_Colossus && !m.CanOccupy(cell))) return;
            if (!(m is Pawn_Colossus)) m.CancelWeapon();
            var job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            m.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
        public static void Attack(Map map, Pawn_Immortal m, Thing target)
        {
            if (!Owned(map, m) || !m.Driven || target == null || !MechaAutonomy.CanCommand(m, target.Position) || !m.Powered || !m.CanShoot(target)) return;
            m.Combat.SetEnabled(false);
            if (!(m is Pawn_Colossus)) { m.pather.StopDead(); m.jobs.StopAll(); }
            m.CancelWeapon(); m.SetAttackTarget(target, manual: true);
        }
        public static void Stop(Map map, Pawn_Immortal m)
        {
            if (!Owned(map, m)) return;
            m.Combat.SetEnabled(false);
            m.jobs.StopAll(); m.pather.StopDead(); m.CancelWeapon();
        }
        public static void ToggleFire(Map map, Pawn_Immortal m) { if (Owned(map, m)) { m.autoFire = !m.autoFire; m.CancelWeapon(); } }
        public static void ToggleCombat(Map map, Pawn_Immortal m) { if (Owned(map, m)) m.Combat.SetEnabled(!m.Combat.Enabled); }
        public static bool CanTeleport(Pawn_Immortal m, IntVec3 cell)
            => m.Spawned && !m.Dead && !m.MovementLocked && m.teleportCooldown == 0 && !ASELComponent.IsExcessiveTraffic()
            && cell.InBounds(m.Map) && !cell.Fogged(m.Map) && m.CanOccupy(cell, true)
            && LightNetwork.Covering(m.Map, cell) != null;
        public static void Teleport(Map map, Pawn_Immortal m, IntVec3 cell)
        {
            if (!Owned(map, m) || !CanTeleport(m, cell)) return;
            var old = new TargetInfo(m.Position, map);
            m.jobs.StopAll(); m.pather.StopDead(); m.CancelWeapon(); m.Combat.ResetMovement();
            m.Receiver.UnregisterFromLightDistributorsNoSound();
            // Move the existing pawn and its real pilot holder together. No disembarkation,
            // generation or replacement: pilot, inventory, hull, shield and IDs survive.
            m.Position = cell;
            m.Notify_Teleported();
            m.teleportCooldown = 1000;
            ASELComponent.AddTraffic(300f);
            m.ReconnectLight();
            DefDatabase<EffecterDef>.GetNamed("MNLinkLost").Spawn(old, old).Cleanup();
            DefDatabase<EffecterDef>.GetNamed("ConstructSpawned").Spawn(m, m).Cleanup();
        }
        public static void OrderLoad(Map map, Pawn_Immortal m, Pawn worker, Thing item)
        {
            if (!Owned(map, m) || worker == null || !worker.Spawned || worker.Map != map || worker.Faction != m.Faction
                || item == null || !item.Spawned || item.Map != map || !item.def.EverHaulable) return;
            float mass = item.GetStatValue(StatDefOf.Mass);
            int count = mass <= 0f ? item.stackCount : Mathf.FloorToInt((m.CargoMax - MassUtility.InventoryMass(m)) / mass);
            if (count <= 0) return;
            var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("MM_Load"), item, m);
            job.targetC = item;
            job.count = Math.Min(item.stackCount, count);
            worker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
        public static void Unload(Map map, Pawn_Immortal m)
        {
            if (Owned(map, m)) m.inventory.innerContainer.TryDropAll(m.Position, map, ThingPlaceMode.Near);
        }

        public static IEnumerable<Gizmo> Gizmos(Pawn_Immortal m)
            => (m.actionGizmos ?? (m.actionGizmos = new CachedGizmos(m))).Get();

        // A pawn owns its commands; the delegates always resolve the current map and
        // live state when clicked. None of this cache participates in MP simulation.
        internal sealed class CachedGizmos
        {
            readonly Pawn_Immortal mecha;
            readonly Gizmo[] visible = new Gizmo[10];
            Command[] commands;
            LoadedLanguage language;
            ThingDef definition;
            string needsPilotLight, locked, busy, cooldownText;
            int cooldown = -1;

            internal CachedGizmos(Pawn_Immortal mecha) { this.mecha = mecha; }

            internal Gizmo[] Get()
            {
                if (commands == null || language != LanguageDatabase.activeLanguage || definition != mecha.def)
                {
                    language = LanguageDatabase.activeLanguage; definition = mecha.def;
                    commands = CreateGizmos(mecha).Cast<Command>().ToArray();
                    needsPilotLight = "MM_NeedsPilotLight".Translate();
                    locked = "MM_DisruptorLocked".Translate();
                    busy = "MM_NetworkBusy".Translate();
                    cooldown = -1;
                    for (int i = 1; i < visible.Length; i++) visible[i] = commands[i + 1];
                }
                // Grid/third-party UI may disable a command. Do not retain that state
                // across refreshes, boarding, charge changes, or selection changes.
                for (int i = 0; i < commands.Length; i++)
                {
                    commands[i].Disabled = false;
                    commands[i].disabledReason = null;
                }
                bool occupied = mecha.Pilot != null;
                bool operational = mecha.Driven && mecha.Powered && (mecha.Pilot != null || mecha.Drafted);
                visible[0] = commands[occupied ? 1 : 0];
                if (!operational)
                {
                    commands[2].Disable(needsPilotLight);
                    commands[3].Disable(needsPilotLight);
                }
                else if (mecha.MovementLocked) commands[2].Disable(locked);
                var teleport = commands[6];
                if (mecha.MovementLocked) teleport.Disable(locked);
                else if (mecha.teleportCooldown > 0)
                {
                    if (cooldown != mecha.teleportCooldown)
                    {
                        cooldown = mecha.teleportCooldown;
                        cooldownText = "MM_Cooldown".Translate((cooldown / 60f).ToString("F1"));
                    }
                    teleport.Disable(cooldownText);
                }
                else if (ASELComponent.IsExcessiveTraffic()) teleport.Disable(busy);
                if (!MechaSelfDestruct.Available(mecha)) commands[10].Disable("MM_SelfDestructUnavailable".Translate());
                return visible;
            }
        }

        static IEnumerable<Gizmo> CreateGizmos(Pawn_Immortal m)
        {
            yield return Action(m, "MM_Board", () => Find.WindowStack.Add(new FloatMenu(Colonists(m)
                    .Select(p => new FloatMenuOption(p.LabelShortCap, () => OrderBoard(m.Map, m, p))).ToList())));
            yield return Action(m, "MM_Eject", () => Eject(m.Map, m));
            var move = Action(m, "MM_Move", () => Find.Targeter.BeginTargeting(new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false }, t => Move(m.Map, m, t.Cell)));
            yield return move;
            var attack = Action(m, "MM_Attack", () => Find.Targeter.BeginTargeting(new TargetingParameters { canTargetPawns = true, canTargetBuildings = true, canTargetLocations = false }, t => Attack(m.Map, m, t.Thing)));
            yield return attack;
            yield return new Command_Toggle { defaultLabel = (m is Pawn_Disruptor ? "MM_DisruptorAutoFire" : "MM_AutoFire").Translate(), defaultDesc = (m is Pawn_Disruptor ? "MM_DisruptorAutoFireDesc" : "MM_AutoFireDesc").Translate(), icon = m.def.uiIcon,
                isActive = () => m.autoFire, toggleAction = () => ToggleFire(m.Map, m) };
            yield return Action(m, "MM_Stop", () => Stop(m.Map, m));
            var teleport = Action(m, "MM_Teleport", () => Find.Targeter.BeginTargeting(
                new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false },
                t => Teleport(m.Map, m, t.Cell), t =>
                {
                    if (t.Cell.InBounds(m.Map)) GenDraw.DrawTargetHighlight(t);
                    foreach (var d in ASELComponent.Distributors.Where(d => d?.parent != null && d.parent.Map == m.Map && d.ConnectedWithTower))
                        GenDraw.DrawRadiusRing(d.parent.Position, d.Props.distributeRadius);
                }, t => CanTeleport(m, t.Cell)));
            teleport.icon = ContentFinder<Texture2D>.Get("UI/Transfer");
            yield return teleport;
            yield return Action(m, "MM_Load", () => Find.Targeter.BeginTargeting(new TargetingParameters { canTargetItems = true, canTargetLocations = false, canTargetPawns = false, canTargetBuildings = false },
                t => { if (t.Thing != null) Find.WindowStack.Add(new FloatMenu(Colonists(m).Select(p => new FloatMenuOption(p.LabelShortCap, () => OrderLoad(m.Map, m, p, t.Thing))).ToList())); }));
            yield return Action(m, "MM_Unload", () => Unload(m.Map, m));
            yield return new Command_Toggle { defaultLabel = "MM_MechaAutoCombat".Translate(),
                defaultDesc = (m is Pawn_Colossus ? "MM_ColossusAutoCombatDesc" : "MM_MechaAutoCombatDesc").Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack"), isActive = () => m.Combat.Enabled,
                toggleAction = () => ToggleCombat(m.Map, m) };
            var selfDestruct = Action(m, "MM_SelfDestruct", () => MechaSelfDestruct.Confirm(m));
            selfDestruct.defaultDesc = "MM_SelfDestructDesc".Translate(MechaSelfDestruct.DamageFor(m));
            selfDestruct.icon = TexCommand.Attack;
            yield return selfDestruct;
        }
        static Command_Action Action(Pawn_Immortal m, string key, Action action)
            => new Command_Action { defaultLabel = key.Translate(), defaultDesc =
                (m is Pawn_Disruptor && (key == "MM_Attack" || key == "MM_Load" || key == "MM_Move" || key == "MM_Teleport" || key == "MM_Stop")
                    ? key.Replace("MM_", "MM_Disruptor") + "Desc"
                    : m is Pawn_Reaver && (key == "MM_Attack" || key == "MM_Load" || key == "MM_Move" || key == "MM_Teleport")
                    ? key.Replace("MM_", "MM_Reaver") + "Desc"
                    : m is Pawn_Colossus && (key == "MM_Attack" || key == "MM_Load" || key == "MM_Move" || key == "MM_Teleport")
                    ? key.Replace("MM_", "MM_Colossus") + "Desc" : key + "Desc").Translate(), icon = m.def.uiIcon, action = action };
    }

    public sealed class JobDriver_BoardImmortal : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !(job.targetA.Thing is Pawn_Immortal m) || m.Pilot != null || m.Dead);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(60).WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() => ((Pawn_Immortal)job.targetA.Thing).Board(pawn));
        }
    }
    public sealed class JobDriver_LoadImmortal : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
            => pawn.Reserve(job.targetA, job, 1, job.count, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                var m = (Pawn_Immortal)job.targetB.Thing;
                var carried = pawn.carryTracker.CarriedThing;
                if (carried == null) return;
                float mass = carried.GetStatValue(StatDefOf.Mass);
                int count = mass <= 0 ? carried.stackCount : Math.Max(0, Mathf.FloorToInt((m.CargoMax - MassUtility.InventoryMass(m)) / mass));
                int delivered = pawn.carryTracker.innerContainer.TryTransferToContainer(carried, m.inventory.innerContainer, Math.Min(count, carried.stackCount));
                if (pawn.carryTracker.CarriedThing != null) pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                var original = job.targetC.Thing;
                int remaining = job.count - delivered;
                // A colonist may carry less than the selected stack. Queue another real hauling trip.
                if (delivered > 0 && remaining > 0 && original != null && original.Spawned && !original.Destroyed
                    && MassUtility.InventoryMass(m) + Math.Max(0, mass) <= m.CargoMax)
                {
                    var next = JobMaker.MakeJob(job.def, original, m);
                    next.targetC = original;
                    next.count = Math.Min(remaining, original.stackCount);
                    next.playerForced = true;
                    pawn.jobs.jobQueue.EnqueueFirst(next, JobTag.Misc);
                }
            });
        }
    }

    // Uses the same input boundary and deterministic executor split as AircraftDirectControl.
    [HarmonyPatch(typeof(Selector), "HandleMapClicks")]
    static class MechaDirectControl
    {
        static void Prefix()
        {
            var ev = Event.current;
            var map = Find.CurrentMap;
            if (ev == null || ev.type != EventType.MouseDown || ev.button != 1 || map == null || !WorldRendererUtility.DrawingMap
                || Find.Targeter.IsTargeting || Find.DesignatorManager.SelectedDesignator != null) return;
            var selected = Find.Selector.SelectedObjectsListForReading;
            if (selected.Count == 0 || selected.Any(o => !(o is Pawn_Immortal m) || !m.Driven || (m.Pilot == null && !m.Drafted) || m.Faction != Faction.OfPlayer)) return;
            var cell = UI.MouseCell();
            if (!cell.InBounds(map)) return;
            var target = cell.GetThingList(map).Where(t => t is Pawn || t.def.category == ThingCategory.Building).FirstOrDefault(t => t.HostileTo(Faction.OfPlayer));
            foreach (var m in selected.Cast<Pawn_Immortal>().OrderBy(m => m.thingIDNumber))
                if (target != null) MechaActions.Attack(map, m, target); else MechaActions.Move(map, m, cell);
            ev.Use();
        }
    }
}
