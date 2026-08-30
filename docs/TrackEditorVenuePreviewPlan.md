# Track Editor Venue Preview Plan

# Venue Preview Iteration

## 1. Цель

Добавить в Track Editor read-only предпросмотр Venue при создании нового Track.

Пользователь должен иметь возможность до создания Track:

- увидеть существующие Venue;
- выбрать Venue;
- увидеть его геометрию;
- понять размеры площадки;
- увидеть размещённые объекты;
- увидеть постоянные конусы;
- увидеть Venue markings;
- оценить свободное пространство для размещения Exercise.

Preview используется только для выбора Venue и не изменяет Venue Definition.

---

## 2. Основной принцип

Preview должен строиться непосредственно из актуального Venue Definition.

Не использовать отдельные заранее сгенерированные изображения как source of truth.

Схема:

Venue Library
→ VenueDefinition
→ read-only VenuePreviewRenderer
→ top-down preview

Это гарантирует, что preview автоматически соответствует текущему состоянию Venue.

---

## 3. Scope

Реализовать:

- Venue selection preview;
- top-down orthographic rendering;
- Venue dimensions;
- Venue boundary;
- fence;
- Venue objects;
- cones;
- Venue markings;
- line markings;
- cubic Bézier markings;
- solid/dashed/dotted styles;
- object footprints;
- object rotation;
- object scale;
- free-space visualization;
- basic Venue metadata;
- auto-fit;
- selection change;
- library reload;
- invalid Venue state.

Не реализовывать:

- полноценную 3D камеру;
- walk/fly;
- physics;
- collision preview;
- panorama rendering;
- object editing;
- cone editing;
- marking editing;
- GLB import;
- Track placement;
- Exercise preview внутри Venue preview;
- format changes.

---

## 4. Location

Preview должен находиться в UI создания нового Track рядом со списком Venue.

Предпочтительный layout:

```text
+----------------------+-------------------------+
| Venue list           | Venue preview           |
|                      |                         |
| > Training Ground A  |                         |
|   Training Ground B  |       top-down          |
|   ...                |       preview           |
|                      |                         |
|                      |                         |
+----------------------+-------------------------+
| Venue metadata       |                         |
+----------------------+-------------------------+
