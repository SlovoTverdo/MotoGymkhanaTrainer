# Domain Model

## 1. Основные принципы

Трасса собирается из переиспользуемых элементов упражнений.

Каждый элемент описывает собственную геометрию в локальной двумерной системе координат:

* границы элемента;
* конусы;
* дополнительную разметку;
* точку входа;
* точку выхода;
* траекторию движения внутри элемента;
* необязательные контрольные точки.

При создании трассы экземпляры элементов:

* размещаются на площадке;
* поворачиваются;
* при необходимости независимо масштабируются по X и Y;
* упорядочиваются в последовательность прохождения.

Editor автоматически строит плавный участок траектории между точкой выхода предыдущего элемента и точкой входа следующего.

Отдельной доменной сущности `Connector` нет.

Соединительный участок является обычным сегментом итоговой траектории.

---

# 2. ExerciseDefinition

`ExerciseDefinition` — переиспользуемый шаблон упражнения в собственной локальной двумерной системе координат.

Содержит:

* `id`;
* `name`;
* `version`;
* локальный origin / геометрический центр;
* `bounds`;
* `cones[]`;
* `markings[]`;
* `entryPoint`;
* `exitPoint`;
* `trajectory`;
* `checkpoints[]`.

Концептуально:

```text
ExerciseDefinition
├─ Id
├─ Name
├─ Version
├─ Bounds
├─ Cones[]
├─ Markings[]
├─ EntryPoint
├─ ExitPoint
├─ Trajectory
│  └─ Segments[]
└─ Checkpoints[]
```

Все координаты внутри `ExerciseDefinition` являются локальными относительно origin элемента.

---

# 3. Bounds

`bounds` описывает базовые габариты упражнения:

```text
width
length
```

Границы нужны:

* для отображения элемента в Editor;
* для выбора;
* для предварительного обнаружения пересечений;
* для масштабирования;
* для удобства размещения элементов.

`bounds` не являются физической коллизией.

---

# 4. Cones

Каждый конус содержит как минимум:

* локальную позицию;
* цвет;
* тип.

Логический цвет может иметь значение `none`. В этом случае физическая модель
конуса остаётся обычной, а дополнительный цветовой навигационный маркер не
создаётся.

Пример концептуально:

```text
Cone
├─ Position
├─ Color
└─ Type
```

Размер самого 3D-конуса является физическим свойством его типа и не масштабируется вместе с упражнением.

При масштабировании `ExerciseInstance` изменяется только положение конуса относительно центра элемента.

---

# 5. Markings

`markings[]` описывает дополнительную визуальную разметку упражнения.

Например:

* жёлтая линия между конусами;
* граница стартовой зоны;
* стоп-линия;
* стрелка;
* дополнительный контур.

Потенциальные типы:

```text
line
polyline
arc
polygon
arrow
```

Координаты разметки масштабируются вместе с экземпляром элемента.

Физическая толщина линии не масштабируется.

---

# 6. EntryPoint и ExitPoint

Каждый элемент имеет:

```text
EntryPoint
ExitPoint
```

Они задают места, где внутренняя траектория упражнения:

* начинается;
* заканчивается.

Это геометрические точки, а не отдельные порты со своим независимым направлением.

Направление движения на входе и выходе **не хранится отдельно**.

Оно вычисляется из касательной самой trajectory.

Это исключает ситуацию, когда сохранённое направление точки входа/выхода противоречит фактической форме траектории.

---

# 7. Trajectory

Trajectory является единственным источником геометрии движения.

Она состоит из упорядоченных сегментов:

```text
Trajectory
└─ Segments[]
   ├─ Polyline
   ├─ CubicBezier
   └─ потенциальные будущие типы
```

На текущем этапе определены два типа:

```text
polyline
cubicBezier
```

---

## 7.1. Polyline

`polyline` представляет последовательность точек, соединённых прямыми отрезками.

Концептуально:

```text
P0 → P1 → P2 → ... → Pn
```

Подходит для:

* простых участков;
* змейки;
* явно заданных опорных точек;
* дискретизированных сложных траекторий.

Первая точка является началом сегмента.

