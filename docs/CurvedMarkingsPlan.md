# Curved Markings Implementation Plan

# Curved Markings Domain and Rendering Iteration 1

## 1. Цель

Расширить разметку упражнений и площадок поддержкой непрерывных путей, состоящих из:

* прямых сегментов;
* кубических кривых Bézier.

После итерации криволинейная разметка должна:

* храниться в JSON;
* загружаться в desktop C# application;
* загружаться в GDScript Web Viewer;
* трансформироваться вместе с Exercise instance;
* корректно обрабатывать независимые `scaleX` и `scaleY`;
* экспортироваться в итоговый Track;
* отображаться стилями `solid`, `dashed` и `dotted`;
* проецироваться на поверхность площадки и эстакады;
* учитываться при расчёте bounds.

Полноценное интерактивное создание и редактирование Bézier-сегментов будет добавлено отдельной итерацией.

---

## 2. Scope

В Iteration 1 реализовать:

* общий контракт геометрического пути;
* line segment;
* cubic Bézier segment;
* validation;
* sampling;
* cumulative length;
* bounds;
* transforms;
* Exercise Definition v3;
* Venue Definition v2;
* Exported Track v5;
* преобразование существующей полилинейной разметки;
* desktop Viewer rendering;
* Web Viewer rendering;
* Track Editor read-only preview;
* Exercise Editor read-only rendering существующих curved markings;
* Venue Editor read-only rendering существующих curved markings;
* обновление fixtures и текущих JSON.

Не реализовывать:

* создание Bézier-сегмента мышью;
* перетаскивание control points;
* преобразование line в Bézier через UI;
* segment selection UI;
* разделение сегмента;
* объединение сегментов;
* автоматическое сглаживание;
* текстовую разметку;
* стрелки как часть marking;
* closed fill areas;
* миграцию произвольных старых пользовательских файлов во время runtime.

---

## 3. Версии форматов

После изменения:

```text
Exercise Definition formatVersion 3
Venue Definition formatVersion 2
Track Project formatVersion 3
Exported Track formatVersion 5
```

Track Project остаётся v3, если фактическая схема хранит только ссылки на Exercise и Venue и не содержит копий marking geometry.

Если аудит обнаружит, что Track Project сериализует markings либо snapshot Exercise geometry, версию Track Project необходимо повысить и документировать причину.

---

## 4. Общий Path contract

Разметка хранит непрерывный путь:

```json
{
  "start": {
    "x": 0.0,
    "y": 0.0
  },
  "segments": [
    {
      "type": "line",
      "end": {
        "x": 3.0,
        "y": 1.0
      }
    },
    {
      "type": "cubicBezier",
      "control1": {
        "x": 4.0,
        "y": 1.0
      },
      "control2": {
        "x": 5.0,
        "y": 3.0
      },
      "end": {
        "x": 7.0,
        "y": 3.0
      }
    }
  ]
}
```

Начало каждого следующего сегмента определяется концом предыдущего.

Не хранить отдельное `start` в каждом сегменте.

Преимущества:

* непрерывность задана структурой;
* отсутствует дублирование координат;
* меньше риск расхождения соседних сегментов;
* формат совпадает с логикой последовательного sampling.

---

## 5. Типы сегментов

Поддерживаемые значения:

```text
line
cubicBezier
```

### Line segment

```json
{
  "type": "line",
  "end": {
    "x": 3.0,
    "y": 1.0
  }
}
```

### Cubic Bézier segment

```json
{
  "type": "cubicBezier",
  "control1": {
    "x": 4.0,
    "y": 1.0
  },
  "control2": {
    "x": 5.0,
    "y": 3.0
  },
  "end": {
    "x": 7.0,
    "y": 3.0
  }
}
```

Значения `type` являются частью внешнего JSON-контракта и должны использоваться одинаково в C# и GDScript.

---

## 6. Marking contract

Marking должен содержать минимум:

```json
{
  "id": "marking-01",
  "style": "solid",
  "color": "#FFFFFFFF",
  "thickness": 0.08,
  "visibleInViewer": true,
  "path": {
    "start": {
      "x": 0.0,
      "y": 0.0
    },
    "segments": [
      {
        "type": "line",
        "end": {
          "x": 2.0,
          "y": 0.0
        }
      }
    ]
  }
}
```

