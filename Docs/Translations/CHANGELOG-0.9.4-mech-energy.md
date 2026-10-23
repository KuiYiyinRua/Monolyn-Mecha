# 0.9.4 hotfix · 2026-10-24 · 2026-10-24-mech-energy

## 简体中文

- 修复制造大型战甲后打开机械族页面时反复出现空引用红字、能源栏无法显示的问题。四种大型战甲现在显示实际光芒储量。
- 问题来自原版能源栏读取战甲不存在的电量需求；启用和移除 Ancot Library 窗口补丁时均可复现。无需停用 Ancot Library，能源消耗与存档格式保持不变。
- 1.6 隔离实机验证通过 66 项检查，覆盖本模组六单位和原版搬运机。1.6/1.7 编译通过；此次热修复未做 1.7 实机或联机测试。更新完整模组并重启游戏。

## English

- Fixes repeated null-reference errors and missing energy bars when opening the Mechs tab after creating a warframe. All four warframes now display their actual stored light.
- The vanilla column accessed a battery need these warframes do not have. The error reproduces with or without Ancot Library’s window postfix. Ancot Library can stay enabled; energy consumption and save format are unchanged.
- 66 checks passed in isolated 1.6 gameplay, covering all six mod units and a vanilla lifter. Both 1.6/1.7 builds passed; this hotfix has not been runtime-tested in 1.7 or multiplayer. Update the complete mod and restart.

## 日本語

- 大型メカ製造後、メカノイド画面でNullReferenceExceptionが繰り返され、エネルギー欄が表示されない問題を修正。4種の大型メカには実際の光貯蔵量を表示します。
- 原因はバニラの欄が存在しない電力欲求を参照することでした。Ancot Libraryの画面パッチの有無にかかわらず再現します。Ancot Libraryを無効にする必要はなく、消費量とセーブ形式も変更しません。
- 1.6の隔離実機で本MODの6種とバニラの運搬メカを含む66項目を確認。1.6/1.7のビルドは成功しましたが、今回の修正は1.7実機・マルチプレイ未検証です。MOD全体を更新して再起動してください。

## Français

- Corrige les erreurs répétées de référence nulle et les barres d’énergie absentes dans l’onglet des mécanoïdes après la création d’un mécha. Les quatre méchas affichent désormais leur réserve réelle de lumière.
- La colonne du jeu lisait un besoin de batterie absent de ces méchas. Le problème se reproduit avec ou sans le postfix de fenêtre d’Ancot Library. Vous pouvez garder Ancot Library activé ; consommation et format des sauvegardes restent inchangés.
- 66 contrôles réussis en jeu isolé sous 1.6, couvrant les six unités du mod et un transporteur du jeu de base. Compilations 1.6/1.7 réussies ; ce correctif n’a pas été testé en jeu sous 1.7 ni en multijoueur. Mettez à jour le mod complet et redémarrez.

## Русский

- Исправлены повторяющиеся ошибки NullReferenceException и отсутствие полос энергии на вкладке механоидов после создания меха. Четыре крупных меха теперь показывают фактический запас света.
- Штатный столбец обращался к отсутствующей у этих мехов потребности в заряде. Ошибка воспроизводится как с оконным постфиксом Ancot Library, так и без него. Отключать Ancot Library не нужно; расход энергии и формат сохранений не изменены.
- В изолированной игре 1.6 пройдены 66 проверок для всех шести единиц мода и штатного транспортного меха. Сборки 1.6/1.7 успешны; этот патч не проверялся в игре 1.7 или мультиплеере. Обновите мод целиком и перезапустите игру.
