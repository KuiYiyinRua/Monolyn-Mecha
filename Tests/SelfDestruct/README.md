# Self-destruct runtime validation

These command-line gated probes run only in isolated saves and copied loadouts. Never put probe DLLs in the production mod.

1. Archive a fresh candidate with Source, About, 1.6, Multiplayer, Languages, Textures, LoadFolders.xml and Build.ps1. Build into that candidate using Build.ps1 -OutputRoot.
2. Build Probe.csproj and MultiplayerProbe.csproj in Release with GameRoot and MechaAssembly pointing to the archived core DLL. Finish builds before launching processes.
3. Run Run.ps1 with explicit GameRoot, CandidateSource and a unique Label. Tests cover all four piloted chassis, confirmation cancellation/reopening, cancelled targeting, exact 50-cell boundary, blocked targets, stale pilot and duplicate commands, death/corpse/cargo outcomes, native flyers, explosion parameters, friendly targets inside and outside the blast, and autonomous bandwidth release.
4. Run Run.ps1 with Cold, a new Label and ColdSource referring to the successful SP evidence directory relative to BuildValidation. The save is paused with pilots in flight. Compare exact state before the first loaded tick, then assert successful landing.
5. Run RunMP.ps1 with a unique absolute RunRoot, Mecha candidate, GameRoot, Ticks 12000 and Trace. Prefer SeedSave pointing to the proven clean pre-action SelfDestructMpSeed.rws; the controller copies and hashes it and loads only the host from the menu. Non-host invokes actual gizmo, dialog confirmation and target callbacks. Three rounds cover each of the four occupied and four autonomous chassis: 24 detonations and 12 pilot flights. Both peers assert each phase. Previous survivors move away before the next round to keep landing cells free.
6. Repeat in a fresh RunRoot with Ticks 120000 and traces off. Require identical frozen baseline/final snapshots, 12 matching phase assertions, stable input hashes and no synchronization or command errors.

The fixture supplies test bandwidth and maintains food/rest/mood for surviving pilots during the soak. It does not patch ejection, death, damage, flight or synchronization. Normal time, one faction and one map are used. Controllers record exact binaries, configs, owned process identities and logs, and stop only their own processes. Failed or superseded attempts remain separately labelled.

The gated probe captures one stack for the upstream `Deep-saving destroyed thing` warning. In the tested MP build this comes from `TimestampFixer.FixPawn` calling `ScribeSaver.DebugOutputFor` as the native death path passes a destroyed pawn to WorldPawns. It is an auxiliary in-memory traversal, not a failed save or missing reference. The probe does not suppress the warning or alter the timestamp fixer.
