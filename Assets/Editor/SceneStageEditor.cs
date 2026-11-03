using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Edit a stage's objects directly in the Unity scene (owner, 2026-10-04: add existing objects, turn them and mirror
    /// them in the scene; sizes and colours stay as they are). Open a stage: its ground, walls, fence and buildings are
    /// built as a fixed backdrop, and every prop and decoration becomes a selectable object. Move them with Unity's move
    /// tool and turn them with the rotate tool (only the turn around the vertical counts; they stay on the ground). The
    /// window turns them by 90°, mirrors them, sets a pose or a colour version, and adds any existing object at the centre
    /// of the Scene view. Save writes the objects back into the stage file; everything else in the file is kept.
    /// 2026-10-05 (owner): this is the owner's one map tool. They edit existing stages only (obstacles, decorations,
    /// hiding grass); Claude lays out new stages. So the window has two modes: Obstacles and decorations, and Hiding grass
    /// (SceneStageEditorGrid.cs). Markers show read-only, Save checks reachability, and Play tests the stage, then
    /// reopens it here. The Map Editor and the generators moved to Zombie > Developer (Claude).
    /// </summary>
    public partial class SceneStageEditor : EditorWindow
    {
        private const string RootName = "[Stage edit]";

        private string[] _stages = new string[0];
        private int _stageIndex;
        private string _openId;
        private GameObject _root;
        private Transform _props, _decorations;
        private string _search = "";
        private bool _addDecoration;
        private Vector2 _paletteScroll;
        private bool _dirty;

        [MenuItem("Zombie/Scene Stage Editor")]
        public static void Open() => GetWindow<SceneStageEditor>("Scene Stage Editor");

        private void OnEnable()
        {
            _stages = StageFiles.ReadIndex().ToArray();
            EditorApplication.update += KeepOnGround;
            SceneView.duringSceneGui += OnSceneGrid;
            Undo.undoRedoPerformed += Repaint;
            EditorApplication.playModeStateChanged += AfterPlay;
        }

        private void OnDisable()
        {
            EditorApplication.update -= KeepOnGround;
            SceneView.duringSceneGui -= OnSceneGrid;
            Undo.undoRedoPerformed -= Repaint;
            EditorApplication.playModeStateChanged -= AfterPlay;
        }

        private const string ReopenKey = "ZombieSceneStageEditor.Reopen";

        /// <summary>Saves, puts the editing copy away (it would show over the game), and plays the stage in Solo.</summary>
        private void Play()
        {
            if (EditorApplication.isPlaying || _openId == null) return;
            Save();
            var id = _openId;
            if (EditorSceneManager.GetActiveScene().path != ZombieSetup.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                Close(true);
                EditorSceneManager.OpenScene(ZombieSetup.ScenePath);
            }
            else Close(true);
            EditorPrefs.SetString(GameSession.PlayStagePref, id);
            UnityEditor.SessionState.SetString(ReopenKey, id);
            EditorApplication.isPlaying = true;
        }

        /// <summary>Back from playing: reopens the stage that was being edited.</summary>
        private void AfterPlay(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            var id = UnityEditor.SessionState.GetString(ReopenKey, "");
            if (string.IsNullOrEmpty(id)) return;
            UnityEditor.SessionState.EraseString(ReopenKey);
            _stages = StageFiles.ReadIndex().ToArray();
            _stageIndex = Mathf.Max(0, System.Array.IndexOf(_stages, id));
            OpenStage(id);
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Stage", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _stageIndex = EditorGUILayout.Popup(Mathf.Clamp(_stageIndex, 0, Mathf.Max(0, _stages.Length - 1)), _stages);
                if (GUILayout.Button(_root == null ? "Open in scene" : "Reopen", GUILayout.Width(110)) && _stages.Length > 0) OpenStage(_stages[_stageIndex]);
            }
            if (_root == null)
            {
                EditorGUILayout.HelpBox("Open a stage to edit its obstacles, decorations and hiding grass in the Scene view.", MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField($"Editing {_openId}: {_props.childCount} props, {_decorations.childCount} decorations{(_dirty ? " (unsaved)" : "")}");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save", GUILayout.Height(28))) Save();
                if (GUILayout.Button("Save and play", GUILayout.Height(28))) Play();
                if (GUILayout.Button("Close", GUILayout.Height(28), GUILayout.Width(70))) Close(false);
            }
            DrawProblems();
            EditorGUILayout.Space();
            _paint = GUILayout.Toolbar(_paint ? 1 : 0, new[] { "Obstacles and decorations", "Hiding grass" }, GUILayout.Height(24)) == 1;
            if (_paint)
            {
                DrawPaintGUI();
                return;
            }
            DrawMarkerKey();

            EditorGUILayout.Space();
            var selected = Selected();
            EditorGUILayout.LabelField(selected.Count == 0 ? "Selection: none (click an object in the Scene view)" : $"Selection: {selected.Count}", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(selected.Count == 0))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Turn -90°")) Turn(selected, -90f);
                    if (GUILayout.Button("Turn +90°")) Turn(selected, 90f);
                    if (GUILayout.Button("Mirror")) Mirror(selected);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Upright")) Pose(selected, 0f, 0f);
                    if (GUILayout.Button("Leaning")) Pose(selected, 18f, 0f);
                    if (GUILayout.Button("On its side")) Pose(selected, 90f, 0f);
                    if (GUILayout.Button("Upside down")) Pose(selected, 180f, 0f);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Colour ◀")) Recolour(selected, -1);
                    if (GUILayout.Button("Colour ▶")) Recolour(selected, 1);
                    if (GUILayout.Button("Delete")) Delete(selected);
                }
                if (selected.Count == 1)
                {
                    var p = selected[0];
                    var piece = Art.ArtLibrary.Get(p.id);
                    EditorGUILayout.LabelField($"{piece?.Name ?? p.id}   turn {Mathf.Repeat(p.transform.eulerAngles.y, 360f):0}°   " +
                                               $"{(p.flip ? "mirrored" : "not mirrored")}   colour {(p.variant == 0 ? "auto" : p.variant.ToString())}" +
                                               (p.roll != 0f || p.pitch != 0f ? $"   pose roll {p.roll:0} pitch {p.pitch:0}" : ""));
                    if (piece != null && piece.NoFlip) EditorGUILayout.HelpBox("A building: it can be turned but not mirrored.", MessageType.None);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Add an object (at the centre of the Scene view)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _addDecoration = GUILayout.Toolbar(_addDecoration ? 1 : 0, new[] { "Props", "Decorations" }) == 1;
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            }
            _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll);
            var pieces = Art.ArtLibrary.OfKind(_addDecoration ? Art.ArtLibrary.Kind.Decoration : Art.ArtLibrary.Kind.Prop)
                .Where(p => string.IsNullOrEmpty(_search) || p.Name.ToLowerInvariant().Contains(_search.ToLowerInvariant()) || p.Id.Contains(_search.ToLowerInvariant()))
                .OrderBy(p => p.Name).ToList();
            foreach (var piece in pieces)
                if (GUILayout.Button(piece.Name + (piece.ShootThrough ? "  (shoot-through)" : ""), EditorStyles.miniButton)) Add(piece);
            EditorGUILayout.EndScrollView();
        }


        /// <summary>Batch check: open a stage, save it unchanged (must round-trip), turn and mirror pieces, save, reopen.</summary>
        public static void RoundTripCheck()
        {
            const string id = "m1_gate_a";
            var original = System.IO.File.ReadAllText(StageFiles.PathOf(id));
            var before = StageFiles.Load(id);
            var w = CreateInstance<SceneStageEditor>();
            try
            {
                w.OpenStage(id);
                w.Save();
                var after = StageFiles.Load(id);
                var same = before.props.Count == after.props.Count && before.decorations.Count == after.decorations.Count;
                for (var i = 0; same && i < before.props.Count; i++)
                {
                    var a = before.props[i];
                    var b = after.props[i];
                    same = a.id == b.id && Mathf.Abs(a.x - b.x) < 0.02f && Mathf.Abs(a.z - b.z) < 0.02f && Mathf.Abs(Mathf.DeltaAngle(a.rotation, b.rotation)) < 0.2f
                           && a.flip == b.flip && a.variant == b.variant && Mathf.Abs(a.roll - b.roll) < 0.01f;
                }
                Debug.Log($"[ZOMBIE-SCENE-EDIT] round trip unchanged: {same} ({after.props.Count} props, {after.decorations.Count} decorations)");
                var pieces = w._props.GetComponentsInChildren<StagePiece>().ToList();
                var mirrorable = pieces.First(p => !(Art.ArtLibrary.Get(p.id)?.NoFlip ?? true));
                var turned = pieces[0];
                var yaw = turned.transform.eulerAngles.y;
                w.Turn(new List<StagePiece> { turned }, 90f);
                var wasFlipped = mirrorable.flip;
                w.Mirror(new List<StagePiece> { mirrorable });
                w.Save();
                var edited = StageFiles.Load(id);
                var t = edited.props.Find(p => p.id == turned.id && Mathf.Abs(p.x - turned.transform.position.x) < 0.02f && Mathf.Abs(p.z - turned.transform.position.z) < 0.02f);
                var m = edited.props.Find(p => p.id == mirrorable.id && Mathf.Abs(p.x - mirrorable.transform.position.x) < 0.02f && Mathf.Abs(p.z - mirrorable.transform.position.z) < 0.02f);
                Debug.Log($"[ZOMBIE-SCENE-EDIT] turned {Mathf.Abs(Mathf.DeltaAngle(t.rotation, yaw + 90f)) < 0.2f}, mirrored {m.flip != wasFlipped}");
            }
            finally
            {
                w.Close(true);
                System.IO.File.WriteAllText(StageFiles.PathOf(id), original);
                Object.DestroyImmediate(w);
            }
        }
        // ------------------------------------------------------------------ open / save / close

        private void OpenStage(string id)
        {
            if (_root != null && _dirty && !EditorUtility.DisplayDialog("Scene Stage Editor", $"Discard unsaved changes to {_openId}?", "Discard", "Cancel")) return;
            Close(true);
            var map = StageFiles.Load(id);
            _openId = id;
            _root = new GameObject($"{RootName} {id}") { hideFlags = HideFlags.DontSave };
            // The fixed backdrop: ground, walls, fence, cover, bushes and the start pad. Not selectable.
            var backdrop = new GameObject("Backdrop (not editable)").transform;
            backdrop.SetParent(_root.transform, false);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.name = "Ground";
            ground.transform.SetParent(backdrop, false);
            ground.transform.SetPositionAndRotation(new Vector3(map.width * 0.5f, 0f, map.height * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            ground.transform.localScale = new Vector3(map.width + 40f, map.height + 40f, 1f);
            Art.GroundLayer.Apply(ground.GetComponent<MeshRenderer>(), map, map.baseGround, null);
            _map = map;
            _backdrop = backdrop;
            _groundRenderer = ground.GetComponent<MeshRenderer>();
            BuildGrid();
            if (map.spawnArea != null && map.spawnArea.w > 0)
            {
                var pad = new GameObject("Helipad");
                pad.transform.SetParent(backdrop, false);
                pad.AddComponent<MeshFilter>().sharedMesh = Art.HelipadArt.Build(map.spawnArea.ToRect());
                pad.AddComponent<MeshRenderer>().sharedMaterial = Art.ArtMaterials.Stylized;
            }
            foreach (var t in backdrop.GetComponentsInChildren<Transform>(true)) SceneVisibilityManager.instance.DisablePicking(t.gameObject, false);
            _props = new GameObject("Props").transform;
            _props.SetParent(_root.transform, false);
            _decorations = new GameObject("Decorations").transform;
            _decorations.SetParent(_root.transform, false);
            foreach (var d in map.props) Spawn(d, false);
            foreach (var d in map.decorations) Spawn(d, true);
            _dirty = false;
            Selection.activeGameObject = null;
            var view = SceneView.lastActiveSceneView;
            if (view != null) view.LookAt(new Vector3(map.spawnArea?.x ?? map.width * 0.5f, 0f, map.spawnArea?.z ?? map.height * 0.5f), Quaternion.Euler(60f, 0f, 0f), 30f);
        }

        private void Save()
        {
            // The edited map (its tiles and painted ground included), with the objects as they stand in the scene.
            var map = _map ?? StageFiles.Load(_openId);
            map.props = Records(_props);
            map.decorations = Records(_decorations);
            StageFiles.Save(_openId, map);
            _dirty = false;
            Debug.Log($"[ZOMBIE-SCENE-EDIT] saved {_openId}: {map.props.Count} props, {map.decorations.Count} decorations");
            CheckMap(map);
            ShowNotification(new GUIContent("Saved"));
        }

        private static List<MapDecoration> Records(Transform parent)
        {
            var list = new List<MapDecoration>();
            foreach (Transform child in parent)
            {
                var piece = child.GetComponent<StagePiece>();
                if (piece != null) list.Add(piece.ToRecord());
            }
            return list;
        }

        private void Close(bool silent)
        {
            if (!silent && _dirty && !EditorUtility.DisplayDialog("Scene Stage Editor", $"Close {_openId} without saving?", "Close", "Cancel")) return;
            foreach (var old in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.name.StartsWith(RootName) && g.transform.parent == null).ToList())
                Object.DestroyImmediate(old);
            _root = null;
            _openId = null;
            ResetGrid();
            _dirty = false;
        }

        // ------------------------------------------------------------------ pieces

        private StagePiece Spawn(MapDecoration d, bool decoration)
        {
            var go = new GameObject(d.id) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(decoration ? _decorations : _props, false);
            go.transform.SetPositionAndRotation(new Vector3(d.x, 0f, d.z), Quaternion.Euler(0f, d.rotation, 0f));
            var p = go.AddComponent<StagePiece>();
            p.id = d.id;
            p.decoration = decoration;
            p.scale = d.scale > 0f ? d.scale : 1f;
            p.flip = d.flip;
            p.variant = d.variant;
            p.roll = d.roll;
            p.pitch = d.pitch;
            Rebuild(p);
            return p;
        }

        /// <summary>The visual child, made the way the game makes it (model, size, pose, mirror, colour version).</summary>
        private static void Rebuild(StagePiece p)
        {
            for (var i = p.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(p.transform.GetChild(i).gameObject);
            var piece = Art.ArtLibrary.Get(p.id);
            if (piece == null) return;
            var record = p.ToRecord();
            // Hiding grass: the same aligned clumps as in the game (owner, 2026-10-05).
            if (piece.Bush && Art.GrassLayer.Available)
            {
                Art.GrassLayer.Prop(p.transform, p.transform.position, piece.Footprint.x * p.scale, 0);
                foreach (var t in p.GetComponentsInChildren<Transform>(true)) if (t != p.transform) t.gameObject.hideFlags = HideFlags.DontSave;
                return;
            }
            if (piece.Model != null)
            {
                var visual = Art.PropLayer.PlaceModel(p.transform, record, piece);
                if (visual != null) visual.hideFlags = HideFlags.DontSave;
                return;
            }
            var kit = new Art.MeshKit { Transform = Matrix4x4.Scale(new Vector3(p.flip && !piece.NoFlip ? -p.scale : p.scale, p.scale, p.scale)) };
            piece.Build(kit, Mathf.FloorToInt(record.x * 31f) * 7919 + Mathf.FloorToInt(record.z * 17f));
            var go = new GameObject("Visual") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(p.transform, false);
            go.transform.localPosition = new Vector3(0f, p.decoration ? 0.012f : 0f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh(p.id);
            go.AddComponent<MeshRenderer>().sharedMaterial = Art.ArtMaterials.Stylized;
        }

        private static List<StagePiece> Selected() =>
            Selection.gameObjects.Select(g => g.GetComponentInParent<StagePiece>()).Where(p => p != null).Distinct().ToList();

        /// <summary>Hiding grass (owner, 2026-10-05): only added, deleted or moved, always on a cell centre, never turned.</summary>
        private static bool IsGrass(StagePiece p) => Art.ArtLibrary.Get(p.id)?.Bush ?? false;

        private static Vector3 CellCentre(Vector3 p) => new Vector3(Mathf.Floor(p.x) + 0.5f, 0f, Mathf.Floor(p.z) + 0.5f);

        private void Turn(List<StagePiece> pieces, float degrees)
        {
            foreach (var p in pieces)
            {
                if (IsGrass(p)) continue;   // hiding grass is never turned (owner, 2026-10-05)
                Undo.RecordObject(p.transform, "Turn");
                p.transform.rotation = Quaternion.Euler(0f, p.transform.eulerAngles.y + degrees, 0f);
            }
            _dirty = true;
        }

        private void Mirror(List<StagePiece> pieces)
        {
            foreach (var p in pieces)
            {
                var piece = Art.ArtLibrary.Get(p.id);
                if (piece != null && (piece.NoFlip || piece.Bush)) continue;   // buildings and hiding grass are never mirrored (owner)
                Undo.RecordObject(p, "Mirror");
                p.flip = !p.flip;
                Rebuild(p);
            }
            _dirty = true;
        }

        private void Pose(List<StagePiece> pieces, float roll, float pitch)
        {
            foreach (var p in pieces)
            {
                if (Art.ArtLibrary.Get(p.id)?.Model == null) continue;   // poses are for the owner's models
                Undo.RecordObject(p, "Pose");
                p.roll = roll;
                p.pitch = pitch;
                Rebuild(p);
            }
            _dirty = true;
        }

        private void Recolour(List<StagePiece> pieces, int step)
        {
            foreach (var p in pieces)
            {
                var piece = Art.ArtLibrary.Get(p.id);
                if (piece?.Model == null) continue;
                var count = Art.PackProps.Variants(piece.Model);
                Undo.RecordObject(p, "Colour");
                p.variant = (int)Mathf.Repeat(p.variant + step, count + 1);
                Rebuild(p);
            }
            _dirty = true;
        }

        private void Delete(List<StagePiece> pieces)
        {
            foreach (var p in pieces) Undo.DestroyObjectImmediate(p.gameObject);
            _dirty = true;
        }

        private void Add(Art.ArtLibrary.Piece piece)
        {
            var view = SceneView.lastActiveSceneView;
            var at = view != null ? view.pivot : Vector3.zero;
            var d = new MapDecoration { id = piece.Id, x = Mathf.Round(at.x * 2f) / 2f, z = Mathf.Round(at.z * 2f) / 2f, scale = 1f };
            if (piece.Bush)
            {
                d.x = Mathf.Floor(at.x) + 0.5f;
                d.z = Mathf.Floor(at.z) + 0.5f;
            }
            var p = Spawn(d, piece.Kind == Art.ArtLibrary.Kind.Decoration);
            Undo.RegisterCreatedObjectUndo(p.gameObject, "Add " + piece.Name);
            Selection.activeGameObject = p.gameObject;
            _dirty = true;
        }

        /// <summary>
        /// Objects stay on the ground and only turn around the vertical; their size never changes (owner: sizes and
        /// colours are not edited). Anything the move/rotate/scale tools did beyond that is put back.
        /// </summary>
        private void KeepOnGround()
        {
            if (_root == null) return;
            foreach (var go in Selection.gameObjects)
            {
                var p = go != null ? go.GetComponent<StagePiece>() : null;
                if (p == null) continue;
                var t = p.transform;
                var changed = false;
                // Hiding grass snaps to the cell under it and stays unturned.
                if (IsGrass(p) && (t.position != CellCentre(t.position) || Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, 0f)) > 0.01f))
                {
                    t.SetPositionAndRotation(CellCentre(t.position), Quaternion.identity);
                    changed = true;
                }
                if (Mathf.Abs(t.position.y) > 0.0001f)
                {
                    t.position = new Vector3(t.position.x, 0f, t.position.z);
                    changed = true;
                }
                var e = t.eulerAngles;
                if (Mathf.Abs(Mathf.DeltaAngle(e.x, 0f)) > 0.01f || Mathf.Abs(Mathf.DeltaAngle(e.z, 0f)) > 0.01f)
                {
                    t.rotation = Quaternion.Euler(0f, e.y, 0f);
                    changed = true;
                }
                if (t.localScale != Vector3.one)
                {
                    t.localScale = Vector3.one;
                    changed = true;
                }
                if (t.hasChanged)
                {
                    t.hasChanged = false;
                    changed = true;
                }
                if (changed)
                {
                    _dirty = true;
                    Repaint();
                }
            }
        }
    }
}
