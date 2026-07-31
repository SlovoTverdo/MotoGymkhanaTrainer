# Venue Definition JSON Format

## 1. Назначение

Venue Definition описывает постоянную тренировочную площадку.

Площадка существует независимо от конкретной трассы и может многократно использоваться разными Track Project.

```text
Venue Editor
      ↓
Venue Definition
      ↓
Track Editor
      ↓
Track Project
      ↓
Track compilation
      ↓
Exported Track JSON
      ↓
Viewer
```

Venue Definition содержит:

* размеры рабочей поверхности;
* панорамное окружение;
* постоянные трёхмерные объекты;
* постоянные конусы;
* постоянную разметку.

```markdown
# Track integration

Track Project ссылается на Venue Definition через `venuePath`.

Venue Definition не хранит список Track Project и не знает, какие трассы её используют.

Во время Track compilation:

- metadata Venue копируется в export;
- area копируется в export;
- panorama копируется в export;
- Venue objects получают namespace IDs;
- Venue cones получают namespace IDs;
- Venue markings получают namespace IDs.

Исходная Venue Definition не изменяется.

Visible Venue object с unresolved `assetPath` блокирует export трассы.

Hidden Venue object с unresolved asset создаёт warning, но может не блокировать export.

Enabled panorama с отсутствующей texture блокирует export.
```

Venue Definition не содержит:

* ExerciseInstance;
* Route Order;
* trajectory конкретной трассы;
* transition splines;
* TransitionOverride;
* состояние прохождения;
* Viewer camera state.

---

# 2. Корневая библиотека

Venue Definition хранятся внутри:

```text
res://venues/
```

Допускаются произвольные вложенные подпапки.

Пример:

```text
res://venues/
├─ main-training-ground/
│  ├─ venue.json
│  └─ assets/
│     ├─ panorama.webp
│     ├─ shed.tscn
│     ├─ ramp.tscn
│     ├─ fence-section.tscn
│     └─ fence-gate.tscn
└─ empty-test-ground/
   └─ venue.json
```

Пути внутри Venue Definition должны быть Godot resource paths:

```text
res://...
```

Venue Editor должен ограничивать операции с Venue JSON каталогом:

```text
res://venues/
```

Asset может находиться в другом каталоге проекта, но предпочтительно хранить связанные ресурсы рядом с площадкой.

---

# 3. Версия формата

Текущая версия:

```json
{
  "formatVersion": 2
}
```

Venue Definition versioning независим от:

* Exercise Definition;
* Track Project;
* Exported Track JSON.

Совместимость с предварительными или незафиксированными версиями не требуется.

---

# 4. Каноническая структура

```json
{
  "formatVersion": 1,

  "venue": {
    "id": "main-training-ground",
    "name": "Основная тренировочная площадка"
  },

  "area": {
    "width": 60.0,
    "length": 100.0
  },

  "panorama": {
    "enabled": true,
    "texturePath": "res://venues/main-training-ground/assets/panorama.webp",
    "rotationDeg": 0.0,
    "energyMultiplier": 1.0
  },

  "objects": [],

  "cones": [],

  "markings": []
}
```

---

# 5. venue

```json
{
  "venue": {
    "id": "main-training-ground",
    "name": "Основная тренировочная площадка"
  }
}
```

## id

Стабильный идентификатор площадки.

Рекомендуемый формат:

```text
lowercase-latin-digits-hyphens
```

Примеры:

```text
main-training-ground
north-parking-lot
empty-test-ground
```

Изменение `venue.id` не должно автоматически переименовывать файл или папку.

## name

Отображаемое имя площадки.

Допускаются:

* пробелы;
* кириллица;
* локализованный текст.

---

# 6. area

```json
{
  "area": {
    "width": 60.0,
    "length": 100.0
  }
}
```

Размеры задаются в метрах.

Требования:

```text
width > 0
length > 0
```

Система координат Venue:

```text
X / Y
1 unit = 1 meter
origin = geometric center
```

Границы:

```text
X: -width / 2 ... +width / 2
Y: -length / 2 ... +length / 2
```

Соответствие Godot 3D:

```text
Venue X       → Godot X
Venue Y       → Godot Z
elevation     → Godot Y
rotationDeg   → rotation around Godot Y
```

Положительное значение `rotationDeg` соответствует положительному повороту, принятому в общей двумерной доменной системе проекта.

---

# 7. panorama

```json
{
  "panorama": {
    "enabled": true,
    "texturePath": "res://venues/main-training-ground/assets/panorama.webp",
    "rotationDeg": 125.0,
    "energyMultiplier": 1.0
  }
}
```

Панорама предназначена для дальнего визуального окружения.

Она должна отображаться Viewer через:

```text
PanoramaSkyMaterial
    ↓
Sky
    ↓
Environment
    ↓
WorldEnvironment
```

Отдельная цилиндрическая или сферическая MeshInstance для панорамы не требуется.

---

## 7.1. enabled

```json
"enabled": true
```

Определяет, используется ли панорама.

Если `false`:

