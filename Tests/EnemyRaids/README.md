# 敌袭隔离实机验证

针对 RimWorld 1.6.4850、当前安装的 Monolyn Race、Harmony 与 Humanoid Alien Races。
探针仅在独立测试游戏中安装，且需要 `-mm-enemy-raids-test` 或 `-mm-enemy-raids-cold` 开关。

先把产品编译到独立 Candidate 目录，并复制同版 About、Defs、Patches、Languages、Textures 和 LoadFolders.xml。
再编译 `Probe.csproj`，通过 `MechaAssembly` 指向该候选 DLL；项目默认 GameRoot 为当前模组上溯两级的游戏根目录。

```powershell
dotnet build .\Tests\EnemyRaids\Probe.csproj -c Release -p:MechaAssembly=<候选DLL绝对路径>
.\Tests\EnemyRaids\Run.ps1 -GameRoot <游戏根目录> -Label SP-R1 -CandidateSource <候选模组目录>
.\Tests\EnemyRaids\Run.ps1 -GameRoot <游戏根目录> -Label Cold-R1 -CandidateSource <同一候选目录> -Cold -ColdSource EnemyRaids-20261017\SP-R1
```

每次使用新的 Label。控制器复制三个依赖模组，单独保存配置、日志与存档，冻结输入哈希；不修改玩家配置和存档。窗口在屏外运行，退出和兜底清理仅作用于记录过完整命令行的测试进程。

地图世界种子固定为 `MonolynEnemyRaids261017`。探针把格子温度固定为 21°C，并暂停故事叙述者的随机事件；四类被测袭击仍调用真实 `RaidEnemy.Worker.TryExecute`。这是为隔离环境伤病和随机事件，产品 DLL 不包含这些测试补丁。

加 `-DiagnosticRun` 可跳过逐台持续战斗，快速检查子树引用、支援和真实空投，并记录尚未入场的任务调用栈。该模式不能代替完整 SP 与冷读档检查。

覆盖内容：嵌套行为树引用完整性，1,200 组原生点数抽取，六种机甲可达性与禁用派系检查，真实驾驶员与初始储备，六种单位自然追击/开火，追猎者闪现队伍保持，守护者补盾，玩家能源隔离，离舱与重新登机，三种传送入场与破墙袭击，独立进程冷读档及四种战甲毁坏后的驾驶员生存与队伍归属。

射击靶只在收到真实伤害调用时记录命中，然后屏蔽该靶的损伤以允许持续测试。测试并未证明长战役平衡、完整整合包兼容或 Multiplayer 双端一致性。
