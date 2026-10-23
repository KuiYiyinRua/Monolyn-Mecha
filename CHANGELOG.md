# 更新记录 / Changelog

## 0.9.4 hotfix · Mechs energy column · 2026-10-24

- 修复制造大型战甲后打开机械族页面时反复出现空引用红字、能源栏无法显示的问题。四种大型战甲现在显示实际光芒储量。
- 问题来自原版能源栏读取战甲不存在的电量需求；启用和移除 Ancot Library 窗口补丁时均可复现。无需停用 Ancot Library，能源消耗与存档格式保持不变。
- 1.6 隔离实机验证通过 66 项检查，覆盖本模组六单位和原版搬运机。1.6/1.7 编译通过；此次热修复未做 1.7 实机或联机测试。更新完整模组并重启游戏。
- 发布修订 `2026-10-24-mech-energy`；沿用已实测 DLL 与 0.9.4 模组版本。[五语更新说明](Docs/Translations/CHANGELOG-0.9.4-mech-energy.md)。

## 0.9.4 · RimWorld 1.7 · 2026-10-21

- 新增 1.7 独立 .NET 10 程序集、Defs 和加载路径；按 Monolyn Race 1.7 编译，保留原 1.6 运行文件。
- 构建脚本支持选择游戏版本与外部前置路径，拒绝误用不同版本的游戏程序集。
- 1.7.4984 rev1242 隔离单机功能验证 94 项通过；冷读档状态一致，继续运行至少 1,000 tick。覆盖六种单位的移动、命中、召回、带宽、货物、驾驶员与守护者护盾。
- 现有 Multiplayer 适配只用于 1.6。本次未验证 1.7 联机或 1.6 存档跨版本迁移。
- [五语更新说明](Docs/Translations/CHANGELOG-0.9.4.md)；[环境、基线诊断和完整验证范围](Docs/RimWorld17.md)。

## 0.9.3 · Disruptor multiplayer revision · 2026-10-21

### 简体中文

- 禁断者脉冲与终爆按固定目标顺序结算，多格目标每轮只受击一次；视觉随机数与战斗随机状态隔离。
- 光球终爆改为第 300 tick 一次结算，不再生成分多刻传播的原生 Explosion。保留 5 秒持续时间、范围、敌我筛选及原生伤害处理。
- 基础伤害：脉冲 5→10（40/秒）、终爆 23→28、近战 12→17、自毁 80→85。其他战甲自毁仍为 80。
- 编译及静态核验通过，尚未双端实测，不能保证此次失同步已经消除。所有玩家更新完整模组并重启。

### English

- Disruptor pulses and the final blast resolve targets in a fixed order, hitting multi-cell targets once per event. Visual randomness is isolated from combat.
- The final blast resolves once at tick 300 instead of creating a native Explosion that propagates over later ticks. The five-second lifetime, radius, enemy filtering and native damage processing remain.
- Base damage: pulse 5→10 (40/s), final blast 23→28, melee 12→17, self-destruct 80→85. Other warframes retain 80 self-destruct damage.
- Compilation and static checks passed; this revision has not been tested with two clients and is not a confirmed desync fix. Every player must update the complete mod and restart.

### 日本語

- ディスラプターの継続攻撃と最終爆発を固定順で処理し、複数マスの対象にも各回1回だけ命中。視覚効果の乱数を戦闘から分離しました。
- 最終爆発は300 tick時点で一括処理し、後続tickに伝播する原生Explosionを生成しません。持続5秒、範囲、敵のみの判定、原生ダメージ処理を維持します。
- 基本ダメージ：継続5→10（毎秒40）、最終爆発23→28、近接12→17、自爆80→85。他の戦甲の自爆は80のままです。
- コンパイル・静的確認のみ完了。2クライアント実機検証は未実施で、同期ずれの解消は未確認です。全員が完全なMODを更新し再起動してください。

### Français

- Les impulsions et l’explosion finale du Disrupteur traitent les cibles dans un ordre fixe, une fois par cible occupant plusieurs cases et par attaque. L’aléatoire visuel est isolé du combat.
- L’explosion finale est résolue au tick 300, sans créer d’Explosion native se propageant aux ticks suivants. Durée de 5 secondes, rayon, sélection des ennemis et traitement natif des dégâts conservés.
- Dégâts de base : impulsion 5→10 (40/s), explosion 23→28, mêlée 12→17, autodestruction 80→85. Les autres méchas restent à 80 en autodestruction.
- Compilation et contrôles statiques réussis ; aucune validation à deux clients. La disparition de la désynchronisation n’est pas confirmée. Tous les joueurs doivent mettre à jour le mod complet et redémarrer.

### Русский

- Импульсы и финальный взрыв Дизраптора обрабатывают цели в фиксированном порядке; многоклеточная цель получает один удар за событие. Визуальный генератор случайных чисел изолирован от боя.
- Финальный взрыв рассчитывается целиком на тике 300, без отдельного штатного Explosion с распространением по следующим тикам. Сохранены длительность 5 секунд, радиус, выбор врагов и штатная обработка урона.
- Базовый урон: импульс 5→10 (40/с), взрыв 23→28, ближний бой 12→17, самоуничтожение 80→85. У остальных мехов самоуничтожение остаётся 80.
- Компиляция и статические проверки пройдены; проверки с двумя клиентами не было. Устранение рассинхронизации не подтверждено. Всем игрокам нужно обновить весь мод и перезапустить игру.

