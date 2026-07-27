Godot Web Viewer Implementation Plan
Web Viewer Feasibility Iteration 1
1. Цель

Создать отдельный Godot Web Viewer на GDScript, публикуемый через GitHub Pages.

Web Viewer предназначен только для просмотра одной актуальной трассы:

tracks/default-track.json

Desktop-часть проекта остаётся на Godot C# и продолжает содержать:

Exercise Editor;
Venue Editor;
Track Editor;
desktop C# Viewer;
Track compilation;
экспорт Track JSON.

Перевод desktop-редакторов на GDScript не выполняется.

2. Архитектура
Godot C# desktop project
├─ Exercise Editor
├─ Venue Editor
├─ Track Editor
├─ Track compiler
└─ Exported Track v4
        ↓
tracks/default-track.json
        ↓
Godot GDScript Web Viewer
        ↓
GitHub Pages

Web Viewer является независимым Godot-проектом:

web-viewer/project.godot

Он не должен содержать C# или зависеть от .NET runtime.

3. Поддерживаемый формат

Web Viewer загружает:

Exported Track formatVersion 4

Он не загружает:

Track Project;
Venue Definition;
Exercise Definition;
исходные редакторские документы.

Структура и семантика Track v4 должны соответствовать:

docs/TrackFormat.md
4. Источник Track JSON

Основной источник:

tracks/default-track.json

Файл располагается рядом с Web-export, а не обязательно внутри Godot PCK.

При запуске Web Viewer должен:

определить базовый URL текущей страницы;
сформировать URL:
tracks/default-track.json;
добавить cache-busting query parameter;
загрузить JSON через HTTPRequest;
проверить HTTP response;
разобрать JSON;
проверить formatVersion;
построить Viewer scene.

Пример результирующего URL:

https://example.github.io/MotoGymkhanaTrainer/tracks/default-track.json?v=...

Нельзя жёстко предполагать, что приложение опубликовано в корне домена.

GitHub Project Pages обычно публикует приложение в подпапке репозитория.

5. Cache busting

Чтобы браузер и CDN не продолжали показывать старую трассу после замены файла, запрос должен включать изменяемый параметр:

tracks/default-track.json?v=<timestamp-or-build-id>

Для первой версии допустимо использовать текущее Unix time браузера.

Это применяется только к JSON.

Godot .pck, .wasm, .js, модели и текстуры используют обычное версионирование Web-export.

6. Fallback

В Web Viewer также хранится встроенная копия:

res://tracks/default-track.json

Если внешний HTTP-файл:

не найден;
вернул неуспешный HTTP status;
содержит невалидный JSON;
имеет неподдерживаемую версию;

Viewer должен:

показать понятное предупреждение;
попробовать встроенный fallback;
не завершаться необработанной ошибкой.

Если невалидны оба источника, показать blocking error screen.

7. Asset paths

Exported Track v4 может содержать Godot resource paths:

res://venues/...

Web Viewer project должен содержать используемые ресурсы с теми же путями относительно:

web-viewer/

Пример:

desktop:
res://venues/shared_assets/scenes/VenueHouse.tscn

web:
web-viewer/venues/shared_assets/scenes/VenueHouse.tscn

Для Web Viewer этот файл также разрешается как:

res://venues/shared_assets/scenes/VenueHouse.tscn
8. Допустимые Web assets

Для Iteration 1 Web Viewer включает только ресурсы, необходимые текущей опубликованной площадке:

Venue object scenes;
GLB-модели;
материалы;
текстуры;
панораму;
cone model;
необходимые общие shader/material resources.

Asset scenes не должны содержать C# scripts.

Если исходная .tscn содержит C#-узлы, необходимо создать GDScript-neutral или script-free web wrapper.

9. Renderer

Web Viewer использует:

Compatibility renderer

Не использовать:

Forward+;
Mobile renderer как замену Compatibility;
WebGPU-specific features;
desktop-only rendering features.

Все материалы и shaders должны проверяться в Compatibility renderer.

10. Threading

Первая версия использует однопоточный Web-export.

Причины:

проще хостинг на GitHub Pages;
не требуется отдельная настройка COOP/COEP headers;
меньше инфраструктурных факторов при проверке feasibility.

Если производительности окажется недостаточно, многопоточный export рассматривается отдельно.

11. Функции Web Viewer

Обязательно поддержать:

загрузку внешнего default-track.json;
fallback на встроенный JSON;
Track v4 validation;
Venue area surface;
panorama;
Venue object scenes;
transforms Venue objects;
visibleInViewer;
collisionEnabled;
Venue cones;
Exercise cones;
Venue markings;
Exercise markings;
global trajectory;
direction arrows;
surface projection;
Walk mode;
Fly mode;
переключение Walk/Fly;
collision;
движение по эстакаде;
загрузочный экран;
понятный error screen.
12. Паритет с C# Viewer

Web Viewer должен воспроизводить пользовательское поведение C# Viewer, но не обязан повторять его внутреннюю архитектуру.

Обязательный паритет:

система координат;
размеры;
transforms;
цвета;
widths;
trajectory sampling;
markings styles;
surface projection;
physics layers;
collision semantics;
panorama orientation;
camera movement.

Необязательный паритет:

debug UI;
подробные developer diagnostics;
desktop file picker;
arbitrary Track selection;
hot reload;
editor integration.
13. GDScript DTO

Допускаются:

typed Dictionaries;
custom RefCounted DTO classes;
validation functions;
typed arrays, где это практично.

