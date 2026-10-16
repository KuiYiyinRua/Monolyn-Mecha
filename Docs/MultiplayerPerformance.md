# 0.6.1 性能与 Multiplayer 兼容

本次修改针对当前安装的 RimWorld 1.6、墨诺灵本体和 MP-meow-online-shop。验证记录更新于 2026-10-14。

当前状态：最终 R6 联机验收与性能复测均通过，已于 2026-10-14 安装为 0.6.1。正式 DLL/PDB 直接复制自已测试候选，并逐文件核对 SHA-256，没有在测试后重新构建。

## 改动

- 新增按需加载的 `Monolyn.Mecha.Multiplayer.dll`。同步登乘、下机、移动、攻击、停止、自动开火、迁移、装货、卸货，以及追猎者的自动战斗/闪现、守护者的自动战斗/恢复、掠夺者的自动制弹，共 14 个入口。
- 为驾驶舱 `PilotHolder` 注册同步序列化，以所属机甲定位同一个容器。按钮菜单和选点界面仍使用玩家本地界面，最终动作通过联机指令执行。
- RimWorld 1.6 的 `Thing.DoTick` 已通过子容器遍历推进驾驶员。删除机甲里重复的 `pilots.DoTick()`，消除驾驶员每刻执行两次的问题。
- 掠夺者和禁断者读档时保留已保存的位置；机甲光芒网络由本体读档流程恢复，避免加入方在恢复过程中提前清理连接。
- 弹道起点、转向、追踪球和伤害方向使用模拟坐标；范围伤害及威胁评分使用稳定的实体编号顺序，减少画面插值、受击抖动、探身和加载顺序对模拟的影响。
- 守护者补盾搜索复用候选缓冲区，遍历原生 Pawn/建筑列表并按实际占地筛选；保留血量比例、距离、实体编号及可达性优先级。护盾查找避免构造 LINQ 临时序列。
- 光芒分配器选择改为单次遍历，保留距离及实体编号优先级，避免为寻找一个最近节点分配并排序整个候选序列。

贴图、配方、武器伤害、射程、移速、载重和制造周期没有调整。

## 与 MP-meow-online-shop 的关系

使用已安装的 MP-meow 核心 3.0.158 及 RaceTrio 1.1.11 中的墨诺灵兼容。原生 ASEL 的投射物保存、随机数隔离、延迟事件和选择排序继续由该兼容包处理；机甲模块只注册自身新增的操作和容器。43 个 MP-meow 加载文件已与隔离副本逐一校验一致，没有修改该兼容包。

加载顺序：墨诺灵本体、Multiplayer、MP-meow-online-shop、Monolyn-Mecha。未启用 Multiplayer 时，不加载机甲联机 DLL。单机核心没有 Multiplayer 程序集依赖。

联机各端需使用相同版本和加载文件。新增联机 DLL 位于 `Multiplayer/Assemblies/Monolyn.Mecha.Multiplayer.dll`；核心仍位于 `1.6/Assemblies/Monolyn.Mecha.dll`。

## 验证范围

本机两个独立 RimWorld 进程，通过真实 Multiplayer 会话验证。游戏为 1.6.4850 rev646，Multiplayer 为 0.11.5+4a3be27-dirty，API 0.6。模组组合包含官方 DLC、Harmony、Prepatcher、HAR、墨诺灵本体、现有 MP-meow 和本机甲模组；关闭异步时间、多阵营和自动存档。

| 验证 | 结果与证据 |
|---|---|
| 带追踪的冒烟测试 | 最终 R6，MMMP11：36 组双端动作，24,120 个共享 tick，通过 |
| 长时间运行 | 最终 R6，MMMP19：36 组双端动作，120,346 个共享 tick，通过 |
| 冷重连与继续战斗 | MMMP19：客户端完全退出，新配置目录重启；末态一致，再由新客户端关闭/开启自动开火并重新下达攻击指令；继续 12,120 个共享 tick，通过 |
| 总模拟及射击 | MMMP19 合计 132,466 个共享 tick；四种机甲射击次数均增长，结束时实体状态、地图与世界 RNG 一致 |
| 算法等价性 | 真实补盾与传送场景中，将新补盾查询、光芒节点查询与原始算法的选择结果比对，通过 |
| 原文件保护 | 原始源码重建与原 DLL 字节一致；82 个定义/语言/贴图文件与初始备份一致，另保留追猎者独立显示修正；19 个最终源文件/构建输入与冻结候选一致 |

12 类操作各重复 3 次：模式开关、登乘、移动、攻击、停止、下机、装货、卸货并离开落点、迁移、原生补盾任务、原生自动闪现、自动开火。覆盖不朽、巨像、掠夺者、禁断者、守护者和追猎者，包含原生成型器、真实驾驶员及光芒网络。驾驶员每刻重复推进作为失败条件检查。

