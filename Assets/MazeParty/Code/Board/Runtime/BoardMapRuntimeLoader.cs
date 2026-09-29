using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Activates the server-selected authored map on every peer. The legacy scene
    /// topology remains the single compatibility surface for existing board systems;
    /// a selected map supplies that topology's tiles and gates at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardMapRuntimeLoader : MonoBehaviour
    {
        public const string CatalogResourcesPath =
            "MazeParty/Board/Maps/BoardMapCatalog";

        [SerializeField] private BoardMapCatalog catalogOverride;
        [SerializeField] private BoardTopology legacyTopology;
        [SerializeField] private GameObject[] legacyContentRoots =
            Array.Empty<GameObject>();
        [SerializeField] private Transform mapContainer;

        private BoardTile[] _legacyTiles = Array.Empty<BoardTile>();
        private BoardGate[] _legacyGates = Array.Empty<BoardGate>();
        private BoardMapRoot _activeMapRoot;
        private GameObject _activeMapObject;
        private BoardMapSelection _activeSelection;
        private bool _legacyCaptured;
        private bool _isReady;
        private string _lastFailure = string.Empty;

        public static BoardMapRuntimeLoader Instance { get; private set; }

        public static BoardTopology ActiveTopology =>
            Instance != null && Instance._isReady
                ? Instance.legacyTopology
                : null;

        public static BoardMapRoot ActiveMapRoot =>
            Instance != null && Instance._isReady
                ? Instance._activeMapRoot
                : null;

        public static BoardMapRoot ResolveActiveMapRoot(BoardTopology topology)
        {
            if (topology == null)
            {
                return null;
            }

            var authoredParent = topology.GetComponentInParent<BoardMapRoot>();
            if (authoredParent != null)
            {
                return authoredParent;
            }

            return Instance != null
                ? Instance.ResolveMapRoot(topology)
                : null;
        }

        public BoardMapCatalog Catalog => catalogOverride != null
            ? catalogOverride
            : Resources.Load<BoardMapCatalog>(CatalogResourcesPath);
        public BoardTopology RuntimeTopology => legacyTopology;
        public BoardMapRoot RuntimeMapRoot => _activeMapRoot;
        public BoardMapSelection ActiveSelection => _activeSelection;
        public bool IsReady => _isReady;
        public string LastFailure => _lastFailure;
        public bool HasRequiredReferences => legacyTopology != null;

        private void Awake()
        {
            Instance = this;
            ResolveLegacyReferences();
            CaptureLegacyTopology();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Configure(
            BoardMapCatalog catalog,
            BoardTopology sceneLegacyTopology,
            GameObject[] sceneLegacyContentRoots,
            Transform runtimeMapContainer = null)
        {
            Instance = this;
            catalogOverride = catalog;
            legacyTopology = sceneLegacyTopology;
            legacyContentRoots = sceneLegacyContentRoots != null
                ? (GameObject[])sceneLegacyContentRoots.Clone()
                : Array.Empty<GameObject>();
            mapContainer = runtimeMapContainer;
            _legacyCaptured = false;
            ResolveLegacyReferences();
            CaptureLegacyTopology();
        }

        public bool TryResolveFreshSelection(
            out BoardMapSelection selection,
            out string error)
        {
            return TryResolveFreshSelection(Catalog, out selection, out error);
        }

        public static bool TryResolveFreshSelection(
            BoardMapCatalog catalog,
            out BoardMapSelection selection,
            out string error)
        {
            if (catalog == null || catalog.Maps.Count == 0)
            {
                selection = BoardMapSelection.Legacy;
                error = string.Empty;
                return true;
            }

            if (!catalog.HasUniqueValidIds())
            {
                selection = default;
                error = "The board map catalog contains a missing, invalid, or duplicate map id.";
                return false;
            }

            var definition = catalog.Maps[0];
            if (definition == null || !definition.HasValidPrefab ||
                !BoardMapSelection.TryCreate(
                    definition.MapId,
                    definition.ContentVersion,
                    out selection))
            {
                selection = default;
                error = "The first board map catalog entry has no valid network identity or map-root prefab.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static bool TryResolveAdjacentSelection(
            BoardMapCatalog catalog,
            BoardMapSelection current,
            int delta,
            out BoardMapSelection selection,
            out string error)
        {
            if (delta == 0)
            {
                selection = current.MapId == null
                    ? BoardMapSelection.Legacy
                    : current;
                error = string.Empty;
                return true;
            }

            if (catalog == null || catalog.Maps.Count == 0)
            {
                selection = BoardMapSelection.Legacy;
                error = string.Empty;
                return true;
            }

            if (!catalog.HasUniqueValidIds())
            {
                selection = default;
                error =
                    "The board map catalog contains a missing, invalid, or duplicate map id.";
                return false;
            }

            var definitions = catalog.Maps;
            var currentIndex = -1;
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (definition == null || !definition.HasValidPrefab ||
                    !BoardMapSelection.TryCreate(
                        definition.MapId,
                        definition.ContentVersion,
                        out var candidate))
                {
                    selection = default;
                    error =
                        $"Board map catalog entry {index} has no valid network identity or map-root prefab.";
                    return false;
                }

                if (candidate == current)
                {
                    currentIndex = index;
                }
            }

            var direction = delta > 0 ? 1 : -1;
            var startIndex = currentIndex >= 0
                ? currentIndex
                : direction > 0 ? -1 : 0;
            var nextIndex = (startIndex + direction) % definitions.Count;
            if (nextIndex < 0)
            {
                nextIndex += definitions.Count;
            }

            selection = BoardMapSelection.FromDefinition(definitions[nextIndex]);
            error = string.Empty;
            return true;
        }

        public bool TryResolveExactSelection(
            BoardMapSelection requested,
            out BoardMapSelection selection,
            out string error)
        {
            return TryResolveExactSelection(
                Catalog,
                requested,
                out selection,
                out error);
        }

        public static bool TryResolveExactSelection(
            BoardMapCatalog catalog,
            BoardMapSelection requested,
            out BoardMapSelection selection,
            out string error)
        {
            if (requested.IsLegacy)
            {
                selection = requested;
                error = string.Empty;
                return true;
            }

            if (catalog == null || !catalog.HasUniqueValidIds() ||
                !catalog.TryGetMap(requested.MapId, out var definition) ||
                definition == null ||
                definition.ContentVersion != requested.ContentVersion ||
                !definition.HasValidPrefab)
            {
                selection = default;
                error = $"Board map '{requested}' is not available in the runtime catalog.";
                return false;
            }

            selection = requested;
            error = string.Empty;
            return true;
        }

        public bool TryActivate(BoardMapSelection selection, out string error)
        {
            ResolveLegacyReferences();
            CaptureLegacyTopology();
            if (legacyTopology == null)
            {
                return Fail("The Board scene has no legacy topology binding.", out error);
            }

            if (_isReady && _activeSelection == selection)
            {
                error = string.Empty;
                return true;
            }

            if (selection.IsLegacy)
            {
                ReleaseActiveMap();
                SetLegacyContentActive(true);
                legacyTopology.Configure(_legacyTiles, _legacyGates);
                _activeMapRoot = null;
                _activeSelection = BoardMapSelection.Legacy;
                _isReady = true;
                _lastFailure = string.Empty;
                error = string.Empty;
                return true;
            }

            if (!TryResolveExactSelection(selection, out _, out error))
            {
                _lastFailure = error;
                _isReady = false;
                return false;
            }

            var definition = Catalog.TryGetMap(selection.MapId, out var resolved)
                ? resolved
                : null;
            var instance = definition != null
                ? Instantiate(
                    definition.MapRootPrefab,
                    mapContainer != null ? mapContainer : transform,
                    false)
                : null;
            var mapRoot = instance != null
                ? instance.GetComponent<BoardMapRoot>()
                : null;
            if (!ValidateInstantiatedMap(definition, mapRoot, out error))
            {
                DestroyMapObject(instance);
                _lastFailure = error;
                _isReady = false;
                return false;
            }

            var authoredTopology = mapRoot.Topology;
            var tiles = Copy(authoredTopology.Tiles);
            var gates = Copy(authoredTopology.Gates);

            ReleaseActiveMap();
            _activeMapObject = instance;
            _activeMapObject.name = $"Active Board Map - {selection.MapId}";
            _activeMapRoot = mapRoot;
            SetLegacyContentActive(false);
            legacyTopology.Configure(tiles, gates);
            _activeSelection = selection;
            _isReady = true;
            _lastFailure = string.Empty;
            error = string.Empty;
            return true;
        }

        public BoardMapRoot ResolveMapRoot(BoardTopology topology)
        {
            if (!_isReady || _activeMapRoot == null || topology == null)
            {
                return null;
            }

            return topology == legacyTopology || topology == _activeMapRoot.Topology
                ? _activeMapRoot
                : null;
        }

        private void ResolveLegacyReferences()
        {
            if (legacyTopology == null)
            {
                var topologies = FindObjectsByType<BoardTopology>(
                    FindObjectsInactive.Include);
                for (var index = 0; index < topologies.Length; index++)
                {
                    if (topologies[index] != null &&
                        topologies[index].GetComponentInParent<BoardMapRoot>() == null)
                    {
                        legacyTopology = topologies[index];
                        break;
                    }
                }
            }

            if (mapContainer == null)
            {
                mapContainer = transform;
            }

            if ((legacyContentRoots == null || legacyContentRoots.Length == 0) &&
                legacyTopology != null)
            {
                var roots = new List<GameObject>();
                for (var index = 0; index < legacyTopology.transform.childCount; index++)
                {
                    var child = legacyTopology.transform.GetChild(index);
                    if (child.GetComponentInChildren<BoardTile>(true) != null ||
                        child.GetComponentInChildren<BoardGate>(true) != null)
                    {
                        roots.Add(child.gameObject);
                    }
                }

                legacyContentRoots = roots.ToArray();
            }
        }

        private void CaptureLegacyTopology()
        {
            if (_legacyCaptured || legacyTopology == null)
            {
                return;
            }

            _legacyTiles = Copy(legacyTopology.Tiles);
            _legacyGates = Copy(legacyTopology.Gates);
            _legacyCaptured = true;
        }

        private static bool ValidateInstantiatedMap(
            BoardMapDefinition definition,
            BoardMapRoot mapRoot,
            out string error)
        {
            if (definition == null || mapRoot == null ||
                mapRoot.Definition != definition ||
                !mapRoot.HasAuthoringRoots ||
                mapRoot.Topology == null)
            {
                error = "The selected board map prefab does not match its definition or authoring roots.";
                return false;
            }

            var validation = mapRoot.Topology.ValidateTopology();
            if (!validation.IsValid)
            {
                error = validation.Issues.Count > 0
                    ? validation.Issues[0].Message
                    : "The selected board map topology is invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void SetLegacyContentActive(bool active)
        {
            var roots = legacyContentRoots ?? Array.Empty<GameObject>();
            for (var index = 0; index < roots.Length; index++)
            {
                if (roots[index] != null && roots[index].activeSelf != active)
                {
                    roots[index].SetActive(active);
                }
            }
        }

        private void ReleaseActiveMap()
        {
            if (_activeMapObject == null)
            {
                return;
            }

            _activeMapObject.SetActive(false);
            DestroyMapObject(_activeMapObject);
            _activeMapObject = null;
            _activeMapRoot = null;
        }

        private static void DestroyMapObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private bool Fail(string message, out string error)
        {
            _isReady = false;
            _lastFailure = message;
            error = message;
            return false;
        }

        private static T[] Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<T>();
            }

            var result = new T[source.Count];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = source[index];
            }

            return result;
        }
    }
}
