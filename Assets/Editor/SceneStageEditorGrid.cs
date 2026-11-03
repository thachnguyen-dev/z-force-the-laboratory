using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// Grass mode of the Scene Stage Editor (owner, 2026-10-05: the owner edits existing stages only, placing obstacles,
    /// decorations and hiding grass; Claude lays out new stages, so walls, markers and ground paint are no longer edited
    /// here). Click or drag in the Scene view to paint hiding grass cells; hold Shift to erase them. Grass goes only on
    /// open ground and the eraser removes only grass, so walls, markers and the fence can't be touched. The grass
    /// rebuilds in 3D when you let go of the mouse; strokes have their own undo. Markers show as coloured discs with
    /// their letter (read only) so obstacles aren't put on them. Save checks the map and lists any marker a player
    /// couldn't reach.
    /// </summary>
    public partial class SceneStageEditor
    {
        private struct MarkerLook
        {
            public Tile Tile;
            public string Name, Letter;
            public Color Colour;
        }

        private static readonly MarkerLook[] MarkerLooks =
        {
            new MarkerLook { Tile = Tile.PlayerSpawn, Name = "player start", Letter = "P", Colour = new Color(0.25f, 0.55f, 1f) },
            new MarkerLook { Tile = Tile.ZombieSpawn, Name = "zombie spawn", Letter = "z", Colour = new Color(0.6f, 0.15f, 0.15f) },
            new MarkerLook { Tile = Tile.LootSpawn, Name = "loot", Letter = "l", Colour = new Color(1f, 0.85f, 0.25f) },
            new MarkerLook { Tile = Tile.ChestSpawn, Name = "equipment chest", Letter = "c", Colour = new Color(0.6f, 0.9f, 0.4f) },
            new MarkerLook { Tile = Tile.Incubator, Name = "zombie incubator", Letter = "n", Colour = new Color(0.7f, 0.3f, 0.8f) },
            new MarkerLook { Tile = Tile.Barrel, Name = "fuel barrel", Letter = "r", Colour = new Color(1f, 0.45f, 0.15f) },
            new MarkerLook { Tile = Tile.BossSpawn, Name = "Main Boss", Letter = "X", Colour = new Color(1f, 0.15f, 0.35f) },
            new MarkerLook { Tile = Tile.SubBossSpawn, Name = "sub-boss", Letter = "S", Colour = new Color(1f, 0.5f, 0.1f) },
            new MarkerLook { Tile = Tile.Hostage, Name = "hostage", Letter = "H", Colour = Color.white },
            new MarkerLook { Tile = Tile.BombTarget, Name = "bomb target", Letter = "B", Colour = new Color(1f, 0.2f, 0.1f) },
            new MarkerLook { Tile = Tile.Vaccine, Name = "vaccine", Letter = "V", Colour = new Color(0.35f, 1f, 0.5f) },
            new MarkerLook { Tile = Tile.RivalSpawn, Name = "rival team", Letter = "E", Colour = new Color(0.9f, 0.2f, 0.65f) },
        };

        private static readonly Color GrassColour = new Color(0.35f, 0.8f, 0.3f);

        private StageMap _map;
        private Transform _backdrop;
        private MeshRenderer _groundRenderer;
        private bool _paint;
        private int _brushSize = 1;
        private bool _stroking;
        private readonly List<List<(int x, int z, Tile tile)>> _undo = new List<List<(int, int, Tile)>>();
        private List<(int x, int z, Tile tile)> _stroke;
        private readonly List<(Vector2Int cell, Tile tile)> _markers = new List<(Vector2Int, Tile)>();
        private int _grassCells;
        private List<string> _problems = new List<string>();

        /// <summary>Batch check of grass painting: paint, erase, undo, save; walls and markers can't be painted over.</summary>
        public static void PaintCheck()
        {
            const string id = "m1_gate_a";
            var original = System.IO.File.ReadAllText(StageFiles.PathOf(id));
            var before = StageFiles.Load(id);
            var w = CreateInstance<SceneStageEditor>();
            try
            {
                w.OpenStage(id);
                Vector2Int a = default, wall = default, marker = default;
                for (var x = 20; x < before.width - 20; x++)
                for (var z = 20; z < before.height - 20; z++)
                {
                    if (a == default && before[x, z] == Tile.Ground && before[x + 1, z] == Tile.Ground && before[x + 2, z] == Tile.Ground) a = new Vector2Int(x, z);
                    if (wall == default && before[x, z] == Tile.Wall) wall = new Vector2Int(x, z);
                    if (marker == default && Tiles.IsMarker(before[x, z])) marker = new Vector2Int(x, z);
                }
                void Stroke(Vector2Int cell, bool erase)
                {
                    w._brushSize = 1;
                    w._stroke = new List<(int, int, Tile)>();
                    w._stroking = true;
                    w.PaintAt(cell, erase);
                    w.EndStroke();
                }
                Stroke(a, false);
                Stroke(a + Vector2Int.right, false);
                Stroke(a + Vector2Int.right * 2, false);
                Stroke(wall, false);
                Stroke(marker, false);
                Stroke(wall, true);
                Stroke(marker, true);
                Stroke(a + Vector2Int.right * 2, true);
                Stroke(a + Vector2Int.right, true);
                w.UndoStroke();   // brings back the grass at a + 1
                w.Save();
                var after = StageFiles.Load(id);
                var ok = after[a.x, a.y] == Tile.Bush && after[a.x + 1, a.y] == Tile.Bush && after[a.x + 2, a.y] == Tile.Ground
                         && after[wall.x, wall.y] == Tile.Wall && after[marker.x, marker.y] == before[marker.x, marker.y];
                var changed = 0;
                for (var x = 0; x < after.width; x++)
                for (var z = 0; z < after.height; z++)
                    if (after[x, z] != before[x, z] || after.SurfaceAt(x, z) != before.SurfaceAt(x, z)) changed++;
                Debug.Log($"[ZOMBIE-SCENE-EDIT] grass ok {ok}, cells changed {changed} (expected 2), markers drawn {w._markers.Count}, " +
                          $"problems: {string.Join(" | ", w._problems)}");
            }
            finally
            {
                w.Close(true);
                System.IO.File.WriteAllText(StageFiles.PathOf(id), original);
                Object.DestroyImmediate(w);
            }
        }

        private void ResetGrid()
        {
            _map = null;
            _backdrop = null;
            _groundRenderer = null;
            _undo.Clear();
            _markers.Clear();
            _problems.Clear();
            _stroking = false;
        }

        // ------------------------------------------------------------------ backdrop

        /// <summary>(Re)builds the 3D grid (walls, cover, the fence, grass clumps) and the painted ground from the map.</summary>
        private void BuildGrid()
        {
            if (_map == null || _backdrop == null) return;
            var old = new List<GameObject>();
            foreach (Transform child in _backdrop)
                if (child.name == "Tiles" || child.name == "Grass") old.Add(child.gameObject);
            foreach (var go in old) Object.DestroyImmediate(go);
            TileAssembler.Build(_map, Resources.Load<TileSet>(TileSet.ResourceName), _backdrop);
            Art.GrassLayer.BuildCells(_map, _backdrop);
            if (_groundRenderer != null) Art.GroundLayer.Apply(_groundRenderer, _map, _map.baseGround, null);
            foreach (var t in _backdrop.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.hideFlags = HideFlags.DontSave;
                SceneVisibilityManager.instance.DisablePicking(t.gameObject, false);
            }
            CollectMarkers();
        }

        private void CollectMarkers()
        {
            _markers.Clear();
            _grassCells = 0;
            if (_map == null) return;
            for (var x = 0; x < _map.width; x++)
            for (var z = 0; z < _map.height; z++)
            {
                if (Tiles.IsMarker(_map[x, z])) _markers.Add((new Vector2Int(x, z), _map[x, z]));
                if (_map[x, z] == Tile.Bush) _grassCells++;
            }
        }

        // ------------------------------------------------------------------ window

        private void DrawPaintGUI()
        {
            EditorGUILayout.HelpBox("Click or drag in the Scene view to paint hiding grass. Hold Shift to erase it.\n" +
                                    "Grass goes only on open ground; walls, markers and the fence are never changed.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Brush size", GUILayout.Width(70));
                _brushSize = GUILayout.Toolbar(_brushSize - 1, new[] { "1", "2", "3" }) + 1;
                using (new EditorGUI.DisabledScope(_undo.Count == 0))
                    if (GUILayout.Button($"Undo ({_undo.Count})", GUILayout.Width(90))) UndoStroke();
            }
            EditorGUILayout.LabelField($"Hiding grass cells on this stage: {_grassCells}", EditorStyles.miniLabel);
            DrawMarkerKey();
        }

        /// <summary>What the marker discs in the Scene view mean, with this stage's counts.</summary>
        private void DrawMarkerKey()
        {
            if (_map == null) return;
            var counts = new Dictionary<Tile, int>();
            foreach (var (_, tile) in _markers) counts[tile] = counts.TryGetValue(tile, out var n) ? n + 1 : 1;
            var line = new System.Text.StringBuilder("Markers (keep obstacles off them): ");
            foreach (var m in MarkerLooks)
                if (counts.TryGetValue(m.Tile, out var n)) line.Append($"{m.Letter} {m.Name} ×{n}   ");
            EditorGUILayout.LabelField(line.ToString(), EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawProblems()
        {
            if (_problems.Count == 0) return;
            EditorGUILayout.HelpBox(string.Join("\n", _problems), _problems.Exists(p => p.StartsWith("Error") || p.Contains("can't be reached"))
                ? MessageType.Warning : MessageType.Info);
        }

        /// <summary>After saving: the map's own checks, and any marker the players couldn't walk to.</summary>
        private void CheckMap(StageMap map)
        {
            _problems = StageMapBuilder.Validate(map);
            var unreachable = StageForge.Unreachable(map);
            if (unreachable.Count > 0)
            {
                var first = string.Join(", ", unreachable.GetRange(0, Mathf.Min(6, unreachable.Count)).ConvertAll(c => $"({c.x}, {c.y})"));
                _problems.Add($"{unreachable.Count} marker(s) can't be reached from the start: {first}{(unreachable.Count > 6 ? "…" : "")}");
            }
            if (_problems.Count == 0) _problems.Add("Checked: no problems found.");
            Repaint();
        }

        // ------------------------------------------------------------------ scene

        private void OnSceneGrid(SceneView view)
        {
            if (_map == null || _root == null) return;
            DrawMarkers();
            if (!_paint) return;
            var e = Event.current;
            // Painting owns the left mouse button in the Scene view while Grass mode is on (no selecting).
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            var cell = CellUnder(e.mousePosition);
            if (cell.HasValue) DrawBrushOutline(cell.Value, e.shift);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Z && (e.control || e.command))
            {
                UndoStroke();
                e.Use();
                return;
            }
            if (e.button != 0 || e.alt) return;
            if (e.type == EventType.MouseDown && cell.HasValue)
            {
                _stroking = true;
                _stroke = new List<(int, int, Tile)>();
                PaintAt(cell.Value, e.shift);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _stroking && cell.HasValue)
            {
                PaintAt(cell.Value, e.shift);
                e.Use();
            }
            else if ((e.type == EventType.MouseUp || e.type == EventType.MouseLeaveWindow) && _stroking)
            {
                EndStroke();
                e.Use();
            }
            if (e.type == EventType.MouseMove) view.Repaint();
        }

        private Vector2Int? CellUnder(Vector2 mouse)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return null;
            var t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return null;
            var p = ray.origin + ray.direction * t;
            var x = Mathf.FloorToInt(p.x);
            var z = Mathf.FloorToInt(p.z);
            if (x < 0 || z < 0 || x >= _map.width || z >= _map.height) return null;
            return new Vector2Int(x, z);
        }

        private IEnumerable<Vector2Int> BrushCells(Vector2Int centre)
        {
            var lo = -(_brushSize - 1) / 2;
            for (var dx = lo; dx < lo + _brushSize; dx++)
            for (var dz = lo; dz < lo + _brushSize; dz++)
                yield return new Vector2Int(centre.x + dx, centre.y + dz);
        }

        private void DrawBrushOutline(Vector2Int centre, bool erase)
        {
            Handles.color = erase ? new Color(1f, 0.3f, 0.3f, 0.9f) : new Color(GrassColour.r, GrassColour.g, GrassColour.b, 0.9f);
            var lo = -(_brushSize - 1) / 2;
            var x0 = centre.x + lo;
            var z0 = centre.y + lo;
            var y = 0.06f;
            var s = _brushSize;
            Handles.DrawAAPolyLine(4f, new Vector3(x0, y, z0), new Vector3(x0, y, z0 + s), new Vector3(x0 + s, y, z0 + s), new Vector3(x0 + s, y, z0), new Vector3(x0, y, z0));
        }

        /// <summary>Grass onto open ground, or (erasing) grass back to ground. No other cell is ever changed.</summary>
        private void PaintAt(Vector2Int centre, bool erase)
        {
            foreach (var c in BrushCells(centre))
            {
                if (c.x < 0 || c.y < 0 || c.x >= _map.width || c.y >= _map.height) continue;
                var tile = _map[c.x, c.y];
                if (erase && tile == Tile.Bush) SetCell(c, Tile.Ground);
                else if (!erase && tile == Tile.Ground) SetCell(c, Tile.Bush);
            }
            CollectMarkers();
            SceneView.RepaintAll();
        }

        private void SetCell(Vector2Int c, Tile tile)
        {
            var old = _map[c.x, c.y];
            if (old == tile) return;
            // Remember the first state of each cell in this stroke.
            if (!_stroke.Exists(s => s.x == c.x && s.z == c.y)) _stroke.Add((c.x, c.y, old));
            _map[c.x, c.y] = tile;
        }

        private void EndStroke()
        {
            _stroking = false;
            if (_stroke == null || _stroke.Count == 0) return;
            _undo.Add(_stroke);
            if (_undo.Count > 100) _undo.RemoveAt(0);
            _stroke = null;
            _dirty = true;
            BuildGrid();
            Repaint();
        }

        private void UndoStroke()
        {
            if (_undo.Count == 0 || _map == null) return;
            var last = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            foreach (var (x, z, tile) in last) _map[x, z] = tile;
            _dirty = true;
            BuildGrid();
            Repaint();
            SceneView.RepaintAll();
        }

        private void DrawMarkers()
        {
            foreach (var (cell, tile) in _markers)
            {
                var i = System.Array.FindIndex(MarkerLooks, m => m.Tile == tile);
                if (i < 0) continue;
                var m = MarkerLooks[i];
                var p = new Vector3(cell.x + 0.5f, 0.08f, cell.y + 0.5f);
                Handles.color = new Color(m.Colour.r, m.Colour.g, m.Colour.b, 0.85f);
                Handles.DrawSolidDisc(p, Vector3.up, 0.42f);
                Handles.color = Color.black;
                Handles.DrawWireDisc(p, Vector3.up, 0.42f);
                Handles.Label(p + new Vector3(-0.12f, 0f, 0.15f), m.Letter, EditorStyles.boldLabel);
            }
        }
    }
}
