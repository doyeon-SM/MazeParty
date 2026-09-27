using System;
using System.Reflection;
using MazeParty.Gameplay;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Editor
{
    /// <summary>
    /// SoundCue inspector with a preview button that plays the next
    /// variation the way the game would pick it (volume/pitch variance and
    /// the mixer are not applied in the preview).
    /// </summary>
    [CustomEditor(typeof(SoundCue))]
    public sealed class SoundCueEditor : UnityEditor.Editor
    {
        private const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly Type AudioUtilType =
            typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AudioUtil");

        private readonly SoundVariationPicker _picker = new SoundVariationPicker();
        private readonly System.Random _random = new System.Random();
        private string _lastPreview = string.Empty;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var cue = (SoundCue)target;
            EditorGUILayout.Space();
            if (string.IsNullOrWhiteSpace(cue.Key))
            {
                EditorGUILayout.HelpBox(
                    "Set the key the game plays (see SoundKeys), then run " +
                    "MazeParty/Audio/Refresh Sound Library.",
                    MessageType.Warning);
            }
            else if (!cue.HasClips)
            {
                EditorGUILayout.HelpBox(
                    "No clips yet: '" + cue.Key + "' plays silence. Drag clips into " +
                    "the list; several clips become random variations.",
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!cue.HasClips || AudioUtilType == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview next variation"))
                {
                    Preview(cue);
                }

                if (GUILayout.Button("Stop", GUILayout.Width(60f)))
                {
                    AudioUtilType?.GetMethod("StopAllPreviewClips", Flags)?.Invoke(null, null);
                }
            }

            if (!string.IsNullOrEmpty(_lastPreview))
            {
                EditorGUILayout.LabelField("Last preview", _lastPreview);
            }
        }

        private void OnDisable()
        {
            AudioUtilType?.GetMethod("StopAllPreviewClips", Flags)?.Invoke(null, null);
        }

        private void Preview(SoundCue cue)
        {
            AudioClip clip = null;
            for (var attempt = 0; attempt < cue.ClipCount && clip == null; attempt++)
            {
                clip = cue.GetClip(_picker.Next(cue.ClipCount, cue.VariationMode, _random));
            }

            if (clip == null)
            {
                return;
            }

            AudioUtilType.GetMethod("StopAllPreviewClips", Flags)?.Invoke(null, null);
            AudioUtilType.GetMethod("PlayPreviewClip", Flags)
                ?.Invoke(null, new object[] { clip, 0, false });
            _lastPreview = clip.name;
        }
    }
}
