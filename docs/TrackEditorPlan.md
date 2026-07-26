# Track Editor Implementation Plan

## 1. Назначение

Track Editor собирает конкретную трассу из переиспользуемых Exercise Definition.

Workflow:

```text
Exercise Library
        ↓
Track Editor
        ↓
Track Project
        ↓
Export
        ↓
Exported Track JSON
        ↓
Viewer
```

Track Editor должен оставаться отдельным режимом и отдельной сценой.

---

# 2. Рекомендуемая структура

```text
scenes/
├── viewer/
│   └── Viewer.tscn
└── editor/
    ├── ExerciseEditor.tscn
    └── TrackEditor.tscn
```

Scripts могут быть разделены:

```text
scripts/
├── common/
├── domain/
├── serialization/
├── viewer/
└── editor/
   ├── exercise/
   └── track/
```

Не выполнять массовый рефакторинг уже работающих файлов только ради соответствия примеру.

---

# 3. Track Editor Iteration 1

## Цель

Создать базовый 2D Track Editor, в котором пользователь может:

1. создать проект трассы;
2. выбрать Exercise Definition из библиотеки;
3. разместить экземпляр на площадке;
4. перемещать его;
5. поворачивать;
6. масштабировать;
7. задавать порядок прохождения;
8. сохранить и повторно открыть Track Project.

Генерация переходных spline и экспорт для Viewer относятся к следующей итерации.

---

# 4. Сцена и layout

Создать:

```text
TrackEditor.tscn
```

Рекомендуемый layout:

```text
┌──────────────────────────────────────────────────────┐
│ New  Open  Save  Save As                             │
├───────────────┬──────────────────────┬───────────────┤
│ Exercise      │                      │ Properties    │
│ Library       │     Track Canvas     │               │
│               │                      │ Position      │
│ folders/files │                      │ Rotation      │
│               │                      │ Scale X/Y     │
├───────────────┴──────────────────────┴───────────────┤
│ Route Order / Instances                              │
└──────────────────────────────────────────────────────┘
```

Точный визуальный стиль не фиксируется.

---

# 5. Track canvas

Canvas должен:

* работать в 2D с видом сверху;
* показывать area bounds;
* показывать meter grid;
* показывать origin;
* поддерживать pan;
* поддерживать zoom;
* использовать те же соглашения координат, что Exercise Editor.

Grid:

```text
1 × 1 meter
```

Более заметные линии:

```text
каждые 5 meters
```

---

# 6. Новый Track Project

Команда:

```text
New Track
```

Поля:

* track id;
* track name;
* area width;
* area length.

Предлагаемые defaults:

```text
width  = 100 m
length = 40 m
```

После создания:

* instances пусты;
* документ dirty;
* canvas адаптирован под area.

---

# 7. Exercise Library

Track Editor использует:

```text
res://exercises/
```

Дерево библиотеки должно переиспользовать существующую реализацию Exercise Editor, если это возможно без жёсткой связи UI-компонентов.

Минимальные операции:

* открыть папку;
* выбрать Exercise Definition;
* обновить дерево;
* добавить выбранное упражнение на трассу.

Создание и редактирование Exercise Definition остаётся задачей Exercise Editor.

Track Editor не должен изменять файлы Exercise Definition.

---

# 8. Добавление экземпляра

Пользователь выбирает Exercise Definition и выполняет:

```text
Add to Track
```

или перетаскивает его на canvas, если drag-and-drop реализуется просто и надёжно.

Для первой версии достаточно:

1. выбрать definition;
2. нажать `Add to Track`;
3. разместить экземпляр в центре площадки;
4. затем переместить его.

Новый экземпляр получает:

```text
instanceId
position
rotationDeg = 0
scale.x = 1
scale.y = 1
```

---

# 9. Отображение экземпляра

Track Editor должен показывать transformed preview:

* bounds;
* cones;
* trajectory;
* markings.

Для markings:

* `visibleInViewer` не должен скрывать линию в Track Editor;
* скрытая для Viewer линия должна показываться с editor-only визуальным признаком.

