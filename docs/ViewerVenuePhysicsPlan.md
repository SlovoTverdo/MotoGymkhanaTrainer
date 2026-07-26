# Viewer/Venue Physics Implementation Plan

# Viewer/Venue Physics Iteration 1

## 1. Цель

Завершить физическую интеграцию Viewer с Venue.

После этой итерации Viewer должен:

* блокировать движение камерой через дом, забор и другие препятствия;
* позволять подниматься и спускаться по эстакаде;
* сохранять высоту глаз относительно проходимой поверхности;
* отображать trajectory поверх асфальта, эстакады и других проходимых поверхностей;
* размещать markings, direction arrows и cones на фактической высоте поверхности;
* устойчиво работать при перезагрузке разных трасс и площадок.

Эта итерация не меняет persisted JSON contracts.

---

# 2. Текущая проблема

Визуальная загрузка Venue assets сама по себе не обеспечивает физическое взаимодействие.

Даже если Venue asset содержит:

```text
StaticBody3D
└─ CollisionShape3D
```

камера сможет проходить сквозь него, если camera rig:

* является обычным `Node3D`;
* перемещается прямым изменением `Position`;
* не имеет collision shape;
* не использует physics movement API.

Также двумерная trajectory содержит только координаты Track X/Y.

При построении на фиксированном Godot Y она оказывается:

* на поверхности основной площадки;
* под поднятой эстакадой;
* внутри наклонных поверхностей;
* под объектами, перекрывающими базовую плоскость.

---

# 3. Viewer movement modes

Viewer должен иметь два явно различимых режима.

```text
Walk
Fly
```

## 3.1. Walk mode

Walk mode использует физический `CharacterBody3D`.

Он поддерживает:

* collision;
* gravity;
* движение по floor surfaces;
* подъём и спуск по эстакаде;
* sliding вдоль стен;
* floor snapping;
* фиксированную высоту камеры относительно character body.

## 3.2. Fly mode

Fly mode предназначен для свободного осмотра трассы.

Он может:

* перемещаться по трём осям;
* игнорировать gravity;
* игнорировать collision либо использовать collision по отдельной настройке.

Для Iteration 1 допускается сохранить существующее свободное перемещение как Fly mode.

## 3.3. Переключение

Добавить явную команду:

```text
Toggle Walk/Fly Mode
```

Рекомендуемая клавиша:

```text
F
```

Если `F` уже используется, выбрать другую свободную клавишу и показать её в Viewer UI.

Viewer должен отображать текущий режим.

---

# 4. CharacterBody3D hierarchy

Рекомендуемая структура:

```text
ViewerCharacter
├─ CollisionShape3D
├─ Head
│  └─ Camera3D
└─ GroundProbe
```

Корневой узел:

```text
CharacterBody3D
```

`Head` отвечает за вертикальный и горизонтальный обзор.

Поворот вокруг вертикальной оси может применяться:

* к CharacterBody3D;
* либо к Head, если это соответствует текущей реализации.

Вертикальный pitch применяется только к Head или Camera pivot.

---

# 5. Character collision shape

Использовать:

```text
CapsuleShape3D
```

Рекомендуемые начальные параметры:

```text
radius = 0.30–0.35 m
body height = 1.1–1.3 m
eye height = 1.65–1.75 m
```

Точные значения могут быть вынесены в exported properties Viewer controller.

Collision shape должна:

* находиться над поверхностью;
* не пересекать floor в состоянии покоя;
* соответствовать camera eye height;
* позволять проходить через предусмотренные проёмы.

---

# 6. Walk movement

Walk mode не изменяет `GlobalPosition` напрямую.

Использовать:

```text
Velocity
MoveAndSlide()
```

Основной порядок physics tick:

1. прочитать input;
2. вычислить horizontal desired velocity;
3. применить acceleration/deceleration;
4. применить gravity, если character не на floor;
5. вызвать `MoveAndSlide`;
6. обновить Viewer state.

---

# 7. Gravity

Walk mode использует gravity.

Предпочтительно брать значение из:

```text
ProjectSettings
physics/3d/default_gravity
```

Допускается exported Viewer-specific multiplier.

Fly mode gravity не использует.

---

# 8. Floor detection

Настроить:

```text
UpDirection = Vector3.Up
```

Использовать разумный:

```text
FloorMaxAngle
```

Начальное рекомендуемое значение:

```text
45–50 degrees
```

Оно должно позволять проходить по наклонной поверхности эстакады, но не считать вертикальные стены floor.

---

# 9. Floor snap

Использовать floor snapping для устойчивого контакта:

```text
FloorSnapLength
```

Рекомендуемый начальный диапазон:

```text
0.2–0.4 m
```

