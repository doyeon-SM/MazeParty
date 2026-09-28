using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    [CreateAssetMenu(menuName = "MazeParty/Player Expressions")]
    public sealed class PlayerExpressionCatalog : ScriptableObject
    {
        [Serializable] public class Face { public string Name; public Sprite Sprite; }
        [Serializable]
        public class Hat
        {
            public string Name;
            public GameObject Prefab;
            public Vector3 LocalPosition;
            public Vector3 LocalEulerAngles;
            public Vector3 LocalScale = Vector3.one;
        }
        [Serializable] public class Gesture { public string Name; public GameObject HandsPrefab; }
        public Face[] Faces = Array.Empty<Face>();
        public Hat[] Hats = Array.Empty<Hat>();
        public Gesture[] Gestures = Array.Empty<Gesture>();
        private static PlayerExpressionCatalog _instance;
        public static PlayerExpressionCatalog Instance => _instance != null ? _instance :
            (_instance = Resources.Load<PlayerExpressionCatalog>("MazeParty/Expressions/PlayerExpressions"));
        // Default appearance is also constructed by Unity's serialization thread.
        public static byte SanitizeFace(byte id) => id == 0 ? (byte)0 : Instance != null && id < Instance.Faces.Length ? id : (byte)0;
        public static byte SanitizeHat(byte id) => id > 0 && Instance != null && Instance.Hats != null && id <= Instance.Hats.Length &&
            Instance.Hats[id - 1] != null && Instance.Hats[id - 1].Prefab != null ? id : (byte)0;
        public static bool HasGesture(byte id) => Instance != null && id > 0 && id <= Instance.Gestures.Length &&
            Instance.Gestures[id - 1].HandsPrefab != null;
    }

    public static class HandEmoteRules
    {
        public const double Duration = 1d;
        public static bool CanStart(double now, double previousEnd, bool allowed, bool validGesture) =>
            allowed && validGesture && !double.IsNaN(now) && !double.IsInfinity(now) && now >= previousEnd;
        public static bool IsActive(byte id, double end, double now) => id > 0 && now < end;
        // Top, bottom-right, bottom-left; the center cancels selection.
        public static int Select(Vector2 offset, float deadZone, int count)
        {
            if (count <= 0 || offset.sqrMagnitude < deadZone * deadZone) return -1;
            float angle = Mathf.Repeat(Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg + 180f / count, 360f);
            return Mathf.FloorToInt(angle / (360f / count)) % count;
        }
    }
}
