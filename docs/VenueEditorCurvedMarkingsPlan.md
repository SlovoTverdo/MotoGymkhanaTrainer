# Venue Editor Curved Markings Plan

# Curved Markings Venue Editor Iteration 3

## 1. Цель

Добавить в Venue Editor полноценное интерактивное редактирование Path-based markings с:

- line segments;
- cubic Bézier segments;
- segment selection;
- endpoints;
- control points;
- split;
- conversion;
- deletion;
- snapping;
- Undo/Redo.

По поведению инструментарий должен быть максимально согласован с уже реализованным Exercise Editor Curved Markings Iteration 2.

Форматы данных не меняются:

- Exercise Definition v3;
- Venue Definition v2;
- Track Project v3;
- Exported Track v5.

---

## 2. Основной принцип

Не реализовывать второй независимый curved-marking editor.

Перед началом необходимо проанализировать реализацию Exercise Editor Iteration 2 и определить, какие части можно вынести или уже вынесены в reusable editor infrastructure.

Переиспользовать, где возможно:

- selection model;
- handle kinds;
- handle rendering;
- hit testing;
- drag transaction logic;
- Path mutations;
- line/cubic conversion;
- de Casteljau split;
- snapping;
- Undo/Redo commands or command helpers;
- inspector bindings;
- Path preview rendering.

Venue Editor должен добавлять только Venue-specific integration.

---

## 3. Scope

Реализовать:

- создание Venue marking;
- line segments;
- cubic Bézier segments;
- marking selection;
- segment selection;
- PathStart handle;
- SegmentEnd handle;
- Control1;
- Control2;
- handle dragging;
- whole-marking translation;
- line → cubic;
- cubic → line;
- line split;
- cubic split;
- append segment;
- delete segment;
- style editing;
- color editing;
- thickness editing;
- visibleInViewer;
- snapping;
- Undo/Redo;
- save/reload Venue v2.

Не реализовывать:

- text markings;
- filled regions;
- tangent modes;
- automatic smoothing;
- quadratic Bézier;
- arcs;
- circles as dedicated primitives;
- group editing;
- cross-marking joins;
- GLB import;
- Track editing from Venue Editor.

---

## 4. Shared infrastructure

Exercise Editor и Venue Editor должны использовать общую editor infrastructure там, где domain semantics одинаковы.

Предпочтительные reusable responsibilities:

- PathSelection;
- PathHandleKind;
- PathHitTester;
- PathEditOperations;
- PathDragSession;
- PathSplitService;
- PathConversionService.

Не выносить UI-specific code в абстракции ради абстракции.

Если Exercise Editor implementation tightly coupled to its scene, допускается минимальный refactor, но только в объёме, необходимом для безопасного reuse.

После refactor Exercise Editor behavior не должно измениться.

---

## 5. Selection hierarchy

Venue Editor различает:

- Venue object;
- Cone;
- Marking;
- Path segment;
- Path handle.

Marking selection hierarchy:

- marking ID;
- segment index;
- handle kind.

Handle kinds:

- PathStart;
- SegmentEnd;
- Control1;
- Control2.

Selection не должна зависеть только от временного Godot Node reference.

---

## 6. Tool modes

Venue Editor toolbar должен поддерживать минимум:

- Select;
- Add Object;
- Add Cone;
- Add Marking;
- Add Line;
- Add Cubic Bézier.

Если текущая Exercise Editor UX использует другой набор команд, Venue Editor должен по возможности использовать тот же пользовательский паттерн.

Активный инструмент должен быть явно виден.

---

## 7. Create marking

Workflow аналогичен Exercise Editor:

1. выбрать Add Marking;
2. кликнуть Path start;
3. создать transient Path;
4. следующий клик создаёт line segment;
5. последующие clicks добавляют segments;
6. Enter/double click завершает;
7. Escape отменяет незавершённую операцию.

Venue Definition не должна содержать marking без segments.

---

## 8. Cubic creation

При добавлении cubic segment:

- пользователь задаёт endpoint;
- control1 = 1/3 chord;
- control2 = 2/3 chord;
- segment изначально совпадает с прямой;
- segment становится selected;
- control handles становятся доступны.

Не использовать четырёхкликовый workflow.

---

## 9. Handles

Использовать те же визуальные обозначения и screen-space sizes, что в Exercise Editor.

Handles:

- PathStart;
- SegmentEnd;
- Control1;
- Control2.

Control lines являются editor-only overlay.

Их нельзя сериализовать в Venue Definition.

---

## 10. Hit testing

Приоритет:

1. selected handles;
2. visible handles;
3. Path centerline;
4. marking;
5. Venue object/cone согласно существующей selection policy;
6. empty canvas.

Для curved geometry использовать shared PathSampler.

Dashed/dotted marking должен быть selectable и в gap.

Hit tolerance задаётся в screen pixels.

---

## 11. Dragging

Drag поддерживается для:

- PathStart;
- SegmentEnd;
- Control1;
- Control2;
- whole marking.

Один mouse drag = одна Undo operation.

При Escape:

- восстановить исходную geometry;
- отменить drag transaction;
- не добавлять history entry.

---

## 12. Endpoint semantics

Segment start не хранится отдельно.

Если изменяется:

`segment[i].end`

то следующий segment автоматически начинается в новой точке.

Не создавать duplicated start coordinate.

---

## 13. Convert line to cubic

Использовать ту же domain operation, что в Exercise Editor.

Для chord:

P0 → P3

создать:

- P1 = lerp(P0, P3, 1/3)
- P2 = lerp(P0, P3, 2/3)

Форма остаётся прямой.

---

## 14. Convert cubic to line

Удалить control points и сохранить segment end.

Операция должна быть явной.

Undo должен точно восстанавливать control points.

