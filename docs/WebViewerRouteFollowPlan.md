# Web Viewer Route Follow Implementation Plan

# Route Follow Iteration 1

## 1. Цель

Добавить в Godot GDScript Web Viewer автоматический режим следования вдоль опубликованной траектории трассы.

Режим предназначен для изучения трассы:

* на desktop без ручного ведения камеры;
* на мобильном устройстве без точного управления маленьким виртуальным джойстиком;
* для последовательного просмотра всех элементов трассы;
* для осмотра направления входа, прохождения и выхода из упражнений.

После итерации Web Viewer имеет три пользовательских режима:

```text
Walk
Fly
Follow
```

Режим Follow не является симуляцией движения мотоцикла.

---

## 2. Scope

Итерация относится только к:

```text
web-viewer/
```

Не изменять:

* основной Godot C#-проект;
* Exercise Definition formatVersion 2;
* Venue Definition formatVersion 1;
* Track Project formatVersion 3;
* Exported Track formatVersion 4.

Follow использует уже загруженную глобальную trajectory из Exported Track v4.

---

## 3. Источник маршрута

Источник маршрута:

```text
track.trajectory
```

либо фактическое поле глобальной trajectory в текущем Track v4 DTO.

Использовать ту же runtime-траекторию, которая применяется для визуального отображения маршрута.

Не выполнять повторную компиляцию:

* Exercise trajectories;
* transitions;
* route order;
* Bezier segments.

Не создавать независимую альтернативную траекторию специально для Follow.

---

## 4. Projected trajectory

Follow должен использовать trajectory после проекции на поверхность Venue.

Последовательность:

```text
Track v4 trajectory
    ↓
sampling
    ↓
surface projection
    ↓
projected 3D route points
    ↓
Route Follow path
```

Follow нельзя запускать до завершения:

* создания Venue collision;
* physics-frame synchronization;
* проекции trajectory;
* проверки projected points.

---

## 5. Минимальные требования к маршруту

Режим Follow доступен, если:

* trajectory существует;
* после sampling имеется минимум две точки;
* после удаления невалидных и последовательных дубликатов остаётся минимум две точки;
* суммарная длина больше минимального порога.

Рекомендуемый порог:

```text
0.1 m
```

Если маршрут недоступен:

* кнопка Follow выключена;
* UI показывает понятное сообщение;
* Viewer продолжает работать в Walk/Fly;
* не возникает необработанной ошибки.

---

## 6. Route path representation

Создать централизованный runtime-объект, например:

```text
RoutePath
```

Он содержит:

```text
points: Array[Vector3]
cumulative_distances: PackedFloat32Array
total_length: float
```

Для каждой точки:

```text
cumulative_distances[0] = 0
```

Для последующих:

```text
cumulative_distances[i] =
    cumulative_distances[i - 1]
    + points[i - 1].distance_to(points[i])
```

Последнее значение является:

```text
total_length
```

---

## 7. Очистка точек

Перед построением cumulative distance удалить:

* non-finite points;
* соседние точки с почти одинаковыми координатами;
* сегменты практически нулевой длины.

Рекомендуемый epsilon:

```text
0.001–0.01 m
```

Не удалять точки настолько агрессивно, чтобы заметно менять геометрию маршрута.

---

## 8. Движение по длине

Источник истины для текущей позиции:

```text
route_distance_meters
```

Не использовать как основное состояние:

* индекс sample point;
* номер segment;
* процент между текущим и следующим индексом.

При воспроизведении:

```text
route_distance_meters +=
    playback_speed_mps
    * speed_multiplier
    * delta
```

Затем значение ограничивается:

```text
0 ... total_length
```

Это обеспечивает постоянную линейную скорость независимо от плотности sampling.

---

## 9. Получение позиции по расстоянию

RoutePath предоставляет операцию:

```text
sample_position(distance_meters)
```

Алгоритм:

1. clamp distance;
2. найти соседние cumulative distances;
3. вычислить локальный коэффициент;
4. линейно интерполировать Vector3 между точками.

Для поиска допустим:

* binary search;
* cached forward index для последовательного playback;
* комбинация cached index и binary search после перемотки.

Не выполнять полный линейный поиск с начала массива каждый frame для длинных маршрутов.

---

## 10. Направление движения

Направление камеры определяется через look-ahead.

```text
current_position =
    sample_position(route_distance)

look_ahead_position =
    sample_position(route_distance + look_ahead_distance)

route_forward =
    look_ahead_position - current_position
```

Рекомендуемое начальное значение:

```text
look_ahead_distance = 1.0 m
```

Допустимый диапазон настройки:

```text
0.5–2.0 m
```

Если до конца маршрута осталось меньше look-ahead:

* использовать конечную точку;
* если vector слишком мал, использовать последнее валидное route forward;
* не создавать резкий поворот на нулевом векторе.