Track Editor не должен изменять исходный Exercise Definition.

---

# 10. Выбор экземпляра

Клик по геометрии или bounds выбирает instance.

Выбранный instance:

* визуально выделяется;
* отображается в Properties;
* синхронизируется с Route Order list.

Клик по строке Route Order выбирает соответствующий instance на canvas.

---

# 11. Перемещение

Выбранный instance можно перемещать:

* drag-and-drop;
* числовыми Position X/Y.

Snap должен использовать существующую систему Exercise Editor.

Рекомендуемый default:

```text
0.25 m
```

---

# 12. Rotation

Properties:

```text
Rotation, degrees
```

Минимальные способы:

* числовое поле;
* кнопки поворота на `-15°` и `+15°`.

Свободный rotation handle расположен на каждом углу выбранного bounds. Drag
поворачивает instance вокруг его центра; при hover используется изогнутый
двунаправленный курсор поворота.

Rotation не изменяет исходное Exercise Definition.

---

# 13. Scale

Properties:

```text
Scale X
Scale Y
```

Условия:

```text
abs(Scale X) > 0
abs(Scale Y) > 0
```

Предлагаемый диапазон UI:

```text
0.1 ... 10
```

Модуль scale редактируется числовыми полями и drag сторон bounds. Изменение
размера выполняется симметрично относительно центра instance. Знак scale
зарезервирован для явных кнопок зеркалирования:

* горизонтально — изменить знак `scale.x`;
* вертикально — изменить знак `scale.y`.

При hover стороны курсор показывает двунаправленное изменение размера по
экранной горизонтали или вертикали, соответствующей ориентации стороны.

Можно добавить кнопку:

```text
Lock uniform scale
```

но она не обязательна в Iteration 1.

---

# 14. Bounds и selection

Selection hit testing может использовать:

1. transformed bounds;
2. transformed rendered geometry.

Для MVP достаточно надёжного выбора по transformed bounds.

При перекрытии нескольких instances допустим выбор последнего добавленного или верхнего в draw order.

Cycling selection можно добавить позднее.

---

# 15. Route Order

`instances[]` является порядком прохождения.

UI должен отображать:

```text
1. Exercise name
2. Exercise name
3. Exercise name
```

Команды:

* Move Up;
* Move Down;
* Delete;
* Select.

Изменение порядка:

* изменяет порядок `instances[]`;
* устанавливает dirty state;
* обновляет отображаемые номера.

---

# 16. Номера элементов

На canvas рекомендуется показывать номер порядка рядом с каждым instance:

```text
1
2
3
```

Это временный editor overlay.

Он не входит в Track Project JSON и не экспортируется Viewer.

---

# 17. Удаление instance

Выбранный instance удаляется:

* клавишей Delete;
* кнопкой Delete.

Exercise Definition файл при этом не удаляется.

После удаления Route Order перенумеровывается.

---

# 18. Изменение area

Пользователь может изменить:

```text
area.width
area.length
```

Существующие instances не масштабируются и не перемещаются автоматически.

Если instance выходит за пределы area:

* он сохраняется;
* Editor показывает warning или визуальный признак.

---

# 19. Track Library

Корень:

```text
res://tracks/
```

Поддержать:

* вложенные подпапки;
* выбор папки;
* создание подпапки;
* Save;
* Save As;
* Open;
* Refresh.

Применить те же требования безопасности путей, что для Exercise Library.

Не реализовывать пока:

* rename;
* recursive delete;
* tags;
* thumbnails;
* database.

---

# 20. Save/Open

Сохранение соответствует:

```text
docs/TrackProjectFormat.md
```

Не сериализовать:

* pan;
* zoom;
* selected instance;
* expanded folders;
* active tool;
* cached Exercise Definition;
* rendering geometry.

Open должен:

1. прочитать JSON;
2. проверить formatVersion;
3. валидировать DTO;
4. загрузить Exercise Definition для instances;
5. только после базовой успешной проверки заменить текущий проект.

