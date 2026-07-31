# Exported Track JSON Format

## 1. Назначение

Exported Track JSON является self-contained runtime snapshot трассы для Viewer.

Он создаётся Track Editor из:

```text
Venue Definition
+
Exercise Definitions
+
Track Project
```

Viewer не загружает:

* Track Project;
* Venue Definition;
* Exercise Definition.

Viewer получает готовую world geometry и ссылки на необходимые project resources.

---

# 2. Версия

Текущая версия:

```json
{
  "formatVersion": 5
}
```

Предыдущие версии не обязаны поддерживаться.

---

# 3. Каноническая структура

```json
{
  "formatVersion": 4,

  "track": {
    "id": "training-track-01",
    "name": "Тренировка 01"
  },

  "venue": {
    "id": "main-training-ground",
    "name": "Основная тренировочная площадка"
  },

  "area": {
    "width": 60.0,
    "length": 100.0
  },

  "panorama": {
    "enabled": true,
    "texturePath": "res://venues/main-training-ground/assets/panorama.webp",
    "rotationDeg": 125.0,
    "energyMultiplier": 1.0
  },

  "venueObjects": [],

  "elements": [],

  "cones": [],

  "markings": [],

  "trajectory": {
    "segments": []
  },

  "checkpoints": []
}
```

---

# 4. track

Metadata конкретной трассы.

```json
{
  "id": "training-track-01",
  "name": "Тренировка 01"
}
```

---

# 5. venue

Metadata исходной Venue Definition.

```json
{
  "id": "main-training-ground",
  "name": "Основная тренировочная площадка"
}
```

Viewer использует metadata только для отображения и diagnostics.

---

# 6. area

Размер поверхности площадки:

```json
{
  "width": 60.0,
  "length": 100.0
}
```

Единица:

```text
meter
```

Viewer создаёт поверхность площадки с origin в центре.

---

# 7. panorama

```json
{
  "enabled": true,
  "texturePath": "res://venues/main-training-ground/assets/panorama.webp",
  "rotationDeg": 125.0,
  "energyMultiplier": 1.0
}
```

Если `enabled = true`, Viewer создаёт:

```text
PanoramaSkyMaterial
→ Sky
→ Environment
→ WorldEnvironment
```

Панорама не создаётся как физический mesh.

---

# 8. venueObjects

`venueObjects[]` содержит runtime snapshot постоянных 3D-объектов.

Пример:

```json
{
  "id": "venue--object--shed-main",
  "name": "Домик",

  "assetPath": "res://venues/main-training-ground/assets/shed.tscn",

  "position": {
    "x": -22.0,
    "y": 31.0
  },

  "elevation": 0.0,

  "rotationDeg": 90.0,

  "scale": {
    "x": 1.0,
    "y": 1.0,
    "z": 1.0
  },

  "footprint": {
    "width": 6.0,
    "length": 4.0
  },

  "collisionEnabled": true,
  "visibleInViewer": true
}
```

---

# 9. Venue object coordinates

Преобразование в Godot:

```text
position.x → Godot X
elevation  → Godot Y
position.y → Godot Z
```

`rotationDeg` применяется вокруг Godot Y.

`scale` применяется как Godot X/Y/Z.

---

# 10. Venue object IDs

Канонический ID:

```text
venue--object--{objectId}
```

Viewer не должен использовать array index как identity.

---

# 11. Footprint

Footprint является diagnostic metadata.

Viewer не обязан использовать его для collision.

Collision берётся из asset scene.

---

# 12. collisionEnabled

Если `false`, Viewer отключает collision nodes внутри instantiated asset.

Viewer не создаёт collision shapes автоматически.

---

# 13. visibleInViewer

Если `false`, Viewer не создаёт Venue object.

Object остаётся в JSON.

---

# 14. elements

`elements[]` содержит диагностическое описание ExerciseInstance.

Пример:

