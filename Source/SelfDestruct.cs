using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Monolyn.Mecha
{
    public static class MechaSelfDestruct
    {
        public const float EjectionRange = 50f, BlastRadius = 8f, ArmorPenetration = .8f;
        public const int Damage = 80;
        public static int DamageFor(Pawn_Immortal mecha) => mecha is Pawn_Disruptor ? Damage + 5 : Damage;

        public static bool Available(Pawn_Immortal mecha)
            => mecha != null && mecha.Spawned && !mecha.Dead && !mecha.Destroyed
                && mecha.Faction == Faction.OfPlayer && (mecha.Pilot == null || !mecha.Pilot.Dead);

        public static bool ValidLanding(Pawn_Immortal mecha, IntVec3 cell)
            => Available(mecha) && mecha.Pilot != null && cell != mecha.Position
                && mecha.Position.DistanceToSquared(cell) <= EjectionRange * EjectionRange
                && JumpUtility.ValidJumpTarget(mecha.Pilot, mecha.Map, cell)
                && GenSight.LineOfSight(mecha.Position, cell, mecha.Map, skipFirstCell: true);

        // Opening/cancelling the confirmation and choosing a destination are local UI.
        // Only Execute is synchronized. The expected pilot ID rejects stale dialogs
        // if another player boards/ejects/swaps the pilot before the command executes.
        public static void Confirm(Pawn_Immortal mecha)
        {
            if (!Available(mecha)) return;
            Map map = mecha.Map;
            int pilotId = mecha.Pilot?.thingIDNumber ?? -1;
            string pilotText = (pilotId < 0 ? "MM_SelfDestructEmpty" : "MM_SelfDestructPilot").Translate();
            var dialog = Dialog_MessageBox.CreateConfirmation(
                "MM_SelfDestructConfirm".Translate(mecha.LabelShortCap, pilotText, DamageFor(mecha)),
                () =>
                {
                    if (!Available(mecha) || mecha.Map != map || (mecha.Pilot?.thingIDNumber ?? -1) != pilotId) return;
                    if (pilotId < 0) { Execute(map, mecha, pilotId, IntVec3.Invalid); return; }
                    Find.Targeter.BeginTargeting(new TargetingParameters
                    { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false },
                        target => Execute(map, mecha, pilotId, target.Cell),
                        target =>
                        {
                            if (!Available(mecha) || mecha.Map != Find.CurrentMap) return;
                            GenDraw.DrawRadiusRing(mecha.Position, EjectionRange);
                            GenDraw.DrawRadiusRing(mecha.Position, BlastRadius, Color.red);
                            if (ValidLanding(mecha, target.Cell)) GenDraw.DrawTargetHighlight(target);
                        },
                        target => mecha.Map == map && Find.CurrentMap == map
                            && (mecha.Pilot?.thingIDNumber ?? -1) == pilotId && ValidLanding(mecha, target.Cell));
                }, true, "MM_SelfDestruct".Translate());
            dialog.buttonAText = (pilotId < 0 ? "MM_SelfDestructNow" : "MM_SelfDestructChoose").Translate();
            Find.WindowStack.Add(dialog);
        }

        public static void Execute(Map map, Pawn_Immortal mecha, int expectedPilotId, IntVec3 landing)
        {
            if (!Available(mecha) || mecha.Map != map || (mecha.Pilot?.thingIDNumber ?? -1) != expectedPilotId) return;
            Pawn pilot = mecha.Pilot;
            if (pilot != null && !ValidLanding(mecha, landing)) return;
            IntVec3 origin = mecha.Position;
            int damage = DamageFor(mecha);
            if (pilot != null)
            {
                // Move the real pawn into the native saved/ticking flight holder before
                // killing the chassis. Its normal death-ejection path now sees no pilot.
                pilot.jobs.StopAll();
                // Boarding removes the draft controller, not just its drafted flag.
                // MakeFlyer captures this state and restores it after landing.
                if (pilot.drafter == null) pilot.drafter = new Pawn_DraftController(pilot);
                if (!pilot.Downed && !pilot.InMentalState) pilot.drafter.Drafted = true;
                mecha.pilots.Remove(pilot);
                pilot.Position = origin;
                var flyer = PawnFlyer.MakeFlyer(ThingDefOf.PawnFlyer, pilot, landing,
                    DefDatabase<EffecterDef>.GetNamedSilentFail("JumpFlightEffect"),
                    DefDatabase<SoundDef>.GetNamedSilentFail("JumpPackLand"),
                    overrideStartVec: MechaGeometry.Center(mecha));
                GenSpawn.Spawn(flyer, landing, map);
            }
            MechaAutonomy.Release(mecha);
            mecha.inventory?.DestroyAll();
            mecha.Kill(new DamageInfo(DamageDefOf.Bomb, damage, ArmorPenetration, instigator: mecha));
            mecha.Corpse?.Destroy(DestroyMode.Vanish);
            if (!mecha.Destroyed) mecha.Destroy(DestroyMode.Vanish);
            GenExplosion.DoExplosion(origin, map, BlastRadius, DamageDefOf.Bomb, mecha,
                damAmount: damage, armorPenetration: ArmorPenetration, damageFalloff: false);
        }
    }
}
