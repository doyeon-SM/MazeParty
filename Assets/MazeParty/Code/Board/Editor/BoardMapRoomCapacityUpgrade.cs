using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Editor
{
    /// <summary>
    /// Pads every board map view to <see cref="BoardMinimapView.MaxRoomCount"/> room icons by
    /// cloning its last authored map cell. Existing cells, their layout and styling are never
    /// changed; freeform maps position every room from its tile at runtime.
    /// </summary>
    public static class BoardMapRoomCapacityUpgrade
    {
        private static readonly string[] SingleFields = { "Floor", "Symbol", "TypeIcon", "EffectIcon" };
        private static readonly string[] SideFields = { "Walls", "Exits", "ProgressArrows" };

        public static void Ensure(GameObject root)
        {
            foreach (var view in root.GetComponentsInChildren<BoardMinimapView>(true))
            {
                Pad(view);
            }
        }

        private static void Pad(BoardMinimapView view)
        {
            var data = new SerializedObject(view);
            var rooms = data.FindProperty("rooms");
            var authoredCount = rooms.arraySize;
            if (authoredCount == 0 || authoredCount >= BoardMinimapView.MaxRoomCount)
            {
                return;
            }

            var templateFloor = rooms.GetArrayElementAtIndex(authoredCount - 1)
                .FindPropertyRelative("Floor").objectReferenceValue as Component;
            if (templateFloor == null)
            {
                return;
            }

            var templateRoot = templateFloor.transform;
            var siblingIndex = templateRoot.GetSiblingIndex();
            for (var index = authoredCount; index < BoardMinimapView.MaxRoomCount; index++)
            {
                var clone = Object.Instantiate(templateRoot.gameObject, templateRoot.parent, false);
                clone.transform.SetSiblingIndex(++siblingIndex);
                clone.SetActive(false);

                rooms.arraySize = index + 1;
                var template = rooms.GetArrayElementAtIndex(authoredCount - 1);
                var room = rooms.GetArrayElementAtIndex(index);
                foreach (var field in SingleFields)
                {
                    Remap(template.FindPropertyRelative(field), room.FindPropertyRelative(field),
                        templateRoot, clone.transform);
                }

                foreach (var field in SideFields)
                {
                    var source = template.FindPropertyRelative(field);
                    var target = room.FindPropertyRelative(field);
                    target.arraySize = source.arraySize;
                    for (var side = 0; side < source.arraySize; side++)
                    {
                        Remap(source.GetArrayElementAtIndex(side), target.GetArrayElementAtIndex(side),
                            templateRoot, clone.transform);
                    }
                }

                var x = index % BoardMapView.GridSize;
                var y = index / BoardMapView.GridSize;
                clone.name = $"Map Cell {x}_{y}";
                RenameMarker(clone.transform, x, y);
            }

            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(view);
        }

        private static void Remap(
            SerializedProperty source,
            SerializedProperty target,
            Transform templateRoot,
            Transform cloneRoot)
        {
            var component = source.objectReferenceValue as Component;
            if (component == null)
            {
                target.objectReferenceValue = null;
                return;
            }

            var path = AnimationUtility.CalculateTransformPath(component.transform, templateRoot);
            var mapped = string.IsNullOrEmpty(path) ? cloneRoot : cloneRoot.Find(path);
            target.objectReferenceValue = mapped != null
                ? mapped.GetComponent(component.GetType())
                : null;
        }

        private static void RenameMarker(Transform cell, int x, int y)
        {
            for (var index = 0; index < cell.childCount; index++)
            {
                var child = cell.GetChild(index);
                if (child.name.StartsWith("Marker "))
                {
                    child.name = $"Marker {x}_{y}";
                }
            }
        }
    }
}
