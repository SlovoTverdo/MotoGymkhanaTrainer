# Curved marking editing

Полноценное редактирование Path-based markings реализуется согласно:

```text
docs/ExerciseEditorCurvedMarkingsPlan.md
```

Exercise Editor поддерживает:

* line segments;
* cubic Bézier segments;
* endpoints;
* control points;
* segment selection;
* whole-marking translation;
* conversion line/cubic;
* split;
* deletion;
* Undo/Redo.

Path continuity основана на общем endpoint:

```text
start следующего segment
=
end предыдущего segment
```

Следующий segment не хранит отдельную копию start.

Инструменты marking явно отображаются в toolbar: `Create Marking`, `Append Line`,
`Append Curve` и `Split`. `V`, `L`, `B` выбирают Select/Append Line/Append Curve;
`Enter` завершает построение, `Escape` отменяет transient operation и возвращает
Select. `Ctrl+Z`/`Ctrl+Y` используют общую snapshot history редактора, при этом
drag добавляет только одну revision после отпускания кнопки мыши.

Path start до появления первого segment является только editor preview и не
добавляется в Exercise Definition. Handles и selection также не сериализуются.

