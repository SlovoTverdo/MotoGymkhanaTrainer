# GLB Import Plan

# Venue Editor GLB Import Iteration

## 1. Цель

Добавить в Venue Editor возможность импортировать произвольные `.glb`/`.gltf` объекты непосредственно в редактор, без предварительного создания отдельной Godot Scene.

Пользователь должен иметь возможность:

- выбрать GLB-файл;
- импортировать его в project asset library;
- увидеть объект в preview;
- разместить его на Venue;
- перемещать;
- вращать;
- масштабировать;
- автоматически определить footprint;
- автоматически создать collision representation;
- сохранить ссылку на asset в Venue Definition;
- повторно открыть Venue без потери объекта.

---

## 2. Основной принцип

GLB является внешним asset.

Venue Definition не содержит mesh data.

Venue Definition хранит:

- asset reference;
- transform;
- calculated footprint;
- collision policy/data;
- editor/runtime properties.

Файловая структура:

```text
assets/
  venue/
    imported/
      <asset-id>/
        source.glb
        metadata.json

Фактическая структура каталогов может соответствовать существующей структуре проекта.
Не дублировать один GLB в каждом Venue.
3. Scope
Реализовать:
- GLB import;
- GLTF/GLB validation;
- asset library;
- imported asset metadata;
- asset preview;
- placement;
- position;
- rotation;
- scale;
- footprint calculation;
- footprint persistence;
- collision generation;
- collision preview;
- object selection;
- object deletion;
- duplicate;
- Undo/Redo;
- save/reload;
- missing asset diagnostics;
- asset reload;
- recalculate footprint;
- Track export integration.
Не реализовывать:
- online asset repository;
- asset marketplace;
- texture editor;
- mesh editing;
- material editing;
- skeletal animation;
- animation playback;
- LOD generation;
- mesh optimization;
- texture compression pipeline;
- runtime GLB loading in Web Viewer;
- arbitrary procedural collision authoring;
- boolean collision editing.
4. Existing Venue objects
Не ломать существующий механизм Venue objects.
Определить два класса объектов:
Built-in Venue Object
Imported GLB Object
Они должны иметь максимально общий runtime/editor representation.
Imported GLB не должен создавать отдельную параллельную систему трансформации.
5. Asset identity
Каждый imported asset получает стабильный ID.
Не использовать абсолютный путь Windows.
Не использовать display filename как единственный identifier.
Рекомендуемый ID:
venue-object-<uuid>
или существующий project asset ID policy.
ID должен оставаться стабильным после:
- Venue save;
- Venue reload;
- file rename, если asset storage поддерживает rename;
- перемещения внутри project asset directory.
6. Asset path
Venue Definition хранит project-relative reference.
Пример:
{
  "asset": "assets/venue/imported/bench-01/source.glb"
}
Не хранить:
C:\Users\...
D:\...
E:\Projects\...
Абсолютные пути запрещены.
7. Asset metadata
Для каждого imported asset хранить metadata:
{
  "assetId": "bench-01",
  "sourceFile": "source.glb",
  "displayName": "Bench",
  "sourceBounds": {
    "min": {},
    "max": {}
  },
  "footprint": {
    "width": 1.2,
    "depth": 0.5,
    "centerX": 0.0,
    "centerY": 0.0
  },
  "collision": {
    "mode": "generated"
  }
}
Фактическая metadata schema должна следовать существующим project conventions.
8. Import process
При импорте:
1. выбрать .glb/.gltf;
2. проверить extension;
3. скопировать asset в managed project directory;
4. создать asset ID;
5. импортировать через Godot;
6. дождаться завершения import;
7. загрузить imported scene;
8. проанализировать hierarchy;
9. вычислить bounds;
10. вычислить footprint;
11. определить collision policy;
12. сохранить metadata;
13. показать asset в Asset Library.
Если любой шаг не удался:
- asset не должен появиться как usable object;
- partial files должны быть удалены либо помечены;
- показать diagnostic.
9. Source asset immutability
Оригинальный imported GLB не редактируется Venue Editor.
Transform объекта хранится отдельно.
Не применять destructive transform к mesh.
10. Godot import
Использовать штатный Godot GLTF/GLB importer.
Не писать собственный parser GLB.
Не декодировать GLB вручную.
Не зависеть от Blender для runtime/editor import.
11. Imported Scene
После import получить Godot scene/resource hierarchy.
Найти все:
VisualInstance3D
и связанные mesh resources.
Bounds рассчитывать по фактически отображаемой geometry.
Не рассчитывать footprint только по root Node3D.
12. Bounds
Для asset получить world/local aggregate AABB.
Обходить все VisualInstance3D.
Для каждого:
global/local transform
+
mesh AABB
объединять в aggregate bounds.
Учитывать:
- child transforms;
- nested Node3D;
- scale;
- rotation;
- multiple meshes.
Не учитывать:
- Camera3D;
- Light3D;
- AudioStreamPlayer3D;
- helper nodes;
если они не содержат визуальную geometry.
13. Footprint
Footprint — 2D projection объекта на Venue plane.
Для первой версии использовать conservative AABB projection.
Определить:
width
depth
center
в локальных координатах asset.
Footprint должен учитывать фактические mesh bounds.
Если asset имеет rotation, footprint должен вращаться вместе с object transform.
14. Footprint orientation
Footprint хранится в local asset coordinates.
Venue object transform применяется поверх него.
Не пересчитывать footprint при каждом rotation event.
Rotation только изменяет placement transform.
15. Footprint persistence
После import calculated footprint сохраняется в asset metadata.
Venue object может ссылаться на этот footprint.
При повторном открытии Venue не требуется заново анализировать mesh hierarchy.
Команда:
Recalculate Footprint
пересчитывает metadata.
16. Recalculate Footprint
Должна быть доступна команда:
Recalculate Footprint
Она:
1. reload asset;
2. пересчитывает VisualInstance3D bounds;
3. обновляет footprint;
4. обновляет collision;
5. сохраняет metadata;
6. обновляет все Venue instances этого asset после явного подтверждения либо согласно существующей asset-reference policy.
Изменение footprint может повлиять на существующие Venue objects.
Не менять placement transform автоматически.
17. Asset transform
Venue object хранит:
position
rotation
scale
в Venue coordinates.
Scale может быть:
uniform
или:
scaleX
scaleY
scaleZ
если текущая Venue object architecture это поддерживает.
Не менять mesh resource.
18. Default scale
При первом размещении:
scale = (1, 1, 1)
если asset metadata не содержит рекомендованного import scale.
Не угадывать размер по filename.
19. Import scale
Не применять автоматическое масштабирование к условным единицам без явного project policy.
GLB/glTF использует метрическую семантику.
Если asset явно импортирован с неправильным scale, пользователь может изменить object scale.
20. Preview
После выбора asset в Asset Library показать preview.
Preview должен использовать реальный imported scene.
Не создавать отдельную thumbnail image как source of truth.
Минимальный preview:
- model;
- neutral lighting;
- ground plane;
- orbit camera либо существующий object preview infrastructure.
21. Placement
После выбора asset:
1. asset preview активен;
2. пользователь выбирает место на Venue;
3. появляется ghost object;
4. object follows cursor;
5. snapping используется согласно Venue Editor policy;
6. click подтверждает placement;
7. object становится обычным Venue object.
Ghost object:
- не участвует в collision;
- не попадает в save;
- не создаёт history entry до confirmation.
22. Placement collision
На этапе placement не запрещать overlap автоматически.
Venue Editor должен позволять поставить объект поверх другого.
Допускается warning:
Object overlaps existing geometry
Но blocking collision validation не требуется.
23. Collision modes
Поддержать минимум:
none
generated
generated — автоматически созданный collision representation.
Не реализовывать ручное редактирование collision mesh в первой версии.
24. Collision generation
Предпочтительный первый алгоритм:
ConcavePolygonShape3D
либо существующий Godot static mesh collision generation API, если он уже используется проектом.
Для простых static objects допускается:
- convex decomposition;
- multiple ConvexPolygonShape3D.
Выбрать безопасный и стабильный вариант после анализа текущего physics architecture.
Не использовать collision из render mesh без проверки размеров.
25. Collision policy
Imported Venue objects являются static environment geometry.
Collision должен блокировать:
- Walk mode;
- Fly mode, если существующая Fly collision policy предусматривает collision;
- Track Viewer camera/object collision согласно текущей architecture.
Не добавлять collision для editor-only ghost.
26. Collision offset
Collision должен использовать тот же world transform, что render geometry.
Не создавать отдельную scale/rotation policy.
После transform:
render transform == collision transform
27. Collision preview
Venue Editor должен иметь возможность визуально показать collision representation.
Рекомендуется:
- toggle Show Collision.
При выключенном toggle collision не видна, но продолжает существовать.
Не добавлять collision meshes в normal rendering layer.
28. Runtime rendering
При открытии Venue:
asset reference
→ load imported scene
→ instantiate
→ apply transform
→ attach collision
Не сохранять instantiated NodePath в Venue Definition.
NodePath является runtime/editor state.
29. Venue Definition
Не хранить raw mesh.
Imported object должен хранить что-то эквивалентное:
{
  "id": "object-01",
  "type": "imported",
  "asset": "assets/venue/imported/bench-01/source.glb",
  "transform": {
    "position": {},
    "rotation": {},
    "scale": {}
  },
  "collision": {
    "mode": "generated"
  }
}
Существующие object properties должны сохраняться.
Если текущая schema уже имеет object type, использовать её.
30. Format version
Поскольку Venue Definition v2 уже находится в production development state, перед реализацией проверить, может ли текущая schema расшириться без version bump.
Предпочтительно:
Venue Definition v2
с новым optional/known object type.
Если schema contract требует version change:
остановиться на архитектурном уровне и явно зафиксировать необходимость Venue v3 до реализации.
Не делать молчащий version change.
31. Asset references
При сохранении Venue проверить:
- asset path существует;
- asset ID корректен;
- asset metadata существует.
Missing asset:
- не ломает загрузку всего Venue;
- объект получает Missing Asset placeholder;
- diagnostic содержит object ID и asset path.
32. Missing Asset placeholder
При missing GLB показывать:
- placeholder bounds;
- object ID;
- asset path;
- warning.
Placeholder не экспортируется как реальная geometry.
Пользователь может удалить object либо relink asset.
33. Relink Asset
Добавить command:
Relink Asset
для missing asset.
Пользователь выбирает новый GLB.
После relink:
- asset reference обновляется;
- bounds пересчитываются;
- footprint обновляется;
- collision regenerates;
- transform object сохраняется.
34. Duplicate
Duplicate imported object:
- создаёт новый Venue object ID;
- сохраняет тот же asset reference;
- сохраняет transform;
- сохраняет collision mode;
- не копирует GLB.
Asset должен быть shared.
35. Delete
Удаление Venue object:
- удаляет только instance;
- не удаляет asset из library.
Удаление asset из Asset Library отдельная операция и не входит в первую версию.
36. Undo/Redo
Поддержать:
- Import asset;
- Place object;
- Delete object;
- Duplicate object;
- Move;
- Rotate;
- Scale;
- Change collision mode;
- Relink asset;
- Recalculate footprint.
Import asset может быть отдельной asset operation.
Drag transform = одна Undo operation.
37. Asset library
Asset Library должна показывать:
- display name;
- asset ID;
- source filename;
- dimensions;
- footprint;
- collision mode.
Preview thumbnail не обязательна.
38. Asset folders
Импортированные assets хранить в managed folder.
Не позволять Venue Definition ссылаться на произвольные абсолютные paths.
Если пользователь импортирует одинаковый GLB дважды:
- обнаружить duplicate;
- предложить использовать существующий asset;
- либо создать отдельную asset identity согласно current policy.
Не создавать silently duplicate copies.
39. File deletion
Если source GLB удалён вручную:
- Venue не должен crash;
- object становится Missing Asset;
- diagnostic отображается.
Не удалять Venue object автоматически.
40. File rename/move
Если managed asset перемещён средствами приложения:
- обновить asset references;
- Venue instances должны продолжить работать.
Не обещать automatic tracking arbitrary filesystem moves в первой версии.
41. Materials and textures
GLB materials/textures должны использовать штатный Godot import.
Не редактировать материалы в Venue Editor.
Если material/resource missing:
- asset может отображаться с fallback;
- diagnostic сохраняется;
- import не должен crash editor.
42. Animations
Animated/skeletal assets не являются целью Venue static geometry.
В первой версии:
- импорт разрешён;
- render может использовать default pose;
- animation playback не поддерживается.
Если asset не может безопасно использоваться как static object:
- показать warning;
- не блокировать остальные assets.
43. Lights/Cameras
Imported lights/cameras не должны автоматически становиться частью Venue runtime.
Для static Venue object:
- render geometry используется;
- embedded Camera3D/Light3D игнорируются либо отключаются;
- physics collision строится только для geometry.
44. Nested transforms
Обход imported hierarchy должен корректно учитывать:
- Node3D transforms;
- nested children;
- scale;
- rotations.
Нельзя рассчитывать bounds только по root mesh.
45. Coordinate conversion
GLB coordinates должны преобразовываться в текущую Venue coordinate system через существующий Godot import pipeline.
Не писать custom Y-up/Z-up conversion, если Godot importer уже выполняет необходимую обработку.
Проверить:
- forward direction;
- up direction;
- units;
- handedness.
46. Object origin
Не менять mesh origin при import.
Footprint center должен быть вычислен независимо от origin.
При placement:
object transform
определяет положение origin.
Footprint offset относительно origin должен сохраняться.
47. Bounds and origin example
Если mesh bounds:
min = (-2, 0, -1)
max = (2, 3, 1)
то:
width = 4
depth = 2
height = 3
Но object origin может находиться вне bounds.
Footprint должен сохранять local offset относительно origin.
48. Existing built-in objects
Не переносить существующие:
- house;
- ramp;
- fence;
в GLB system автоматически.
GLB import является дополнительным механизмом.
Существующие built-in objects продолжают работать.
49. Exported Track
Track export должен учитывать imported Venue objects через существующий Venue object export mechanism.
Если Exported Track хранит asset references:
- сохранить project-relative asset path;
- Web Viewer не обязан поддерживать imported GLB runtime в этой итерации.
Если текущий Web Viewer не может загрузить asset:
- это допустимо;
- документировать limitation.
Desktop Viewer должен поддерживать imported objects.
50. Web Viewer
GLB runtime loading в Web Viewer НЕ входит в scope.
Не менять Web Viewer только ради этой итерации.
Если Exported Track v5 содержит imported asset reference, Web Viewer должен:
- либо пропустить unsupported object;
- либо показать diagnostic;
- не crash.
51. Security
Не разрешать arbitrary filesystem path в Venue Definition.
Все managed assets должны находиться внутри project directory.
Не выполнять скрипты из imported GLB.
Не исполнять embedded executable content.
52. Performance
Asset import выполняется вне realtime render loop.
Bounds calculation выполняется один раз при import/recalculate.
Collision generation выполняется один раз при import/recalculate.
Runtime scene instantiation не должна происходить каждый frame.
Shared asset resources должны кэшироваться.
53. Tests
Добавить tests:
Asset
1. valid GLB import;
2. invalid GLB;
3. missing GLB;
4. duplicate import;
5. asset ID;
6. project-relative path.
Bounds
7. single mesh;
8. multiple meshes;
9. nested transform;
10. rotated child;
11. scaled child;
12. origin outside bounds.
Footprint
13. width/depth;
14. center offset;
15. rotation;
16. scale.
Venue object
17. placement;
18. save/reload;
19. duplicate;
20. delete;
21. move;
22. rotate;
23. scale.
Collision
24. generated collision;
25. none;
26. collision transform;
27. collision toggle.
Recovery
28. missing asset;
29. relink;
30. recalculate footprint.
Undo/Redo
31. place;
32. delete;
33. duplicate;
34. transform;
35. relink;
36. recalculate footprint.
54. Manual/MCP checks
Проверить минимум:
1. import простой GLB;
2. asset появляется в library;
3. preview;
4. placement;
5. move;
6. rotate;
7. scale;
8. footprint display;
9. collision display;
10. Save;
11. close/reopen;
12. object remains;
13. duplicate;
14. delete;
15. Undo/Redo;
16. missing asset;
17. relink;
18. Track export;
19. desktop Viewer;
20. existing house/ramp/fence unaffected.
Особенно проверить collision:
- камера не проходит сквозь object;
- Fly/Walk ведут себя согласно существующей collision policy.
55. Не реализовывать
Не добавлять:
- Web Viewer GLB runtime;
- text markings;
- animation editor;
- material editor;
- mesh editor;
- LOD;
- automatic optimization;
- asset marketplace;
- remote assets;
- arbitrary filesystem links;
- collision mesh manual editing;
- boolean geometry;
- format migration system.
56. Definition of Done
Итерация завершена, если:
- GLB можно импортировать;
- imported asset появляется в library;
- asset хранится в managed project directory;
- Venue хранит relative asset reference;
- preview работает;
- object можно разместить;
- object можно перемещать;
- вращать;
- масштабировать;
- footprint рассчитывается по фактической geometry;
- nested meshes учитываются;
- origin offset учитывается;
- footprint сохраняется;
- collision создаётся;
- collision соответствует render transform;
- collision можно визуально проверить;
- object сохраняется;
- Venue reload восстанавливает object;
- Duplicate работает;
- Delete работает;
- Undo/Redo работают;
- missing asset не ломает Venue;
- Relink работает;
- Recalculate Footprint работает;
- existing built-in objects не ломаются;
- Track export не ломается;
- desktop Viewer отображает imported object;
- Web Viewer gracefully handles unsupported imported asset;
- format version не изменён без документированного решения;
- tests проходят;
- desktop build проходит.
