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

* # Track Editor Iteration 3 — Manual Transition Editing

## 1. Цель

Дать пользователю возможность вручную корректировать форму переходных cubicBezier между соседними ExerciseInstance.

Iteration 3 должна сохранять автоматическое построение как default и создавать persisted override только после ручного изменения.

---

# 2. Основной workflow

```text
automatic transition
        ↓
select transition
        ↓
move Bezier handle
        ↓
create/update TransitionOverride
        ↓
save Track Project
        ↓
reload
        ↓
restore manual transition
```

Сброс:

```text
manual transition
        ↓
Reset to Automatic
        ↓
remove TransitionOverride
        ↓
recalculate automatic transition
```

---

# 3. Selection

Пользователь должен иметь возможность выбрать transition на Track Editor canvas.

Выбранный transition:

* визуально выделяется;
* показывает control handles;
* отображается в properties panel;
* указывает связанные instances;
* показывает режим `Automatic` или `Manual`.

Hit testing должен выполняться по дискретизированному preview cubicBezier с экранным tolerance.

---

# 4. Hit testing priority

Рекомендуемый порядок:

1. transition control handle;
2. выбранный transition curve;
3. ExerciseInstance;
4. другие trajectory overlays.

Если текущая архитектура требует другого порядка, он должен оставаться предсказуемым и быть задокументирован.

Control handle нельзя путать:

* с instance bounds;
* с trajectory anchor Exercise Definition;
* с номером Route Order.

---

# 5. Bezier handles

Для выбранного transition показываются:

* `control1`;
* `control2`;
* линия `start → control1`;
* линия `end → control2`.

Endpoints:

* отображаются;
* не редактируются напрямую;
* зависят от ExerciseInstance.

Чтобы изменить endpoints, пользователь перемещает соответствующее упражнение.

---

# 6. First edit

Автоматический transition изначально не имеет TransitionOverride.

При первом перемещении handle:

1. получить текущие P0/P1/P2/P3;
2. создать TransitionOverride;
3. сохранить offsets:

   * `P1 - P0`;
   * `P2 - P3`;
4. применить drag;
5. отметить Track Project dirty.

После этого режим transition становится:

```text
Manual
```

---

# 7. Drag handles

Control handles перемещаются drag-and-drop.

При движении:

* обновляется TransitionOverride;
* обновляется compiled transition;
* обновляется preview;
* устанавливается dirty state;
* properties panel синхронизируется.

Snap:

* по умолчанию может использовать общий snap Track Editor;
* должен существовать простой способ временно отключить snap для тонкой настройки.

Рекомендуемый вариант:

```text
Alt while dragging — disable snap
```

Если Alt уже занят, допускается другой понятный modifier.

---

# 8. Properties panel

Для выбранного transition отображать:

```text
Transition ID
From Exercise
To Exercise
Mode
Start X/Y
Control 1 X/Y
Control 2 X/Y
End X/Y
```

Дополнительно:

```text
Control 1 Offset X/Y
Control 2 Offset X/Y
```

Start и End — read-only.

Control points или offsets можно редактировать численно.

Предпочтительно редактировать абсолютные control point coordinates в UI, а сохранять offsets внутри Track Project.

---

# 9. Reset to Automatic

Добавить действие:

```text
Reset to Automatic
```

Оно:

1. удаляет соответствующий TransitionOverride;
2. пересчитывает автоматическую кривую;
3. обновляет preview;
4. устанавливает dirty state;
5. меняет режим на `Automatic`.

Если override отсутствует, команда disabled.

---

# 10. Instance transform with override

При перемещении, повороте или масштабировании связанных ExerciseInstance:

* endpoints пересчитываются;
* сохранённые offsets не изменяются;
* control points перемещаются вместе с соответствующими endpoints.

```text
P1 = new P0 + saved Control1Offset
P2 = new P3 + saved Control2Offset
```

Такое поведение является простым и предсказуемым для Iteration 3.

Track Editor может показать warning, если после сильного изменения transform переход стал неестественным.

Автоматически менять пользовательские offsets нельзя.

---

# 11. Reorder

После изменения Route Order необходимо повторно сопоставить overrides с соседними парами.

