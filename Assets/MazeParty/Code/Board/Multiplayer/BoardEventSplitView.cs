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
            _director = director;
            _count = Mathf.Min(4, count);
            _focusHeight = focusHeight;
            System.Array.Copy(roots, _roots, _count);
            System.Array.Copy(heads, _heads, _count);
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
                if (!camera.enabled) continue;
                camera.CopyFrom(_source);
                camera.enabled = true;
                camera.orthographic = false;
                camera.rect = Viewport(_count, index);
                camera.depth = _source.depth + index + 1f;
                camera.tag = "Untagged";
                _director.CalculateBoardResourceEventPose(_roots[index], _heads[index], _focusHeight,
                    out var position, out var rotation, out var fieldOfView);
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
            }
        }

        public void Clear()
        {
            if (cameras != null)
                foreach (var camera in cameras) if (camera != null) camera.enabled = false;
            if (_source != null) _source.enabled = _sourceWasEnabled;
            _source = null;
            _director = null;
            _count = 0;
        }
        private void OnDisable() => Clear();
    }
}
