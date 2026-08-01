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
* `docs/WebViewerMobileControlsPlan.md` — сенсорное управление Web Viewer, мультитач, responsive mobile UI и ограничение минимальной высоты Fly camera.
* `docs/WebViewerRouteFollowPlan.md` — автоматическое следование Web Viewer по projected trajectory, playback, управление камерой и мобильный Follow UI.
* `docs/WebViewerFollowTrajectoryHighlightPlan.md` — динамическое окно видимости и цветовая подсветка trajectory во время Follow.

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
* Build the Web Viewer only with `tools/build-web-viewer.ps1` and a regular Godot executable; do not use the .NET/Mono binary for the published export.
* `web-viewer/` must remain free of `.cs`, `.csproj`, desktop editor scenes and editor-only DTOs.
* Keep both Track copies intentional: embedded `web-viewer/tracks/default-track.json` for fallback and external `dist/web/tracks/default-track.json` for Pages updates.
* GitHub Actions publishes the already-built `dist/web`; it must not install or run Godot.

- Mobile Web controls must feed the shared Viewer input state and must not implement a second movement controller.
- Movement and look touch contacts must be tracked by independent touch IDs.
- Mobile UI buttons must not leak touch events into the camera look area.
- Losing browser focus must clear all active touch state.
- Fly mode camera height must be clamped relative to the WalkableSurface below the camera.
- Fly minimum height must reuse the Walk eye-height source of truth.
- Fly height clamp must not act as ground-following movement and must not automatically lower the camera.
- If no WalkableSurface is found, Fly mode must use the Venue base level as a safe lower-bound fallback.
- Route Follow must use the already projected global Track trajectory and must not recompile Exercise routes or transitions.
- Route Follow position must be represented as distance in meters, not as a sample-point index.
- Route paths must maintain cumulative distances and provide interpolation by distance.
- Follow camera direction must use a look-ahead point rather than only the next sample.
- User look in Follow mode must be stored as an offset from route orientation and must not change route movement.
- Follow camera height must use projected route height plus the shared Viewer eye-height source of truth.
- Mobile Follow mode must hide the movement joystick while preserving the touch look area.
- Losing browser focus during Follow must pause playback and clear active touch state.
- Reloading a Track must clear the old RoutePath and all Follow runtime state.
- Route Follow is a route-learning feature, not motorcycle simulation; do not add vehicle physics, checkpoints or timing to this iteration.
- Follow trajectory highlighting must use RoutePath cumulative distance in meters, not world-space distance from the camera.
- Walk and Fly must continue to display the complete trajectory.
- Follow mode must hide passed and distant route portions while showing a green-to-base-color preview window ahead.
- Route-distance data for rendering must reuse the RoutePath metric contract instead of building an unrelated distance model from the rendered mesh.
- Pausing, stepping backward or forward, restarting and exiting Follow must update trajectory visualization immediately.
- The preferred implementation is a Compatibility-compatible shader driven by a route-distance vertex attribute and a current-distance uniform.
- If the shader path is not viable, use a bounded CPU-generated mesh fallback; do not create one scene node per short trajectory segment.
- Failure of the highlight renderer must not disable Route Follow movement.

## Subagent workflow

Use subagents selectively. Do not create subagents for trivial, localized tasks.

Before implementing a non-trivial change:

1. Delegate read-only codebase exploration to `project_explorer`.
2. Wait for its result.
3. Produce a concise implementation plan based on verified repository evidence.
4. Keep ownership of the architectural decision and final integration in the main agent.
5. Use only one write-capable agent for files that may overlap.
6. After implementation, delegate independent review to `change_reviewer`.
7. Delegate build and automated verification to `test_runner`.
8. Fix blocking findings and rerun affected checks.
9. Return a final report containing:
   - changed files;
   - behavioral changes;
   - tests and commands executed;
   - unresolved risks;
   - documentation changes.

Parallelize only independent workstreams.
Prefer read-only subagents for exploration, review, logs, tests, and documentation analysis.
Do not allow multiple agents to edit the same file concurrently.
Do not delegate product requirements, architecture ownership, or final acceptance.
Use lightweight models for bounded read-heavy tasks and stronger models for
ambiguous implementation and final review.

### Curved markings documentation

* `docs/CurvedMarkingsPlan.md` — Path-based markings, line/cubic Bézier segments, sampling, transforms, export and Viewer rendering.

### Curved markings constraints

* Exercise and Venue markings use a continuous Path with one start point and ordered segments.
* Supported marking segment types are `line` and `cubicBezier`.
* Do not store a duplicated start point inside every segment.
* Marking Path and trajectory remain separate domain concepts even when sharing mathematical helpers.
* Exercise marking control geometry must be transformed before adaptive sampling.
* Independent Exercise scale X/Y must transform Bézier control points correctly.
* Marking thickness is measured in world meters and must not be implicitly distorted by Exercise scale.
* Dashed and dotted styles must be generated by cumulative world-space length, not by sample indices.
* Desktop editors and Viewer must share one C# Path sampling implementation.
* The Web Viewer must provide an equivalent GDScript implementation with shared test vectors.
* Surface projection must operate on sampled curved geometry and must preserve ramp behavior.
* Exercise Definition v3, Venue Definition v2 and Exported Track v5 must not serialize the removed legacy marking points representation.
* Runtime backward compatibility is not required; update fixtures, samples and fallback Track together.
* Curved Markings Iteration 1 adds domain, export and rendering support only. Do not add Bézier control-point editing UI in this iteration.

### Exercise Editor curved markings
* `docs/ExerciseEditorCurvedMarkingsPlan.md` — интерактивное создание и редактирование Path-разметки в Exercise Editor.

### Exercise Editor curved-marking constraints

* Editor selection must distinguish a marking, segment, endpoint and Bézier control point.
* Segment starts are implicit and must not be duplicated in the serialized model.
* Moving a segment endpoint also moves the implicit start of the next segment.
* Line-to-cubic conversion must preserve shape by placing controls at one-third and two-thirds of the chord.
* Cubic splitting must use de Casteljau and preserve the original curve.
* Dragging a handle must create one Undo/Redo transaction, not one command per mouse-motion event.
* Editor transient creation and drag state must never be serialized into Exercise Definition.
* Control handles are editor overlays and must not become Viewer geometry.
* Dashed and dotted markings must be selectable through their complete centerline, including visual gaps.
* Exercise Editor Iteration 2 must not add Venue Editor curved-segment editing or change JSON format versions.
