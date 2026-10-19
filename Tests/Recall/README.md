# Caravan entry and mechanitor recall regression

Run from the mod root, with an absolute RimWorld 1.6 game root. Tests use copied mods, separate save-data folders and localhost multiplayer; they do not touch the player's saves.

Build `MultiplayerProbe.csproj` with `-p:GameRoot=<game>` and `-p:MechaAssembly=<candidate>/1.6/Assemblies/Monolyn.Mecha.dll`. For the original-DLL reproduction, also pass `-p:Baseline=true`; otherwise pass `-p:Baseline=false`.

`RunMP.ps1 -RunRoot <new-directory> -Mecha <candidate> -GameRoot <game>` starts a host, waits for `Server started.`, then starts the client through `-connect`. Optional flags:

- `-Baseline`: reproduce the original edge-entry failure without hosting a session. Use the baseline probe and original mod files.
- `-OneMap`: single-map control.
- `-Async`: two maps with different clocks and differing client focus.
- `-Trace`: diagnostic short smoke only.
- `-Ticks 120000`: long soak; the default is 12000 shared ticks.
- `-Rejoins 3`: keep the host and perform three cold client joins, comparing frozen state and running the action matrix plus 12000 shared ticks after each join.

The non-host client runs three repetitions across all six unit types: same-map button, another-map button, caravan recall menu, orphaned-world-pawn recall menu, insufficient energy, and native caravan entry at a deliberately invalid large-pawn anchor. Assertions check exact energy debits at execution, destination and full footprint, overseer and control membership, inventory, world registration, invalid commands, blocked landing, and frozen host/client simulation and random-state equality. Map entry costs no recall energy.

Keep each run directory immutable. `result.json` is only PASS after paired assertions and final state equality. Failed startup or broken fixture references are not desyncs. The harness does nothing without `-mm-recall-test` (baseline uses `-mm-baseline`) and is never part of the release mod.
