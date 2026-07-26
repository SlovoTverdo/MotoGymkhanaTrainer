# Exported Track JSON Format

Это контракт между Editor и Viewer.

Главный принцип:

> Exported Track JSON является самодостаточным snapshot трассы.

Viewer не загружает библиотеку `ExerciseDefinition`, не генерирует соединения между упражнениями и не применяет трансформации исходных элементов.

Текущая версия:

```json
{
  "formatVersion": 4
}
```

Версия 2 вводит сегментную модель trajectory.

---

# 1. Верхний уровень

Каноническая структура:

```json
{
  "formatVersion": 2,

  "track": {
    "id": "training-2026-07-25",
    "name": "Тренировка 25 июля 2026"
  },

  "area": {
    "width": 60.0,
    "length": 100.0
  },

  "elements": [],

  "cones": [],

  "markings": [],

  "trajectory": {
    "segments": []
  },

  "checkpoints": []
}
```

`trajectory` всегда является объектом:

```json
{
  "trajectory": {
    "segments": []
  }
}
```

Не допускаются устаревшие варианты:

```json
{
  "trajectory": []
}
```

или:

```json
{
  "trajectory": {
    "points": []
  }
}
```

---

# 2. Координаты и единицы

Доменные данные используют двумерные координаты:

```text
X / Y
```

Единица измерения:

```text
1 unit = 1 meter
```

В Godot рекомендуется:

```text
domain X → Godot X
domain Y → Godot -Z
```

Вертикальная ось:

```text
Godot Y
```

используется для высоты.

Преобразование должно быть централизовано в одном mapper/helper.

---

# 3. track

```json
{
  "track": {
    "id": "training-2026-07-25",
    "name": "Тренировка 25 июля 2026"
  }
}
```

Поля:

* `id` — стабильный идентификатор трассы;
* `name` — отображаемое имя.

Позже metadata могут быть расширены без изменения основной геометрической модели.

---

# 4. area

```json
{
  "area": {
    "width": 60.0,
    "length": 100.0
  }
}
```

Размеры заданы в метрах.

Viewer использует их для создания или настройки площадки.

---

# 5. elements

`elements[]` может сохраняться как диагностическая и информационная часть snapshot.

Пример:

```json
{
  "instanceId": "element-07",
  "definitionId": "slalom-5",

  "position": {
    "x": 22.4,
    "y": 31.8
  },

  "rotationDeg": 90.0,

  "scale": {
    "x": 1.25,
    "y": 0.9
  }
}
```

Viewer не должен использовать `elements[]` для повторного построения трассы.

К моменту экспорта transform уже применён ко всей мировой геометрии.

Viewer не должен повторно применять:

* position;
* rotation;
* scale.

---

# Compiled export rules

## 1. Источник экспортированных данных

Exported Track JSON создаётся Track Editor из:

```text
Track Project
+
Exercise Definition Library
```

К моменту сериализации Track Editor уже:

* разрешил Exercise Definition;
* применил ExerciseInstance transforms;
* преобразовал geometry в мировые координаты;
* вычислил entry/exit tangents;
* создал переходные cubicBezier;
* сформировал global trajectory;
* проверил уникальность IDs.

Viewer получает итоговый snapshot.

---

## 2. elements metadata

`elements[]` описывает исходные ExerciseInstance только для:

* диагностики;
* анализа;
* потенциальных будущих editor tools.

Пример:

```json
{
  "instanceId": "exercise-instance-001",
  "definitionId": "slalom-5",
  "exercisePath": "slaloms/slalom-5.json",

  "position": {
    "x": 12.0,
    "y": -18.0
  },

  "rotationDeg": 30.0,

  "scale": {
    "x": 1.0,
    "y": 1.25
  }
}
```

Обязательные поля:

* `instanceId`;
* `definitionId`;
* `position`;
* `rotationDeg`;
* `scale`.

`exercisePath` является необязательным диагностическим полем.

Viewer не должен использовать `elements[]` для построения:

* cones;
* markings;
* trajectory;
* transitions.

