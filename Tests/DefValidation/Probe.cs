using System;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Monolyn.Mecha.DefValidation;

[StaticConstructorOnStartup]
public static class Probe
{
    static Probe()
    {
        if (!GenCommandLine.CommandLineArgPassed("mm-body-validation")) return;
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            string root = GenFilePaths.SaveDataFolderPath;
            try
            {
                bool baseline = GenCommandLine.CommandLineArgPassed("mm-body-baseline");
                string[] names = { "Colossus", "Disruptor", "Guardian", "Immortal", "Reaver", "Stalker" };
                var lines = names.Select(name =>
                {
                    var body = DefDatabase<BodyDef>.GetNamed("MM_" + name + "Body");
                    string[] errors = body.ConfigErrors().ToArray();
                    if (body.AllParts.Count != 1) throw new Exception(name + ": body structure changed");
                    if (baseline)
                    {
                        if (errors.Length != 1 || errors[0] != "no parts vulnerable to frostbite")
                            throw new Exception(name + ": baseline did not reproduce: " + string.Join("; ", errors));
                    }
                    else
                    {
                        if (errors.Length != 0) throw new Exception(name + ": " + string.Join("; ", errors));
                        if (body.AllPartsVulnerableToFrostbite.Count != 1)
                            throw new Exception(name + ": frostbite cache not resolved");
                        var part = body.corePart.def;
                        if (part.alive || part.IsSkinCoveredInDefinition_Debug || !part.IsSolidInDefinition_Debug || part.bleedRate != 0)
                            throw new Exception(name + ": mechanical physiology changed");
                    }
                    return "PASS " + body.defName + " errors=" + errors.Length;
                }).ToArray();
                File.WriteAllLines(Path.Combine(root, "assertions.txt"), lines);
                File.WriteAllText(Path.Combine(root, "complete"), VersionControl.CurrentVersionStringWithRev);
                Log.Message("[MM BodyValidation] " + string.Join(" | ", lines));
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(root, "failed"), ex.ToString());
                Log.Error("[MM BodyValidation] " + ex);
            }
            Application.Quit();
        });
    }
}
