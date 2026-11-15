# Импорт моделей оружия и модулей

Скрипты, которыми сделаны 19 стволов в `Assets/_Game/Art/Weapons/Models` и 23 модуля в `Attachments`, вместе с их конфигами. Все скрипты запускаются в Blender в фоне (`blender -b --factory-startup --disable-autoexec --python <script> -- <args>`). `stage_manifest.py` запускается питоном из Blender (`<Blender>/5.x/python/bin/python.exe -I`).

`{WORK}` в `weapons_cfg.json`, `att_cfg.json` и `att_cfg.py` — рабочая папка с распакованными исходниками. Перед запуском замените его на свой путь. Все выходные пути передавайте абсолютными: относительный путь превью Blender считает от `//` и пишет, например, в `C:\renders`.

## Порядок
1. Распакуйте архивы в `{WORK}/weapons_src/<имя>/<имя>.<fbx|blend|obj>`: имя папки и файла одинаковое, без пробелов. Из `lowpoly-ar-weapon-customization-kit` взяты только модули: `split_kit.py kit.fbx {WORK}/work/kit_split.blend report.json` раскладывает набор по объектам.
2. Ствол: `weapon_prep.py weapons_cfg.json <ID> {WORK}/work/prep/<ID>.blend`, затем `weapon_finish.py weapons_cfg.json <ID> <prep.blend> {WORK}/work/fbx/<ID>.fbx {WORK}/work/entries/<ID>.json <превью.png>`.
3. Модуль: `attach_prep.py att_cfg.json <ID> {WORK}/work/att/<ID>.fbx {WORK}/work/att/<ID>.json <превью.png>`. `att_cfg.py` собирает `att_cfg.json`: совместимость, масштабы, параметры линз.
4. `stage_manifest.py -- {WORK}/work Assets/_Game/Art/Weapons/weapons_manifest_imported.json` собирает манифест. В нём:
   - подвижные части (затвор, затворная рама Deagle, цевьё R870);
   - ёмкость магазинов;
   - `compat_add` — какие из старых модулей подходят новым стволам.
5. Скопируйте FBX в `Assets/_Game/Art/Weapons/Models` и `Attachments`. Затем в Unity:
   - **Tools > FPS > Weapons > Build Weapon Data**;
   - проверка: **Tools > FPS > Weapons > Check Weapon Assembly** (сейчас 15 054 проверки, 0 ошибок).

`grid_render.py` рисует модель с сеткой в миллиметрах. По ней подбираются переопределения сокетов в `weapons_cfg.json` (мм от начала координат ствола).

## Соглашения (как у сгенерированных стволов, `Tools/Blender/weapons`)
- **Пространство ствола:** u — вперёд, v — вверх, w — вправо. Начало координат — спусковой крючок.
- **Оси в Blender:** дуло по −Y, верх +Z, право −X. Экспорт: `axis_forward='-Z'`, `axis_up='Y'`, bake space transform.
- **Части:** `Body`, `Mag`, `Bolt` / `Slide` / `Pump`, `IronSights`, `Trigger`.
- **Сокеты:** `Socket_Muzzle`, `Socket_Optic`, `Socket_Underbarrel`, `Socket_Side`, `Socket_Mag`, `Grip_R`, `Grip_L`, `Sight_Iron`, `Eject`, `Socket_Butt`.
- **`Sight_Iron`** — точка на линии прицеливания. Глаз встаёт на её оси в 8 см позади (минимум `aimDist`). Поэтому точку ставят на линию «целик — мушка»: у РПГ-7 прицел слева от трубы, `Sight_Iron` = (−190, 126, −24.5) мм.
- **Начало координат модулей:**
  - прицел — низ крепления по центру, плюс `Lens_Rear` и `Sight_Point` на задней линзе;
  - дульное устройство — задний торец, `Socket_Muzzle` спереди;
  - рукоять — верх по центру, `Grip_L`; имя начинается с `Grip_Vertical` или `Grip_Angled`;
  - магазин — верх по центру.
- Логотипов и надписей на моделях нет.

## Что не взято
- `low-poly-m4a1` и `MK14 EBR`: в игре уже есть M4A1 и M14 EBR.
- Из набора AR customization kit взяты только модули, сам ствол не используется.
