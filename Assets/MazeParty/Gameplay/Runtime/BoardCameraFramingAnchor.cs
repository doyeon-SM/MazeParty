using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    [Serializable]
    public struct BoardCameraFramingSettings
    {
        [SerializeField] private Vector2 boardSize;
        [SerializeField, Range(0f, 1f)] private float marginRatio;
        [SerializeField, Range(10f, 120f)] private float fieldOfView;
        [SerializeField, Min(0.1f)] private float minimumHeight;
        [SerializeField] private float yawDegrees;

        public BoardCameraFramingSettings(
            Vector2 size,
            float margin,
            float verticalFieldOfView,
            float minHeight,
            float yaw)
        {
            boardSize = size;
            marginRatio = margin;
            fieldOfView = verticalFieldOfView;
            minimumHeight = minHeight;
            yawDegrees = yaw;
        }

        public static BoardCameraFramingSettings Default =>
            new BoardCameraFramingSettings(
                new Vector2(20f, 20f),
                0.15f,
                45f,
                8f,
                0f);

        public Vector2 BoardSize => SanitizeSize(boardSize);
        public float MarginRatio => Mathf.Clamp(marginRatio, 0f, 1f);
        public float FieldOfView => Mathf.Clamp(
            fieldOfView <= 0f ? Default.fieldOfView : fieldOfView,
            10f,
            120f);
        public float MinimumHeight => Mathf.Max(
            0.1f,
            minimumHeight <= 0f ? Default.minimumHeight : minimumHeight);
        public float YawDegrees => yawDegrees;

        public BoardCameraFramingSettings Sanitized()
        {
            return new BoardCameraFramingSettings(
                BoardSize,
                MarginRatio,
                FieldOfView,
                MinimumHeight,
                YawDegrees);
        }

        public BoardCameraPose Evaluate(Vector3 center, float aspect)
        {
            var safeSettings = Sanitized();
            var safeAspect = Mathf.Max(0.1f, aspect);
            var yawRadians = safeSettings.YawDegrees * Mathf.Deg2Rad;
            var absoluteCosine = Mathf.Abs(Mathf.Cos(yawRadians));
            var absoluteSine = Mathf.Abs(Mathf.Sin(yawRadians));

            var size = safeSettings.BoardSize;
            var projectedWidth =
                size.x * absoluteCosine + size.y * absoluteSine;
            var projectedDepth =
                size.x * absoluteSine + size.y * absoluteCosine;
            var marginScale = 1f + safeSettings.MarginRatio;

            var halfVerticalFov =
                safeSettings.FieldOfView * 0.5f * Mathf.Deg2Rad;
            var verticalTangent = Mathf.Max(0.001f, Mathf.Tan(halfVerticalFov));
            var horizontalTangent = Mathf.Max(
                0.001f,
                verticalTangent * safeAspect);

            var heightForDepth =
                projectedDepth * 0.5f * marginScale / verticalTangent;
            var heightForWidth =
                projectedWidth * 0.5f * marginScale / horizontalTangent;
            var height = Mathf.Max(
                safeSettings.MinimumHeight,
                heightForDepth,
                heightForWidth);

            return new BoardCameraPose(
                center + Vector3.up * height,
                Quaternion.Euler(90f, safeSettings.YawDegrees, 0f),
                safeSettings.FieldOfView,
                height);
        }

        private static Vector2 SanitizeSize(Vector2 size)
        {
            var defaults = Default.boardSize;
            return new Vector2(
                size.x > 0f ? size.x : defaults.x,
                size.y > 0f ? size.y : defaults.y);
        }
    }

    public readonly struct BoardCameraPose
    {
        public BoardCameraPose(
            Vector3 position,
            Quaternion rotation,
            float fieldOfView,
            float height)
        {
            Position = position;
            Rotation = rotation;
            FieldOfView = fieldOfView;
            Height = height;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float FieldOfView { get; }
        public float Height { get; }
    }

    [DisallowMultipleComponent]
    public sealed class BoardCameraFramingAnchor : MonoBehaviour
    {
        [SerializeField] private BoardCameraFramingSettings settings =
            BoardCameraFramingSettings.Default;

        public BoardCameraFramingSettings Settings => settings.Sanitized();

        public BoardCameraPose Evaluate(float aspect)
        {
            return Settings.Evaluate(transform.position, aspect);
        }

        public void Configure(BoardCameraFramingSettings framingSettings)
        {
            settings = framingSettings.Sanitized();
        }

        private void OnValidate()
        {
            settings = settings.Sanitized();
        }

        private void OnDrawGizmosSelected()
        {
            var safeSettings = Settings;
            var previousMatrix = Gizmos.matrix;
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Gizmos.matrix = Matrix4x4.TRS(
                transform.position,
                Quaternion.Euler(0f, safeSettings.YawDegrees, 0f),
                Vector3.one);
            Gizmos.DrawWireCube(
                Vector3.zero,
                new Vector3(
                    safeSettings.BoardSize.x,
                    0.05f,
                    safeSettings.BoardSize.y));
            Gizmos.matrix = previousMatrix;
        }
    }
}