Floor snap должен уменьшить:

* подпрыгивание на стыке асфальта и пандуса;
* потерю контакта на переходе к горизонтальной площадке;
* кратковременное зависание при спуске.

Floor snap не применяется в Fly mode.

---

# 10. Step handling

Iteration 1 не требует отдельного полноценного step-climbing алгоритма.

Небольшие стыки должны проходиться за счёт:

* корректно состыкованных collision shapes;
* floor snapping;
* отсутствия вертикальных щелей;
* небольшого safe margin CharacterBody3D.

Если эстакада содержит заметную вертикальную ступень, её collision asset должен быть исправлен.

Viewer не обязан обходить плохо подготовленную collision geometry.

---

# 11. Venue collision requirements

Venue object asset с:

```text
collisionEnabled = true
```

должен содержать физическое тело, например:

```text
StaticBody3D
└─ CollisionShape3D
```

Дом и забор должны иметь препятствующие collision shapes.

Эстакада должна иметь проходимые collision surfaces:

```text
EntryRampCollision
TopPlatformCollision
ExitRampCollision
```

Предпочтительно использовать простые convex shapes:

* `BoxShape3D`;
* несколько `ConvexPolygonShape3D`, если необходимо.

Не использовать один общий bounding box вокруг всей эстакады, если он создаёт вертикальную стену вместо наклонного въезда.

---

# 12. Collision enable/disable

Exported Venue object уже содержит:

```text
collisionEnabled
```

Если `false`, Viewer рекурсивно отключает collision descendants instantiated asset.

Необходимо поддержать минимум:

* `CollisionShape3D.Disabled = true`;
* `CollisionPolygon3D.Disabled = true`, если используется;
* другие существующие collision nodes проекта, если они присутствуют.

Если `true`, asset collision сохраняется в исходном состоянии.

Viewer не создаёт collision автоматически.

---

# 13. Collision layers

Определить централизованные physics layers.

Рекомендуемая логическая схема:

```text
WalkableSurface
WorldObstacle
TrackVisual
ViewerCharacter
```

Конкретные номера layers определяются проектом.

## WalkableSurface

Содержит:

* основную поверхность площадки;
* наклонные поверхности эстакады;
* верхнюю поверхность эстакады;
* другие поверхности, на которых можно находиться.

## WorldObstacle

Содержит:

* стены домика;
* забор;
* столбы;
* непроходимые части эстакады;
* другие препятствия.

## TrackVisual

Содержит только визуальную geometry:

* trajectory;
* markings;
* arrows.

TrackVisual не участвует в character collision и surface projection.

## ViewerCharacter

Содержит CharacterBody3D Viewer.

---

# 14. Character collision mask

Walk controller должен видеть:

```text
WalkableSurface
WorldObstacle
```

Он не должен сталкиваться с:

* trajectory mesh;
* markings;
* arrows;
* cone visual mesh, если конусы не должны блокировать ходьбу.

Если конусы должны иметь collision позднее, это определяется отдельной итерацией.

---

# 15. Surface projection

Viewer должен преобразовать двумерную Track geometry в трёхмерную geometry, лежащую на поверхности Venue.

Использовать downward raycast.

Для Track point:

```text
Track X/Y
    ↓
Godot X/Z
    ↓
raycast from above
    ↓
nearest walkable surface
    ↓
Godot X/Y/Z
```

---

# 16. Projection ray

Для каждой sampled point:

```text
ray start = (x, ProjectionTopY, z)
ray end   = (x, ProjectionBottomY, z)
```

Рекомендуемые defaults:

```text
ProjectionTopY = 50 m
ProjectionBottomY = -10 m
```

Эти значения могут быть вычислены из известных bounds Venue objects или заданы константами для MVP.

Ray mask должен включать только:

```text
WalkableSurface
```

Ray не должен попадать в:

* trajectory;
* markings;
* arrows;
* cones;
* стены;
* забор;
* декоративные объекты, не являющиеся поверхностью.

---

# 17. Surface offset

После hit:

```text
projectedPoint = hit.Position + hit.Normal * SurfaceVisualOffset
```

Рекомендуемый диапазон:

```text
SurfaceVisualOffset = 0.02–0.05 m
```

Использование normal лучше простого добавления по Godot Y на наклонной поверхности.

Это уменьшает:

* z-fighting;
* погружение линии в пандус;
* неправильный offset на наклонной поверхности.

---

# 18. Projection fallback

Если ray не нашёл walkable surface:

1. записать warning с source object ID;
2. использовать fallback height основной площадки;
3. не прерывать загрузку Viewer.

Рекомендуемый fallback:

```text
Godot Y = SurfaceVisualOffset
```

Для cones fallback может быть:

```text
Godot Y = 0
```

с учётом pivot модели.

---

# 19. Physics space synchronization

Raycasts нельзя выполнять до добавления Venue collision bodies в active physics space.

Порядок загрузки:

1. очистить предыдущий Viewer runtime;
2. создать surface и её collision;
3. instantiate Venue objects;
4. применить transforms;
5. добавить их в SceneTree;
6. дождаться physics frame;
7. выполнять surface projection;
8. строить Track visuals;
9. размещать Viewer character.

В Godot C# допустимо использовать:

```text
await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame)
```

или эквивалентную безопасную синхронизацию.

Не использовать фиксированную задержку по времени.

---

# 20. Venue surface collision

Основная поверхность площадки должна иметь не только visual `PlaneMesh`, но и collision.

Рекомендуемая структура:

```text
SurfaceRoot
├─ MeshInstance3D
└─ StaticBody3D
   └─ CollisionShape3D
```

Collision shape:

```text
BoxShape3D
```

с небольшой толщиной.

Размер:

```text
X = area.width
Z = area.length
```

Верхняя поверхность находится на:

```text
Godot Y = 0
```

Surface collision принадлежит WalkableSurface.

---

# 21. Projection of trajectory

Global trajectory сначала sampled в двумерную polyline существующим способом.

После sampling:

1. каждую точку спроецировать на surface;
2. построить 3D trajectory mesh;
3. применять surface offset;
4. сохранить порядок точек.

Нельзя проецировать только:

* anchors;
* cubic control points;
* начало и конец segment.

Поверхность между anchors может менять высоту.

---

# 22. Sampling density

Projection quality зависит от spacing между sampled points.

Использовать существующий Bezier sampling, но дополнительно ограничить максимальное расстояние между соседними sampled points.

Рекомендуемый начальный максимум:

```text
0.25–0.5 m
```

Особенно важно на:

* начале подъёма;
* стыке ramp/platform;
* вершине;
* начале спуска.

Не требуется adaptive curvature/surface subdivision framework.

Допускается равномерное дополнительное subdivide по длине.

---

# 23. Projection of direction arrows

Direction arrows должны строиться после surface projection.

Позиция arrow:

* берётся из projected trajectory;
* слегка поднимается над line mesh;
* ориентируется по трёхмерной касательной projected trajectory.

Arrow orientation должна учитывать:

* horizontal direction;
* изменение высоты на ramp;
* surface slope.

Стрелка не должна оставаться горизонтальной при движении по заметному наклону.

---

# 24. Projection of markings

Venue и Exercise markings проецируются на surface.

Для line/polyline:

1. subdivide segments с максимальным spacing;
2. project каждую sample point;
3. построить 3D marking mesh.

Для dashed/dotted styles:

* использовать существующую style semantics;
* вычислять pattern по длине projected или исходной линии последовательно;
* не создавать отдельные physics objects.

`visibleInViewer = false` по-прежнему не отображается.

---

# 25. Projection of cones

Для каждого cone:

1. взять world X/Z;
2. выполнить downward surface query;
3. установить base position на hit surface;
4. учитывать pivot cone asset.

Если pivot конуса расположен в центре его основания:

```text
cone.GlobalPosition = hit.Position
```

Если pivot отличается, использовать централизованный offset.

Не задавать разные случайные offsets для Venue и Exercise cones.

---

# 26. Surface query service

Создать единый runtime service или utility:

```text
SurfaceProjectionService
```

Концептуальные операции:

```text
ProjectPoint
ProjectPolyline
ProjectConePosition
TryGetSurface
```

Он должен централизовать:

* ray start/end;
* collision mask;
* fallback;
* surface offset;
* diagnostics.

Не дублировать raycast-код отдельно в:

* trajectory renderer;
* marking renderer;
* cone renderer;
* arrow renderer.

---

# 27. Source diagnostics

Projection warning должен содержать:

* geometry type;
* exported ID;
* исходную Track X/Y;
* причину fallback.

Примеры source type:

```text
TrajectorySegment
Marking
Cone
DirectionArrow
```

Не спамить логом одной ошибкой для каждой из сотен соседних samples.

Рекомендуется группировать warnings по source ID.

---

# 28. Walk spawn

После Venue collision initialization Viewer должен выбрать безопасную стартовую позицию.

Предпочтительный источник:

1. начало global trajectory;
2. центр Venue, если trajectory отсутствует.

Алгоритм:

1. взять X/Z;
2. raycast вниз на WalkableSurface;
3. поставить CharacterBody3D над hit;
4. учитывать capsule dimensions;
5. проверить отсутствие immediate collision.

Если старт пересекает obstacle:

* попробовать несколько небольших offsets;
* либо использовать заранее определённую безопасную позицию около центра.

