using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum DisplayModeOption : byte { Windowed, Fullscreen, BorderlessFullscreen }

    public static class DisplayModeOptions
    {
        public const int Count = 3;
        public static DisplayModeOption Sanitize(DisplayModeOption value) =>
            (byte)value < Count ? value : DisplayModeOption.BorderlessFullscreen;
        public static DisplayModeOption Step(DisplayModeOption value, int direction) =>
            (DisplayModeOption)(((int)Sanitize(value) + Math.Sign(direction) + Count) % Count);
        public static string GetLabelSource(DisplayModeOption value)
        {
            switch (Sanitize(value))
            {
                case DisplayModeOption.Windowed: return "Windowed";
                case DisplayModeOption.Fullscreen: return "Fullscreen";
                default: return "Borderless Fullscreen";
            }
        }
        public static FullScreenMode ToFullScreenMode(DisplayModeOption value)
        {
            switch (Sanitize(value))
            {
                case DisplayModeOption.Windowed: return FullScreenMode.Windowed;
                case DisplayModeOption.Fullscreen: return FullScreenMode.ExclusiveFullScreen;
                default: return FullScreenMode.FullScreenWindow;
            }
        }
        public static DisplayModeOption FromFullScreenMode(FullScreenMode value)
        {
            switch (value)
            {
                case FullScreenMode.Windowed:
                case FullScreenMode.MaximizedWindow: return DisplayModeOption.Windowed;
                case FullScreenMode.ExclusiveFullScreen: return DisplayModeOption.Fullscreen;
                default: return DisplayModeOption.BorderlessFullscreen;
            }
        }
    }

    public enum ResolutionOption : byte
    {
        Hd720, HdPlus900, FullHd1080, Qhd1440, Uhd2160
    }

    public static class ResolutionOptions
    {
        private static readonly Vector2Int[] Sizes =
        {
            new Vector2Int(1280, 720),
            new Vector2Int(1600, 900),
            new Vector2Int(1920, 1080),
            new Vector2Int(2560, 1440),
            new Vector2Int(3840, 2160)
        };
        public const int Count = 5;
        public static ResolutionOption Sanitize(ResolutionOption value) =>
            (byte)value < Count ? value : ResolutionOption.FullHd1080;
        public static ResolutionOption Step(ResolutionOption value, int direction) =>
            (ResolutionOption)(((int)Sanitize(value) + Math.Sign(direction) + Count) % Count);
        public static Vector2Int GetSize(ResolutionOption value) => Sizes[(int)Sanitize(value)];
        public static string GetLabel(ResolutionOption value)
        {
            var size = GetSize(value);
            return $"{size.x} x {size.y}";
        }
        public static Vector2Int FitToDisplay(
            ResolutionOption value,
            int displayWidth,
            int displayHeight)
        {
            return GetSize(NormalizeForDisplay(
                value,
                displayWidth,
                displayHeight));
        }

        public static ResolutionOption NormalizeForDisplay(
            ResolutionOption value,
            int displayWidth,
            int displayHeight)
        {
            var requested = Sanitize(value);
            if (displayWidth <= 0 || displayHeight <= 0)
            {
                return requested;
            }

            for (var index = (int)requested; index >= 0; index--)
            {
                if (Sizes[index].x <= displayWidth && Sizes[index].y <= displayHeight)
                {
                    return (ResolutionOption)index;
                }
            }

            // 1280x720 is the minimum supported preset. Keeping the first
            // explicit option is preferable to reporting a resolution the
            // menu cannot represent or persist.
            return ResolutionOption.Hd720;
        }

        public static ResolutionOption NormalizeForDisplay(
            ResolutionOption value,
            DisplayModeOption displayMode,
            int displayWidth,
            int displayHeight,
            IReadOnlyList<Vector2Int> exclusiveFullscreenSizes)
        {
            var bounded = NormalizeForDisplay(
                value,
                displayWidth,
                displayHeight);
            if (DisplayModeOptions.Sanitize(displayMode) !=
                    DisplayModeOption.Fullscreen ||
                exclusiveFullscreenSizes == null ||
                exclusiveFullscreenSizes.Count == 0)
            {
                return bounded;
            }

            for (var index = (int)bounded; index >= 0; index--)
            {
                if (ContainsSize(exclusiveFullscreenSizes, Sizes[index]))
                {
                    return (ResolutionOption)index;
                }
            }

            for (var index = (int)bounded + 1; index < Sizes.Length; index++)
            {
                if ((displayWidth <= 0 || Sizes[index].x <= displayWidth) &&
                    (displayHeight <= 0 || Sizes[index].y <= displayHeight) &&
                    ContainsSize(exclusiveFullscreenSizes, Sizes[index]))
                {
                    return (ResolutionOption)index;
                }
            }

            // Windows normally exposes at least one supported preset. If a
            // driver reports none of the menu presets, preserve the bounded
            // choice instead of silently changing the user's display mode.
            return bounded;
        }

        private static bool ContainsSize(
            IReadOnlyList<Vector2Int> sizes,
            Vector2Int candidate)
        {
            for (var index = 0; index < sizes.Count; index++)
            {
                if (sizes[index] == candidate)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public enum QualityPresetOption : byte { Low, High }

    public static class QualityPresetOptions
    {
        public const int Count = 2;
        public static QualityPresetOption Sanitize(QualityPresetOption value) =>
            (byte)value < Count ? value : QualityPresetOption.High;
        public static QualityPresetOption Step(QualityPresetOption value, int direction) =>
            (QualityPresetOption)(((int)Sanitize(value) + Math.Sign(direction) + Count) % Count);
        public static string GetLabelSource(QualityPresetOption value) =>
            Sanitize(value) == QualityPresetOption.Low ? "Low" : "High";
    }

    public enum FrameRateCapOption : byte { Fps30, Fps60, Unlimited }

    public static class FrameRateCapOptions
    {
        public const int Count = 3;
        public static FrameRateCapOption Sanitize(FrameRateCapOption value) =>
            (byte)value < Count ? value : FrameRateCapOption.Fps60;
        public static FrameRateCapOption Step(FrameRateCapOption value, int direction) =>
            (FrameRateCapOption)(((int)Sanitize(value) + Math.Sign(direction) + Count) % Count);
        public static int ToTargetFrameRate(FrameRateCapOption value)
        {
            switch (Sanitize(value))
            {
                case FrameRateCapOption.Fps30: return 30;
                case FrameRateCapOption.Unlimited: return -1;
                default: return 60;
            }
        }
        public static string GetLabelSource(FrameRateCapOption value)
        {
            switch (Sanitize(value))
            {
                case FrameRateCapOption.Fps30: return "30 FPS";
                case FrameRateCapOption.Unlimited: return "Unlimited";
                default: return "60 FPS";
            }
        }
    }

    [Serializable]
    public struct GameSettingsData : IEquatable<GameSettingsData>
    {
        public const float MinimumMouseSensitivity = 0.25f;
        public const float MaximumMouseSensitivity = 2f;
        public const float DefaultMouseSensitivity = 1f;

        public float MasterVolume;
        public float SfxVolume;
        public float BgmVolume;
        public GameLanguage Language;
        public ResolutionOption Resolution;
        public DisplayModeOption DisplayMode;
        public QualityPresetOption QualityPreset;
        public FrameRateCapOption FrameRateCap;
        public float MouseSensitivity;
        public bool InvertY;
        public bool ReduceScreenShake;
        public bool ReduceFlashes;

        public GameSettingsData(
            float masterVolume, float sfxVolume, float bgmVolume,
            GameLanguage language, DisplayModeOption displayMode)
            : this(masterVolume, sfxVolume, bgmVolume, language,
                ResolutionOption.FullHd1080, displayMode,
                QualityPresetOption.High, FrameRateCapOption.Fps60,
                DefaultMouseSensitivity, false, false, false) { }

        public GameSettingsData(
            float masterVolume, float sfxVolume, float bgmVolume,
            GameLanguage language, ResolutionOption resolution,
            DisplayModeOption displayMode, QualityPresetOption qualityPreset,
            FrameRateCapOption frameRateCap, float mouseSensitivity,
            bool invertY, bool reduceScreenShake, bool reduceFlashes)
        {
            MasterVolume = masterVolume;
            SfxVolume = sfxVolume;
            BgmVolume = bgmVolume;
            Language = language;
            Resolution = resolution;
            DisplayMode = displayMode;
            QualityPreset = qualityPreset;
            FrameRateCap = frameRateCap;
            MouseSensitivity = mouseSensitivity;
            InvertY = invertY;
            ReduceScreenShake = reduceScreenShake;
            ReduceFlashes = reduceFlashes;
        }

        public static GameSettingsData Default => new GameSettingsData(
            GameAudio.DefaultVolume, GameAudio.DefaultVolume, GameAudio.DefaultVolume,
            GameLanguage.English, ResolutionOption.FullHd1080,
            DisplayModeOption.BorderlessFullscreen, QualityPresetOption.High,
            FrameRateCapOption.Fps60, DefaultMouseSensitivity, false, false, false);

        public GameSettingsData Sanitized()
        {
            var sensitivity = float.IsNaN(MouseSensitivity) ||
                              float.IsInfinity(MouseSensitivity)
                ? DefaultMouseSensitivity
                : Mathf.Clamp(MouseSensitivity, MinimumMouseSensitivity, MaximumMouseSensitivity);
            return new GameSettingsData(
                GameAudio.Sanitize(MasterVolume), GameAudio.Sanitize(SfxVolume),
                GameAudio.Sanitize(BgmVolume), GameLanguages.Sanitize(Language),
                ResolutionOptions.Sanitize(Resolution), DisplayModeOptions.Sanitize(DisplayMode),
                QualityPresetOptions.Sanitize(QualityPreset),
                FrameRateCapOptions.Sanitize(FrameRateCap), sensitivity, InvertY,
                ReduceScreenShake, ReduceFlashes);
        }

        public bool Equals(GameSettingsData other) =>
            Mathf.Approximately(MasterVolume, other.MasterVolume) &&
            Mathf.Approximately(SfxVolume, other.SfxVolume) &&
            Mathf.Approximately(BgmVolume, other.BgmVolume) &&
            Language == other.Language && Resolution == other.Resolution &&
            DisplayMode == other.DisplayMode && QualityPreset == other.QualityPreset &&
            FrameRateCap == other.FrameRateCap &&
            Mathf.Approximately(MouseSensitivity, other.MouseSensitivity) &&
            InvertY == other.InvertY &&
            ReduceScreenShake == other.ReduceScreenShake &&
            ReduceFlashes == other.ReduceFlashes;
        public override bool Equals(object obj) => obj is GameSettingsData other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = MasterVolume.GetHashCode();
                hash = (hash * 397) ^ SfxVolume.GetHashCode();
                hash = (hash * 397) ^ BgmVolume.GetHashCode();
                hash = (hash * 397) ^ (int)Language;
                hash = (hash * 397) ^ (int)Resolution;
                hash = (hash * 397) ^ (int)DisplayMode;
                hash = (hash * 397) ^ (int)QualityPreset;
                hash = (hash * 397) ^ (int)FrameRateCap;
                hash = (hash * 397) ^ MouseSensitivity.GetHashCode();
                hash = (hash * 397) ^ InvertY.GetHashCode();
                hash = (hash * 397) ^ ReduceScreenShake.GetHashCode();
                return (hash * 397) ^ ReduceFlashes.GetHashCode();
            }
        }
    }

    internal interface IGameSettingsStore
    {
        float GetFloat(string key, float defaultValue);
        int GetInt(string key, int defaultValue);
        void SetFloat(string key, float value);
        void SetInt(string key, int value);
        void Save();
    }

    internal sealed class PlayerPrefsGameSettingsStore : IGameSettingsStore
    {
        public float GetFloat(string key, float defaultValue) =>
            PlayerPrefs.GetFloat(key, defaultValue);
        public int GetInt(string key, int defaultValue) =>
            PlayerPrefs.GetInt(key, defaultValue);
        public void SetFloat(string key, float value) => PlayerPrefs.SetFloat(key, value);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void Save() => PlayerPrefs.Save();
    }

    internal readonly struct GameSettingsApplicationPlan
    {
        public readonly int Width;
        public readonly int Height;
        public readonly FullScreenMode FullScreenMode;
        public readonly int QualityLevel;
        public readonly int TargetFrameRate;
        public GameSettingsApplicationPlan(
            int width, int height, FullScreenMode fullScreenMode,
            int qualityLevel, int targetFrameRate)
        {
            Width = width;
            Height = height;
            FullScreenMode = fullScreenMode;
            QualityLevel = qualityLevel;
            TargetFrameRate = targetFrameRate;
        }
    }

    [Flags]
    internal enum GameSettingsPlatformChanges : byte
    {
        None = 0,
        Display = 1 << 0,
        Quality = 1 << 1,
        FrameRate = 1 << 2,
        All = Display | Quality | FrameRate
    }

    public static class GameSettings
    {
        internal const string KeyPrefix = "MazeParty.Settings.";
        internal const string MasterKey = KeyPrefix + "MasterVolume";
        internal const string SfxKey = KeyPrefix + "SfxVolume";
        internal const string BgmKey = KeyPrefix + "BgmVolume";
        internal const string LanguageKey = KeyPrefix + "Language";
        internal const string ResolutionKey = KeyPrefix + "Resolution";
        internal const string DisplayModeKey = KeyPrefix + "DisplayMode";
        internal const string QualityPresetKey = KeyPrefix + "QualityPreset";
        internal const string FrameRateCapKey = KeyPrefix + "FrameRateCap";
        internal const string MouseSensitivityKey = KeyPrefix + "MouseSensitivity";
        internal const string InvertYKey = KeyPrefix + "InvertY";
        internal const string ReduceScreenShakeKey = KeyPrefix + "ReduceScreenShake";
        internal const string ReduceFlashesKey = KeyPrefix + "ReduceFlashes";

        private static readonly IGameSettingsStore PlayerPrefsStore =
            new PlayerPrefsGameSettingsStore();
        private static bool _initialized;
        private static GameSettingsData _applied = GameSettingsData.Default;

        public static GameSettingsData Applied
        {
            get { EnsureInitialized(); return _applied; }
        }
        public static event Action AppliedChanged;
        public static GameSettingsData Load() => Load(PlayerPrefsStore);
        internal static GameSettingsData Load(IGameSettingsStore store)
        {
            var defaults = GameSettingsData.Default;
            return new GameSettingsData(
                store.GetFloat(MasterKey, defaults.MasterVolume),
                store.GetFloat(SfxKey, defaults.SfxVolume),
                store.GetFloat(BgmKey, defaults.BgmVolume),
                (GameLanguage)ReadBoundedOption(
                    store, LanguageKey, (int)defaults.Language,
                    GameLanguages.Count),
                (ResolutionOption)ReadBoundedOption(
                    store, ResolutionKey, (int)defaults.Resolution,
                    ResolutionOptions.Count),
                (DisplayModeOption)ReadBoundedOption(
                    store, DisplayModeKey, (int)defaults.DisplayMode,
                    DisplayModeOptions.Count),
                (QualityPresetOption)ReadBoundedOption(
                    store, QualityPresetKey, (int)defaults.QualityPreset,
                    QualityPresetOptions.Count),
                (FrameRateCapOption)ReadBoundedOption(
                    store, FrameRateCapKey, (int)defaults.FrameRateCap,
                    FrameRateCapOptions.Count),
                store.GetFloat(MouseSensitivityKey, defaults.MouseSensitivity),
                store.GetInt(InvertYKey, defaults.InvertY ? 1 : 0) != 0,
                store.GetInt(ReduceScreenShakeKey, defaults.ReduceScreenShake ? 1 : 0) != 0,
                store.GetInt(ReduceFlashesKey, defaults.ReduceFlashes ? 1 : 0) != 0)
                .Sanitized();
        }

        public static void Save(GameSettingsData data) => Save(PlayerPrefsStore, data);
        internal static void Save(IGameSettingsStore store, GameSettingsData data)
        {
            data = data.Sanitized();
            store.SetFloat(MasterKey, data.MasterVolume);
            store.SetFloat(SfxKey, data.SfxVolume);
            store.SetFloat(BgmKey, data.BgmVolume);
            store.SetInt(LanguageKey, (int)data.Language);
            store.SetInt(ResolutionKey, (int)data.Resolution);
            store.SetInt(DisplayModeKey, (int)data.DisplayMode);
            store.SetInt(QualityPresetKey, (int)data.QualityPreset);
            store.SetInt(FrameRateCapKey, (int)data.FrameRateCap);
            store.SetFloat(MouseSensitivityKey, data.MouseSensitivity);
            store.SetInt(InvertYKey, data.InvertY ? 1 : 0);
            store.SetInt(ReduceScreenShakeKey, data.ReduceScreenShake ? 1 : 0);
            store.SetInt(ReduceFlashesKey, data.ReduceFlashes ? 1 : 0);
            store.Save();
        }

        public static void Apply(GameSettingsData data)
        {
            EnsureInitialized();
            var displaySize = GetCurrentDisplaySize();
            var exclusiveFullscreenSizes =
                GetCurrentExclusiveFullscreenSizes();
            var next = NormalizeForDisplay(
                data,
                displaySize.x,
                displaySize.y,
                exclusiveFullscreenSizes);
            var platformChanges = GetPlatformChanges(_applied, next);
            _applied = next;
            ApplyNonPlatformSettings(_applied);
            ApplyPlatformSettings(
                _applied,
                platformChanges,
                displaySize.x,
                displaySize.y,
                exclusiveFullscreenSizes);
            Save(_applied);
            AppliedChanged?.Invoke();
        }

        public static void PreviewVolumes(float master, float sfx, float bgm)
        {
            EnsureInitialized();
            GameAudio.SetVolumes(master, sfx, bgm);
        }
        public static void RevertPreview()
        {
            EnsureInitialized();
            GameAudio.SetVolumes(_applied.MasterVolume, _applied.SfxVolume, _applied.BgmVolume);
        }

        internal static GameSettingsApplicationPlan CreateApplicationPlan(
            GameSettingsData data,
            int displayWidth,
            int displayHeight,
            IReadOnlyList<Vector2Int> exclusiveFullscreenSizes = null)
        {
            data = NormalizeForDisplay(
                data,
                displayWidth,
                displayHeight,
                exclusiveFullscreenSizes);
            var size = ResolutionOptions.GetSize(data.Resolution);
            return new GameSettingsApplicationPlan(
                size.x, size.y, DisplayModeOptions.ToFullScreenMode(data.DisplayMode),
                (int)data.QualityPreset,
                FrameRateCapOptions.ToTargetFrameRate(data.FrameRateCap));
        }

        internal static GameSettingsData NormalizeForDisplay(
            GameSettingsData data,
            int displayWidth,
            int displayHeight,
            IReadOnlyList<Vector2Int> exclusiveFullscreenSizes = null)
        {
            data = data.Sanitized();
            data.Resolution = ResolutionOptions.NormalizeForDisplay(
                data.Resolution,
                data.DisplayMode,
                displayWidth,
                displayHeight,
                exclusiveFullscreenSizes);
            return data;
        }

        internal static GameSettingsPlatformChanges GetPlatformChanges(
            GameSettingsData previous,
            GameSettingsData next)
        {
            previous = previous.Sanitized();
            next = next.Sanitized();
            var changes = GameSettingsPlatformChanges.None;
            if (previous.Resolution != next.Resolution ||
                previous.DisplayMode != next.DisplayMode)
            {
                changes |= GameSettingsPlatformChanges.Display;
            }
            if (previous.QualityPreset != next.QualityPreset)
            {
                changes |= GameSettingsPlatformChanges.Quality;
            }
            if (previous.FrameRateCap != next.FrameRateCap)
            {
                changes |= GameSettingsPlatformChanges.FrameRate;
            }

            return changes;
        }

        private static void ApplyPlatformSettings(
            GameSettingsData data,
            GameSettingsPlatformChanges changes,
            int displayWidth,
            int displayHeight,
            IReadOnlyList<Vector2Int> exclusiveFullscreenSizes)
        {
            if (changes == GameSettingsPlatformChanges.None)
            {
                return;
            }

            var plan = CreateApplicationPlan(
                data,
                displayWidth,
                displayHeight,
                exclusiveFullscreenSizes);
            if ((changes & GameSettingsPlatformChanges.Quality) != 0)
            {
                QualitySettings.SetQualityLevel(plan.QualityLevel, true);
            }
            if ((changes & GameSettingsPlatformChanges.FrameRate) != 0)
            {
                BuildFrameRateLimiter.Apply(plan.TargetFrameRate);
            }
            if ((changes & GameSettingsPlatformChanges.Display) != 0 &&
                !Application.isEditor)
            {
                Screen.SetResolution(plan.Width, plan.Height, plan.FullScreenMode);
            }
        }

        private static void ApplyNonPlatformSettings(GameSettingsData data)
        {
            GameAudio.SetVolumes(data.MasterVolume, data.SfxVolume, data.BgmVolume);
            GameText.SetLanguage(data.Language);
            PresentationAccessibility.Apply(data.ReduceScreenShake, data.ReduceFlashes);
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            var loaded = Load();
            var displaySize = GetCurrentDisplaySize();
            var exclusiveFullscreenSizes =
                GetCurrentExclusiveFullscreenSizes();
            _applied = NormalizeForDisplay(
                loaded,
                displaySize.x,
                displaySize.y,
                exclusiveFullscreenSizes);
            ApplyNonPlatformSettings(_applied);
            ApplyPlatformSettings(
                _applied,
                GameSettingsPlatformChanges.All,
                displaySize.x,
                displaySize.y,
                exclusiveFullscreenSizes);
            if (!_applied.Equals(loaded))
            {
                Save(_applied);
            }
        }

        private static int ReadBoundedOption(
            IGameSettingsStore store,
            string key,
            int defaultValue,
            int count)
        {
            var raw = store.GetInt(key, defaultValue);
            return raw >= 0 && raw < count ? raw : defaultValue;
        }

        private static Vector2Int GetCurrentDisplaySize()
        {
            var display = Screen.mainWindowDisplayInfo;
            return new Vector2Int(
                display.width > 0
                    ? display.width
                    : Screen.currentResolution.width,
                display.height > 0
                    ? display.height
                    : Screen.currentResolution.height);
        }

        private static Vector2Int[] GetCurrentExclusiveFullscreenSizes()
        {
            var resolutions = Screen.resolutions;
            var sizes = new Vector2Int[resolutions.Length];
            for (var index = 0; index < resolutions.Length; index++)
            {
                sizes[index] = new Vector2Int(
                    resolutions[index].width,
                    resolutions[index].height);
            }

            return sizes;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeOnStartup()
        {
            _initialized = false;
            AppliedChanged = null;
            EnsureInitialized();
        }
    }
}
