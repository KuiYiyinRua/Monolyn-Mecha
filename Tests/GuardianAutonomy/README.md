# Guardian / autonomous warframe validation

Command-line gated isolated runtime probes for RimWorld 1.6. Never copy the probe DLLs into the installed mod. `Run.ps1` and `RunMP.ps1` use separate savedata folders, copied mod inputs, recorded owned PIDs, real graphics and offscreen windows. Frozen input hashes are checked after completion.

1. Archive the complete candidate (About, 1.6, Languages, Textures, Multiplayer, LoadFolders.xml, Source, Build.ps1) into a fresh validation directory. Run Build.ps1 with OutputRoot pointing there.
2. Build Probe.csproj and MultiplayerProbe.csproj in Release, passing GameRoot and MechaAssembly pointing at the candidate core DLL. Complete all builds before launching a run.
3. Run Run.ps1 with GameRoot, CandidateSource and a unique Label. It checks natural seed hauling, bandwidth costs and rejection, native binding/drafting/groups, movement, all four weapons, reflection distributions, stacking, boarding and expiry. Its paused save is the cold-load input.
4. Run Run.ps1 again with Cold, a new Label, and ColdSource relative to BuildValidation. The load is paused in FinalizeInit before timer assertions. Compare actual-state.txt and expected-state.txt.
5. Run RunMP.ps1 with GameRoot, Mecha (archived candidate), an absolute unique RunRoot, Ticks 12000 and Trace for smoke. It waits for the exact server-start log, connects a non-host peer via the official menu connection, freezes and compares initial map/world RNG and gameplay state, then resumes.
6. The non-host opens/cancels/reopens binding menus, confirms bindings, uses native draft commands, casts both fields, moves four chassis, drags the native seed slider and clicks the auto-haul checkbox, then disconnects. Repeat three times. IMGUI presses/releases occur in separate GUI frames to preserve control IDs; both peers display the same Guardian.
7. Run a fresh RunRoot with Ticks 120000 and no Trace for the soak. The test repeats those same assertions, launches real projectiles through overlapping shields and refreshes the test fixture's energy/food. It requires paired state assertions, matching frozen final snapshots and no desync/command errors.

No async time, multifaction or developer mode is enabled. Test-only fixture health/energy maintenance does not bypass production shield, binding, UI synchronization or projectile logic. Test source is copied into each MP run folder; the SP probe DLL is included in frozen inputs. This is a targeted clean-loadout test, not a full external-modpack or separate-PC test.

Keep earlier failures and superseded candidates separate from final results. Do not treat a passing build, startup registration summary or baseline join as a completed multiplayer test.
