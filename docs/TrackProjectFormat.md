# Track Project JSON Format

## 1. Назначение

Track Project является редактируемым описанием конкретной трассы на существующей Venue Definition.

Он хранит:

* metadata трассы;
* ссылку на Venue;
* упорядоченные ExerciseInstance;
* ручные TransitionOverride.

Track Project не является самодостаточным Viewer snapshot.

Для его открытия нужны:

* Venue Definition;
* Exercise Definition Library.

---

# 2. Workflow

```text
Venue Definition
        +
Exercise Definitions
        ↓
Track Editor
        ↓
Track Project
        ↓
Track compilation
        ↓
Exported Track JSON
        ↓
Viewer
```

---

# 3. Версия

Текущая версия:

```json
{
  "formatVersion": 3
}
```

Track Project versions 1–2 не поддерживаются.

Миграция не реализуется.

---

# 4. Каноническая структура

```json
{
  "formatVersion": 3,

  "track": {
    "id": "training-track-01",
    "name": "Тренировка 01"
  },

  "venuePath": "main-training-ground/venue.json",

  "instances": [
    {
      "instanceId": "exercise-instance-001",
      "exercisePath": "gates/start-gate.json",

      "position": {
        "x": 0.0,
        "y": -30.0
      },

      "rotationDeg": 0.0,

      "scale": {
        "x": 1.0,
        "y": 1.0
      }
    }
  ],

  "transitionOverrides": []
}
```

---

# 5. track

```json
{
  "track": {
    "id": "training-track-01",
    "name": "Тренировка 01"
  }
}
```

## id

Стабильный идентификатор трассы.

Рекомендуемый формат:

```text
lowercase-latin-digits-hyphens
```

## name

Отображаемое имя трассы.

Изменение `id` или `name` не переименовывает файл автоматически.

---

# 6. venuePath

```json
"venuePath": "main-training-ground/venue.json"
```

Обязательная ссылка на Venue Definition.

Путь:

* относителен `res://venues/`;
* не содержит prefix `res://venues/`;
* не является абсолютным;
* не содержит `..`;
* не выходит за Venue Library root.

Допустимо:

```text
main-training-ground/venue.json
empty-ground/venue.json
training/parking-lot.json
```

Недопустимо:

```text
res://venues/main-training-ground/venue.json
C:\Project\venues\venue.json
../../venue.json
```

Track Editor разрешает полный путь как:

```text
res://venues/ + venuePath
```

---

# 7. Venue ownership

Venue Definition является единственным источником:

* area width;
* area length;
* panorama;
* постоянных objects;
* постоянных cones;
* постоянных markings.

Track Project не копирует эти данные.

Если Venue Definition изменена, Track Project использует её актуальное состояние после reload/open/compile.

---

# 8. instances

`instances[]` — упорядоченный список ExerciseInstance и порядок прохождения трассы.

Пример:

```json
{
  "instanceId": "exercise-instance-001",
  "exercisePath": "slaloms/slalom-5.json",

  "position": {
    "x": 12.0,
    "y": -5.0
  },

  "rotationDeg": 30.0,

  "scale": {
    "x": 1.0,
    "y": 1.2
  }
}
```

---

# 9. instanceId

Уникальный стабильный идентификатор instance внутри Track Project.

Несколько instances могут ссылаться на один Exercise Definition, но обязаны иметь разные `instanceId`.

---

# 10. exercisePath

Относительный путь внутри:

```text
res://exercises/
```

Он не должен выходить за Exercise Library root.

---

# 11. position

Позиция local origin Exercise Definition в координатах Venue.

```text
Venue X / Y
1 unit = 1 meter
```

---

# 12. rotationDeg

Поворот вокруг вертикальной оси в общей двумерной доменной системе.

---

# 13. scale

Независимый масштаб локальных координат Exercise Definition.

Требования:

```text
scale.x > 0
scale.y > 0
```

Масштабируются:

* cone positions;
* marking points;
* trajectory;
* entry/exit;
* bounds.

Не масштабируются:

* физический размер конусов;
* marking widthMeters.

---

# 14. Transform order

```text
local point
    ↓
scale X/Y
    ↓
rotation
    ↓
translation
    ↓
Venue world point
```

---

# 15. transitionOverrides

`transitionOverrides[]` хранит только переходы, вручную изменённые пользователем.

Пример:

```json
{
  "transitionId": "transition--exercise-instance-001--exercise-instance-002",

  "fromInstanceId": "exercise-instance-001",
  "toInstanceId": "exercise-instance-002",

  "control1Offset": {
    "x": 3.0,
    "y": 1.5
  },

  "control2Offset": {
    "x": -2.0,
    "y": -1.0
  }
}
```

Автоматические transitions не сохраняются.

---

# 16. TransitionOverride applicability

Override применяется только для текущей соседней ориентированной пары:

```text
fromInstanceId → toInstanceId
```

Если пара перестала быть соседней:

* override становится orphaned;
* не применяется;
* не экспортируется;
* сохраняется до ручного удаления.

---

# 17. Track Project validation

## Blocking errors

* unsupported `formatVersion`;
* пустой `track.id`;
* пустой `track.name`;
* пустой или небезопасный `venuePath`;
* Venue Definition не существует;
* Venue Definition невалидна;
* duplicate `instanceId`;
* небезопасный `exercisePath`;
* `scale.x <= 0`;
* `scale.y <= 0`;
* duplicate TransitionOverride pair;
* duplicate transitionId;
* non-finite persisted values.

## Warnings

* unresolved Exercise Definition;
* orphaned TransitionOverride;
* ExerciseInstance за bounds Venue;
* ExerciseInstance пересекает Venue object footprint.

Unresolved Exercise Definition может оставаться редактируемым состоянием, но блокирует export.

---

# 18. Editor-only state

Track Project не сохраняет:

* Venue cache;
* Exercise Definition cache;
* transformed geometry;
* automatic transitions;
* global trajectory;
* selection;
* locks;
* pan;
* zoom;
* active tool;
* Undo/Redo history;
* validation UI;
* last export path;
* Viewer state.

---

# 19. Что не входит в formatVersion 3

* копия Venue Definition;
* area;
* panorama;
* Venue objects;
* Venue cones;
* Venue markings;
* transformed Exercise geometry;
* compiled global trajectory;
* automatic transitions;
* exported IDs;
* Viewer resources;
* gameplay state;
* checkpoints;
* environment settings;
* surface material;
* spawn point.
