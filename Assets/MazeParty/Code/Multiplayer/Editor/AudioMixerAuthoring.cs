using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace MazeParty.Editor
{
    /// <summary>
    /// Builds the game's AudioMixer asset. Unity has no public API for
    /// authoring mixers, so this drives the editor's internal
    /// AudioMixerController by reflection (verified on Unity 6000.6). It only
    /// runs when the mixer asset is missing; afterwards edit the mixer in the
    /// Audio Mixer window.
    /// </summary>
    internal static class AudioMixerAuthoring
    {
        private const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;

        public const string MasterGroupName = "Master";
        public const string BgmGroupName = "BGM";
        public const string MusicGroupName = "Music";
        public const string SfxGroupName = "SFX";
        public const string EffectsGroupName = "Effects";
        public const string UiGroupName = "UI";

        private const string LowpassEffectName = "Lowpass Simple";
        private const string LowpassCutoffParameter = "Cutoff freq";
        private const float OpenCutoffHz = 22000f;
        private const float PausedCutoffHz = 1400f;
        private const float PausedMusicDecibels = -8f;
        private const float PausedEffectsDecibels = -4f;
        private const float DuckedMusicDecibels = -10f;

        private static readonly Assembly EditorAssembly = typeof(UnityEditor.Editor).Assembly;
        private static readonly Type ControllerType = Find("UnityEditor.Audio.AudioMixerController");
        private static readonly Type GroupType = Find("UnityEditor.Audio.AudioMixerGroupController");
        private static readonly Type EffectType = Find("UnityEditor.Audio.AudioMixerEffectController");
        private static readonly Type ParameterPathType = Find("UnityEditor.Audio.AudioGroupParameterPath");
        private static readonly Type ExposedParameterType = Find("UnityEditor.Audio.ExposedAudioParameter");
        private static readonly Type GroupViewType = Find("UnityEditor.Audio.MixerGroupView");

        /// <summary>
        /// Master (exposed MasterVolume)
        ///   BGM (exposed BgmVolume) → Music (Lowpass Simple)
        ///   SFX (exposed SfxVolume) → Effects, UI
        /// Snapshots: Default; Paused (music -8 dB and muffled, effects -4 dB);
        /// Ducked (music -10 dB for fanfares and reveals).
        /// </summary>
        public static AudioMixer Create(string path)
        {
            var controller = Invoke(null, "CreateMixerControllerAtPath", path);
            var master = GetProperty(controller, "masterGroup");
            CreateDefaultView(controller, master);

            var bgm = CreateGroup(controller, BgmGroupName, master);
            var music = CreateGroup(controller, MusicGroupName, bgm);
            var sfx = CreateGroup(controller, SfxGroupName, master);
            var effects = CreateGroup(controller, EffectsGroupName, sfx);
            CreateGroup(controller, UiGroupName, sfx);

            ExposeVolume(controller, master, "MasterVolume");
            ExposeVolume(controller, bgm, "BgmVolume");
            ExposeVolume(controller, sfx, "SfxVolume");

            var lowpass = AddEffect(controller, music, LowpassEffectName);

            var normal = (Object)((Array)GetProperty(controller, "snapshots")).GetValue(0);
            normal.name = "Default";
            SetEffectParameter(controller, lowpass, normal, LowpassCutoffParameter, OpenCutoffHz);

            var paused = CloneSnapshot(controller, "Paused");
            SetVolume(controller, music, paused, PausedMusicDecibels);
            SetVolume(controller, effects, paused, PausedEffectsDecibels);
            SetEffectParameter(controller, lowpass, paused, LowpassCutoffParameter, PausedCutoffHz);

            var ducked = CloneSnapshot(controller, "Ducked");
            SetVolume(controller, music, ducked, DuckedMusicDecibels);
            SetVolume(controller, effects, ducked, 0f);
            SetEffectParameter(controller, lowpass, ducked, LowpassCutoffParameter, OpenCutoffHz);

            SetProperty(controller, "TargetSnapshot", normal);
            EditorUtility.SetDirty((Object)controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
        }

        private static void CreateDefaultView(object controller, object master)
        {
            var groupIdProperty = GroupType.GetProperty("groupID", Flags);
            var ids = Array.CreateInstance(groupIdProperty.PropertyType, 1);
            ids.SetValue(groupIdProperty.GetValue(master, null), 0);
            var view = Activator.CreateInstance(GroupViewType);
            GroupViewType.GetField("name", Flags).SetValue(view, "View");
            GroupViewType.GetField("guids", Flags).SetValue(view, ids);
            var views = Array.CreateInstance(GroupViewType, 1);
            views.SetValue(view, 0);
            SetProperty(controller, "views", views);
        }

        private static object CreateGroup(object controller, string name, object parent)
        {
            var group = Invoke(controller, "CreateNewGroup", name, false);
            Invoke(controller, "AddChildToParent", group, parent);
            Invoke(controller, "AddGroupToCurrentView", group);
            return group;
        }

        private static void ExposeVolume(object controller, object group, string parameterName)
        {
            var guid = Invoke(group, "GetGUIDForVolume");
            var path = Activator.CreateInstance(ParameterPathType, group, guid);
            Invoke(controller, "AddExposedParameter", path);

            var property = ControllerType.GetProperty("exposedParameters", Flags);
            var parameters = (Array)property.GetValue(controller, null);
            var guidField = ExposedParameterType.GetField("guid", Flags);
            var nameField = ExposedParameterType.GetField("name", Flags);
            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters.GetValue(index);
                if (Equals(guidField.GetValue(parameter), guid))
                {
                    nameField.SetValue(parameter, parameterName);
                    parameters.SetValue(parameter, index);
                }
            }

            property.SetValue(controller, parameters, null);
        }

        private static object AddEffect(object controller, object group, string effectName)
        {
            var effect = Activator.CreateInstance(EffectType, effectName);
            Invoke(controller, "AddNewSubAsset", effect, false);
            Invoke(effect, "PreallocateGUIDs");
            var effects = (Array)GetProperty(group, "effects");
            Invoke(group, "InsertEffect", effect, effects.Length);
            return effect;
        }

        private static Object CloneSnapshot(object controller, string name)
        {
            Invoke(controller, "CloneNewSnapshotFromTarget", false);
            var snapshots = (Array)GetProperty(controller, "snapshots");
            var snapshot = (Object)snapshots.GetValue(snapshots.Length - 1);
            snapshot.name = name;
            return snapshot;
        }

        private static void SetVolume(object controller, object group, Object snapshot, float decibels)
        {
            Invoke(group, "SetValueForVolume", controller, snapshot, decibels);
        }

        private static void SetEffectParameter(
            object controller,
            object effect,
            Object snapshot,
            string parameter,
            float value)
        {
            Invoke(effect, "SetValueForParameter", controller, snapshot, parameter, value);
        }

        private static object Invoke(object target, string method, params object[] arguments)
        {
            var type = target != null ? target.GetType() : ControllerType;
            var candidates = new List<MethodInfo>();
            for (var current = type; current != null; current = current.BaseType)
            {
                foreach (var info in current.GetMethods(Flags | BindingFlags.DeclaredOnly))
                {
                    if (info.Name == method && info.GetParameters().Length == arguments.Length)
                    {
                        candidates.Add(info);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                throw new MissingMethodException(type.FullName, method);
            }

            try
            {
                return candidates[0].Invoke(target, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw new InvalidOperationException(
                    "AudioMixer authoring failed at " + method + ": " +
                    exception.InnerException.Message,
                    exception.InnerException);
            }
        }

        private static object GetProperty(object target, string name)
        {
            return target.GetType().GetProperty(name, Flags).GetValue(target, null);
        }

        private static void SetProperty(object target, string name, object value)
        {
            target.GetType().GetProperty(name, Flags).SetValue(target, value, null);
        }

        private static Type Find(string name)
        {
            return EditorAssembly.GetType(name) ??
                   throw new InvalidOperationException(
                       "Unity internal type not found: " + name +
                       ". Create the mixer by hand (see PROJECT_MEMORY.md, sound system).");
        }
    }
}
