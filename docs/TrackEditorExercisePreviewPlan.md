# Track Editor Exercise Preview Plan

## Track Editor Exercise Preview Iteration

### 1. Цель

Добавить в Track Editor read-only окно предпросмотра упражнения, выбранного в библиотеке.

Preview должен позволять до размещения упражнения на трассе быстро понять:

* геометрию упражнения;
* расположение конусов;
* markings;
* внутреннюю trajectory;
* entry/exit;
* footprint;
* routing-only свойства;
* направление прохождения.

Preview не является отдельным редактором Exercise.

---

### 2. Scope

Реализовать:

* preview panel в Track Editor;
* обновление preview при выборе Exercise в library;
* отображение Exercise footprint;
* cones;
* Path-based markings;
* line segments;
* cubic Bézier segments;
* solid/dashed/dotted styles;
* internal trajectory;
* entry point;
* exit point;
* basic exercise metadata;
* auto-fit камеры/viewport;
* empty state;
* invalid exercise state;
* refresh после изменения library;
* корректную работу после reload Exercise definitions.

Не реализовывать:

* редактирование Exercise из preview;
* Path handles;
* dragging;
* placement preview на Venue;
* surface projection;
* 3D rendering;
* physics;
* Track instance transform;
* Venue objects;
* text markings;
* GLB preview;
* изменение JSON formats.

---

### 3. Основной принцип

Preview должен использовать существующий Exercise domain contract и shared rendering helpers.

Не создавать третий независимый renderer Exercise geometry.

Предпочтительно переиспользовать:

* PathSampler;
* Path marking renderer;
* trajectory sampler/renderer;
* cone rendering helper;
* coordinate transform helpers;
* Exercise bounds calculation;
* style parser;
* color parser.

Editor-only overlays Exercise Editor не переиспользовать:

* selection handles;
* control lines;
* snapping guides;
* transient Path creation;
* drag state.

---

### 4. UI placement

Preview размещается внутри Track Editor рядом с Exercise Library.

Предпочтительные варианты:

* правая нижняя часть library panel;
* collapsible panel под списком упражнений;
* отдельный panel справа, если текущий layout это позволяет.

Preview не должен существенно уменьшать центральную рабочую область Track canvas.

Panel должен иметь minimum size и возможность адаптироваться к resize окна.

---

### 5. Preview state

Preview имеет состояния:

* NoSelection;
* Loading;
* Ready;
* InvalidExercise;
* MissingExercise.

NoSelection:

показать нейтральный текст:

`Выберите упражнение для предпросмотра`

InvalidExercise:

показать:

* exercise name/id;
* краткое validation сообщение;
* не падать Track Editor.

---

### 6. Source Exercise

Preview отображает ExerciseDefinition из текущей Exercise Library.

Не использовать уже размещённый ExerciseInstance.

Preview показывает исходную локальную геометрию Exercise:

* без Track rotation;
* без Track translation;
* без instance scaleX/scaleY.

Таким образом пользователь видит каноническую форму упражнения.

---

### 7. Coordinate system

Использовать локальные Exercise coordinates.

Preview renderer преобразует их только в screen-space.

Не изменять domain geometry.

Не создавать временный Track instance только ради preview.

---

### 8. Auto-fit

При выборе Exercise вычислить content bounds.

Bounds должны учитывать:

* footprint;
* cones;
* markings;
* cubic Bézier extrema;
* trajectory;
* entry/exit;
* helper geometry, если она относится к отображаемому Exercise contract.

После этого preview viewport выполняет fit с margin.

Рекомендуемый margin:

10–15% от большей стороны bounds.

Если Exercise пуст или bounds почти нулевые:

использовать безопасный default view.

---

### 9. Aspect ratio

Preview должен сохранять aspect ratio geometry.

Не растягивать Exercise независимо по X/Y под размер panel.

Использовать uniform screen scale.

---

### 10. Footprint

Footprint отображается read-only.

Предпочтительный стиль:

* тонкий контур;
* полупрозрачная заливка либо без заливки;
* менее яркий, чем cones и markings.

Footprint не должен визуально доминировать.

---

### 11. Cones

Cones отображаются с тем же color/type semantics, что в Exercise Editor.

Если в Exercise есть разные cone types/colors, сохранить их различимость.

На малом preview допускается минимальный screen-space размер cone marker, чтобы они не исчезали.

Не менять domain cone size.

---

### 12. Markings

Preview должен поддерживать текущий Exercise Definition v3 Path contract.

Поддержать:

* line;
* cubicBezier;
* mixed Path;
* solid;
* dashed;
* dotted;
* color;
* thickness;
* visibleInViewer semantics.

Поскольку это editor preview, marking с `visibleInViewer=false` рекомендуется всё равно показывать, но с тем же editor-only indication, что в Exercise Editor.

Не скрывать authoring geometry только потому, что она скрыта в runtime Viewer.

---

### 13. Trajectory

Internal Exercise trajectory должна быть хорошо различима.

Отображать:

* прямые segments;
* cubic Bézier segments, если trajectory contract их поддерживает;
* direction indicators, если уже существует shared renderer.

Trajectory должна визуально отличаться от markings.

Не использовать Follow-mode gradient.

---

### 14. Entry / Exit

Отображать entry и exit отдельными маркерами.