Последняя точка является концом сегмента.

---

## 7.2. CubicBezier

`cubicBezier` представляет кубическую кривую Безье:

```text
P0
 \
  P1
    \
     curve
          \
           P2
             \
              P3
```

Содержит:

* `start`;
* `control1`;
* `control2`;
* `end`.

Используется:

* внутри элементов, если это удобно для описания упражнения;
* для автоматически созданных плавных переходов между соседними упражнениями.

---

# 8. Касательная trajectory

Направление движения на входе и выходе вычисляется непосредственно из trajectory.

## Для polyline

Входная касательная:

```text
points[1] - points[0]
```

Выходная касательная:

```text
points[last] - points[last - 1]
```

## Для cubicBezier

Входная касательная:

```text
control1 - start
```

Выходная касательная:

```text
end - control2
```

Если trajectory состоит из нескольких segments:

* входное направление определяется первым валидным segment;
* выходное направление определяется последним валидным segment.

Полученный вектор нормализуется перед использованием при построении перехода.

---

# 9. Согласованность EntryPoint / ExitPoint и trajectory

`EntryPoint` должен совпадать с началом trajectory элемента.

`ExitPoint` должен совпадать с концом trajectory элемента.

Editor должен валидировать это условие.

Допускается небольшая численная погрешность.

Не допускается ситуация:

```text
EntryPoint != начало trajectory
```

или:

```text
ExitPoint != конец trajectory
```

без диагностического предупреждения или ошибки валидации.

---

# 10. ExerciseInstance

`ExerciseInstance` — размещение `ExerciseDefinition` на конкретной трассе.

Содержит:

* `instanceId`;
* ссылку на definition во внутреннем формате Editor;
* позицию;
* rotation;
* `scaleX`;
* `scaleY`.

Концептуально:

```text
ExerciseInstance
├─ InstanceId
├─ DefinitionId
├─ Position
├─ Rotation
└─ Scale
   ├─ X
   └─ Y
```

Исходный `ExerciseDefinition` при этом не изменяется.

---

# 11. Преобразование координат экземпляра

К локальной геометрии применяется следующий порядок:

```text
Local coordinates
      ↓
Scale X/Y
      ↓
Rotation
      ↓
Translation
      ↓
World coordinates
```

Преобразование применяется к:

* позициям конусов;
* точкам markings;
* `entryPoint`;
* `exitPoint`;
* всем trajectory points;
* всем Bezier control points;
* checkpoints;
* bounds.

Не масштабируются:

* физические размеры конусов;
* толщина линий;
* другие физически фиксированные визуальные параметры.

---

# 12. Порядок элементов трассы

Трасса содержит упорядоченную последовательность `ExerciseInstance`.

Например:

```text
Element 1
   ↓
Element 2
   ↓
Element 3
```

Порядок определяет:

* последовательность прохождения;
* какие пары элементов должны быть автоматически соединены.

Для каждой соседней пары:

```text
A → B
```

Editor создаёт переход:

```text
A.ExitPoint
      ↓
generated cubicBezier
      ↓
B.EntryPoint
```

---

# 13. Автоматическое соединение соседних элементов

Отдельной сущности `Connector` нет.

Editor создаёт обычный `cubicBezier` segment между:

```text
P0 = transformed ExitPoint элемента A
P3 = transformed EntryPoint элемента B
```

Направления вычисляются из trajectory:

```text
T0 = нормализованная выходная касательная A
T1 = нормализованная входная касательная B
```

Управляющие точки:

```text
P1 = P0 + T0 * L0

P2 = P3 - T1 * L1
```

где:

* `L0`;
* `L1`;

— автоматически определяемые длины управляющих векторов.

Начальная стратегия выбора `L0` и `L1` может зависеть от расстояния между элементами.

Точный алгоритм генерации не является частью стабильного exported JSON contract и может развиваться независимо.

---

# 14. Ручная коррекция перехода

Автоматически созданный spline может оказаться неудобным.

Editor должен в перспективе позволить пользователю вручную корректировать:

```text
control1
control2
```

Ручная коррекция относится к **редакторскому проекту трассы**, а не к библиотеке `ExerciseDefinition`.