Если пара:

```text
A → B
```

сохранилась, override продолжает применяться.

Если A и B больше не соседи:

* override становится orphaned;
* transition для новых соседей строится автоматически;
* orphaned override не экспортируется;
* пользователь получает warning.

---

# 12. Orphaned overrides UI

Добавить минимальное представление orphaned overrides.

Допустимые варианты:

* отдельный список warnings;
* секция validation;
* диалог очистки.

Минимально необходимая операция:

```text
Remove Orphaned Overrides
```

Она должна требовать подтверждение, поскольку удаляет ручные данные.

Не удалять orphaned overrides автоматически при Open или Reorder.

---

# 13. Deleting instance

При удалении ExerciseInstance, связанном с overrides, показать предупреждение:

```text
This instance is referenced by manual transition overrides.
```

Допустимые действия:

```text
Delete Instance and Keep Orphaned Overrides
Delete Instance and Remove Related Overrides
Cancel
```

Если полный диалог чрезмерно усложняет MVP, допустимо:

* удалить instance;
* сохранить orphaned overrides;
* показать warning.

Не удалять overrides молча.

---

# 14. Validation

## Errors

Экспорт блокируется при:

* non-finite offset;
* duplicate override для одной пары;
* duplicate transitionId;
* применимом override с невалидной geometry;
* невозможности построить конечный cubicBezier.

## Warnings

Экспорт не блокируется из-за:

* orphaned override;
* слишком длинных handles;
* перехода за пределами area;
* резкого изгиба;
* самопересечения preview, если оно обнаруживается простым способом.

Orphaned override не экспортируется.

---

# 15. Save/Open

Track Editor сохраняет Track Project:

```text
formatVersion = 2
```

и:

```json
"transitionOverrides": []
```

При Open:

1. загрузить project DTO;
2. выполнить migration v1 → v2 в памяти;
3. разрешить Exercise Definitions;
4. определить соседние пары;
5. применить подходящие overrides;
6. отметить остальные overrides orphaned;
7. построить preview.

Исходный v1-файл не перезаписывается до Save.

---

# 16. Export

При экспорте:

* automatic transition экспортируется с автоматически рассчитанными control points;
* overridden transition экспортируется с control points из override;
* оба становятся обычными `cubicBezier` в global trajectory;
* Viewer не различает их источник.

Exported Track formatVersion остаётся:

```text
3
```

---

# 17. Visual distinction

На canvas рекомендуется различать:

```text
Automatic transition
Manual transition
Selected transition
Invalid transition
```

Например:

* automatic — стандартный overlay;
* manual — дополнительный значок или другой оттенок;
* selected — яркое выделение;
* invalid — предупреждающий стиль.

Точные цвета не являются частью доменного контракта.

---

# 18. Не реализовывать

В Iteration 3 не добавлять:

* дополнительные anchors внутри перехода;
* несколько spline segments между упражнениями;
* split transition;
* convert transition to polyline;
* linked/mirrored handles;
* automatic smoothing after manual edit;
* obstacle avoidance;
* minimum turn radius;
* speed model;
* environment;
* undo/redo framework;
* Viewer changes без необходимости;
* Exercise Editor changes без необходимости.

---

# 19. Definition of Done

* Transition можно выбрать.
* Для выбранного transition видны handles.
* Handles перемещаются мышью.
* Первое изменение создаёт TransitionOverride.
* Повторное изменение обновляет override.
* Числовое редактирование работает.
* Reset удаляет override.
* Automatic transition восстанавливается.
* Override сохраняется в Track Project v2.
* Override восстанавливается после Open.
* Track Project v1 открывается через in-memory migration.
* Перемещение instance обновляет endpoints.
* Offset сохраняется при изменении instance transform.
* Reorder создаёт orphaned override при потере соседства.
* Orphaned override не экспортируется.
* Manual transition экспортируется в Viewer.
* Viewer отображает итоговую кривую.
* Exercise Editor не сломан.
* Track Editor Iteration 1–2 не сломан.
* Нет compile/runtime errors.

# Track Editor Iteration 4 — Productivity and Editing Safety

## 1. Цель

