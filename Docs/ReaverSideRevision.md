# 掠夺者侧面与六格尺寸修订 · 0.5.1

2026-10-14。侧面采用新绘制 east R4：按用户原图保留左尾右头，右端大护颊下方为主发射口，左端收窄为尾；背甲隆起、顶面与侧面遮挡以及机腹厚度贴近原参考。south R2 / north R2 继续使用。

## 实际占地与绘制

- `MM_Reaver.size=(2,3)`，地图实际注册六格；正背方向为 2×3，侧向为 3×2。四向旋转后都取消旧格注册。
- 旧版仅设置了 2×3 绘制画布，实体仍默认 1×1；透明留白也使可见机体小于画布。现在分别按三张源图的 alpha>16 轮廓计算绘制大小与中心，使可见机体接近六格占地。
- Pawn 的绘制中心补偿偶数宽度带来的半格差；炮口与贴图共用同一套像素坐标转换。朝东的发射口在原图 `(1238,731)`，其相对实际占地中心偏移约 `(+0.982402,-0.523457)`；朝西镜像。
- 保留内置 image_gen 原始 RGBA，尺寸补偿在渲染阶段完成，没有改写或拉伸源文件。实际方向绘制参数与炮口坐标见 `Artwork/Review/Reaver_SixCell_Geometry.json`。
- 地面路径按完整机体和转向空间避让障碍；可纵向通过两格宽开放通道，不能钻过一格宽通道。迁移检查六格，出口边界裁切注册以允许远行离图。

## 文件

- 生产侧图：`Textures/Things/Mecha/Reaver/Reaver_east.png`
- 版本源图：`Artwork/Reaver/Reaver_east_r4.png`
- 生成方式：内置 image_gen；完整成稿提示词：`Artwork/Reaver/prompt-east-r4.md`
- 三视图源图预览：`Artwork/Review/Reaver_ThreeViews_SideR4.png`
- 最终六格实机截图：`Artwork/Review/Reaver_SixCell_InGame_Final.png`
- 独立美术审阅：`Artwork/Review/Reaver_SideRevision_review.md`

## 验证

最终 DLL SHA256：`5535ADCBE67D926E59F086F9B69F2448C536B1D0CD9493BB0C2FDA6FBAD19687`。

`Build.ps1` 编译 0 警告、0 错误。

`BuildValidation/Reaver-Footprint-SP-R2`：60 项通过，61 秒。除原有制造、驾驶、导弹、护盾、光芒、2000kg 货运检查外，增加：

- 实际四向六格 ThingGrid 注册、旋转无残留。
- 实际行走绕过完整钢墙，逐 tick 检查机体占格没有穿墙；地图速度仍为 4。
- 两格通道可用，一格通道不生成有效路径。
- 迁移目标的非锚点格被墙挡住时拒绝迁移。
- 地图出口边界不会因多格注册而销毁机体。
- 黄色格线直接由运行时 `OccupiedRect` 投影绘制，截图没有补画占地框；四向取相同画面区域与倍率。

`BuildValidation/Reaver-Footprint-LegacyCold`：18 项通过，39 秒。使用本版 DLL 冷启动读取 0.5.0 的 `Reaver-SP-R6/Single/Saves/ReaverPiloted.rws`；保留实际驾驶员、生命、护盾、光芒、2000kg 货物、5 枚导弹、137 ticks 制造进度与冷却，并建立六格注册。世界移动仍为 4 格/天、1200 光芒/天，停车制弹及混编慢速规则通过。

前一轮 `Reaver-Footprint-SP-R1` 的 57 项检查也通过；R2 只补充测试用例，没有改变生产 DLL、贴图或绘制参数。`Reaver-SideR4-SP` 为同一 R4 贴图但尚未修正六格尺寸的历史证据，不代表当前尺寸。

最终加载文件清单：`BuildValidation/Release-0.5.1-manifest.json`。独立运行环境未改动用户配置或既有存档；运行日志没有新增加载、贴图、执行或绘制异常。本轮验证单机及旧存档迁移，不声明多人同步兼容。