---

## 11. Положение камеры

Базовая target position:

```text
route_position
+
Vector3.UP * follow_eye_height
```

Follow eye height должен использовать общий источник высоты глаз ViewerCharacter, если это соответствует текущей hierarchy.

Не создавать расходящиеся магические константы:

```text
walk_eye_height
follow_eye_height
```

без явной причины.

Предпочтительно:

```text
follow_eye_height = walk_eye_height
```

или небольшая настраиваемая поправка к нему.

Trajectory visual offset нельзя считать высотой глаз.

Если projected trajectory уже имеет небольшой normal offset для предотвращения z-fighting, этот offset остаётся частью route position, но не заменяет eye height.

---

## 12. Вертикальная плавность

Позиция камеры должна корректно повторять:

* асфальт;
* ramp;
* верхнюю поверхность эстакады;
* спуск.

Не использовать сильное vertical smoothing, вызывающее проникновение камеры в ramp.

Если применяется сглаживание:

* оно должно быть frame-rate independent;
* иметь ограниченную задержку;
* не нарушать высоту над поверхностью;
* не опускать камеру ниже route position плюс eye height.

Для Iteration 1 допустимо не сглаживать позицию по вертикали, если projected trajectory достаточно плавная.

---

## 13. Режим Follow

Добавить отдельное runtime-состояние:

```text
ViewerMode.FOLLOW
```

или эквивалентное централизованное состояние.

Не моделировать Follow как разновидность Fly с набором специальных flags.

Режимы должны быть взаимоисключающими:

```text
Walk
Fly
Follow
```

---

## 14. Вход в Follow

Основная команда:

```text
Follow From Start
```

При входе:

1. проверить RoutePath;
2. сохранить предыдущее пользовательское состояние, если требуется;
3. сбросить Walk/Fly movement input;
4. сбросить Fly vertical input;
5. сбросить touch joystick;
6. сбросить активные movement touch IDs;
7. установить route distance в `0`;
8. установить Follow camera pose;
9. включить playback либо начать с паузы согласно принятой UX-политике.

Рекомендуемая политика первой версии:

```text
войти в Follow с начала и сразу начать воспроизведение
```

---

## 15. Выход из Follow

При выходе:

* остановить playback;
* сохранить текущую позицию маршрута только в Follow runtime state;
* разместить ViewerCharacter в текущей Follow position;
* переключить в Walk либо Fly;
* не возвращать автоматически в позицию до запуска Follow;
* очистить Follow-only input;
* восстановить соответствующий Walk/Fly UI.

Рекомендуемый default exit mode:

```text
Walk
```

если текущая точка имеет доступную walkable surface.

Если безопасное переключение в Walk невозможно, использовать Fly.

Не допускать появления CharacterBody3D внутри Venue geometry.

---

## 16. Follow camera ownership

В Follow mode маршрут управляет:

* базовой позицией;
* базовым yaw;
* направлением движения.

Пользователь управляет:

* look yaw offset;
* look pitch offset.

Итоговая ориентация:

```text
route orientation
+
user look offset
```

Не изменять route forward при пользовательском осмотре.

Камера продолжает двигаться вдоль маршрута независимо от направления взгляда.

---

## 17. User look offset

Хранить отдельно:

```text
follow_look_yaw_offset
follow_look_pitch_offset
```

Input источники:

* desktop mouse;
* mobile LookArea.

Pitch ограничивается теми же безопасными значениями, что и в Walk/Fly.

Yaw может быть:

* неограниченным;
* либо нормализованным в диапазон `-PI ... PI`.

Не выполнять автоматический возврат взгляда вперёд в Iteration 1.

---

## 18. Look Forward

Добавить команду:

```text
Look Forward
```

Она плавно либо мгновенно сбрасывает:

```text
follow_look_yaw_offset = 0
follow_look_pitch_offset = 0
```

Рекомендуется короткое плавное возвращение.

Но если текущая архитектура усложняет это, допустим мгновенный reset.

Не сбрасывать route distance и playback state.

---

## 19. Orientation smoothing

Базовое route orientation должно быть достаточно плавным.

Использовать:

* look-ahead vector;
* quaternion/basis interpolation;
* frame-rate independent smoothing.

Не выполнять линейную интерполяцию Euler angles через discontinuity около ±180°.

Предпочтительно:

```text
Quaternion.slerp
```

или `Basis`/quaternion-эквивалент текущей версии Godot.

Сглаживание не должно создавать чрезмерное отставание направления на тесных поворотах.

---

## 20. Playback state

Follow runtime содержит:

```text
is_playing
route_distance_meters
base_speed_mps
speed_multiplier
route_finished
```

Рекомендуемая базовая скорость:

```text
2.5 m/s
```

