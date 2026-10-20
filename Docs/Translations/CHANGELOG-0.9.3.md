# 0.9.3 · Disruptor multiplayer revision · 2026-10-21

## 简体中文

- 禁断者脉冲与终爆按固定目标顺序结算，多格目标每轮只受击一次；视觉随机数与战斗随机状态隔离。
- 光球终爆改为第 300 tick 一次结算，不再生成分多刻传播的原生 Explosion。保留 5 秒持续时间、范围、敌我筛选及原生伤害处理。
- 基础伤害：脉冲 5→10（40/秒）、终爆 23→28、近战 12→17、自毁 80→85。其他战甲自毁仍为 80。
- 编译及静态核验通过，尚未双端实测，不能保证此次失同步已经消除。所有玩家更新完整模组并重启。

## English

- Disruptor pulses and the final blast resolve targets in a fixed order, hitting multi-cell targets once per event. Visual randomness is isolated from combat.
- The final blast resolves once at tick 300 instead of creating a native Explosion that propagates over later ticks. The five-second lifetime, radius, enemy filtering and native damage processing remain.
- Base damage: pulse 5→10 (40/s), final blast 23→28, melee 12→17, self-destruct 80→85. Other warframes retain 80 self-destruct damage.
- Compilation and static checks passed; this revision has not been tested with two clients and is not a confirmed desync fix. Every player must update the complete mod and restart.

## 日本語

- ディスラプターの継続攻撃と最終爆発を固定順で処理し、複数マスの対象にも各回1回だけ命中。視覚効果の乱数を戦闘から分離しました。
- 最終爆発は300 tick時点で一括処理し、後続tickに伝播する原生Explosionを生成しません。持続5秒、範囲、敵のみの判定、原生ダメージ処理を維持します。
- 基本ダメージ：継続5→10（毎秒40）、最終爆発23→28、近接12→17、自爆80→85。他の戦甲の自爆は80のままです。
- コンパイル・静的確認のみ完了。2クライアント実機検証は未実施で、同期ずれの解消は未確認です。全員が完全なMODを更新し再起動してください。

## Français

- Les impulsions et l’explosion finale du Disrupteur traitent les cibles dans un ordre fixe, une fois par cible occupant plusieurs cases et par attaque. L’aléatoire visuel est isolé du combat.
- L’explosion finale est résolue au tick 300, sans créer d’Explosion native se propageant aux ticks suivants. Durée de 5 secondes, rayon, sélection des ennemis et traitement natif des dégâts conservés.
- Dégâts de base : impulsion 5→10 (40/s), explosion 23→28, mêlée 12→17, autodestruction 80→85. Les autres méchas restent à 80 en autodestruction.
- Compilation et contrôles statiques réussis ; aucune validation à deux clients. La disparition de la désynchronisation n’est pas confirmée. Tous les joueurs doivent mettre à jour le mod complet et redémarrer.

## Русский

- Импульсы и финальный взрыв Дизраптора обрабатывают цели в фиксированном порядке; многоклеточная цель получает один удар за событие. Визуальный генератор случайных чисел изолирован от боя.
- Финальный взрыв рассчитывается целиком на тике 300, без отдельного штатного Explosion с распространением по следующим тикам. Сохранены длительность 5 секунд, радиус, выбор врагов и штатная обработка урона.
- Базовый урон: импульс 5→10 (40/с), взрыв 23→28, ближний бой 12→17, самоуничтожение 80→85. У остальных мехов самоуничтожение остаётся 80.
- Компиляция и статические проверки пройдены; проверки с двумя клиентами не было. Устранение рассинхронизации не подтверждено. Всем игрокам нужно обновить весь мод и перезапустить игру.