Для сохранения такой правки Editor может хранить transition override между конкретной парой соседних `ExerciseInstance`.

Концептуально:

```text
TransitionOverride
├─ FromElementInstanceId
├─ ToElementInstanceId
├─ Control1
└─ Control2
```

Это внутренняя сущность Editor.

Она не является частью обязательного exported Viewer format.

При экспорте результат override превращается в обычный:

```text
cubicBezier trajectory segment
```

Viewer не должен знать, был spline:

* сгенерирован автоматически;
* исправлен пользователем;
* изначально частью упражнения.

---

# 15. # Track compilation and global trajectory

## 1. Track compilation

Track compilation — процесс преобразования редактируемого Track Project в самодостаточный Exported Track snapshot.

```text
Track Project
    +
Exercise Definition Library
    ↓
Track compilation
    ↓
Exported Track JSON
```

Compilation не изменяет:

* Track Project;
* Exercise Definition.

Она создаёт новое производное представление.

---

## 2. Compiled ExerciseInstance

Для каждого resolved ExerciseInstance Track Editor создаёт временное compiled-представление:

```text
CompiledExerciseInstance
├─ InstanceId
├─ SourceDefinition
├─ Transform
├─ WorldBounds
├─ WorldCones[]
├─ WorldMarkings[]
├─ WorldTrajectory
├─ WorldEntryPoint
├─ WorldExitPoint
├─ WorldEntryTangent
└─ WorldExitTangent
```

CompiledExerciseInstance не сериализуется в Track Project.

Он может существовать только:

* в runtime Track Editor;
* во время preview;
* во время validation;
* во время export.

---

## 3. Instance transform

Transform содержит:

```text
position
rotationDeg
scaleX
scaleY
```

Порядок применения:

```text
scale X/Y
    ↓
rotation
    ↓
translation
```

Одна общая transform-функция применяется ко всем локальным точкам.

---

## 4. Direction transform

Направляющий вектор не получает translation.

Для преобразования tangent:

```text
local tangent
    ↓
non-uniform scale
    ↓
rotation
    ↓
normalization
    ↓
world tangent
```

Нормализовать tangent до scale нельзя, если:

```text
scaleX != scaleY
```

так как это может дать неправильное мировое направление.

---

## 5. Global trajectory

Глобальная trajectory представляет полный порядок движения по трассе.

Она строится по `TrackProject.instances[]`.

```text
World trajectory instance 1
        ↓
Automatic transition 1 → 2
        ↓
World trajectory instance 2
        ↓
Automatic transition 2 → 3
        ↓
World trajectory instance 3
```

Все segments глобальной trajectory находятся в координатах площадки.

---

## 6. Automatic transition

Automatic transition не является отдельной обязательной доменной сущностью Track Project.

В Iteration 2 это производный `cubicBezier` segment.

```text
P0 = WorldExitPoint A
P3 = WorldEntryPoint B

P1 = P0 + WorldExitTangent A × L0
P2 = P3 - WorldEntryTangent B × L1
```

Automatic transition существует:

* в preview Track Editor;
* в compiled global trajectory;
* в Exported Track JSON.

Он не существует в Track Project formatVersion 1.

---

# TransitionOverride

## 1. Назначение

`TransitionOverride` представляет ручную коррекцию автоматически построенного перехода между соседними ExerciseInstance.

```text
ExerciseInstance A
        ↓
TransitionOverride
        ↓
ExerciseInstance B
```

TransitionOverride принадлежит Track Project.

Он не принадлежит:

* Exercise Definition;
* Viewer;
* Exported Track как отдельная runtime-сущность.

В Exported Track результат override становится обычным `cubicBezier` segment.

---

## 2. Структура

Концептуальная доменная модель:

```text
TransitionOverride
├─ TransitionId
├─ FromInstanceId
├─ ToInstanceId
├─ Control1Offset
└─ Control2Offset
```

Offsets находятся в системе координат трассы.

---

## 3. Endpoints

TransitionOverride не хранит endpoints.

Они вычисляются:

```text
P0 = compiled WorldExitPoint FromInstance
P3 = compiled WorldEntryPoint ToInstance
```

Управляющие точки:

```text
P1 = P0 + Control1Offset
P2 = P3 + Control2Offset
```

Это обеспечивает перемещение endpoints вместе с ExerciseInstance.

---

## 4. Automatic transition state

Переход между соседними instances имеет один из режимов:

```text
Automatic
ManualOverride
Invalid
```

### Automatic

Override отсутствует.

Control points вычисляются алгоритмом автоматического перехода.

### ManualOverride

Существует валидный TransitionOverride для текущей соседней пары.

### Invalid

Невозможно построить transition из-за:

* unresolved instance;
* invalid trajectory;
* undefined tangent;
* non-finite data;
* invalid override.

---

## 5. Transition runtime representation

Track Editor может использовать runtime-модель:

```text
CompiledTransition
├─ TransitionId
├─ FromInstanceId
├─ ToInstanceId
├─ Start
├─ Control1
├─ Control2
├─ End
├─ SourceMode
└─ ValidationState
```

Где `SourceMode`:

```text
Automatic
Override
```

`CompiledTransition` является производной runtime-моделью и не сериализуется напрямую в Track Project.


# Track Editor history and transient state

## 1. Persisted document state

Persisted-состоянием Track Editor является Track Project:

```text
TrackProject
├─ Track metadata
├─ Area
├─ Instances
└─ TransitionOverrides
```

Только это состояние сохраняется в Track Project JSON.

---

## 2. Transient editor state

Transient-состояние существует только во время текущего редакторского сеанса.

Примеры:

```text
TrackEditorSessionState
├─ SelectedInstanceId
├─ SelectedTransitionId
├─ ActiveTool
├─ Pan
├─ Zoom
├─ LockedInstanceIds
├─ Clipboard
├─ LastExportPath
├─ TransitionPreviewVisibility
└─ ValidationDisplayState
```

Transient state:

* не сериализуется в Track Project;
* не является частью Exported Track;
* не влияет на domain formatVersion.

---

## 3. Edit transaction

`EditTransaction` представляет одно логическое изменение persisted Track Project.

Примеры:

* перемещение instance;
* поворот instance;
* изменение scale;
* reorder;
* изменение TransitionOverride;
* Duplicate;
* Delete.

Концептуально:

```text
EditTransaction
├─ Description
├─ BeforeState
└─ AfterState
```

EditTransaction не создаётся для изменений только transient state.

---

## 4. TrackEditorHistory

Концептуальная модель:

```text
TrackEditorHistory
├─ Entries[]
├─ CurrentPosition
├─ SavedPosition
└─ Capacity
```

`CurrentPosition` определяет применённое состояние истории.

`SavedPosition` соответствует последнему успешно сохранённому Track Project.

Dirty state:

```text
CurrentPosition != SavedPosition
```

При snapshot-based реализации допустимо сравнивать сохранённую revision identity вместо непосредственного номера позиции.

---

## 5. History restoration

После Undo или Redo восстанавливается persisted Track Project.

Затем производные данные пересчитываются:

* resolved Exercise Definitions;
* transformed previews;
* automatic transitions;
* overridden transitions;
* global trajectory;
* validation.

Derived runtime data не хранится в history snapshot.

---

## 6. Duplicate semantics

`Duplicate ExerciseInstance` создаёт новый доменный instance.

Копируются:

```text
exercisePath
position
rotationDeg
scale
```

Не копируются:

```text
instanceId
TransitionOverrides
editor lock state
selection identity
runtime cache
```

Duplicate является одной логической EditTransaction.

---

## 7. Temporary instance lock

Temporary lock принадлежит:

```text
TrackEditorSessionState
```

а не `ExerciseInstance`.

Lock обращается к instance через `instanceId`.

Он регулирует editor interaction, но не изменяет семантику трассы.

Lock не влияет на:

* compilation;
* validation;
* transitions;
* export;
* Viewer.

---

## 8. Viewer preview

Viewer preview создаётся через обычную track compilation:

```text
Track Project in memory
        ↓
Track compilation
        ↓
Exported Track snapshot
        ↓
Viewer
```