Сохранить другие существующие свойства Marking, если они присутствуют в текущей схеме.

Не удалять существующую семантику без явной необходимости.

---

## 7. Стили

Поддержать существующие стили:

```text
solid
dashed
dotted
```

Если текущий проект использует другие строковые или enum-значения, сохранить фактический контракт и обновить документацию.

Стиль является визуальной характеристикой marking и не меняет геометрию Path.

---

## 8. Толщина

`thickness` задаётся в мировых метрах.

При размещении Exercise instance с независимыми:

```text
scaleX
scaleY
```

геометрия пути масштабируется, но `thickness` по умолчанию не масштабируется.

Причина:

* разметка должна сохранять визуально и физически понятную ширину;
* при сильном неравномерном scale ширина не должна становиться эллиптически или непредсказуемо искажённой;
* толщина не является частью локальной координатной геометрии пути.

Если текущая семантика проекта уже масштабирует thickness, аудит должен явно это зафиксировать до изменений.

---

## 9. Цвет

Цвет хранится в существующем проектном формате.

Предпочтительный вид:

```text
#RRGGBBAA
```

Если текущая схема использует:

```text
#RRGGBB
```

alpha считается равной `FF`.

Парсинг цвета должен быть одинаковым в C# и GDScript.

Невалидный цвет:

* создаёт validation error;
* не вызывает необработанного исключения;
* может использовать безопасный fallback только для диагностического preview.

---

## 10. Validation Path

Проверить:

* `path` существует;
* `start` существует;
* `start.x` и `start.y` finite;
* `segments` является массивом;
* массив содержит минимум один сегмент;
* `type` поддерживается;
* все координаты finite;
* line имеет `end`;
* cubicBezier имеет `control1`, `control2`, `end`;
* нет сегментов практически нулевой длины после sampling;
* итоговый Path имеет хотя бы две различимые точки;
* толщина положительна;
* ID не пуст;
* ID уникален в своей области документа;
* стиль поддерживается.

Validation не должна запрещать:

* control points вне bounding rectangle endpoints;
* петли;
* самопересечения;
* резкие углы;
* Bézier-кривую, выходящую за footprint Exercise.

Это допустимая пользовательская геометрия.

---

## 11. Sampling line

Line segment sampling должен включать:

* начало;
* конец.

При объединении нескольких сегментов общая точка стыка не должна дублироваться в результирующей полилинии.

Если line используется для dashed/dotted, renderer может дополнительно делить его согласно длинам штрихов.

---

## 12. Sampling cubic Bézier

Кубическая кривая определяется:

```text
P0 — начало сегмента
P1 — control1
P2 — control2
P3 — end
```

Положение:

```text
B(t) =
(1 - t)^3 P0
+ 3(1 - t)^2 t P1
+ 3(1 - t)t^2 P2
+ t^3 P3
```

где:

```text
0 <= t <= 1
```

Не использовать постоянное малое число samples для всех кривых.

Предпочтительно использовать adaptive subdivision по:

* flatness;
* максимальной длине хорды;
* максимальной ошибке относительно control polygon.

Начальные параметры должны обеспечивать визуально плавную кривую без чрезмерного количества точек.

Рекомендуемые ориентиры:

```text
maximum chord length: 0.20–0.50 m
flatness tolerance:   0.01–0.03 m
maximum recursion:    10–12
```

Фактические значения централизовать.

---

## 13. Sampling contract

Создать общий C#-сервис, например:

```text
PathSampler
```

Он возвращает:

```text
ordered points
cumulative distances
total length
```

Не создавать независимые реализации sampling для:

* Exercise Editor;
* Venue Editor;
* Track Editor;
* desktop Viewer;
* exporter.

Все desktop-компоненты используют одну C#-реализацию.

Web Viewer содержит эквивалентную GDScript-реализацию с теми же допусками и тестовыми примерами.

---

## 14. Cumulative length

Для sampled points построить накопленные расстояния:

```text
distance[0] = 0
distance[i] =
    distance[i - 1]
    + distance(points[i - 1], points[i])
```

Использовать эту длину для:

* dashed pattern;
* dotted pattern;
* bounds diagnostics;
* будущих editor operations;
* проверок соответствия C# и GDScript.

Не рассчитывать dash pattern по индексам sample points.

---

## 15. Dashed rendering