---

## 3. Exported object IDs

Все IDs должны быть уникальны в пределах соответствующей коллекции exported Track.

Для объектов, пришедших из Exercise Definition, рекомендуется:

```text
{instanceId}--{localObjectId}
```

Примеры:

```text
exercise-instance-001--cone-001
exercise-instance-001--marking-001
exercise-instance-001--trajectory-segment-001
```

Для автоматически построенного перехода:

```text
transition--{fromInstanceId}--{toInstanceId}
```

Пример:

```text
transition--exercise-instance-001--exercise-instance-002
```

IDs не должны зависеть от:

* `track.name`;
* Exercise display name;
* позиции instance в массиве;
* имени экспортного файла.

---

## 4. Exported cones

Экспортированный cone содержит:

* уникальный world-level `id`;
* world `position`;
* `color`;
* `type`.

World position уже учитывает:

* scale;
* rotation;
* translation.

Viewer не должен повторно применять transform.

---

## 5. Exported markings

Экспортированный marking содержит:

* уникальный world-level `id`;
* `type`;
* world `points`;
* `color`;
* `widthMeters`;
* `style`;
* `visibleInViewer`.

`points` уже учитывают instance transform.

`widthMeters` не масштабируется вместе с ExerciseInstance.

Marking с:

```json
"visibleInViewer": false
```

остаётся в JSON, но Viewer не создаёт его визуальное представление.

---

## 6. Global trajectory origin

Exported `trajectory.segments[]` содержит единую глобальную последовательность:

```text
transformed Exercise trajectory
        ↓
automatic transition cubicBezier
        ↓
transformed Exercise trajectory
        ↓
...
```

Для Viewer все segments равноправны.

Viewer не обязан определять, является segment:

* внутренней trajectory упражнения;
* автоматически созданным переходом;
* в будущем вручную исправленным переходом.


# Transition source transparency

Exported Track JSON не различает источник переходного cubicBezier.

Переход может быть:

* автоматически рассчитан Track Editor;
* скорректирован пользователем через TransitionOverride.

В обоих случаях он экспортируется как обычный:

```text
trajectory.segments[].type = cubicBezier
```

Viewer использует только итоговые:

* start;
* control1;
* control2;
* end.

Viewer не должен:

* искать TransitionOverride;
* пересчитывать control points;
* определять, был ли переход изменён вручную;
* зависеть от Track Project.

Таким образом, ручная коррекция перехода не требует повышения `TrackFormat formatVersion`.

---

## 7. Automatic transition representation

Автоматический переход экспортируется как обычный:

```json
{
  "id": "transition--exercise-instance-001--exercise-instance-002",
  "type": "cubicBezier",

  "start": {
    "x": 10.0,
    "y": 12.0
  },

  "control1": {
    "x": 12.0,
    "y": 14.0
  },

  "control2": {
    "x": 17.0,
    "y": 16.0
  },

  "end": {
    "x": 19.0,
    "y": 18.0
  }
}
```

Отдельные поля:

```text
connector
fromElement
toElement
```

не являются обязательными для rendering.

Идентификатор transition уже позволяет диагностировать связанную пару instances.

---

## 8. Trajectory continuity

Exported global trajectory должна быть непрерывной с небольшой численной погрешностью.

Последовательность:

```text
previous segment end
=
next segment start
```

Track Editor обязан проверить это до сохранения export.

Viewer при обнаружении разрыва:

* выводит warning;
* не падает;
* отображает валидные segments;
* не пытается автоматически перестроить transition.

---

## 9. Single-instance track

Для трассы из одного ExerciseInstance:

* global trajectory содержит только transformed trajectory упражнения;
* transition segments отсутствуют.

---

## 10. Empty track

Для production export рекомендуется требовать минимум один валидный ExerciseInstance.

Если поддерживается пустой export, его каноническая trajectory:

```json
{
  "trajectory": {
    "segments": []
  }
}
```

Предпочтительное поведение Track Editor Iteration 2 — блокировать экспорт пустой трассы.

---