Viewer preview не должен обходить export contract и не должен читать Track Project напрямую.

---

## 9. Routing-only Exercise Definition

Exercise Definition может не содержать конусов.

Допустимый routing-only элемент содержит:

```text
cones = []
trajectory = valid non-empty trajectory
entryPoint = trajectory start
exitPoint = trajectory end
```

Такой элемент используется для описания произвольного участка маршрута между другими упражнениями.

Он не требует отдельной доменной категории и компилируется как обычный ExerciseInstance.

---

## 6. Identity

Transition identity определяется ориентированной парой:

```text
FromInstanceId → ToInstanceId
```

Route Order является источником соседства.

Номер позиции в массиве не является стабильной identity.

---

## 7. Orphaned override

`TransitionOverride` является orphaned, если:

* FromInstance отсутствует;
* ToInstance отсутствует;
* instances больше не являются соседними;
* порядок стал обратным.

Orphaned override:

* сохраняется;
* не применяется;
* не экспортируется;
* создаёт warning;
* может быть удалён пользователем.

---

## 8. Reset

Reset удаляет TransitionOverride и возвращает transition в режим:

```text
Automatic
```

Он не создаёт новый автоматический объект в Track Project.

---

## 9. Source of truth

Источниками истины являются:

```text
TrackProject.Instances
TrackProject.TransitionOverrides
ExerciseDefinitions
```

Canvas handles и compiled cubicBezier не являются отдельными источниками persisted state.


## 7. Transition identity

Переход получает детерминированный runtime/export ID:

```text
transition--{fromInstanceId}--{toInstanceId}
```

Это позволяет:

* диагностировать ошибки;
* сохранять уникальность;
* в будущем связать transition override с конкретной парой instances.

Существование ID не означает, что transition уже является сохраняемой сущностью Track Project.

---

## 8. Derived data

К производным данным относятся:

* transformed cones;
* transformed markings;
* transformed trajectory;
* world entry/exit;
* world tangents;
* transition splines;
* global trajectory;
* exported IDs;
* validation warnings.

Они не должны становиться вторым редактируемым источником истины.

Источник истины:

```text
Track Project
+
Exercise Definitions
```

---

## 9. Validation model

Compilation возвращает не только snapshot, но и validation result:

```text
TrackCompilationResult
├─ Snapshot
├─ Errors[]
└─ Warnings[]
```

При наличии blocking errors `Snapshot` не должен сохраняться как валидный export.

Warnings не блокируют создание snapshot.

---

## 10. Blocking errors

К blocking errors относятся:

* unresolved Exercise Definition;
* invalid instance transform;
* invalid local trajectory;
* broken trajectory continuity;
* missing entry/exit geometry;
* undefined tangent;
* non-finite coordinate;
* duplicate exported ID.

---

## 11. Warnings

К warnings относятся:

* geometry outside area;
* intersecting instance bounds;
* unusually long transition;
* transition outside area;
* sharp transition;
* very short transition.

Warnings описывают потенциальную проблему трассы, но не обязательно делают файл технически невалидным.

---

## 12. Export snapshot stability

После создания Exported Track JSON дальнейшее изменение Exercise Definition не изменяет уже сохранённый snapshot.

Это ключевое отличие:

```text
Track Project
```

зависит от библиотеки, а:

```text
Exported Track JSON
```

является самодостаточным.

---

## 13. Future transition overrides

В будущей итерации может появиться:

```text
TransitionOverride
```

который сохранит пользовательские control points для пары соседних instances.

До его реализации:

* transitions вычисляются автоматически;
* не редактируются вручную;
* не сериализуются в Track Project;
* пересчитываются после каждого релевантного изменения.

---

# 16. Exported Track Snapshot

Viewer не зависит от библиотеки `ExerciseDefinition`.

При экспорте Editor:

1. разрешает ссылки на definitions;
2. применяет scale;
3. применяет rotation;
4. применяет translation;
5. преобразует внутренние trajectory segments элементов в мировые координаты;
6. генерирует переходные spline между соседними элементами;
7. применяет сохранённые transition overrides, если они есть;
8. формирует одну упорядоченную глобальную trajectory;
9. экспортирует самодостаточный JSON snapshot.