Dashed style определяется расстоянием вдоль sampled world-space path.

Рекомендуемые параметры:

```text
dash length: 0.50 m
gap length:  0.30 m
```

Если текущий проект уже имеет параметры стиля, сохранить их.

Алгоритм должен:

* начинать pattern от начала Path;
* сохранять ритм через границы line/Bézier segments;
* не сбрасывать pattern на каждом сегменте;
* работать после неравномерного transform;
* сохранять приблизительно одинаковую мировую длину штрихов.

---

## 16. Dotted rendering

Dotted style также определяется мировой длиной.

Рекомендуемый интервал:

```text
dot spacing: 0.35–0.60 m
```

Dot должен иметь визуальный диаметр, связанный с `thickness`.

Не создавать сотни отдельных Node3D, если точки можно объединить в один или малое число mesh surfaces.

---

## 17. Transform Path

Для Exercise instance применить существующую transform policy:

1. local Exercise coordinates;
2. independent `scaleX` и `scaleY`;
3. rotation;
4. translation в Track coordinates.

Трансформируются:

* `start`;
* line `end`;
* cubic `control1`;
* cubic `control2`;
* cubic `end`.

Предпочтительный порядок:

```text
transform control geometry
→ sample transformed path
→ calculate world-space length
→ apply dash/dot pattern
```

Не следует:

```text
sample local curve
→ неравномерно масштабировать готовые штрихи
```

если это искажает длину pattern.

---

## 18. Bounds

Bounds Path должны учитывать не только endpoints.

Для cubic Bézier bounds должны учитывать внутренние экстремумы.

Допустимы два подхода:

### Analytical bounds

Вычислить корни производной отдельно по X и Y и проверить значения `t` в диапазоне `0...1`.

### Conservative sampled bounds

Использовать достаточно точный adaptive sampling и добавить небольшой tolerance.

Предпочтителен analytical вариант для domain bounds.

Если Iteration 1 использует sampled bounds, это должно быть явно документировано как ограничение.

Bounds marking должны учитывать половину `thickness`.

---

## 19. Exercise footprint

Curved marking может выходить за существующий Exercise footprint.

Iteration 1 не должна автоматически менять footprint без существующей проектной политики.

Но редакторские diagnostics должны уметь обнаружить:

```text
marking bounds outside footprint
```

Если Exercise footprint в текущем проекте рассчитывается автоматически по содержимому, curved markings необходимо включить в расчёт.

---

## 20. Surface projection

После преобразования и sampling полученная мировая полилиния проецируется на `WalkableSurface`.

Использовать существующий SurfaceProjectionService.

Проецировать:

* каждую необходимую точку Path;
* дополнительные точки после subdivision;
* точки dashed/dotted geometry.

Предпочтительно:

```text
sample geometry
→ project centerline
→ build ribbon/dashes around projected centerline
```

Это позволяет линии корректно следовать наклонной поверхности.

Не строить плоский ribbon до projection, если его вершины затем могут пересечь ramp.

---

## 21. Surface normal

Если существующий marking renderer использует orientation по surface normal, сохранить это поведение.

Для каждого sampled point может потребоваться:

```text
position
normal
projection success
```

Не использовать только глобальный `Vector3.UP` на ramp, если это приводит к проникновению линии в поверхность.

---

## 22. Projection fallback

При неуспешной проекции точки:

* применить существующую fallback policy;
* не падать всем Viewer;
* сгруппировать diagnostics;
* не писать warning каждый frame.

Если часть Path не спроецирована:

* допустимо использовать Venue base plane;
* либо пропустить повреждённый участок;
* итоговое поведение должно совпадать с текущей политикой trajectory/markings.

---

## 23. Exercise Definition v3

Exercise marking больше не хранит простую полилинию как основной контракт.

Новый основной контракт:

```text
Marking.path
```

Существующие поля наподобие:

```text
points
polyline
```

удалить из v3, если они являлись только старым представлением геометрии.

Не хранить одновременно старое и новое представление в одном документе.

---

## 24. Venue Definition v2

Venue marking использует тот же Path contract.

Venue path coordinates остаются в системе координат Venue.

Все существующие свойства:

* style;
* thickness;
* color;
* visibility;

должны сохраняться.

---

## 25. Exported Track v5

