# 更新记录 / Changelog

## 0.9.0 · 2026-10-18

### 简体中文

- 守护者带宽2，种子储备30，可调目标储量及自动搬运。护盾消耗10种子，半径8格、持续30秒；敌弹50%原路反弹，其余穿过，多盾独立叠加。
- 无人驾驶绑定有足够带宽的机械师：不朽4、巨像6、掠夺者6、禁断者5；加入机械体控制组，可征召指挥，仍耗光芒。登机解除绑定。
- 四战甲新增自爆确认及50格内选点弹射。驾驶员保留个人装备物品；机甲与货舱销毁，不留尸体。爆炸半径8、基础伤害80、穿甲80%，有友伤；近处落点有风险。空舱确认即爆并释放带宽，取消不引爆。
- 五语介绍与更新记录同步，含Multiplayer界面同步。更新后重启，联机全员使用相同完整模组。

### English

- Guardian: 2 bandwidth, 30-seed storage, target slider and automatic hauling toggle. Shield costs 10 seeds, lasts 30 seconds and has radius 8. Hostile shots have a 50% chance to reflect; the rest pass through. Overlapping fields roll independently.
- Autonomy: bind a mechanitor with free bandwidth — Immortal 4, Colossus 6, Reaver 6, Disruptor 5. Uses mech groups and drafting, still consumes light; boarding releases the binding.
- Self-destruct: confirm, then eject the pilot to a valid cell within 50. Personal gear/items survive; chassis and cargo are destroyed without a corpse. Radius 8, base damage 80, 80% armor penetration, friendly fire; nearby landings are risky. Empty chassis detonate on confirmation and release bandwidth. Cancellation is safe.
- Descriptions and notes updated in five languages, with Multiplayer UI synchronization. Restart and use matching complete mod files on all peers.

### 日本語

- ガーディアン：帯域幅2、種子30個、目標量スライダーと自動運搬切替。種子10個で半径8マス、30秒のシールド。敵弾50%反射、残りは通過。重複シールドは独立判定。
- 無人操縦：空き帯域幅のあるメカニターに接続。イモータル4、コロッサス6、リーヴァー6、ディスラプター5。メカグループと徴兵に対応し、光を消費。搭乗で接続解除。
- 自爆：確認後、50マス以内へ操縦者を射出。個人装備・所持品は保持し、機体と貨物は残骸なしで消滅。爆発半径8、基本ダメージ80、貫通80%。味方も被害を受け、近距離着地は危険。空機は確認直後に爆発して帯域幅を解放。キャンセルは安全。
- 紹介と更新履歴を5言語で更新。MultiplayerのUI同期に対応。再起動し、全員同じ完全なMODを使用してください。

### Français

- Gardien : bande passante 2, réserve de 30 graines, curseur et réapprovisionnement automatique réglables. Bouclier : 10 graines, rayon 8, durée 30 secondes. Réflexion de 50% des tirs ennemis, les autres passent. Champs superposés indépendants.
- Autonomie : mécaniteur avec bande passante libre — Immortel 4, Colosse 6, Ravageur 6, Disrupteur 5. Groupes de méchas et mobilisation, consommation de lumière maintenue ; embarquer rompt le lien.
- Autodestruction : confirmation puis éjection du pilote à 50 cases maximum. Équipement et inventaire personnels conservés ; châssis et fret détruits sans cadavre. Rayon 8, dégâts de base 80, pénétration 80%, tirs amis ; atterrissage proche dangereux. À vide, explosion dès confirmation et libération de la bande passante. Annulation sans explosion.
- Descriptions et notes en cinq langues ; interface synchronisée avec Multiplayer. Redémarrez et utilisez les mêmes fichiers complets chez tous les joueurs.

### Русский

- Страж: пропускная способность 2, запас 30 семян, ползунок цели и автопополнение. Щит: 10 семян, радиус 8, 30 секунд. 50% вражеских снарядов отражаются, остальные проходят. Поля перекрываются независимо.
- Беспилотный режим: механитор со свободной пропускной способностью — Бессмертный 4, Колосс 6, Разоритель 6, Дизраптор 5. Группы мехов, призыв и команды; свет расходуется. Посадка пилота разрывает связь.
- Самоуничтожение: подтверждение, затем катапультирование до 50 клеток. Личные вещи сохраняются; мех и груз уничтожаются без останков. Радиус 8, базовый урон 80, пробитие 80%, урон союзникам; близкая посадка опасна. Пустой мех взрывается после подтверждения и освобождает ресурс. Отмена безопасна.
- Описание и журнал обновлены на пяти языках, интерфейс синхронизируется в Multiplayer. Перезапустите игру; всем нужны одинаковые полные файлы.


