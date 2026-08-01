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