Exported Track содержит уже преобразованные marking paths либо корректно преобразованную геометрию согласно текущей export architecture.

Предпочтительная схема:

* Exercise markings экспортируются в Track world coordinates;
* Venue markings экспортируются в Venue/Track coordinates;
* Path contract сохраняет line/cubicBezier segments;
* Web Viewer выполняет sampling и surface projection.

Не заменять кривую огромным массивом samples в JSON без серьёзной причины.

Преимущества сохранения сегментов:

* компактный export;
* независимая настройка качества renderer;
* точная геометрия;
* дальнейшая поддержка редактирования и анализа.

---

## 26. Export transform

При экспорте Exercise instance:

* Path control geometry трансформируется;
* сегмент остаётся `line` или `cubicBezier`;
* cubic control points трансформируются тем же affine transform;
* geometry не обязана превращаться в polyline.

Affine transform сохраняет кубическую Bézier-кривую как кубическую Bézier-кривую.

---

## 27. Web Viewer

GDScript Web Viewer должен поддержать Exported Track v5.

Добавить:

* Path parser;
* Path validator;
* line sampling;
* cubic sampling;
* cumulative length;
* solid/dashed/dotted rendering;
* surface projection;
* failure diagnostics.

Web Viewer больше не обязан поддерживать Track v4 после перехода production export на v5, если проект не требует backward compatibility.

Fallback `default-track.json` также обновить до v5.

---

## 28. Desktop Viewer

Desktop C# Viewer должен использовать общий PathSampler.

Не оставлять отдельный старый code path только для straight markings.

Все markings проходят единый pipeline:

```text
Path
→ transform
→ sample
→ cumulative length
→ style geometry
→ surface projection
→ render
```

---

## 29. Exercise Editor в Iteration 1

Exercise Editor должен:

* загружать Exercise v3;
* отображать line и cubicBezier;
* корректно выбирать marking целиком, если selection уже существует;
* сохранять документ без потери curved geometry;
* сохранять существующие marking properties.

Не требуется:

* выбор отдельного segment;
* отображение control handles;
* редактирование control points.

Если редактор позволяет перемещать marking целиком, transform должен применяться ко всем точкам Path.

---

## 30. Venue Editor в Iteration 1

Venue Editor должен:

* загружать Venue v2;
* отображать curved markings;
* сохранять документ без потери geometry;
* учитывать curved bounds;
* поддерживать перемещение marking целиком, если это уже предусмотрено.

Не добавлять segment editing UI.

---

## 31. Track Editor

Track Editor должен отображать transformed curved Exercise markings.

Проверить:

* rotation;
* translation;
* scaleX;
* scaleY;
* negative scale, если он допускается;
* duplicated Exercise instances;
* Undo/Redo transform instance;
* preview.

Track Editor не редактирует внутренние control points Exercise marking.

---

## 32. Старые JSON

Поскольку проект ещё находится в разработке, долговременная runtime-поддержка старых форматов не требуется.

Необходимо:

* обновить все Exercise fixtures v2 → v3;
* обновить Venue fixtures v1 → v2;
* обновить Track exports v4 → v5;
* обновить `default-track.json`;
* обновить sample documents;
* обновить tests.

Допускается одноразовый development converter.

Converter не должен становиться частью production runtime без необходимости.

---

## 33. Конвертация старой полилинии

Старая разметка вида:

```json
{
  "points": [
    { "x": 0, "y": 0 },
    { "x": 2, "y": 1 },
    { "x": 4, "y": 1 }
  ]
}
```

преобразуется в:

```json
{
  "path": {
    "start": {
      "x": 0,
      "y": 0
    },
    "segments": [
      {
        "type": "line",
        "end": {
          "x": 2,
          "y": 1
        }
      },
      {
        "type": "line",
        "end": {
          "x": 4,
          "y": 1
        }
      }
    ]
  }
}
```

Порядок точек сохраняется.

Последовательные duplicate points удалить либо зарегистрировать как validation error согласно выбранной converter policy.

---

## 34. Архитектура C#

Рекомендуемые domain-типы:

```text
PathDefinition
PathSegmentDefinition
LinePathSegmentDefinition
CubicBezierPathSegmentDefinition
```

Рекомендуемые сервисы:

```text
PathValidator
PathSampler
PathTransformService
PathBoundsCalculator
PathLengthCalculator
```

