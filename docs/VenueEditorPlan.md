# Venue Editor Implementation Plan

# Venue Editor Iteration 1 — Venue Definition Authoring

## 1. Цель

Создать отдельный 2D Venue Editor для формирования переиспользуемых площадок.

Пользователь должен иметь возможность:

1. создать Venue Definition;
2. задать размеры площадки;
3. настроить панорамное окружение;
4. разместить постоянные 3D-assets;
5. задать footprints;
6. создать постоянные конусы;
7. создать постоянную разметку;
8. сохранить площадку в библиотеку;
9. повторно открыть и отредактировать её.

Track Editor и Viewer в этой итерации не интегрируются с Venue Definition.

---

# 2. Отдельная сцена

Создать отдельную сцену:

```text
VenueEditor.tscn
```

Venue Editor не следует объединять с:

* Exercise Editor;
* Track Editor;
* Viewer.

Рекомендуемая структура:

```text
scenes/
└─ editor/
   ├─ ExerciseEditor.tscn
   ├─ TrackEditor.tscn
   └─ VenueEditor.tscn
```

Не выполнять массовый перенос существующих сцен только ради соответствия примеру.

---

# 3. Библиотека площадок

Корень:

```text
res://venues/
```

Поддержать:

* вложенные подпапки;
* выбор папки;
* создание подпапки;
* New;
* Open;
* Save;
* Save As;
* Refresh.

Не поддерживать сейчас:

* rename folder;
* recursive delete folder;
* tags;
* search index;
* thumbnails;
* cloud storage.

Все файловые операции с Venue JSON должны оставаться внутри:

```text
res://venues/
```

Path traversal запрещён.

---

# 4. Новый Venue

Команда:

```text
New Venue
```

Поля:

* venue id;
* venue name;
* area width;
* area length.

Рекомендуемые defaults:

```text
width = 60 m
length = 100 m
```

После создания:

* `objects = []`;
* `cones = []`;
* `markings = []`;
* panorama disabled;
* документ dirty;
* canvas подстраивается под bounds.

---

# 5. Canvas

Venue Editor использует 2D-вид сверху.

Canvas должен показывать:

* area bounds;
* origin;
* сетку 1 × 1 м;
* усиленные линии каждые 5 м;
* постоянные cones;
* постоянные markings;
* object footprints;
* selection overlays.

Поддержать:

* pan;
* zoom;
* snap;
* fit area to view.

Переиспользовать canvas navigation и coordinate conversion существующих редакторов.

---

# 6. Panorama settings

Добавить секцию:

```text
Panorama
```

Поля:

* Enabled;
* Texture Path;
* Rotation Deg;
* Energy Multiplier.

## Texture Path

Поддержать выбор project resource.

Проверять:

* путь начинается с `res://`;
* resource существует;
* resource может быть загружен как Texture2D.

Venue Editor Iteration 1 не обязан визуализировать панораму в 2D canvas.

Достаточно:

* показать filename;
* показать validation state;
* при возможности добавить небольшую preview-картинку.

Preview необязателен.

## Rotation

Редактируется численно.

Допускается normalize только для UI.

## Energy

Требование:

```text
energyMultiplier >= 0
```

---

# 7. Venue object assets

Добавить список объектов площадки.

Минимальные команды:

```text
Add Object
Duplicate Object
Delete Object
```

`Add Object` должен позволять выбрать `.tscn` resource.

Новый объект получает:

* unique objectId;
* name на основе filename;
* assetPath;
* position в центре площадки;
* elevation = 0;
* rotationDeg = 0;
* scale = 1/1/1;
* footprint, автоматически измеренный по визуальной геометрии asset;
* collisionEnabled = true;
* visibleInViewer = true.

При `Add Object` Venue Editor временно создаёт выбранный `PackedScene`,
объединяет локальные `AABB` всех его `VisualInstance3D` и записывает проекцию
общих габаритов на X/Z как:

```text
footprint.width  = visual bounds size X
footprint.length = visual bounds size Z
```

Трансформации дочерних узлов учитываются в координатах корня asset scene.
Collision shapes не участвуют в измерении: footprint описывает видимый размер,
а не физическую collision geometry.

Если asset не содержит конечных положительных визуальных габаритов по X/Z,
`Add Object` завершается понятной ошибкой и Venue Definition не изменяется.
После добавления пользователь по-прежнему может явно скорректировать footprint.

---

# 8. Object preview

