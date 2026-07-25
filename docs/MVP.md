# MVP Implementation Plan

## Цель

Создать минимальный Godot .NET/C# Viewer для просмотра трассы мотоджимханы.

Viewer читает self-contained Track JSON.

Актуальный контракт:

```text
docs/TrackFormat.md
```

Текущая версия:

```text
formatVersion = 2
```

---

# Iteration 1 — базовый Viewer

1. Создать Godot .NET проект.
2. Создать основную 3D-сцену.
3. Добавить плоскую площадку заданного размера.
4. Реализовать DTO exported Track JSON версии 2.
5. Реализовать TrackLoader.
6. Загрузить:

```text
examples/courses/basic.json
```

7. Создать конусы по мировым координатам.
8. Реализовать камеру:

   * фиксированная высота;
   * WASD;
   * mouse look;
   * Shift speed multiplier.
9. Запустить проект.
10. Проверить отсутствие ошибок.

---

## Требование к DTO

Даже если trajectory ещё не визуализируется, DTO должны соответствовать:

```text
trajectory
└─ segments[]
```

Не использовать устаревшие:

```text
trajectory: []
```

или:

```text
trajectory:
  points[]
```

DTO должны распознавать:

```text
polyline
cubicBezier
```

Не требуется создавать сложную polymorphic serialization architecture.

Для MVP допустима простая DTO-модель:

```text
TrajectorySegment
├─ Type
├─ Points
├─ Start
├─ Control1
├─ Control2
└─ End
```

с использованием только полей, относящихся к конкретному `type`.

Не создавать framework или abstraction layer только ради двух типов сегментов.

---

# Iteration 1.1 — проверка масштаба

Зафиксировать:

```text
1 Godot unit = 1 meter
```

Проверить:

1. размеры площадки;
2. масштаб модели конуса;
3. высоту камеры;
4. FOV;
5. скорость движения.

Рекомендуемый debug mode:

* grid 1 × 1 метр;
* более заметные линии каждые 5 или 10 метров;
* эталонный объект известного размера.

Debug grid может быть включаемым и не является обязательным production feature.

---

# Iteration 2 — markings и trajectory

1. Отрисовать `markings`.
2. Отрисовать:

```text
trajectory.segments[]
```

3. Поддержать:

```text
polyline
cubicBezier
```

4. Для `cubicBezier`:

   * выполнить дискретизацию только для rendering;
   * не изменять исходную модель JSON.

5. Централизовать преобразование:

```text
domain X/Y
    ↓
Godot X/Z
```

6. Добавить обработку ошибок JSON.

7. Добавить проверку `formatVersion`.

8. Неизвестный trajectory segment type:

   * не должен ломать загрузку всей трассы;
   * segment пропускается;
   * создаётся понятный warning.

9. Проверять визуально и/или логически continuity последовательных segments.

При заметном разрыве:

* warning;
* без автоматического исправления.

---

# Важное ограничение Viewer

Viewer **не должен**:

* соединять элементы упражнений;
* вычислять EntryPoint/ExitPoint;
* вычислять tangent;
* генерировать cubicBezier между элементами;
* знать о transition overrides.

Вся эта логика относится к будущему Editor.

Viewer получает только итоговый:

```text
trajectory.segments[]
```

---

# Editor — разделение ответственности

Track Editor:

1. пользователь размещает ExerciseInstance;
2. задаёт их порядок;
3. применяет rotation;
4. применяет scale X/Y;
5. Editor получает:

   * ExitPoint предыдущего элемента;
   * EntryPoint следующего;
6. Editor вычисляет tangent из trajectory обоих элементов;
7. Editor генерирует промежуточный `cubicBezier`;
8. пользователь при необходимости корректирует control points;
9. Editor экспортирует всё как единый список trajectory segments.

Отдельную сущность Connector не вводить без новой объективной необходимости.

---

# Iteration 3 — загрузка пользовательских трасс

1. Выбор JSON-файла пользователем.
2. Перезагрузка трассы без перезапуска приложения.
3. Минимальный UI:

   * название трассы;
   * show/hide trajectory.

---

# Не реализовывать до отдельного решения

* Editor упражнений.
* ExerciseDefinition library UI.
* Автоматическое построение переходов.
* Ручное редактирование Bezier control points.
* Transition overrides.
* Web export.
* GitHub Pages deployment.
* Панорамное окружение.
* Забор.
* Checkpoint validation.
* Exam mode.
* Автодвижение камеры.
* Физику.
* Мотоцикл.

---

# Definition of Done — Iteration 1

* Проект компилируется.
* Godot запускает сцену.
* `basic.json` версии 2 загружается.
* DTO используют `trajectory.segments[]`.
* Конусы отображаются в ожидаемых координатах.
* Камера управляется WASD + mouse + Shift.
* Масштаб соответствует:

```text
1 unit = 1 meter
```

* Нет runtime errors.
* Код подробно прокомментирован.

---

# Definition of Done — Iteration 2

* Markings отображаются.
* Polyline trajectory отображается.
* CubicBezier trajectory отображается гладко.
* Sample с обоими segment types работает.
* Неизвестный segment type не ломает всю трассу.
* Domain coordinate mapping централизован.
* Ошибки и warnings диагностичны.
* Viewer не содержит логики генерации переходов между упражнениями.
