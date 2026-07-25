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
width  = 60 m
length = 100 m
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

Свободный rotation handle можно добавить позднее.

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
Scale X > 0
Scale Y > 0
```

Предлагаемый диапазон UI:

```text
0.1 ... 10
```

Не вводить отрицательный scale.

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
