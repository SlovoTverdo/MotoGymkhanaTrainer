# Exercise Definition JSON Format

Этот документ определяет формат переиспользуемого элемента мотоджимхана-трассы.

`ExerciseDefinition` создаётся в Exercise Editor и позднее используется Track Editor для сборки полноценной трассы.

Это **не экспортный формат для Viewer**.

Разделение форматов:

```text
Exercise Definition JSON
        ↓
    Track Editor
        ↓
Exported Track JSON
        ↓
      Viewer
```

Актуальный экспортный формат трассы описан отдельно:

```text
docs/TrackFormat.md
```

---

# 1. Основные принципы

`ExerciseDefinition` описывает одно упражнение в собственной локальной двумерной системе координат.

Примеры упражнений:

* створ;
* змейка;
* круг;
* восьмёрка;
* разворот;
* стоп-бокс;
* коридор;
* произвольный пользовательский элемент.

Все геометрические данные элемента задаются относительно локального origin.

Рекомендуемое расположение origin:

```text
геометрический центр bounds
```

Единица измерения:

```text
1 unit = 1 meter
```

Координаты:

```text
local X / Y
```

При последующем размещении элемента на трассе Track Editor применяет:

```text
local point
    ↓
scale X/Y
    ↓
rotation
    ↓
translation
    ↓
world point
```

---

# 2. Версия формата

Текущая версия Exercise Definition:

```json
{
  "formatVersion": 3
}
```

`formatVersion` является обязательным полем.

Версия Exercise Definition не обязана совпадать с версией exported Track JSON, поскольку это разные контракты данных.

---

# 3. Каноническая структура

```json
{
  "formatVersion": 3,

  "exercise": {
    "id": "slalom-5",
    "name": "Змейка, 5 конусов",
    "version": 1
  },

  "bounds": {
    "width": 6.0,
    "length": 18.0
  },

  "cones": [],

  "markings": [],

  "entryPoint": {
    "x": 0.0,
    "y": -9.0
  },

  "exitPoint": {
    "x": 0.0,
    "y": 9.0
  },

  "trajectory": {
    "segments": []
  },

  "checkpoints": []
}
```

---

# 4. exercise

```json
{
  "exercise": {
    "id": "slalom-5",
    "name": "Змейка, 5 конусов",
    "version": 1
  }
}
```

Поля:

## id

Стабильный идентификатор определения упражнения.

Требования:

* не должен зависеть от отображаемого имени;
* должен быть уникальным внутри библиотеки элементов;
* рекомендуется использовать латинские буквы, цифры и дефисы;
* не должен изменяться при обычном редактировании элемента.

Примеры:

```text
slalom-5
figure-eight-standard
stop-box-small
left-circle
```

## name

Отображаемое имя элемента.

Может быть изменено без изменения `id`.

## version

Версия конкретного определения упражнения.

Она увеличивается при существенном изменении геометрии шаблона.

Это поле предназначено для будущего Track Editor и диагностики зависимостей.

В первой версии редактора автоматическое управление версиями не обязательно.

---

# 5. Локальная система координат

Все координаты элемента локальные.

Рекомендуемая ориентация:

```text
X — ширина элемента
Y — длина элемента / основное направление движения
```

Условный вид сверху:

```text
            +Y
             ↑
             │
       -X ← origin → +X
             │
             ↓
            -Y
```

Это соглашение является рекомендуемым, но геометрия конкретного упражнения может иметь произвольное направление движения.

Track Editor не должен предполагать, что вход всегда находится снизу, а выход всегда сверху.

Фактическое направление определяется trajectory.

---

# 6. bounds

```json
{
  "bounds": {
    "width": 6.0,
    "length": 18.0
  }
}
```

Поля задаются в метрах.

`bounds` описывает рабочий прямоугольник элемента относительно origin.

При origin в центре предполагаемые границы:

```text
X: -width / 2 ... +width / 2
Y: -length / 2 ... +length / 2
```

Bounds используются для:

* отображения рабочей области;
* настройки сетки;
* выбора элемента;
* размещения экземпляра на трассе;
* предварительной проверки пересечений;
* масштабирования.

Bounds не являются физической коллизией.

Editor должен предупреждать, если геометрия выходит за bounds, но не обязан запрещать это.

---

# 7. cones

