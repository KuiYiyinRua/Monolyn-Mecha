# Caravan selection and round trip regression

Run from the mod root with an absolute installed RimWorld 1.6 game root. The controller copies the candidate and dependencies into a unique run directory, uses separate save-data folders and a localhost Multiplayer connection, and owns only the PIDs it launches.

Build `MultiplayerProbe.csproj` with `-p:GameRoot=<game>` and `-p:MechaAssembly=<candidate>/1.6/Assemblies/Monolyn.Mecha.dll`. Use `-p:Baseline=true` for the original-DLL reproduction; otherwise use `-p:Baseline=false`.

Run `RunMP.ps1 -RunRoot <new-directory> -Mecha <candidate> -GameRoot <game>`. Use `-Baseline` with the baseline build, `-Trace -Ticks 12000` for a diagnostic smoke, or `-Ticks 120000` for the normal-settings soak. `-Port` selects a dedicated local port. Evidence directories must never be reused.

The original-DLL probe proves that all four empty and piloted warframes pass the native sendable filter but appear in no selectable UI section. The candidate test exercises three complete round trips: empty/unbound, piloted, and bound in Escort mode while the mechanitor remains at home. It checks actual section contents, cancel/reopen, selection persistence, native capacity, native cargo loading, normal caravan formation, walking to the exit, world travel and return through the native home-entry menu. Pilots, bindings, inventories, complete footprints and world registration must survive the trip. Ordinary animals and unbound vanilla mechs keep their original UI rules.

Commands originate on the non-host client through the original caravan UI and Multiplayer session. Test-only synchronized setup/check methods prepare the fixture and compare invariants; they do not force caravan creation, despawn the selected units, or bypass formation with the debug instant-depart command. Final simulation and Rand state must match on both peers. The probe is disabled without `-mm-caravan-test` and is never installed into release assemblies.