```json
{
  "instanceId": "exercise-instance-001",
  "definitionId": "slalom-5",
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

Viewer не использует `elements[]` для построения geometry.

---

# 15. cones

Итоговый массив содержит:

* Venue cones;
* Exercise cones.

Venue ID:

```text
venue--cone--{localId}
```

Exercise ID:

```text
{instanceId}--{localId}
```

Все positions находятся в world coordinates.

---

# 16. markings

# Exported Track formatVersion 5

Exported Track v5 поддерживает Path-based markings.

## Venue markings

Venue markings экспортируются в координатах итоговой сцены.

## Exercise markings

Exercise marking Path преобразуется из локальных Exercise coordinates в итоговые Track coordinates.

Affine transform применяется непосредственно к:

* path.start;
* line.end;
* cubicBezier.control1;
* cubicBezier.control2;
* cubicBezier.end.

Тип сегмента сохраняется.

Пример:

```json
{
  "markings": [
    {
      "id": "exercise-instance-12/entry-guide",
      "style": "dashed",
      "color": "#FFD000FF",
      "thickness": 0.08,
      "visibleInViewer": true,
      "path": {
        "start": {
          "x": 14.0,
          "y": 8.0
        },
        "segments": [
          {
            "type": "cubicBezier",
            "control1": {
              "x": 15.0,
              "y": 8.0
            },
            "control2": {
              "x": 16.0,
              "y": 10.0
            },
            "end": {
              "x": 18.0,
              "y": 10.0
            }
          }
        ]
      }
    }
  ]
}
```

Web и desktop Viewer выполняют:

```text
Path validation
→ adaptive sampling
→ cumulative world-space length
→ style generation
→ surface projection
→ rendering
```

Exported Track v5 не содержит старое `points`-представление marking.

## Compatibility

Production Viewer не обязан загружать Exported Track v4 после перехода на v5.

Все sample exports и `default-track.json` должны быть обновлены одновременно.

# 17. trajectory

`trajectory.segments[]` является полной глобальной trajectory трассы.

Она содержит:

* transformed Exercise segments;
* automatic transitions;
* manual override transitions.

Поддерживаемые типы:

```text
polyline
cubicBezier
```

Viewer не пересчитывает transitions.

---

# 18. Transition source transparency

Viewer не различает:

* automatic transition;
* manually overridden transition.

Оба экспортируются как обычный `cubicBezier`.

---

# 19. Exported IDs

Все IDs должны быть уникальны.

Namespaces:

```text
venue--object--...
venue--cone--...
venue--marking--...
{instanceId}--...
transition--{fromInstanceId}--{toInstanceId}
```

Duplicate ID является blocking compile error.

---

# 20. checkpoints

На текущем этапе:

```json
"checkpoints": []
```

Поле сохраняется для будущего расширения.

---

# 21. Resource dependencies

JSON содержит project resource paths:

* `.tscn`;
* panorama texture.

Файл не встраивает binary resources.

Viewer должен запускаться в проекте, содержащем соответствующие assets.

---

# 22. Snapshot independence

После export Viewer не зависит от:

* Track Project;
* Venue Definition;
* Exercise Definition.

Изменение исходных JSON не меняет существующий exported snapshot.

Однако изменение asset по тому же `res://` path влияет на его визуальное представление.

---

# 23. Viewer safety

Если asset отсутствует:

* Viewer пишет error;
* пропускает object;
* продолжает загрузку остальных данных.

Если panorama texture отсутствует:

* Viewer пишет error;
* использует fallback environment;
* продолжает загрузку.

Compile-time validation должна предотвращать такие exports, но Viewer остаётся устойчивым к повреждённым файлам.

---

# 24. Validation before export

## Blocking errors

* invalid Venue;
* unresolved visible Venue object;
* enabled panorama без texture;
* invalid Track;
* unresolved Exercise;
* invalid trajectory;
* undefined tangent;
* non-finite values;
* duplicate exported IDs;
* serialization failure.

## Warnings

* hidden unresolved Venue object;
* Venue object outside area;
* overlapping Venue footprints;
* Exercise bounds outside area;
* Exercise bounds intersect Venue footprint;
* geometry outside area;
* long transition;
* transition outside area.

Warnings не блокируют export.

---

# 25. Viewer reload

При загрузке нового exported Track Viewer очищает:

* предыдущий WorldEnvironment panorama;
* Venue surface;
* Venue objects;
* Venue cones;
* Venue markings;
* Exercise cones;
* Exercise markings;
* trajectory;
* runtime diagnostics.

---

# Runtime surface projection

Exported Track formatVersion 4 хранит двумерную world geometry:

```text
Track X/Y
```

Она не содержит обязательную высоту поверхности.

Viewer преобразует:

```text
Track X/Y
    ↓
Godot X/Z
    ↓
downward surface query
    ↓
Godot X/Y/Z
```

Runtime projection применяется к:

* trajectory samples;
* direction arrows;
* Venue markings;
* Exercise markings;
* Venue cones;
* Exercise cones.

## Projection source

Проекция использует physics collision проходимых поверхностей:

* основную Venue surface;
* ramps;
* platforms;
* другие walkable Venue surfaces.

Стены, заборы и Track visual geometry не должны использоваться как projection surface.

## Persisted independence

Projected Y coordinates:

* не записываются обратно в Exported Track JSON;
* не изменяют Track Project;
* не изменяют Venue Definition;
* не изменяют Exercise Definition.

## Runtime fallback

Если Viewer не обнаружил surface hit:

* он использует безопасную fallback height;
* выводит diagnostic warning;
* продолжает загрузку остальной трассы.

Отсутствие projection hit не должно приводить к падению Viewer.


# 26. Что не входит в formatVersion 4

* встроенные asset bytes;
* встроенные texture bytes;
* Venue Definition JSON;
* Exercise Definition JSON;
* Track Project JSON;
* terrain;
* heightmap;
* weather;
* time of day;
* lighting configuration;
* surface material configuration;
* spawn point;
* motorcycle state;
* gameplay checkpoint state;
* timing results.