* `texturePath` может быть пустым;
* Viewer использует стандартное окружение проекта.

---

## 7.2. texturePath

```json
"texturePath": "res://venues/main-training-ground/assets/panorama.webp"
```

Путь к эквидистантной панорамной текстуре.

Рекомендуемое соотношение сторон:

```text
2:1
```

Примеры разрешений:

```text
2048 × 1024
4096 × 2048
8192 × 4096
```

Поддерживаемые форматы определяются импортом Godot.

Рекомендуются:

* `.webp`;
* `.png`;
* `.jpg`, если артефакты сжатия приемлемы.

---

## 7.3. rotationDeg

```json
"rotationDeg": 125.0
```

Горизонтальный поворот панорамы вокруг вертикальной оси.

Используется для совмещения направлений фотографии и координат площадки.

---

## 7.4. energyMultiplier

```json
"energyMultiplier": 1.0
```

Визуальная яркость панорамы.

Требование:

```text
energyMultiplier >= 0
```

В Venue Definition version 2 панорама является прежде всего визуальным фоном.

Она не обязана быть основным источником:

* ambient lighting;
* отражений;
* экспозиции;
* освещения 3D-объектов.

Освещение Viewer настраивается отдельно.

---

# 8. objects

`objects[]` содержит постоянные объекты площадки.

Примеры:

* домик;
* эстакада;
* секция забора;
* ворота;
* фонарь;
* столб;
* дерево;
* неподвижное препятствие;
* постоянный ориентир.

Все типы представлены универсальным `VenueObjectInstance`.

---

# 9. VenueObjectInstance

Пример:

```json
{
  "objectId": "shed-main",
  "name": "Домик",

  "assetPath": "res://venues/main-training-ground/assets/shed.tscn",

  "position": {
    "x": -22.0,
    "y": 31.0
  },

  "elevation": 0.0,

  "rotationDeg": 90.0,

  "scale": {
    "x": 1.0,
    "y": 1.0,
    "z": 1.0
  },

  "footprint": {
    "width": 6.0,
    "length": 4.0
  },

  "collisionEnabled": true,
  "visibleInViewer": true
}
```

---

## 9.1. objectId

Стабильный уникальный идентификатор объекта внутри Venue Definition.

Примеры:

```text
shed-main
ramp-01
fence-section-014
north-gate
```

`objectId` не должен зависеть от позиции объекта в массиве.

---

## 9.2. name

Отображаемое имя объекта.

Используется:

* в Venue Editor;
* в properties;
* в diagnostics.

---

## 9.3. assetPath

Путь к Godot-сцене объекта:

```text
res://.../*.tscn
```

Предпочтительно использовать `.tscn`, а не напрямую `.glb`.

Сцена-обёртка может содержать:

* импортированную модель;
* материалы;
* collision shapes;
* корректирующий локальный transform;
* LOD;
* дополнительные узлы;
* pivot;
* metadata.

Venue Editor version 1 не редактирует внутреннее содержимое asset scene.

Если asset отсутствует или не загружается:

* Venue Definition остаётся открываемой;
* объект помечается unresolved;
* Editor показывает placeholder;
* остальные объекты продолжают работать;
* Save не должен молча удалять unresolved object.

---

## 9.4. position

```json
{
  "position": {
    "x": -22.0,
    "y": 31.0
  }
}
```

Позиция origin asset на площадке.

Единица:

```text
meter
```

---

## 9.5. elevation

```json
"elevation": 0.0
```

Высота объекта относительно поверхности площадки.

Соответствует:

```text
Godot Y
```

Единица:

```text
meter
```

Отрицательное значение допускается, если модель должна быть частично опущена ниже поверхности.

---

## 9.6. rotationDeg

```json
"rotationDeg": 90.0
```

Поворот вокруг вертикальной оси.

Другие оси вращения в Venue Definition version 1 не поддерживаются.

Наклон модели должен быть настроен внутри asset `.tscn`.

---

## 9.7. scale

```json
{
  "scale": {
    "x": 1.0,
    "y": 1.0,
    "z": 1.0
  }
}
```

Требования:

```text
scale.x > 0
scale.y > 0
scale.z > 0
```

Отрицательный scale и mirror не поддерживаются.

Соответствие:

```text
scale.x → Godot X
scale.y → Godot Y
scale.z → Godot Z
```

---

## 9.8. footprint

```json
{
  "footprint": {
    "width": 6.0,
    "length": 4.0
  }
}
```

Footprint — приближённый прямоугольник, занимаемый объектом на поверхности.

Он используется для:

* 2D-preview;
* selection;
* отображения занятого пространства;
* будущей проверки пересечения с ExerciseInstance;
* предупреждения о выходе объекта за границы.

Footprint не является:

* collision shape;
* точной проекцией Mesh;
* физической границей Viewer.

Требования:

```text
width > 0
length > 0
```

Footprint:

* масштабируется `scale.x` и `scale.z`;
* поворачивается `rotationDeg`;
* перемещается `position`.

Vertical scale не влияет на footprint.

---