Отсутствующее упражнение не должно блокировать открытие всего проекта.

Такой instance становится unresolved.

---

# 21. Unresolved instances

Если `exercisePath` отсутствует или повреждён:

* instance остаётся в Route Order;
* на canvas показывается placeholder bounds/marker;
* отображается путь;
* выводится warning;
* transform можно сохранить.

Track Editor не должен молча удалять unresolved instance.

---

# 22. Dirty state

Изменения, устанавливающие dirty:

* metadata;
* area;
* добавление instance;
* удаление instance;
* move;
* rotation;
* scale;
* reorder.

Перед New/Open/Close необходима защита несохранённых изменений.

---

# 23. Общие geometry utilities

Track Editor должен переиспользовать:

* Point2 DTO;
* trajectory geometry;
* Bezier sampling;
* markings rendering logic;
* coordinate conversion;
* color parsing;
* style rendering;
* library path validation.

Не копировать реализацию Exercise Editor целиком.

Допустимы общие reusable UI или utility-компоненты.

---

# 24. Не реализовывать в Iteration 1

* автоматические transition splines;
* ручные transition overrides;
* экспорт Track JSON;
* запуск Viewer из Editor;
* environment objects;
* checkpoints;
* collision detection;
* automatic layout;
* undo/redo framework;
* minimap;
* 3D preview;
* drag-and-drop из дерева, если простой Add button достаточен;
* thumbnails;
* embedded Exercise Editor.

---

# 25. Definition of Done

* Track Editor запускается отдельной сценой.
* Создаётся новый Track Project.
* Видны area и grid.
* Exercise Library отображается.
* Exercise Definition добавляется на canvas.
* Несколько instances одного definition поддерживаются.
* Instance выбирается.
* Instance перемещается.
* Rotation работает.
* Scale X/Y работает.
* Cones, trajectory и markings отображаются после transform.
* Route Order работает.
* Move Up/Down работает.
* Delete не удаляет Exercise Definition.
* Track Project сохраняется.
* Track Project повторно открывается.
* Вложенные папки `res://tracks/` работают.
* Unresolved instance не ломает весь проект.
* Exercise Editor продолжает работать.
* Viewer продолжает работать.
* Нет compile/runtime errors.

* # Track Editor Iteration 2 — Global Trajectory and Viewer Export

## 1. Цель итерации

Iteration 2 должна замкнуть основной пользовательский workflow:

```text
Exercise Editor
        ↓
Exercise Definition Library
        ↓
Track Editor
        ↓
Track Project
        ↓
World geometry generation
        ↓
Automatic transition splines
        ↓
Exported Track JSON
        ↓
Viewer
```

После завершения этой итерации пользователь должен иметь возможность:

1. создать несколько Exercise Definition;
2. разместить их в Track Editor;
3. определить порядок прохождения;
4. увидеть автоматически построенные переходы;
5. экспортировать самодостаточный Track JSON;
6. открыть экспортированный файл в Viewer.

---

# 2. Граница ответственности

## Track Project

Track Project хранит только редактируемую структуру трассы:

* metadata;
* area;
* упорядоченные ExerciseInstance;
* ссылки на Exercise Definition;
* position;
* rotation;
* scale.

Track Project не хранит в Iteration 2:

* преобразованные мировые конусы;
* преобразованные markings;
* глобальную trajectory;
* автоматически построенные переходные spline;
* rendering samples;
* результаты валидации.

## Track Editor

Track Editor отвечает за:

* загрузку Exercise Definition;
* применение transform;
* построение мировой геометрии;
* вычисление касательных;
* построение переходных cubicBezier;
* предварительный просмотр;
* валидацию;
* экспорт snapshot.

## Exported Track JSON

Exported Track JSON содержит уже готовые мировые данные.

Viewer не должен:

* загружать Exercise Definition;
* применять ExerciseInstance transform;
* вычислять entry/exit;
* вычислять касательные;
* строить переходы.

---

# 3. World geometry