---

## 15. Split

### Line

Разделить:

P0 → P3

на:

P0 → Pnew
Pnew → P3

### Cubic

Использовать shared de Casteljau split service.

Split должен сохранять форму исходной cubic.

Не реализовывать отдельную математику split в Venue Editor.

---

## 16. Delete segment

Поддержать:

- first;
- internal;
- last;
- only segment.

Если удаляется единственный segment:

- удалить marking;
- либо использовать уже принятую Exercise Editor UX policy.

Exercise и Venue editors должны вести себя одинаково.

---

## 17. Snapping

Endpoints используют существующий Venue Editor snapping:

- grid;
- object points, если существует;
- modifier to disable snap.

Control points:

- grid snap допустим;
- object snapping не обязателен.

Не привязывать Bézier controls к Venue objects автоматически.

---

## 18. Venue area

Изменение Venue area:

- не масштабирует markings;
- не перемещает markings;
- не изменяет control points.

Marking за bounds Venue сохраняется и получает existing warning.

Curved bounds должны использовать PathBoundsCalculator.

---

## 19. Permanent Venue geometry

Markings принадлежат Venue Definition напрямую.

Они не являются:

- Exercise instance geometry;
- Track geometry;
- editor overlays.

При Track export Venue markings входят в Exported Track v5 согласно существующему Venue integration pipeline.

Venue Editor не должен выполнять Track export при каждом drag.

---

## 20. Properties panel

При выборе marking показывать:

- ID;
- style;
- color;
- thickness;
- visibleInViewer;
- segment count;
- approximate length.

При выборе segment:

- index;
- type;
- Convert;
- Split;
- Delete.

Control coordinates могут отображаться для выбранного handle.

Все изменения properties должны поддерживать Undo/Redo.

---

## 21. visibleInViewer

Если:

`visibleInViewer = false`

marking остаётся видимым в Venue Editor.

Использовать существующий editor-only visual indication.

Curved marking должен вести себя так же, как straight marking.

---

## 22. Undo/Redo

Поддержать минимум:

- CreateMarking;
- DeleteMarking;
- MoveMarking;
- AddSegment;
- SplitSegment;
- DeleteSegment;
- MovePathStart;
- MoveSegmentEnd;
- MoveControl1;
- MoveControl2;
- ConvertLineToCubic;
- ConvertCubicToLine;
- ChangeStyle;
- ChangeColor;
- ChangeThickness;
- ChangeVisibility.

Переиспользовать shared commands или immutable marking snapshots, если это уже сделано в Exercise Editor.

Dirty state остаётся связан с history revision.

---

## 23. Object interaction

Curved-marking editing не должно ломать:

- object selection;
- object drag;
- object rotation;
- object scale;
- cone tools;
- area editing;
- panorama settings.

При active Path tool clicks не должны случайно выбирать Venue object под курсором.

При Select tool selection priority должна быть явно определена.

---

## 24. Rendering

Venue Editor preview использует тот же PathSampler и marking rendering pipeline, что Exercise Editor, насколько возможно.

Не создавать отдельный simplified cubic preview.

Rendering должен поддерживать:

- solid;
- dashed;
- dotted;
- width/thickness;
- color;
- hidden-in-viewer indication.

---

## 25. Save/reload

После сохранения и повторного открытия Venue v2 должны полностью совпадать:

- Path.start;
- segment order;
- segment types;
- control1;
- control2;
- endpoints;
- style;
- color;
- thickness;
- visibleInViewer.

Editor transient state не сериализуется.

---

## 26. Validation

Save блокируется при:

- empty Path;
- zero-length invalid segment;
- non-finite coordinates;
- duplicate marking ID;
- thickness <= 0.

Разрешены:

- loops;
- self intersections;
- control points outside Venue area;
- Path outside Venue area.

Последние могут создавать warning, но не blocking error.

---

## 27. Performance

Во время drag пересобирать только affected marking.

Не пересобирать:

- все Venue objects;
- panorama;
- Track;
- Web export.

Не запускать surface projection: Venue Editor редактирует 2D domain geometry.

---

## 28. Regression

Проверить, что не сломаны:

- Add Object;
- object preview;
- object transform;
- Duplicate;
- object locking;
- Add Cone;
- cone editing;
- existing straight markings;
- area changes;
- panorama;
- Save/Open;
- Undo/Redo;
- dirty state;
- Track Editor Venue preview;
- desktop Viewer;
- Web Viewer.

---

## 29. Не реализовывать

Не добавлять:

- text;
- GLB import;
- footprint recalculation changes;
- path tangent modes;
- linked handles;
- automatic smoothing;
- closed fill paths;
- SVG import;
- custom dash editor;
- multi-handle selection;
- Track Editor geometry editing;
- JSON format changes.

---

## 30. Definition of Done

Итерация завершена, если:

- Venue marking можно создать;
- line можно создать;
- cubic можно создать;
- marking selectable;
- segment selectable;
- PathStart selectable;
- endpoint selectable;
- control1/control2 selectable;
- handles draggable;
- whole marking draggable;
- line → cubic работает;
- cubic → line работает;
- line split работает;
- cubic split сохраняет форму;
- segment delete работает;
- only-segment case работает;
- style работает;
- color работает;
- thickness работает;
- visibleInViewer работает;
- snapping работает;
- handles имеют стабильный screen size;
- dashed/dotted hit testing работает через gaps;
- один drag создаёт один Undo step;
- Undo/Redo structural edits работают;
- Save/Reload сохраняет exact Path geometry;
- Venue area не масштабирует Path;
- Path outside Venue area сохраняется с warning;
- object/cone editing не сломаны;
- Track export versions не меняются;
- desktop build проходит;
- tests проходят.
