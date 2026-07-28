# MotoGymkhana Web Viewer

Отдельный Godot GDScript Web Viewer.

## Назначение

Проект загружает одну опубликованную трассу:

```text
tracks/default-track.json
```

и предназначен для GitHub Pages.

Desktop-редакторы находятся в основном C#-проекте.

## Открытие проекта

Открыть в обычной, не .NET, версии Godot:

```text
web-viewer/project.godot
```

Версия Godot должна совпадать с версией основного проекта.

## Renderer

Используется:

```text
Compatibility
```

## Локальный запуск Web-export

После сборки нельзя надёжно запускать `index.html` через `file://`.

Запустите HTTP server из корня репозитория и оставьте терминал открытым:

```powershell
python -m http.server 8060 --bind 127.0.0.1 --directory .\dist\web
```

Откройте:

```text
http://localhost:8060/
```

## Актуальная трасса

Внешний Track:

```text
dist/web/tracks/default-track.json
```

Fallback:

```text
web-viewer/tracks/default-track.json
```

## Сборка

Из корня репозитория:

```powershell
.\tools\build-web-viewer.ps1 `
  -GodotPath "C:\Tools\Godot\Godot_v4.x-stable_win64.exe"
```

Для Web Viewer нужна обычная версия Godot, не Godot .NET.

По умолчанию script берёт актуальный Exported Track v4 из:

```text
exports/tracks/new-track-001.json
```

Другой source можно указать явно:

```powershell
.\tools\build-web-viewer.ps1 `
  -GodotPath "C:\Tools\Godot\Godot_v4.x-stable_win64.exe" `
  -TrackPath "E:\Tracks\published-track.json"
```

Script синхронизирует embedded fallback, запускает parser tests, выполняет
release export и затем создаёт внешний `dist/web/tracks/default-track.json`.
PCK получает content-addressed имя `index.<sha256>.pck`, а `index.html`
ссылается на него через `mainPack`. Это исключает запуск устаревшего PCK из
кэша мобильного браузера после нового GitHub Pages deployment.
`index.html` и соответствующий hashed PCK необходимо коммитить вместе. После
первого перехода со старого `index.pck` можно открыть Pages URL с одноразовым
query, например `?v=<commit>`, чтобы сразу обойти ранее закэшированный HTML;
без этого GitHub Pages может удерживать старую точку входа до 10 минут.

## Headless проверки

```powershell
godot --headless --path web-viewer `
  --script res://tests/track_v4_parser_test.gd

godot --headless --path web-viewer -- `
  --embedded-only --smoke-test
```

## GitHub Pages

Pages публикует готовую папку:

```text
dist/web
```

Workflow:

```text
.github/workflows/deploy-web-viewer.yml
```

## Mobile controls

На устройствах с touch input Viewer автоматически показывает:

* виртуальный джойстик движения;
* touch-зону вращения камеры;
* Walk/Fly;
* Fly Up/Down;
* Reset Position.

Основной режим использования на телефоне:

```text
landscape
```

Управление:

```text
Левый палец:
движение

Правый палец:
обзор

Walk/Fly:
переключение режима

Up/Down:
высота в Fly mode
```

В Fly mode камера не может опуститься ниже высоты глаз Walk mode относительно поверхности площадки или эстакады.

Touch controls можно вручную показать или скрыть кнопкой `Show/Hide Touch UI`
либо клавишей `T`. Автоматическое отображение использует
`DisplayServer.is_touchscreen_available()`; User-Agent не используется.

Keyboard/mouse и touch записывают вклад в один `ViewerInputState`. Joystick и
LookArea независимо владеют своими touch IDs, поэтому движение и обзор могут
работать одновременно. При reset, скрытии controls, resize/rotation или потере
focus все touch IDs и удерживаемые направления очищаются.

Источник Walk eye height — фактический offset `Camera3D` относительно root
`ViewerCharacter` (сейчас `1.7 m`). Fly lower bound запрашивает только physics
layer `WalkableSurface`; при отсутствии hit используется Venue base `Y=0`.
Clamp только поднимает camera до допустимого minimum и никогда автоматически
не опускает её при переходе на более низкую поверхность.

Fullscreen запускается только непосредственным нажатием mobile-кнопки. Browser
может отклонить запрос; в таком случае Viewer остаётся в обычном режиме и
показывает warning.

Для headless проверки auto-detection можно переопределить без изменения проекта:

```powershell
godot --headless --path web-viewer -- `
  --embedded-only --smoke-test --touch-controls show

godot --headless --path web-viewer -- `
  --embedded-only --smoke-test --touch-controls hide
```
