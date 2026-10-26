using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>Authored cameras share one event shot without changing the board's game state.</summary>
    [DefaultExecutionOrder(1200)]
    public sealed class BoardEventSplitView : MonoBehaviour
    {
        [SerializeField] private Camera[] cameras = new Camera[4];
        private readonly Transform[] _roots = new Transform[4];
        private readonly Transform[] _heads = new Transform[4];
        private GameplayCameraDirector _director;
        private Camera _source;
        private bool _sourceWasEnabled;
        private int _count;
        private float _focusHeight;
        [SerializeField, Min(1f)] private float retreatCameraDistance = 7f;
        [SerializeField, Range(20f, 100f)] private float retreatFieldOfView = 55f;
        private NetworkMatchState _retreatMatch;
        private readonly int[] _retreatSlots = new int[4];
        private readonly BoardEventCameraOcclusion _occlusion = new BoardEventCameraOcclusion();

        public bool HasRequiredReferences => cameras != null && cameras.Length == 4 &&
            System.Array.TrueForAll(cameras, camera => camera != null);

        public static Rect Viewport(int count, int index)
        {
            if (count < 4) return new Rect(index / (float)count, 0f, 1f / count, 1f);
            return new Rect(index % 2 * 0.5f, index < 2 ? 0.5f : 0f, 0.5f, 0.5f);
        }

        public void Configure(Camera[] views) => cameras = views;

        public void Present(GameplayCameraDirector director, Transform[] roots, Transform[] heads,
            int count, float focusHeight)
        {
            if (!HasRequiredReferences || director == null || director.OutputCamera == null || count <= 0)
            { Clear(); return; }
            if (_source != director.OutputCamera)
            {
                Clear();
                _source = director.OutputCamera;
                _sourceWasEnabled = _source.enabled;
            }
            _retreatMatch = null;
            _director = director;
            _count = Mathf.Min(4, count);
            _focusHeight = focusHeight;
            System.Array.Copy(roots, _roots, _count);
            System.Array.Copy(heads, _heads, _count);
            RefreshCameras();
        }

        public void PresentCombatRetreat(GameplayCameraDirector director, Transform[] roots,
            Transform[] heads, int[] slots, int count, NetworkMatchState match)
        {
            Present(director, roots, heads, count, 0f);
            if (_source == null) return;
            _retreatMatch = match;
            System.Array.Copy(slots, _retreatSlots, _count);
            RefreshCameras();
        }

        private void LateUpdate() { if (_count > 0) RefreshCameras(); }
        private void RefreshCameras()
        {
            if (_source == null || _director == null) { Clear(); return; }
            _source.enabled = false;
            for (var index = 0; index < cameras.Length; index++)
            {
                var camera = cameras[index];
                camera.enabled = index < _count && _roots[index] != null;
                _occlusion.SetFocus(camera, camera.enabled ? _roots[index] : null, _retreatMatch != null);
                if (!camera.enabled) continue;
                camera.CopyFrom(_source);
                camera.enabled = true;
                camera.orthographic = false;
                camera.rect = Viewport(_count, index);
                camera.aspect = _source.aspect * camera.rect.width / camera.rect.height;
                camera.depth = _source.depth + index + 1f;
                camera.tag = "Untagged";
                Vector3 position;
                Quaternion rotation;
                float fieldOfView;
                if (_retreatMatch != null)
                {
                    var path = _retreatMatch.CombatRetreat.GetPath(_retreatSlots[index]);
                    var elapsed = _retreatMatch.CombatRetreatElapsed;
                    var delayed = path.Evaluate(elapsed - BoardCombatRetreatTiming.CameraDelay);
                    var focus = path.Evaluate(elapsed);
                    // Delay the camera's translation, while keeping the moving body
                    // in frame. The growing gap makes the retreat readable.
                    position = delayed + path.Facing * Vector3.forward * retreatCameraDistance + Vector3.up * 1.2f;
                    rotation = Quaternion.LookRotation(focus - position, Vector3.up);
                    fieldOfView = retreatFieldOfView;
                }
                else
                    _director.CalculateBoardResourceEventPose(_roots[index], _heads[index], _focusHeight,
                        out position, out rotation, out fieldOfView);
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
            }
        }

        public void Clear()
        {
            _occlusion.Dispose();
            if (cameras != null)
                foreach (var camera in cameras) if (camera != null) camera.enabled = false;
            if (_source != null) _source.enabled = _sourceWasEnabled;
            _source = null;
            _retreatMatch = null;
            _director = null;
            _count = 0;
        }
        private void OnDisable() => Clear();
    }
}
