# RimWorld 1.7 compatibility · 0.9.4

本版本为 RimWorld 1.7 增加独立的 .NET 10 程序集和加载目录。1.6 的运行文件及可选 Multiplayer 适配保留。两版共用机甲源码、贴图和五语文本；1.7 按当前游戏及 Monolyn Race 1.7 程序集重新编译。

## 安装与依赖

- 安装完整的 Monolyn-Mecha 文件夹，不能只复制 `1.7` 子目录；贴图、语言与 About 位于根目录。
- 加载顺序：Harmony 1.7 → Humanoid Alien Races 1.7 → Monolyn Race 1.7 → Monolyn: Mecha。
- 构装体和无人驾驶需要 Biotech。
- `LoadFolders.xml` 在 1.7 只加载共享资源及 `1.7`，不会加载 1.6 核心或旧 Multiplayer 适配。
- 本次验证为单机 1.7。现有 `rwmt.Multiplayer` 适配仍只用于 1.6。

## 本次验证环境

- 游戏：Steam unstable，1.7.4984 rev1242，build ID 26087487。
- 游戏安装：`G:\Steam\steamapps\common\RimWorld`。
- Monolyn Race：用户指定的 `G:\Steam\steamapps\workshop\content\294100\3742031864`，加载其 `1.7/Assemblies/ASEL.dll`。
- Harmony：官方 3.0.1.0 的 1.7 程序集。
- HAR：官方 1.7 源版本 `b715c63ea7179bd2f9b9cedcdb39233c2bb36ef0` 的本机已下载候选。
- 游戏、依赖及机甲均复制到隔离目录；使用独立存档和配置，未更改玩家存档或活动模组列表。

## 验证内容

1. 1.7 独立编译，以及 1.6 核心和 Multiplayer 工程回归编译：均零警告、零错误。
2. 原生游戏与前置的启动对照；启用机甲后全部 Harmony 补丁成功安装，敌对判定替换两处，守护者弹道拦截入口解析成功。
3. 六种单位生成、四战甲绑定菜单取消/重开/确认、原生控制组和带宽、征召、检查面板、三向材质与图标。
4. 六种单位接受真实移动指令并到达；各自武器对真实目标造成伤害，包含禁断者光球及终爆链路。
5. 守护者护盾消耗 5 种子、启动 1,800 tick 计时；六种单位通过召回按钮安全落地，精确消耗 30% 能源，货物数量保持。
6. 驾驶员通过登机作业入舱，释放不朽的 4 点带宽。暂停保存后另启进程冷读档，逐项核对机体 ID、位置、驾驶员、货物、绑定、征召、光芒、护盾、弹药及护盾计时；驾驶员可正常离舱，继续运行至少 1,000 tick。
7. 实际游戏截图确认单位、护盾和操作面板完成绘制。

最终功能结果：`BuildValidation/RimWorld17-20261021/Functional-R3`。冷读档结果：`Cold-R1`。完整日志、冻结输入 SHA-256、断言及截图均保留于同一验证目录。早期 R1 指挥目标超出原生范围、R2 对构装体贴图来源的断言不正确，已保留为失败夹具记录，不计通过。

基线已存在 `AncientHeavyBridge` 定义缺失、Monolyn 神祇命名规则冲突，以及若干音效/翻译警告。这些记录按前置对照归属，不宣称整个游戏组合零报错。本次未验证所有大型整合包、旧版 1.6 存档迁移或 1.7 联机。

## 构建

使用 .NET 10 SDK。1.7 引用位于游戏根目录的 `Assembly-CSharp.dll`、`UnityStubs.dll`、`Waylith.dll`，不再引用 Unity 版 `RimWorldWin64_Data/Managed`。

```powershell
.\Build.ps1 -GameVersion 1.7 `
  -GameRoot 'G:\Steam\steamapps\common\RimWorld' `
  -MonolynRoot 'G:\Steam\steamapps\workshop\content\294100\3742031864' `
  -HarmonyAssembly '<Harmony目录>\1.7\Assemblies\0Harmony.dll'
```

`-OutputRoot` 可将构建输出和运行覆盖文件写到指定目录；它不是完整发行包生成器。默认 1.6 构建入口保持不变，并验证 `GameRoot/Version.txt` 与所选版本一致。1.6 已发布 DLL 保留 0.9.3，新增 1.7 DLL 为 0.9.4；这是保留原 1.6 运行产物的有意安排。

1.7 核心 SHA-256：`612A330E15CFC7401652BF73AE7644D3DE3A1BF066B5DC70E7BAD302AC3E697A`。
Monolyn Race 1.7 ASEL SHA-256：`83A05361FA0CA5D71E16E828C76A4C937AFB90E2F9844AC2C8587B92144BAF91`。