## 0.9.2 · Caravan and tower transit revision · 2026-10-19

### 简体中文

- 不朽、巨像、掠夺者和禁断者可在远征队的动物分组中选择，支持空舱、有人驾驶和机械师控制。机械师无需同行，护卫模式也不会自动取消机甲的勾选。仍需至少一名舱外殖民者带队。更新后重启游戏，联机全员同步模组文件。
- 机甲有驾驶员或绑定机械师时，可以通过已连接的灯塔往返。保留驾驶员、货物与绑定；按完整机体占地选择出口，出口堵塞时留在原地图。仍需能源并遵守灯塔网络限制。

### English

- Immortal, Colossus, Reaver and Disruptor can be selected in the caravan's Animals section while empty, piloted or autonomous. Their mechanitor can stay home, including in Escort mode. At least one colonist outside the cockpits must accompany the caravan. Restart after updating; all players need the same mod files.
- Mechs with a pilot or an assigned mechanitor can travel both ways through connected towers, retaining their pilots, cargo and links. Arrival checks the full chassis footprint; blocked exits leave the mech on its source map. Energy and tower network restrictions still apply.

### 日本語

- イモータル、コロッサス、リーヴァー、ディスラプターを、無人・有人操縦・自律制御のいずれでもキャラバンの動物欄から選択できます。護衛モードでもメカニターの同行は不要です。操縦席の外にいる入植者が最低1人同行する必要があります。更新後は全員が同じMODファイルを使用し、ゲームを再起動してください。
- 搭乗者またはリンク済みのメカニターがいるメカは、接続された塔を往復できます。搭乗者・貨物・リンクを保持し、機体全体が収まる出口を選びます。出口が塞がっている場合は元のマップに残ります。エネルギーと塔のネットワーク制限は引き続き適用されます。

### Français

- L’Immortel, le Colosse, le Ravageur et le Disrupteur peuvent être sélectionnés dans la section Animaux des caravanes, qu’ils soient vides, pilotés ou autonomes. Leur mécaniteur peut rester à la colonie, même en mode Escorte. Au moins un colon hors des cockpits doit accompagner la caravane. Redémarrez après la mise à jour ; tous les joueurs doivent utiliser les mêmes fichiers du mod.
- Les méchas avec un pilote ou un mécaniteur lié peuvent faire l’aller-retour entre des tours connectées, en conservant pilotes, cargaison et liens. La sortie doit accueillir tout le châssis ; sinon, le mécha reste sur sa carte de départ. Les restrictions d’énergie et du réseau des tours restent applicables.

### Русский

- Бессмертного, Колосса, Разорителя и Дизраптора можно выбрать в разделе животных каравана: с пустой кабиной, с пилотом или на автономном управлении. Механитор может остаться в колонии, в том числе при режиме сопровождения. В караване должен быть хотя бы один колонист вне кабины. После обновления перезапустите игру; у всех игроков должны быть одинаковые файлы мода.
- Мехи с пилотом или привязанным механитором могут перемещаться между соединёнными башнями в обе стороны, сохраняя пилотов, груз и связь. Проверяется вся площадь корпуса; при заблокированном выходе мех остаётся на исходной карте. Требования к энергии и сети башен сохраняются.

## 0.9.2 · Recall revision · 2026-10-19

### 简体中文

- 修复大型机甲远征入图掉队；六种机械体新增折跃至已绑定机械师，同图消耗满容量30%，跨图50%。机械师菜单可召回仍保留绑定的掉队单位。
- 已通过隔离双端、普通和异步双地图120,120 tick长测及三次冷重连；未覆盖所有整合包存档。更新后全员同步完整模组并重启。

### English

- Fixed large mechs being left behind when a caravan enters a map. All six units can warp to their assigned mechanitor for 30% of maximum energy on the same map or 50% across maps. The mechanitor menu also recalls still-bound stranded units.
- Passed isolated host/client tests, normal and asynchronous two-map soaks of 120,120 ticks, and three cold rejoins. Not every modpack/save is covered. All peers must update the complete mod and restart.

### 日本語

- 大型機体がキャラバンのマップ進入時に取り残される不具合を修正。全6種が接続済みメカニターへ転移でき、同一マップは最大容量の30%、別マップは50%を消費。接続の残る機体はメカニターのメニューから呼び戻せます。
- 隔離した2クライアント、通常・非同期2マップ各120,120 tick、3回のコールド再接続を検証済み。全MOD構成・セーブの保証ではありません。全員が完全なMODを更新し再起動してください。

### Français

