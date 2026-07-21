# Exported Track JSON Format

Это контракт между Editor и Viewer.

Главный принцип: JSON должен быть самодостаточным. Viewer не загружает библиотеку ExerciseDefinition и не должен знать, как исходная трасса собиралась.

## Верхний уровень

```json
{
  "formatVersion": 1,
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
  "trajectory": [],
  "checkpoints": []
}
```

## elements

В snapshot сохраняется metadata экземпляров для диагностики/редакторских целей.

```json
{
  "instanceId": "element-07",
  "definitionId": "slalom-5",
  "position": { "x": 22.4, "y": 31.8 },
  "rotationDeg": 90.0,
  "scale": { "x": 1.25, "y": 0.9 }
}
```

Viewer не обязан использовать `elements` для построения геометрии.

## cones

Все позиции уже мировые.

```json
{
  "id": "cone-001",
  "position": { "x": 10.0, "y": 15.5 },
  "color": "red",
  "type": "standard"
}
```

## markings

```json
{
  "id": "marking-001",
  "type": "polyline",
  "points": [
    { "x": 1.0, "y": 2.0 },
    { "x": 2.0, "y": 3.5 }
  ],
  "color": "yellow",
  "widthMeters": 0.08
}
```

Поддерживаемые типы можно расширять: `line`, `polyline`, `arc`, `polygon`, `arrow`.

## trajectory

Итоговая мировая траектория после объединения внутренних траекторий элементов и connector spline.

```json
{
  "points": [
    { "x": 1.0, "y": 2.0 },
    { "x": 1.5, "y": 2.4 },
    { "x": 2.0, "y": 3.0 }
  ]
}
```

В будущем возможно перейти от дискретных точек к spline segments, сохранив versioning формата.

## checkpoints

Не обязательны для первого viewer, но формат резервируется сразу.

```json
{
  "id": "cp-01",
  "order": 1,
  "center": { "x": 5.0, "y": 10.0 },
  "direction": { "x": 1.0, "y": 0.0 },
  "width": 3.0
}
```

## Координатная система

Доменные данные трассы двумерные: X/Y на плоскости площадки, единица измерения — метр.

Godot Viewer самостоятельно отображает их в своём 3D пространстве (например, domain X/Y -> Godot X/Z).

Это преобразование должно находиться в одном месте кода, а не размазываться по системе.
