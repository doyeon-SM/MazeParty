using System;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>Screen modes offered by the settings menu, in selector order.</summary>
    public enum DisplayModeOption : byte
    {
        Windowed = 0,
        Fullscreen = 1,
        BorderlessFullscreen = 2
    }

    public static class DisplayModeOptions
    {
        public const int Count = 3;

        public static DisplayModeOption Sanitize(DisplayModeOption option)
        {
            return (byte)option < Count ? option : DisplayModeOption.BorderlessFullscreen;
        }

        /// <summary>Left/right selector step with wrap-around.</summary>
        public static DisplayModeOption Step(DisplayModeOption option, int direction)
        {
            var index = (int)Sanitize(option) + Math.Sign(direction);
            index = ((index % Count) + Count) % Count;
            return (DisplayModeOption)index;
        }

        /// <summary>English source text (localized by the view).</summary>
        public static string GetLabelSource(DisplayModeOption option)
        {
            switch (Sanitize(option))
            {
                case DisplayModeOption.Windowed:
                    return "Windowed";
                case DisplayModeOption.Fullscreen:
                    return "Fullscreen";
                default:
                    return "Borderless Fullscreen";
            }
        }

        public static FullScreenMode ToFullScreenMode(DisplayModeOption option)
        {
            switch (Sanitize(option))
            {
                case DisplayModeOption.Windowed:
                    return FullScreenMode.Windowed;
                case DisplayModeOption.Fullscreen:
                    return FullScreenMode.ExclusiveFullScreen;
                default:
                    return FullScreenMode.FullScreenWindow;
            }
        }

        public static DisplayModeOption FromFullScreenMode(FullScreenMode mode)
        {
            switch (mode)
            {
                case FullScreenMode.Windowed:
                case FullScreenMode.MaximizedWindow:
                    return DisplayModeOption.Windowed;
                case FullScreenMode.ExclusiveFullScreen:
                    return DisplayModeOption.Fullscreen;
                default:
                    return DisplayModeOption.BorderlessFullscreen;
            }
        }
    }

    [Serializable]
    public struct GameSettingsData : IEquatable<GameSettingsData>
    {
        public float MasterVolume;
        public float SfxVolume;
        public float BgmVolume;
        public GameLanguage Language;
        public DisplayModeOption DisplayMode;

        public GameSettingsData(
            float masterVolume,
            float sfxVolume,
            float bgmVolume,
            GameLanguage language,
            DisplayModeOption displayMode)
        {
            MasterVolume = masterVolume;
            SfxVolume = sfxVolume;
            BgmVolume = bgmVolume;
            Language = language;
            DisplayMode = displayMode;
        }

        /// <summary>
        /// First-run defaults. English stays the default language until a
        /// CJK-capable font is installed.
        /// </summary>
        public static GameSettingsData Default => new GameSettingsData(
            GameAudio.DefaultVolume,
            GameAudio.DefaultVolume,
            GameAudio.DefaultVolume,
            GameLanguage.English,
            DisplayModeOption.BorderlessFullscreen);

        public GameSettingsData Sanitized()
        {
            return new GameSettingsData(
                GameAudio.Sanitize(MasterVolume),
                GameAudio.Sanitize(SfxVolume),
                GameAudio.Sanitize(BgmVolume),
                GameLanguages.Sanitize(Language),
                DisplayModeOptions.Sanitize(DisplayMode));
        }

        public bool Equals(GameSettingsData other)
        {
            return Mathf.Approximately(MasterVolume, other.MasterVolume) &&
                   Mathf.Approximately(SfxVolume, other.SfxVolume) &&
                   Mathf.Approximately(BgmVolume, other.BgmVolume) &&
                   Language == other.Language &&
                   DisplayMode == other.DisplayMode;
        }

        public override bool Equals(object obj)
        {
            return obj is GameSettingsData other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = MasterVolume.GetHashCode();
                hash = (hash * 397) ^ SfxVolume.GetHashCode();
                hash = (hash * 397) ^ BgmVolume.GetHashCode();
                hash = (hash * 397) ^ (int)Language;
                return (hash * 397) ^ (int)DisplayMode;
            }
        }
    }

    /// <summary>
    /// Loads, applies and saves local settings (PlayerPrefs, per PC).
    /// Volumes and language are applied at startup; the screen mode keeps the
    /// mode Unity restored for the window and changes only when applied.
    /// </summary>
    public static class GameSettings
    {
        private const string KeyPrefix = "MazeParty.Settings.";
        private const string MasterKey = KeyPrefix + "MasterVolume";
        private const string SfxKey = KeyPrefix + "SfxVolume";
        private const string BgmKey = KeyPrefix + "BgmVolume";
        private const string LanguageKey = KeyPrefix + "Language";
        private const string DisplayModeKey = KeyPrefix + "DisplayMode";
        private const float WindowedDisplayFraction = 0.75f;

        private static bool _initialized;
        private static GameSettingsData _applied = GameSettingsData.Default;

        /// <summary>The last applied (and saved) settings.</summary>
        public static GameSettingsData Applied
        {
            get
            {
                EnsureInitialized();
                return _applied;
            }
        }

        public static event Action AppliedChanged;

        public static GameSettingsData Load()
        {
            var defaults = GameSettingsData.Default;
            var data = new GameSettingsData(
                PlayerPrefs.GetFloat(MasterKey, defaults.MasterVolume),
                PlayerPrefs.GetFloat(SfxKey, defaults.SfxVolume),
                PlayerPrefs.GetFloat(BgmKey, defaults.BgmVolume),
                (GameLanguage)PlayerPrefs.GetInt(LanguageKey, (int)defaults.Language),
                DisplayModeOptions.FromFullScreenMode(Screen.fullScreenMode));
            return data.Sanitized();
        }

        public static void Save(GameSettingsData data)
        {
            data = data.Sanitized();
            PlayerPrefs.SetFloat(MasterKey, data.MasterVolume);
            PlayerPrefs.SetFloat(SfxKey, data.SfxVolume);
            PlayerPrefs.SetFloat(BgmKey, data.BgmVolume);
            PlayerPrefs.SetInt(LanguageKey, (int)data.Language);
            PlayerPrefs.SetInt(DisplayModeKey, (int)data.DisplayMode);
            PlayerPrefs.Save();
        }

        /// <summary>Applies every setting and saves it.</summary>
        public static void Apply(GameSettingsData data)
        {
            EnsureInitialized();
            data = data.Sanitized();
            var displayChanged = data.DisplayMode != _applied.DisplayMode;
            _applied = data;
            GameAudio.SetVolumes(data.MasterVolume, data.SfxVolume, data.BgmVolume);
            GameText.SetLanguage(data.Language);
            if (displayChanged)
            {
                ApplyDisplayMode(data.DisplayMode);
            }

            Save(data);
            AppliedChanged?.Invoke();
        }

        /// <summary>Live volume preview while a slider moves; not saved.</summary>
        public static void PreviewVolumes(float master, float sfx, float bgm)
        {
            EnsureInitialized();
            GameAudio.SetVolumes(master, sfx, bgm);
        }

        /// <summary>Restores the applied volumes after an unapplied preview.</summary>
        public static void RevertPreview()
        {
            EnsureInitialized();
            GameAudio.SetVolumes(
                _applied.MasterVolume,
                _applied.SfxVolume,
                _applied.BgmVolume);
        }

        private static void ApplyDisplayMode(DisplayModeOption option)
        {
            if (Application.isEditor)
            {
                return;
            }

            var mode = DisplayModeOptions.ToFullScreenMode(option);
            var display = Screen.mainWindowDisplayInfo;
            var displayWidth = display.width > 0 ? display.width : Screen.currentResolution.width;
            var displayHeight = display.height > 0 ? display.height : Screen.currentResolution.height;
            if (mode == FullScreenMode.Windowed)
            {
                var width = Screen.width;
                var height = Screen.height;
                if (Screen.fullScreenMode != FullScreenMode.Windowed)
                {
                    width = Mathf.Max(640, Mathf.RoundToInt(displayWidth * WindowedDisplayFraction));
                    height = Mathf.Max(360, Mathf.RoundToInt(displayHeight * WindowedDisplayFraction));
                }

                Screen.SetResolution(width, height, FullScreenMode.Windowed);
                return;
            }

            Screen.SetResolution(displayWidth, displayHeight, mode);
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _applied = Load();
            GameAudio.SetVolumes(_applied.MasterVolume, _applied.SfxVolume, _applied.BgmVolume);
            GameText.SetLanguage(_applied.Language);
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
