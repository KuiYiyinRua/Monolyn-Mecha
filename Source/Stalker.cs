using System;
using System.Collections.Generic;
using System.Linq;
using ASEL;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Monolyn.Mecha
{
    // The single chassis represents a flat HP pool, not a biological organ.
    // Vanilla turns a severely damaged core's efficiency to zero before its HP reaches zero.
    // Keep this chassis functional while it has HP; EMP/energy capacity modifiers still apply.
    [HarmonyPatch(typeof(PawnCapacityUtility), nameof(PawnCapacityUtility.CalculatePartEfficiency))]
    static class StalkerFrameEfficiency
    {
        static bool Prefix(HediffSet diffSet, BodyPartRecord part, ref float __result)
        {
            if (part.def.defName != "MM_StalkerCore" && part.def.defName != "MM_GuardianCore") return true;
            __result = diffSet.GetPartHealth(part) > 0 ? 1f : 0f;
            return false;
        }
    }

    [DefOf]
    public static class StalkerDefOf
    {
        public static JobDef MM_StalkerCombat;
        public static AbilityDef MM_StalkerBlink;
        static StalkerDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(StalkerDefOf)); }
    }

    public sealed class CompProperties_Stalker : CompProperties
    { public CompProperties_Stalker() { compClass = typeof(CompStalker); } }

    // Native MNConstruct supplies the energy need, 2-bandwidth overseer and work settings.
    // This component only adds optional tactics and movement-assisted native blink.
    public sealed class CompStalker : ThingComp
    {
        const float RetreatStartRange = 24f, RetreatStopRange = 27f;
        const int MoveCheckInterval = 30, RepathInterval = 90, PositionReviewInterval = 120;
        const int StuckTicks = 240, FailedTargetRetryTicks = 300;
        enum CombatMove { None, Pursuit, Retreat, Cover }

        public bool autoCombat;
        public bool autoBlink = true;
        public Thing enemy;
        public bool preservingBlinkJob;
        int nextScan;
        int nextDecision;
        CombatMove combatMove;
        bool retreating;
        IntVec3 lastCombatPosition = IntVec3.Invalid;
        int lastCombatProgressTick, nextCombatRepath;
        Thing blockedEnemy;
        int blockedEnemyUntil;
        List<Pawn> nearbyThreats = new List<Pawn>();
        public Pawn Pawn => (Pawn)parent;
        public Verb Laser => Pawn.equipment?.PrimaryEq?.PrimaryVerb;
        public Ability Blink => Pawn.abilities?.GetAbility(StalkerDefOf.MM_StalkerBlink);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref autoCombat, "stalkerAutoCombat", false);
            Scribe_Values.Look(ref autoBlink, "stalkerAutoBlink", true);
            Scribe_References.Look(ref enemy, "stalkerEnemy");
            Scribe_Values.Look(ref nextScan, "stalkerNextScan");
            Scribe_Values.Look(ref nextDecision, "stalkerNextDecision");
            Scribe_Values.Look(ref combatMove, "stalkerCombatMove");
            Scribe_Values.Look(ref retreating, "stalkerRetreating");
            Scribe_Values.Look(ref lastCombatPosition, "stalkerLastCombatPosition", IntVec3.Invalid);
            Scribe_Values.Look(ref lastCombatProgressTick, "stalkerLastCombatProgressTick");
            Scribe_Values.Look(ref nextCombatRepath, "stalkerNextCombatRepath");
            Scribe_References.Look(ref blockedEnemy, "stalkerBlockedEnemy");
            Scribe_Values.Look(ref blockedEnemyUntil, "stalkerBlockedEnemyUntil");
        }

        public bool Operational => Pawn.Spawned && !Pawn.Dead && !Pawn.Downed && !Pawn.InMentalState
            && ((Pawn.Faction == Faction.OfPlayer && Pawn.IsColonyMechPlayerControlled) || EnemyMechaRaids.IsHostileMonolyn(Pawn.Faction))
            && !Pawn.IsSelfShutdown() && !Pawn.IsCharging() && Pawn.Awake()
            && (Pawn.needs.energy == null || Pawn.needs.energy.CurLevelPercentage > 0.1f);

        public bool MayHunt
        {
            get
            {
                if (!Operational || Laser == null) return false;
                if (EnemyMechaRaids.IsHostileMonolyn(Pawn.Faction)) return EnemyMechaRaids.MayFight(Pawn);
                if (!autoCombat || Pawn.GetLord() != null) return false;
                if (Pawn.CurJob?.playerForced == true && Pawn.CurJob.def != StalkerDefOf.MM_StalkerCombat) return false;
                if (Pawn.jobs.jobQueue.AnyPlayerForced || Pawn.carryTracker.CarriedThing != null) return false;
                var mode = Pawn.GetMechWorkMode();
                return Pawn.Drafted || mode == MechWorkModeDefOf.Work || mode == MechWorkModeDefOf.Escort;
            }
        }

        public bool ValidEnemy(Thing t)
        {
            if (t == null || t.Destroyed || !t.Spawned || t.Map != Pawn.Map || !t.HostileTo(Pawn) || t.Position.Fogged(Pawn.Map)) return false;
            if (t is Pawn p && (p.Dead || p.Downed || p.IsPsychologicallyInvisible())) return false;
            return t is IAttackTarget a && !a.ThreatDisabled(Pawn);
        }

        public Thing FindEnemy(bool force = false)
        {
            int now = Find.TickManager.TicksGame;
            if (!force && now < nextScan) return AvailableEnemy(enemy) ? enemy : null;
            nextScan = now + 90;
            if (AvailableEnemy(enemy) && (Laser.CanHitTarget(enemy) || Pawn.CanReach(enemy, PathEndMode.Touch, Danger.Deadly))) return enemy;
            enemy = Pawn.Map.attackTargetsCache.GetPotentialTargetsFor(Pawn)
                .Select(t => t.Thing).Where(AvailableEnemy)
                .OrderBy(t => Pawn.Position.DistanceToSquared(t.Position)).ThenBy(t => t.thingIDNumber)
                .FirstOrDefault(t => Laser.CanHitTarget(t) || Pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly));
            return enemy;
        }

        bool AvailableEnemy(Thing target) => ValidEnemy(target)
            && (target != blockedEnemy || Find.TickManager.TicksGame >= blockedEnemyUntil);

        public float PositionScore(IntVec3 cell, Thing target)
        {
            float distance = cell.DistanceTo(target.Position);
            float score = -Math.Abs(distance - 27.5f) * 2f;
            if (distance < 22f) score -= (22f - distance) * 4f;
            if (Laser.CanHitTargetFrom(cell, target)) score += 18f;
            score += CoverUtility.CalculateOverallBlockChance(cell, target.Position, Pawn.Map) * 22f;
            // Do not kite one attacker directly into a different nearby hostile pawn.
            foreach (var other in nearbyThreats)
                if (other != target && other.Spawned && !other.Downed && !other.Dead)
                    score -= Mathf.Max(0, 9f - cell.DistanceTo(other.Position)) * 3f;
            return score - cell.DistanceTo(Pawn.Position) * 0.16f;
        }

        // Doors are walkable, but are neither blink landing spots nor firing positions.
        // GenSight deliberately omits its endpoint, so LOS alone does not exclude them.
        bool SafeCell(IntVec3 c) => c.InBounds(Pawn.Map) && c.Standable(Pawn.Map) && c.GetDoor(Pawn.Map) == null && !c.Fogged(Pawn.Map)
            && !c.IsForbidden(Pawn) && c.GetFirstPawn(Pawn.Map) == null && c.GetFirstThing<Fire>(Pawn.Map) == null;

        bool StableShootingPosition(IntVec3 cell, Thing target)
        {
            if (cell.GetDoor(Pawn.Map) != null || !Laser.CanHitTargetFrom(cell, target)
                || !Laser.TryFindShootLineFromTo(cell, target, out var line)) return false;
            // Use the verb's actual leaning source. A door briefly opened by our approach
            // must not cancel pursuit or invite a retreat back into the previous room.
            // A held-open door, or one occupied by the target, can sustain a firing line.
            foreach (var step in GenSight.PointsOnLineOfSight(line.Source, line.Dest))
            {
                var door = step.GetDoor(Pawn.Map);
                if (door != null && (!door.Open || (!door.HoldOpen && !target.OccupiedRect().Contains(step)))) return false;
            }
            return true;
        }

        bool ClearTacticalSegment(IntVec3 cell, Thing target, bool forRetreat)
        {
            int distanceSquared = Pawn.Position.DistanceToSquared(target.Position);
            return GenSight.LineOfSight(Pawn.Position, cell, Pawn.Map, skipFirstCell: true,
                validator: step => step.WalkableBy(Pawn.Map, Pawn)
                    && !step.IsForbidden(Pawn) && StableShootingPosition(step, target)
                    && (!forRetreat || step.DistanceToSquared(target.Position) >= distanceSquared));
        }

        public IntVec3 FindCombatCell(Thing target) => FindCombatCell(target, false);

        IntVec3 FindCombatCell(Thing target, bool forRetreat)
        {
            nearbyThreats = Pawn.Map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Pawn)
                && !p.Downed && !p.Dead && p.Position.DistanceToSquared(Pawn.Position) < 400).OrderBy(p => p.thingIDNumber).ToList();
            float distance = Pawn.Position.DistanceTo(target.Position);
            // Local tactics must retain a shot and a clear walking segment. A high-scoring
            // cell behind a wall is a pursuit destination, not a nearby sidestep or retreat.
            var candidates = GenRadial.RadialCellsAround(Pawn.Position, 10, true)
                .Where(c => c == Pawn.Position || (SafeCell(c) && ClearTacticalSegment(c, target, forRetreat)))
                .Where(c => StableShootingPosition(c, target)
                    && (!forRetreat || c.DistanceTo(target.Position) >= distance + 2f))
                .Select(c => new { Cell = c, Score = PositionScore(c, target) })
                .OrderByDescending(c => c.Score).ThenBy(c => c.Cell.x).ThenBy(c => c.Cell.z).Take(18);
            foreach (var c in candidates)
                if (c.Cell == Pawn.Position || (Pawn.CanReserve(c.Cell) && Pawn.CanReach(c.Cell, PathEndMode.OnCell, Danger.Deadly))) return c.Cell;
            return Pawn.Position;
        }

        bool TryFindPursuitCell(Thing target, out IntVec3 cell)
        {
            // Search firing positions around the target, including beyond the local 10-cell
            // tactics radius. The native finder checks reachability, cover and firing lines;
            // the pather can then finish a detour even when it initially leads away from the enemy.
            float range = Laser.verbProps.range;
            var request = new CastPositionRequest {
                caster = Pawn, target = target, verb = Laser, wantCoverFromTarget = true,
                maxRangeFromTarget = range,
                maxRangeFromCaster = Pawn.Position.DistanceTo(target.Position) + range,
                validator = c => (c == Pawn.Position || SafeCell(c))
                    && c.DistanceToSquared(target.Position) >= RetreatStartRange * RetreatStartRange
                    && StableShootingPosition(c, target)
            };
            if (CastPositionFinder.TryFindCastPosition(request, out cell)) return true;
            // Small rooms may not contain a firing position at the preferred distance.
            request.validator = c => (c == Pawn.Position || SafeCell(c)) && StableShootingPosition(c, target);
            return CastPositionFinder.TryFindCastPosition(request, out cell);
        }

        public void ResetCombatMovement()
        {
            combatMove = CombatMove.None;
            retreating = false;
            lastCombatPosition = IntVec3.Invalid;
            lastCombatProgressTick = Find.TickManager.TicksGame;
            nextCombatRepath = nextDecision = 0;
        }

        public void NotifyCombatPathFailed()
        {
            // Do not immediately recreate the same failed job from the think tree.
            blockedEnemy = enemy;
            blockedEnemyUntil = Find.TickManager.TicksGame + FailedTargetRetryTicks;
            enemy = null;
            nextScan = 0;
            ResetCombatMovement();
        }

        void StartCombatMove(LocalTargetInfo destination, PathEndMode mode, CombatMove move)
        {
            int now = Find.TickManager.TicksGame;
            combatMove = move;
            lastCombatPosition = Pawn.Position;
            lastCombatProgressTick = now;
            nextCombatRepath = now + RepathInterval;
            nextDecision = now + MoveCheckInterval;
            Pawn.pather.StartPath(destination, mode);
        }

        void ShootEnemy()
        {
            Pawn.rotationTracker.FaceTarget(enemy);
            Laser.TryStartCastOn(enemy, preventFriendlyFire: true);
        }

        public void CombatTick()
        {
            if (!MayHunt || FindEnemy() == null)
            { Pawn.jobs.EndCurrentJob(JobCondition.Succeeded); return; }
            int now = Find.TickManager.TicksGame;
            if (Pawn.CurJob.targetA.Thing != enemy)
            {
                Pawn.CurJob.SetTarget(TargetIndex.A, enemy);
                Pawn.pather.StopDead();
                ResetCombatMovement();
            }
            // Old saves have no committed-move state. Replan their existing combat path
            // once rather than firing while an untracked legacy route keeps walking.
            if (combatMove == CombatMove.None && Pawn.pather.Moving) Pawn.pather.StopDead();
            if (Pawn.stances.FullBodyBusy || Laser.Bursting)
            {
                // Opening a door, warming up and firing are not failed movement.
                lastCombatProgressTick = now;
                return;
            }
            bool shootable = StableShootingPosition(Pawn.Position, enemy);
            float distance = Pawn.Position.DistanceTo(enemy.Position);
            bool wasRetreating = retreating;
            // A nearby enemy behind a wall is a pursuit problem, not a reason to back away
            // from the only opening. Separate enter/exit ranges prevent threshold oscillation.
            retreating = shootable && distance < (retreating ? RetreatStopRange : RetreatStartRange);
            if (retreating != wasRetreating) nextDecision = now;

            if (Pawn.pather.Moving && combatMove != CombatMove.None)
            {
                if (Pawn.Position != lastCombatPosition)
                {
                    lastCombatPosition = Pawn.Position;
                    lastCombatProgressTick = now;
                }
                else if (now - lastCombatProgressTick >= StuckTicks)
                {
                    Pawn.pather.StopDead();
                    if (shootable)
                    {
                        combatMove = CombatMove.None;
                        nextDecision = now + PositionReviewInterval;
                        ShootEnemy();
                        return;
                    }
                    NotifyCombatPathFailed();
                    Pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                bool readyToFire = shootable && !retreating
                    && (combatMove == CombatMove.Pursuit || combatMove == CombatMove.Retreat);
                bool needRetreat = retreating && combatMove != CombatMove.Retreat;
                if (!readyToFire && !needRetreat)
                {
                    if (now < nextDecision) return;
                    nextDecision = now + MoveCheckInterval;
                    var destination = Pawn.pather.Destination;
                    bool usable = destination.IsValid && (destination.HasThing
                        ? destination.Thing == enemy
                        : destination.Cell == Pawn.Position || SafeCell(destination.Cell));
                    bool stale = usable && !destination.HasThing && now >= nextCombatRepath
                        && (!StableShootingPosition(destination.Cell, enemy)
                            || (combatMove == CombatMove.Retreat
                                && destination.Cell.DistanceTo(enemy.Position) <= distance));
                    if (usable && !stale) return;
                }
                Pawn.pather.StopDead();
                combatMove = CombatMove.None;
                nextDecision = readyToFire ? now + PositionReviewInterval : now;
            }
            else if (combatMove != CombatMove.None)
            {
                // Arriving at a cover point should lead to a firing window, not another
                // immediate search that turns the pawn back toward its previous position.
                combatMove = CombatMove.None;
                nextDecision = shootable && !retreating ? now + PositionReviewInterval : now;
            }
            if (!shootable) nextDecision = now;
            if (now < nextDecision)
            {
                if (shootable) ShootEnemy();
                return;
            }

            nextDecision = now + PositionReviewInterval;
            if (!shootable)
            {
                if (TryFindPursuitCell(enemy, out var firingCell) && firingCell != Pawn.Position)
                    StartCombatMove(firingCell, PathEndMode.OnCell, CombatMove.Pursuit);
                else if (!Pawn.CanReachImmediate(enemy, PathEndMode.Touch)
                    && Pawn.CanReach(enemy, PathEndMode.Touch, Danger.Deadly))
                    StartCombatMove(enemy, PathEndMode.Touch, CombatMove.Pursuit);
                else
                {
                    NotifyCombatPathFailed();
                    Pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                }
                return;
            }

            IntVec3 best = FindCombatCell(enemy, retreating);
            if (best != Pawn.Position && (retreating
                || PositionScore(best, enemy) > PositionScore(Pawn.Position, enemy) + 4f))
            {
                StartCombatMove(best, PathEndMode.OnCell, retreating ? CombatMove.Retreat : CombatMove.Cover);
                return;
            }
            // When a wall or crowd leaves no useful retreat, shoot instead of walking
            // toward the enemy via Touch and immediately trying to retreat again.
            ShootEnemy();
        }

        public override void CompTick()
        {
            base.CompTick();
            if (parent.IsHashIntervalTick(15)) TryMovementBlink();
        }

        public bool TryMovementBlink()
        {
            if (!autoBlink || preservingBlinkJob || !Operational || Pawn.Position.GetDoor(Pawn.Map) != null) return false;
            var pather = Pawn.pather;
            var path = pather?.curPath;
            // StartPath can retain the previous route while a replacement is calculated.
            // Wait for the current destination's path before choosing a movement shortcut.
            if (pather == null || !pather.Moving || path == null || !path.Found || path.NodesLeftCount < 2
                || pather.curPathJobIsStale || pather.curPathRequest != null || Pawn.CurJob == null
                || Pawn.stances.FullBodyBusy || Laser?.Bursting == true) return false;
            LocalTargetInfo destination = pather.Destination;
            if (!destination.IsValid || destination.ThingDestroyed
                || (destination.HasThing && (!destination.Thing.Spawned || destination.Thing.Map != Pawn.Map))
                || pather.lastPathedTargetPosition != destination.Cell) return false;
            Ability ability = Blink;
            if (ability == null || !ability.CanCast) return false;

            // NodesReversed also contains already-consumed nodes. Peek starts at the next
            // step, so a short remaining route cannot send the pawn back along its history.
            // Limit the scan by walking distance, not total path size; winding patrol routes
            // only use their local forward segment and long trips cost the same to inspect.
            float range = ability.verb.verbProps.range;
            float routeDistance = 0f;
            IntVec3 previous = Pawn.Position;
            int lastForwardNode = -1;
            for (int i = 0; i < path.NodesLeftCount; i++)
            {
                IntVec3 c = path.Peek(i);
                // A displaced pawn must let the native pather recover before blinking again.
                if (previous.DistanceToSquared(c) > 2 || !c.WalkableBy(Pawn.Map, Pawn) || c.IsForbidden(Pawn)) break;
                var door = c.GetDoor(Pawn.Map);
                // Let the pather open and traverse slow/closing doors. Skipping its opening
                // stance can otherwise put the pawn inside a closed door or stale route.
                if (door != null && (!door.Open || door.TicksTillFullyOpened > 0 || door.WillCloseSoon)) break;
                routeDistance += previous.DistanceTo(c);
                if (routeDistance > range) break;
                previous = c;
                lastForwardNode = i;
            }
            // Test the furthest forward candidate first to avoid repeated sight/verb checks
            // on every intermediate cell of an unobstructed route.
            IntVec3 landing = IntVec3.Invalid;
            for (int i = lastForwardNode; i >= 0; i--)
            {
                IntVec3 c = path.Peek(i);
                if (Pawn.Position.DistanceToSquared(c) >= 25 && SafeCell(c) && Pawn.CanReserve(c)
                    && Pawn.Map.pawnDestinationReservationManager.CanReserve(c, Pawn, Pawn.Drafted)
                    && GenSight.LineOfSight(Pawn.Position, c, Pawn.Map)
                    && ability.verb.CanHitTarget(c)) { landing = c; break; }
            }
            if (!landing.IsValid) return false;
            PathEndMode mode = Traverse.Create(pather).Field("peMode").GetValue<PathEndMode>();
            Job original = Pawn.CurJob;
            preservingBlinkJob = true;
            try { ability.Activate(landing, LocalTargetInfo.Invalid); }
            finally { preservingBlinkJob = false; }
            if (Pawn.Spawned && Pawn.CurJob == original)
            {
                if (Pawn.jobs.curDriver.TryMakePreToilReservations(false)) Pawn.pather.StartPath(destination, mode);
                else Pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            }
            return Pawn.Position == landing;
        }

        public void SetAutoCombat(bool enabled)
        {
            autoCombat = enabled;
            nextScan = 0;
            ResetCombatMovement();
            blockedEnemy = null;
            blockedEnemyUntil = 0;
            if (!enabled)
            {
                enemy = null;
                if (Pawn.CurJob?.def == StalkerDefOf.MM_StalkerCombat)
                    Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        public void SetAutoBlink(bool enabled) => autoBlink = enabled;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Pawn.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Toggle {
                defaultLabel = "MM_StalkerAutoCombat".Translate(), defaultDesc = "MM_StalkerAutoCombatDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack"), isActive = () => autoCombat,
                toggleAction = () => SetAutoCombat(!autoCombat)
            };
            yield return new Command_Toggle {
                defaultLabel = "MM_StalkerAutoBlink".Translate(), defaultDesc = "MM_StalkerAutoBlinkDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/SyncBlink"), isActive = () => autoBlink,
                toggleAction = () => SetAutoBlink(!autoBlink)
            };
        }
    }

    public sealed class JobGiver_StalkerCombat : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var c = pawn.GetComp<CompStalker>();
            if (c == null || !c.MayHunt || pawn.CurJob?.def == StalkerDefOf.MM_StalkerCombat || c.FindEnemy() == null) return null;
            var j = JobMaker.MakeJob(StalkerDefOf.MM_StalkerCombat, c.enemy);
            j.locomotionUrgency = LocomotionUrgency.Jog;
            return j;
        }
    }
    public sealed class JobDriver_StalkerCombat : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override void Notify_Starting()
        {
            base.Notify_Starting();
            pawn.GetComp<CompStalker>().ResetCombatMovement();
        }
        public override void Notify_PatherFailed()
        {
            pawn.GetComp<CompStalker>().NotifyCombatPathFailed();
            EndJobWith(JobCondition.Incompletable);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            toil.tickAction = () => pawn.GetComp<CompStalker>().CombatTick();
            yield return toil;
        }
    }

    public sealed class CompProperties_StalkerBlink : CompProperties_AbilitySyncBlink
    { public CompProperties_StalkerBlink() { compClass = typeof(CompStalkerBlink); } }
    // Keep the native Enforcer teleport, selection/draft restoration and both effecters.
    public sealed class CompStalkerBlink : CompAbilityEffect_SyncBlink
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            var p = parent.pawn;
            return p.Spawned && target.Cell.InBounds(p.Map) && target.Cell.Standable(p.Map)
                && (target.Cell.GetFirstPawn(p.Map) == null || target.Cell == p.Position)
                && !target.Cell.Fogged(p.Map) && base.Valid(target, throwMessages);
        }
        public override bool AICanTargetNow(LocalTargetInfo target) => Valid(target);
    }

    // Native SyncBlink despawns the same pawn and normally cancels all jobs. An automatic
    // movement blink must preserve the exact driver, carried stack and shift-command queue.
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StopAll))]
    static class StalkerBlinkJobPreservation
    {
        static bool Prefix(Pawn ___pawn) => ___pawn.GetComp<CompStalker>()?.preservingBlinkJob != true;
    }

    [HarmonyPatch(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.RemoveComponentsOnDespawned))]
    static class StalkerBlinkDraftPreservation
    {
        static void Prefix(Pawn pawn, out Pawn_DraftController __state)
            => __state = pawn.GetComp<CompStalker>()?.preservingBlinkJob == true ? pawn.drafter : null;
        static void Postfix(Pawn pawn, Pawn_DraftController __state)
        {
            // Recreating the draft controller and re-drafting clears both the running job
            // and shift queue. Retain the controller (including fire-at-will) during this blink.
            if (__state != null) pawn.drafter = __state;
        }
    }

    // The generic pawn shooting stat is capped and exponentiated by distance. This
    // construct's fixed optical solution uses its verb's 0.9 at every range instead.
    // Native cover, target size, weather and projectile interception still apply.
    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitFactorFromShooter))]
    static class StalkerOpticalAccuracy
    {
        static bool Prefix(Thing caster, ref float __result)
        {
            if (!(caster is Pawn p) || p.GetComp<CompStalker>() == null) return true;
            __result = 1f;
            return false;
        }
    }
}
