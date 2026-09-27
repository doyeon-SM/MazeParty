using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace MazeParty.Multiplayer.Editor
{
    /// <summary>
    /// Reproducible Windows MVP builds. Each entry point restores the editor's
    /// scripting backend after the build, so validating release never leaves a
    /// developer checkout switched to IL2CPP.
    /// </summary>
    public static class WindowsBuildUtility
    {
        public const string DevelopmentOutput =
            "Builds/Windows-Development/MazeParty.exe";
        public const string ReleaseOutput =
            "Builds/Windows-Release/MazeParty.exe";

        [MenuItem("MazeParty/Build/Windows Development (Mono x64)")]
        public static void BuildDevelopmentMono()
        {
            Build(
                ScriptingImplementation.Mono2x,
                BuildOptions.Development,
                DevelopmentOutput);
        }

        [MenuItem("MazeParty/Build/Windows Release (IL2CPP x64)")]
        public static void BuildReleaseIl2Cpp()
        {
            Build(
                ScriptingImplementation.IL2CPP,
                BuildOptions.None,
                ReleaseOutput);
        }

        private static void Build(
            ScriptingImplementation backend,
            BuildOptions options,
            string outputPath)
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException(
                    "No enabled scenes are configured in Build Settings.");
            }

            var namedTarget = NamedBuildTarget.Standalone;
            var previousBackend =
                PlayerSettings.GetScriptingBackend(namedTarget);
            try
            {
                PlayerSettings.SetScriptingBackend(namedTarget, backend);
                var absoluteOutput = Path.GetFullPath(outputPath);
                var outputDirectory = Path.GetDirectoryName(absoluteOutput);
                if (string.IsNullOrEmpty(outputDirectory))
                {
                    throw new InvalidOperationException(
                        "Windows build output directory could not be resolved.");
                }

                // Never validate against stale files from another backend.
                // These two menu commands own their dedicated output folders.
                if (Directory.Exists(outputDirectory))
                {
                    Directory.Delete(outputDirectory, true);
                }
                Directory.CreateDirectory(outputDirectory);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = absoluteOutput,
                    target = BuildTarget.StandaloneWindows64,
                    options = options
                });
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Windows build failed: {report.summary.result} " +
                        $"({report.summary.totalErrors} errors).");
                }

                ValidateBackendArtifacts(backend, absoluteOutput);
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(namedTarget, previousBackend);
            }
        }

        private static void ValidateBackendArtifacts(
            ScriptingImplementation backend,
            string executablePath)
        {
            var outputDirectory = Path.GetDirectoryName(executablePath);
            var executableName = Path.GetFileNameWithoutExtension(executablePath);
            var dataDirectory = Path.Combine(
                outputDirectory,
                executableName + "_Data");
            var monoRuntimeDirectory = Path.Combine(
                outputDirectory,
                "MonoBleedingEdge");
            var managedDirectory = Path.Combine(dataDirectory, "Managed");
            var gameAssembly = Path.Combine(outputDirectory, "GameAssembly.dll");

            var valid = backend == ScriptingImplementation.Mono2x
                ? Directory.Exists(monoRuntimeDirectory) &&
                  Directory.Exists(managedDirectory) &&
                  !File.Exists(gameAssembly)
                : backend == ScriptingImplementation.IL2CPP &&
                  File.Exists(gameAssembly) &&
                  !Directory.Exists(monoRuntimeDirectory);
            if (!valid)
            {
                throw new InvalidOperationException(
                    $"Windows build output does not match the requested " +
                    $"scripting backend ({backend}).");
            }
        }
    }
}