MMMP19 的完整结果、双方日志、加载哈希、驱动源码、冷重连和结束快照保存在 [Evidence/MMMP19](../BuildValidation/MPPerf_20261014/Evidence/MMMP19/)，冒烟记录在 [Evidence/MMMP11](../BuildValidation/MPPerf_20261014/Evidence/MMMP11/)。

用户完整的大型整合包、已有殖民地存档、异步多地图与多阵营未列为本次已验证范围。日志保留了原始警告：启动时的 ReflectionOnly / UnityEngine.InputLegacyModule 提示也存在于原版性能基线，另有未启用可选模组的补丁跳过信息。验收检查目标补丁就绪、真实操作、状态与 RNG，以及运行异常，不表示所有依赖日志均无警告。

## 性能测量

原版基线 MMPerfB04 与最终 R6 的 MMPerfC03 均完成同存档 6,000 tick。探针、DPA 驱动、测量配置一致，输入哈希在运行前后保持不变。

最终版本保留了独立修正的 `Stalker.xml`：仅隐藏追猎者内置武器的手持网格（drawSize=0），不改弹丸。该文件在最终候选、MMMP19、MMPerfC03 与正式目录中哈希一致；其单机显示修订另见 [StalkerValidation.md](StalkerValidation.md)。本轮没有覆盖此修正。

| 测量对象 | 基线累计 ms | 0.6.1 累计 ms | 变化 | 调用次数：基线 → 新版 |
|---|---:|---:|---:|---:|
| 守护者补盾搜索 FindSupport | 123.6039 | 33.0277 | −73.28% | 268 → 268 |
| 光芒节点查询 Covering | 3.6199 | 0.4460 | −87.68% | 132 → 132 |
| 网络重连 ReconnectLight | 4.7427 | 1.6839 | −64.49% | 132 → 132 |
| 护盾查找 GuardianShield.Find | 12.0089 | 2.7007 | −77.51% | 3082 → 1608 |

表中是 DPA 导出采样窗口内的累计测量值，不是全部 6,000 tick 的总执行耗时。护盾查找的调用次数也发生变化，不能把该行降幅全部归因于单次调用速度。测试使用 Normal 常速，TPS 为 57.91 → 57.73，因此不宣称整体 TPS 或整合包性能提升相同比例。

原始日志与分析数据在 [Evidence/MMPerfB04](../BuildValidation/MPPerf_20261014/Evidence/MMPerfB04/) 和 [Evidence/MMPerfC03](../BuildValidation/MPPerf_20261014/Evidence/MMPerfC03/)，解析对照在 [performance-comparison.txt](../BuildValidation/MPPerf_20261014/performance-comparison.txt)。

## 保留的反例与驱动修正

- MMMP04 在原重复推进逻辑下测得驾驶员同一 tick 的 count=2；修正后将该条件作为失败门禁。
- MMMP07 证明落点被搬运者占据时迁移被原生规则拒绝；测试改用真实 Goto 清空落点，正式模组没有放宽占地限制。
- MMMP14/16 的首窗口命令失步来自测试驱动直接发送 CreateJoinPoint，绕过服务器命令历史切换。MMMP16 双方地图/世界 RNG 完全相同，加入端却遗漏 4 条命令；重放中存在 241 条过期地图命令和 538 条过期世界命令。改用原生 /joinpoint，并检查服务器完成及过期队列。原始失步包和诊断保存在 Evidence/MMMP16，没有修改或绕过 Multiplayer 校验。
- MMMP17/18 分别完成长测和 3 次冷加载，但后续恢复阶段因驱动遗漏速度恢复、未重新指定普通建筑靶而失败，因此不计作完整通过。客户端退出的原生速度投票会暂停游戏；普通 Building 不实现 IAttackTarget，开火开关清除指定目标后不会自动重新选择该靶。最终 MMMP19 通过原生速度、开关和攻击命令完成全部阶段。

## 构建与备份

`Build.ps1 -OutputRoot <目录>` 同时构建核心和可选联机 DLL，便于冻结候选后验证。原文件和哈希保存在 `BuildValidation/MPPerf_20261014/Original` 与 `original-hashes.json`。

回归驱动位于 `BuildValidation/MPPerf_20261014/Harness`，只在隔离游戏副本通过专用命令行参数运行，不放入正式加载目录。

最终安装清单：[installed-artifacts.json](../BuildValidation/MPPerf_20261014/installed-artifacts.json)。核心 SHA-256：`256D155B50E05ADF14524097B1CEFB735DE6197632A9CDC3D9B2FFED615B564B`；联机 DLL：`C5E0BBAA6FB6CED540C30618EC797AAEAEE01A1362F56707994FD66F225F62D4`。