Для каждого разрешённого ExerciseInstance Track Editor должен построить мировое представление Exercise Definition.

Порядок преобразования каждой локальной точки:

```text
local point
    ↓
scale X/Y
    ↓
rotation
    ↓
translation
    ↓
track world point
```

Преобразование применяется к:

* cone positions;
* marking points;
* trajectory polyline points;
* cubicBezier start;
* cubicBezier control1;
* cubicBezier control2;
* cubicBezier end;
* entryPoint;
* exitPoint;
* bounds corners;
* checkpoint geometry, когда checkpoints будут реализованы.

Не масштабируются:

* физический размер конуса;
* `marking.widthMeters`.

---

## 3.1. Общая функция transform

Должна существовать единая операция преобразования точки:

```text
TransformLocalPoint(localPoint, instanceTransform)
```

Она должна использоваться для всех видов геометрии.

Не допускается независимая реализация математики transform для:

* cones;
* markings;
* trajectory;
* bounds;
* entry/exit.

---

## 3.2. Поворот

В доменной системе координат используется положительный поворот против часовой стрелки.

Для локальной точки после scale:

```text
scaledX = localX × scaleX
scaledY = localY × scaleY
```

Затем применяется rotation и translation.

Точная математическая реализация должна быть централизована в geometry utility.

---

# 4. Transform trajectory

## 4.1. Polyline

Для `polyline` преобразуется каждая точка:

```text
local points[]
        ↓
instance transform
        ↓
world points[]
```

Порядок точек сохраняется.

## 4.2. CubicBezier

Для `cubicBezier` независимо преобразуются:

* `start`;
* `control1`;
* `control2`;
* `end`.

После affine transform результат остаётся корректной кубической кривой Безье.

Не следует:

* сначала дискретизировать локальную кривую;
* затем экспортировать полученные samples.

Экспорт должен сохранять исходное spline-представление.

---

# 5. Касательные Exercise Definition

Направление входа и выхода определяется фактической trajectory.

Отдельные поля направления не используются.

## 5.1. Polyline

Входная касательная:

```text
points[1] - points[0]
```

Выходная касательная:

```text
points[last] - points[last - 1]
```

## 5.2. CubicBezier

Входная касательная:

```text
control1 - start
```

Выходная касательная:

```text
end - control2
```

## 5.3. Trajectory из нескольких segments

Для всей trajectory:

* входная касательная берётся из первого валидного segment;
* выходная касательная берётся из последнего валидного segment.

Если первый или последний segment имеет нулевую касательную, Track Editor должен попытаться найти ближайшее ненулевое направление внутри этого segment.

Например, для polyline допускается пропуск последовательных совпадающих точек.

Если корректное направление определить невозможно, это является блокирующей ошибкой экспорта.

---

# 6. Transform tangent

При неравномерном scale нельзя корректно получить мировую касательную простым поворотом уже нормализованного локального направления.

Правильный порядок:

```text
local tangent
      ↓
scale vector components by scale X/Y
      ↓
rotation
      ↓
normalization
      ↓
world tangent
```

Translation к направлению не применяется.

Концептуально:

```text
worldTangent =
    Normalize(
        Rotate(
            ScaleVector(localTangent, scaleX, scaleY),
            rotationDeg
        )
    )
```

Это правило особенно важно для:

```text
scaleX != scaleY
```

---

# 7. Global trajectory

Глобальная trajectory формируется в порядке:

```text
TrackProject.instances[]
```

Для трассы из трёх элементов:

```text
Instance A transformed trajectory
        ↓
generated transition A → B
        ↓
Instance B transformed trajectory
        ↓
generated transition B → C
        ↓
Instance C transformed trajectory
```

Результат:

```text
GlobalTrajectory
└─ Segments[]
   ├─ transformed exercise segment
   ├─ generated transition cubicBezier
   ├─ transformed exercise segment
   ├─ generated transition cubicBezier
   └─ transformed exercise segment
```

Порядок `segments[]` является порядком движения.

---