Venue Editor не обязан создавать полноценный 3D-preview.

На 2D canvas object отображается как transformed footprint:

* position;
* rotation;
* scale.x;
* scale.z;
* width;
* length.

Дополнительно отображать:

* object name;
* направление rotation;
* unresolved marker;
* hidden-in-viewer marker;
* lock overlay.

---

# 9. Object selection

Объект можно выбрать:

* кликом по transformed footprint;
* через список объектов.

Selection синхронизируется между:

* canvas;
* object list;
* properties panel.

При перекрытии объектов допустим выбор верхнего по draw order.

Cycling selection не требуется.

---

# 10. Object transform

Выбранный object поддерживает:

* drag position;
* числовой Position X/Y;
* Elevation;
* Rotation Deg;
* Scale X/Y/Z;
* Footprint Width/Length.

Порядок 3D transform Viewer будет применять как:

```text
scale
    ↓
rotation around vertical axis
    ↓
translation
```

2D footprint использует:

```text
footprint size
    ↓
scale X/Z
    ↓
rotationDeg
    ↓
position X/Y
```

Elevation и scale.y не влияют на footprint.

---

# 11. Duplicate

Поддержать:

```text
Duplicate Object
Ctrl+D
```

Копия получает:

* новый objectId;
* остальные persisted properties исходного объекта;
* positional offset;
* selection;
* unlocked state.

Рекомендуемый offset:

```text
+1 m X
+1 m Y
```

Duplicate является одной Undo-операцией.

---

# 12. Keyboard transforms

Переиспользовать Track Editor UX:

```text
Arrow keys
    current snap step

Shift + Arrow
    1 meter

Alt + Arrow
    0.05 meter
```

Поворот:

```text
Q / E
    -15° / +15°

Shift + Q / Shift + E
    -90° / +90°
```

Shortcuts не должны работать при фокусе в editable Control.

---

# 13. Temporary locking

Object можно временно заблокировать.

Locked object:

* можно select;
* нельзя drag;
* нельзя менять transform;
* можно unlock;
* можно duplicate;
* можно delete после подтверждения;
* сохраняется и экспортируется как обычно.

Lock:

* не сериализуется;
* не меняет dirty state;
* не входит в Undo history;
* очищается после Open/New.

---

# 14. Venue cones

Переиспользовать cone editing из Exercise Editor.

Поддержать:

* Add Cone;
* select;
* drag;
* snap;
* numerical X/Y;
* color;
* delete;
* unique ID;
* Duplicate, если существующая инфраструктура делает это просто.

Cone принадлежит Venue Definition напрямую.

Cone не имеет:

* entry/exit;
* trajectory;
* Route Order.

---

# 15. Venue markings

Venue Definition v2 использует Path-based markings согласно:

`docs/CurvedMarkingsPlan.md`

Полноценное интерактивное редактирование описано в:

`docs/VenueEditorCurvedMarkingsPlan.md`

Поддерживаются:

- line segments;
- cubic Bézier segments;
- Path/segment selection;
- endpoints;
- control points;
- drag;
- split;
- line/cubic conversion;
- segment deletion;
- whole-marking translation;
- color;
- thickness;
- solid;
- dashed;
- dotted;
- visibleInViewer;
- Undo/Redo.

Marking с:

`visibleInViewer = false`

остаётся видимым в Venue Editor с editor-only признаком.

Venue Editor должен максимально переиспользовать curved-marking editor infrastructure Exercise Editor.

Не добавлять:

- text;
- arc;
- circle;
- ellipse;
- polygon fill;
- tangent modes;
- custom dash pattern.

---

# 16. Tools and selection modes

Venue Editor имеет разные редактируемые сущности:

* object;
* cone;
* marking.

UI должен ясно показывать active tool или selection type.

Минимально допустимые инструменты:

```text
Select
Add Object
Add Cone
Add Marking
Add Line
Add Curve
```

При смене инструмента незавершённая операция должна:

* завершиться корректно;
* либо отмениться без повреждения документа.

---

# 17. Undo/Redo

Переиспользовать историю Track Editor Iteration 4 либо общий reusable history component.

Undo/Redo должен поддерживать:

* metadata;
* area;
* panorama settings;
* add/delete/move object;
* object rotation;
* scale;
* elevation;
* footprint;
* collisionEnabled;
* visibleInViewer;
* duplicate object;
* add/delete/move cone;
* cone properties;
* add/delete/edit marking;
* marking properties.

