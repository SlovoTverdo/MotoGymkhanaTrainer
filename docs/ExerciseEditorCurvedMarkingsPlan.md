## FILE: docs/ExerciseEditorCurvedMarkingsPlan.md

# Exercise Editor Curved Markings Plan

# Curved Markings Exercise Editor Iteration 2

## 1. Цель

Добавить в Exercise Editor полноценное интерактивное создание и редактирование marking Path, состоящих из:

* line segments;
* cubic Bézier segments.

После итерации пользователь должен иметь возможность:

* создать новую разметку;
* начать Path в выбранной точке;
* добавлять прямые сегменты;
* добавлять cubic Bézier segments;
* выбирать marking;
* выбирать отдельный segment;
* выбирать endpoint;
* выбирать Bézier control point;
* перемещать endpoints;
* перемещать control points;
* преобразовывать line в cubic Bézier;
* преобразовывать cubic Bézier в line;
* добавлять segment в конец Path;
* вставлять segment между существующими;
* удалять segment;
* перемещать marking целиком;
* изменять style, color и thickness;
* использовать Undo/Redo;
* сохранять Exercise Definition v3 без потери геометрии.

---

## 2. Scope

Итерация относится только к Exercise Editor.

Реализовать:

* Path creation tool;
* line segment tool;
* cubic Bézier segment tool;
* selection hierarchy;
* segment hit testing;
* endpoint handles;
* control-point handles;
* handle dragging;
* whole-marking translation;
* line-to-cubic conversion;
* cubic-to-line conversion;
* append segment;
* insert segment;
* delete segment;
* Undo/Redo;
* inspector properties;
* snapping integration;
* visual preview;
* validation feedback.

Не реализовывать:

* Venue Editor curved marking tools;
* text markings;
* arcs;
* quadratic Bézier;
* tangent continuity modes;
* automatic smoothing across segments;
* closed filled polygons;
* path boolean operations;
* freehand drawing;
* custom dash pattern editor;
* multi-marking group edit;
* reusable marking templates.

---

## 3. Selection hierarchy

Exercise Editor должен различать:

```text
Marking
Path segment
Endpoint
Control point
```

Selection state должен храниться централизованно.

Пример:

```text
selectedMarkingId
selectedSegmentIndex
selectedHandleKind
selectedHandleIndex
```

Не хранить selection только через визуальный Node reference.

Selection должна оставаться корректной после:

* Undo;
* Redo;
* save/reload;
* segment insertion;
* segment deletion;
* marking deletion.

---

## 4. Handle kinds

Поддерживаемые типы handles:

```text
PathStart
SegmentEnd
Control1
Control2
```

Для line segment доступны:

```text
SegmentEnd
```

Для cubic Bézier доступны:

```text
SegmentEnd
Control1
Control2
```

Начальная точка всего Path редактируется отдельным:

```text
PathStart
```

Начало внутреннего segment определяется концом предыдущего segment и не имеет отдельного дублирующего handle.

---

## 5. Visual representation

Выбранный marking отображает:

* основную линию Path;
* endpoints;
* control points;
* вспомогательные линии от segment start к control1;
* вспомогательные линии от control2 к segment end.

Рекомендуемое визуальное различие:

```text
Path start       квадрат
Segment endpoint круг
Control point    ромб или меньший круг
Control lines    тонкая полупрозрачная линия
```

Выбранный handle должен выделяться сильнее.

Не полагаться только на цвет: форма handles тоже должна отличаться.

---

## 6. Handle size

Handles должны сохранять приблизительно постоянный размер на экране независимо от zoom.

Размер hit area должен быть больше визуального размера.

Рекомендуемые значения:

```text
visual radius: 5–7 px
hit radius:    9–14 px
```

Фактические значения должны использовать существующую UI scale policy редактора.

---

## 7. Tool modes

Добавить либо расширить tool state:

```text
Select
CreateMarking
AppendLine
AppendCubicBezier
InsertLine
InsertCubicBezier
```

Допускается более компактный UI:

```text
Create Path
Add Line
Add Curve
```

с контекстным поведением.

Не создавать скрытые режимы, которые пользователь не может определить по UI.

Активный tool должен отображаться в toolbar/status bar.

---

## 8. Create marking

При создании marking:

1. пользователь выбирает Create Marking;
2. кликает начальную точку;
3. создаётся временный Path с `start`;
4. следующий клик создаёт первый line segment;
5. после первого segment marking становится валидным;
6. последующие clicks добавляют line segments;
7. Enter или double click завершает создание;
8. Escape отменяет текущую незавершённую операцию.

Не сохранять marking без segments в Exercise Definition.

Допустимо держать незавершённый Path только в transient editor state.

---

## 9. Create cubic Bézier

Для добавления cubic segment рекомендуемый workflow:

### Вариант первой версии

1. выбрать Add Curve;
2. кликнуть endpoint;
3. автоматически создать control points по направлению segment chord;
4. выбрать созданный segment;
5. пользователь перемещает control handles вручную.

Начальные control points:

```text
control1 = lerp(start, end, 1/3)
control2 = lerp(start, end, 2/3)
```

Такой segment изначально представляет прямую, но уже редактируется как cubic.

Этот workflow проще и предсказуемее, чем требовать четыре последовательных клика.

---

## 10. Convert line to cubic

Команда:

```text
Convert to Curve
```

Для line от `P0` до `P3` создать:

```text
P1 = lerp(P0, P3, 1/3)
P2 = lerp(P0, P3, 2/3)
```

Внешняя форма Path не меняется.

Операция является одной Undo/Redo-командой.

После преобразования segment остаётся выбранным.

---

## 11. Convert cubic to line

Команда:

```text
Convert to Line
```

Удаляет control points и сохраняет endpoint.

Новая линия соединяет:

```text
segment start → segment end
```

Операция может заметно изменить геометрию, поэтому должна быть явно инициирована пользователем.

Не выполнять автоматическое преобразование почти прямых cubic segments.

---

## 12. Append segment

При добавлении segment в конец Path:

* start нового segment равен текущему Path end;
* пользователь задаёт новый end;
* line или cubic создаётся согласно активному tool;
* selection переходит на новый segment;
* операция записывается в Undo history.

Escape до подтверждения отменяет transient preview.

---

## 13. Insert segment

Для вставки segment между segment `i` и `i + 1` необходимо сохранить непрерывность Path.

Рекомендуемый workflow:

1. пользователь выбирает segment;
2. выбирает Insert Point/Segment;
3. указывает новую точку;
4. исходный segment делится на два.

### Line split

Для line:

```text
P0 → P3
```

создать:

```text
P0 → Pnew
Pnew → P3
```

### Cubic split

Для cubic использовать алгоритм de Casteljau.

При параметре `t`:

```text
Q0 = lerp(P0, P1, t)
Q1 = lerp(P1, P2, t)
Q2 = lerp(P2, P3, t)

R0 = lerp(Q0, Q1, t)
R1 = lerp(Q1, Q2, t)

S = lerp(R0, R1, t)
```

Первый cubic:

```text
P0, Q0, R0, S
```

Второй cubic:

```text
S, R1, Q2, P3
```

Это сохраняет исходную геометрию кривой.

---

## 14. Finding split parameter

При клике по cubic segment нужно определить приблизительное `t`, ближайшее к позиции курсора.

Допустимый подход:

1. coarse sampling;
2. найти ближайший sampled interval;
3. уточнить `t` бинарным или локальным поиском.

Не использовать только индекс sampled point без refinement, если это даёт заметный скачок.

---

## 15. Delete segment

Удаление segment должно сохранять валидную непрерывную структуру Path.

### Удаление внутреннего segment

После удаления начало следующего segment автоматически становится концом предыдущего.

Поскольку start не хранится в segment, структура формально остаётся непрерывной.

Но геометрически следующий segment теперь начинается в другой точке.

Для cubic следующего segment его control points сохраняются в абсолютных локальных координатах.

### Удаление первого segment

Path.start остаётся прежним, следующий segment начинается от Path.start.

### Удаление последнего segment

Path end становится концом предыдущего segment.

### Удаление единственного segment

Удаляется весь marking либо операция блокируется с понятным предложением удалить marking.

Предпочтительно удалить marking после подтверждения либо как единый Undo command.

---

## 16. Move Path start

Перемещение `Path.start` изменяет только начало первого segment.

Для line это меняет line geometry.