## 0.8.0 · 2026-10-18 护盾立场与无人驾驶

- 守护者带宽2，种子储备上限30；可拉条设置目标储量，开关殖民者自动搬运。
- 护盾立场：半径8格、持续30秒、消耗10种子；敌方投射物50%原路反弹，未反弹的继续通过；重叠护盾各自独立判定。
- 四种战甲增加机械师绑定与无人驾驶：不朽4、巨像6、掠夺者6、禁断者5带宽；使用原生机械体关系、分组、征召和指挥范围。驾驶员登机解除绑定。
- UI复用原生燃料滑条、搬运开关及其Multiplayer同步；新增施放与绑定/解绑同步入口，存档保存全部新状态。
- 修复原动物型战甲启用原生征召界面时缺失装备栏的问题；停放及有人驾驶的战甲不产生无主机械体警报。
- 五语说明同步。验证结果见 Docs/GuardianAutonomy.md。

## 0.7.0 · 2026-10-17 数值调整

- 巨像：五格十字激光，每束21伤害，护盾200。
- 六种单位：利器护甲+20个百分点，钝器+10个百分点。
- 禁断者：每0.25秒伤害2→5（每秒20），终爆20→23。
- 掠夺者弹仓8→10，五语说明同步。
- 编译及静态检查通过，未做本轮实机或联机复测，需重启。

## 0.7.0 · 2026-10-17 光网连线修复

- 将光网隔离补丁注册移至主线程初始化，修复选中光芒建筑时连线材质变紫的问题，保留供能与敌我光网隔离。
- 用户重启游戏后确认连线正常；自动化实机测试按用户要求中止，未完成本次双端联机复测。更新后需重启游戏。

## 0.7.0 · 2026-10-17 自动维修修订

- 修复机械师无法自动维修不朽、巨像、掠夺者和禁断者的问题；保留自动维修开关、锻造工作及战斗限制。
- 四种机甲通过正常工作调度自行接单并修满，单机实测 30 项检查通过。本修订未重新进行双端联机测试。
- 更新后需重启游戏；详情见 Docs/AutomaticRepair.md。

## 0.7.0 · 2026-10-17 墨诺灵敌袭

- 仅允许敌对 Uriel / Raphael 袭击编组抽取六种扩展机甲，保留原生点数结算，并限制机甲预算占比、最低出场点数与单种数量。
- 调整六种单位战斗点数；四种驾驶战甲包含一名真实墨诺灵驾驶员，满光芒入场，掠夺者携带初始导弹。
- 接通敌方战甲、追猎者与守护者的战斗逻辑；守护者可消耗自身储备为同阵营单位补盾，追猎者闪现保留袭击队伍。
- 阻止敌机接入玩家光芒网络；驾驶员离舱或机体被摧毁后返回原袭击队伍。玩家制造的空舱、掠夺者空弹仓与自动战斗默认设置保留。
- 补上空投入场前的待机处理，修复机甲内部嵌套行为树的子树引用，防止袭击进攻任务在空投舱内访问空地图。
- 运行验证记录见 `Docs/EnemyRaids.md`；可复跑探针位于 `Tests/EnemyRaids`。

## 0.6.3 · 2026-10-16 完整修订

- 修复追猎者自动闪现在门格落地，以及门口和窄通道追击、撤退时反复折返的问题。巡逻继续沿剩余路线前进，无安全退路时停下射击。
- 巨像、不朽、掠夺者、禁断者新增默认关闭的「自动战斗」。巨像会追击、拉开距离及移动射击；另外三种追到可射击位置后停下攻击，不主动拉扯。
- 手动移动优先；手动攻击或停止关闭自动战斗。新开关与状态支持存档，并接入可选 Multiplayer 同步入口。
- 四种驾驶机甲支持携带驾驶员一起光芒迁移，保留机体、驾驶员、货物与弹药。原有网络覆盖、空间、冷却和禁断者光球移动锁仍有效。
- 同步中、英、日、法、俄五种语言的功能说明。
- 完整包包含此前全部代码与数值调整：不朽导弹每枚 10 伤害、五格十字范围；追猎者速度 9；掠夺者射程 45；禁断者光球持续 5 秒、速度 8 格/秒，以及转向和甲瓣连续性修复。

本修订在 RimWorld 1.6.4850 的隔离单机环境完成 52 项追猎者导航、69 项四机甲行为及 9 项冷启动读档检查，合计 130 项通过。新改动未完成 Multiplayer 双端实机复测；历史联机验证不代表本修订已完成双端验证。
