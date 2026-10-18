#if UNITY_EDITOR || (DEVELOPMENT_BUILD && !MAZEPARTY_STOVE && !MAZEPARTY_STEAM)
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Development-only telemetry. All graphics are authored in its prefab.</summary>
    public sealed class DevelopmentDiagnosticsPanel : MonoBehaviour
    {
        [SerializeField] private Canvas panelCanvas;
        [SerializeField] private Text metrics;
        [SerializeField] private Text logs;
        [SerializeField] private Text status;
        private static DevelopmentDiagnosticsPanel _instance;
        private readonly object _gate = new object();
        private readonly Queue<string> _lines = new Queue<string>(512);
        private readonly float[] _frames = new float[240];
        private readonly float[] _sorted = new float[240];
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private int _frameIndex, _frameCount;
        private float _nextRefresh, _copiedUntil;
        private ProfilerRecorder _cpu, _gc, _triangles;
        private readonly List<ProfilerRecorder> _drawCalls = new List<ProfilerRecorder>();
        private bool _recording;
        public bool HasRequiredReferences => panelCanvas != null && metrics != null && logs != null && status != null &&
            metrics.transform.IsChildOf(transform) && logs.transform.IsChildOf(transform) && status.transform.IsChildOf(transform);
        public void Configure(Canvas canvas, Text values, Text logText, Text footer)
        { panelCanvas = canvas; metrics = values; logs = logText; status = footer; }

        private void Awake()
        {
            if (PlatformContext.IsStoreBuild || (_instance != null && _instance != this))
            { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            if (!HasRequiredReferences) { enabled = false; return; }
            panelCanvas.enabled = false;
            Application.logMessageReceivedThreaded += CaptureLog;
        }
        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= CaptureLog;
            StopCounters();
            if (_instance == this) _instance = null;
        }
        private void CaptureLog(string message, string stack, LogType type)
        {
            // Keep bounded memory even if an exception repeats every frame.
            var text = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + type + "] " + message;
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !string.IsNullOrEmpty(stack))
                text += "\n" + stack;
            if (text.Length > 2400) text = text.Substring(0, 2400) + "...";
            lock (_gate) { if (_lines.Count == 512) _lines.Dequeue(); _lines.Enqueue(text); }
        }
        private void Update()
        {
            if (!HasRequiredReferences || PlatformContext.IsStoreBuild) return;
            _frames[_frameIndex] = Time.unscaledDeltaTime * 1000f;
            _frameIndex = (_frameIndex + 1) % _frames.Length;
            _frameCount = Mathf.Min(_frameCount + 1, _frames.Length);
            var keyboard = Keyboard.current;
            if (keyboard != null && !IsTyping())
            {
                if (keyboard.lKey.wasPressedThisFrame)
                {
                    panelCanvas.enabled = !panelCanvas.enabled;
                    if (panelCanvas.enabled) { StartCounters(); _nextRefresh = 0f; }
                    else StopCounters();
                }
                if (panelCanvas.enabled && keyboard.cKey.wasPressedThisFrame)
                {
                    GUIUtility.systemCopyBuffer = "MazeParty development " + Application.version + "\n" +
                        DateTime.Now.ToString("O") + "\n" + metrics.text + "\n\n" + BuildLogs(false);
                    _copiedUntil = Time.unscaledTime + 2f;
                    _nextRefresh = 0f;
                }
            }
            if (!panelCanvas.enabled) return;
            FrameTimingManager.CaptureFrameTimings();
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + .25f;
            RefreshMetrics();
            logs.text = BuildLogs(true);
            status.text = Time.unscaledTime < _copiedUntil ? "Logs copied to clipboard" : "L  Close     C  Copy logs + metrics     Last 512 Unity logs + server trace";
        }
        private static bool IsTyping()
        {
            var current = EventSystem.current?.currentSelectedGameObject;
            return current != null && (current.GetComponent<InputField>() != null || current.GetComponent<TMP_InputField>() != null);
        }
        private void StartCounters()
        {
            if (_recording) return;
            _recording = true;
            _cpu = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
            var handles = new List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
            Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var handle in handles)
            {
                var description = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(handle);
                // New Unity versions split render draw calls by path. Avoid counting UI Toolkit or batched input geometry twice.
                if (description.Category == ProfilerCategory.Render && description.Name.EndsWith("Draw Calls Count") &&
                    !description.Name.Contains("Batched"))
                    _drawCalls.Add(ProfilerRecorder.StartNew(description.Category, description.Name, 1));
            }
        }
        private void StopCounters()
        {
            if (!_recording) return;
            _cpu.Dispose(); _gc.Dispose(); _triangles.Dispose();
            foreach (var recorder in _drawCalls) recorder.Dispose();
            _drawCalls.Clear(); _recording = false;
        }
        private void RefreshMetrics()
        {
            float sum = 0;
            for (var i = 0; i < _frameCount; i++) { _sorted[i] = _frames[i]; sum += _frames[i]; }
            Array.Sort(_sorted, 0, _frameCount);
            var average = _frameCount > 0 ? sum / _frameCount : 0;
            var p95 = _frameCount > 0 ? _sorted[Mathf.Clamp(Mathf.CeilToInt(_frameCount * .95f) - 1, 0, _frameCount - 1)] : 0;
            var timingCount = FrameTimingManager.GetLatestTimings(1, _timings);
            var gpu = timingCount > 0 && _timings[0].gpuFrameTime > 0 ? _timings[0].gpuFrameTime.ToString("F2") + " ms" : "N/A";
            var cpu = _cpu.Valid && _cpu.Count > 0 ? (_cpu.LastValue / 1000000d).ToString("F2") + " ms" : "N/A";
            long draws = 0; var drawAvailable = false;
            foreach (var recorder in _drawCalls) if (recorder.Valid && recorder.Count > 0) { draws += recorder.LastValue; drawAvailable = true; }
            var manager = NetworkManager.Singleton;
            var network = "Offline";
            if (manager != null && manager.IsListening)
            {
                var rtt = manager.IsServer ? "N/A (host)" : manager.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId) + " ms";
                network = (manager.IsHost ? "Host" : manager.IsServer ? "Server" : "Client") + "   RTT " + rtt +
                    "   Tick " + manager.NetworkConfig.TickRate + " Hz   Players " + (manager.IsServer ? manager.ConnectedClientsIds.Count.ToString() : "N/A");
            }
            metrics.text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "FPS {0:F1}     Frame {1:F2} ms     P95 {2:F2} ms\nCPU main {3}     GPU {4}\nMemory {5:F1} MB     Managed {6:F1} MB     GC/frame {7}\nDraw calls {8}     Triangles {9}\n{10}\n{11}x{12}  {13}  VSync {14}  Target FPS {15}  Time scale {16:F2}",
                average > 0 ? 1000f / average : 0, average, p95, cpu, gpu,
                Profiler.GetTotalAllocatedMemoryLong() / 1048576d, GC.GetTotalMemory(false) / 1048576d,
                _gc.Valid && _gc.Count > 0 ? (_gc.LastValue / 1024d).ToString("F1") + " KB" : "N/A",
                drawAvailable ? draws.ToString() : "N/A", _triangles.Valid && _triangles.Count > 0 ? _triangles.LastValue.ToString("N0") : "N/A",
                network, Screen.width, Screen.height, Application.isEditor ? "Editor" : "Development", QualitySettings.vSyncCount, Application.targetFrameRate, Time.timeScale);
        }
        private string BuildLogs(bool preview)
        {
            var builder = new StringBuilder();
            var trace = ServerEventTrace.Snapshot();
            builder.AppendLine("SERVER EVENTS");
            for (var i = preview ? Math.Max(0, trace.Length - 4) : 0; i < trace.Length; i++)
            {
                var e = trace[i];
                builder.Append(e.Sequence).Append(" t=").Append(e.RecordedAt.ToString("F3"))
                    .Append(' ').Append(e.Code).Append(" turn=").Append(e.Turn).Append(" state=").Append(e.State)
                    .Append(" slot=").Append(e.Slot).Append(" values=").Append(e.Value0).Append(',').Append(e.Value1).AppendLine();
            }
            if (trace.Length == 0) builder.AppendLine("(no server events)");
            builder.AppendLine("UNITY LOGS");
            lock (_gate)
            {
                var skip = preview ? Math.Max(0, _lines.Count - 10) : 0;
                foreach (var line in _lines)
                {
                    if (skip-- > 0) continue;
                    // Preview is one compact line per entry; the clipboard retains stack traces.
                    var newline = preview ? line.IndexOf('\n') : -1;
                    var shown = newline >= 0 ? line.Substring(0, newline) : line;
                    if (preview && shown.Length > 110) shown = shown.Substring(0, 107) + "...";
                    builder.AppendLine(shown);
                }
            }
            return builder.ToString();
        }
    }
}
#endif