Они должны быть различимы даже на маленьком preview.

Рекомендуется:

* Entry: отдельный символ/подпись `IN`;
* Exit: отдельный символ/подпись `OUT`.

Если в текущем Exercise Editor уже есть стандартные glyphs, использовать их.

---

### 15. Direction

Preview должен помогать понять направление упражнения.

Минимально:

* trajectory direction arrows;
* либо entry/exit markers.

Не добавлять новый domain field ради направления.

---

### 16. Metadata

Рядом с preview либо в его header показывать:

* display name;
* ID;
* footprint width × length;
* routing-only flag;
* количество cones;
* количество markings.

Не перегружать panel дополнительной статистикой.

Approximate trajectory length допустима как необязательный бонус.

---

### 17. Routing-only Exercise

Если Exercise является routing-only:

preview всё равно должен отображать его geometry.

В header показать небольшой признак:

`Routing only`

Не скрывать preview.

---

### 18. Selection behavior

При выборе другого Exercise:

* preview обновляется сразу;
* старый renderer state очищается;
* zoom/fit пересчитывается.

При снятии selection:

* очистить geometry;
* показать NoSelection state.

---

### 19. Library reload

При reload Exercise Library:

* если selected Exercise всё ещё существует, обновить preview;
* если Exercise удалён, очистить selection;
* если Exercise изменён на диске, отображать новую версию;
* не держать stale DTO reference.

---

### 20. Invalid Exercise

Если selected Exercise не проходит validation:

* Track Editor не падает;
* placement action для него использует существующую validation policy;
* preview показывает понятную ошибку.

Если часть geometry можно безопасно показать, это допустимо, но validation state должен быть виден.

---

### 21. Input

Preview в первой версии read-only.

Не обрабатывать:

* mouse drag geometry;
* handle interactions;
* snapping;
* object selection.

Допускается:

* mouse wheel zoom;
* middle/right mouse pan;
* Fit button.

Но это не обязательная часть Iteration.

Минимальный вариант — полностью автоматический fit.

---

### 22. Resize

При resize Track Editor:

* preview panel адаптируется;
* geometry не искажается;
* fit пересчитывается при существенном изменении viewport size.

Не создавать layout loop из-за постоянного fit/update.

---

### 23. Rendering architecture

Предпочтительная структура:

Track Editor
→ ExercisePreviewPanel
→ ExercisePreviewRenderer
→ shared geometry helpers

ExercisePreviewRenderer получает immutable/current ExerciseDefinition и создаёт read-only preview data.

Не связывать renderer напрямую с Exercise Library persistence.

---

### 24. Reuse Exercise Editor

Перед реализацией необходимо проверить, можно ли переиспользовать read-only часть Exercise Editor rendering.

Хороший вариант:

общий `ExerciseGeometryRenderer` или эквивалент, которому можно передать rendering options:

* show footprint;
* show cones;
* show markings;
* show trajectory;
* show entry/exit;
* show editor handles = false.

Если существующий Exercise Editor renderer tightly coupled к editing state, вынести только чистую rendering часть.

Не переносить весь Exercise Editor в Track Editor.

---

### 25. Performance

Preview обновляется только когда:

* меняется selected Exercise;
* Exercise Library reload;
* panel resize требует redraw;
* underlying Exercise меняется.

Не пересобирать preview каждый frame.

Не выполнять physics queries.

---

### 26. Undo/Redo

Preview сам не создаёт Undo/Redo operations.

Выбор Exercise в library не является document mutation.

Если Track Editor Undo/Redo изменяет library selection косвенно, preview просто обновляется согласно текущей selection.

---

### 27. Formats

Не изменять:

* Exercise v3;
* Venue v2;
* Track Project v3;
* Exported Track v5.

Preview не сериализуется.

Не добавлять preview settings в Track Project.

---

### 28. Regression

Не ломать:

* Exercise Library selection;
* Add/Place Exercise;
* Track instance transform;
* route order;
* transition editing;
* Duplicate;
* Undo/Redo;
* Venue preview;
* Track save/open;
* Track export;
* desktop Viewer;
* Web Viewer.

---

### 29. Не реализовывать

Не добавлять:

* editing from preview;
* drag-to-place from preview;
* animation;
* Venue background;
* 3D preview;
* runtime surface projection;
* text markings;
* GLB assets;
* alternate Exercise variants;
* thumbnail caching to disk;
* image export;
* JSON changes.

---

### 30. Definition of Done

Итерация завершена, если:

* preview panel существует;
* NoSelection state работает;
* Exercise selection обновляет preview;
* Exercise switch очищает старую geometry;
* footprint отображается;
* cones отображаются;
* line markings отображаются;
* cubic markings отображаются;
* dashed/dotted отображаются;
* trajectory отображается;
* entry отображается;
* exit отображается;
* routing-only indicator работает;
* metadata отображается;
* auto-fit работает;
* aspect ratio сохраняется;
* tiny Exercise не ломает fit;
* large Exercise помещается;
* invalid Exercise не ломает Track Editor;
* library reload обновляет preview;
* deleted selected Exercise очищает preview;
* preview read-only;
* preview не создаёт history commands;
* Track placement продолжает работать;
* Track Undo/Redo продолжает работать;
* Track export не меняется;
* format versions не меняются;
* desktop build проходит;
* tests проходят.