Названия могут быть адаптированы к текущим conventions.

Не использовать Godot Node-типы в чистом domain layer без необходимости.

Domain geometry должна тестироваться без запуска сцены.

---

## 35. Полиморфная сериализация C#

Использовать явный discriminator:

```text
type
```

Проверить текущий `System.Text.Json` setup.

Допустимые подходы:

* `JsonPolymorphic`;
* `JsonDerivedType`;
* custom converter;
* DTO с enum/string type и явно nullable segment fields.

Выбранный подход должен:

* строго отклонять неизвестный type;
* создавать понятные ошибки;
* стабильно сериализовать ожидаемый JSON;
* поддерживать AOT/trim, если это важно для итоговой сборки.

---

## 36. GDScript parsing

В Web Viewer допускается parsing через Dictionary.

Но parsing должен быть централизованным:

```text
parse_path
parse_segment
validate_path
```

Не размазывать проверки по renderer и Track loader.

Unknown segment type должен блокировать конкретный marking либо документ согласно общей validation policy.

---

## 37. Паритет

Добавить общие test vectors для C# и GDScript:

* одна line;
* две line;
* одна cubic;
* line + cubic;
* cubic + line;
* S-образная cubic;
* control points вне endpoint bounds;
* почти прямая cubic;
* очень короткая cubic;
* неравномерный scale;
* rotation;
* translation.

Для test vectors хранить ожидаемые:

* start/end;
* approximate length;
* bounds;
* sampled point count range;
* first/last point.

Точный набор intermediate samples может различаться, если adaptive implementations эквивалентны по tolerance.

---

## 38. Производительность

Не создавать Node3D на каждый sampled segment.

Предпочтительно:

* один MeshInstance3D на marking;
* либо batched mesh для группы markings;
* один или малое количество surfaces.

Adaptive sampling должен иметь верхнюю границу.

Невалидная или экстремальная curve не должна создавать миллионы samples.

---

## 39. Error handling

Ошибка одного marking:

* не должна падать всем Viewer;
* должна указывать ID;
* должна указывать document source;
* должна указывать тип ошибки;
* может привести к пропуску конкретного marking.

Ошибка schema version документа обрабатывается на уровне загрузчика документа.

---

## 40. Не реализовывать

В этой итерации не добавлять:

* editor segment toolbar;
* control handles;
* snapping control points;
* tangent modes;
* quadratic Bézier;
* Catmull–Rom;
* arcs;
* circles как отдельный primitive;
* closed polygon fills;
* text;
* labels;
* arrows;
* decals;
* runtime animation;
* user-defined dash pattern;
* format backward compatibility beyond development converter.

---

## 41. Definition of Done

Итерация завершена, если:

* Path contract документирован;
* line segment сериализуется;
* cubicBezier сериализуется;
* unknown type отклоняется;
* Exercise v3 загружается;
* Venue v2 загружается;
* Exported Track v5 загружается;
* Track Project version подтверждена;
* старые fixtures конвертированы;
* fallback Web Track обновлён;
* C# sampling работает;
* GDScript sampling работает;
* line отображается;
* cubic отображается;
* mixed path отображается;
* solid работает;
* dashed работает по мировой длине;
* dotted работает по мировой длине;
* неравномерный scale работает;
* rotation работает;
* translation работает;
* bounds учитывают curve;
* Exercise Editor отображает curve;
* Venue Editor отображает curve;
* Track Editor отображает transformed curve;
* desktop Viewer отображает curve;
* Web Viewer отображает curve;
* surface projection работает над asphalt;
* surface projection работает над ramp;
* invalid marking не ломает Viewer;
* JSON formats имеют новые версии;
* документация обновлена;
* тестовые данные обновлены;
* desktop build проходит;
* Web export проходит.


# Exercise Editor editing

Интерактивное редактирование curved markings выполняется отдельной итерацией:

```text
docs/ExerciseEditorCurvedMarkingsPlan.md
```

Domain and Rendering Iteration предоставляет:

* Path contract;
* segment types;
* sampling;
* transforms;
* serialization;
* rendering.

Exercise Editor Iteration добавляет:

* создание Path;
* segment tools;
* handles;
* hit testing;
* structural edits;
* Undo/Redo.

Не смешивать editor transient state с сериализуемым Path contract.