Iteration 4 улучшает повседневную работу с Track Editor после завершения основного workflow:

```text
Exercise Definition
        ↓
Track Project
        ↓
Automatic transitions
        ↓
Manual transition overrides
        ↓
Exported Track
        ↓
Viewer
```

Основные цели:

* безопасно отменять ошибочные изменения;
* быстро повторять одинаковые ExerciseInstance;
* точно изменять transform клавиатурой;
* временно защищать готовые instances от случайного изменения;
* быстро экспортировать и открыть текущую трассу во Viewer.

Iteration 4 не изменяет Track Project JSON contract и Exported Track JSON contract.

---

# 2. Undo and Redo

Track Editor должен поддерживать историю изменений текущего Track Project.

Команды:

```text
Undo
Redo
```

Рекомендуемые shortcuts:

```text
Ctrl+Z        Undo
Ctrl+Y        Redo
Ctrl+Shift+Z  Redo
```

Если операционная система или текущий input mapping требуют других сочетаний, поведение должно оставаться стандартным и понятным.

---

## 2.1. Операции, поддерживающие Undo/Redo

История должна включать:

* создание нового ExerciseInstance;
* удаление ExerciseInstance;
* Duplicate;
* изменение position;
* изменение rotation;
* изменение scale;
* reorder Route Order;
* изменение Track metadata;
* изменение area;
* создание TransitionOverride;
* изменение control handles TransitionOverride;
* числовое изменение TransitionOverride;
* Reset Transition to Automatic;
* удаление orphaned overrides;
* удаление instance, приводящее к orphaned overrides;
* изменение других persisted полей Track Project.

Не требуется добавлять в историю:

* selection;
* pan;
* zoom;
* active tool;
* expanded tree nodes;
* transition preview visibility;
* временную блокировку instance;
* clipboard;
* status messages;
* результаты validation;
* последний экспортный путь.

---

## 2.2. Единица Undo

Одна логическая пользовательская операция должна создавать одну запись истории.

Пример drag:

```text
mouse press
    ↓
begin edit transaction
    ↓
mouse move
mouse move
mouse move
    ↓
mouse release
    ↓
commit one undo entry
```

Не допускается создание отдельной Undo-записи для каждого события движения мыши.

То же правило применяется к:

* drag ExerciseInstance;
* drag transition control handle;
* длительному изменению числового поля;
* повторяющемуся изменению slider или spin box.

---

## 2.3. Cancelled operation

Если пользователь начал drag, но итоговое значение не изменилось, Undo-запись не создаётся.

Если операция отменена:

* клавишей Escape;
* потерей допустимого target;
* внутренней validation;
* другим штатным механизмом отмены;

документ должен вернуться к состоянию до начала операции.

---

## 2.4. Redo invalidation

После Undo, если пользователь выполняет новое persisted изменение:

```text
Redo history
```

очищается.

Это стандартная линейная модель истории.

Branching history не требуется.

---

## 2.5. История и Save

Undo/Redo history не сериализуется.

После Save:

* история может сохраняться в текущем сеансе;
* Track Project становится clean в сохранённой позиции истории.

Редактор должен различать:

```text
current history position
saved history position
```

Dirty state определяется не просто наличием Undo-записей, а отличием текущей позиции от последнего успешно сохранённого состояния.

Пример:

```text
Save
→ Move instance
→ Undo
```

После Undo документ снова должен стать:

```text
clean
```

если его состояние совпадает с последним Save.

---

## 2.6. История и Open/New

При успешном:

* Open;
* New Track;

история предыдущего документа очищается.

Нельзя Undo вернуться в ранее открытый Track Project.

---

## 2.7. History capacity

Допускается ограниченная история.

Рекомендуемый первоначальный предел:

```text
100 logical operations
```

Точное значение может быть изменено после проверки потребления памяти.

При превышении лимита удаляются самые старые записи.

---

# 3. Рекомендуемая стратегия истории

Для текущего размера Track Project допустим snapshot-based подход.

Концептуально:

```text
TrackEditorHistoryEntry
├─ Description
├─ BeforeSnapshot
└─ AfterSnapshot
```

Snapshot должен содержать persisted-состояние Track Project:

* metadata;
* area;
* instances;
* transitionOverrides.

Snapshot не должен содержать:

* resolved Exercise Definition cache;
* transformed preview geometry;
* selected instance;
* selected transition;
* viewport state;
* lock state;
* validation state.

Допустима command-based реализация, если она существенно лучше соответствует текущей архитектуре.

Не следует создавать сложный event-sourcing framework.

---

# 4. Editor state after Undo/Redo

После восстановления Track Project необходимо:

1. повторно разрешить или переиспользовать Exercise Definition;
2. пересобрать transformed preview;
3. пересчитать automatic transitions;
4. применить TransitionOverride;
5. обновить Route Order;
6. выполнить validation;
7. обновить canvas;
8. восстановить selection, если соответствующий объект ещё существует.

Selection не обязана быть частью snapshot.

Рекомендуемое поведение:

* попытаться сохранить selection по `instanceId` или `transitionId`;
* очистить selection, если объект больше не существует.

---

# 5. Duplicate ExerciseInstance

Добавить команду:

```text
Duplicate Instance
```

Рекомендуемый shortcut:

```text
Ctrl+D
```

Команда работает для выбранного ExerciseInstance.

---

## 5.1. Результат Duplicate

Новый instance получает:

* новый уникальный `instanceId`;
* тот же `exercisePath`;
* ту же position с небольшим offset;
* тот же `rotationDeg`;
* тот же `scale`;
* место непосредственно после исходного instance в Route Order.

Рекомендуемый positional offset:

```text
+1 meter по Track X
+1 meter по Track Y
```

Допускается использование текущего snap step.

Новый instance становится выбранным.

---

## 5.2. Identity

Duplicate не копирует `instanceId`.

Пример:

```text
source:
exercise-instance-007

duplicate:
exercise-instance-008
```

ID должен создаваться существующим централизованным генератором.

Нельзя определять новый ID только по текущему количеству instances, если это может привести к повторному использованию ранее удалённого ID.

---

## 5.3. TransitionOverride

При Duplicate не копируются TransitionOverride исходного instance.

Причины:

* новый instance имеет другую identity;
* у него другие соседние пары;
* control offsets исходных переходов могут быть неприменимы.

После вставки нового instance:

* соседние automatic transitions пересчитываются;
* существующие overrides повторно сопоставляются;
* потерявшие соседство overrides становятся orphaned согласно Iteration 3.

Duplicate вместе с последствиями для Route Order и overrides является одной Undo-операцией.

---

## 5.4. Unresolved instance

Разрешается Duplicate unresolved instance.

Копия сохраняет:

* `exercisePath`;
* transform;
* unresolved state после повторного разрешения.

Она получает новый `instanceId`.

---

# 6. Keyboard transforms

Track Editor должен поддерживать точное изменение transform выбранного ExerciseInstance с клавиатуры.

Keyboard transforms применяются только:

* когда фокус не находится в текстовом или числовом поле;
* когда выбран ExerciseInstance;
* когда instance не заблокирован.

---

## 6.1. Position nudging

Рекомендуемое управление:

```text
Arrow keys
    move by current snap step

Shift + Arrow
    move by 1 meter

Alt + Arrow
    move by fine step
```

Рекомендуемый fine step:

```text
0.05 meter
```

Если текущий snap step равен или меньше fine step, реализация должна оставаться предсказуемой.

Направления соответствуют доменной Track X/Y, а не произвольной ориентации экрана после transform.

---

## 6.2. Repeated key input

Удержание клавиши может повторять перемещение.

Последовательность автоповтора одной удерживаемой клавиши желательно объединять в одну Undo-операцию.

Минимально допустимый вариант:

* одно нажатие — одна Undo-запись;
* системный key repeat — несколько Undo-записей.

Однако предпочтительнее транзакция:

```text
first key press
    ↓
begin nudge transaction

key repeat
    ↓
update transform

key release
    ↓
commit one undo entry
```

---

## 6.3. Rotation shortcuts

Добавить поворот выбранного instance:

```text
Q  rotate -15 degrees
E  rotate +15 degrees
```

Дополнительно:

```text
Shift+Q  rotate -90 degrees
Shift+E  rotate +90 degrees
```

Если Q/E конфликтуют с текущими инструментами, выбрать другие свободные клавиши и явно отразить их в UI.

Rotation:

* изменяет persisted `rotationDeg`;
* пересчитывает geometry;
* пересчитывает transitions;
* применяет TransitionOverride к новым endpoints;
* создаёт Undo-запись.

---

## 6.4. Exact properties remain available

Keyboard transforms не заменяют Properties panel.

Пользователь по-прежнему может численно редактировать:

* Position X;
* Position Y;
* Rotation;
* Scale X;
* Scale Y.

---

# 7. Temporary instance locking

Добавить временную editor-only блокировку ExerciseInstance.

Назначение:

* защитить уже размещённый instance от случайного drag;
* защитить от keyboard transform;
* защитить от случайного изменения transform в Properties.

---

## 7.1. Persisted contract

Lock state не сохраняется в Track Project formatVersion 2.

Он является editor UI state.

После повторного открытия Track Project все instances могут считаться разблокированными.

Track Project version повышать не требуется.

---

## 7.2. Lock identity

Editor хранит lock state по:

```text
instanceId
```

Концептуально:

```text
LockedInstanceIds
```

Эта коллекция не является частью Track Project DTO.

---

## 7.3. UI

Для выбранного ExerciseInstance добавить:

```text
Lock Instance
Unlock Instance
```

Допустимые представления:

* checkbox в Properties;
* кнопка с иконкой замка;
* контекстное действие.

На canvas заблокированный instance должен иметь ненавязчивый editor overlay:

* значок замка;
* изменённый outline;
* иной понятный визуальный признак.

---

## 7.4. Поведение locked instance

Locked instance нельзя:

* перемещать drag-and-drop;
* двигать Arrow keys;
* поворачивать keyboard shortcuts;
* изменять Position/Rotation/Scale через Properties.

Locked instance можно:

* выбрать;
* просматривать;
* использовать в Route Order;
* экспортировать;
* включать в transitions;
* разблокировать;
* удалить после обычного подтверждения;
* дублировать, если это сознательно разрешено.

Рекомендуемое поведение Duplicate:

* разрешить Duplicate locked instance;
* новый instance создаётся разблокированным.

---

## 7.5. Lock и Undo

Lock/unlock не входит в Undo/Redo history, потому что не меняет Track Project.

Lock state не влияет на dirty state.

После Undo/Redo lock state сохраняется для instanceId, которые продолжают существовать.

Для удалённых instances устаревшие lock entries должны очищаться.

---

# 8. Export and Open in Viewer

Добавить команду:

```text
Export and Open in Viewer
```

Она использует существующий pipeline Iteration 2–3.

---

## 8.1. Последовательность

Команда:

1. компилирует Track Project;
2. выполняет export validation;
3. блокирует запуск при blocking errors;
4. отображает warnings;
5. экспортирует snapshot;
6. передаёт Viewer путь экспортированного файла;
7. запускает Viewer scene.

Viewer по-прежнему загружает только:

```text
Exported Track JSON
```

Он не должен получать:

* Track Project DTO;
* Exercise Definition;
* TransitionOverride;
* Track Editor runtime state.

---

## 8.2. Export path

Для быстрого теста допустимо использовать:

```text
res://exports/tracks/_preview/
```

Пример:

```text
res://exports/tracks/_preview/current-track-preview.json
```

Или существующий последний export path.

Рекомендуется отдельный preview-файл, чтобы:

* не перезаписывать пользовательский production export;
* не открывать дополнительный Save As dialog при каждом тесте;
* быстро повторять цикл редактирования.

Preview path является editor state и не сериализуется.

---

## 8.3. Dirty project

Track Project может быть dirty.

Команда использует текущее состояние в памяти.

Она не должна автоматически выполнять Save Track Project.

Допустимо показать ненавязчивое сообщение:

```text
Viewer preview uses the current unsaved editor state.
```

---

## 8.4. Viewer launch strategy

Способ запуска зависит от существующей архитектуры проекта.

Допустимы:

* переход на Viewer scene внутри текущего приложения;
* запуск отдельной сцены через SceneTree;
* передача startup path через singleton;
* запуск отдельного процесса Godot;
* debug launch configuration.

Предпочтительная стратегия должна:

* не создавать прямую зависимость Viewer от Track Editor DTO;
* позволять вернуться в Track Editor без потери несохранённого состояния;
* не завершать редакторский сеанс неожиданно.

Если сохранение текущего Editor runtime state при переключении сцены сложно, допустимо запускать отдельный процесс Viewer.

---

## 8.5. Failure handling

Если:

* validation содержит errors;
* export завершился ошибкой;
* Viewer не удалось запустить;
* export-файл невозможно прочитать;

Track Editor должен:

* не падать;
* показать понятное сообщение;
* сохранить текущий документ и историю без изменений;
* записать технические подробности в лог.

---

# 9. Optional copy/paste boundary

Полноценные:

```text
Ctrl+C
Ctrl+V
```

не являются обязательной частью Iteration 4.

Основной быстрый workflow обеспечивается командой Duplicate.

Не создавать clipboard architecture, если Duplicate решает текущую практическую задачу.

---

# 10. Custom routing exercises

Для нестандартного участка между основными упражнениями пользователь может создать отдельный Exercise Definition:

* без cones;
* без обязательных markings;
* с собственной trajectory;
* с близко расположенными entryPoint и exitPoint;
* с любой необходимой формой polyline/cubicBezier.

Track Editor обрабатывает такой объект как обычный ExerciseInstance.

Это позволяет создавать:

* дополнительные петли;
* объезды;
* сложные связующие участки;
* развороты за пределами стандартного упражнения;
* произвольные изменения направления.

Отдельная доменная сущность checkpoint или waypoint для этой задачи в текущем scope не требуется.

Такой Exercise Definition должен по-прежнему иметь:

* валидную trajectory;
* минимум один валидный trajectory segment;
* определимые entry/exit tangents;
* валидные entryPoint и exitPoint.

Пустой массив cones является допустимым.

---

# 11. Не реализовывать в Iteration 4

Не добавлять:

* multi-selection;
* group transform;
* group duplicate;
* copy/paste framework;
* persistent instance lock;
* lock layers;
* alignment tools;
* distribution tools;
* snapping instances друг к другу;
* environment objects;
* Venue Definition;
* checkpoints gameplay;
* timing;
* automatic route optimization;
* motorcycle physics;
* minimum turn radius validation;
* event-sourcing framework;
* branching Undo history;
* persistent Undo history;
* autosave recovery system.

---

# 12. Definition of Done

Iteration 4 завершена, если:

* Undo работает для persisted изменений Track Project;
* Redo работает;
* новое изменение после Undo очищает Redo;
* drag создаёт одну Undo-запись;
* transition handle drag создаёт одну Undo-запись;
* dirty state связан с saved history position;
* Undo к сохранённому состоянию возвращает clean state;
* New/Open очищают старую историю;
* Duplicate создаёт новый instanceId;
* Duplicate вставляет копию после source instance;
* Duplicate не копирует TransitionOverride;
* keyboard position nudging работает;
* rotation shortcuts работают;
* keyboard input не срабатывает внутри числовых и текстовых полей;
* instance можно временно заблокировать;
* locked instance нельзя случайно трансформировать;
* lock не сериализуется;
* lock не меняет dirty state;
* Export and Open in Viewer использует текущий in-memory Track Project;
* Viewer получает только Exported Track JSON;
* ошибка запуска Viewer не повреждает редактор;
* custom routing Exercise Definition без конусов корректно используется;
* Track Project formatVersion остаётся 2;
* Exported Track formatVersion остаётся 3;
* Exercise Editor продолжает работать;
* Track Editor Iteration 1–3 продолжает работать;
* Viewer продолжает работать;
* проект собирается без compile errors;
* runtime logs не содержат необработанных ошибок.


---

Venue Editor Iteration 1

# Track/Venue Integration Iteration 1

Track Editor работает только с Track Project formatVersion 3.

Новый Track создаётся через:

```text
Select Venue
    ↓
Track metadata
    ↓
Create Track
```

