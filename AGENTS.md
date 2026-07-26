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