Persisted spawn point не добавляется в formatVersion 4.

---

# 29. Camera height

Camera должна быть дочерним узлом CharacterBody3D.

При подъёме CharacterBody по эстакаде камера автоматически поднимается.

Не выполнять отдельный camera raycast для изменения высоты при Walk mode.

Источник высоты:

```text
CharacterBody3D collision movement
```

а не визуальная surface projection.

---

# 30. Fly mode transition

При переключении Walk → Fly:

* отключить gravity;
* сохранить текущую позицию и ориентацию;
* разрешить vertical movement;
* не перемещать камеру в другую точку.

При переключении Fly → Walk:

1. проверить текущую позицию;
2. найти walkable surface снизу;
3. установить CharacterBody на безопасную высоту;
4. не помещать body внутрь obstacle.

Если под камерой нет walkable surface:

* не переключать режим;
* показать сообщение;
* либо использовать безопасный fallback spawn.

---

# 31. Viewer reload cleanup

При загрузке нового Track v4 удалить:

* старый CharacterBody runtime state, если он пересоздаётся;
* Venue surface visual;
* Venue surface collision;
* Venue objects;
* panorama;
* cones;
* markings;
* trajectory;
* arrows;
* projection diagnostics.

После очистки:

* collision bodies предыдущей Venue не должны оставаться в physics space;
* surface queries не должны попадать в старые объекты.

Перед новой projection может потребоваться physics frame после удаления и после создания новой Venue.

---

# 32. Validation of asset collision

Viewer может выполнить runtime diagnostics для visible object с:

```text
collisionEnabled = true
```

Если instantiated scene не содержит ни одного поддерживаемого collision node:

* показать warning;
* не падать;
* продолжить загрузку.

Это не blocking runtime error.

Venue Editor или отдельная asset validation может позднее выполнять эту проверку заранее.

---

# 33. Debug visualization

Добавить временно включаемый debug mode.

Полезные overlays:

* character capsule;
* collision shapes;
* projection rays;
* hit points;
* walkable surface normals;
* fallback points.

Использовать:

* Godot visible collision shapes;
* либо собственный минимальный debug drawing.

Debug mode не сериализуется.

---

# 34. Performance

Surface projection выполняется при:

* initial load;
* reload Track;
* reload Venue;
* изменении runtime snapshot.

Она не выполняется каждый frame.

Допускается кэшировать projected geometry в рамках текущей загруженной сцены.

Кэш очищается при reload.

Не требуется:

* multithreaded raycast;
* GPU projection;
* terrain baking;
* persistent projected cache в JSON.

---

# 35. Не менять форматы

Iteration 1 не добавляет elevation в:

* trajectory points;
* markings;
* cones;
* Exercise Definition;
* Track Project.

Высота является Viewer runtime projection.

Не повышать:

```text
Venue Definition formatVersion 1
Track Project formatVersion 3
Exported Track formatVersion 4
Exercise Definition formatVersion 2
```

---

# 36. Не реализовывать

Не добавлять:

* motorcycle controller;
* wheel physics;
* vehicle suspension;
* jumping;
* crouching;
* stairs navigation;
* moving platforms;
* dynamic Venue objects;
* arbitrary terrain;
* heightmaps;
* navigation mesh;
* AI pathfinding;
* persisted spawn points;
* per-point trajectory elevation;
* editor 3D projection preview;
* cone gameplay collision;
* physical trajectory collision;
* automatic asset collision generation;
* slope speed model;
* fall damage.

---

# 37. Definition of Done

Iteration завершена, если:

* Viewer имеет Walk mode;
* Walk mode использует CharacterBody3D;
* camera controller имеет capsule collision;
* движение выполняется через MoveAndSlide;
* gravity работает;
* floor detection работает;
* floor snap работает;
* дом блокирует движение;
* забор блокирует движение;
* эстакада имеет проходимую collision surface;
* камера поднимается по ramp;
* камера проходит по верхней платформе;
* камера спускается;
* Fly mode сохраняется или явно реализован;
* режим отображается пользователю;
* Venue surface имеет collision;
* trajectory проецируется на surface;
* trajectory проходит поверх эстакады;
* direction arrows проецируются и ориентируются по slope;
* Venue markings проецируются;
* Exercise markings проецируются;
* Venue cones ставятся на surface;
* Exercise cones ставятся на surface;
* raycasts выполняются после physics synchronization;
* missing hit использует fallback;
* warnings группируются по source ID;
* collision disabled objects не блокируют движение;
* collision enabled objects сохраняют collision;
* Viewer reload удаляет старые physics bodies;
* проект собирается без compile errors;
* runtime logs не содержат необработанных ошибок.
