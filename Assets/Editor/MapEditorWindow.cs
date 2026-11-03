using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ZombieGame.EditorTools
{
    /// <summary>
    /// The stage editor (owner, 2026-09-30: every stage's map must stay freely editable). Everything a stage is made of is
    /// edited here and saved to its light JSON file: cells (terrain, walls, cover, water, bushes, floors and every marker),
    /// buildings (roofs), areas (spawning), the Spawn and Boss Areas, placed zombies, the stage's conditions and mission,
    /// and the stage list. Nothing is generated over a stage you've edited.
    /// </summary>
    public class MapEditorWindow : EditorWindow
    {
        private enum Tool { Paint, Building, Area, SpawnArea, BossArea, Zombie, Select, Decoration, Surface, Prop }

        private static readonly Tile[] Palette =
        {
            Tile.Ground, Tile.Wall, Tile.Cliff, Tile.Cover, Tile.Bush, Tile.LootSpawn, Tile.ZombieSpawn,
            Tile.PlayerSpawn, Tile.BossSpawn, Tile.Hostage, Tile.BombTarget, Tile.Vaccine, Tile.RivalSpawn, Tile.SubBossSpawn, Tile.Barrel, Tile.ChestSpawn, Tile.Incubator,
        };

        private static readonly string[] Weathers = { "clear", "rain" };
        private static readonly string[] Missions = { "boss", "rescue", "bomb", "vaccine" };

        private string _id;
        private StageMap _map;
        private bool _dirty;
        private Tool _tool = Tool.Paint;
        private Tile _brush = Tile.Wall;
        private Surface _surfaceBrush = Surface.Grass;

        private static readonly Surface[] SurfacePalette = { Surface.None, Surface.Floor, Surface.Grass, Surface.Grass2, Surface.Road, Surface.Road2 };
        private int _brushSize = 1;
        private ZombieType _zombieType = ZombieType.Basic;
        private float _cell = 6f;
        private Vector2 _sideScroll;
        private Vector2 _gridScroll;
        private Texture2D _texture;
        private bool _textureStale = true;
        private List<string> _problems = new List<string>();

        // Rectangle drawing.
        private bool _dragging;
        private Vector2Int _dragFrom;
        private Vector2Int _dragTo;

        // Selection.
        private MapBuilding _building;
        private MapArea _area;

        // New stage.
        private string _newName = "New stage";
        private int _newWidth = 80;
        private int _newHeight = 80;
        private int _resizeWidth;
        private int _resizeHeight;

        private readonly List<string> _undo = new List<string>();

        [MenuItem("Zombie/Developer (Claude)/Map Editor")]
        public static void Open() => GetWindow<MapEditorWindow>("Map Editor");

        private void OnEnable()
        {
            if (_map == null && StageFiles.Exists(StageLibrary.TestStageId)) Load(StageLibrary.TestStageId);
        }

        private void OnGUI()
        {
            HandleShortcuts();
            EditorGUILayout.BeginHorizontal();
            _sideScroll = EditorGUILayout.BeginScrollView(_sideScroll, GUILayout.Width(340));
            DrawStageList();
            if (_map != null)
            {
                DrawStageSettings();
                DrawTools();
                DrawSelection();
                DrawProblems();
            }
            EditorGUILayout.EndScrollView();
            if (_map != null) DrawCanvas();
            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ stage list

        private void DrawStageList()
        {
            EditorGUILayout.LabelField("Stages (stage selection order)", EditorStyles.boldLabel);
            var index = StageFiles.ReadIndex();
            for (var i = 0; i < index.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var id = index[i];
                var style = id == _id ? EditorStyles.boldLabel : EditorStyles.label;
                if (GUILayout.Button(id, style, GUILayout.ExpandWidth(true))) Load(id);
                if (GUILayout.Button("▲", GUILayout.Width(24)) && i > 0) Swap(index, i, i - 1);
                if (GUILayout.Button("▼", GUILayout.Width(24)) && i < index.Count - 1) Swap(index, i, i + 1);
                if (GUILayout.Button("✕", GUILayout.Width(24)) && EditorUtility.DisplayDialog("Remove from list",
                        $"Remove '{id}' from stage selection? The file stays.", "Remove", "Cancel"))
                {
                    index.RemoveAt(i);
                    StageFiles.WriteIndex(index);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            foreach (var id in StageFiles.AllFiles())
            {
                if (index.Contains(id)) continue;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button($"{id} (not listed)", EditorStyles.miniLabel)) Load(id);
                if (GUILayout.Button("Add to list", GUILayout.Width(90)))
                {
                    index.Add(id);
                    StageFiles.WriteIndex(index);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
            _newName = EditorGUILayout.TextField("New stage name", _newName);
            EditorGUILayout.BeginHorizontal();
            _newWidth = EditorGUILayout.IntField("Size", _newWidth);
            _newHeight = EditorGUILayout.IntField(_newHeight);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("New blank stage")) CreateStage(BlankStage(_newName, _newWidth, _newHeight));
            GUI.enabled = _map != null;
            if (GUILayout.Button("Duplicate current")) CreateStage(StageMap.FromJson(Json(_map)), $"{_map.name} copy");
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8);
        }

        private static void Swap(List<string> list, int a, int b)
        {
            (list[a], list[b]) = (list[b], list[a]);
            StageFiles.WriteIndex(list);
        }

        /// <summary>A blank stage: ground, a cliff border (D-075), a Spawn Area with the player spawn, and a Boss Area.</summary>
        private static StageMap BlankStage(string name, int width, int height)
        {
            width = Mathf.Clamp(width, 16, 400);
            height = Mathf.Clamp(height, 16, 400);
            var map = StageMap.Blank(name, width, height);
            for (var x = 0; x < width; x++) map[x, 0] = map[x, height - 1] = Tile.Cliff;
            for (var z = 0; z < height; z++) map[0, z] = map[width - 1, z] = Tile.Cliff;
            map.spawnArea = new MapRect { name = "Spawn Area", x = 2, z = 2, w = 10, d = 10 };
            map.bossArea = new MapRect { name = "Boss Area", x = width - 14, z = height - 14, w = 12, d = 12 };
            map[6, 6] = Tile.PlayerSpawn;
            map[width - 8, height - 8] = Tile.BossSpawn;
            return map;
        }

        private void CreateStage(StageMap map, string name = null)
        {
            if (!ConfirmDiscard()) return;
            if (name != null) map.name = name;
            var id = StageFiles.IdFrom(map.name);
            StageFiles.Save(id, map);
            var index = StageFiles.ReadIndex();
            index.Add(id);
            StageFiles.WriteIndex(index);
            Load(id);
        }

        // ------------------------------------------------------------------ stage settings

        private void DrawStageSettings()
        {
            EditorGUILayout.LabelField($"Stage '{_id}'{(_dirty ? "  (unsaved)" : "")}", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var name = EditorGUILayout.TextField("Name", _map.name);
            var difficulty = EditorGUILayout.FloatField("Difficulty (spawn ×)", _map.difficulty);
            var weather = Weathers[Mathf.Max(0, System.Array.IndexOf(Weathers, _map.weather))];
            weather = Weathers[EditorGUILayout.Popup("Weather", System.Array.IndexOf(Weathers, weather), Weathers)];
            var dark = EditorGUILayout.Toggle("Darkness", _map.dark);
            var lootScale = EditorGUILayout.Slider(new GUIContent("Loot amount", "1 = normal; the Vaccine stage uses 0.3"), _map.lootScale <= 0f ? 1f : _map.lootScale, 0.1f, 2f);
            var mission = Missions[Mathf.Max(0, System.Array.IndexOf(Missions, _map.mission))];
            mission = Missions[EditorGUILayout.Popup("Mission", System.Array.IndexOf(Missions, mission), Missions)];
            var tileSet = EditorGUILayout.TextField(new GUIContent("Tile set", "Map blocks: Resources/TileSets/<name>; empty = default"), _map.tileSet ?? "");
            var groundIds = BaseGround.Ids();
            var groundNames = new string[groundIds.Length];
            for (var i = 0; i < groundIds.Length; i++) groundNames[i] = BaseGround.Presets[i].Name;
            var groundIndex = EditorGUILayout.Popup(new GUIContent("Base ground", "The bottom layer under the whole stage"), Mathf.Max(0, System.Array.IndexOf(groundIds, _map.baseGround)), groundNames);
            if (EditorGUI.EndChangeCheck())
            {
                Record();
                _map.name = name;
                _map.difficulty = difficulty;
                _map.weather = weather;
                _map.dark = dark;
                _map.lootScale = lootScale;
                _map.mission = mission;
                _map.tileSet = tileSet;
                _map.baseGround = groundIds[groundIndex];
                Changed();
            }
            EditorGUILayout.BeginHorizontal();
            if (_resizeWidth == 0) _resizeWidth = _map.width;
            if (_resizeHeight == 0) _resizeHeight = _map.height;
            _resizeWidth = EditorGUILayout.IntField("Size", _resizeWidth);
            _resizeHeight = EditorGUILayout.IntField(_resizeHeight);
            if (GUILayout.Button("Resize", GUILayout.Width(60)) && (_resizeWidth != _map.width || _resizeHeight != _map.height)) Resize(_resizeWidth, _resizeHeight);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_dirty ? "Save *" : "Save")) Save();
            if (GUILayout.Button("Undo (Ctrl+Z)")) Undo();
            if (GUILayout.Button("Play this stage")) Play();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8);
        }

        /// <summary>Grows or shrinks the map from its north-east corner; everything else keeps its cells.</summary>
        private void Resize(int width, int height)
        {
            width = Mathf.Clamp(width, 16, 400);
            height = Mathf.Clamp(height, 16, 400);
            Record();
            var old = _map;
            var map = StageMap.FromJson(Json(old));
            map.width = width;
            map.height = height;
            map.rows = new List<string>();
            for (var r = 0; r < height; r++) map.rows.Add(new string('.', width));
            map.Parse();
            for (var x = 0; x < Mathf.Min(width, old.width); x++)
            for (var z = 0; z < Mathf.Min(height, old.height); z++)
            {
                map[x, z] = old[x, z];
                map.SetSurface(x, z, old.SurfaceAt(x, z));
            }
            _map = map;
            Changed();
        }

        // ------------------------------------------------------------------ tools

        private void DrawTools()
        {
            EditorGUILayout.LabelField("Tool", EditorStyles.boldLabel);
            _tool = (Tool)GUILayout.SelectionGrid((int)_tool, new[] { "Paint", "Building", "Area", "Spawn Area", "Boss Area", "Zombie", "Select", "Decoration", "Surface", "Prop" }, 4);
            switch (_tool)
            {
                case Tool.Paint:
                    var names = new string[Palette.Length];
                    for (var i = 0; i < Palette.Length; i++) names[i] = $"{Tiles.ToChar(Palette[i])} {Palette[i]}";
                    var selected = Mathf.Max(0, System.Array.IndexOf(Palette, _brush));
                    _brush = Palette[GUILayout.SelectionGrid(selected, names, 3)];
                    _brushSize = EditorGUILayout.IntSlider("Brush size", _brushSize, 1, 10);
                    EditorGUILayout.HelpBox("Left-drag paints, right-drag erases to ground. Markers are single cells on walkable ground.", MessageType.None);
                    break;
                case Tool.Surface:
                    var surfaceNames = new string[SurfacePalette.Length];
                    for (var i = 0; i < SurfacePalette.Length; i++)
                        surfaceNames[i] = SurfacePalette[i] == Surface.None ? "None (erase)" : $"{Surfaces.ToChar(SurfacePalette[i])} {SurfacePalette[i]}";
                    _surfaceBrush = SurfacePalette[GUILayout.SelectionGrid(Mathf.Max(0, System.Array.IndexOf(SurfacePalette, _surfaceBrush)), surfaceNames, 3)];
                    _brushSize = EditorGUILayout.IntSlider("Brush size", _brushSize, 1, 10);
                    EditorGUILayout.HelpBox("Layer 2: flat looks (grass, roads, floors) with no gameplay effect. Left-drag paints, right-drag erases. " +
                        "Walls, bushes and markers stay where they are.", MessageType.None);
                    break;
                case Tool.Zombie:
                    _zombieType = (ZombieType)EditorGUILayout.EnumPopup("Zombie type", _zombieType);
                    if (ZombieTypes.IsBoss(_zombieType)) _zombieType = ZombieType.Basic;
                    EditorGUILayout.HelpBox("Left-click places a zombie on that exact cell, right-click removes it. The Main Boss is the X cell.", MessageType.None);
                    break;
                case Tool.Select:
                    EditorGUILayout.HelpBox("Click inside a building or area to edit or delete it.", MessageType.None);
                    break;
                case Tool.Decoration:
                case Tool.Prop:
                    DrawPieceTool();
                    break;
                default:
                    EditorGUILayout.HelpBox("Drag a rectangle on the map.", MessageType.None);
                    break;
            }
            _cell = EditorGUILayout.Slider("Zoom", _cell, 2f, 20f);
            EditorGUILayout.Space(8);
        }

        // Prop and decoration tools (owner, 2026-10-02: free placement, not tied to the grid). Props are physical;
        // decorations are looks only. Both list the built-in pieces plus any prefabs in Resources/Props or Decorations.
        private readonly Dictionary<Art.ArtLibrary.Kind, string[]> _pieceIds = new Dictionary<Art.ArtLibrary.Kind, string[]>();
        private readonly Dictionary<Art.ArtLibrary.Kind, int> _piece = new Dictionary<Art.ArtLibrary.Kind, int>();
        private float _pieceRotation;
        private bool _pieceRandomTurn = true;
        private float _pieceScale = 1f;
        private bool _pieceFlip;
        private int _pieceVariant;
        private float _pieceRoll, _piecePitch;
        private bool _pieceSnap;

        private Art.ArtLibrary.Kind PieceKind => _tool == Tool.Prop ? Art.ArtLibrary.Kind.Prop : Art.ArtLibrary.Kind.Decoration;
        private List<MapDecoration> PieceList => _tool == Tool.Prop ? _map.props : _map.decorations;

        private string[] PieceIds(Art.ArtLibrary.Kind kind, bool refresh)
        {
            if (!refresh && _pieceIds.TryGetValue(kind, out var cached)) return cached;
            var ids = new List<string>();
            foreach (var p in Art.ArtLibrary.OfKind(kind)) ids.Add(p.Id);
            foreach (var p in Resources.LoadAll<GameObject>(kind == Art.ArtLibrary.Kind.Prop ? "Props" : "Decorations"))
                if (!ids.Contains(p.name)) ids.Add(p.name);
            return _pieceIds[kind] = ids.ToArray();
        }

        private void DrawPieceTool()
        {
            var kind = PieceKind;
            var refresh = GUILayout.Button("Refresh list", GUILayout.Width(100));
            var ids = PieceIds(kind, refresh);
            if (ids.Length == 0) return;
            _piece.TryGetValue(kind, out var selected);
            var labels = new string[ids.Length];
            for (var i = 0; i < ids.Length; i++) labels[i] = Art.ArtLibrary.Get(ids[i])?.Name ?? ids[i];
            _piece[kind] = GUILayout.SelectionGrid(Mathf.Clamp(selected, 0, ids.Length - 1), labels, 2);
            _pieceRandomTurn = EditorGUILayout.Toggle("Random turn", _pieceRandomTurn);
            GUI.enabled = !_pieceRandomTurn;
            _pieceRotation = EditorGUILayout.Slider("Turn (degrees)", _pieceRotation, 0f, 359f);
            GUI.enabled = true;
            _pieceScale = EditorGUILayout.Slider("Scale", _pieceScale, 0.25f, 4f);
            // The owner's pack (2026-10-04): vehicles and objects may be mirrored, buildings never; colours vary.
            var chosen = ids.Length > 0 ? Art.ArtLibrary.Get(ids[Mathf.Clamp(_piece[kind], 0, ids.Length - 1)]) : null;
            GUI.enabled = chosen == null || !chosen.NoFlip;
            _pieceFlip = EditorGUILayout.Toggle(chosen != null && chosen.NoFlip ? "Mirror (not for buildings)" : "Mirror", _pieceFlip && GUI.enabled);
            GUI.enabled = true;
            if (chosen != null && chosen.Model != null)
            {
                var variants = Art.PackProps.Variants(chosen.Model);
                _pieceVariant = EditorGUILayout.IntSlider(new GUIContent("Colour (0 = auto)", $"{variants} colour versions"), Mathf.Min(_pieceVariant, variants), 0, variants);
                // Poses (2026-10-04): tip it over (roll: 90 on its side, 180 upside down) or nose-down (pitch).
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Upright")) _pieceRoll = _piecePitch = 0f;
                    if (GUILayout.Button("Leaning")) { _pieceRoll = 18f; _piecePitch = 0f; }
                    if (GUILayout.Button("On its side")) { _pieceRoll = 90f; _piecePitch = 0f; }
                    if (GUILayout.Button("Upside down")) { _pieceRoll = 180f; _piecePitch = 0f; }
                }
                _pieceRoll = EditorGUILayout.Slider("Roll (tip sideways)", _pieceRoll, -180f, 180f);
                _piecePitch = EditorGUILayout.Slider("Pitch (nose up/down)", _piecePitch, -45f, 45f);
            }
            _pieceSnap = EditorGUILayout.Toggle("Snap to half cells", _pieceSnap);
            var list = PieceList;
            EditorGUILayout.HelpBox(kind == Art.ArtLibrary.Kind.Prop
                ? "Left-click places the prop exactly where you click; right-click removes the nearest one. Props block movement; solid ones also stop bullets " +
                  $"and sight, shoot-through ones (benches, barriers, shelves, chairs…) let them pass, and bushes hide whoever is inside. " +
                  $"Outlines show their footprints. This stage has {list.Count}."
                : "Left-click places the decoration exactly where you click; right-click removes the nearest one. Decorations are looks " +
                  $"only: no collision, no effect on sight. This stage has {list.Count}.", MessageType.None);
            if (list.Count > 0 && GUILayout.Button(kind == Art.ArtLibrary.Kind.Prop ? "Remove all props" : "Remove all decorations") &&
                EditorUtility.DisplayDialog("Remove", $"Remove all {list.Count} from this stage?", "Remove", "Cancel"))
            {
                Record();
                list.Clear();
                Changed(texture: false);
            }
        }

        private void HandlePieceInput(Rect area, Event e)
        {
            if (e.type != EventType.MouseDown || (e.button != 0 && e.button != 1)) return;
            var wx = (e.mousePosition.x - area.x) / _cell;
            var wz = _map.height - (e.mousePosition.y - area.y) / _cell;
            if (_pieceSnap)
            {
                wx = Mathf.Round(wx * 2f) / 2f;
                wz = Mathf.Round(wz * 2f) / 2f;
            }
            Record();
            var list = PieceList;
            var ids = PieceIds(PieceKind, false);
            if (e.button == 0 && ids.Length > 0)
            {
                _piece.TryGetValue(PieceKind, out var selected);
                list.Add(new MapDecoration
                {
                    id = ids[Mathf.Clamp(selected, 0, ids.Length - 1)],
                    x = wx,
                    z = wz,
                    rotation = _pieceRandomTurn ? Random.Range(0f, 360f) : _pieceRotation,
                    scale = _pieceScale,
                    flip = _pieceFlip && !(Art.ArtLibrary.Get(ids[Mathf.Clamp(selected, 0, ids.Length - 1)])?.NoFlip ?? false),
                    variant = _pieceVariant,
                    roll = _pieceRoll,
                    pitch = _piecePitch,
                });
                // Hiding grass (owner, 2026-10-05): on the cell centre, never turned or mirrored.
                var added = list[list.Count - 1];
                if (Art.ArtLibrary.Get(added.id)?.Bush ?? false)
                {
                    added.x = Mathf.Floor(added.x) + 0.5f;
                    added.z = Mathf.Floor(added.z) + 0.5f;
                    added.rotation = 0f;
                    added.flip = false;
                }
            }
            else
            {
                var best = -1;
                var bestDistance = 1.5f * 1.5f;
                for (var i = 0; i < list.Count; i++)
                {
                    var d = list[i];
                    var distance = (d.x - wx) * (d.x - wx) + (d.z - wz) * (d.z - wz);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }
                if (best >= 0) list.RemoveAt(best);
            }
            Changed(texture: false);
            e.Use();
        }

        private void DrawSelection()
        {
            if (_building != null && _map.buildings.Contains(_building))
            {
                EditorGUILayout.LabelField("Building (roof)", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var name = EditorGUILayout.TextField("Name", _building.name);
                var rect = EditorGUILayout.RectIntField("Cells", new RectInt(_building.x, _building.z, _building.w, _building.d));
                var height = EditorGUILayout.FloatField("Roof height", _building.height);
                if (EditorGUI.EndChangeCheck())
                {
                    Record();
                    _building.name = name;
                    (_building.x, _building.z, _building.w, _building.d) = (rect.x, rect.y, Mathf.Max(1, rect.width), Mathf.Max(1, rect.height));
                    _building.height = height;
                    Changed();
                }
                EditorGUILayout.HelpBox("Walls are painted Wall cells; gaps in the outline are doorways. Chest spots and incubators must be inside.", MessageType.None);
                if (GUILayout.Button("Delete building"))
                {
                    Record();
                    _map.buildings.Remove(_building);
                    _building = null;
                    Changed();
                }
            }
            else if (_area != null && _map.areas.Contains(_area))
            {
                EditorGUILayout.LabelField("Area", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var name = EditorGUILayout.TextField("Name", _area.name);
                var rect = EditorGUILayout.RectIntField("Cells", new RectInt(_area.x, _area.z, _area.w, _area.d));
                var entry = EditorGUILayout.IntField("Entry group", _area.entryGroup);
                var refills = EditorGUILayout.Toggle("Refills over time", _area.refills);
                var combat = EditorGUILayout.Toggle("Combat zone", _area.combatZone);
                if (EditorGUI.EndChangeCheck())
                {
                    Record();
                    _area.name = name;
                    (_area.x, _area.z, _area.w, _area.d) = (rect.x, rect.y, Mathf.Max(1, rect.width), Mathf.Max(1, rect.height));
                    _area.entryGroup = Mathf.Max(0, entry);
                    _area.refills = refills;
                    _area.combatZone = combat;
                    Changed();
                }
                EditorGUILayout.LabelField("Zombies placed in this area");
                for (var i = 0; i < _area.placed.Count; i++)
                {
                    var p = _area.placed[i];
                    EditorGUILayout.BeginHorizontal();
                    System.Enum.TryParse<ZombieType>(p.type, out var t);
                    EditorGUI.BeginChangeCheck();
                    t = (ZombieType)EditorGUILayout.EnumPopup(t);
                    var count = EditorGUILayout.IntField(p.count, GUILayout.Width(50));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Record();
                        p.type = (ZombieTypes.IsBoss(t) ? ZombieType.Basic : t).ToString();
                        p.count = Mathf.Max(0, count);
                        Changed();
                    }
                    if (GUILayout.Button("✕", GUILayout.Width(24)))
                    {
                        Record();
                        _area.placed.RemoveAt(i);
                        Changed();
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Add zombies"))
                {
                    Record();
                    _area.placed.Add(new MapPlaced { type = ZombieType.Basic.ToString(), count = 1 });
                    Changed();
                }
                if (GUILayout.Button("Delete area"))
                {
                    Record();
                    _map.areas.Remove(_area);
                    _area = null;
                    Changed();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space(8);
        }

        private void DrawProblems()
        {
            EditorGUILayout.LabelField("Checks", EditorStyles.boldLabel);
            if (_problems.Count == 0) EditorGUILayout.HelpBox("No problems found.", MessageType.Info);
            foreach (var p in _problems) EditorGUILayout.HelpBox(p, p.StartsWith("Error") ? MessageType.Error : MessageType.Warning);
        }

        // ------------------------------------------------------------------ canvas

        private void DrawCanvas()
        {
            _gridScroll = EditorGUILayout.BeginScrollView(_gridScroll);
            var area = GUILayoutUtility.GetRect(_map.width * _cell, _map.height * _cell, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                if (_textureStale || _texture == null) RebuildTexture();
                GUI.DrawTexture(area, _texture);
                DrawOverlays(area);
            }
            HandleCanvasInput(area, e);
            EditorGUILayout.EndScrollView();
        }

        private Rect CellsToScreen(Rect area, int x, int z, int w, int d) =>
            new Rect(area.x + x * _cell, area.y + (_map.height - z - d) * _cell, w * _cell, d * _cell);

        private void Outline(Rect r, Color c, float t = 2f)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        private void DrawOverlays(Rect area)
        {
            var small = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white } };
            if (_map.spawnArea != null && _map.spawnArea.w > 0)
                Outline(CellsToScreen(area, _map.spawnArea.x, _map.spawnArea.z, _map.spawnArea.w, _map.spawnArea.d), new Color(0.3f, 0.6f, 1f));
            if (_map.bossArea != null && _map.bossArea.w > 0)
                Outline(CellsToScreen(area, _map.bossArea.x, _map.bossArea.z, _map.bossArea.w, _map.bossArea.d), new Color(1f, 0.25f, 0.3f));
            foreach (var a in _map.areas)
            {
                var r = CellsToScreen(area, a.x, a.z, a.w, a.d);
                Outline(r, a == _area ? Color.white : new Color(0.3f, 0.9f, 0.9f, 0.8f), a == _area ? 3f : 1.5f);
                GUI.Label(new Rect(r.x + 3, r.y + 2, 200, 16), a.name, small);
            }
            foreach (var b in _map.buildings)
            {
                // Footprint = the rectangle plus its far wall cells.
                var r = CellsToScreen(area, b.x, b.z, b.w + 1, b.d + 1);
                Outline(r, b == _building ? Color.white : new Color(1f, 0.85f, 0.3f), b == _building ? 3f : 1.5f);
                if (_cell >= 5f) GUI.Label(new Rect(r.x + 3, r.y + 2, 200, 16), b.name, small);
            }
            foreach (var z in _map.zombies)
            {
                var r = CellsToScreen(area, z.x, z.z, 1, 1);
                EditorGUI.DrawRect(new Rect(r.center.x - _cell * 0.35f, r.center.y - _cell * 0.35f, _cell * 0.7f, _cell * 0.7f), new Color(0.9f, 0.2f, 0.2f));
                if (_cell >= 8f) GUI.Label(new Rect(r.x, r.y - 2, 30, 16), z.type.Substring(0, 1), small);
            }
            // Decorations: small orange squares at their exact spots (brighter while the tool is active).
            var decorationColour = _tool == Tool.Decoration ? new Color(1f, 0.6f, 0.15f) : new Color(1f, 0.6f, 0.15f, 0.45f);
            var dot = Mathf.Max(3f, _cell * 0.45f);
            foreach (var d in _map.decorations)
            {
                var px = area.x + d.x * _cell;
                var py = area.y + (_map.height - d.z) * _cell;
                EditorGUI.DrawRect(new Rect(px - dot * 0.5f, py - dot * 0.5f, dot, dot), decorationColour);
            }
            // Props: their footprints, turned as placed (bushes green, the rest white).
            Handles.BeginGUI();
            foreach (var p in _map.props)
            {
                var piece = Art.ArtLibrary.Get(p.id);
                var size = (piece != null ? piece.Footprint : Vector2.one) * (p.scale > 0f ? p.scale : 1f);
                var color = piece != null && piece.Bush ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 1f, 1f, 0.9f);
                if (_tool != Tool.Prop) color.a *= 0.55f;
                Handles.color = color;
                Vector3 ToScreen(Vector3 w) => new Vector3(area.x + w.x * _cell, area.y + (_map.height - w.z) * _cell, 0f);
                var center = new Vector3(p.x, 0f, p.z);
                var turn = Quaternion.Euler(0f, p.rotation, 0f);
                if (piece != null && piece.Round)
                {
                    var points = new Vector3[17];
                    for (var i = 0; i <= 16; i++)
                    {
                        var a = i / 16f * Mathf.PI * 2f;
                        points[i] = ToScreen(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * size.x * 0.5f);
                    }
                    Handles.DrawAAPolyLine(2f, points);
                }
                else
                {
                    var hx = size.x * 0.5f;
                    var hz = size.y * 0.5f;
                    var corners = new[]
                    {
                        ToScreen(center + turn * new Vector3(-hx, 0f, -hz)), ToScreen(center + turn * new Vector3(-hx, 0f, hz)),
                        ToScreen(center + turn * new Vector3(hx, 0f, hz)), ToScreen(center + turn * new Vector3(hx, 0f, -hz)),
                        ToScreen(center + turn * new Vector3(-hx, 0f, -hz)),
                    };
                    Handles.DrawAAPolyLine(2f, corners);
                }
            }
            Handles.EndGUI();
            if (_dragging)
            {
                var x0 = Mathf.Min(_dragFrom.x, _dragTo.x);
                var z0 = Mathf.Min(_dragFrom.y, _dragTo.y);
                Outline(CellsToScreen(area, x0, z0, Mathf.Abs(_dragTo.x - _dragFrom.x) + 1, Mathf.Abs(_dragTo.y - _dragFrom.y) + 1), Color.white, 2f);
            }
        }

        private void HandleCanvasInput(Rect area, Event e)
        {
            if (!area.Contains(e.mousePosition) && e.type != EventType.MouseUp) return;
            var cx = Mathf.FloorToInt((e.mousePosition.x - area.x) / _cell);
            var cz = _map.height - 1 - Mathf.FloorToInt((e.mousePosition.y - area.y) / _cell);
            var cell = new Vector2Int(Mathf.Clamp(cx, 0, _map.width - 1), Mathf.Clamp(cz, 0, _map.height - 1));
            var left = e.button == 0;
            var right = e.button == 1;
            switch (_tool)
            {
                case Tool.Paint:
                    if (e.type == EventType.MouseDown && (left || right)) Record();
                    if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && (left || right))
                    {
                        var tile = left ? _brush : Tile.Ground;
                        var size = Tiles.IsMarker(tile) ? 1 : _brushSize;
                        for (var dx = 0; dx < size; dx++)
                        for (var dz = 0; dz < size; dz++) _map[cell.x + dx, cell.y - dz] = tile;
                        Changed(texture: true);
                        e.Use();
                    }
                    break;
                case Tool.Surface:
                    if (e.type == EventType.MouseDown && (left || right)) Record();
                    if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && (left || right))
                    {
                        var paint = left ? _surfaceBrush : Surface.None;
                        for (var dx = 0; dx < _brushSize; dx++)
                        for (var dz = 0; dz < _brushSize; dz++) _map.SetSurface(cell.x + dx, cell.y - dz, paint);
                        Changed(texture: true);
                        e.Use();
                    }
                    break;
                case Tool.Zombie:
                    if (e.type != EventType.MouseDown || !(left || right)) break;
                    Record();
                    _map.zombies.RemoveAll(z => z.x == cell.x && z.z == cell.y);
                    if (left) _map.zombies.Add(new MapZombie { type = _zombieType.ToString(), x = cell.x, z = cell.y });
                    Changed();
                    e.Use();
                    break;
                case Tool.Decoration:
                case Tool.Prop:
                    HandlePieceInput(area, e);
                    break;
                case Tool.Select:
                    if (e.type != EventType.MouseDown || !left) break;
                    _building = null;
                    _area = null;
                    foreach (var b in _map.buildings)
                        if (cell.x >= b.x && cell.x <= b.x + b.w && cell.y >= b.z && cell.y <= b.z + b.d) _building = b;
                    if (_building == null)
                        foreach (var a in _map.areas)
                            if (new RectInt(a.x, a.z, a.w, a.d).Contains(cell)) _area = a;
                    e.Use();
                    break;
                default:
                    if (e.type == EventType.MouseDown && left)
                    {
                        _dragging = true;
                        _dragFrom = _dragTo = cell;
                        e.Use();
                    }
                    else if (e.type == EventType.MouseDrag && _dragging)
                    {
                        _dragTo = cell;
                        e.Use();
                    }
                    else if (e.type == EventType.MouseUp && _dragging)
                    {
                        _dragging = false;
                        FinishRect();
                        e.Use();
                    }
                    break;
            }
            if (e.type == EventType.MouseDrag || e.type == EventType.MouseDown) Repaint();
        }

        private void FinishRect()
        {
            var x = Mathf.Min(_dragFrom.x, _dragTo.x);
            var z = Mathf.Min(_dragFrom.y, _dragTo.y);
            var w = Mathf.Abs(_dragTo.x - _dragFrom.x) + 1;
            var d = Mathf.Abs(_dragTo.y - _dragFrom.y) + 1;
            Record();
            switch (_tool)
            {
                case Tool.Building:
                    // The rectangle drawn is the whole footprint; stored as origin + size without the far wall.
                    _building = new MapBuilding { name = $"Building {_map.buildings.Count + 1}", x = x, z = z, w = Mathf.Max(1, w - 1), d = Mathf.Max(1, d - 1) };
                    _map.buildings.Add(_building);
                    _area = null;
                    _tool = Tool.Select;
                    break;
                case Tool.Area:
                    _area = new MapArea { name = $"Area {_map.areas.Count + 1}", x = x, z = z, w = w, d = d };
                    _map.areas.Add(_area);
                    _building = null;
                    _tool = Tool.Select;
                    break;
                case Tool.SpawnArea:
                    _map.spawnArea = new MapRect { name = "Spawn Area", x = x, z = z, w = w, d = d };
                    break;
                case Tool.BossArea:
                    _map.bossArea = new MapRect { name = "Boss Area", x = x, z = z, w = w, d = d };
                    break;
            }
            Changed();
        }

        private void RebuildTexture()
        {
            if (_texture == null || _texture.width != _map.width || _texture.height != _map.height)
                _texture = new Texture2D(_map.width, _map.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[_map.width * _map.height];
            var baseGround = BaseGround.Find(_map.baseGround).Light * 1.6f;
            baseGround.a = 1f;
            for (var z = 0; z < _map.height; z++)
            for (var x = 0; x < _map.width; x++)
            {
                var tile = _map[x, z];
                var under = SurfaceColour(_map.SurfaceAt(x, z)) ?? baseGround;
                pixels[z * _map.width + x] = tile == Tile.Ground ? (Color32)under : Colour(tile);
            }
            _texture.SetPixels32(pixels);
            _texture.Apply();
            _textureStale = false;
        }

        // ------------------------------------------------------------------ load / save / undo / play

        private void Load(string id)
        {
            if (!ConfirmDiscard()) return;
            _id = id;
            _map = StageFiles.Load(id);
            _dirty = false;
            _undo.Clear();
            _building = null;
            _area = null;
            _resizeWidth = _resizeHeight = 0;
            Changed(dirty: false);
        }

        private bool ConfirmDiscard() =>
            !_dirty || EditorUtility.DisplayDialog("Unsaved changes", $"Discard the changes to '{_id}'?", "Discard", "Keep editing");

        private void Save()
        {
            StageFiles.Save(_id, _map);
            _dirty = false;
        }

        private static string Json(StageMap map)
        {
            map.Pack();
            return map.ToJson();
        }

        private void Record()
        {
            _undo.Add(Json(_map));
            if (_undo.Count > 60) _undo.RemoveAt(0);
        }

        private void Undo()
        {
            if (_undo.Count == 0) return;
            _map = StageMap.FromJson(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
            _building = null;
            _area = null;
            Changed();
        }

        private void HandleShortcuts()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || !(e.control || e.command)) return;
            if (e.keyCode == KeyCode.Z)
            {
                Undo();
                e.Use();
            }
            else if (e.keyCode == KeyCode.S && _map != null)
            {
                Save();
                e.Use();
            }
        }

        private void Changed(bool dirty = true, bool texture = true)
        {
            if (dirty) _dirty = true;
            if (texture) _textureStale = true;
            _problems = StageMapBuilder.Validate(_map);
            Repaint();
        }

        /// <summary>Saves, then plays this stage (Solo) straight away.</summary>
        private void Play()
        {
            if (EditorApplication.isPlaying) return;
            Save();
            EditorPrefs.SetString(GameSession.PlayStagePref, _id);
            if (EditorSceneManager.GetActiveScene().path != ZombieSetup.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(ZombieSetup.ScenePath);
            }
            EditorApplication.isPlaying = true;
        }

        private static Color? SurfaceColour(Surface surface)
        {
            switch (surface)
            {
                case Surface.Floor: return new Color(0.45f, 0.4f, 0.35f);
                case Surface.Grass: return new Color(0.3f, 0.48f, 0.26f);
                case Surface.Grass2: return new Color(0.45f, 0.62f, 0.32f);
                case Surface.Road: return new Color(0.42f, 0.4f, 0.36f);
                case Surface.Road2: return new Color(0.5f, 0.4f, 0.28f);
                default: return null;
            }
        }

        private static Color32 Colour(Tile tile)
        {
            switch (tile)
            {
                case Tile.Wall: return new Color(0.75f, 0.75f, 0.75f);
                case Tile.Cliff: return new Color(0.5f, 0.4f, 0.3f);
                case Tile.Cover: return new Color(0.7f, 0.6f, 0.45f);
                case Tile.Bush: return new Color(0.25f, 0.6f, 0.25f);
                case Tile.LootSpawn: return new Color(1f, 0.85f, 0.3f);
                case Tile.ZombieSpawn: return new Color(0.7f, 0.2f, 0.2f);
                case Tile.PlayerSpawn: return new Color(0.3f, 0.6f, 1f);
                case Tile.BossSpawn: return new Color(1f, 0.1f, 0.4f);
                case Tile.Hostage: return new Color(1f, 1f, 1f);
                case Tile.BombTarget: return new Color(1f, 0.2f, 0.1f);
                case Tile.Vaccine: return new Color(0.3f, 1f, 0.5f);
                case Tile.RivalSpawn: return new Color(0.85f, 0.15f, 0.6f);
                case Tile.SubBossSpawn: return new Color(1f, 0.5f, 0.1f);
                case Tile.Barrel: return new Color(1f, 0.4f, 0.1f);
                case Tile.ChestSpawn: return new Color(0.6f, 0.9f, 0.4f);
                case Tile.Incubator: return new Color(0.7f, 0.3f, 0.8f);
                default: return new Color(0.22f, 0.24f, 0.21f);
            }
        }
    }
}
