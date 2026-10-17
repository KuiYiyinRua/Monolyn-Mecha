# Warframe automatic repair — 2026-10-17

The vanilla WorkGiver_RepairMech rejects targets without needs.energy. The four piloted warframes deliberately suppress vanilla needs and use light storage; the vanilla repair driver also dereferences that missing battery.

Source/Repair.cs limits the eligibility change to Pawn_Immortal and its three subclasses (Colossus, Reaver, Disruptor), and selects MM_RepairWarframe. The driver keeps native reservation, reachability, repair speed, crafting XP, health repair and serialized repair timer behavior without accessing a battery. Existing light upkeep is unchanged. Mechanitor eligibility, Smithing work assignment, autoRepair toggle, enemy/burning/attacking and reservation checks remain in effect. Stalker, Guardian and unrelated mechs retain their existing jobs.

## Use
Restart RimWorld. Enable Smithing for the mechanitor and Auto repair on the damaged warframe. Keep the target reachable and out of combat. Existing saves need no migration; disabled auto-repair settings remain disabled.

## Validation
- Both Release projects: 0 warnings, 0 errors.
- Isolated real game SP-R2: 30 PASS assertions. A real mechlink colonist received all four jobs through normal work scheduling (no ordered repair jobs), then healed each three-point hull injury fully.
- Each chassis: missing battery confirmed; auto repair disabled rejects automatic work; forced repair accepted; enabled automatic work accepted; enemy target rejected; actual automatic job and full healing observed.
- Startup diagnostics compared with the existing native-mod baseline; no new errors. Exact candidate hash checked inside the game and all frozen input hashes rechecked after exit.
- SP-R1 was a harness failure: SetForbidden cannot set a flag on pawns without CompForbiddable. Preserved separately; not release evidence.
- Multiplayer already synchronizes the existing autoRepair toggle. No new UI synchronization is introduced. No two-client or cold-save test was performed for this change.

Evidence: BuildValidation/Repair-20261017/SP-R2, provenance.json, deployment.json. Previous DLLs: BuildValidation/Repair-20261017/Before/Assemblies.

Core SHA256: 223B7B21966BB26022DC4DA0A56386ECE6E66084C3E0DFA6106BB0CB496B4283
MP adapter SHA256: E1D755EEA067AD1E4EC3C6322B44B0806D168121E8B5101F85422AA241911CAC

The tested candidate was copied to the local mod and checked by SHA256. No Workshop upload or Git push.

Reproduce: build the core into a complete separate candidate directory, build Tests/Repair/Probe.csproj with MechaAssembly pointing to that candidate DLL, then run Tests/Repair/Run.ps1 with -GameRoot, -CandidateSource and a fresh -Label. Use its normal single-player mode only.
