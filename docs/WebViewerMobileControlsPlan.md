# Web Viewer Mobile Controls Implementation Plan

# Web Viewer Mobile Controls Iteration 1

## 1. Цель

Добавить в Godot GDScript Web Viewer полноценное сенсорное управление для телефонов и планшетов.

После итерации пользователь должен иметь возможность:

* двигаться по площадке с помощью виртуального джойстика;
* одновременно поворачивать камеру вторым пальцем;
* переключаться между Walk и Fly;
* подниматься и опускаться в Fly mode;
* сбрасывать позицию;
* использовать Viewer в landscape и portrait;
* не опускать камеру ниже допустимой высоты поверхности в Fly mode.

Desktop keyboard/mouse управление должно сохраниться без изменений.

---

# 2. Scope

Итерация относится только к:

```text
web-viewer/
```

Основной C# Viewer можно позднее привести к тому же поведению Fly height constraint, но это не является обязательной частью текущего Web Viewer этапа.

Не изменять:

* Exported Track formatVersion 4;
* Venue Definition formatVersion 1;
* Track Project formatVersion 3;
* Exercise Definition formatVersion 2.

---

# 3. Touch capability detection

Основной признак:

```gdscript
DisplayServer.is_touchscreen_available()
```

Если touch input доступен:

* Mobile Controls отображаются автоматически;
* desktop help заменяется mobile help либо адаптируется;
* mouse capture не требуется для touch look.

Touch capability не должна трактоваться строго как «телефон».

Сенсорный ноутбук также может вернуть `true`.

Поэтому добавить ручное действие:

```text
Toggle Touch Controls
```

Пользователь должен иметь возможность:

* показать сенсорные controls вручную;
* скрыть их вручную;
* повторно включить без перезапуска Viewer.

---

# 4. Mobile Controls hierarchy

Рекомендуемая структура:

```text
MobileControlsLayer
├─ MovementJoystick
│  ├─ Base
│  └─ Knob
├─ LookArea
├─ TopButtons
│  ├─ ModeButton
│  ├─ ResetButton
│  └─ FullscreenButton
├─ FlyButtons
│  ├─ FlyUpButton
│  └─ FlyDownButton
├─ OrientationHint
└─ MobileHelp
```

Корневой UI должен находиться в:

```text
CanvasLayer
```

и не зависеть от 3D camera transform.

---

# 5. Input architecture

Сенсорный UI не должен реализовывать отдельную физику движения.

Он только формирует унифицированное состояние input:

```text
ViewerInputState
├─ movement: Vector2
├─ look_delta: Vector2
├─ fly_vertical: float
├─ fast_move: bool
└─ reset_requested: bool
```

Существующий ViewerCharacter использует общий input state для:

* keyboard;
* mouse;
* touch.

Результирующее movement state формируется объединением источников.

Не создавать отдельный MobileCharacterController.

---

# 6. Movement joystick

Левый виртуальный джойстик управляет:

```text
movement.x = strafe
movement.y = forward/backward
```

Диапазон:

```text
-1.0 ... 1.0
```

Направления:

```text
joystick up    → forward
joystick down  → backward
joystick left  → strafe left
joystick right → strafe right
```

Поддерживать:

* диагональное движение;
* аналоговую величину;
* dead zone;
* возврат knob в центр после отпускания;
* перестановку пальца внутри joystick area;
* корректную работу при resize.

Рекомендуемая dead zone:

```text
0.10–0.20
```

Длина результирующего movement vector не должна превышать `1.0`.

---

# 7. Joystick touch ownership

Joystick захватывает только один touch ID:

```text
movement_touch_id
```

После захвата:

* события этого пальца обрабатываются joystick;
* этот палец не должен вращать камеру;
* второй палец может использовать LookArea;
* отпускание другого пальца не сбрасывает joystick.

При отпускании movement finger:

```text
movement = Vector2.ZERO
movement_touch_id = -1
```