Не следует создавать чрезмерно сложную систему сериализуемых ресурсов.

JSON является внешним контрактом, а parsed runtime data — производным состоянием.

14. JSON validation

До построения сцены проверить минимум:

root является Dictionary;
formatVersion == 4;
присутствует track;
присутствует venue;
area width/length положительны;
массивы имеют ожидаемый тип;
все числовые значения finite;
asset paths начинаются с res://;
отсутствуют duplicate runtime IDs;
trajectory имеет поддерживаемые segment types.

Ошибка одного Venue object во время runtime loading не должна падать вместе со всем Viewer.

15. Load order
1. Показать Loading UI
2. Загрузить внешний Track JSON
3. Выполнить validation
4. Очистить предыдущий runtime
5. Создать Environment и panorama
6. Создать Venue surface и collision
7. Instantiate Venue objects
8. Дождаться physics frame
9. Спроецировать cones/markings/trajectory
10. Создать Viewer character
11. Скрыть Loading UI
16. Surface projection

Web Viewer повторяет runtime projection C# Viewer:

Track X/Y
    ↓
Godot X/Z
    ↓
downward physics ray
    ↓
surface hit
    ↓
projected Godot X/Y/Z

Проецируются:

trajectory;
direction arrows;
markings;
cones.

Projection выполняется после регистрации Venue collision в physics space.

17. Physics

Walk mode использует:

CharacterBody3D

и:

capsule collision;
gravity;
move_and_slide;
floor detection;
floor snap;
collision layers/masks.

Fly mode:

не использует gravity;
допускает вертикальное движение;
сохраняет orientation при переключении.
18. UI

Минимальный UI:

Loading
Error
Mode: Walk/Fly
Controls help

Рекомендуемые controls:

WASD      movement
Mouse     look
Shift     faster movement
F         Walk/Fly
Esc       release mouse

Если controls отличаются от desktop Viewer, фактические клавиши должны быть показаны на экране.

19. Browser-specific behavior

Viewer должен корректно обрабатывать:

mouse capture;
потерю browser focus;
resize;
fullscreen, если добавлен;
загрузку по Project Pages subpath;
HTTP error;
browser cache;
WebGL context error.

Нельзя строить filesystem workflow на доступе к произвольным локальным папкам пользователя.

20. Не реализовывать

В Iteration 1 не добавлять:

Web Exercise Editor;
Web Venue Editor;
Web Track Editor;
GitHub login;
роли;
backend;
URL selection разных трасс;
пользовательскую загрузку GLB;
запись в GitHub;
PR creation;
Track list;
runtime asset download;
внешние .tscn;
arbitrary local file access;
сохранение результатов;
checkpoints;
motorcycle controller.
21. Публикация

Web Viewer экспортируется локально в:

dist/web/

GitHub Actions публикует содержимое этой папки через GitHub Pages.

В корне Pages artifact должен находиться:

index.html

Внешняя трасса располагается:

dist/web/tracks/default-track.json
22. Обновление трассы

Обычный weekly workflow:

1. Открыть Track Editor C#
2. Отредактировать трассу
3. Export Track v4
4. Скопировать export в:
   web-viewer/tracks/default-track.json
5. Запустить:
   tools/build-web-viewer.ps1
6. Проверить локально через HTTP server
7. Commit/push `dist/web`
8. GitHub Actions публикует Pages

Если Web runtime и assets не менялись, допускается заменить только:

dist/web/tracks/default-track.json

и выполнить commit/push без повторного Godot export.

23. Definition of Done

Iteration завершена, если:

web-viewer открывается как отдельный Godot-проект;
проект не содержит C#;
Compatibility renderer включён;
Web preset создан;
single-thread Web export работает;
dist/web/index.html создаётся;
Viewer загружает внешний default-track.json;
fallback работает;
площадка отображается;
panorama отображается;
дом отображается;
эстакада отображается;
забор отображается;
cones отображаются;
markings отображаются;
trajectory отображается;
trajectory проецируется на эстакаду;
collision работает;
Walk/Fly работает;
build запускается через локальный HTTP server;
GitHub Actions публикует dist/web;
GitHub Pages открывает Viewer;
обновление только default-track.json меняет опубликованную трассу.
FILE: web-viewer/README.md
MotoGymkhana Web Viewer

Отдельный Godot GDScript Web Viewer.

Назначение

Проект загружает одну опубликованную трассу:

tracks/default-track.json

и предназначен для GitHub Pages.

Desktop-редакторы находятся в основном C#-проекте.

Открытие проекта

Открыть в обычной, не .NET, версии Godot:

web-viewer/project.godot

Версия Godot должна совпадать с версией основного проекта.

Renderer

Используется:

Compatibility
Локальный запуск Web-export

После сборки нельзя надёжно запускать index.html через file://.

Запустите HTTP server в dist/web, например:

cd dist/web
python -m http.server 8060

Откройте:

http://localhost:8060/
Актуальная трасса

Внешний Track:

dist/web/tracks/default-track.json

Fallback:

web-viewer/tracks/default-track.json
Сборка

Из корня репозитория:

.\tools\build-web-viewer.ps1 `
  -GodotPath "C:\Tools\Godot\Godot_v4.x-stable_win64.exe"

Для Web Viewer нужна обычная версия Godot, не Godot .NET.

GitHub Pages

Pages публикует готовую папку:

dist/web

Workflow:

.github/workflows/deploy-web-viewer.yml