Рекомендуемые множители:

```text
0.25x
0.5x
1.0x
2.0x
```

Можно добавить:

```text
4.0x
```

только если управление остаётся удобным.

---

## 21. Play/Pause

Команда:

```text
Play / Pause
```

При Pause:

* route distance не меняется;
* пользовательский look работает;
* Step Forward/Backward работает;
* Look Forward работает;
* Exit работает.

При Play:

* route distance увеличивается согласно скорости;
* пользовательский look продолжает работать.

---

## 22. Restart

Команда:

```text
Restart Route
```

Действия:

```text
route_distance_meters = 0
route_finished = false
```

Рекомендуется также:

```text
follow look offsets = 0
```

Playback state:

* если до Restart был Play, продолжить Play;
* если была Pause, оставить Pause.

Не перезагружать Track и Venue.

---

## 23. Step Forward/Backward

Шаги должны изменять расстояние, а не индекс точки.

Рекомендуемый шаг:

```text
1.0 m
```

Допустимый диапазон:

```text
0.5–2.0 m
```

Команды:

```text
Step Backward
Step Forward
```

После шага:

* обновить camera pose немедленно;
* сохранить Play/Pause state;
* очистить `route_finished`, если пользователь отошёл от конца.

---

## 24. Speed controls

UI позволяет:

* выбрать multiplier;
* переключать preset назад/вперёд.

Не требуется произвольный slider.

Показывать:

```text
0.25×
0.5×
1×
2×
```

Изменение скорости не меняет текущую route distance.

---

## 25. Route end

При достижении:

```text
route_distance_meters >= total_length
```

выполнить:

* clamp к total length;
* `is_playing = false`;
* `route_finished = true`;
* сохранить camera в конечной позиции;
* показать состояние `Маршрут завершён`.

Доступны:

* Restart;
* Step Backward;
* Exit;
* Look Forward.

Не выполнять автоматический loop.

Не телепортировать автоматически в начало.

---

## 26. Progress

Показывать минимум один показатель:

```text
current distance / total distance
```

Например:

```text
42 / 185 м
```

Дополнительно можно показывать процент:

```text
23%
```

Вычисление процента:

```text
route_distance / total_length
```

Не менять Track format ради progress.

---

# Follow trajectory visualization

Динамическое отображение trajectory в режиме Follow реализуется согласно:

```text
docs/WebViewerFollowTrajectoryHighlightPlan.md
```

В Walk и Fly глобальная trajectory отображается полностью обычным цветом.

В Follow отображается только ограниченное окно относительно:

```text
route_distance_meters
```

Визуальные зоны:

```text
позади:
затухание и полное скрытие

непосредственно впереди:
зелёная подсветка

далее:
переход зелёный → основной цвет

ещё дальше:
основной цвет

на границе видимого окна:
затухание

далёкий маршрут:
полностью скрыт
```

Расчёты выполняются по накопленному расстоянию RoutePath в метрах.

Не использовать мировое расстояние от камеры: близко расположенные петли маршрута не должны подсвечиваться ошибочно.

Follow rendering не должен изменять RoutePath или playback state.

Если специализированный renderer не работает, Follow camera movement должно продолжать работать с обычной полной trajectory.


## 27. Desktop UI

В Walk/Fly добавить кнопку:

```text
Следовать по трассе
```

В Follow показать панель:

```text
Exit Follow
Restart
Step Backward
Play/Pause
Step Forward
Speed Down
Speed Label
Speed Up
Look Forward
Progress
```

Клавиатурные shortcuts допустимы, но UI должен оставаться полностью работоспособным без них.

Рекомендуемые shortcuts:

```text
Space       Play/Pause
Home        Restart
Left        Step Backward
Right       Step Forward
Esc         Exit Follow
F           Look Forward либо не назначать, если конфликтует
```

Не ломать существующие Walk/Fly shortcuts.

---

## 28. Mobile UI

В Follow mode:

* скрыть MovementJoystick;
* скрыть Fly Up;
* скрыть Fly Down;
* скрыть обычную Walk/Fly mode button либо заменить её на Exit Follow;
* сохранить LookArea;
* показать крупные Follow controls.

Минимальная мобильная панель:

```text
Exit
Restart
Step Backward
Play/Pause
Step Forward
Speed Down
Speed Up
Look Forward
Progress
```

Touch controls должны иметь достаточные размеры.

Рекомендуемый минимальный touch target:

```text
56–72 logical pixels
```

Не размещать все кнопки в одну узкую строку на маленьком экране.

---

## 29. Mobile layout

Landscape:

* playback controls вдоль нижнего края;
* Exit и Look Forward по верхним углам;
* progress не перекрывает центр обзора.

Portrait:

* controls переносятся на две строки или вертикальную панель;
* Viewer остаётся работоспособным;
* orientation hint может сохраняться.

LookArea не должна перекрываться Follow buttons.

Touches, принадлежащие кнопкам, не должны вращать камеру.

---

## 30. Input cleanup

При входе в Follow сбросить:

* keyboard movement runtime state, если он накопительный;
* movement joystick;
* movement touch ID;
* fly up/down;
* vertical Fly input.

При выходе из Follow сбросить:

* playback-only held buttons;
* step-repeat, если он реализован;
* stale look touch ID при реконструкции UI;
* Follow-specific pending actions.

При focus loss:

* остановить движение по маршруту либо поставить на Pause;
* сбросить активные touch IDs;
* не продолжать незаметно playback в фоновой вкладке.

Рекомендуемая политика:

```text
focus loss → Pause
```

После возвращения пользователь вручную нажимает Play.

---

## 31. Route marker

Допускается добавить небольшой маркер текущей Follow position.

Маркер:

* не обязан иметь collision;
* не должен попадать в surface queries;
* не должен перекрывать камеру;
* скрывается вне Follow.

Маркер является optional и не блокирует Iteration 1.

---

## 32. Траектория и визуальный offset

Follow path должен основываться на геометрических projected points.

Если visual trajectory renderer дополнительно поднимает mesh над поверхностью, нельзя повторно добавлять этот renderer offset к camera path.

Не извлекать route points из final rendered mesh, если доступна исходная projected polyline.

---

## 33. Поведение на эстакаде

Проверить:

* подъезд к ramp;
* подъём;
* переход на верхнюю площадку;
* движение по верхней площадке;
* спуск;
* переход обратно на асфальт.

Camera не должна:

* проваливаться внутрь ramp;
* зависать на высоте асфальта;
* резко телепортироваться по Y;
* смотреть в случайную сторону на границе samples.

---

## 34. Degenerate route handling

Обрабатывать:

* две одинаковые начальные точки;
* одинаковые конечные точки;
* короткие нулевые segments;
* route length почти ноль;
* один невалидный projected point;
* частично отсутствующие projected points.

Если после очистки route невалидна, Follow недоступен.

Не пытаться восстанавливать произвольную траекторию догадками.

---

## 35. Reload Track

При reload внешнего `default-track.json`:

1. остановить Follow;
2. очистить RoutePath;
3. очистить Follow UI state;
4. выгрузить старую geometry;
5. построить новую trajectory;
6. построить новый RoutePath;
7. вернуть Viewer в safe Walk/Fly state.

Не сохранять старую route distance для нового маршрута.

---

## 36. Архитектура

Рекомендуемое разделение ответственности:

```text
RoutePath
    points, cumulative length, sampling by distance

RouteFollowController
    playback state, distance, speed, camera targets

ViewerCharacter / ViewerCamera
    application of route pose and user look offsets

RouteFollowUI
    desktop/mobile controls and presentation
```

Не помещать:

* всю математику длины;
* UI callbacks;
* camera transforms;
* touch handling;

в один большой script.

Если текущая архитектура имеет другие подходящие границы, использовать их, сохраняя разделение ответственности.

---

## 37. Не реализовывать

В Iteration 1 не добавлять:

* motorcycle model;
* motorcycle physics;
* lean;
* acceleration/braking model;
* checkpoints;
* lap timer;
* route deviation;
* collisions в Follow;
* автоматические остановки;
* current exercise metadata;
* озвучивание;
* loop mode;
* cinematic camera;
* alternate trajectories;
* nearest route point start;
* сохранение progress;
* URL parameters;
* JSON format changes;
* серверное состояние.

---

## 38. Definition of Done

Итерация завершена, если:

* Follow является отдельным Viewer mode;
* Follow доступен только при валидном RoutePath;
* используются projected trajectory points;
* consecutive duplicate points удаляются;
* cumulative distance table построена;
* total length корректна;
* движение выполняется по метрам;
* скорость не зависит от плотности samples;
* Play/Pause работает;
* Restart работает;
* Step Backward работает;
* Step Forward работает;
* speed presets работают;
* progress отображается;
* look-ahead direction работает;
* orientation сглажена;
* пользователь может осматриваться независимо;
* Look Forward работает;
* маршрут корректно заканчивается;
* автоматического loop нет;
* выход из Follow работает;
* Viewer не помещается внутрь geometry;
* Follow работает на ramp;
* Follow работает на верхней площадке;
* Follow работает на спуске;
* joystick скрывается на мобильном;
* mobile look сохраняется;
* mobile playback controls доступны;
* focus loss ставит playback на Pause;
* reload Track очищает Follow state;
* desktop Walk/Fly не сломаны;
* mobile Walk/Fly не сломаны;
* Web export успешно создаётся;
* GitHub Pages build запускается.