---

# 8. Look area

Правая часть экрана используется для вращения камеры свайпом.

LookArea:

* не обязана иметь видимый фон;
* должна исключать зоны кнопок;
* использует `InputEventScreenTouch`;
* использует `InputEventScreenDrag`;
* захватывает один touch ID.

Хранить:

```text
look_touch_id
```

Горизонтальный drag изменяет:

```text
yaw
```

Вертикальный drag изменяет:

```text
pitch
```

Pitch использует те же ограничения, что desktop mouse look.

Touch look sensitivity должна быть отдельной настройкой.

---

# 9. Multitouch

Обязательный сценарий:

```text
левый палец:
движение

правый палец:
поворот камеры
```

Оба действия должны работать одновременно.

Нельзя использовать только глобальную позицию последнего touch.

Каждый `InputEventScreenTouch` и `InputEventScreenDrag` должен маршрутизироваться по его `index`.

Touch ID, принадлежащий кнопке, не должен одновременно назначаться LookArea.

---

# 10. Walk/Fly button

Кнопка:

```text
Walk / Fly
```

вызывает существующую операцию переключения режима ViewerCharacter.

Текст или icon обновляется после переключения.

Пример:

```text
Walk
Fly
```

либо:

```text
Режим: ходьба
Режим: полёт
```

Кнопка не должна напрямую менять transform character.

---

# 11. Fly vertical controls

В Fly mode показывать:

```text
Fly Up
Fly Down
```

Кнопки работают по удержанию.

Состояние:

```text
fly_vertical = 1.0   # Up
fly_vertical = -1.0  # Down
fly_vertical = 0.0   # released
```

При одновременном нажатии Up и Down результат:

```text
0.0
```

В Walk mode кнопки скрыты и `fly_vertical` принудительно сбрасывается.

---

# 12. Fly minimum camera height

Fly mode не должен позволять опустить camera eye ниже высоты Walk mode относительно поверхности под ней.

Ограничение относится к положению камеры, а не обязательно к origin CharacterBody3D.

Концептуально:

```text
surface height
+
Walk eye height
=
minimum Fly camera height
```

---

# 13. Surface-relative floor

Минимальная Fly height определяется с помощью downward physics query.

Для текущей horizontal position камеры:

```text
camera X/Z
    ↓
ray downward
    ↓
nearest WalkableSurface
    ↓
minimum camera Y
```

Query должен использовать тот же physics layer:

```text
WalkableSurface
```

который используется SurfaceProjectionService.

Ray не должен учитывать:

* Track visuals;
* trajectory;
* markings;
* arrows;
* cones;
* стены;
* забор;
* прочие WorldObstacle-only surfaces.

---

# 14. Fly height formula

После surface hit:

```text
minimumCameraY =
    surfaceHit.position.y
    + walkEyeHeight
    + flyFloorClearance
```

Рекомендуемое начальное значение:

```text
flyFloorClearance = 0.02–0.05 m
```

Если рассчитанная после movement позиция камеры ниже:

```text
cameraY = minimumCameraY
```

Вертикальная скорость вниз сбрасывается:

```text
fly_vertical_velocity = 0
```

если она существует.

---

# 15. Character origin versus camera position

ViewerCharacter может иметь структуру:

```text
CharacterBody3D
└─ Head
   └─ Camera3D
```

Поэтому нельзя приравнивать:

```text
CharacterBody3D.GlobalPosition.Y
```

к высоте глаз.

Нужно учитывать локальную высоту Camera3D относительно character root.

Допустимые подходы:

## Подход A

Ограничивать фактическую:

```text
Camera3D.GlobalPosition.Y
```

и корректировать root на разницу.

## Подход B

Рассчитать минимальный root Y:

```text
minimumRootY =
    surfaceY
    + requiredCameraHeight
    - cameraLocalHeight
```

Выбрать один централизованный подход.

