# Mechanical tab light-energy display fix (2026-10-24)

The native `RimWorld.PawnColumnWorker_Energy.DoCell` dereferences
`pawn.needs.energy` for every non-gestating mech. `OnlyLightNeed` deliberately
removes all needs from `Pawn_Immortal` and its Colossus, Reaver and Disruptor
subclasses. These pawns still enter `MainTabWindow_Mechs` through their
`CompOverseerSubject`, so rendering the energy column throws on every GUI event.

Ancot Library's postfix on `MainTabWindow_Mechs.DoWindowContents` adds the drone
tab button. Its name appears in Harmony's annotated stack, but it does not read
the energy need. The old DLL reproduces the same null reference both with that
postfix enabled and after the test removes that postfix.

`Source/MechEnergyColumn.cs` intercepts only these four warframes' energy cells
and draws their actual `CompLightStorage.currentLight / maxLightP`, with a
localized Light tooltip. It preserves the no-battery model, leaves gestating
rows blank, clamps fill, guards zero capacity/missing storage, and restores the
previous font/alignment. Other pawns use the original method. No gameplay state,
save fields, energy consumption, charging, or MP commands are changed.

Validation in `BuildValidation/MechEnergy-20261024/`:

- Baseline-R1: old 1.6 DLL reproduced the exact window exception with and without
  Ancot's window postfix. The completion marker records the expected failure;
  the early runner's generic `PASS_MECHA_SP` result means the reproduction check
  completed, not that the old mod worked.
- Candidate-R1: 66 assertions passed in RimWorld 1.6.4850 rev645 with Ancot Library
  loaded. Real native table repaint/fill/text observed for all six mod units and
  a vanilla lifter; empty/partial/full light and a paused change to 25% verified;
  closing/reopening the tab works. No window/null-reference errors. Both actual
  screenshots were inspected. Warframes continue to have no battery need.
- 1.6 and 1.7.4985 rev1269 builds: zero warnings/errors. The 1.7 native energy
  column has the same dereference. 1.7 runtime and multiplayer were not tested.
- Sibling scan: Guardian/Stalker energy accesses guard null; tower transit
  branches by warframe type; recall branches by type and validates energy before
  execution. No additional instance of this defect was found in those callers.
- Official player saves were not loaded. Tests used isolated config/save folders;
  both test processes exited. The generated test save is fixture evidence, not a
  cold-load verification.

Both local core DLLs were installed and their SHA256 matched the built candidates:

- 1.6: `9F0EB48FF71897DC3AF96612CA0CA6567D2453A09C8C5C87767B91B309647F43`
- 1.7: `6D126457AD16FC18D50B1A9D3598A6E590DD65B82496F7C00E183BB8F8231A6D`

Previous DLLs are retained in `BuildValidation/MechEnergy-20261024/Before/`.
The Multiplayer adapter is unchanged. Restart to load the fix. Release revision: `2026-10-24-mech-energy`, GitHub tag `v0.9.4-mech-energy`. The release
uses these exact binaries; the numeric mod version remains 0.9.4.

The reproducible regression harness is `Tests/MechEnergy/Probe.cs` and
`Run.ps1`. Build the probe with `-p:MechaAssembly=<core DLL>`, then invoke the
runner with `-GameRoot <1.6 install> -CandidateSource <complete mod payload>
-Label <fresh run name>`. Use `-Baseline` with the pre-fix DLL to check the
expected failure and Ancot isolation. Candidate payloads need About, Languages,
Textures, LoadFolders.xml and the complete 1.6 directory.
