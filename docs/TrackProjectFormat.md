# Track Project JSON Format

Этот документ описывает редактируемый проект трассы.

Track Project используется только Track Editor.

Он отличается от:

* Exercise Definition JSON;
* Exported Track JSON для Viewer.

Workflow:

```text
Exercise Definition Library
            ↓
       Track Editor
            ↓
     Track Project JSON
            ↓
          Export
            ↓
    Exported Track JSON
            ↓
          Viewer
```

---

# 1. Основной принцип

Track Project хранит:

* параметры трассы;
* параметры площадки;
* упорядоченный список экземпляров упражнений;
* ссылки на Exercise Definition;
* трансформации экземпляров;
* в будущем — ручные корректировки переходов.

Track Project не является самодостаточным snapshot.

Для его открытия требуется соответствующая библиотека:

```text
res://exercises/
```

Viewer не должен загружать Track Project.

---

# 2. Версия формата

Текущая версия:

```json
{
  "formatVersion": 2
}
```

Версия Track Project независима от:

* Exercise Definition formatVersion;
* Exported Track formatVersion.

---

# 3. Каноническая структура

```json
{
  "formatVersion": 1,

  "track": {
    "id": "training-2026-07-26",
    "name": "Тренировка 26 июля 2026"
  },

  "area": {
    "width": 60.0,
    "length": 100.0
  },

  "instances": []
}
```

---

# 4. track

```json
{
  "track": {
    "id": "training-2026-07-26",
    "name": "Тренировка 26 июля 2026"
  }
}
```

## id

Стабильный идентификатор проекта трассы.

Рекомендуемый формат:

```text
lowercase-latin-digits-hyphens
```

## name

Отображаемое имя трассы.

Изменение имени не изменяет автоматически ID или имя файла.

---

# 5. area

```json
{
  "area": {
    "width": 60.0,
    "length": 100.0
  }
}
```

Размеры задаются в метрах.

Локальная система Track Editor:

```text
X / Y
1 unit = 1 meter
```

Рекомендуемый origin площадки:

```text
геометрический центр area
```

Предполагаемые границы:

```text
X: -width / 2 ... +width / 2
Y: -length / 2 ... +length / 2
```

---

# 6. instances

`instances[]` является упорядоченным списком прохождения упражнений.

Порядок элементов массива определяет порядок трассы.

Пример:

```json
{
  "instances": [
    {
      "instanceId": "exercise-instance-001",
      "exercisePath": "slaloms/slalom-5.json",
      "position": {
        "x": 10.0,
        "y": -20.0
      },
      "rotationDeg": 0.0,
      "scale": {
        "x": 1.0,
        "y": 1.0
      }
    },
    {
      "instanceId": "exercise-instance-002",
      "exercisePath": "turns/u-turn-left.json",
      "position": {
        "x": 10.0,
        "y": 10.0
      },
      "rotationDeg": 90.0,
      "scale": {
        "x": 1.0,
        "y": 1.0
      }
    }
  ]
}
```

---

# 7. instanceId

```json
"instanceId": "exercise-instance-001"
```

Уникальный стабильный идентификатор экземпляра внутри Track Project.

Несколько экземпляров могут ссылаться на один и тот же Exercise Definition.

Например, один и тот же створ может быть размещён в трассе несколько раз.

---

# 8. exercisePath

```json
"exercisePath": "slaloms/slalom-5.json"
```

Путь:

* относительный к `res://exercises/`;
* не содержит абсолютный путь;
* не содержит `res://exercises/` в самом значении;
* не должен выходить за пределы Exercise Library.

Допустимо:

```text
slaloms/slalom-5.json
basic/gates/start-gate.json
custom/my-element.json
```

Недопустимо:

```text
C:\Projects\MotoGymkhana\exercises\slalom.json
../../other-file.json
res://exercises/slaloms/slalom.json
```

При загрузке Track Editor разрешает путь как:

```text
res://exercises/ + exercisePath
```

---

# 9. position

```json
{
  "position": {
    "x": 10.0,
    "y": -20.0
  }
}
```

Позиция локального origin Exercise Definition в координатах площадки.

Единица:

```text
meter
```

---

# 10. rotationDeg

```json
"rotationDeg": 90.0
```

Поворот экземпляра в градусах.

Положительное направление должно быть единообразно определено реализацией Track Editor и geometry mapper.

Рекомендуется положительный поворот против часовой стрелки в доменной двумерной системе координат.

Editor должен нормализовать отображаемое значение при необходимости, но сохранённое значение не обязано находиться строго в диапазоне `0..360`.

