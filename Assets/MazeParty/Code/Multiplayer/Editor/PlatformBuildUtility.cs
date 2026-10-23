using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MazeParty.Multiplayer.Editor
{
    // Local settings are not imported. Only whitelisted client fields are written to the output.
    public sealed class PlatformBuildUtility : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const string SdkDirectory = "Assets/Stove&Steam/STOVE/x86_64";
        private const string Profiles = "Assets/Settings/Build Profiles/";
        private static bool _validationBuild;
        public int callbackOrder => 1000;

        [MenuItem("MazeParty/Platform/Set up profiles and local settings")]
        public static void Setup()
        {
            EnsureProfile("STOVE", "MAZEPARTY_STOVE");
            EnsureProfile("Steam", "MAZEPARTY_STEAM");
            Directory.CreateDirectory(PlatformContext.LocalSettingsDirectory);
            foreach (var channel in new[] { "stove", "steam" })
            {
                var file = Path.Combine(PlatformContext.LocalSettingsDirectory, channel + ".json");
                if (!File.Exists(file)) File.WriteAllText(file, new PlatformLaunchSettings().ToClientJson(channel));
            }
            ConfigureSdkImporters();
            AssetDatabase.SaveAssets();
        }

        private static void EnsureProfile(string name, string symbol)
        {
            var asset = Profiles + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<BuildProfile>(asset) == null &&
                !AssetDatabase.CopyAsset(Profiles + "Windows.asset", asset))
                throw new InvalidOperationException("The Windows build profile template is missing.");
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(asset);
            profile.scriptingDefines = new[] { symbol };
            // Unity 6.6 serializes the override switch separately from the array.
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("m_HasScriptingDefines").boolValue = true;
            serialized.FindProperty("m_Subtarget").intValue = (int)StandaloneBuildSubtarget.Player;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }

        public static void ConfigureSdkImporters()
        {
            foreach (var file in new[] { "BaseSDK.dll", "BaseSDK_NET.dll" })
            {
                var importer = AssetImporter.GetAtPath(SdkDirectory + "/" + file) as PluginImporter;
                if (importer == null) throw new InvalidOperationException("Import the STOVE 3.5.3 x64 SDK first. See Docs/STOVE_SETUP.md.");
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(true);
                importer.SetEditorData("OS", "Windows");
                importer.SetEditorData("CPU", "x86_64");
                importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
                importer.DefineConstraints = new[] { "MAZEPARTY_STOVE" };
                importer.SaveAndReimport();
            }
        }

        [MenuItem("MazeParty/Platform/Select STOVE")]
        public static void SelectStove() => Select("STOVE");
        [MenuItem("MazeParty/Platform/Select Steam")]
        public static void SelectSteam() => Select("Steam");
        [MenuItem("MazeParty/Platform/Select Development")]
        public static void SelectDevelopment() => Select("Windows");
        private static void Select(string name)
        {
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(Profiles + name + ".asset");
            if (profile == null) throw new InvalidOperationException("Run platform setup first.");
            BuildProfile.SetActiveBuildProfile(profile);
            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
        }

        [MenuItem("MazeParty/Platform/Open local settings folder")]
        public static void OpenSettings() => EditorUtility.RevealInFinder(Path.GetFullPath(PlatformContext.LocalSettingsDirectory));

        [MenuItem("MazeParty/Build/Selected Store - Release (IL2CPP x64)")]
        public static void BuildRelease() => Build(false);
        [MenuItem("MazeParty/Build/Selected Store - Integration Check (IL2CPP x64)")]
        public static void BuildIntegrationCheck() => Build(true);

        private static void Build(bool validation)
        {
            if (!PlatformContext.IsStoreBuild) throw new BuildFailedException("Select STOVE or Steam and wait for compilation first.");
            ValidateCompiledChannel();
            var previousBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            _validationBuild = validation;
            try
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
                var output = "Builds/" + PlatformContext.Channel + (validation ? "-integration-" : "-release-") +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss") + "/MazeParty.exe";
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                    target = BuildTarget.StandaloneWindows64, subtarget = (int)StandaloneBuildSubtarget.Player,
                    locationPathName = output, options = validation ? BuildOptions.Development : BuildOptions.None
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("Store build failed: " + report.summary.result);
                File.WriteAllText("Library/MazePartyPlatformBuild.txt", "Succeeded\n" + PlatformContext.Channel + "\n" + Path.GetFullPath(output));
                Debug.Log("Platform build succeeded: " + Path.GetFullPath(output));
            }
            catch (Exception exception)
            {
                File.WriteAllText("Library/MazePartyPlatformBuild.txt", "Failed\n" + PlatformContext.Channel + "\n" + exception.GetType().Name);
                throw;
            }
            finally
            {
                _validationBuild = false;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, previousBackend);
                AssetDatabase.SaveAssets();
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            ValidateCompiledChannel();
            foreach (var importer in PluginImporter.GetAllImporters().Where(plugin =>
                         plugin.assetPath.StartsWith(SdkDirectory + "/", StringComparison.Ordinal)))
                importer.SetIncludeInBuildDelegate(_ => PlatformContext.Channel == "stove");
            foreach (var importer in PluginImporter.GetAllImporters().Where(plugin =>
                         plugin.assetPath.StartsWith("Packages/com.rlabrecque.steamworks.net/Plugins/", StringComparison.Ordinal)))
                importer.SetIncludeInBuildDelegate(_ => PlatformContext.Channel == "steam");
            if (!PlatformContext.IsStoreBuild) return;
            if (report.summary.platform != BuildTarget.StandaloneWindows64 ||
                PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.IL2CPP)
                throw new BuildFailedException("Store builds require Windows x64 / IL2CPP.");
            if (!_validationBuild) ReadLocalSettings().Validate(PlatformContext.Channel);
            if (PlatformContext.Channel == "stove")
            {
                foreach (var file in new[] { "BaseSDK.dll", "BaseSDK_NET.dll" })
                {
                    var source = SdkDirectory + "/" + file;
                    if (!File.Exists(source))
                        throw new BuildFailedException("STOVE SDK 3.5.3.0 is required: " + source);
                    var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(source);
                    // Native SDK includes a git suffix in its display version.
                    if (version.FileMajorPart != 3 || version.FileMinorPart != 5 ||
                        version.FileBuildPart != 3 || version.FilePrivatePart != 0)
                        throw new BuildFailedException("STOVE SDK 3.5.3.0 is required: " + source);
                }
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            var root = Path.GetDirectoryName(report.summary.outputPath);
            var data = Path.Combine(root, Path.GetFileNameWithoutExtension(report.summary.outputPath) + "_Data");
            var steamFiles = Directory.GetFiles(data, "steam_api*.dll", SearchOption.AllDirectories);
            if (PlatformContext.Channel != "steam" && steamFiles.Length != 0)
                throw new BuildFailedException("Steam binaries were unexpectedly included in another platform build.");
            if (PlatformContext.Channel == "steam" && !steamFiles.Any(file => Path.GetFileName(file) == "steam_api64.dll"))
                throw new BuildFailedException("The Steam x64 native SDK is missing from the output.");
            if (File.Exists(Path.Combine(root, "steam_appid.txt")))
                throw new BuildFailedException("Do not ship steam_appid.txt. Use a fresh output directory.");
            if (!PlatformContext.IsStoreBuild) return;
            var sdkFiles = Directory.GetFiles(data, "BaseSDK*.dll", SearchOption.AllDirectories);
            if (PlatformContext.Channel == "steam" && sdkFiles.Length != 0)
                throw new BuildFailedException("STOVE binaries were unexpectedly included in a Steam build.");
            if (PlatformContext.Channel == "stove" && !sdkFiles.Any(file => Path.GetFileName(file) == "BaseSDK.dll"))
                throw new BuildFailedException("The STOVE native SDK is missing from the output.");
            if (_validationBuild)
            {
                File.WriteAllText(Path.Combine(root, "INTEGRATION-CHECK-ONLY.txt"),
                    "Compilation/package check only. Platform configuration is intentionally absent; online login is disabled. Not for upload or release.");
                return;
            }
            var streaming = Path.Combine(data, "StreamingAssets");
            Directory.CreateDirectory(streaming);
            File.WriteAllText(Path.Combine(streaming, PlatformContext.SettingsFileName), ReadLocalSettings().ToClientJson(PlatformContext.Channel));
        }

        private static PlatformLaunchSettings ReadLocalSettings()
        {
            var path = Path.Combine(PlatformContext.LocalSettingsDirectory, PlatformContext.Channel + ".json");
            if (!File.Exists(path)) throw new BuildFailedException("Configure " + path + " first. See Docs/STOVE_SETUP.md.");
            var settings = JsonUtility.FromJson<PlatformLaunchSettings>(File.ReadAllText(path));
            if (settings == null) throw new BuildFailedException("Invalid platform configuration.");
            return settings;
        }

        private static void ValidateCompiledChannel()
        {
            var profile = BuildProfile.GetActiveBuildProfile();
            var symbols = profile == null ? Array.Empty<string>() : profile.scriptingDefines;
            var expected = symbols.Contains("MAZEPARTY_STOVE") ? "stove" :
                symbols.Contains("MAZEPARTY_STEAM") ? "steam" : "development";
            if (expected != PlatformContext.Channel)
                throw new BuildFailedException("Platform selection is still compiling. Wait for compilation before building.");
        }
    }
}