Не дублировать разные eye-height constants для Walk и Fly.

Источник истины:

```text
walkEyeHeight
```

из ViewerCharacter settings или фактической camera hierarchy.

---

# 16. Surface query timing

Fly floor query выполняется в physics processing, когда physics space доступен.

Не выполнять `direct_space_state` query из неподходящего потока.

Query выполняется:

* после расчёта предполагаемого Fly movement;
* перед окончательным применением позиции;
* либо сразу после движения с последующей коррекцией.

Предпочтительно предотвращать проникновение, а не регулярно телепортировать character обратно из-под пола.

---

# 17. Missing surface fallback

Если downward ray не обнаружил WalkableSurface:

использовать базовую высоту Venue:

```text
Venue base Y = 0
```

Тогда:

```text
minimumCameraY =
    walkEyeHeight
    + flyFloorClearance
```

Viewer не должен разрешать уход ниже основной площадки из-за временного отсутствия ray hit.

Однако отсутствие hit не должно блокировать горизонтальное движение.

Diagnostic warning:

* группировать;
* не писать каждый physics frame;
* сбрасывать после обнаружения valid surface.

---

# 18. Surface transitions

При горизонтальном полёте над:

* асфальтом;
* ramp;
* верхней платформой;

минимальная высота должна следовать высоте поверхности.

Если camera уже находится выше минимума:

* не менять её высоту;
* не прилипать к поверхности;
* не выполнять автоматический подъём без необходимости.

Если новая поверхность под камерой выше текущей допустимой позиции:

* поднять camera до нового minimum;
* не позволить проникнуть внутрь ramp/platform.

Таким образом Fly mode сохраняется свободным, но имеет нижнюю границу.

---

# 19. Descending over an edge

При полёте с эстакады за её край:

* minimum height может уменьшиться до уровня площадки;
* camera не должна мгновенно падать вниз;
* текущая высота сохраняется;
* пользователь может опуститься вручную до нового minimum.

Ограничение является clamp снизу, а не ground-following controller.

---

# 20. Obstacles in Fly mode

Iteration 1 ограничивает только минимальную высоту относительно WalkableSurface.

Она не обязана включать полную collision семантику Fly mode.

Если текущий Fly mode игнорирует WorldObstacle collision:

* это поведение может сохраниться;
* height clamp всё равно применяется.

Не смешивать нижнюю границу с полной Fly collision переработкой.

---

# 21. Reset button

Кнопка:

```text
Reset Position
```

возвращает ViewerCharacter в safe spawn.

Она должна:

1. сбросить movement input;
2. сбросить touch ownership;
3. сбросить Fly vertical input;
4. использовать существующую safe spawn strategy;
5. сохранить или восстановить предусмотренный default mode;
6. не перезагружать Track JSON.

---

# 22. Fullscreen button

В Web Viewer добавить optional fullscreen button.

Если browser запрещает fullscreen:

* не падать;
* показать краткое сообщение;
* продолжить работу в обычном режиме.

Fullscreen request должен выполняться как непосредственная реакция на пользовательское нажатие.

Если реализация fullscreen создаёт проблемы в используемой версии Godot Web, кнопку можно оставить скрытой с documented limitation.

---

# 23. Orientation

Основной мобильный layout:

```text
landscape
```

В portrait:

* Viewer продолжает работать;
* controls адаптируются;
* показывается ненавязчивая подсказка:
  `Поверните устройство горизонтально для удобного управления`.

Не блокировать Viewer полностью.

Orientation hint скрывается:

* после перехода в landscape;
* либо после ручного закрытия.

---

# 24. Responsive layout

Использовать:

* anchors;
* containers;
* offsets относительно viewport edges;
* logical UI sizes.

Не использовать фиксированные координаты под один телефон.

Рекомендуемые ориентиры:

```text
joystick diameter: 140–190 logical pixels
button size:       56–72 logical pixels
edge margin:       16–24 logical pixels
```