Не включать:

* selection;
* pan/zoom;
* active tool;
* lock state;
* validation UI;
* asset cache.

Drag создаёт одну history entry.

Dirty state должен быть связан с saved history revision.

---

# 18. Properties panel

## Venue properties

* ID;
* Name;
* Width;
* Length.

## Panorama properties

* Enabled;
* Texture Path;
* Rotation;
* Energy.

## Object properties

* Object ID read-only;
* Name;
* Asset Path;
* Position X;
* Position Y;
* Elevation;
* Rotation;
* Scale X;
* Scale Y;
* Scale Z;
* Footprint Width;
* Footprint Length;
* Collision Enabled;
* Visible in Viewer;
* Lock editor-only.

## Cone properties

Переиспользовать Exercise Editor.

## Marking properties

Переиспользовать Exercise Editor.

---

# 19. Area changes

При изменении area:

* существующие объекты не перемещаются;
* cones не перемещаются;
* markings не масштабируются;
* panorama не изменяется.

Geometry за новыми bounds:

* сохраняется;
* показывает warning;
* не удаляется автоматически.

---

# 20. Validation

## Блокирующие ошибки Save

* invalid Venue JSON model;
* duplicate IDs;
* non-finite persisted numbers;
* non-positive area;
* non-positive scale;
* non-positive footprint;
* invalid marking;
* unsupported formatVersion.

## Warnings

* unresolved assetPath;
* missing panorama texture;
* panorama enabled without texture;
* object outside area;
* cone outside area;
* marking outside area;
* overlapping footprints;
* unusual scale.

Warnings не блокируют Save.

---

# 21. Safe loading

Open должен:

1. прочитать JSON;
2. десериализовать во временный DTO;
3. проверить formatVersion;
4. выполнить validation;
5. разрешить assets;
6. только после успешной базовой проверки заменить текущий документ.

Повреждённый файл не должен уничтожать текущую Venue Definition.

Unresolved asset не блокирует открытие всего документа.

---

# 22. Dirty state

Dirty устанавливается при любом persisted изменении:

* metadata;
* area;
* panorama;
* object;
* cone;
* marking.

Не устанавливается при:

* selection;
* pan;
* zoom;
* lock;
* validation;
* resource preview.

Перед New/Open/Close должна действовать защита несохранённых изменений.

---

# 23. Asset path policy

Venue Editor хранит canonical Godot resource path:

```text
res://...
```

Не хранить:

* абсолютные Windows paths;
* `file://`;
* относительные пути к Venue JSON;
* импортированные внутренние `.godot/imported` paths.

При выборе файла вне project resource tree Editor должен показать ошибку.

---

# 24. Не реализовывать в Iteration 1

Не добавлять:

* Track Editor integration;
* Track Project venuePath;
* Exported Track venue data;
* Viewer loading Venue Definition;
* 3D-preview;
* встроенный Blender;
* asset import configuration;
* procedural fence;
* fence polyline;
* object hierarchy;
* multi-selection;
* group transforms;
* alignment/distribution;
* terrain;
* slope;
* lighting editor;
* weather;
* audio;
* spawn points;
* surface zones;
* collision generation;
* automatic footprint extraction;
* exact mesh bounds extraction;
* autosave recovery;
* thumbnails library;
* copy/paste framework.

---

# 25. Definition of Done

Venue Editor Iteration 1 завершена, если:

* Venue Editor запускается отдельной сценой;
* `res://venues/` создаётся при отсутствии;
* работает New;
* работает Open;
* работает Save;
* работает Save As;
* работают пользовательские подпапки;
* задаются venue id/name;
* задаются width/length;
* сохраняются panorama settings;
* можно добавить `.tscn` object;
* object отображается через footprint;
* object выбирается;
* object перемещается;
* object поворачивается;
* object масштабируется;
* редактируется elevation;
* редактируется footprint;
* работает collisionEnabled;
* работает visibleInViewer;
* работает Duplicate;
* работают keyboard transforms;
* работает temporary lock;
* работают Undo/Redo;
* можно создавать Venue cones;
* можно создавать Venue markings;
* visibleInViewer marking работает в Editor;
* unresolved asset не ломает документ;
* save/open сохраняют все persisted поля;
* old Track Editor работает без изменений;
* Exercise Editor работает;
* Viewer работает;
* проект собирается без compile errors;
* runtime logs не содержат необработанных ошибок.
