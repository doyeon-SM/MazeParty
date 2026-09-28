using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MazeParty.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MazeParty.Editor
{
    public sealed class BoardMapAuthoringWindow : EditorWindow
    {
        private const string DefaultMapFolder = "Assets/MazeParty/Board/Maps";
        private const string DefaultTileFolder = "Assets/MazeParty/Prefabs/Board/World";
        private const float DefaultGateWidth = 2.5f;
        private const float StartAnchorHeight = 1f;
        private const float StartAnchorSafeInset = 0.52f;
        private const float StartAnchorHeightTolerance = 0.05f;
        private const int MinimumPolygonSides = 3;
        private const int MaximumPolygonSides = 5;
        private const int MaximumMapTileCount = 49;

        private readonly List<AuthoringIssue> _issues = new List<AuthoringIssue>();
        private BoardMapCatalog _catalog;
        private BoardMapDefinition _definition;
        private BoardMapRoot _mapRoot;
        private BoardTile _sourceTile;
        private BoardTile _destinationTile;
        private BoardTileType _newTileType = BoardTileType.Normal;
        private GameObject _tilePrefab;
        private int _polygonSides = 4;
        private int _playerStartSlot;
        private float _footprintRadius = BoardTile.HalfRoomSize;
        private float _gateWidth = DefaultGateWidth;
        private bool _bidirectional = true;
        private bool _placementMode;
        private bool _editVertices;
        private bool _editGate;
        private Vector2 _scroll;

        [MenuItem("MazeParty/Board/Map Authoring")]
        public static void Open()
        {
            GetWindow<BoardMapAuthoringWindow>("Board Map Authoring");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            Selection.selectionChanged += Repaint;
            TryAdoptSelection();
            ResolveDefaultTilePrefab();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            Selection.selectionChanged -= Repaint;
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawMapAssets();
            EditorGUILayout.Space(8f);
            DrawMapRoot();
            EditorGUILayout.Space(8f);
            DrawTileAuthoring();
            EditorGUILayout.Space(8f);
            DrawConnectionAuthoring();
            EditorGUILayout.Space(8f);
            DrawValidation();
            EditorGUILayout.EndScrollView();
        }

        private void DrawMapAssets()
        {
            EditorGUILayout.LabelField("Map Assets", EditorStyles.boldLabel);
            _catalog = (BoardMapCatalog)EditorGUILayout.ObjectField(
                "Catalog", _catalog, typeof(BoardMapCatalog), false);
            _definition = (BoardMapDefinition)EditorGUILayout.ObjectField(
                "Definition", _definition, typeof(BoardMapDefinition), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Definition..."))
                {
                    CreateDefinitionAsset();
                }

                if (GUILayout.Button("Create Catalog..."))
                {
                    CreateCatalogAsset();
                }
            }

            using (new EditorGUI.DisabledScope(_catalog == null || _definition == null))
            {
                if (GUILayout.Button("Add Definition To Catalog"))
                {
                    AddDefinitionToCatalog();
                }
            }
        }

        private void DrawMapRoot()
        {
            EditorGUILayout.LabelField("Authored Map Root", EditorStyles.boldLabel);
            _mapRoot = (BoardMapRoot)EditorGUILayout.ObjectField(
                "Map Root", _mapRoot, typeof(BoardMapRoot), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selection"))
                {
                    TryAdoptSelection(true);
                }

                if (GUILayout.Button("Create Map Root"))
                {
                    CreateMapRoot();
                }
            }

            using (new EditorGUI.DisabledScope(_mapRoot == null))
            {
                using (new EditorGUI.DisabledScope(_definition == null))
                {
                    if (GUILayout.Button("Bind Selected Definition"))
                    {
                        BindDefinitionToRoot();
                    }
                }

                if (GUILayout.Button("Refresh Topology From Authored Roots"))
                {
                    RefreshTopology();
                }

                if (GUILayout.Button("Save Map Root As Prefab..."))
                {
                    SaveMapRootPrefab();
                }
            }

            EditorGUILayout.HelpBox(
                "Tiles and connections are synchronized. Environment Root is never rebuilt or cleared, so authored hills, trees, roads, and other visual work are preserved.",
                MessageType.Info);
        }

        private void DrawTileAuthoring()
        {
            EditorGUILayout.LabelField("Tile Placement", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _newTileType = (BoardTileType)EditorGUILayout.EnumPopup("Tile Type", _newTileType);
            if (EditorGUI.EndChangeCheck())
            {
                ResolveDefaultTilePrefab();
            }

            _tilePrefab = (GameObject)EditorGUILayout.ObjectField(
                "Production Tile Prefab", _tilePrefab, typeof(GameObject), false);
            _polygonSides = EditorGUILayout.IntSlider(
                "Polygon Sides", _polygonSides, MinimumPolygonSides, MaximumPolygonSides);
            _footprintRadius = EditorGUILayout.FloatField(
                "Footprint Radius", Mathf.Max(0.1f, _footprintRadius));

            using (new EditorGUI.DisabledScope(!CanPlaceTile()))
            {
                var nextPlacementMode = GUILayout.Toggle(
                    _placementMode,
                    _placementMode ? "Click In Scene To Place (Esc To Stop)" : "Enable Scene Placement",
                    "Button");
                if (nextPlacementMode != _placementMode)
                {
                    _placementMode = nextPlacementMode;
                    if (_placementMode)
                    {
                        _editVertices = false;
                        _editGate = false;
                    }
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Place At Scene View Pivot"))
                {
                    var view = SceneView.lastActiveSceneView;
                    PlaceTile(view != null ? view.pivot : _mapRoot.transform.position);
                }
            }

            using (new EditorGUI.DisabledScope(!CanEditSelectedFootprint()))
            {
                var nextEditVertices = GUILayout.Toggle(
                    _editVertices,
                    _editVertices ? "Editing Selected Polygon (Esc To Stop)" : "Edit Selected Polygon Vertices",
                    "Button");
                if (nextEditVertices != _editVertices)
                {
                    _editVertices = nextEditVertices;
                    if (_editVertices)
                    {
                        _placementMode = false;
                        _editGate = false;
                    }
                    SceneView.RepaintAll();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(GetSelectedTile() == null))
                {
                    if (GUILayout.Button("Mark Selected Start"))
                    {
                        MarkSelectedTile(BoardTileType.Start);
                    }

                    if (GUILayout.Button("Mark Selected Respawn"))
                    {
                        MarkSelectedTile(BoardTileType.Respawn);
                    }
                }
            }

            _playerStartSlot = EditorGUILayout.IntSlider(
                "Player Start Slot", _playerStartSlot + 1, 1, PlayerSlotRules.Count) - 1;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(GetSelectedTile() == null || _mapRoot == null))
                {
                    if (GUILayout.Button("Assign Selected To Slot"))
                    {
                        AssignSelectedPlayerStart();
                    }
                }

                using (new EditorGUI.DisabledScope(_mapRoot == null))
                {
                    if (GUILayout.Button("Use Shared Start For Slot"))
                    {
                        ClearPlayerStartAssignment();
                    }
                }
            }

            using (new EditorGUI.DisabledScope(_mapRoot == null || _mapRoot.StartTile == null))
            {
                if (GUILayout.Button("Create / Recenter Four Start Anchors"))
                {
                    EnsureStartAnchors();
                }
            }

            EditorGUILayout.HelpBox(
                "Changing a tile type updates gameplay data only. The tool does not replace an authored prefab or overwrite its visuals.",
                MessageType.None);
        }

        private void DrawConnectionAuthoring()
        {
            EditorGUILayout.LabelField("Tile Connections", EditorStyles.boldLabel);
            _sourceTile = (BoardTile)EditorGUILayout.ObjectField(
                "Source", _sourceTile, typeof(BoardTile), true);
            _destinationTile = (BoardTile)EditorGUILayout.ObjectField(
                "Destination", _destinationTile, typeof(BoardTile), true);
            _bidirectional = EditorGUILayout.Toggle("Bidirectional", _bidirectional);
            _gateWidth = Mathf.Max(0.1f, EditorGUILayout.FloatField("Gate Width", _gateWidth));

            if (GUILayout.Button("Use Two Selected Tiles"))
            {
                AdoptSelectedTilesAsConnection();
            }

            using (new EditorGUI.DisabledScope(!CanCreateConnection()))
            {
                if (GUILayout.Button(_bidirectional
                        ? "Create Bidirectional Connection"
                        : "Create Directed Connection"))
                {
                    CreateConnection();
                }
            }

            using (new EditorGUI.DisabledScope(GetSelectedGate() == null))
            {
                var nextEditGate = GUILayout.Toggle(
                    _editGate,
                    _editGate ? "Editing Selected Gate (Esc To Stop)" : "Edit Selected Gate Handles",
                    "Button");
                if (nextEditGate != _editGate)
                {
                    _editGate = nextEditGate;
                    if (_editGate)
                    {
                        _placementMode = false;
                        _editVertices = false;
                    }
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.HelpBox(
                "For two selected tiles, the active tile becomes Destination and the other tile becomes Source. Gate position and direction are fitted between their centers.",
                MessageType.None);
        }

        private void DrawValidation()
        {
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_mapRoot == null))
            {
                if (GUILayout.Button("Validate Authored Map"))
                {
                    ValidateMap();
                }
            }

            if (_issues.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    _mapRoot == null
                        ? "Select or create a map root to validate it."
                        : "No cached issues. Run validation after authoring changes.",
                    MessageType.Info);
                return;
            }

            for (var index = 0; index < _issues.Count; index++)
            {
                var issue = _issues[index];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox(issue.Message, issue.Type);
                    using (new EditorGUI.DisabledScope(issue.Context == null))
                    {
                        if (GUILayout.Button("Select", GUILayout.Width(58f)))
                        {
                            Selection.activeObject = issue.Context;
                            EditorGUIUtility.PingObject(issue.Context);
                        }
                    }
                }
            }
        }

        private void OnSceneGui(SceneView sceneView)
        {
            var current = Event.current;
            if ((_placementMode || _editVertices || _editGate) &&
                current.type == EventType.KeyDown &&
                current.keyCode == KeyCode.Escape)
            {
                _placementMode = false;
                _editVertices = false;
                _editGate = false;
                current.Use();
                Repaint();
                return;
            }

            if (_editVertices)
            {
                if (!CanEditSelectedFootprint())
                {
                    _editVertices = false;
                    Repaint();
                    return;
                }

                DrawSelectedFootprintHandles();
                return;
            }

            if (_editGate)
            {
                if (GetSelectedGate() == null)
                {
                    _editGate = false;
                    Repaint();
                    return;
                }

                DrawSelectedGateHandles();
                return;
            }

            if (!_placementMode || !CanPlaceTile())
            {
                return;
            }

            var plane = new Plane(Vector3.up, _mapRoot.transform.position);
            var ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!plane.Raycast(ray, out var distance))
            {
                return;
            }

            var point = ray.GetPoint(distance);
            Handles.color = new Color(0.15f, 0.85f, 1f, 0.9f);
            Handles.DrawWireDisc(point, Vector3.up, Mathf.Max(0.1f, _footprintRadius));
            Handles.Label(point + Vector3.up * 0.2f, $"{_polygonSides}-sided {_newTileType}");
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (current.type == EventType.MouseDown && current.button == 0 && !current.alt)
            {
                PlaceTile(point);
                current.Use();
            }
        }

        private bool CanEditSelectedFootprint()
        {
            var tile = GetSelectedTile();
            return tile != null &&
                   !EditorUtility.IsPersistent(tile) &&
                   IsOwnedTile(tile) &&
                   tile.Footprint != null &&
                   tile.Footprint.VertexCount >= BoardTileFootprint.MinVertexCount &&
                   tile.Footprint.VertexCount <= BoardTileFootprint.MaxVertexCount;
        }

        private void DrawSelectedFootprintHandles()
        {
            var tile = GetSelectedTile();
            var footprint = tile.Footprint;
            var vertices = footprint.LocalVertices.ToArray();
            var worldVertices = new Vector3[vertices.Length + 1];
            for (var index = 0; index < vertices.Length; index++)
            {
                worldVertices[index] = tile.transform.TransformPoint(
                    new Vector3(vertices[index].x, 0f, vertices[index].y));
            }
            worldVertices[vertices.Length] = worldVertices[0];

            Handles.color = footprint.TryValidate(out _)
                ? new Color(0.1f, 0.85f, 1f, 1f)
                : new Color(1f, 0.25f, 0.2f, 1f);
            Handles.DrawAAPolyLine(3f, worldVertices);

            for (var index = 0; index < vertices.Length; index++)
            {
                var world = worldVertices[index];
                Handles.Label(
                    world + tile.transform.up * 0.2f,
                    (index + 1).ToString(),
                    EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var editedWorld = Handles.PositionHandle(world, tile.transform.rotation);
                if (!EditorGUI.EndChangeCheck())
                {
                    continue;
                }

                var local = tile.transform.InverseTransformPoint(editedWorld);
                vertices[index] = new Vector2(local.x, local.z);
                Undo.RecordObject(footprint, "Edit Board Tile Polygon");
                footprint.Configure(vertices);
                EditorUtility.SetDirty(footprint);
                RecordPrefabInstance(footprint);
                MarkSceneDirty();
            }
        }

        private void DrawSelectedGateHandles()
        {
            var gate = GetSelectedGate();
            var reciprocal = FindReciprocalGate(gate);
            var gateTransform = gate.transform;

            Handles.color = new Color(0.2f, 0.85f, 1f, 1f);
            var halfWidth = gate.GateWidth * 0.5f;
            Handles.DrawAAPolyLine(
                4f,
                gate.PlanePoint - gateTransform.right * halfWidth,
                gate.PlanePoint + gateTransform.right * halfWidth);

            EditorGUI.BeginChangeCheck();
            var position = Handles.PositionHandle(
                gateTransform.position, gateTransform.rotation);
            var rotation = Handles.RotationHandle(
                gateTransform.rotation, gateTransform.position);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyGateTransform(gate, reciprocal, position, rotation);
            }

            var handleSize = HandleUtility.GetHandleSize(gate.PlanePoint) * 0.09f;
            EditorGUI.BeginChangeCheck();
            var widthHandle = Handles.Slider(
                gate.PlanePoint + gateTransform.right * halfWidth,
                gateTransform.right,
                handleSize,
                Handles.CubeHandleCap,
                0.1f);
            if (EditorGUI.EndChangeCheck())
            {
                var editedHalfWidth = Mathf.Abs(Vector3.Dot(
                    widthHandle - gate.PlanePoint,
                    gateTransform.right));
                ApplyGateWidth(gate, reciprocal, Mathf.Max(0.1f, editedHalfWidth * 2f));
            }
        }

        private void ApplyGateTransform(
            BoardGate gate,
            BoardGate reciprocal,
            Vector3 position,
            Quaternion rotation)
        {
            var targets = reciprocal != null
                ? new UnityEngine.Object[] { gate.transform, reciprocal.transform }
                : new UnityEngine.Object[] { gate.transform };
            Undo.RecordObjects(targets, "Edit Board Gate Transform");
            gate.transform.SetPositionAndRotation(position, rotation);
            RecordPrefabInstance(gate.transform);
            if (reciprocal != null)
            {
                var reverseRotation = Quaternion.LookRotation(
                    -(rotation * Vector3.forward),
                    rotation * Vector3.up);
                reciprocal.transform.SetPositionAndRotation(position, reverseRotation);
                RecordPrefabInstance(reciprocal.transform);
            }

            MarkSceneDirty();
        }

        private void ApplyGateWidth(BoardGate gate, BoardGate reciprocal, float width)
        {
            var targets = reciprocal != null
                ? new UnityEngine.Object[] { gate, reciprocal }
                : new UnityEngine.Object[] { gate };
            Undo.RecordObjects(targets, "Edit Board Gate Width");
            gate.Configure(gate.Source, gate.Destination, width);
            RecordPrefabInstance(gate);
            if (reciprocal != null)
            {
                reciprocal.Configure(reciprocal.Source, reciprocal.Destination, width);
                RecordPrefabInstance(reciprocal);
            }

            _gateWidth = width;
            MarkSceneDirty();
        }

        private static BoardGate FindReciprocalGate(BoardGate gate)
        {
            if (gate == null || gate.transform.parent == null)
            {
                return null;
            }

            var siblings = gate.transform.parent.GetComponentsInChildren<BoardGate>(true);
            for (var index = 0; index < siblings.Length; index++)
            {
                var candidate = siblings[index];
                if (candidate != gate &&
                    candidate.transform.parent == gate.transform.parent &&
                    candidate.Source == gate.Destination &&
                    candidate.Destination == gate.Source)
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool CanPlaceTile()
        {
            return _mapRoot != null &&
                   !EditorUtility.IsPersistent(_mapRoot) &&
                   _mapRoot.Topology != null &&
                   _mapRoot.TilesRoot != null &&
                   _tilePrefab != null &&
                   _tilePrefab.GetComponent<BoardTile>() != null;
        }

        private void ResolveDefaultTilePrefab()
        {
            var fileName = _newTileType switch
            {
                BoardTileType.Start => "TileStart.prefab",
                BoardTileType.Respawn => "TileRespawn.prefab",
                _ => "TileNormalA.prefab"
            };
            _tilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                DefaultTileFolder + "/" + fileName);
        }

        private void PlaceTile(Vector3 worldPosition)
        {
            if (!CanPlaceTile())
            {
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(
                _tilePrefab, _mapRoot.TilesRoot) as GameObject;
            if (instance == null)
            {
                Debug.LogError("Could not instantiate the selected tile prefab.");
                return;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Place Board Tile");
            Undo.RecordObject(instance.transform, "Position Board Tile");
            instance.transform.position = worldPosition;
            var tile = instance.GetComponent<BoardTile>();
            var coordinate = NextCoordinate();
            Undo.RecordObject(tile, "Configure Board Tile");
            tile.Configure(coordinate, _newTileType);
            instance.name = $"Tile {coordinate.x:00} - {_newTileType}";

            var footprint = instance.GetComponent<BoardTileFootprint>();
            if (footprint == null)
            {
                footprint = Undo.AddComponent<BoardTileFootprint>(instance);
            }

            Undo.RecordObject(footprint, "Configure Tile Footprint");
            footprint.Configure(BuildRegularPolygon(_polygonSides, _footprintRadius));
            RecordPrefabInstance(tile);
            RecordPrefabInstance(footprint);
            RecordPrefabInstance(instance.transform);
            Selection.activeGameObject = instance;
            if (_newTileType == BoardTileType.Start)
            {
                MarkSelectedTile(BoardTileType.Start);
            }
            else
            {
                RefreshTopology();
            }

            MarkSceneDirty();
        }

        private Vector2Int NextCoordinate()
        {
            var used = new HashSet<Vector2Int>();
            var existing = _mapRoot.TilesRoot.GetComponentsInChildren<BoardTile>(true);
            for (var index = 0; index < existing.Length; index++)
            {
                used.Add(existing[index].Coordinate);
            }

            for (var ordinal = 0; ordinal < int.MaxValue; ordinal++)
            {
                var coordinate = new Vector2Int(ordinal, 0);
                if (!used.Contains(coordinate))
                {
                    return coordinate;
                }
            }

            throw new InvalidOperationException("No unused board tile coordinate remains.");
        }

        internal static Vector2[] BuildRegularPolygon(int sideCount, float radius)
        {
            sideCount = Mathf.Clamp(sideCount, MinimumPolygonSides, MaximumPolygonSides);
            radius = Mathf.Max(0.1f, radius);
            var vertices = new Vector2[sideCount];
            var angleOffset = sideCount == 4 ? 45f : 90f;
            var effectiveRadius = sideCount == 4 ? radius * Mathf.Sqrt(2f) : radius;
            for (var index = 0; index < sideCount; index++)
            {
                var angle = (angleOffset + 360f * index / sideCount) * Mathf.Deg2Rad;
                vertices[index] = new Vector2(
                    Mathf.Cos(angle) * effectiveRadius,
                    Mathf.Sin(angle) * effectiveRadius);
            }

            return vertices;
        }

        private void MarkSelectedTile(BoardTileType tileType)
        {
            var selected = GetSelectedTile();
            if (selected == null || _mapRoot == null)
            {
                return;
            }

            var tiles = _mapRoot.TilesRoot != null
                ? _mapRoot.TilesRoot.GetComponentsInChildren<BoardTile>(true)
                : Array.Empty<BoardTile>();

            if (tileType == BoardTileType.Start)
            {
                for (var index = 0; index < tiles.Length; index++)
                {
                    var tile = tiles[index];
                    if (tile != selected && tile.TileType == BoardTileType.Start)
                    {
                        Undo.RecordObject(tile, "Replace Board Start Tile");
                        tile.Configure(tile.Coordinate, BoardTileType.Normal);
                        RecordPrefabInstance(tile);
                    }
                }
            }

            Undo.RecordObject(selected, "Change Board Tile Type");
            selected.Configure(selected.Coordinate, tileType);
            RecordPrefabInstance(selected);

            var mapRootObject = new SerializedObject(_mapRoot);
            mapRootObject.Update();
            var startProperty = mapRootObject.FindProperty("startTile");
            Undo.RecordObject(_mapRoot, tileType == BoardTileType.Start
                ? "Set Board Start Tile"
                : "Clear Board Start Tile");
            if (tileType == BoardTileType.Start)
            {
                startProperty.objectReferenceValue = selected;
            }
            else if (startProperty.objectReferenceValue == selected)
            {
                startProperty.objectReferenceValue = null;
            }

            mapRootObject.ApplyModifiedProperties();
            RecordPrefabInstance(_mapRoot);
            RefreshTopology();
            MarkSceneDirty();
        }

        private void EnsureStartAnchors()
        {
            if (_mapRoot == null || _mapRoot.StartTile == null)
            {
                return;
            }

            var anchorRoot = _mapRoot.transform.Find("Player Spawn Anchors");
            if (anchorRoot == null)
            {
                var anchorRootObject = new GameObject("Player Spawn Anchors");
                Undo.RegisterCreatedObjectUndo(anchorRootObject, "Create Player Spawn Anchors");
                anchorRoot = anchorRootObject.transform;
                Undo.SetTransformParent(anchorRoot, _mapRoot.transform, "Parent Player Spawn Anchors");
            }

            var offsets = new[]
            {
                new Vector3(-1.2f, 0f, -1.2f),
                new Vector3(1.2f, 0f, -1.2f),
                new Vector3(-1.2f, 0f, 1.2f),
                new Vector3(1.2f, 0f, 1.2f)
            };
            var anchors = new Transform[4];
            for (var index = 0; index < anchors.Length; index++)
            {
                var name = $"Player Spawn {index + 1}";
                var anchor = anchorRoot.Find(name);
                if (anchor == null)
                {
                    var anchorObject = new GameObject(name);
                    Undo.RegisterCreatedObjectUndo(anchorObject, "Create Player Spawn Anchor");
                    anchor = anchorObject.transform;
                    Undo.SetTransformParent(anchor, anchorRoot, "Parent Player Spawn Anchor");
                }

                Undo.RecordObject(anchor, "Position Player Spawn Anchor");
                var assignedStart = _mapRoot.GetStartTile(index);
                var desiredPosition = assignedStart.transform.TransformPoint(offsets[index]) +
                                      assignedStart.transform.up.normalized * StartAnchorHeight;
                anchor.position = assignedStart.GetClosestPointInside(
                    desiredPosition,
                    StartAnchorSafeInset);
                anchor.rotation = assignedStart.transform.rotation;
                anchors[index] = anchor;
            }

            var serializedRoot = new SerializedObject(_mapRoot);
            Undo.RecordObject(_mapRoot, "Bind Player Spawn Anchors");
            serializedRoot.Update();
            var property = serializedRoot.FindProperty("playerSpawnAnchors");
            property.arraySize = anchors.Length;
            for (var index = 0; index < anchors.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = anchors[index];
            }

            serializedRoot.ApplyModifiedProperties();
            RecordPrefabInstance(_mapRoot);
            MarkSceneDirty();
        }

        private bool CanCreateConnection()
        {
            return _mapRoot != null &&
                   _mapRoot.ConnectionsRoot != null &&
                   _sourceTile != null &&
                   _destinationTile != null &&
                   _sourceTile != _destinationTile &&
                   IsOwnedTile(_sourceTile) &&
                   IsOwnedTile(_destinationTile);
        }

        private void AdoptSelectedTilesAsConnection()
        {
            var selectedTiles = Selection.gameObjects
                .Select(gameObject => gameObject.GetComponentInParent<BoardTile>())
                .Where(tile => tile != null)
                .Distinct()
                .ToArray();
            if (selectedTiles.Length != 2)
            {
                ShowNotification(new GUIContent("Select exactly two tiles."));
                return;
            }

            var active = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<BoardTile>()
                : null;
            _destinationTile = active != null && selectedTiles.Contains(active)
                ? active
                : selectedTiles[1];
            _sourceTile = selectedTiles.First(tile => tile != _destinationTile);
            Repaint();
        }

        private void CreateConnection()
        {
            if (!CanCreateConnection())
            {
                return;
            }

            if (HasDirectedGate(_sourceTile, _destinationTile) ||
                (_bidirectional && HasDirectedGate(_destinationTile, _sourceTile)))
            {
                ShowNotification(new GUIContent("A requested directed gate already exists."));
                return;
            }

            var connectionObject = new GameObject(
                $"Connection {_sourceTile.Coordinate} - {_destinationTile.Coordinate}");
            Undo.RegisterCreatedObjectUndo(connectionObject, "Create Board Connection");
            Undo.SetTransformParent(
                connectionObject.transform,
                _mapRoot.ConnectionsRoot,
                "Parent Board Connection");
            CreateGate(connectionObject.transform, _sourceTile, _destinationTile);
            if (_bidirectional)
            {
                CreateGate(connectionObject.transform, _destinationTile, _sourceTile);
            }

            RefreshTopology();
            Selection.activeGameObject = connectionObject;
            MarkSceneDirty();
        }

        private BoardGate CreateGate(Transform parent, BoardTile source, BoardTile destination)
        {
            var sourcePoint = source.GetClosestPointInside(destination.WorldCenter);
            var destinationPoint = destination.GetClosestPointInside(source.WorldCenter);
            var direction = destinationPoint - sourcePoint;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = destination.WorldCenter - source.WorldCenter;
                direction.y = 0f;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }

            var gateObject = new GameObject($"Gate {source.Coordinate} to {destination.Coordinate}");
            Undo.RegisterCreatedObjectUndo(gateObject, "Create Directed Board Gate");
            Undo.SetTransformParent(gateObject.transform, parent, "Parent Directed Board Gate");
            gateObject.transform.SetPositionAndRotation(
                (sourcePoint + destinationPoint) * 0.5f + Vector3.up * 0.15f,
                Quaternion.LookRotation(direction.normalized, Vector3.up));
            var gate = Undo.AddComponent<BoardGate>(gateObject);
            Undo.RecordObject(gate, "Configure Directed Board Gate");
            gate.Configure(source, destination, _gateWidth);
            return gate;
        }

        private bool HasDirectedGate(BoardTile source, BoardTile destination)
        {
            if (_mapRoot.Topology == null)
            {
                return false;
            }

            var gates = _mapRoot.ConnectionsRoot.GetComponentsInChildren<BoardGate>(true);
            for (var index = 0; index < gates.Length; index++)
            {
                if (gates[index].Source == source && gates[index].Destination == destination)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshTopology()
        {
            if (_mapRoot == null ||
                _mapRoot.Topology == null ||
                _mapRoot.TilesRoot == null ||
                _mapRoot.ConnectionsRoot == null)
            {
                return;
            }

            var tiles = _mapRoot.TilesRoot.GetComponentsInChildren<BoardTile>(true);
            var gates = _mapRoot.ConnectionsRoot.GetComponentsInChildren<BoardGate>(true);
            Array.Sort(tiles, CompareTiles);
            Array.Sort(gates, CompareGates);
            Undo.RecordObject(_mapRoot.Topology, "Refresh Board Topology");
            _mapRoot.Topology.Configure(tiles, gates);
            EditorUtility.SetDirty(_mapRoot.Topology);
            RecordPrefabInstance(_mapRoot.Topology);
            MarkSceneDirty();
        }

        private static int CompareTiles(BoardTile left, BoardTile right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            var xComparison = left.Coordinate.x.CompareTo(right.Coordinate.x);
            return xComparison != 0
                ? xComparison
                : left.Coordinate.y.CompareTo(right.Coordinate.y);
        }

        private static int CompareGates(BoardGate left, BoardGate right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            var sourceComparison = CompareTiles(left.Source, right.Source);
            return sourceComparison != 0
                ? sourceComparison
                : CompareTiles(left.Destination, right.Destination);
        }

        private void ValidateMap()
        {
            _issues.Clear();
            if (_mapRoot == null)
            {
                _issues.Add(new AuthoringIssue("Map root is missing.", null));
                return;
            }

            if (!_mapRoot.HasAuthoringRoots)
            {
                _issues.Add(new AuthoringIssue(
                    "Map root must bind Topology, Tiles, Connections, and Environment roots.",
                    _mapRoot));
                return;
            }

            if (!_mapRoot.Topology.transform.IsChildOf(_mapRoot.transform) ||
                !_mapRoot.TilesRoot.IsChildOf(_mapRoot.Topology.transform) ||
                !_mapRoot.ConnectionsRoot.IsChildOf(_mapRoot.Topology.transform) ||
                !_mapRoot.EnvironmentRoot.IsChildOf(_mapRoot.transform))
            {
                _issues.Add(new AuthoringIssue(
                    "Topology, Tiles, Connections, and Environment must belong to this Map Root.",
                    _mapRoot));
            }

            RefreshTopology();
            var tiles = _mapRoot.Topology.Tiles;
            var startCount = 0;
            for (var index = 0; index < tiles.Count; index++)
            {
                var tile = tiles[index];
                if (tile.TileType == BoardTileType.Start)
                {
                    startCount++;
                }

                var footprint = tile.GetComponent<BoardTileFootprint>();
                if (footprint == null)
                {
                    _issues.Add(new AuthoringIssue(
                        $"Tile '{tile.name}' has no 3-5 sided footprint.", tile));
                }
                else if (!footprint.TryValidate(out var error))
                {
                    _issues.Add(new AuthoringIssue(
                        $"Tile '{tile.name}' footprint: {error}", footprint));
                }
                else if (!footprint.CanContainInset(StartAnchorSafeInset))
                {
                    _issues.Add(new AuthoringIssue(
                        $"Tile '{tile.name}' footprint is too small for the required {StartAnchorSafeInset:0.##} safe inset.",
                        footprint));
                }
            }

            if (startCount != 1)
            {
                _issues.Add(new AuthoringIssue(
                    $"Map requires exactly one Start tile; found {startCount}.", _mapRoot));
            }
            else if (_mapRoot.StartTile == null || _mapRoot.StartTile.TileType != BoardTileType.Start)
            {
                _issues.Add(new AuthoringIssue(
                    "Map Root Start Tile must reference the authored Start tile.", _mapRoot));
            }
            else if (!IsOwnedTile(_mapRoot.StartTile))
            {
                _issues.Add(new AuthoringIssue(
                    "Map Root Start Tile is outside this map.", _mapRoot.StartTile));
            }

            if (_mapRoot.PlayerSpawnAnchors.Count != PlayerSlotRules.Count ||
                _mapRoot.PlayerSpawnAnchors.Any(anchor => anchor == null))
            {
                _issues.Add(new AuthoringIssue(
                    "Map requires four bound player spawn anchors.", _mapRoot));
            }

            if (_mapRoot.PlayerStartTiles.Count != PlayerSlotRules.Count)
            {
                _issues.Add(new AuthoringIssue(
                    "Player start assignments must contain four slots.", _mapRoot));
            }

            for (var slot = 0; slot < PlayerSlotRules.Count; slot++)
            {
                var resolvedStart = _mapRoot.GetStartTile(slot);
                var anchor = slot < _mapRoot.PlayerSpawnAnchors.Count
                    ? _mapRoot.PlayerSpawnAnchors[slot]
                    : null;
                if (resolvedStart == null)
                {
                    _issues.Add(new AuthoringIssue(
                        $"Player {slot + 1} has no resolved start tile.", _mapRoot));
                    continue;
                }

                if (!IsOwnedTile(resolvedStart))
                {
                    _issues.Add(new AuthoringIssue(
                        $"Player {slot + 1} start tile is outside this map.", resolvedStart));
                    continue;
                }

                if (anchor == null)
                {
                    _issues.Add(new AuthoringIssue(
                        $"Player {slot + 1} has no spawn anchor for its resolved start tile.",
                        _mapRoot));
                    continue;
                }

                if (!anchor.IsChildOf(_mapRoot.transform))
                {
                    _issues.Add(new AuthoringIssue(
                        $"Player {slot + 1} spawn anchor is outside this map.", anchor));
                    continue;
                }

                if (!resolvedStart.ContainsHorizontalPoint(anchor.position))
                {
                    _issues.Add(new AuthoringIssue(
                        $"Player {slot + 1} spawn anchor is outside its resolved start tile '{resolvedStart.name}'.",
                        anchor));
                    continue;
                }

                var safePosition = resolvedStart.GetClosestPointInside(
                    anchor.position,
                    StartAnchorSafeInset);
                if ((safePosition - anchor.position).sqrMagnitude > 0.0001f)
                {
                    _issues.Add(new AuthoringIssue(
                        $"Player {slot + 1} spawn anchor is not safely inset within its resolved start tile '{resolvedStart.name}'.",
                        anchor));
                }

                var height = Vector3.Dot(
                    anchor.position - resolvedStart.WorldCenter,
                    resolvedStart.transform.up.normalized);
                if (Mathf.Abs(height - StartAnchorHeight) > StartAnchorHeightTolerance)
                {
                    _issues.Add(new AuthoringIssue(
                        $"Player {slot + 1} spawn anchor must be {StartAnchorHeight:0.##} above its resolved start tile '{resolvedStart.name}'.",
                        anchor));
                }
            }

            ValidateReachability(tiles);

            var topologyValidation = _mapRoot.Topology.ValidateTopology();
            for (var index = 0; index < topologyValidation.Issues.Count; index++)
            {
                var issue = topologyValidation.Issues[index];
                _issues.Add(new AuthoringIssue(issue.Message, issue.Context));
            }

            if (_definition != null)
            {
                if (!_definition.HasValidIdentity)
                {
                    _issues.Add(new AuthoringIssue(
                        "Map definition requires a lowercase kebab-case ID and display name.",
                        _definition));
                }
                else if (_mapRoot.Definition != _definition)
                {
                    _issues.Add(new AuthoringIssue(
                        "Map Root does not reference the selected Map Definition.", _mapRoot));
                }
            }

            if (_issues.Count == 0)
            {
                _issues.Add(new AuthoringIssue(
                    "Map authoring contracts are valid.", _mapRoot, MessageType.Info));
            }
        }

        private void ValidateReachability(IReadOnlyList<BoardTile> tiles)
        {
            if (tiles.Count > MaximumMapTileCount)
            {
                _issues.Add(new AuthoringIssue(
                    $"Map contains {tiles.Count} tiles, but map UI supports at most {MaximumMapTileCount} icons.",
                    _mapRoot));
            }

            var starts = new HashSet<BoardTile>();
            if (_mapRoot.StartTile != null && IsOwnedTile(_mapRoot.StartTile))
            {
                starts.Add(_mapRoot.StartTile);
            }

            for (var slot = 0; slot < PlayerSlotRules.Count; slot++)
            {
                var start = _mapRoot.GetStartTile(slot);
                if (start != null && IsOwnedTile(start))
                {
                    starts.Add(start);
                }
            }

            for (var tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
            {
                var tile = tiles[tileIndex];
                if (tile == null || tiles.Count <= 1)
                {
                    continue;
                }

                if (_mapRoot.Topology.GetOutgoingGates(tile).Count == 0 &&
                    _mapRoot.Topology.GetIncomingGates(tile).Count == 0)
                {
                    _issues.Add(new AuthoringIssue(
                        $"Tile '{tile.name}' is orphaned and has no connection.", tile));
                }
            }

            foreach (var start in starts)
            {
                var reached = new HashSet<BoardTile> { start };
                var pending = new Queue<BoardTile>();
                pending.Enqueue(start);
                while (pending.Count > 0)
                {
                    var current = pending.Dequeue();
                    var outgoing = _mapRoot.Topology.GetOutgoingGates(current);
                    for (var gateIndex = 0; gateIndex < outgoing.Count; gateIndex++)
                    {
                        var destination = outgoing[gateIndex]?.Destination;
                        if (destination != null && reached.Add(destination))
                        {
                            pending.Enqueue(destination);
                        }
                    }
                }

                var unreachable = new List<BoardTile>();
                for (var tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
                {
                    var tile = tiles[tileIndex];
                    if (tile != null && !reached.Contains(tile))
                    {
                        unreachable.Add(tile);
                    }
                }

                if (unreachable.Count > 0)
                {
                    var sampleNames = string.Join(
                        ", ",
                        unreachable.Take(4).Select(tile => tile.name));
                    var remainder = unreachable.Count > 4
                        ? $" and {unreachable.Count - 4} more"
                        : string.Empty;
                    _issues.Add(new AuthoringIssue(
                        $"Start '{start.name}' cannot reach {unreachable.Count} tile(s): {sampleNames}{remainder}.",
                        unreachable[0]));
                }
            }
        }

        private void CreateMapRoot()
        {
            var rootObject = new GameObject(
                _definition != null ? $"Board Map - {_definition.DisplayName}" : "Board Map");
            Undo.RegisterCreatedObjectUndo(rootObject, "Create Board Map Root");
            var mapRoot = Undo.AddComponent<BoardMapRoot>(rootObject);

            var topologyObject = CreateChild(rootObject.transform, "Topology");
            var topology = Undo.AddComponent<BoardTopology>(topologyObject);
            var tiles = CreateChild(topologyObject.transform, "Tiles").transform;
            var connections = CreateChild(topologyObject.transform, "Connections").transform;
            var environment = CreateChild(rootObject.transform, "Environment").transform;
            mapRoot.Configure(
                _definition,
                topology,
                tiles,
                connections,
                environment,
                null,
                Array.Empty<Transform>());
            EditorUtility.SetDirty(mapRoot);
            _mapRoot = mapRoot;
            _issues.Clear();
            Selection.activeGameObject = rootObject;
            MarkSceneDirty();
        }

        private void BindDefinitionToRoot()
        {
            if (_mapRoot == null || _definition == null)
            {
                return;
            }

            var serializedRoot = new SerializedObject(_mapRoot);
            Undo.RecordObject(_mapRoot, "Bind Board Map Definition");
            serializedRoot.Update();
            serializedRoot.FindProperty("definition").objectReferenceValue = _definition;
            serializedRoot.ApplyModifiedProperties();
            RecordPrefabInstance(_mapRoot);
            MarkSceneDirty();
        }

        private void AssignSelectedPlayerStart()
        {
            var selected = GetSelectedTile();
            if (_mapRoot == null || selected == null || !IsOwnedTile(selected))
            {
                return;
            }

            SetPlayerStartAssignment(_playerStartSlot, selected);
        }

        private void ClearPlayerStartAssignment()
        {
            if (_mapRoot != null)
            {
                SetPlayerStartAssignment(_playerStartSlot, null);
            }
        }

        private void SetPlayerStartAssignment(int playerSlot, BoardTile tile)
        {
            var serializedRoot = new SerializedObject(_mapRoot);
            Undo.RecordObject(_mapRoot, "Set Player Start Tile");
            serializedRoot.Update();
            var assignments = serializedRoot.FindProperty("playerStartTiles");
            assignments.arraySize = PlayerSlotRules.Count;
            assignments.GetArrayElementAtIndex(playerSlot).objectReferenceValue = tile;
            serializedRoot.ApplyModifiedProperties();
            RecordPrefabInstance(_mapRoot);
            MarkSceneDirty();
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(child, $"Create {name}");
            Undo.SetTransformParent(child.transform, parent, $"Parent {name}");
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            return child;
        }

        private void TryAdoptSelection(bool notifyFailure = false)
        {
            var active = Selection.activeGameObject;
            var candidate = active != null ? active.GetComponentInParent<BoardMapRoot>() : null;
            if (candidate == null && active != null)
            {
                var topology = active.GetComponentInParent<BoardTopology>();
                candidate = topology != null ? topology.GetComponentInParent<BoardMapRoot>() : null;
            }

            if (candidate != null)
            {
                _mapRoot = candidate;
                if (candidate.Definition != null)
                {
                    _definition = candidate.Definition;
                }
            }
            else if (notifyFailure)
            {
                ShowNotification(new GUIContent("Selection is not inside a Board Map Root."));
            }
        }

        private BoardTile GetSelectedTile()
        {
            return Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<BoardTile>()
                : null;
        }

        private BoardGate GetSelectedGate()
        {
            if (Selection.activeGameObject == null)
            {
                return null;
            }

            var gate = Selection.activeGameObject.GetComponent<BoardGate>() ??
                       Selection.activeGameObject.GetComponentInChildren<BoardGate>(true);
            if (gate == null ||
                _mapRoot == null ||
                _mapRoot.ConnectionsRoot == null ||
                !gate.transform.IsChildOf(_mapRoot.ConnectionsRoot))
            {
                return null;
            }

            return gate;
        }

        private bool IsOwnedTile(BoardTile tile)
        {
            return tile != null &&
                   _mapRoot != null &&
                   _mapRoot.TilesRoot != null &&
                   tile.transform.IsChildOf(_mapRoot.TilesRoot);
        }

        private void CreateDefinitionAsset()
        {
            EnsureAssetFolder(DefaultMapFolder);
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Board Map Definition",
                "BoardMap",
                "asset",
                "Choose a location for the map definition.",
                DefaultMapFolder);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var definition = CreateInstance<BoardMapDefinition>();
            var fileName = Path.GetFileNameWithoutExtension(path);
            definition.Configure(
                BoardMapDefinition.NormalizeMapId(fileName),
                ObjectNames.NicifyVariableName(fileName),
                1,
                null);
            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssets();
            _definition = definition;
            Selection.activeObject = definition;
        }

        private void CreateCatalogAsset()
        {
            EnsureAssetFolder(DefaultMapFolder);
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Board Map Catalog",
                "BoardMapCatalog",
                "asset",
                "Choose a location for the map catalog.",
                DefaultMapFolder);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var catalog = CreateInstance<BoardMapCatalog>();
            if (_definition != null)
            {
                catalog.Configure(new[] { _definition });
            }

            AssetDatabase.CreateAsset(catalog, path);
            AssetDatabase.SaveAssets();
            _catalog = catalog;
            Selection.activeObject = catalog;
        }

        private void AddDefinitionToCatalog()
        {
            var definitions = _catalog.Maps
                .Where(definition => definition != null)
                .ToList();
            if (!definitions.Contains(_definition))
            {
                definitions.Add(_definition);
            }

            Undo.RecordObject(_catalog, "Add Board Map To Catalog");
            _catalog.Configure(definitions.ToArray());
            EditorUtility.SetDirty(_catalog);
            AssetDatabase.SaveAssets();
        }

        private void SaveMapRootPrefab()
        {
            EnsureAssetFolder(DefaultMapFolder);
            var defaultName = _definition != null
                ? _definition.MapId + "-map-root"
                : "board-map-root";
            var path = EditorUtility.SaveFilePanelInProject(
                "Save Board Map Root Prefab",
                defaultName,
                "prefab",
                "Save the complete authored map root. Existing environment visuals are kept.",
                DefaultMapFolder);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            RefreshTopology();
            var prefab = PrefabUtility.SaveAsPrefabAsset(_mapRoot.gameObject, path);
            if (prefab == null)
            {
                Debug.LogError($"Could not save board map prefab at '{path}'.");
                return;
            }

            if (_definition != null)
            {
                Undo.RecordObject(_definition, "Bind Board Map Prefab");
                _definition.Configure(
                    _definition.MapId,
                    _definition.DisplayName,
                    _definition.ContentVersion,
                    prefab);
                EditorUtility.SetDirty(_definition);
                AssetDatabase.SaveAssets();
            }

            Selection.activeObject = prefab;
        }

        private static void EnsureAssetFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                var next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        private void MarkSceneDirty()
        {
            if (_mapRoot != null && _mapRoot.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(_mapRoot.gameObject.scene);
            }
        }

        private static void RecordPrefabInstance(UnityEngine.Object target)
        {
            if (target != null && PrefabUtility.IsPartOfPrefabInstance(target))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
        }

        private readonly struct AuthoringIssue
        {
            public AuthoringIssue(
                string message,
                UnityEngine.Object context,
                MessageType type = MessageType.Error)
            {
                Message = message;
                Context = context;
                Type = type;
            }

            public string Message { get; }
            public UnityEngine.Object Context { get; }
            public MessageType Type { get; }
        }
    }
}
