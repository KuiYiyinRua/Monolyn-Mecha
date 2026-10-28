# Monolyn: Mecha · 0.9.4

## 0.9.4 · Guardian navigation fix · 2026-10-28

- Fixed Guardians repeatedly reversing outside walls and near closing doors. Pursuit routes now persist, with separate retreat and reposition decisions.
- Failed or stalled paths retry after a delay; the continuous beam yields to retreat. Wall-detour attack regressions passed for the other five unit types.
- 187 isolated 1.6 combat, downed-target and cold-reload checks passed. 1.6/1.7 and multiplayer adapter builds passed. No 1.7 gameplay or two-client multiplayer testing. Update the full mod and restart; synchronize all multiplayer clients.

## 0.9.4 · Caravan riding speed fix · 2026-10-26

- Colossus now applies riding bonuses to its base speed of 35 tiles/day: about 56 with one rider and one Colossus, or 45.5 with two riders and one Colossus. Other warframes share the fix; mixed groups use the slowest warframe base speed.
- Daily speed previews now match travel in formation, splitting, trading and existing caravans.
- 76 checks passed in isolated 1.6 gameplay; 1.6/1.7 builds passed. No 1.7 gameplay, multiplayer or cold save reload testing. Route arrival estimates still use the native estimator. Update the complete mod and restart.

## 0.9.4 hotfix: Downed target selection · 2026-10-25

- Fixes Immortal automatically selecting downed enemies. Automatic targeting skips downed enemies for all six units, while manual attack orders can still target them.
- The four warframes now distinguish manual and automatic targets. A Disruptor orb can pursue and damage its manually ordered downed target; automatic retargeting does not inherit that permission. Guardian manual sustained beams can also hit downed targets.
- 120 checks passed in isolated 1.6 gameplay; 1.6/1.7 builds passed. This revision has not been tested in 1.7 gameplay, multiplayer or a cold save reload. Update the complete mod and restart; all players need the same files.

## 0.9.4 hotfix: Mechs tab energy column · 2026-10-24

- Fixes repeated null-reference errors and missing energy bars when opening the Mechs tab after creating a warframe. All four warframes now display their actual stored light.
- The vanilla column accessed a battery need these warframes do not have. The error reproduces with or without Ancot Library’s window postfix. Ancot Library can stay enabled; energy consumption and save format are unchanged.
- 66 checks passed in isolated 1.6 gameplay, covering all six mod units and a vanilla lifter. Both 1.6/1.7 builds passed; this hotfix has not been runtime-tested in 1.7 or multiplayer. Update the complete mod and restart.

Six light-powered ground units for Monolyn Race: Immortal, Stalker, Colossus, Guardian, Reaver and Disruptor. The four warframes support colonist pilots or autonomous mechanitor control; the two constructs are controlled by mechanitors. Version: 0.9.4.

## Warp to mechanitor

All six units gain a warp button. It costs 30% of maximum personal light on the same map, or 50% across maps and from caravans; Stalker and Guardian use the same fraction of their construct energy. The mechanitor menu can recall stranded units whose binding still exists. The mechanitor must be on a map. Insufficient energy or no safe landing means no charge. Large units now receive valid full-footprint caravan entry cells.

## Guardian: Shield Field

Uses 2 bandwidth. A separate seed reserve holds up to 30 seeds. Set a target of 0–30 with the slider and toggle automatic refueling by colonists. Spend 5 seeds to immediately deploy a radius-8 shield for 30 seconds, moving with the Guardian. Each hostile projectile crossing its boundary has a 50% chance to reflect back along its incoming path; otherwise it continues through. Friendly projectiles are unaffected. Multiple Guardians can overlap their fields, with an independent roll for each shield. Instant beams and melee attacks are not reflected.

## Autonomous control

Bind an empty warframe to a mechanitor on the same map with enough free bandwidth: Immortal 4, Colossus 6, Reaver 6, Disruptor 5. It joins a mech control group and can be drafted, moved and ordered to attack within the mechanitor’s command range. It still consumes light. Boarding a pilot or unbinding releases the bandwidth. Manufacturing a new warframe also requires the corresponding free bandwidth and binds it to the Formator’s mechanitor.

## Self-destruct and emergency ejection