- Correction des grands méchas laissés hors carte à l’arrivée d’une caravane. Les six unités peuvent rejoindre leur mécaniteur lié pour 30 % de capacité maximale sur la même carte ou 50 % entre cartes. Son menu rappelle les unités égarées encore liées.
- Tests isolés hôte/client réussis, deux cartes en temps normal et asynchrone pendant 120 120 ticks chacune, puis trois reconnexions à froid. Tous les modpacks/sauvegardes ne sont pas couverts. Tous les joueurs doivent mettre à jour le mod complet et redémarrer.

### Русский

- Исправлена потеря крупных мехов при входе каравана на карту. Все шесть типов прыгают к связанному механитору за 30% полной ёмкости на той же карте или 50% между картами; меню механитора возвращает отставших с сохранённой связью.
- Пройдены изолированные тесты двух клиентов, обычные и асинхронные две карты по 120 120 тиков и три холодных переподключения. Не все сборки и сохранения проверены. Всем игрокам обновить полный мод и перезапустить игру.

## 0.9.2 · 2026-10-19

### 简体中文

- 守护者「护盾立场」单次种子消耗由10降为5，施放所需的最低储量同步调整为5。五种语言的技能说明和种子不足提示同步更新。
- 更新后重启游戏，联机所有玩家同步完整模组文件。

### English

- Reduced the Guardian’s Shield Field cost from 10 to 5 seeds per activation. The minimum reserve required to activate it is now 5. Updated ability descriptions and insufficient-seed messages in all five languages.
- Restart the game and use matching complete mod files on every Multiplayer peer.

### 日本語

- ガーディアンのシールドフィールドの種子消費を1回10個から5個に変更。発動に必要な最低備蓄量も5個になりました。5言語のスキル説明と種子不足メッセージを更新しました。
- ゲームを再起動し、Multiplayerの全員が同じ完全なMODファイルを使用してください。

### Français

- Le coût du champ de bouclier du Gardien passe de 10 à 5 graines par activation. La réserve minimale nécessaire est désormais de 5 graines. Les descriptions et les messages de réserve insuffisante sont mis à jour dans les cinq langues.
- Redémarrez le jeu et utilisez les mêmes fichiers complets du mod sur tous les clients Multiplayer.

### Русский

- Стоимость активации щита Стража снижена с 10 до 5 семян. Для активации теперь требуется запас минимум в 5 семян. Описания способности и сообщения о нехватке семян обновлены на всех пяти языках.
- Перезапустите игру и установите одинаковые полные файлы мода у всех участников Multiplayer.

## 0.9.1 · 2026-10-19

### 简体中文

- 修复不朽、巨像、掠夺者和禁断者有驾驶员时被原版误判为未受控机械体，导致自动开火和自动战斗找不到敌人的问题。无人驾驶继续使用原版机械师控制及带宽判断。
- 关闭自动战斗时清除旧武器目标，修复禁断者在等待或放球阶段关闭开关后仍继续放球的问题；已释放的球体按原持续时间结束。
- 更新后重启游戏，联机所有玩家同步完整模组文件。

### English

- Fixed piloted Immortals, Colossi, Reavers and Disruptors being treated as uncontrolled mechs, leaving automatic fire and combat unable to find enemies. Autonomous warframes retain native mechanitor control and bandwidth checks.
- Disabling automatic combat now clears its weapon target even while a Disruptor is waiting or sustaining an orb, preventing further automatic casts. Existing orbs finish their normal lifetime.
- Restart the game and use matching complete mod files on every Multiplayer peer.

### 日本語

- 操縦者がいるイモータル、コロッサス、リーヴァー、ディスラプターが未制御メカと判定され、自動射撃・自動戦闘で敵を発見できない問題を修正。無人操縦時は従来どおりメカニターの制御と帯域幅を確認します。
- 自動戦闘を解除すると武器の旧標的も消去します。ディスラプターが待機中や球体展開中でも、解除後に次の球体を発射しません。展開済みの球体は本来の時間まで持続します。
- ゲームを再起動し、Multiplayerの全員が同じ完全なMODファイルを使用してください。

### Français

- Correction des Immortels, Colosses, Ravageurs et Disrupteurs pilotés considérés à tort comme des méchas non contrôlés : le tir et le combat automatiques peuvent à nouveau détecter les ennemis. Le contrôle autonome conserve les vérifications natives du mécaniteur et de sa bande passante.
- Désactiver le combat automatique efface aussi sa cible d’arme, même si le Disrupteur attend ou maintient un orbe. Aucun nouvel orbe automatique n’est lancé ; les orbes existants terminent leur durée normale.
- Redémarrez le jeu et utilisez les mêmes fichiers complets du mod sur tous les clients Multiplayer.

### Русский

- Исправлено: мехи Immortal, Colossus, Reaver и Disruptor с пилотом ошибочно считались неуправляемыми и не находили врагов для автоматического огня и боя. В автономном режиме сохранены стандартные проверки управления механитором и пропускной способности.
- Отключение автоматического боя очищает прежнюю цель оружия даже во время ожидания или действия сферы Disruptor. Новые сферы автоматически не запускаются; уже созданные действуют до конца обычного срока.
- Перезапустите игру и установите одинаковые полные файлы мода у всех участников Multiplayer.

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
