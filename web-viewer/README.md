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

Запустите HTTP server в `dist/web`, например:

```powershell
cd dist/web
python -m http.server 8060
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

## GitHub Pages

Pages публикует готовую папку:

```text
dist/web
```

Workflow:

```text
.github/workflows/deploy-web-viewer.yml
```