All four pilot-capable warframes have Self-destruct. Read and confirm the consequences, then choose a valid landing cell within 50 cells and line of sight. The pilot flies out with their own equipment and inventory; the chassis immediately dies and explodes, leaving no corpse. Cargo in the chassis is destroyed. The blast has radius 8, base damage 80 (85 for Disruptor) and 80% armor penetration, and harms allies and buildings. Choose a landing outside the red blast ring; landing nearby can still injure the pilot. An empty chassis detonates immediately after confirmation and releases its bandwidth. Cancelling either step does not detonate it.

## Six units, distinct roles

Combine frontline firepower, mobility, shield support and transport. These staged in-game demonstrations come from an earlier version; the text gives the current figures and new features.
![Monolyn Mecha](https://images.steamusercontent.com/ugc/9740647638787754843/D5880F9A041D324ED23B8855F1DF373D85CD8062/)

## Immortal · Armored frontline

A 500-point shield and paired missiles, each dealing 10 damage across the center cell and four orthogonal neighbors. Supports a pilot or autonomous control for 4 bandwidth.
![Monolyn Mecha](https://images.steamusercontent.com/ugc/12589577182004703314/968886AACCE76025368BFCB552D883F75E1EAF8B/)

## Stalker · Mobile combat

Uses 2 bandwidth, twin lasers and blink. Toggle automatic combat and automatic blink separately. It also hauls, cleans and fights fires.
![Monolyn Mecha](https://images.steamusercontent.com/ugc/11915945897327046556/131C9095258E1A543B42A5BC1B9AA9785A3A5791/)

## Colossus · Sweeping beams

A 200-point shield and two sweeping beams, each dealing 21 damage over a five-cell cross. Crosses walls and fires while moving. Autonomous control costs 6 bandwidth.
![Monolyn Mecha](https://images.steamusercontent.com/ugc/12205559618903829612/45310D199D62877569D49AB4279471CD05823682/)

## Guardian · Shield support

Uses 2 bandwidth, attacks with a continuous laser and restores allied shields. Automatic combat and restoration have separate controls; manual restoration and seed-powered reflection fields are also available.
![Monolyn Mecha](https://images.steamusercontent.com/ugc/15217843184345479861/A28999F48C38F2DEDBD13589597C97B065D31F05/)

## Reaver · Heavy bombardment

A ten-round magazine, range 45 and automatic ammunition production from light. Area bombardment deals extra damage to buildings. Autonomous control costs 6 bandwidth.
![Monolyn Mecha](https://images.steamusercontent.com/ugc/12982748170061290114/2229723A6B9E273127444E71ACF99FAF40DB8505/)

## Disruptor · Tracking orb

Its orb lasts 5 seconds, moves at 8 cells/second, deals 10 damage every 0.25 seconds and finishes with a 28-damage explosion. The chassis cannot move while maintaining the orb. Autonomous control costs 5 bandwidth.
![Monolyn Mecha](https://images.steamusercontent.com/ugc/12480251011735394709/A0BF373BEA033B81715C8F123EB8B89BBCEF2A14/)

## Cargo, travel and automatic combat

Capacity: Immortal 500 kg, Colossus 800 kg, Reaver 2,000 kg, Disruptor 600 kg. Select these four in the caravan Animals section, empty, piloted or bound. Their mechanitor may stay home; one colonist outside the cockpits must lead.

All six mechs can use connected towers with a capable pilot or assigned mechanitor, retaining pilots, cargo and bindings. Energy and network limits apply; large mechs may overload the network, requiring recovery and reselection. A blocked exit leaves the mech on its source map.

Automatic combat is off by default. Colossus keeps its distance; the others pursue and stop to fire. Manual movement takes priority; manual attack or Stop disables automatic combat.
![Monolyn Mecha](https://images.steamusercontent.com/ugc/14583897241280851335/E044B67A55D44AD4D8481505D189E1D5979E2E5E/)

## Production, repair and enemy raids

Research and manufacture units at the Construct Formator using materials and light, with a linked mechanitor and free bandwidth. Enable Auto repair and the mechanitor’s Smithing work to repair warframes. Hostile UrielCult and RaphaelCult combat raids can field all six units, using at most half the group budget. Enemy warframes bring real faction pilots and use their own light reserves.
![Monolyn Mecha](https://images.steamusercontent.com/ugc/13286242205039120716/D6208B33079CD4792200948384EB5467D58B4B34/)

## Requirements, languages and Multiplayer

Requires RimWorld 1.6 / 1.7, Harmony, Humanoid Alien Races and Monolyn Race; load after these prerequisites. Constructs and autonomy require Biotech. Includes Chinese, English, Japanese, French and Russian, with optional Multiplayer support on 1.6 only. All players must update the complete mod and restart.
