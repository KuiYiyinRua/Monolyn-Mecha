# 灯塔隔离回归

`MultiplayerProbe.cs` 仅在 `-mm-tower-test` 下工作；测试程序集不放入正式模组。控制器复制并冻结候选、依赖模组和探针，使用独立存档、日志及进程身份记录，等主机 `Server started.` 后才启动客户端。全程保留玩家游戏进程。

先构建探针，再用新的 RunRoot 运行：

```powershell
dotnet build Tests/Tower/MultiplayerProbe.csproj -c Release "-p:GameRoot=<游戏目录>" "-p:MechaAssembly=<冻结候选>/1.6/Assemblies/Monolyn.Mecha.dll"
Tests/Tower/RunMP.ps1 -RunRoot <新隔离目录> -Mecha <冻结候选> -GameRoot <游戏目录> -Ticks 120000 -Port 31430
```

`-Baseline` 对未包含灯塔修复的远征修复候选重现“有人驾驶战甲无法加入入塔任务”；`-Async` 使用两个不同速度的地图；`-Rejoins 3` 运行三次客户端冷重连，每次重新执行完整功能阶段。

18 个双端阶段覆盖：无控制源拒绝、有人驾驶去回、绑定机械师且机械师留家时去回、选择后解绑拒绝、无能源／断连／拥堵拒绝、堵塞出口实际入塔尝试保留源地图、清理出口后重试。正常路径使用灯塔原生按钮和原生选择窗口确认，由原生 Lord 派发真实 EnterPortal 行走和等待作业；测试不直接调用生产传送函数完成路径。

检查六种单位的可见条目、原有 ID、库存、驾驶员容器、绑定、完整占地、源地图 Lord 与待传送清单清理。传送边界额外核对机体和驾驶员的相对时间戳。最终要求双方阶段记录与冻结快照、地图和世界 Rand 相同，且整个运行期间输入文件哈希未变。

最后补充的扫描计时断言会在真实传送前设置旧地图的非零扫描期限，检查到达时清零，并分别覆盖自动攻击／支援开启与关闭。该补充已编译，按用户要求中止的 Candidate-R3 运行不算通过；实际完成范围见 `Docs/TowerTransit.md`。