# 8. Automatic transition spline

Между каждой парой соседних ExerciseInstance:

```text
A
↓
B
```

Track Editor создаёт один переходный `cubicBezier`.

Опорные точки:

```text
P0 = world ExitPoint A
P3 = world EntryPoint B
```

Направления:

```text
T0 = normalized world exit tangent A
T1 = normalized world entry tangent B
```

Управляющие точки:

```text
P1 = P0 + T0 × L0
P2 = P3 - T1 × L1
```

---

## 8.1. Длины управляющих векторов

Начальная стратегия Iteration 2:

```text
distance = |P3 - P0|
baseLength = distance / 3
```

Затем:

```text
L0 = Clamp(baseLength, MinTransitionHandleLength, MaxTransitionHandleLength)
L1 = Clamp(baseLength, MinTransitionHandleLength, MaxTransitionHandleLength)
```

Рекомендуемые начальные значения:

```text
MinTransitionHandleLength = 0.5 m
MaxTransitionHandleLength = 15.0 m
```

Эти значения являются параметрами реализации Track Editor, а не частью JSON-контракта.

Они могут быть скорректированы после практического тестирования.

---

## 8.2. Очень короткий переход

Если расстояние между `P0` и `P3` меньше допустимой численной погрешности:

* отдельный переходный segment может быть не создан;
* должно быть проверено совпадение конца A и начала B;
* глобальная trajectory должна оставаться непрерывной.

Если точки почти совпадают, но направления конфликтуют, Editor может вывести warning.

---

## 8.3. Нулевая касательная

Если невозможно получить ненулевую:

* выходную касательную A;
* входную касательную B;

Track Editor не должен молча выбирать произвольное направление.

Необходимо:

1. добавить блокирующую validation error;
2. указать `instanceId` проблемных элементов;
3. не выполнять экспорт до исправления данных.

---

# 9. Производные данные

Автоматические переходы являются производными данными.

Они пересчитываются при:

* добавлении instance;
* удалении instance;
* перемещении instance;
* изменении rotation;
* изменении scale;
* изменении Route Order;
* открытии Track Project;
* обновлении Exercise Definition;
* повторном разрешении unresolved instance.

Переходы не сохраняются в Track Project formatVersion 1.

Они существуют:

* в runtime-модели Track Editor;
* в canvas preview;
* в Exported Track JSON.

---

# 10. Preview переходов

Track Editor должен отображать автоматически построенные переходы на canvas.

Они должны визуально отличаться от внутренней trajectory Exercise Definition.

Например:

* другим оттенком;
* пунктирным editor overlay;
* отдельной толщиной;
* дополнительным значком в начале или конце.

Этот визуальный стиль является состоянием Editor и не экспортируется.

Желательно добавить переключатель:

```text
Show automatic transitions
```

Если переключатель заметно усложняет UI, он не является блокирующим требованием Iteration 2.

---

# 11. Пересчёт preview

Preview должен обновляться после завершения изменения transform.

Допустимо обновлять его в реальном времени во время drag, если производительности достаточно.

Минимально обязательное поведение:

* после завершения drag;
* после числового изменения transform;
* после reorder;
* после add/delete.

Preview не должен использовать устаревшие переходы после изменения порядка трассы.

---

# 12. Exported Track JSON

Track Editor добавляет действие:

```text
Export for Viewer
```

Оно создаёт самодостаточный JSON согласно:

```text
docs/TrackFormat.md
```

Текущая экспортная версия:

```text
formatVersion = 3
```

Экспорт включает:

* track metadata;
* area;
* elements metadata;
* transformed cones;
* transformed markings;
* global trajectory;
* checkpoints.

Даже если checkpoints ещё не используются:

```json
"checkpoints": []
```

должно присутствовать, если это требуется канонической структурой Track JSON.

---

# 13. Exported elements metadata

`elements[]` содержит диагностическое описание исходных instances:

```json
{
  "instanceId": "exercise-instance-001",
  "definitionId": "slalom-5",
  "exercisePath": "slaloms/slalom-5.json",
  "position": {
    "x": 10.0,
    "y": 15.0
  },
  "rotationDeg": 30.0,
  "scale": {
    "x": 1.0,
    "y": 1.2
  }
}
```

Viewer не использует эти данные для повторного построения геометрии.

Поле `exercisePath` является диагностическим и может быть опущено, если текущий TrackFormat его не закрепляет.

---

# 14. Exported IDs

Два экземпляра одного Exercise Definition не должны создавать одинаковые IDs.

Для локальных объектов экспортный ID строится на основе:

```text
instanceId
+
local object id
```

Рекомендуемый формат:

```text
{instanceId}--{localId}
```

Примеры:

```text
exercise-instance-001--cone-001
exercise-instance-001--marking-002
exercise-instance-001--trajectory-segment-001
```

Для перехода:

```text
transition--{fromInstanceId}--{toInstanceId}
```

Пример:

```text
transition--exercise-instance-001--exercise-instance-002
```

IDs не должны зависеть от:

* отображаемого имени упражнения;
* номера элемента в Route Order;
* имени Track Project файла.

Изменение Route Order не должно создавать коллизии IDs.

---

# 15. Export cones

Для каждого resolved instance:

1. взять локальные cones;
2. применить instance transform к position;
3. сохранить:

   * экспортный id;
   * world position;
   * color;
   * type.

Физический размер cone не экспортируется через scale instance.

---

# 16. Export markings

Для каждого marking:

1. преобразовать все points в мировые координаты;
2. сохранить:

   * экспортный id;
   * type;
   * points;
   * color;
   * widthMeters;
   * style;
   * visibleInViewer.

`widthMeters` не масштабируется.

Marking с:

```text
visibleInViewer = false
```

остаётся в экспортированном JSON.

Viewer решает, создавать ли его визуальное представление.

---

# 17. Export trajectory

В экспортируемую trajectory последовательно добавляются:

1. transformed segments первого ExerciseInstance;
2. transition spline к следующему;
3. transformed segments следующего ExerciseInstance;
4. следующие переходы и упражнения.

Типы сохраняются:

```text
polyline
cubicBezier
```

Не следует объединять все segments в одну дискретизированную polyline.

Не следует сохранять rendering samples Bezier.

---

# 18. Validation

Перед экспортом Track Editor формирует validation result:

```text
ValidationResult
├─ Errors[]
└─ Warnings[]
```

## 18.1. Блокирующие ошибки

Экспорт блокируется при наличии:

* unresolved ExerciseInstance;
* отсутствующего Exercise Definition;
* неподдерживаемого Exercise Definition formatVersion;
* отсутствующей trajectory;
* невалидного trajectory segment;
* polyline менее чем с двумя различимыми точками;
* cubicBezier с отсутствующей geometry;
* разрыва внутренней trajectory;
* несовпадения entryPoint с началом trajectory;
* несовпадения exitPoint с концом trajectory;
* невозможности определить входную или выходную касательную;
* `scale.x <= 0`;
* `scale.y <= 0`;
* NaN;
* Infinity;
* duplicate exported ID;
* невозможности сериализовать итоговый документ.

## 18.2. Предупреждения

Экспорт не блокируется из-за:

* instance bounds за пределами area;
* части geometry за пределами area;
* пересечения transformed bounds разных instances;
* очень длинного перехода;
* перехода, выходящего за area;
* очень короткого перехода;
* резкого изменения направления;
* неизвестного необязательного metadata.

---

# 19. Validation UI

Перед экспортом пользователь должен увидеть:

* количество ошибок;
* количество предупреждений;
* понятное описание;
* instanceId или object id;
* при возможности — имя упражнения.

Если есть ошибки:

```text
Export blocked
```

Если есть только warnings:

```text
Export allowed
```

Допустимо потребовать подтверждение экспорта при warnings, но это не обязательно.

---

# 20. Export folder

Корневой каталог:

```text
res://exports/tracks/
```