## 11. Export validation

Перед сохранением Track Editor проверяет snapshot.

Блокирующие условия:

* unresolved ExerciseInstance;
* невалидная Exercise trajectory;
* невозможность вычислить tangent;
* невалидный scale;
* non-finite coordinates;
* duplicate IDs;
* разрыв global trajectory;
* неподдерживаемый обязательный тип geometry.

Предупреждения:

* geometry за пределами area;
* длинный transition;
* transition за пределами area;
* пересечение bounds;
* резкий переход.

Warnings не делают экспортный JSON структурно невалидным.

---

## 12. Snapshot independence

После сохранения Exported Track JSON файл не зависит от:

* Track Project;
* Exercise Definition Library;
* расположения исходных файлов;
* последующих изменений упражнений.

Это свойство обязательно для Viewer и для распространения готовых трасс.


# 6. cones

Все позиции конусов являются мировыми координатами.

```json
{
  "id": "cone-001",

  "position": {
    "x": 10.0,
    "y": 15.5
  },

  "color": "red",
  "type": "standard"
}
```

Физический размер 3D-модели конуса не зависит от масштаба исходного упражнения.

Допустимое значение `color: "none"` сохраняет обычную 3D-модель конуса, но
Viewer не создаёт дополнительное цветное навершие.

---

# 7. markings


`widthMeters` задаётся в физических метрах.

Он уже является итоговым значением и не должен дополнительно масштабироваться Viewer.

# Markings visibility and style

Exported Track JSON сохраняет итоговые свойства markings после применения геометрических transform ExerciseInstance.

Пример:

```json
{
  "id": "marking-017",

  "type": "polyline",

  "points": [
    {
      "x": 14.0,
      "y": 20.0
    },
    {
      "x": 17.0,
      "y": 23.0
    }
  ],

  "color": "#FFD400",

  "widthMeters": 0.10,

  "style": "dashed",

  "visibleInViewer": true
}
```

К моменту экспорта:

* `points` находятся в мировых координатах;
* `widthMeters` остаётся физической шириной;
* `style` сохраняется без изменения;
* `visibleInViewer` сохраняется без изменения.

Viewer должен:

```text
visibleInViewer = true
        ↓
отрисовать marking

visibleInViewer = false
        ↓
не создавать его визуальное представление
```

Marking с `visibleInViewer = false` остаётся частью exported Track JSON.

В текущей версии поддерживаются стили:

```text
solid
dashed
dotted
```

Неизвестный style должен использовать fallback:

```text
solid
```

с diagnostic warning.


---

# 8. trajectory

`trajectory` содержит **полную итоговую траекторию движения по трассе**.

Она уже включает:

* внутренние trajectory segments всех упражнений;
* автоматически созданные spline между соседними упражнениями;
* ручные корректировки этих spline, если они были сделаны в Editor.

Viewer не различает происхождение сегментов.

Структура:

```json
{
  "trajectory": {
    "segments": [
      {
        "id": "trajectory-segment-001",
        "type": "polyline"
      },
      {
        "id": "trajectory-segment-002",
        "type": "cubicBezier"
      }
    ]
  }
}
```

Порядок элементов `segments[]` является порядком движения по трассе.

---

# 9. Общие поля trajectory segment

Каждый segment должен содержать:

```json
{
  "id": "trajectory-segment-001",
  "type": "polyline"
}
```

## id

Стабильный идентификатор внутри экспортированной трассы.

Используется для:

* диагностики;
* предупреждений;
* журналирования;
* потенциальных будущих инструментов анализа.

## type

Определяет геометрическую интерпретацию.

В `formatVersion: 2`:

```text
polyline
cubicBezier
```

---

# 10. Polyline segment

```json
{
  "id": "trajectory-segment-001",

  "type": "polyline",

  "points": [
    {
      "x": 1.0,
      "y": 2.0
    },
    {
      "x": 1.5,
      "y": 2.4
    },
    {
      "x": 2.0,
      "y": 3.0
    }
  ]
}
```

Требования:

* минимум две точки;
* первая — начало segment;
* последняя — конец segment;
* порядок точек соответствует направлению движения.

---

# 11. CubicBezier segment

```json
{
  "id": "trajectory-segment-002",

  "type": "cubicBezier",

  "start": {
    "x": 9.0,
    "y": 11.5
  },

  "control1": {
    "x": 11.0,
    "y": 11.0
  },

  "control2": {
    "x": 13.0,
    "y": 15.0
  },

  "end": {
    "x": 15.0,
    "y": 15.0
  }
}
```

Поля:

* `start` — начало;
* `control1` — первая управляющая точка;
* `control2` — вторая управляющая точка;
* `end` — конец.

Viewer должен использовать эту геометрию как кубическую кривую Безье.

Для rendering допустима дискретизация кривой.

Исходный JSON при этом остаётся spline-представлением, а не массивом заранее рассчитанных точек.

---

# 12. Переходы между упражнениями

Exported JSON **не содержит отдельной сущности Connector**.

Editor соединяет:

```text
ExitPoint предыдущего элемента
              ↓
      generated cubicBezier
              ↓
EntryPoint следующего элемента
```

После экспорта такой переход является обычным:

```json
{
  "type": "cubicBezier"
}
```

Viewer не должен определять:

* откуда этот spline появился;
* между какими упражнениями он был создан;
* был ли он автоматически сгенерирован или исправлен вручную.

Для Viewer это просто часть итоговой trajectory.

---

# 13. Непрерывность trajectory

Сегменты образуют одну последовательную траекторию.

Конец предыдущего segment должен совпадать с началом следующего с небольшой допустимой погрешностью.

Для `polyline`:

```text
start = points[0]
end   = points[last]
```

Для `cubicBezier`:

```text
start = start
end   = end
```

Editor должен валидировать это до экспорта.

Viewer при обнаружении разрыва:

1. пишет diagnostic warning;
2. не падает;
3. продолжает отображать доступные валидные segments;
4. не пытается автоматически исправлять геометрию.

---

# 14. Неизвестные segment types

Формат должен оставаться расширяемым.

Будущий файл может содержать:

```json
{
  "type": "arc"
}
```

Viewer старой версии должен:

1. не падать;
2. пропустить только неизвестный segment;
3. вывести warning с:

   * `id`;
   * `type`;
4. продолжить загрузку остальных данных.

---

# 15. Checkpoints

Зарезервированы для будущего режима проверки прохождения.

```json
{
  "id": "cp-01",

  "order": 1,

  "center": {
    "x": 5.0,
    "y": 10.0
  },

  "direction": {
    "x": 1.0,
    "y": 0.0
  },

  "width": 3.0
}
```

Поля:

* `id`;
* `order`;
* `center`;
* `direction`;
* `width`.

Checkpoints не обязаны совпадать с trajectory samples.

Они являются отдельной логикой проверки прохождения.

---

# 16. Что Viewer не делает

Viewer не должен:

* загружать ExerciseDefinition;
* определять EntryPoint/ExitPoint;
* вычислять касательные;
* соединять упражнения;
* генерировать Bezier control points;
* применять scale элементов;
* применять rotation элементов;
* применять translation элементов;
* восстанавливать Editor project.

Он получает уже готовый snapshot.

---

# 17. Versioning

`formatVersion` обязателен.

Текущая версия:

```json
{
  "formatVersion": 2
}
```

## Version 1

Ранний черновой формат использовал:

```json
{
  "trajectory": {
    "points": []
  }
}
```

В одном из ранних примеров также ошибочно встречался:

```json
{
  "trajectory": []
}
```

Оба варианта устарели.

## Version 2

Использует только:

```json
{
  "trajectory": {
    "segments": []
  }
}
```

Поддерживаемые типы:

```text
polyline
cubicBezier
```

Так как опубликованных production-трасс версии 1 ещё нет, обязательная backward compatibility сейчас не требуется.

Следует реализовать чистый контракт версии 2, а не добавлять legacy-ветви без практической необходимости.
