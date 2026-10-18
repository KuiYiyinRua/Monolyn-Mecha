using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Monolyn.Mecha
{
    [DefOf]
    public static class MechaCombatDefOf
    {
        public static JobDef MM_MechaCombat;
        static MechaCombatDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(MechaCombatDefOf)); }
    }

    // Owns movement only. The existing weapon ticks still pay light, consume ammunition,
    // finish bursts/sweeps and maintain the Disruptor's immobile orb.
    public sealed class MechaCombatAI
    {
        readonly Pawn_Immortal pawn;
        public bool Enabled;
        public Thing Enemy;
        Thing blockedEnemy;
        int blockedUntil, nextScan, nextDecision, repathAfter, lastProgress;
        IntVec3 lastPosition = IntVec3.Invalid;
        enum MoveKind { None, Pursuit, Retreat, Cover }
        MoveKind move;
        bool retreating;
        PositioningVerb positioningVerb;
        bool Kites => pawn is Pawn_Colossus;
        int Now => Find.TickManager.TicksGame;
        float RetreatStart => pawn.WeaponRange * .8f;
        float RetreatStop => pawn.WeaponRange * .9f;
        public MechaCombatAI(Pawn_Immortal pawn) { this.pawn = pawn; }
        public bool MayHunt => pawn.Spawned && !pawn.Dead && !pawn.Downed && !pawn.InMentalState
            && ((Enabled && pawn.Faction == Faction.OfPlayer && pawn.GetLord() == null) || EnemyMechaRaids.MayFight(pawn))
            && pawn.Driven && pawn.Powered
            && (!pawn.MovementLocked || EnemyMechaRaids.MayFight(pawn)) && !pawn.jobs.jobQueue.AnyPlayerForced
            && !(pawn.CurJob?.playerForced == true && pawn.CurJob.def != MechaCombatDefOf.MM_MechaCombat);

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "mmAutoCombat");
            Scribe_References.Look(ref Enemy, "mmCombatEnemy");
            Scribe_References.Look(ref blockedEnemy, "mmCombatBlockedEnemy");
            Scribe_Values.Look(ref blockedUntil, "mmCombatBlockedUntil");
            Scribe_Values.Look(ref nextScan, "mmCombatScan");
            Scribe_Values.Look(ref nextDecision, "mmCombatDecision");
            Scribe_Values.Look(ref repathAfter, "mmCombatRepath");
            Scribe_Values.Look(ref lastProgress, "mmCombatProgress");
            Scribe_Values.Look(ref lastPosition, "mmCombatPosition", IntVec3.Invalid);
            Scribe_Values.Look(ref move, "mmCombatMove");
            Scribe_Values.Look(ref retreating, "mmCombatRetreating");
        }
        public void SetEnabled(bool value)
        {
            Enabled = value; Enemy = blockedEnemy = null; blockedUntil = nextScan = 0; ResetMovement();
            if (!value)
            {
                if (pawn.CurJob?.def == MechaCombatDefOf.MM_MechaCombat) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                // A Disruptor may be waiting while its orb locks movement. The AI's
                // last weapon target must still be cleared when combat is disabled.
                pawn.CancelWeapon();
            }
        }
        public void ResetMovement()
        { move = MoveKind.None; retreating = false; lastPosition = pawn.Position; lastProgress = Now; nextDecision = repathAfter = 0; }
        bool Valid(Thing target) => target != null && target.Spawned && !target.Destroyed && target.Map == pawn.Map
            && target.HostileTo(pawn) && !target.Position.Fogged(pawn.Map)
            && (!(target is Pawn p) || (!p.Dead && !p.Downed && !p.IsPsychologicallyInvisible()))
            && target is IAttackTarget attack && !attack.ThreatDisabled(pawn)
            && (target != blockedEnemy || Now >= blockedUntil);
        public Thing FindEnemy()
        {
            if (Now < nextScan) return Valid(Enemy) ? Enemy : null;
            nextScan = Now + 90;
            if (Valid(Enemy)) return Enemy;
            Enemy = pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn).Select(t => t.Thing).Where(Valid)
                .OrderBy(t => pawn.Position.DistanceToSquared(t.Position)).ThenBy(t => t.thingIDNumber)
                .FirstOrDefault(t => pawn.CanShoot(t) || pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly));
            return Enemy;
        }
        bool SafeCell(IntVec3 cell)
        {
            if (!cell.InBounds(pawn.Map) || cell.Fogged(pawn.Map) || cell.IsForbidden(pawn)
                || cell.GetFirstThing<Fire>(pawn.Map) != null || cell.GetDoor(pawn.Map) != null) return false;
            // Respect the entire chassis and possible arrival orientation, not only its anchor.
            if (ReaverFootprint.Applies(pawn))
                return Enumerable.Range(0, 4).Any(r => ReaverFootprint.Clear(pawn.Map, cell, new Rot4(r), pawn, true));
            return pawn.CanOccupy(cell, true);
        }
        bool StableShot(IntVec3 cell, Thing target)
        {
            if (cell.GetDoor(pawn.Map) != null || !pawn.CanShootFrom(cell, target)) return false;
            foreach (var step in GenSight.PointsOnLineOfSight(cell, target.Position))
            {
                var door = step.GetDoor(pawn.Map);
                if (door != null && (!door.Open || (!door.HoldOpen && !target.OccupiedRect().Contains(step)))) return false;
            }
            return true;
        }
        bool FindFiringCell(out IntVec3 cell)
        {
            if (positioningVerb == null) positioningVerb = new PositioningVerb(this);
            var request = new CastPositionRequest {
                caster = pawn, target = Enemy, verb = positioningVerb, wantCoverFromTarget = Kites,
                maxRangeFromTarget = pawn.WeaponRange,
                maxRangeFromCaster = pawn.Position.DistanceTo(Enemy.Position) + pawn.WeaponRange,
                validator = c => SafeCell(c) && (!Kites || c.DistanceTo(Enemy.Position) >= RetreatStart)
            };
            if (CastPositionFinder.TryFindCastPosition(request, out cell)) return true;
            request.validator = SafeCell;
            return CastPositionFinder.TryFindCastPosition(request, out cell);
        }
        float Score(IntVec3 cell) => -Math.Abs(cell.DistanceTo(Enemy.Position) - RetreatStop) * 2f
            + CoverUtility.CalculateOverallBlockChance(cell, Enemy.Position, pawn.Map) * 22f
            - cell.DistanceTo(pawn.Position) * .16f;
        IntVec3 FindLocalCell()
        {
            float distance = pawn.Position.DistanceTo(Enemy.Position);
            foreach (var cell in GenRadial.RadialCellsAround(pawn.Position, 10, true)
                .Where(c => c != pawn.Position && SafeCell(c) && StableShot(c, Enemy)
                    && (!retreating || c.DistanceTo(Enemy.Position) >= distance + 2f))
                .OrderByDescending(Score).ThenBy(c => c.x).ThenBy(c => c.z).Take(18))
            {
                // Even a phase-striding Colossus should not kite into a wall that hides its target,
                // or first walk toward a close attacker to reach a distant cell on the other side.
                if (!GenSight.LineOfSight(pawn.Position, cell, pawn.Map, true,
                    step => SafeCell(step) && StableShot(step, Enemy)
                        && (!retreating || step.DistanceTo(Enemy.Position) >= distance))) continue;
                if (pawn.CanReserve(cell) && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return cell;
            }
            return pawn.Position;
        }
        void StartMove(IntVec3 cell, MoveKind kind = MoveKind.Pursuit)
        {
            move = kind; lastPosition = pawn.Position; lastProgress = Now;
            nextDecision = Now + 30; repathAfter = Now + 90;
            pawn.pather.StartPath(cell, PathEndMode.OnCell);
        }
        public void PathFailed()
        {
            blockedEnemy = Enemy; blockedUntil = Now + 300; Enemy = null; nextScan = 0;
            ResetMovement(); pawn.CancelWeapon();
        }
        public void Tick()
        {
            if (pawn.MovementLocked) { lastProgress = Now; return; }
            if (!MayHunt || FindEnemy() == null) { pawn.jobs.EndCurrentJob(JobCondition.Succeeded); return; }
            if (pawn.CurJob.targetA.Thing != Enemy)
            { pawn.CurJob.SetTarget(TargetIndex.A, Enemy); pawn.pather.StopDead(); ResetMovement(); }
            if (move == MoveKind.None && pawn.pather.Moving) pawn.pather.StopDead();
            if (pawn.stances.FullBodyBusy) { lastProgress = Now; return; }
            bool canFire = StableShot(pawn.Position, Enemy);
            float distance = pawn.Position.DistanceTo(Enemy.Position);
            retreating = Kites && canFire && distance < (retreating ? RetreatStop : RetreatStart);
            // Set the real weapon target without restarting an active sweep or burst.
            if (canFire) pawn.attackTarget = Enemy;
            if (move != MoveKind.None && pawn.pather.Moving)
            {
                if (pawn.Position != lastPosition) { lastPosition = pawn.Position; lastProgress = Now; }
                else if (Now - lastProgress >= 240)
                { pawn.pather.StopDead(); move = MoveKind.None; if (!canFire) { PathFailed(); pawn.jobs.EndCurrentJob(JobCondition.Incompletable); return; } }
                if (move != MoveKind.None)
                {
                    var dest = pawn.pather.Destination;
                    bool usable = dest.IsValid && SafeCell(dest.Cell);
                    // Non-kiting chassis stop on their first stable shot. Colossus keeps its
                    // committed retreat until the outer threshold and finishes cover moves.
                    if (canFire && !retreating && move != MoveKind.Cover)
                    { pawn.pather.StopDead(); move = MoveKind.None; nextDecision = Now + 120; }
                    else if (retreating && move != MoveKind.Retreat) { pawn.pather.StopDead(); move = MoveKind.None; nextDecision = Now; }
                    else if (usable && (Now < repathAfter || (StableShot(dest.Cell, Enemy)
                        && (move != MoveKind.Retreat || dest.Cell.DistanceTo(Enemy.Position) > distance)))) return;
                    else { pawn.pather.StopDead(); move = MoveKind.None; nextDecision = Now; }
                }
            }
            else if (move != MoveKind.None) { move = MoveKind.None; nextDecision = Now + (canFire && !retreating ? 120 : 0); }
            if (!canFire)
            {
                if (Now < nextDecision) return;
                // No kiting for these weapons, including an attacker inside their minimum range.
                if (!Kites && distance <= pawn.MinimumWeaponRange && GenSight.LineOfSight(pawn.Position, Enemy.Position, pawn.Map, true))
                { PathFailed(); pawn.jobs.EndCurrentJob(JobCondition.Incompletable); return; }
                if (FindFiringCell(out var cell) && cell != pawn.Position) StartMove(cell);
                else { PathFailed(); pawn.jobs.EndCurrentJob(JobCondition.Incompletable); }
                return;
            }
            if (!Kites || Now < nextDecision) return;
            nextDecision = Now + 120;
            var best = FindLocalCell();
            if (best != pawn.Position && (retreating || Score(best) > Score(pawn.Position) + 4f))
                StartMove(best, retreating ? MoveKind.Retreat : MoveKind.Cover);
        }

        // CastPositionFinder needs only range and CanHitTargetFrom. This adapter does not
        // fire, tick, join a VerbTracker, or replace the chassis' native/custom weapons.
        sealed class PositioningVerb : Verb
        {
            readonly MechaCombatAI ai;
            public PositioningVerb(MechaCombatAI ai)
            { this.ai = ai; caster = ai.pawn; verbProps = new VerbProperties { range = ai.pawn.WeaponRange }; }
            public override float EffectiveRange => ai.pawn.WeaponRange;
            public override bool CanHitTargetFrom(IntVec3 root, LocalTargetInfo target) => target.HasThing && ai.StableShot(root, target.Thing);
            protected override bool TryCastShot() => false;
        }
    }
    public sealed class JobGiver_MechaCombat : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!(pawn is Pawn_Immortal m) || !m.Combat.MayHunt || m.Combat.FindEnemy() == null) return null;
            var job = JobMaker.MakeJob(MechaCombatDefOf.MM_MechaCombat, m.Combat.Enemy);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            return job;
        }
    }
    public sealed class JobDriver_MechaCombat : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override void Notify_Starting() { base.Notify_Starting(); ((Pawn_Immortal)pawn).Combat.ResetMovement(); }
        public override void Notify_PatherFailed() { ((Pawn_Immortal)pawn).Combat.PathFailed(); EndJobWith(JobCondition.Incompletable); }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            toil.tickAction = () => ((Pawn_Immortal)pawn).Combat.Tick();
            yield return toil;
        }
    }
}
