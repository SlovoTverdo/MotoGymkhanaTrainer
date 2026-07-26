# Track/Venue Integration Implementation Plan

# Track/Venue Integration Iteration 1

## 1. Цель

Интегрировать Venue Definition с Track Editor, Track Project, экспортом трассы и Viewer.

После этой итерации:

1. Track Project создаётся только на основе существующей Venue Definition;
2. размеры рабочей области берутся из Venue;
3. постоянные объекты, конусы и markings отображаются в Track Editor;
4. Venue geometry объединяется с Track geometry при экспорте;
5. Viewer получает самодостаточный Track JSON;
6. Viewer отображает:

   * поверхность площадки;
   * панораму;
   * постоянные 3D-объекты;
   * постоянные конусы;
   * постоянную разметку;
   * упражнения трассы;
   * глобальную trajectory.

---

# 2. Общий workflow

```text
Venue Definition
        +
Exercise Definitions
        +
Track Project
        ↓
Track compilation
        ↓
Exported Track JSON
        ↓
Viewer
```

Track Project не копирует редактируемые данные Venue.

Он хранит только:

```text
venuePath
```

Exported Track JSON содержит полный snapshot Venue и Track.

---

# 3. Версии форматов

После интеграции используются:

```text
Venue Definition     formatVersion 1
Exercise Definition  formatVersion 2
Track Project        formatVersion 3
Exported Track       formatVersion 4
```

Совместимость Track Project version 1–2 не реализуется.

Совместимость Exported Track version 1–3 не является обязательной.

Существующие старые Track Project и exports можно удалить и создать повторно.

---

# 4. Track Project version 3

Track Project version 3 содержит:

```text
TrackProject
├─ FormatVersion
├─ TrackMetadata
├─ VenuePath
├─ Instances[]
└─ TransitionOverrides[]
```

Track Project больше не содержит:

```text
area
```

Размеры площадки загружаются из Venue Definition.

---

# 5. Venue path

Пример:

```json
"venuePath": "main-training-ground/venue.json"
```

Путь:

* относителен `res://venues/`;
* не содержит `res://venues/` в самом значении;
* не является абсолютным;
* не содержит path traversal;
* должен разрешаться внутри Venue Library.

Полный путь:

```text
res://venues/ + venuePath
```

---

# 6. New Track workflow

Команда:

```text
New Track
```

открывает последовательность:

```text
Select Venue
    ↓
Track ID
    ↓
Track Name
    ↓
Create
```

Пользователь не вводит:

* width;
* length.

Если Venue Library пуста:

* новый Track создать нельзя;
* пользователь получает понятное сообщение;
* предлагается открыть Venue Editor.

---

# 7. Venue loading in Track Editor

При создании или открытии Track Project Track Editor:

1. разрешает `venuePath`;
2. загружает Venue Definition;
3. проверяет `formatVersion`;
4. валидирует основные данные;
5. загружает Venue assets для диагностики;
6. использует `venue.area` как bounds canvas;
7. строит read-only Venue preview;
8. затем загружает ExerciseInstance.

Если Venue не разрешена:

* Track Project не должен открываться как нормальный редактируемый документ;
* пользователь получает blocking error;
* текущий открытый документ не уничтожается.

В отличие от unresolved ExerciseInstance, unresolved Venue блокирует работу с Track Project, потому что без неё неизвестны:

* размеры;
* постоянная geometry;
* окружение;
* export context.

---

# 8. Read-only Venue preview

Track Editor показывает Venue как неизменяемый фон.

Отображаются:

* area bounds;
* постоянные markings;
* постоянные cones;
* VenueObject footprints;
* names или icons объектов;
* hidden/unresolved diagnostics.

Venue geometry в Track Editor нельзя:

* выбирать как Track object;
* перемещать;
* удалять;
* масштабировать;
* поворачивать;
* переупорядочивать.

Редактирование площадки выполняется только в Venue Editor.

---

# 9. Draw order in Track Editor

Рекомендуемый порядок:

1. canvas background;
2. grid;
3. Venue area;
4. Venue markings;
5. Venue object footprints;
6. Venue cones;
7. Exercise markings;
8. Exercise cones;
9. Exercise trajectory;
10. automatic/manual transitions;
11. selection overlays;
12. diagnostics.

Venue preview должен визуально отличаться от редактируемой Track geometry.

---

# 10. Venue object footprints

Track Editor использует persisted footprint из Venue Definition.

Footprint преобразуется:

```text
footprint width/length
    ↓
scale X/Z
    ↓
rotationDeg
    ↓
position X/Y
```

Footprint применяется для:

* read-only preview;
* occupied-space warnings;
* проверки пересечений ExerciseInstance;
* проверки выхода за area.

Точная 3D collision shape не используется Track Editor.

---

# 11. Placement warnings

При размещении ExerciseInstance Track Editor предупреждает, если transformed bounds упражнения:

* выходят за Venue area;
* пересекают Venue object footprint.

Предупреждение не блокирует:

* Save;
* редактирование;
* экспорт.

На первом этапе не требуется точная проверка:

* отдельных конусов;
* trajectory;
* concave geometry;
* 3D collision.

Используется приближённая проверка bounds/footprint.

---

# 12. Track compilation

Track compilation version 4 объединяет:

```text
Venue snapshot
+
Track snapshot
```

Compilation использует:

* Venue Definition;
* Exercise Definitions;
* Track Project.

Результат не зависит от исходных документов после сохранения JSON.

---

# 13. Compiled Venue

Концептуальная runtime-модель:

```text
CompiledVenue
├─ VenueMetadata
├─ Area
├─ Panorama
├─ Objects[]
├─ Cones[]
├─ Markings[]
├─ Errors[]
└─ Warnings[]
```

Compiled Venue не сериализуется обратно в Venue Definition.

Он используется:

* для validation;
* для export;
* для Viewer snapshot.

---

# 14. Exported Venue objects

Каждый объект экспортируется как snapshot:

```text
ExportedVenueObject
├─ Id
├─ Name
├─ AssetPath
├─ Position
├─ Elevation
├─ RotationDeg
├─ Scale
├─ CollisionEnabled
└─ VisibleInViewer
```

Footprint экспортировать необязательно для Viewer rendering, но рекомендуется сохранить как diagnostic metadata.

Viewer загружает `assetPath` как PackedScene.

---

# 15. Asset references

Exported Track JSON остаётся самодостаточным относительно geometry Track и Venue metadata, но 3D Venue assets и panorama texture остаются project resource references:

```text
res://...
```

То есть JSON не встраивает:

* GLB bytes;
* `.tscn` content;
* Texture bytes.

Для запуска Viewer внутри того же Godot-проекта это допустимо.

Полностью переносимый внешний пакет ресурсов относится к будущей packaging/export итерации.

---

# 16. Exported IDs

Venue IDs получают namespace.

Объекты:

```text
venue--object--{objectId}
```

Конусы:

```text
venue--cone--{coneId}
```

Markings:

```text
venue--marking--{markingId}
```

Exercise geometry продолжает использовать:

```text
{instanceId}--{localId}
```

Transitions:

```text
transition--{fromInstanceId}--{toInstanceId}
```

Коллизии ID между Venue и Exercise geometry недопустимы.

---

# 17. Exported cones

Итоговый массив cones содержит:

1. Venue cones;
2. Exercise cones.

Venue cone:

* уже находится в мировых координатах;
* не получает Exercise transform;
* получает namespace ID.

Exercise cone:

* преобразуется instance transform;
* получает instance namespace ID.

Viewer отображает оба одинаковым cone renderer.

---

# 18. Exported markings

Итоговый массив markings содержит:

1. Venue markings;
2. Exercise markings.

Venue markings:

* уже находятся в Venue world coordinates;
* не получают transform;
* получают Venue namespace ID.

Exercise markings:

* получают ExerciseInstance transform;
* получают instance namespace ID.

Для всех сохраняются:

* color;
* widthMeters;
* style;
* visibleInViewer.

---

# 19. Panorama export

Exported Track version 4 содержит:

```text
panorama
```

с полями:

* enabled;
* texturePath;
* rotationDeg;
* energyMultiplier.

Viewer использует panorama через:

```text
PanoramaSkyMaterial
→ Sky
→ Environment
→ WorldEnvironment
```

Viewer не создаёт:

* cylinder mesh;
* sphere mesh;
* hemisphere mesh.

---

# 20. Viewer area surface

Viewer должен создать визуальную поверхность площадки размером:

```text
area.width × area.length
```

Для первой версии допустим:

* `PlaneMesh`;
* простой asphalt-like StandardMaterial3D;
* UV scale, соответствующий размерам;
* поверхность на Godot Y = 0.

Точная surface texture не входит в Venue Definition version 1.

Можно использовать общий материал проекта.

---

# 21. Viewer Venue objects

Для каждого visible Venue object:

1. загрузить `assetPath` как PackedScene;
2. instantiate;
3. применить:

   * position;
   * elevation;
   * rotation around Y;
   * scale;
4. добавить в Venue root.

Соответствие координат:

```text
Venue X   → Godot X
Venue Y   → Godot Z
Elevation → Godot Y
```

Если:

```text
visibleInViewer = false
```

объект не создаётся.

---

# 22. Collision control

Если:

```text
collisionEnabled = false
```

Viewer должен отключить collision descendants созданного asset.

Допустимая MVP-стратегия:

* найти `CollisionShape3D`;
* установить `disabled = true`;
* отключить другие поддерживаемые collision nodes.

Viewer не создаёт collision shapes автоматически.

Если asset не содержит collision, значение `true` ничего не генерирует.