```json
{
  "cones": [
    {
      "id": "cone-001",

      "position": {
        "x": 0.0,
        "y": -4.0
      },

      "color": "red",
      "type": "standard"
    }
  ]
}
```

Поля:

* `id` — уникальный идентификатор конуса внутри упражнения;
* `position` — локальная позиция в метрах;
* `color` — логический цвет;
* `type` — тип физического объекта.

Первоначально достаточно поддержать:

```text
color:
- red
- blue
- yellow
- orange
- none

type:
- standard
```

Дополнительные цвета и типы могут быть добавлены позднее.

Физический размер модели конуса не входит в Exercise Definition и не масштабируется вместе с экземпляром упражнения.

`color: "none"` означает отсутствие дополнительного цветового навигационного
маркера. Сам стандартный дорожный конус остаётся видимым; Viewer не создаёт над
ним цветное навершие.

Масштабируется только локальная позиция конуса.

Массив `cones` может быть пустым.

Exercise Definition без конусов является допустимым, если он содержит валидную trajectory и используется, например, как:

* связующий участок трассы;
* произвольный маршрут;
* петля;
* объезд;
* дополнительный разворот.

Отсутствие cones не делает Exercise Definition невалидным.

Валидность такого элемента определяется прежде всего:

* корректной trajectory;
* согласованными entryPoint и exitPoint;
* определимыми входной и выходной касательными.


---

# 8. markings

## FILE: docs/ExerciseFormat.md — REPLACE VERSION AND MARKINGS SECTION

# Exercise Definition formatVersion 3

## Markings

Exercise marking хранит визуальный стиль и геометрический Path.

```json
{
  "id": "entry-guide",
  "style": "dashed",
  "color": "#FFD000FF",
  "thickness": 0.08,
  "visibleInViewer": true,
  "path": {
    "start": {
      "x": -2.0,
      "y": 0.0
    },
    "segments": [
      {
        "type": "line",
        "end": {
          "x": 0.0,
          "y": 0.0
        }
      },
      {
        "type": "cubicBezier",
        "control1": {
          "x": 1.0,
          "y": 0.0
        },
        "control2": {
          "x": 2.0,
          "y": 2.0
        },
        "end": {
          "x": 4.0,
          "y": 2.0
        }
      }
    ]
  }
}
```

### Supported segment types

```text
line
cubicBezier
```

### Removed v2 geometry

Exercise Definition v3 не использует старый массив `points` как основной контракт marking.

Старые документы необходимо конвертировать до загрузки production application.

### Coordinates

Все точки Path находятся в локальной системе координат Exercise.

При размещении Exercise instance к ним применяются:

* scaleX;
* scaleY;
* rotation;
* translation.

### Thickness

`thickness` задаётся в метрах и не зависит от scale Exercise instance.



## Marking structure

---

## id

`id` — стабильный уникальный идентификатор marking внутри Exercise Definition.

Например:

```text
marking-001
marking-002
```

---
## color

Цвет marking хранится как явное цветовое значение.

Рекомендуемое каноническое представление:

```text
#RRGGBB
```

Например:

```json
"color": "#FFD400"
```

или:

```json
"color": "#FFFFFF"
```

Это предпочтительнее ограниченного enum цветов, поскольку разметка может иметь произвольный реальный цвет.

Поддержка alpha на данном этапе не требуется.

---

## widthMeters

Физическая ширина линии в метрах.

Пример:

```json
"widthMeters": 0.10
```

`widthMeters` не масштабируется вместе с ExerciseInstance.

То есть масштабирование упражнения изменяет:

* положение точек линии;
* расстояния между точками;

но не физическую ширину нанесённой полосы.

Значение должно быть:

```text
> 0
```

---

## style

В текущей версии поддерживаются:

```text
solid
dashed
dotted
```

### solid

Непрерывная линия.

### dashed

Штриховая линия.

### dotted

Точечная линия.

Editor и Viewer должны использовать единое семантическое значение `style`, но конкретный rendering может отличаться в деталях между 2D Editor и 3D Viewer.

Точная длина штрихов и расстояние между ними пока не являются частью Exercise Definition contract.

Используются разумные значения renderer по умолчанию.

Если позже потребуется точное управление шаблоном штрихов, формат можно расширить отдельными параметрами.

---

## visibleInViewer

```json
"visibleInViewer": true
```

Определяет, должна ли линия быть видима пользователю в обычном Viewer.

Значения:

```text
true  — отображать в Viewer
false — не отображать в Viewer
```

В Exercise Editor marking отображается независимо от `visibleInViewer`, поскольку пользователь должен иметь возможность:

* видеть её;
* выбирать;
* редактировать;
* изменять свойство.

Для marking с:

```text
visibleInViewer = false
```

Editor должен использовать визуальный признак скрытого состояния, например:

* уменьшенную прозрачность;
* специальный overlay;
* иконку скрытой видимости.

Не следует полностью скрывать такой marking в Editor.

---

## Сохранение невидимых markings

`visibleInViewer = false` не означает, что marking удаляется при экспорте трассы.

Track Editor должен сохранить его в exported Track JSON вместе с этим свойством.

Viewer самостоятельно решает:

```text
visibleInViewer = true
        ↓
render

visibleInViewer = false
        ↓
do not render
```

Это сохраняет семантическую информацию в snapshot и позволяет в будущем реализовать дополнительные режимы отображения без повторного редактирования Exercise Definition.

---

## Marking validation

Editor должен проверять:

* уникальный `id`;
* известный `type`;
* минимум допустимого количества points;
* валидный цвет;
* `widthMeters > 0`;
* известный `style`.

Неизвестный `style` при загрузке должен:

* создавать warning;
* использовать безопасный fallback `solid`;
* не приводить к потере остальных данных документа.


---

# 9. entryPoint и exitPoint

```json
{
  "entryPoint": {
    "x": 0.0,
    "y": -9.0
  },

  "exitPoint": {
    "x": 0.0,
    "y": 9.0
  }
}
```

`entryPoint` задаёт начало внутренней trajectory упражнения.

`exitPoint` задаёт конец внутренней trajectory упражнения.

Они являются геометрическими точками.

Они не содержат отдельного направления.

Направление входа и выхода вычисляется по касательной trajectory.

---

# 10. Согласованность entryPoint, exitPoint и trajectory

Обязательные правила:

```text
entryPoint = начало первого trajectory segment
exitPoint  = конец последнего trajectory segment
```

Допускается небольшая численная погрешность.

Для `polyline`:

```text
segment start = points[0]
segment end   = points[last]
```

Для `cubicBezier`:

```text
segment start = start
segment end   = end
```

Exercise Editor должен проверять:

1. наличие trajectory;
2. наличие хотя бы одного валидного segment;
3. совпадение `entryPoint` с началом trajectory;
4. совпадение `exitPoint` с концом trajectory;
5. непрерывность соседних segments.

В первой версии редактора допустимо автоматически устанавливать:

```text
entryPoint = первая точка trajectory
exitPoint  = последняя точка trajectory
```

вместо независимого ручного редактирования этих полей.

Это предпочтительнее, поскольку исключает рассинхронизацию.

---

# 11. trajectory

Trajectory является единственным источником геометрии движения внутри упражнения.

Каноническая структура:

```json
{
  "trajectory": {
    "segments": []
  }
}
```

Не допускаются варианты:

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

Порядок `segments[]` является порядком движения.

---

# 12. Общие поля trajectory segment

Каждый segment содержит:

```json
{
  "id": "trajectory-segment-001",
  "type": "polyline"
}
```

Поля:

* `id` — стабильный идентификатор segment внутри упражнения;
* `type` — геометрический тип.

В `formatVersion: 1` определены:

```text
polyline
cubicBezier
```

---

# 13. Polyline trajectory segment

```json
{
  "id": "trajectory-segment-001",

  "type": "polyline",

  "points": [
    {
      "x": 0.0,
      "y": -9.0
    },
    {
      "x": -1.5,
      "y": -5.0
    },
    {
      "x": 1.5,
      "y": -1.0
    },
    {
      "x": -1.5,
      "y": 3.0
    },
    {
      "x": 0.0,
      "y": 9.0
    }
  ]
}
```

Требования:

* минимум две точки;
* первая точка — начало segment;
* последняя точка — конец segment;
* порядок точек соответствует направлению движения.

Polyline является первым обязательным типом для Exercise Editor.

---

# 14. CubicBezier trajectory segment

```json
{
  "id": "trajectory-segment-002",

  "type": "cubicBezier",

  "start": {
    "x": 0.0,
    "y": -4.0
  },

  "control1": {
    "x": 3.0,
    "y": -4.0
  },

  "control2": {
    "x": 3.0,
    "y": 4.0
  },

  "end": {
    "x": 0.0,
    "y": 4.0
  }
}
```