Для cubic изменяется `P0`; control points остаются на своих координатах.

Не перемещать автоматически control1 вместе с Path start в первой версии.

---

## 17. Move segment endpoint

Endpoint segment `i` одновременно является start segment `i + 1`.

Поскольку точка хранится только как `end` segment `i`, достаточно изменить её один раз.

Следующий segment автоматически использует новую точку как начало.

Не создавать вторую копию координаты в следующем segment.

---

## 18. Move control point

Control point drag меняет только:

```text
control1
```

либо:

```text
control2
```

соответствующего cubic segment.

Не применять автоматическую tangent symmetry в Iteration 2.

Каждый control handle независим.

---

## 19. Whole marking translation

Перемещение marking целиком применяет один translation vector ко всем:

* Path.start;
* line ends;
* cubic control1;
* cubic control2;
* cubic ends.

Операция должна использовать PathTransformService либо эквивалентную domain-операцию.

Не изменять thickness.

---

## 20. Drag transaction

Любой drag:

```text
mouse down
→ transient updates
→ mouse up
```

создаёт одну Undo-команду.

Не добавлять history entry на каждый mouse motion event.

При Escape во время drag:

* вернуть исходные coordinates;
* не добавлять Undo entry.

---

## 21. Undo/Redo commands

Добавить команды минимум для:

```text
CreateMarking
DeleteMarking
MoveMarking
AddSegment
InsertSegment
DeleteSegment
MovePathStart
MoveSegmentEnd
MoveControlPoint
ConvertLineToCubic
ConvertCubicToLine
ChangeMarkingStyle
ChangeMarkingColor
ChangeMarkingThickness
ChangeVisibility
```

Команды должны хранить достаточное состояние для точного восстановления.

Для сложных операций допустимо сохранять immutable before/after snapshot конкретного marking.

Не сохранять snapshot всего Exercise document для каждого малого drag, если текущая architecture позволяет более узкую команду.

---

## 22. Hit testing

При клике порядок приоритета:

```text
selected handles
visible handles
segment path
marking bounds/body
empty canvas
```

Control handles имеют приоритет над underlying path.

При пересечении нескольких markings:

* предпочесть уже selected marking;
* затем ближайший geometry hit;
* затем использовать существующий cycling/context selection, если он есть.

---

## 23. Segment hit testing

Hit testing выполняется по экранному расстоянию до sampled Path.

Для line можно использовать аналитическое расстояние до segment.

Для cubic допускается использовать adaptive sampled polyline.

Hit tolerance задаётся в screen pixels, а не в локальных метрах.

Так выбор остаётся удобным при разном zoom.

---

## 24. Snapping

Перемещение endpoints и control points должно использовать существующий snapping framework.

Поддержать, если уже существуют:

* grid snap;
* object point snap;
* modifier для временного отключения snap.

Рекомендуемая семантика:

```text
Endpoints:
grid/object snapping

Control points:
grid snapping optional
object snapping не обязательно
```

Не заставлять control points всегда прилипать к cones или другим geometry nodes.

---

## 25. Inspector

При выборе marking inspector показывает:

* ID;
* style;
* color;
* thickness;
* visibleInViewer;
* segment count;
* approximate length.

При выборе segment дополнительно:

* segment index;
* segment type;
* Convert to Line/Curve;
* Delete;
* Insert/Split.

При выборе control point допускается показывать coordinates.

---

## 26. ID

Новый marking получает уникальный ID через существующую ID policy.

Не использовать display name как уникальный identifier.

При Duplicate marking создавать новый ID.

---

## 27. Preview during creation

Во время добавления segment показывать transient preview:

* line от текущего Path end до cursor;
* cubic preview с автоматически рассчитанными controls;
* стиль и thickness marking;
* snapping marker.

Preview не должен попадать в serialization или Undo history до подтверждения.

---

## 28. Cancel behavior

Escape:

* отменяет текущий transient segment;
* отменяет active drag;
* выходит из Create/Add tool на уровень Select;
* не удаляет уже подтверждённые segments.

Правый клик может использоваться как завершение Path, если это соответствует существующему UX.

---

## 29. Keyboard commands

Рекомендуемые shortcuts:

```text
V           Select
L           Add Line
B           Add Cubic Bézier
Delete      Delete selected segment/marking
Ctrl+Z      Undo
Ctrl+Y      Redo
Esc         Cancel current operation
Enter       Finish current Path creation
```

Не вводить shortcut, конфликтующий с существующими tools.

Фактические shortcuts отразить в help/status UI.

---

## 30. Marking validation during edit

Редактор должен предотвращать либо подсвечивать:

* Path без segment;
* non-finite coordinates;
* segment с практически нулевой длиной;
* duplicate marking ID;
* thickness <= 0.

Самопересечения и петли разрешены.

Невалидный marking нельзя сохранять без понятного сообщения.

---

## 31. Zero-length segments

Если пользователь создаёт endpoint практически в start:

* не создавать segment;
* оставить tool активным;
* показать краткое сообщение.

Tolerance должна совпадать с domain validation.

---

## 32. Bounds refresh

После любого изменения Path пересчитать:

* marking bounds;
* Exercise content bounds, если это часть существующей policy;
* selection overlay;
* viewport redraw.

Не выполнять полную дорогостоящую перестройку всех Exercise assets, если изменился один marking.

---

## 33. Rendering consistency

Editor preview должен использовать тот же PathSampler и style pipeline, что и desktop preview/rendering, насколько позволяет архитектура.

Не создавать отдельную упрощённую геометрию, которая заметно расходится с Viewer.

Control lines и handles являются editor overlays и не входят в Viewer rendering.

---

## 34. Dashed/dotted editing

При редактировании dashed/dotted marking:

* editor отображает фактический pattern;
* hit testing может выполняться по полной centerline, включая gaps;
* пользователь должен иметь возможность выбрать marking, кликнув рядом с gap.

Не ограничивать hit testing только видимыми dash fragments.

---

## 35. Selection after structural changes

После Add:

* выбирается новый segment.

После Split:

* выбирается новый split point либо второй segment.

После Delete:

* выбирается ближайший оставшийся segment;
* если marking удалён, selection очищается.

После Convert:

* тот же segment остаётся выбранным.

После Undo/Redo:

* selection восстанавливается, если selected object всё ещё существует;
* иначе выбирается marking либо selection очищается.

---

## 36. Save/reload

После сохранения и повторного открытия должны совпадать:

* Path.start;
* segment order;
* segment types;
* controls;
* endpoints;
* style;
* color;
* thickness;
* visibility.

Не допускается автоматическая пересортировка segments.

---

## 37. Performance

Во время control-point drag:

* обновлять только выбранный marking;
* использовать существующий sampler;
* не пересобирать весь Exercise library;
* не запускать Track export;
* не выполнять physics projection.

Exercise Editor работает в локальной 2D-плоскости Exercise.

---

## 38. Не реализовывать

В Iteration 2 не добавлять:

* smooth/symmetric tangent mode;
* linked adjacent handles;
* handles at every sampled point;
* closed path;
* fill;
* text;
* arrowheads;
* Venue Editor segment editing;
* multi-selection of handles;
* copy/paste individual segments;
* segment reordering by drag;
* path reverse;
* auto-fit footprint;
* freehand conversion to Bézier.

---

## 39. Definition of Done

Итерация завершена, если:

* новый marking можно создать;
* line segments добавляются;
* cubic segments добавляются;
* Path creation завершается;
* transient creation отменяется;
* marking выбирается;
* segment выбирается;
* Path start выбирается;
* segment endpoint выбирается;
* control1 выбирается;
* control2 выбирается;
* handles различимы;
* line hit testing работает;
* cubic hit testing работает;
* Path start перемещается;
* endpoint перемещается;
* control points перемещаются;
* marking целиком перемещается;
* line конвертируется в cubic без изменения формы;
* cubic конвертируется в line;
* line split работает;
* cubic split через de Casteljau сохраняет форму;
* segment удаляется;
* единственный segment корректно обрабатывается;
* style изменяется;
* color изменяется;
* thickness изменяется;
* visibility изменяется;
* snapping работает;
* drag создаёт один Undo step;
* Undo/Redo работают для structural edits;
* Undo/Redo работают для handle drag;
* save/reload сохраняет geometry;
* invalid Path не сохраняется;
* existing v3 documents открываются;
* Track/export formats не меняются;
* desktop build проходит;
* tests проходят.
