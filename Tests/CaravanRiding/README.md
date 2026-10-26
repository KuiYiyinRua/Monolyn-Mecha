# Caravan riding speed regression

From the mod root, build the isolated probe against the candidate's 1.6 core:

```powershell
dotnet build Tests/CaravanRiding/Probe.csproj -c Release "-p:MechaAssembly=<absolute candidate>/1.6/Assemblies/Monolyn.Mecha.dll"
./Tests/CaravanRiding/Run.ps1 -GameRoot <absolute game root> -Label <unique label> -CandidateSource <absolute mod or candidate root>
```

Add `-Baseline` to run against the original core. The same probe must reproduce the missing riding bonus and terrain-adjusted speed previews. The runner refuses to reuse an evidence directory, freezes the mod and dependency inputs, uses a separate save-data folder and background game instance, checks logs and hashes, and only cleans up its own verified process. Evidence is written beneath `BuildValidation/CaravanRiding-20261026/<label>`.

The probe is inert without `-mm-riding-test` and must never ship. It calls the actual patched calculation and preview overloads, compares ordinary caravans with a reverse patch of the native calculation, and advances a real caravan path follower to complete a world tile. It also checks energy consumption and the empty-fuel stop. This is a speed regression, not a test of caravan gathering jobs or Multiplayer synchronization.

Cases: one/two/no riders, surplus mounts, all four warframes, mixed convoys, horses, deselection, formation/split/trade/current speed displays, native edge costs and traversal. Expected Colossus costs are 1071 ticks with one rider, 1319 with two riders and one mount, and 1714 with no rider. Native integer rounding makes 56 tiles/day approximately 56.02.

Verified on 2026-10-26: baseline 25 PASS + 51 expected failures; candidate 76 PASS. See `Docs/CaravanRiding.md` for scope and limitations.