Если каталог отсутствует, Track Editor должен создать его.

Поддержать:

* выбор имени файла;
* предлагаемое имя на основе `track.id`;
* расширение `.json`;
* безопасную нормализацию;
* запрет выхода за library root;
* понятное сообщение об успехе или ошибке.

Пример:

```text
res://exports/tracks/training-2026-07-26.json
```

Экспорт не является сохранением Track Project.

Успешный экспорт:

* не изменяет путь Track Project;
* не очищает dirty state;
* не заменяет команду Save.

---

# 21. Export и dirty state

Track Project может быть:

```text
dirty = true
```

и при этом экспортирован.

Экспорт использует текущее состояние документа в памяти.

Рекомендуется предупреждать:

```text
Track Project contains unsaved changes.
The exported snapshot will include the current in-memory state.
```

Но экспорт не должен автоматически сохранять Track Project без явного действия пользователя.

---

# 22. Viewer verification

Экспортированный файл должен открываться существующим Viewer.

Необходимо проверить:

* area;
* cones;
* markings;
* marking colors;
* marking widths;
* solid/dashed/dotted;
* hidden markings;
* internal polyline segments;
* internal cubicBezier segments;
* generated transition splines;
* direction arrows;
* повторную загрузку другого файла.

Viewer не должен получать ссылку на Track Project или Exercise Library.

---

# 23. Empty and single-instance tracks

## Empty Track Project

Пустой Track Project можно сохранить как проект.

Экспорт пустой трассы должен быть:

* либо заблокирован понятной validation error;
* либо разрешён как пустой snapshot, если это сознательно принято.

Для Iteration 2 рекомендуется блокировать экспорт:

```text
Track must contain at least one resolved ExerciseInstance.
```

## One instance

Трасса из одного instance:

* экспортируется;
* не содержит transition segment;
* global trajectory равна transformed trajectory этого упражнения.

---

# 24. Unresolved instances

Если Track Project содержит unresolved instance:

* canvas продолжает работать;
* Route Order сохраняется;
* Track Project можно повторно сохранить;
* экспорт блокируется.

Validation error должна содержать:

* `instanceId`;
* `exercisePath`;
* причину ошибки загрузки.

---

# 25. Производительность

Для MVP допустим полный пересчёт мировой trajectory после каждого структурного изменения.

Не требуется:

* incremental dependency graph;
* background processing;
* cache invalidation framework;
* multithreaded geometry generation.

Можно использовать небольшой runtime cache resolved Exercise Definition и transformed preview, если он не становится вторым источником persisted данных.

---

# 26. Не реализовывать в Iteration 2

Не добавлять:

* ручное редактирование transition control points;
* transition overrides;
* сохранение transitions в Track Project;
* automatic obstacle avoidance;
* minimum turning radius;
* motorcycle physics;
* speed profile;
* collision solver;
* автоматическую перестановку ExerciseInstance;
* undo/redo framework;
* environment objects;
* checkpoints gameplay;
* запуск Viewer из Track Editor;
* embedded 3D preview;
* новые trajectory segment types.

---

# 27. Definition of Done

Iteration 2 завершена, если:

* мировая geometry корректно строится для каждого resolved instance;
* non-uniform scale корректно влияет на geometry и tangent;
* global trajectory формируется в Route Order;
* между соседними instances создаются cubicBezier transitions;
* transitions обновляются после transform и reorder;
* transitions отображаются в Track Editor;
* экспортируются cones;
* экспортируются markings;
* экспортируется global trajectory;
* exported IDs уникальны;
* hidden markings остаются в JSON;
* Viewer не отображает hidden markings;
* Track JSON соответствует formatVersion 3;
* unresolved instance блокирует export, но не ломает Track Project;
* warnings не блокируют export;
* exported JSON открывается Viewer;
* Exercise Editor продолжает работать;
* Track Editor Iteration 1 продолжает работать;
* Viewer продолжает работать;
* проект собирается без compile errors;
* runtime logs не содержат необработанных ошибок.

