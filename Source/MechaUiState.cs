using RimWorld;
using UnityEngine;
using Verse;

namespace Monolyn.Mecha
{
    // Shared by this pawn's inspector and status gizmo, including repeated IMGUI events.
    // At most one rendered frame of display latency, even while paused. Gameplay always
    // uses live values (in particular MassUtility in both hauling executors).
    internal sealed class MechaUiState
    {
        readonly Pawn_Immortal mecha;
        internal readonly Gizmo_ImmortalStatus StatusGizmo;
        LoadedLanguage language;
        int frame = -1, tick = -1, hullMax;
        float hull = float.NaN, mass = float.NaN, cargoMax = float.NaN;
        float light = float.NaN, lightMax = float.NaN;
        string pilotLabel, hullLabel, cargoLabel, cargoInspect, pilotInspect;
        string lightLabel, tip, tipKey, lightName;
        string previousBase, previousSuffix, inspectSuffix, inspectString;
        int inspectFrame = -1, inspectTick = -1;
        LoadedLanguage inspectLanguage;
        Map inspectMap;
        internal float LightFill { get; private set; }
        internal string HullLabel => hullLabel;
        internal string CargoLabel => cargoLabel;
        internal string LightLabel => lightLabel;
        internal string Tip => tip;

        internal MechaUiState(Pawn_Immortal mecha)
        {
            this.mecha = mecha;
            StatusGizmo = new Gizmo_ImmortalStatus(mecha);
        }

        internal void Refresh()
        {
            bool translated = language != LanguageDatabase.activeLanguage;
            if (translated)
            {
                language = LanguageDatabase.activeLanguage;
                lightName = "Light".Translate();
            }
            int nowFrame = Time.frameCount, nowTick = Find.TickManager?.TicksGame ?? -1;
            if (!translated && frame == nowFrame && tick == nowTick) return;
            frame = nowFrame; tick = nowTick;
            bool changed = inspectSuffix == null;
            float nextHull = mecha.Hull, nextMass = MassUtility.InventoryMass(mecha);
            if (translated || hull != nextHull || hullMax != mecha.HullMax)
            {
                hull = nextHull; hullMax = mecha.HullMax;
                hullLabel = "MM_Hull".Translate(hull.ToString("F0"), hullMax);
                changed = true;
            }
            if (translated || mass != nextMass || cargoMax != mecha.CargoMax)
            {
                mass = nextMass; cargoMax = mecha.CargoMax;
                cargoLabel = "MM_Cargo".Translate(mass.ToString("F0"), cargoMax);
                cargoInspect = "MM_Cargo".Translate(mass.ToString("F1"), cargoMax);
                changed = true;
            }
            string nextPilot = mecha.Pilot?.LabelShortCap ?? "Nobody".Translate();
            if (translated || pilotLabel != nextPilot)
            {
                pilotLabel = nextPilot;
                pilotInspect = "MM_Pilot".Translate(pilotLabel);
                changed = true;
            }
            var storage = mecha.Light;
            if (translated || light != storage.currentLight || lightMax != storage.maxLightP)
            {
                light = storage.currentLight; lightMax = storage.maxLightP;
                lightLabel = lightName + " " + light.ToString("F0") + " / " + lightMax.ToString("F0");
            }
            LightFill = Mathf.Clamp01(light / lightMax);
            if (translated || tip == null || tipKey != mecha.StatusTipKey)
            {
                tipKey = mecha.StatusTipKey;
                tip = tipKey.Translate();
            }
            if (changed) inspectSuffix = "\n" + hullLabel + "\n" + pilotInspect + "\n" + cargoInspect;
        }

        internal string InspectString(string native)
        {
            Refresh();
            // Keep native Pawn/comp descriptions. Only repeated reads within the same
            // rendered frame share them; the next frame (including paused play) refreshes.
            if (inspectString == null || native != previousBase || inspectSuffix != previousSuffix)
            {
                previousBase = native; previousSuffix = inspectSuffix;
                inspectString = native + inspectSuffix;
            }
            inspectFrame = Time.frameCount;
            inspectTick = Find.TickManager?.TicksGame ?? -1;
            inspectLanguage = LanguageDatabase.activeLanguage;
            inspectMap = Find.CurrentMap;
            return inspectString;
        }

        internal bool TryGetInspectString(out string text)
        {
            text = inspectString;
            // Do not memoize callers outside Unity's GUI event loop.
            return Event.current != null && text != null && inspectFrame == Time.frameCount
                && inspectTick == (Find.TickManager?.TicksGame ?? -1)
                && inspectLanguage == LanguageDatabase.activeLanguage && inspectMap == Find.CurrentMap;
        }
    }
}