Поля:

* `start`;
* `control1`;
* `control2`;
* `end`.

Exercise Editor не должен требовать ручного ввода координат управляющих точек.

В будущей итерации управляющие точки редактируются визуальными handles.

Для rendering кривая может дискретизироваться.

В JSON она остаётся `cubicBezier`, а не массивом вычисленных точек.

---

# 15. Касательная trajectory

Касательная используется будущим Track Editor для соединения соседних элементов.

## Polyline

Входная касательная:

```text
points[1] - points[0]
```

Выходная касательная:

```text
points[last] - points[last - 1]
```

## CubicBezier

Входная касательная:

```text
control1 - start
```

Выходная касательная:

```text
end - control2
```

Если trajectory содержит несколько segments:

* входная касательная берётся из первого валидного segment;
* выходная касательная берётся из последнего валидного segment.

Отдельные поля направления входа и выхода не хранятся.

---

# 16. Непрерывность segments

Конец предыдущего segment должен совпадать с началом следующего.

Пример:

```text
polyline.end
      ↓
      ● = cubicBezier.start
```

Editor должен диагностировать разрыв.

В первой версии допустима строгая модель, при которой trajectory состоит из одного `polyline`.

Поддержка нескольких segments и `cubicBezier` добавляется последующими итерациями.

---

# 17. checkpoints

```json
{
  "checkpoints": []
}
```

Поле резервируется для будущего режима проверки прохождения.

Первоначальный Exercise Editor не должен реализовывать checkpoints.

Формат отдельного checkpoint будет окончательно зафиксирован перед реализацией соответствующей функциональности.

---

# 18. Пример полного упражнения

```json
{
  "formatVersion": 1,

  "exercise": {
    "id": "slalom-5",
    "name": "Змейка, 5 конусов",
    "version": 1
  },

  "bounds": {
    "width": 6.0,
    "length": 18.0
  },

  "cones": [
    {
      "id": "cone-001",
      "position": {
        "x": 0.0,
        "y": -6.0
      },
      "color": "red",
      "type": "standard"
    },
    {
      "id": "cone-002",
      "position": {
        "x": 0.0,
        "y": -3.0
      },
      "color": "blue",
      "type": "standard"
    },
    {
      "id": "cone-003",
      "position": {
        "x": 0.0,
        "y": 0.0
      },
      "color": "red",
      "type": "standard"
    },
    {
      "id": "cone-004",
      "position": {
        "x": 0.0,
        "y": 3.0
      },
      "color": "blue",
      "type": "standard"
    },
    {
      "id": "cone-005",
      "position": {
        "x": 0.0,
        "y": 6.0
      },
      "color": "red",
      "type": "standard"
    }
  ],

  "markings": [],

  "entryPoint": {
    "x": 0.0,
    "y": -9.0
  },

  "exitPoint": {
    "x": 0.0,
    "y": 9.0
  },

  "trajectory": {
    "segments": [
      {
        "id": "trajectory-segment-001",

        "type": "polyline",

        "points": [
          {
            "x": 0.0,
            "y": -9.0
          },
          {
            "x": -1.5,
            "y": -6.0
          },
          {
            "x": 1.5,
            "y": -3.0
          },
          {
            "x": -1.5,
            "y": 0.0
          },
          {
            "x": 1.5,
            "y": 3.0
          },
          {
            "x": -1.5,
            "y": 6.0
          },
          {
            "x": 0.0,
            "y": 9.0
          }
        ]
      }
    ]
  },

  "checkpoints": []
}
```

---

# 19. Что не входит в Exercise Definition

Exercise Definition не содержит:

* позицию элемента на площадке;
* rotation экземпляра;
* scale экземпляра;
* связи с другими упражнениями;
* переходную trajectory между упражнениями;
* данные окружения площадки;
* мировые координаты;
* настройки Viewer.

Эти данные принадлежат Track Editor или exported Track JSON.

---

# 20. Разделение DTO

Exercise Definition и exported Track должны иметь отдельные корневые DTO.

Допускается повторное использование общих типов:

* `Point2Data`;
* `TrajectoryData`;
* `TrajectorySegmentData`;
* `ConeData`;
* `MarkingData`.

Не следует использовать один корневой DTO для двух разных форматов только из-за частичного совпадения вложенных структур.