## 9.9. collisionEnabled

```json
"collisionEnabled": true
```

Определяет, должны ли collision-узлы asset scene участвовать в Viewer.

Venue Editor не создаёт collision автоматически.

Если asset scene не содержит collision shapes, значение `true` не обязано генерировать их.

---

## 9.10. visibleInViewer

```json
"visibleInViewer": true
```

Если `false`:

* объект остаётся в Venue Definition;
* отображается в Venue Editor с editor-only признаком;
* не инстанцируется в обычном Viewer.

---

# 10. cones

`cones[]` содержит постоянные конусы площадки.

Используется тот же структурный контракт cone, что и в Exercise Definition, если он не содержит exercise-specific семантики.

Пример:

```json
{
  "id": "boundary-cone-001",
  "position": {
    "x": -28.0,
    "y": -45.0
  },
  "color": "orange"
}
```

Постоянные конусы:

* не входят в Route Order;
* не принадлежат ExerciseInstance;
* не влияют на global trajectory;
* экспортируются вместе с площадкой.

IDs должны быть уникальны внутри `venue.cones[]`.

---

# 11. markings

`markings[]` содержит постоянную разметку площадки.
# Venue Definition formatVersion 2

## Markings

Venue marking использует общий Path contract.

```json
{
  "id": "venue-boundary-guide",
  "style": "solid",
  "color": "#FFFFFFFF",
  "thickness": 0.10,
  "visibleInViewer": true,
  "path": {
    "start": {
      "x": 0.0,
      "y": 0.0
    },
    "segments": [
      {
        "type": "cubicBezier",
        "control1": {
          "x": 5.0,
          "y": 0.0
        },
        "control2": {
          "x": 5.0,
          "y": 5.0
        },
        "end": {
          "x": 10.0,
          "y": 5.0
        }
      }
    ]
  }
}
```

Path coordinates находятся в системе координат Venue.

Marking Path может проходить по:

* основной площадке;
* ramp;
* верхней поверхности Venue object;

если соответствующие точки успешно проецируются на WalkableSurface.

Venue Definition v2 не использует старый массив `points` как основной geometry contract.

Постоянная разметка:

* не принадлежит ExerciseInstance;
* не входит в trajectory;
* не трансформируется Track Editor;
* экспортируется в мировых координатах Venue.

---

# 12. Порядок отрисовки в Venue Editor

Рекомендуемый порядок 2D-preview:

1. background;
2. grid;
3. area bounds;
4. markings;
5. object footprints;
6. cones;
7. selection overlays;
8. handles и editor diagnostics.

Точный визуальный стиль не является частью JSON-контракта.

---

# 13. Unresolved assets

Venue Definition может содержать объект с отсутствующим `assetPath`.

Такой объект:

* сохраняет `objectId`;
* сохраняет transform;
* сохраняет footprint;
* отображается как placeholder;
* создаёт diagnostic warning;
* не удаляется автоматически.

Venue Editor может сохранить такой документ.

В будущей интеграции Track compilation может блокировать Viewer export либо экспортировать Venue с предупреждением — это будет определено в отдельной итерации.

---

# 14. Валидация

## 14.1. Блокирующие ошибки документа

* неподдерживаемый `formatVersion`;
* пустой `venue.id`;
* пустой `venue.name`;
* `area.width <= 0`;
* `area.length <= 0`;
* duplicate `objectId`;
* duplicate cone id;
* duplicate marking id;
* non-finite coordinates;
* non-finite scale;
* `scale.x <= 0`;
* `scale.y <= 0`;
* `scale.z <= 0`;
* `footprint.width <= 0`;
* `footprint.length <= 0`;
* невалидный marking contract;
* некорректный JSON.

## 14.2. Предупреждения

* object asset отсутствует;
* panorama texture отсутствует;
* object footprint выходит за bounds;
* cone находится за bounds;
* marking находится частично или полностью за bounds;
* footprints пересекаются;
* panorama enabled, но texturePath пуст;
* очень большой или очень маленький scale.

Warnings не должны препятствовать сохранению Venue Definition.

---

# 15. Editor-only state

Venue Definition не сохраняет:

* selected object;
* selected cone;
* selected marking;
* active tool;
* pan;
* zoom;
* expanded folders;
* temporary locks;
* Undo/Redo history;
* clipboard;
* validation panel state;
* asset preview cache;
* resolved PackedScene;
* transformed footprint cache.

---

# 16. Что не входит в formatVersion 1

Venue Definition version 1 не поддерживает:

* процедурный забор;
* fence polyline;
* terrain heightmap;
* неровную поверхность;
* несколько уровней высоты поверхности;
* произвольный наклон объектов по трём осям;
* polygon footprint;
* точные collision bounds;
* object hierarchy;
* parent-child transforms;
* dynamic objects;
* animation state;
* lighting configuration;
* weather;
* time of day;
* audio environment;
* spawn points;
* gameplay checkpoints;
* surface zones;
* friction maps;
* thumbnails;
* asset catalog metadata;
* embedded Track Project.