Snapshot содержит уже готовые:

* мировые координаты конусов;
* markings;
* trajectory segments;
* checkpoints;
* metadata.

Viewer не восстанавливает исходную структуру трассы.

---

# 17. Разделение ответственности

```text
ExerciseDefinition Library
          ↓
        Editor
          ↓
ExerciseInstances
          ↓
Transforms
          ↓
Automatic spline generation
          ↓
Optional transition overrides
          ↓
        Export
          ↓
Self-contained Track JSON
          ↓
        Viewer
```

## Library

Хранит определения упражнений.

## Editor

Отвечает за:

* размещение элементов;
* rotation;
* scale X/Y;
* порядок элементов;
* автоматическое соединение соседних элементов;
* ручную коррекцию spline;
* валидацию;
* экспорт.

## Exported JSON

Является контрактом между Editor и Viewer.

## Viewer

Отвечает только за:

* загрузку JSON;
* проверку версии;
* отображение конусов;
* отображение markings;
* отображение итоговой trajectory;
* управление камерой.

Viewer не должен:

* загружать ExerciseDefinition;
* повторно трансформировать элементы;
* вычислять касательные элементов;
* генерировать переходные spline;
* восстанавливать transition overrides.

---

# 18. Расширяемость trajectory

Общая структура:

```text
trajectory
└─ segments[]
```

должна оставаться стабильной.

Текущие типы:

```text
polyline
cubicBezier
```

В будущем могут появиться:

* arc;
* другие типы spline;
* специализированные сегменты.

Неизвестный тип segment должен быть локальной проблемой конкретного сегмента, а не причиной отказа загрузки всей трассы.

# Venue domain

## VenueDefinition

`VenueDefinition` описывает постоянную переиспользуемую площадку.

Концептуальная структура:

```text
VenueDefinition
├─ FormatVersion
├─ VenueMetadata
├─ Area
├─ Panorama
├─ Objects[]
├─ Cones[]
└─ Markings[]
```

# Viewer physical runtime

## ViewerCharacter

Концептуальная runtime-модель:

```text
ViewerCharacter
├─ Mode
├─ CharacterBody3D
├─ CollisionShape
├─ Head
├─ Camera
├─ MovementSettings
└─ PhysicsState
```

Режимы:

```text
Walk
Fly
```

## Walk mode

Walk mode принадлежит Viewer runtime и не сериализуется.

Он использует:

* CharacterBody3D;
* gravity;
* MoveAndSlide;
* floor detection;
* floor snap;
* Venue collision.

## Fly mode

Fly mode предназначен для свободного осмотра.

Он не изменяет Track или Venue domain state.

## Walkable surface

Walkable surface — физическая поверхность, подходящая для:

* character floor;
* projection trajectory;
* projection markings;
* placement cones.

Примеры:

* asphalt surface;
* ramp;
* raised platform.

## World obstacle

World obstacle блокирует ViewerCharacter, но не обязательно используется для projection.

Примеры:

* wall;
* fence;
* post;
* closed object volume.

## SurfaceProjectionService

Концептуальная runtime-модель:

```text
SurfaceProjectionService
├─ PhysicsSpace
├─ ProjectionMask
├─ TopHeight
├─ BottomHeight
├─ VisualOffset
└─ FallbackPolicy
```

Операции:

```text
TryProjectPoint
ProjectPolyline
ProjectCone
```

SurfaceProjectionService является единственным источником runtime surface queries для Track visuals.

## Projected geometry

Projected geometry является производной runtime geometry.

```text
ExportedTrackV4 2D geometry
        +
Venue physics surfaces
        ↓
ProjectedViewerGeometry
```

Она не сериализуется и пересоздаётся при Viewer reload.

## Collision asset ownership

VenueObjectInstance ссылается на `.tscn`.

Collision shapes принадлежат asset scene.

Viewer только:

* инстанцирует asset;
* применяет transform;
* включает или отключает существующую collision.

Viewer не является генератором collision geometry.
