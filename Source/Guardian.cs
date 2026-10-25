using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ASEL;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace Monolyn.Mecha
{
    [DefOf]
    public static class GuardianDefOf
    {
        public static JobDef MM_GuardianCombat, MM_GuardianRestore;
        public static DamageDef MM_GuardianBeam;
        static GuardianDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(GuardianDefOf)); }
    }

    public sealed class CompProperties_Guardian : CompProperties
    { public CompProperties_Guardian() { compClass = typeof(CompGuardian); } }

    // The native shield owners keep their own EMP, broken-shield and reactivation timers.
    // Transfers only change the existing pool; they never create a second shield.
    public sealed class GuardianShield
    {
        static readonly AccessTools.FieldRef<CompShield, float> EnergyField = AccessTools.FieldRefAccess<CompShield, float>("energy");
        readonly CompProjectileInterceptor interceptor;
        readonly CompShield belt;
        public GuardianShield(CompProjectileInterceptor comp) { interceptor = comp; }
        public GuardianShield(CompShield comp) { belt = comp; }
        float LossPerPoint => Mathf.Max(.0001f, belt.Props.energyLossPerDamage);
        public float Maximum => interceptor != null ? interceptor.HitPointsMax : belt.parent.GetStatValue(StatDefOf.EnergyShieldEnergyMax) / LossPerPoint;
        public float Current => interceptor != null ? Mathf.Max(0, interceptor.currentHitPoints) : Mathf.Max(0, belt.Energy) / LossPerPoint;
        public float Missing => Mathf.Max(0, Maximum - Current);
        public float Add(float points)
        {
            float amount = Mathf.Min(points, Missing);
            if (interceptor != null)
            {
                int whole = Mathf.FloorToInt(amount + .0001f);
                interceptor.currentHitPoints = Mathf.Min(interceptor.HitPointsMax, Mathf.Max(0, interceptor.currentHitPoints) + whole);
                return whole;
            }
            EnergyField(belt) = Mathf.Min(belt.parent.GetStatValue(StatDefOf.EnergyShieldEnergyMax), belt.Energy + amount * LossPerPoint);
            belt.KeepDisplaying();
            return amount;
        }
        public static GuardianShield Find(Thing target)
        {
            var pool = FindOn(target as ThingWithComps);
            if (pool != null) return pool;
            if (target is Pawn pawn && pawn.apparel != null)
                for (int i = 0; i < pawn.apparel.WornApparel.Count; i++)
                {
                    pool = FindOn(pawn.apparel.WornApparel[i]);
                    if (pool != null) return pool;
                }
            return null;
        }
        static GuardianShield FindOn(ThingWithComps owner)
        {
            if (owner == null) return null;
            var comps = owner.AllComps;
            for (int index = 0; index < comps.Count; index++)
            {
                GuardianShield pool = comps[index] is CompProjectileInterceptor i ? new GuardianShield(i)
                    : comps[index] is CompShield b ? new GuardianShield(b) : null;
                if (pool != null && pool.Maximum > 0 && pool.Missing > .001f) return pool;
            }
            return null;
        }
    }

    public sealed class CompGuardian : ThingComp
    {
        public const float Range = 25f, ShieldPerSecond = 25f, LightPerShield = 1f;
        public bool autoCombat, autoRestore = true;
        public Thing enemy, restoreTarget, beamTarget;
        public bool restoring;
        public int attackTicks, attackPulses, channelJobId = -1;
        public float restoreCredit, totalRestored, totalLightSpent;
        // Native enemy constructs have no player-only mech energy need. This finite
        // onboard reserve supplies their shield projector and persists across save/load.
        public float raidSupportLight = 600f;
        int nextScan, nextSupportScan, nextDecision;
        MoteDualAttached beam;
        Sustainer sound;
        List<Pawn> threats = new List<Pawn>();
        // Derived scratch only: rebuilt before use, never read from UI or serialized.
        readonly List<SupportCandidate> supportCandidates = new List<SupportCandidate>();
        struct SupportCandidate
        {
            public Thing Thing;
            public float Fraction;
            public int Distance;
        }
        static readonly Comparison<SupportCandidate> SupportOrder = (a, b) =>
        {
            int result = a.Fraction.CompareTo(b.Fraction);
            if (result == 0) result = a.Distance.CompareTo(b.Distance);
            return result == 0 ? a.Thing.thingIDNumber.CompareTo(b.Thing.thingIDNumber) : result;
        };
        public Pawn Pawn => (Pawn)parent;
        public Verb_GuardianBeam Laser => Pawn.equipment?.PrimaryEq?.PrimaryVerb as Verb_GuardianBeam;
        public bool Operational => Pawn.Spawned && !Pawn.Dead && !Pawn.Downed && !Pawn.InMentalState
            && ((Pawn.Faction == Faction.OfPlayer && Pawn.IsColonyMechPlayerControlled) || EnemyMechaRaids.IsHostileMonolyn(Pawn.Faction))
            && Pawn.Awake() && !Pawn.IsSelfShutdown() && !Pawn.IsCharging()
            && (Pawn.needs.energy == null || Pawn.needs.energy.CurLevelPercentage > .1f);
        public bool MayAct
        {
            get
            {
                if (!Operational) return false;
                if (EnemyMechaRaids.IsHostileMonolyn(Pawn.Faction)) return EnemyMechaRaids.MayFight(Pawn);
                if (Pawn.GetLord() != null || (!autoCombat && !(autoRestore && Pawn.Drafted))) return false;
                if (Pawn.CurJob?.playerForced == true && Pawn.CurJob.def != GuardianDefOf.MM_GuardianCombat) return false;
                if (Pawn.jobs.jobQueue.AnyPlayerForced || Pawn.carryTracker.CarriedThing != null) return false;
                var mode = Pawn.GetMechWorkMode();
                return Pawn.Drafted || mode == MechWorkModeDefOf.Work || mode == MechWorkModeDefOf.Escort;
            }
        }
        // Comp_MNC converts ConstructConsumption/10 light to .35*chargeFactor energy every 250 ticks.
        // Spending its already charged personal energy uses that exact conversion, never the colony pool.
        public float LightPerEnergy => Pawn.GetStatValue(MNMiscDefOf.ConstructConsumption) / 10f
            / (.35f * Mathf.Max(.01f, Pawn.GetStatValue(StatDefOf.MechEnergyUsageFactor)));
        public float SpendableLight => Pawn.needs.energy == null
            ? (EnemyMechaRaids.IsHostileMonolyn(Pawn.Faction) ? Mathf.Max(0, raidSupportLight) : 0)
            : Mathf.Max(0, Pawn.needs.energy.CurLevel - Pawn.needs.energy.MaxLevel * .1f) * LightPerEnergy;
        public bool ValidEnemy(Thing t) => t != null && !t.Destroyed && t.Spawned && t.Map == Pawn.Map && t.HostileTo(Pawn)
            && !t.Position.Fogged(Pawn.Map) && (!(t is Pawn p) || (!p.Dead && !p.Downed && !p.IsPsychologicallyInvisible()))
            && t is IAttackTarget a && !a.ThreatDisabled(Pawn);
        public bool ValidAlly(Thing t) => t != null && t != Pawn && !t.Destroyed && t.Spawned && t.Map == Pawn.Map
            && (t is Pawn || t is Building) && t.Faction != null
            && (t.Faction == Pawn.Faction || t.Faction.RelationKindWith(Pawn.Faction) == FactionRelationKind.Ally)
            && !t.Position.Fogged(Pawn.Map) && (!(t is Pawn p) || !p.Dead) && GuardianShield.Find(t) != null;
        public bool InBeamRange(Thing t) => t != null && t.Spawned && t.Map == Pawn.Map && Pawn.Position.InHorDistOf(t.Position, Range)
            && GenSight.LineOfSight(Pawn.Position, t.Position, Pawn.Map, skipFirstCell: true);

        public Thing FindEnemy(bool force = false)
        {
            int now = Find.TickManager.TicksGame;
            if (!force && now < nextScan) return ValidEnemy(enemy) ? enemy : null;
            nextScan = now + 90;
            if (ValidEnemy(enemy) && (InBeamRange(enemy) || Pawn.CanReach(enemy, PathEndMode.Touch, Danger.Deadly))) return enemy;
            enemy = Pawn.Map.attackTargetsCache.GetPotentialTargetsFor(Pawn).Select(t => t.Thing).Where(ValidEnemy)
                .OrderBy(t => Pawn.Position.DistanceToSquared(t.Position)).ThenBy(t => t.thingIDNumber)
                .FirstOrDefault(t => InBeamRange(t) || Pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly));
            return enemy;
        }
        public Thing FindSupport(bool force = false)
        {
            if (!autoRestore || SpendableLight < LightPerShield) return restoreTarget = null;
            int now = Find.TickManager.TicksGame;
            if (!force && now < nextSupportScan && ValidAlly(restoreTarget)
                && Pawn.Position.InHorDistOf(restoreTarget.Position, Range)) return restoreTarget;
            if (!force && now < nextSupportScan && restoreTarget == null) return null;
            nextSupportScan = now + 30;
            supportCandidates.Clear();
            var map = Pawn.Map;
            // These native lists cover the exact Pawn/Building categories accepted by
            // ValidAlly, including allied non-colonist buildings. Multi-cell objects
            // qualify when any occupied cell intersects the original radius.
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++) AddSupportCandidate(pawns[i]);
            var buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++) AddSupportCandidate(buildings[i]);
            buildings = map.listerBuildings.allBuildingsNonColonist;
            for (int i = 0; i < buildings.Count; i++) AddSupportCandidate(buildings[i]);
            supportCandidates.Sort(SupportOrder);
            restoreTarget = null;
            for (int i = 0; i < supportCandidates.Count; i++)
            {
                var target = supportCandidates[i].Thing;
                if (InBeamRange(target) || Pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                { restoreTarget = target; break; }
            }
            supportCandidates.Clear();
            return restoreTarget;
        }
        void AddSupportCandidate(Thing target)
        {
            if (!Pawn.Position.InHorDistOf(target.OccupiedRect().ClosestCellTo(Pawn.Position), Range) || !ValidAlly(target)) return;
            var pool = GuardianShield.Find(target);
            supportCandidates.Add(new SupportCandidate { Thing = target, Fraction = pool.Current / pool.Maximum,
                Distance = Pawn.Position.DistanceToSquared(target.Position) });
        }
        bool SafeCell(IntVec3 c) => c.InBounds(Pawn.Map) && c.Standable(Pawn.Map) && !c.Fogged(Pawn.Map)
            && !c.IsForbidden(Pawn) && c.GetFirstPawn(Pawn.Map) == null && c.GetFirstThing<Fire>(Pawn.Map) == null;
        public float PositionScore(IntVec3 cell, Thing target)
        {
            float distance = cell.DistanceTo(target.Position);
            float score = -Math.Abs(distance - 22.5f) * 2f;
            if (distance < 18f) score -= (18f - distance) * 4f;
            if (Laser.CanHitTargetFrom(cell, target)) score += 18f;
            score += CoverUtility.CalculateOverallBlockChance(cell, target.Position, Pawn.Map) * 22f;
            foreach (var other in threats)
                if (other != target && other.Spawned && !other.Downed && !other.Dead) score -= Mathf.Max(0, 9f - cell.DistanceTo(other.Position)) * 3f;
            return score - cell.DistanceTo(Pawn.Position) * .16f;
        }
        public IntVec3 FindCombatCell(Thing target)
        {
            threats = Pawn.Map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Pawn) && !p.Downed && !p.Dead && p.Position.DistanceToSquared(Pawn.Position) < 400).OrderBy(p => p.thingIDNumber).ToList();
            var candidates = GenRadial.RadialCellsAround(Pawn.Position, 10, true).Where(c => c == Pawn.Position || SafeCell(c))
                .Select(c => new { Cell = c, Score = PositionScore(c, target) }).OrderByDescending(c => c.Score)
                .ThenBy(c => c.Cell.x).ThenBy(c => c.Cell.z).Take(18);
            foreach (var c in candidates)
                if (c.Cell == Pawn.Position || (Pawn.CanReserve(c.Cell) && Pawn.CanReach(c.Cell, PathEndMode.OnCell, Danger.Deadly))) return c.Cell;
            return Pawn.Position;
        }
        public void CombatTick()
        {
            if (!MayAct) { Pawn.jobs.EndCurrentJob(JobCondition.Succeeded); return; }
            Thing support = FindSupport();
            if (support != null) { WorkOnShield(support); return; }
            if (restoring) StopChannels();
            if (!autoCombat || FindEnemy() == null || Laser == null) { Pawn.jobs.EndCurrentJob(JobCondition.Succeeded); return; }
            if (Find.TickManager.TicksGame >= nextDecision)
            {
                nextDecision = Find.TickManager.TicksGame + 15;
                var best = FindCombatCell(enemy);
                bool move = !InBeamRange(enemy) || Pawn.Position.DistanceTo(enemy.Position) < 19f
                    || PositionScore(best, enemy) > PositionScore(Pawn.Position, enemy) + 4f;
                if (move && best != Pawn.Position)
                {
                    StopChannels();
                    if (!Pawn.pather.Moving || Pawn.pather.Destination.Cell.DistanceToSquared(best) > 4) Pawn.pather.StartPath(best, PathEndMode.OnCell);
                    return;
                }
                if (InBeamRange(enemy)) Pawn.pather.StopDead();
            }
            if (!Pawn.pather.Moving && InBeamRange(enemy) && !Laser.Bursting && !Pawn.stances.FullBodyBusy)
                Laser.TryStartCastOn(enemy, preventFriendlyFire: true);
            else if (!Pawn.pather.Moving && !InBeamRange(enemy)) Pawn.pather.StartPath(enemy, PathEndMode.Touch);
        }
        public bool WorkOnShield(Thing target)
        {
            if (!Operational || !ValidAlly(target) || SpendableLight < LightPerShield) { StopChannels(); return false; }
            if (!InBeamRange(target))
            {
                StopChannels();
                if (!Pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly)) return false;
                if (!Pawn.pather.Moving || Pawn.pather.Destination.Thing != target) Pawn.pather.StartPath(target, PathEndMode.Touch);
                return true;
            }
            Pawn.pather.StopDead();
            if (!restoring || beamTarget != target)
            {
                StopChannels(); beamTarget = target; restoring = true; restoreCredit = 0; channelJobId = Pawn.CurJob?.loadID ?? -1;
            }
            Pawn.rotationTracker.FaceTarget(target);
            return true;
        }
        public bool BeginAttack(Thing target)
        {
            if (!Operational || target == null || target.Destroyed || !InBeamRange(target) || target == Pawn) return false;
            StopEffects(); restoring = false; beamTarget = target; attackTicks = 0; restoreCredit = 0;
            channelJobId = Pawn.CurJob?.loadID ?? -1;
            return true;
        }
        public bool AttackTick(Thing target)
        {
            if (!Operational || restoring || target == null || beamTarget != target || Pawn.pather.Moving || target.Destroyed || !InBeamRange(target)
                || (target is Pawn p && (p.Dead || (p.Downed && !ManuallyAttacking(target))))
                || (Pawn.CurJob?.loadID ?? -1) != channelJobId) return false;
            Pawn.rotationTracker.FaceTarget(target);
            MaintainBeam();
            if (++attackTicks >= 12)
            {
                attackTicks -= 12; attackPulses++;
                target.TakeDamage(new DamageInfo(GuardianDefOf.MM_GuardianBeam, 1f, .3f, (MechaGeometry.Center(target) - MechaGeometry.Center(Pawn)).AngleFlat(), Pawn));
            }
            return true;
        }
        bool ManuallyAttacking(Thing target) => Pawn.CurJob?.def == JobDefOf.AttackStatic
            && Pawn.CurJob.playerForced && Pawn.CurJob.targetA.Thing == target;
        void RestoreTick()
        {
            if (!Operational || !ValidAlly(beamTarget) || !InBeamRange(beamTarget) || Pawn.pather.Moving
                || (Pawn.CurJob?.loadID ?? -1) != channelJobId
                || (Pawn.CurJob?.def != GuardianDefOf.MM_GuardianRestore && (!MayAct || !autoRestore))) { StopChannels(); return; }
            restoreCredit += ShieldPerSecond / 60f;
            var pool = GuardianShield.Find(beamTarget);
            float units = Mathf.Min(Mathf.Floor(restoreCredit + .0001f), Mathf.Floor(SpendableLight / LightPerShield + .0001f));
            if (units >= 1f)
            {
                float restored = pool.Add(units);
                float cost = restored * LightPerShield;
                if (Pawn.needs.energy != null) Pawn.needs.energy.CurLevel -= cost / LightPerEnergy;
                else raidSupportLight = Mathf.Max(0, raidSupportLight - cost);
                restoreCredit -= restored; totalRestored += restored; totalLightSpent += cost;
            }
            if (SpendableLight < LightPerShield || GuardianShield.Find(beamTarget) == null) { StopChannels(); return; }
            MaintainBeam();
        }
        public override void CompTick()
        {
            base.CompTick();
            if (restoring) RestoreTick();
            else if (beamTarget != null && (Laser?.Bursting != true || !Operational || (Pawn.CurJob?.loadID ?? -1) != channelJobId)) StopChannels();
        }
        public Vector3 EmitterOffset()
        {
            // Reviewed lens centers on 1254px sprites at drawSize 1.2: south (627,734), east (980,749).
            if (Pawn.Rotation == Rot4.East) return new Vector3(.3378f, 0, -.11675f);
            if (Pawn.Rotation == Rot4.West) return new Vector3(-.3378f, 0, -.11675f);
            return new Vector3(0, 0, Pawn.Rotation == Rot4.North ? .30f : -.1024f);
        }
        void MaintainBeam()
        {
            if (beam == null || beam.Destroyed) beam = MoteMaker.MakeInteractionOverlay(DefDatabase<ThingDef>.GetNamed("Mote_InformationBeamShortB"), Pawn, beamTarget);
            beam.UpdateTargets(Pawn, beamTarget, EmitterOffset(), Vector3.zero);
            beam.Maintain();
            if (sound == null || sound.Ended) sound = DefDatabase<SoundDef>.GetNamed("Supression_Shooting").TrySpawnSustainer(SoundInfo.InMap(Pawn, MaintenanceType.PerTick));
            sound?.Maintain();
        }
        void StopEffects()
        {
            if (beam != null && !beam.Destroyed) beam.Destroy();
            beam = null; sound?.End(); sound = null;
        }
        public void StopChannels()
        {
            Laser?.Reset(); StopEffects(); restoring = false; beamTarget = null; restoreCredit = 0; attackTicks = 0; channelJobId = -1;
            Pawn.stances?.CancelBusyStanceSoft();
        }
        public void OnVerbReset()
        {
            if (!restoring) { StopEffects(); beamTarget = null; attackTicks = 0; channelJobId = -1; }
        }
        public void SetAutoCombat(bool enabled)
        {
            autoCombat = enabled; nextScan = nextSupportScan = nextDecision = 0;
            if (Pawn.CurJob?.def == GuardianDefOf.MM_GuardianCombat) Pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }
        public void SetAutoRestore(bool enabled)
        {
            autoRestore = enabled; nextSupportScan = 0; restoreTarget = null;
            if (!enabled && restoring && Pawn.CurJob?.def != GuardianDefOf.MM_GuardianRestore) StopChannels();
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish) { StopChannels(); base.PostDeSpawn(map, mode); }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref autoCombat, "guardianAutoCombat", false); Scribe_Values.Look(ref autoRestore, "guardianAutoRestore", true);
            Scribe_Values.Look(ref raidSupportLight, "guardianRaidSupportLight", 600f);
            Scribe_References.Look(ref enemy, "guardianEnemy"); Scribe_References.Look(ref restoreTarget, "guardianRestoreTarget"); Scribe_References.Look(ref beamTarget, "guardianBeamTarget");
            Scribe_Values.Look(ref restoring, "guardianRestoring"); Scribe_Values.Look(ref channelJobId, "guardianChannelJobId", -1);
            Scribe_Values.Look(ref attackTicks, "guardianAttackTicks"); Scribe_Values.Look(ref restoreCredit, "guardianRestoreCredit");
            Scribe_Values.Look(ref attackPulses, "guardianAttackPulses"); Scribe_Values.Look(ref totalRestored, "guardianTotalRestored"); Scribe_Values.Look(ref totalLightSpent, "guardianLightSpent");
            Scribe_Values.Look(ref nextScan, "guardianNextScan"); Scribe_Values.Look(ref nextSupportScan, "guardianNextSupportScan"); Scribe_Values.Look(ref nextDecision, "guardianNextDecision");
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Pawn.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Toggle { defaultLabel = "MM_GuardianAutoCombat".Translate(), defaultDesc = "MM_GuardianAutoCombatDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack"), isActive = () => autoCombat, toggleAction = () => SetAutoCombat(!autoCombat) };
            yield return new Command_Toggle { defaultLabel = "MM_GuardianAutoRestore".Translate(), defaultDesc = "MM_GuardianAutoRestoreDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/autoAbility"), isActive = () => autoRestore, toggleAction = () => SetAutoRestore(!autoRestore) };
            var manual = new Command_Action { defaultLabel = "MM_GuardianRestore".Translate(), defaultDesc = "MM_GuardianRestoreDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/autoAbility"), action = () => Find.Targeter.BeginTargeting(new TargetingParameters {
                    canTargetPawns = true, canTargetBuildings = true, canTargetLocations = false, canTargetSelf = false,
                    validator = t => t.HasThing && ValidAlly(t.Thing)
                }, t => Pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(GuardianDefOf.MM_GuardianRestore, t))) };
            if (!Operational || SpendableLight < LightPerShield) manual.Disable("MM_GuardianNotReady".Translate());
            yield return manual;
        }
    }

    public sealed class Verb_GuardianBeam : Verb
    {
        CompGuardian Controller => CasterPawn?.GetComp<CompGuardian>();
        public override bool Available() => base.Available() && Controller?.Operational == true && !Controller.restoring;
        public override void WarmupComplete()
        {
            if (Controller?.BeginAttack(CurrentTarget.Thing) != true) { Reset(); return; }
            state = VerbState.Bursting; burstShotsLeft = 1; ticksToNextBurstShot = int.MaxValue;
        }
        public override void BurstingTick()
        {
            if (Controller?.AttackTick(CurrentTarget.Thing) != true) { var completed = castCompleteCallback; Reset(); completed?.Invoke(); }
            else CasterPawn.stances.SetStance(new Stance_Cooldown(2, CurrentTarget, this));
        }
        protected override bool TryCastShot() => false;
        public override void Reset() { base.Reset(); Controller?.OnVerbReset(); }
    }
    public sealed class JobGiver_Guardian : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            var c = pawn.GetComp<CompGuardian>();
            if (c == null || !c.MayAct || pawn.CurJob?.def == GuardianDefOf.MM_GuardianCombat
                || (c.FindSupport() == null && (!c.autoCombat || c.FindEnemy() == null))) return null;
            return JobMaker.MakeJob(GuardianDefOf.MM_GuardianCombat);
        }
    }
    public sealed class JobDriver_Guardian : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(condition => pawn.GetComp<CompGuardian>().StopChannels());
            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            toil.tickAction = () => {
                var c = pawn.GetComp<CompGuardian>();
                if (job.def == GuardianDefOf.MM_GuardianRestore) { if (!c.WorkOnShield(TargetThingA)) EndJobWith(JobCondition.Succeeded); }
                else c.CombatTick();
            };
            yield return toil;
        }
    }
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    static class GuardianNewOrder
    { static void Prefix(Pawn ___pawn) { ___pawn.GetComp<CompGuardian>()?.StopChannels(); } }

    [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.Capacity))]
    static class GuardianMassCapacity
    {
        static void Postfix(Pawn p, StringBuilder explanation, ref float __result)
        { if (p.GetComp<CompGuardian>() != null) { __result = 40f; explanation?.AppendLine("MM_GuardianPayload".Translate()); } }
    }
    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.MaxStackSpaceEver))]
    static class GuardianHaulMass
    {
        static bool Prefix(Pawn_CarryTracker __instance, ThingDef td, ref int __result)
        {
            if (__instance.pawn.GetComp<CompGuardian>() == null) return true;
            float mass = td.GetStatValueAbstract(StatDefOf.Mass);
            __result = mass <= 0 ? td.stackLimit : Math.Min(td.stackLimit, Mathf.FloorToInt(40f / mass + .0001f));
            return false;
        }
    }
    // Planning only has a ThingDef; pickup must also account for the actual material and held stack.
    static class GuardianCarryMass
    {
        public static int Space(Pawn_CarryTracker carry, Thing item)
        {
            float mass = item.GetStatValue(StatDefOf.Mass);
            var held = carry.CarriedThing;
            float remaining = Mathf.Max(0, 40f - (held == null ? 0 : held.GetStatValue(StatDefOf.Mass) * held.stackCount));
            return mass <= 0 ? item.def.stackLimit : Mathf.FloorToInt(remaining / mass + .0001f);
        }
    }
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.PawnCanAutomaticallyHaulFast_NewTemp))]
    static class GuardianHaulEligibility
    {
        static void Postfix(Pawn p, Thing t, ref bool __result)
        {
            if (__result && p.GetComp<CompGuardian>() != null && GuardianCarryMass.Space(p.carryTracker, t) <= 0) __result = false;
        }
    }
    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry), new[] { typeof(Thing), typeof(int), typeof(bool) })]
    static class GuardianPickupCount
    {
        static bool Prefix(Pawn_CarryTracker __instance, Thing item, ref int count, ref int __result)
        {
            if (__instance.pawn.GetComp<CompGuardian>() == null) return true;
            count = Math.Min(count, GuardianCarryMass.Space(__instance, item));
            if (count > 0) return true;
            __result = 0; return false;
        }
    }
    [HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry), new[] { typeof(Thing) })]
    static class GuardianPickupWhole
    {
        static bool Prefix(Pawn_CarryTracker __instance, Thing item, ref bool __result)
        {
            if (__instance.pawn.GetComp<CompGuardian>() == null || item.stackCount <= GuardianCarryMass.Space(__instance, item)) return true;
            __result = false; return false;
        }
    }
    // Directional alignment for the independently generated rear sprite; preserve original RGBA artwork.
    [HarmonyPatch(typeof(PawnRenderNodeWorker), nameof(PawnRenderNodeWorker.ScaleFor))]
    static class GuardianRearScale
    {
        internal static bool Applies(PawnRenderNode node, PawnDrawParms parms)
            => node is PawnRenderNode_AnimalPart_Body && parms.pawn.def.defName == "MM_Guardian" && parms.facing == Rot4.North;
        static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        { if (Applies(node, parms)) { __result.x *= .92f; __result.z *= .92f; } }
    }
    [HarmonyPatch(typeof(PawnRenderNodeWorker_AnimalBody), nameof(PawnRenderNodeWorker_AnimalBody.OffsetFor))]
    static class GuardianRearOffset
    {
        static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        { if (GuardianRearScale.Applies(node, parms)) __result.z += .0138f; }
    }
}