Учитывать:

* viewport resize;
* browser address bar;
* device rotation;
* safe-area margins, насколько они доступны текущему Godot Web export.

---

# 25. Desktop preservation

Если touch controls скрыты:

* keyboard input работает;
* mouse look работает;
* desktop controls help работает;
* mobile UI не перехватывает мышь;
* desktop layout не получает пустые отступы.

Для отладки разрешить включить touch controls на desktop вручную.

Если включена ProjectSettings emulation touch from mouse, не допускать двойной обработки одного mouse event как mouse look и touch look одновременно.

---

# 26. Focus loss

При потере browser focus или visibility:

сбросить:

```text
movement_touch_id
look_touch_id
movement vector
look delta
fly up
fly down
```

После возврата на вкладку пользователь должен повторно коснуться controls.

Character не должен продолжать двигаться после ухода со страницы.

---

# 27. Touch cancellation

Обрабатывать:

* обычное отпускание пальца;
* потерю focus;
* Control visibility change;
* Viewer reload;
* mode switch;
* reset;
* orientation/layout reconstruction.

Любой из этих сценариев должен освобождать устаревшие touch IDs.

---

# 28. UI input isolation

Нажатие:

* Mode;
* Up;
* Down;
* Reset;
* Fullscreen;

не должно:

* вращать камеру;
* двигать joystick;
* проходить в 3D Viewer как look touch.

Использовать корректные `mouse_filter` и touch ownership rules.

---

# 29. Performance

Touch processing выполняется по событиям.

Не требуется создавать virtual `InputEventAction` каждый frame, если текущая архитектура может читать единый `ViewerInputState`.

Fly floor ray выполняется максимум один раз за physics frame.

Не выполнять ray отдельно для:

* root;
* camera;
* head;

если достаточно одного централизованного query.

---

# 30. Accessibility and visibility

Mobile controls должны быть читаемы поверх:

* светлого асфальта;
* тёмной панорамы;
* ярких объектов.

Использовать:

* полупрозрачный фон;
* чёткий контур;
* крупные touch targets;
* достаточную контрастность.

Не делать controls полностью непрозрачными: они не должны закрывать значительную часть трассы.

---

# 31. Не реализовывать

В этой итерации не добавлять:

* гироскоп;
* accelerometer steering;
* pinch zoom;
* мобильный Track Editor;
* мобильный Exercise Editor;
* мобильный Venue Editor;
* переназначение touch layout;
* сохранение touch settings;
* haptic feedback;
* multitouch gestures кроме joystick/look/buttons;
* motorcycle controller;
* jump;
* crouch;
* free vertical movement в Walk mode;
* полную Fly collision переработку;
* backend;
* авторизацию.

---

# 32. Definition of Done

Итерация завершена, если:

* touch capability определяется;
* mobile controls отображаются автоматически;
* controls можно показать/скрыть вручную;
* joystick поддерживает аналоговое движение;
* joystick имеет dead zone;
* movement и look работают одновременно;
* touch IDs отслеживаются независимо;
* camera yaw работает;
* camera pitch работает;
* кнопки не передают touch в LookArea;
* Walk/Fly переключается;
* Fly Up работает;
* Fly Down работает;
* Fly buttons скрыты в Walk;
* Reset Position работает;
* focus loss сбрасывает input;
* orientation change не ломает controls;
* portrait hint работает;
* landscape layout работает;
* desktop keyboard/mouse сохранены;
* Fly camera не опускается ниже Walk eye height над асфальтом;
* Fly camera не опускается ниже Walk eye height над ramp;
* Fly camera не опускается ниже Walk eye height над верхней платформой;
* переход с эстакады не заставляет camera автоматически падать;
* missing surface использует Venue base fallback;
* Viewer собирается для Web;
* опубликованный GitHub Pages Viewer управляется на телефоне.
