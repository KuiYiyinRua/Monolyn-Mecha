# 追猎者导航与驾驶机甲自动战斗实机回归

使用本机 RimWorld 1.6 的真实游戏进程、原生寻路和生产武器。测试辅助模组只装在独立临时游戏目录；使用单独的 `-savedatafolder`，不读取或写入玩家存档。不要把 `Monolyn.Mecha.Probe.dll` 放进正式模组。

## 覆盖范围

- `Navigation`：关闭慢门、常开门的往返巡逻；开关自动闪现分别追过门、绕过一格宽拐角、从自动关门处撤退；死胡同近敌且无退路时原地射击。断言剩余路径中的前进闪现、禁止门格落点、实际射击、路径完成、折返次数和门格停留时间。
- `Mecha`：四机体真实驾驶舱登机；带驾驶员光芒迁移；身份、货物、护盾、光芒和弹药保留；覆盖与冷却限制；绕墙追击；巨像边走边横扫及非撤退换位；另三种近敌不拉扯；禁断者光球移动锁；手动移动优先与停止关闭 AI。
- `Mecha -Cold`：结束前保存四个有人驾驶的机体，重新启动游戏并读档，核对驾驶员身份/容器、货物、生命、传送冷却及自动战斗开关。

目标敌人固定位置且免伤，以便持续测量同一地形；寻路、开门、攻击和武器消耗仍由生产代码执行。该测试证明上述受控单机情形，不等同于整合包全量或 Multiplayer 双端验证。

## 运行

需要可用的 `dotnet`、本机游戏和现有 Harmony、HAR、墨诺灵依赖。先将待测模组的 `Source`、`About`、`1.6`、`Multiplayer`、`Languages`、`Textures`、`LoadFolders.xml`、`Build.ps1` 复制到新的候选目录，再从该目录编译。候选冻结后不要修改；每次运行使用从未存在过的 Label。

在模组根目录执行，以下 `Candidate-NEW` 和 Label 需替换成新的名称：

```powershell
$mod = (Get-Location).Path
$gameRoot = (Resolve-Path -LiteralPath '..\..').Path
$candidate = Join-Path $mod 'BuildValidation\StalkerNavigation-20261016\Candidate-NEW'
& (Join-Path $candidate 'Build.ps1') -GameRoot $gameRoot -OutputRoot $candidate
dotnet build 'Tests\StalkerNavigation\Probe.csproj' -c Release "-p:GameRoot=$gameRoot" "-p:MechaAssembly=$candidate\1.6\Assemblies\Monolyn.Mecha.dll"
& 'Tests\StalkerNavigation\Run.ps1' -GameRoot $gameRoot -CandidateSource $candidate -Mode Navigation -Label 'Navigation-NEW'
& 'Tests\StalkerNavigation\Run.ps1' -GameRoot $gameRoot -CandidateSource $candidate -Mode Mecha -Label 'Mecha-NEW'
& 'Tests\StalkerNavigation\Run.ps1' -GameRoot $gameRoot -CandidateSource $candidate -Mode Mecha -Cold -ColdSource 'StalkerNavigation-20261016\Mecha-NEW' -ColdSave 'MechaCombatState' -Label 'Mecha-Cold-NEW'
```

运行器优先对照本地 `BuildValidation/NativeBaselineDiagnostics.txt` 检查已存在的上游诊断；没有本地记录时，使用测试目录随附的同名基线。更换游戏或依赖后需重新核对基线，不能把新异常直接加入允许列表。历史实机日志与截图保存在本机 `BuildValidation`，不随 Git 仓库分发。进程超时或内存不足时只终止本次创建、且路径/命令行/创建时间全部匹配的进程。

结果在 `BuildValidation/StalkerNavigation-20261016/<Label>/`：`result.json`、`evidence.txt`、`trace.txt`、`game.log`、`inputs.json`、`process.json`、截图及独立存档。只有断言完成、没有新增运行错误且输入哈希一致才算通过。历史运行器统一使用 `PASS_MECHA_SP` 状态名，具体场景以 Label 和 evidence 为准。

## 必须保留的回归约束

1. 闪现只沿尚未消费的前进路径，不能落在门格。检查整段战术路线，不能只检查终点可站立或直线视野。
2. 自动关门产生的短暂射界不能中断穿门追击。没有安全后撤位置时射击，不能先走近敌人再折返。
3. 机甲移动必须区分追击、撤退和换位。取得射界可以结束追击，但不能立刻取消正在执行的掩体换位。
4. 实际武器继续负责消耗、连射/横扫、光球生命周期。AI 不生成替代武器，也不重置已付费光球。
5. 验证后部署同一份候选 DLL/PDB，并核对正式目录哈希；探针和测试存档不得混入正式加载目录。
