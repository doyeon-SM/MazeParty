using UnityEngine;

namespace MazeParty.Gameplay.Minigames.Minefield
{
    /// <summary>
    /// Orthographic top-view camera with optional multi-target framing. A fixed
    /// world center works for the rules image and full-course view; player targets
    /// can be supplied for a tighter gameplay follow.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class MinefieldTopDownCamera : MonoBehaviour
    {
        [SerializeField] private Transform[] framingTargets = new Transform[0];
        [SerializeField] private Vector3 worldCenter;
        [SerializeField, Min(0.01f)] private float cameraHeight = 20f;
        [SerializeField] private Vector3 topDownEuler = new Vector3(90f, 0f, 0f);
        [SerializeField] private bool autoFrameTargets;
        [SerializeField, Min(0.1f)] private float fixedOrthographicSize = 10f;
        [SerializeField, Min(0.1f)] private float minimumOrthographicSize = 6f;
        [SerializeField, Min(0.1f)] private float maximumOrthographicSize = 18f;
        [SerializeField, Min(0f)] private float framingPadding = 2f;
        [SerializeField, Min(0f)] private float followSmoothTime = 0.18f;
        [SerializeField, Min(0f)] private float zoomSmoothTime = 0.18f;

        private Camera _camera;
        private Vector3 _positionVelocity;
        private float _zoomVelocity;

        public Camera OutputCamera => _camera;
        public Vector3 CurrentCenter { get; private set; }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            ConfigureCamera();
        }

        private void OnEnable()
        {
            SnapToDesiredView();
        }

        private void LateUpdate()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }
            ConfigureCamera();

            EvaluateDesiredView(out var center, out var size);
            var desiredPosition = center + Vector3.up * cameraHeight;
            if (followSmoothTime <= 0f)
            {
                transform.position = desiredPosition;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(
                    transform.position,
                    desiredPosition,
                    ref _positionVelocity,
                    followSmoothTime);
            }

            if (zoomSmoothTime <= 0f)
            {
                _camera.orthographicSize = size;
            }
            else
            {
                _camera.orthographicSize = Mathf.SmoothDamp(
                    _camera.orthographicSize,
                    size,
                    ref _zoomVelocity,
                    zoomSmoothTime);
            }

            transform.rotation = Quaternion.Euler(topDownEuler);
            CurrentCenter = center;
        }

        public void SetFramingTargets(params Transform[] targets)
        {
            framingTargets = targets ?? new Transform[0];
        }

        public void SetWorldCenter(Vector3 center)
        {
            worldCenter = center;
        }

        public void SetAutoFrameTargets(bool enabled)
        {
            autoFrameTargets = enabled;
        }

        public void SnapToDesiredView()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }
            ConfigureCamera();
            EvaluateDesiredView(out var center, out var size);
            transform.SetPositionAndRotation(
                center + Vector3.up * cameraHeight,
                Quaternion.Euler(topDownEuler));
            _camera.orthographicSize = size;
            _positionVelocity = Vector3.zero;
            _zoomVelocity = 0f;
            CurrentCenter = center;
        }

        private void ConfigureCamera()
        {
            if (_camera == null)
            {
                return;
            }

            _camera.orthographic = true;
        }

        private void EvaluateDesiredView(out Vector3 center, out float size)
        {
            center = worldCenter;
            size = Mathf.Max(0.1f, fixedOrthographicSize);
            if (!autoFrameTargets || framingTargets == null)
            {
                return;
            }

            var hasTarget = false;
            var minimum = new Vector3(
                float.PositiveInfinity,
                0f,
                float.PositiveInfinity);
            var maximum = new Vector3(
                float.NegativeInfinity,
                0f,
                float.NegativeInfinity);

            for (var index = 0; index < framingTargets.Length; index++)
            {
                var target = framingTargets[index];
                if (target == null)
                {
                    continue;
                }

                hasTarget = true;
                var position = target.position;
                minimum.x = Mathf.Min(minimum.x, position.x);
                minimum.z = Mathf.Min(minimum.z, position.z);
                maximum.x = Mathf.Max(maximum.x, position.x);
                maximum.z = Mathf.Max(maximum.z, position.z);
            }

            if (!hasTarget)
            {
                return;
            }

            center = new Vector3(
                (minimum.x + maximum.x) * 0.5f,
                worldCenter.y,
                (minimum.z + maximum.z) * 0.5f);
            var halfWidth = (maximum.x - minimum.x) * 0.5f;
            var halfDepth = (maximum.z - minimum.z) * 0.5f;
            var aspect = _camera != null
                ? Mathf.Max(0.01f, _camera.aspect)
                : 1f;
            size = Mathf.Clamp(
                Mathf.Max(halfDepth, halfWidth / aspect) + framingPadding,
                minimumOrthographicSize,
                maximumOrthographicSize);
        }
        public void ConfigureView(
            Vector3 center,
            float height,
            float orthographicSize,
            bool frameTargetsAutomatically)
        {
            worldCenter = center;
            cameraHeight = Mathf.Max(0.01f, height);
            fixedOrthographicSize = Mathf.Max(0.1f, orthographicSize);
            autoFrameTargets = frameTargetsAutomatically;
            SnapToDesiredView();
        }
    }
}
