# Guardian combat navigation · 0.9.4 hotfix

The Guardian repeatedly replaced pursuit with nearby tactical positions that could remain behind a wall. The fix commits pursuit, retreat and cover routes, validates firing lines across doors, separates retreat thresholds, and retries stalled or failed paths after a delay. Decision state is saved. Player orders and shield support keep priority.

In the closing-door regression, path starts fell from 72 to 3 and reversals from 71 to 0 over 2,400 ticks. Genuine weapon damage calls increased from 0 to 158 against invulnerable test targets. The other five unit types passed wall-detour firing checks; no matching defect was confirmed in their existing movement logic.

Validation: 63 navigation checks, 120 downed-target checks, and 4 cold-reload checks passed in isolated RimWorld 1.6. The old build reproduced the navigation defect. Cold reload preserved movement decisions and resumed pursuit and attacks without missing-reference errors. Test worlds use fixed fixtures and invulnerable targets, not a player save.

The 1.6 core, 1.7 core and optional 1.6 Multiplayer adapter built successfully. This hotfix has not been tested in 1.7 gameplay, two-client multiplayer, or a full player modpack. Install the complete mod and restart. All multiplayer participants must use identical files.

Run the regression with `Tests/CombatNavigation/Run.ps1`; supply `-GameRoot`, a fresh `-Label`, and `-CandidateSource`. Build `Probe.csproj` first with `GameRoot` and `MechaAssembly`. Use `-Baseline` for the old build, or `-Cold -ColdSource <completed run directory>` to verify its saved snapshot.