---

# 11. scale

```json
{
  "scale": {
    "x": 1.0,
    "y": 1.0
  }
}
```

Независимый масштаб локальных координат Exercise Definition.

Требования:

```text
scale.x != 0
scale.y != 0
```

Масштабируются:

* позиции конусов;
* trajectory anchors;
* Bezier control points;
* marking points;
* entryPoint;
* exitPoint;
* bounds.

Не масштабируются:

* физический размер конусов;
* widthMeters markings.

Модуль `scale.x` и `scale.y` задаёт размер по соответствующей локальной оси.
Знак хранит состояние зеркального отражения без добавления отдельной копии
Exercise Definition:

* отрицательный `scale.x` — горизонтальное отражение (лево/право);
* отрицательный `scale.y` — вертикальное отражение (верх/низ).

Нулевой scale запрещён.

---

# 12. Порядок transform

Для каждой локальной геометрической точки:

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

Преобразование должно быть реализовано общей geometry utility и не дублироваться отдельно для:

* cones;
* trajectory;
* markings;
* bounds;
* entry/exit.

---

# 13. Зависимость от Exercise Definition

Track Project хранит ссылку на Exercise Definition, а не его копию.

При открытии проекта Track Editor должен загрузить каждый `exercisePath`.

Если файл отсутствует или повреждён:

* проект не должен аварийно завершать загрузку;
* экземпляр помечается как unresolved;
* пользователь получает diagnostic warning;
* остальные валидные экземпляры продолжают отображаться.

Unresolved instance должен сохранять:

* instanceId;
* exercisePath;
* transform;
* место в порядке прохождения.

Track Editor не должен молча удалять unresolved instance.

---

# 14. Изменения Exercise Definition

Поскольку Track Project содержит ссылку, изменение Exercise Definition может изменить внешний вид уже существующего проекта трассы.

На первом этапе это допустимое и ожидаемое поведение.

Exported Track JSON решает проблему стабильности, создавая самодостаточный snapshot.

Перед экспортом Track Editor должен использовать актуальные Exercise Definition.

Версионирование и закрепление конкретной версии Exercise Definition могут быть добавлены позднее при реальной необходимости.

---

# Derived geometry and export

# Transition overrides

## 1. Назначение

Track Project formatVersion 2 может сохранять ручную коррекцию переходной trajectory между соседними ExerciseInstance.

По умолчанию Track Editor автоматически создаёт переходный `cubicBezier` между:

```text
ExitPoint предыдущего instance
        ↓
EntryPoint следующего instance
```

Если автоматически построенная кривая неудобна, пользователь может изменить её управляющие точки.

Такая коррекция сохраняется как:

```text
TransitionOverride
```

---

## 2. Основной принцип

Track Project не сохраняет все автоматически вычисленные переходы.

В `transitionOverrides[]` находятся только переходы, которые пользователь изменил вручную.

```text
automatic transition
        ↓
не сохраняется

manually adjusted transition
        ↓
сохраняется как TransitionOverride
```

Это предотвращает дублирование полностью производных данных.

---

## 3. Каноническая структура

```json
{
  "formatVersion": 2,

  "track": {
    "id": "training-track",
    "name": "Training Track"
  },

  "area": {
    "width": 60.0,
    "length": 100.0
  },

  "instances": [],

  "transitionOverrides": []
}
```

Поле:

```text
transitionOverrides
```

является обязательным в Track Project formatVersion 2.

При отсутствии ручных изменений оно содержит пустой массив.

---

## 4. TransitionOverride structure

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

Поля:

* `transitionId`;
* `fromInstanceId`;
* `toInstanceId`;
* `control1Offset`;
* `control2Offset`.

---

## 5. Identity

TransitionOverride относится к упорядоченной паре соседних instances:

```text
fromInstanceId → toInstanceId
```

Рекомендуемый идентификатор:

```text
transition--{fromInstanceId}--{toInstanceId}
```

Например:

```text
transition--exercise-instance-001--exercise-instance-002
```

`transitionId` должен соответствовать паре instances.

Однако источником связи являются отдельные поля:

* `fromInstanceId`;
* `toInstanceId`.

Track Editor должен валидировать их согласованность.

---

## 6. Почему сохраняются offsets

Track Project сохраняет не абсолютные мировые позиции управляющих точек, а смещения относительно endpoints перехода.

Для перехода:

```text
P0 = world ExitPoint fromInstance
P3 = world EntryPoint toInstance
```

ручные точки восстанавливаются как:

```text
P1 = P0 + control1Offset
P2 = P3 + control2Offset
```

Это позволяет переходу сохранять связь с упражнениями при перемещении instances.

Если пользователь перемещает оба упражнения, transition следует за ними.

---

## 7. Coordinate system

`control1Offset` и `control2Offset` хранятся в мировой двумерной системе Track Project:

```text
track X / Y
1 unit = 1 meter
```

Они не являются:

* экранными координатами;
* локальными координатами Exercise Definition;
* координатами относительно rotation instance.

---

## 8. Automatic and overridden transition

Для соседней пары Track Editor действует следующим образом:

```text
есть валидный TransitionOverride
        ↓
использовать сохранённые offsets

override отсутствует
        ↓
построить automatic transition
```

Endpoints всегда вычисляются из актуальных ExerciseInstance:

```text
P0 = current world ExitPoint fromInstance
P3 = current world EntryPoint toInstance
```

Override изменяет только:

* `P1`;
* `P2`.

Он не сохраняет и не заменяет endpoints.

---

## 9. Создание override

При первом ручном изменении автоматически рассчитанного перехода:

1. Track Editor берёт текущие automatic `P1` и `P2`;
2. вычисляет:

```text
control1Offset = P1 - P0
control2Offset = P2 - P3
```

3. создаёт TransitionOverride;
4. применяет пользовательское изменение;
5. устанавливает dirty state.

---

## 10. Reset to automatic

Команда:

```text
Reset Transition to Automatic
```

удаляет соответствующий TransitionOverride.

После удаления Track Editor:

1. повторно вычисляет automatic transition;
2. обновляет preview;
3. устанавливает dirty state.

В Track Project больше не остаётся записи для этой пары.

---

## 11. Reorder

Override действителен только пока instances являются соседними в указанном порядке:

```text
fromInstanceId → toInstanceId
```

Если Route Order изменился и пара больше не соседняя:

* override не применяется;
* запись не должна автоматически применяться к другой паре;
* Track Editor должен пометить её как orphaned.

Рекомендуемое поведение Iteration 3:

* сохранить orphaned override в Track Project;
* показать warning;
* предоставить возможность удалить его;
* не экспортировать его как transition.

Не следует молча удалять пользовательскую ручную работу.

---

## 12. Reversed order

Override:

```text
A → B
```

не является override для:

```text
B → A
```

Изменение порядка создаёт другую ориентированную пару.

Track Editor не должен:

* переставлять control points автоматически;
* отражать старый override;
* считать направления эквивалентными.

---

## 13. Deleted instance

Если один из связанных instances удалён:

* override становится orphaned;
* он не применяется;
* Track Editor показывает warning.

Перед окончательным удалением instance рекомендуется предложить удалить связанные overrides.

Допустим и более простой безопасный вариант:

* удалить instance;
* сохранить orphaned overrides;
* предоставить отдельную очистку.

Главное требование — не применять override к неверной паре.

---

## 14. Duplicate overrides

Для одной ориентированной пары допускается максимум один TransitionOverride.

Недопустимо:

```text
A → B override 1
A → B override 2
```

При загрузке duplicate entries являются validation error Track Project.

---

## 15. Validation

Track Editor должен проверять:

* уникальный `transitionId`;
* уникальную пару `fromInstanceId/toInstanceId`;
* существование связанных instances;
* соседство пары;
* правильный порядок пары;
* конечность offset coordinates;
* отсутствие NaN и Infinity.

Orphaned override не делает Track Project полностью неоткрываемым.

Он создаёт warning.

Экспорт использует только применимые overrides.

---

## 16. Migration version 1 → version 2

Track Project version 1 не содержит `transitionOverrides`.

При загрузке version 1 Track Editor создаёт in-memory:

```json
{
  "transitionOverrides": []
}
```

Файл не перезаписывается автоматически.

После явного Save он сохраняется как:

```text
formatVersion = 2
```

---

## 17. Что не сохраняется

Даже в version 2 Track Project не сохраняет:

* автоматически вычисленные transitions;
* transition endpoints;
* Bezier rendering samples;
* selected transition;
* selected control handle;
* transition preview visibility;
* validation warnings;
* transformed Exercise geometry.

Сохраняются только ручные offsets.


## 1. Track Project не содержит готовую мировую geometry

Track Project formatVersion 1 хранит только:

* metadata;
* area;
* упорядоченные instances;
* ссылки на Exercise Definition;
* instance transforms.

Следующие данные вычисляются Track Editor:

* мировые позиции конусов;
* мировые marking points;
* мировые trajectory segments;
* entry/exit;
* tangents;
* переходные cubicBezier;
* global trajectory;
* exported IDs.

Они не сериализуются в Track Project.

---

## 2. Automatic transitions

Между соседними элементами Route Order Track Editor автоматически создаёт переходный `cubicBezier`.

Например:

```text
instances[0] → instances[1]
instances[1] → instances[2]
```

Переходы вычисляются по:

* world ExitPoint предыдущего instance;
* world EntryPoint следующего instance;
* world exit tangent предыдущего instance;
* world entry tangent следующего instance.

Track Project version 1 не содержит:

```text
transitions[]
```

или:

```text
connectors[]
```

Также он не содержит автоматически рассчитанные control points.

---

## 3. Причина отсутствия transitions в проекте

Автоматический transition полностью определяется:

* Route Order;
* Exercise Definition;
* position;
* rotation;
* scale;
* алгоритмом Track Editor.

Поэтому в Iteration 2 он является производным значением.

Сохранение одновременно:

* instance transforms;
* calculated transitions;

создало бы два потенциально рассинхронизированных источника данных.

---

## 4. Пересчёт transitions

Track Editor пересчитывает переходы после:

* открытия проекта;
* добавления instance;
* удаления instance;
* изменения position;
* изменения rotation;
* изменения scale;
* reorder;
* обновления Exercise Definition;
* успешного повторного разрешения unresolved instance.

---

## 5. Export

Команда:

```text
Export for Viewer
```

не изменяет Track Project formatVersion 1.

Она компилирует текущий проект в отдельный Exported Track JSON согласно:

```text
docs/TrackFormat.md
```

Track Project и exported snapshot сохраняются как разные файлы.

---

## 6. Export path не хранится

Путь последнего exported Track JSON является UI/editor state.

Он не сохраняется внутри Track Project JSON.

Track Project также не хранит:

* дату последнего export;
* validation result;
* export status;
* Viewer path.

Такие поля могут быть добавлены в editor metadata позднее, но не входят в version 1.

---

## 7. Unresolved instances и export

Track Project может содержать unresolved instance и оставаться валидным редактируемым проектом.

Такой проект:

* можно открыть;
* можно изменить;
* можно повторно сохранить.

Но export для Viewer блокируется, пока все участвующие в Route Order instances не будут успешно разрешены.

---

## 8. Empty project

Пустой Track Project является валидным проектным файлом:

```json
{
  "formatVersion": 1,
  "track": {
    "id": "new-track",
    "name": "New Track"
  },
  "area": {
    "width": 60.0,
    "length": 100.0
  },
  "instances": []
}
```

При этом Track Editor может блокировать экспорт пустого проекта.

Проектная валидность и экспортная готовность являются разными понятиями.

---

# Что не входит в formatVersion 1

Track Project version 1 не содержит:

* копий geometry Exercise Definition;
* transformed world geometry;
* global trajectory;
* automatic transition segments;
* transition control points;
* transition overrides;
* exported object IDs;
* export validation result;
* export filename;
* last export path;
* checkpoints runtime state;
* Viewer settings;
* camera position;
* zoom;
* pan;
* selected instance;
* expanded library folders;
* editor overlays;
* environment objects;
* thumbnails.

UI state и derived geometry не сериализуются.


# 15. Что не входит в formatVersion 1

Track Project version 1 не содержит:

* копий геометрии Exercise Definition;
* сгенерированной глобальной trajectory;
* transition splines;
* transition overrides;
* checkpoints runtime state;
* Viewer settings;
* camera position;
* zoom и pan редактора;
* selected instance;
* expanded library folders;
* environment objects;
* thumbnails.

UI state не сериализуется в Track Project.

---

# 16. Корневая папка проектов

Рекомендуемый каталог:

```text
res://tracks/
```

Track Editor должен поддерживать пользовательские подпапки.

Примеры:

```text
res://tracks/training/
res://tracks/competitions/
res://tracks/custom/
```

Значение пути текущего Track Project является состоянием Editor и не хранится внутри самого JSON.

---

# 17. Валидация

Track Editor должен проверять:

* поддерживаемый `formatVersion`;
* непустой `track.id`;
* непустой `track.name`;
* `area.width > 0`;
* `area.length > 0`;
* уникальность `instanceId`;
* безопасный относительный `exercisePath`;
* `scale.x != 0`;
* `scale.y != 0`;
* возможность загрузить Exercise Definition.

Ошибка одного Exercise Definition не должна уничтожать весь Track Project.
