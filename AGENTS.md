# Instructions for Codex

## Project intent

Read all files in `docs/` before making architectural decisions.

This project is deliberately small and iterative. Do not expand scope beyond the explicitly requested iteration.

## Technology

- Godot 4.x .NET edition.
- C# for gameplay/application scripts.
- Prefer current stable .NET supported by the selected Godot release.
- Use Godot MCP tools to inspect/edit scenes and project state when available, rather than guessing scene structure.

## Mandatory working style

1. Before coding, summarize the requested iteration and list the exact files/scenes you intend to change.
2. Implement only the current iteration from `docs/MVP.md` unless explicitly instructed otherwise.
3. After each coherent change:
   - build/compile;
   - run relevant checks;
   - inspect Godot errors/debug output via MCP when available;
   - fix errors before proceeding.
4. Do not silently introduce new frameworks, packages, architectural layers, persistence systems, DI containers, event buses or patterns unless they solve an immediate documented requirement.
5. Prefer simple, readable code over speculative abstraction.

## C# documentation requirement

The owner intends to read and potentially modify the generated code manually.

Therefore:
- add XML documentation to public/internal types and important members;
- comment fields whose purpose is not obvious;
- explain non-trivial coordinate transforms and Godot-specific behavior;
- add block comments before structurally coherent non-obvious algorithms;
- comments must explain WHY and domain meaning, not merely restate syntax;
- keep methods small enough to understand without excessive indirection.

## Architecture constraints

- Exported track JSON is the contract between future Editor and Viewer.
- Viewer must not depend on ExerciseDefinition library.
- Domain track coordinates are 2D X/Y in meters.
- Mapping from domain X/Y to Godot X/Z must be centralized.
- JSON DTOs should be separate from Godot scene/node classes.
- Do not implement motorcycle physics or a motorcycle model.
- Camera movement only: WASD + mouse + Shift.
- - Walk mode Viewer должен использовать `CharacterBody3D` и physics movement; запрещено перемещать физическую камеру прямым изменением position.
- Collision geometry Venue object хранится внутри asset `.tscn`; Viewer не генерирует её автоматически.
- `collisionEnabled=false` отключает существующие collision descendants instantiated asset.
- Двумерная trajectory, markings, arrows и cones проецируются на walkable surfaces только в Viewer runtime.
- Runtime projected height не сериализуется в Track, Venue или Exercise JSON.
- Surface raycasts должны выполняться после добавления Venue collision bodies в physics space.
- Projection ray mask не должен включать Track visual geometry и непроходимые стены.
- Viewer reload обязан удалить старые physics bodies и projected geometry.

## Data and scaling

Exercise templates may later be independently scaled along X/Y in the Editor. Exported Viewer JSON already contains resolved world coordinates. Viewer must not re-apply element transforms to cones/markings/trajectory.

`elements[]` metadata may contain position/rotation/scale, but is informational for Viewer unless explicitly required later.

## Testing

At minimum validate:
- valid JSON loads;
- missing/invalid file produces a clear error;
- coordinate mapping is correct;
- sample cones spawn in deterministic positions;
- camera controls function without changing vertical height unintentionally.

## Scope rule

When documentation is ambiguous, choose the smallest implementation compatible with the current MVP and leave a concise TODO rather than inventing a large subsystem.


---

## Venue definition

```markdown
- `docs/VenueFormat.md` — контракт переиспользуемой площадки, её окружения, постоянных объектов, конусов и разметки.
- `docs/VenueEditorPlan.md` — план и ограничения реализации Venue Editor.
```

- Venue Definition является отдельным корневым документом и не должна моделироваться как Exercise Definition.
- Venue Editor, Track Editor и Exercise Editor должны оставаться отдельными сценами и режимами.
- Панорамное окружение должно храниться как ссылка на equirectangular texture и отображаться через Godot Sky/PanoramaSkyMaterial, а не через сериализованную геометрию цилиндра или сферы.
- VenueObjectInstance ссылается на готовую `.tscn`-сцену; Venue Editor не редактирует внутреннюю структуру asset.
- Editor-only selection, locks, history, pan, zoom и caches не сериализуются в Venue Definition.

`docs/TrackVenueIntegrationPlan.md` — интеграция Venue с Track Project, Track Editor, export pipeline и Viewer.
- Track Project formatVersion 3 обязан ссылаться на Venue Definition через безопасный относительный `venuePath`.
- Track Project не хранит area или редактируемую копию Venue.
- Track Editor не редактирует Venue geometry; она отображается read-only.
- Exported Track formatVersion 4 содержит runtime snapshot Venue и Track.
- Viewer не читает Track Project, Venue Definition или Exercise Definition.
- Панорама отображается через `PanoramaSkyMaterial`, а не через cylinder/sphere mesh.
- Venue object assets загружаются как PackedScene по `res://` path.
- Не реализовывать compatibility или migration для старых Track Project и exports.
  
- `docs/ViewerVenuePhysicsPlan.md` — физический контроллер Viewer, collision Venue, движение по эстакаде и проекция Track geometry на поверхности.


### Web Viewer documentation

* `docs/WebViewerPlan.md` — отдельный GDScript Web Viewer, Track v4 loading, browser runtime и GitHub Pages deployment.
* `web-viewer/README.md` — локальная сборка и проверка Web Viewer.

### Web Viewer architectural constraints

* Desktop editors and Track compilation remain in the Godot C# project.
* `web-viewer/` is a separate Godot project using only GDScript.
* The Web Viewer loads only Exported Track formatVersion 4.
* The published Track is `tracks/default-track.json`.
* The Web Viewer must work under a GitHub Project Pages subpath and must not assume `/` is the site root.
* The Web Viewer must use the Compatibility renderer.
* Initial Web export must be single-threaded.
* The external Track JSON must have an embedded `res://tracks/default-track.json` fallback.
* Venue assets referenced by `res://` must exist at the same resource paths inside the Web Viewer project.
* Web asset scenes must not depend on C# scripts.
* Web Viewer runtime must not contain Exercise, Venue or Track editing functionality.
* GitHub Pages deployment publishes only `dist/web`.
* Do not put secrets, GitHub tokens or credentials into the Web export.