---

# 23. Missing Venue assets

Отсутствующий Venue object asset является blocking export error.

Причина: экспортированный snapshot должен быть пригоден для текущего Viewer.

Отсутствующая panorama texture:

* warning, если panorama disabled;
* blocking error, если panorama enabled.

Venue Editor по-прежнему может сохранять Venue с unresolved assets, но Track export требует полностью разрешённую площадку.

---

# 24. Viewer error handling

Если Viewer всё же получает export с отсутствующим asset:

* не падать;
* вывести error;
* пропустить конкретный object;
* продолжить отображать остальную трассу.

Это runtime safety, а не замена compile validation.

---

# 25. Validation

## Blocking errors

* Venue Definition отсутствует;
* unsupported Venue formatVersion;
* invalid Venue required data;
* unresolved visible Venue object asset;
* panorama enabled, но texture отсутствует;
* duplicate exported IDs;
* non-finite Venue transforms;
* invalid scale;
* invalid area;
* ошибка Track compilation;
* ошибка serialization.

## Warnings

* hidden object asset отсутствует;
* Venue object footprint выходит за bounds;
* footprints пересекаются;
* Exercise bounds пересекают Venue footprint;
* Exercise bounds выходят за Venue area;
* Venue cone или marking выходят за bounds;
* unusually large scale;
* panorama energy имеет необычное значение.

Warnings не блокируют export.

---

# 26. Viewer scene hierarchy

Рекомендуемая runtime-структура:

```text
ViewerRoot
├─ WorldEnvironment
├─ DirectionalLight3D
├─ VenueRoot
│  ├─ Surface
│  ├─ Objects
│  ├─ Cones
│  └─ Markings
├─ TrackRoot
│  ├─ ExerciseCones
│  ├─ ExerciseMarkings
│  └─ Trajectory
└─ CameraRig
```

Точная структура может отличаться, но Venue и Track geometry должны логически разделяться.

---

# 27. Viewer reload

При загрузке другого Track JSON Viewer должен очистить:

* предыдущий Venue surface;
* предыдущие Venue objects;
* предыдущий panorama environment;
* Venue cones;
* Venue markings;
* Exercise geometry;
* trajectory.

Нельзя оставлять assets предыдущей площадки.

---

# 28. Export and Open in Viewer

Существующая команда Track Editor:

```text
Export and Open in Viewer
```

должна автоматически включать Venue snapshot.

Viewer preview использует:

* текущий Track Project;
* актуальную Venue Definition;
* актуальные Exercise Definitions.

Изменения Venue Definition после открытия Track Project должны учитываться при следующем compile/export.

---

# 29. Venue reload

Добавить в Track Editor действие:

```text
Reload Venue
```

Оно:

1. повторно загружает Venue Definition;
2. обновляет bounds;
3. обновляет read-only preview;
4. повторно выполняет validation;
5. не изменяет Track Project;
6. не устанавливает dirty state.

Если размеры Venue изменились:

* ExerciseInstance не перемещаются;
* Editor показывает новые warnings.

---

# 30. Не реализовывать

В этой итерации не добавлять:

* совместимость старых Track Project;
* миграцию version 2 → 3;
* совместимость старых exported Track;
* Venue switching для уже созданного Track Project;
* embedded Venue copy в Track Project;
* редактирование Venue geometry в Track Editor;
* procedural fence;
* terrain;
* uneven surface;
* surface materials в Venue Format;
* lighting settings в Venue Format;
* weather;
* time of day;
* packaging external resources;
* embedding `.tscn` или texture bytes;
* automatic collision generation;
* exact mesh collision validation;
* multi-level venues;
* spawn points;
* motorcycle physics;
* gameplay checkpoints.

---

# 31. Definition of Done

Интеграция завершена, если:

* Track Project version 3 требует `venuePath`;
* Track Project больше не содержит area;
* New Track требует выбора Venue;
* Track Editor использует размеры Venue;
* Venue preview отображается read-only;
* Venue footprints видны;
* Venue cones видны;
* Venue markings видны;
* Exercise geometry остаётся редактируемой;
* placement warnings работают;
* Reload Venue работает;
* export содержит Venue snapshot;
* Exported Track version 4 создаётся;
* Viewer создаёт area surface;
* Viewer применяет panorama sky;
* Viewer загружает Venue `.tscn` objects;
* position/elevation/rotation/scale работают;
* collisionEnabled работает;
* visibleInViewer работает;
* Venue cones отображаются;
* Venue markings отображаются;
* Track cones/markings продолжают работать;
* global trajectory продолжает работать;
* Viewer reload очищает предыдущую Venue;
* Export and Open in Viewer работает;
* Exercise Editor работает;
* Venue Editor работает;
* Track Editor работает;
* проект собирается без compile errors;
* runtime logs не содержат необработанных ошибок.
