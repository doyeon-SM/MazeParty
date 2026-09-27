using System;
using System.IO;
using System.Linq;
using System.Threading;
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
        private const int OutputCleanupAttemptCount = 5;
        private const int OutputCleanupRetryDelayMilliseconds = 250;

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
            var backendChanged = previousBackend != backend;
            try
            {
                if (backendChanged)
                {
                    PlayerSettings.SetScriptingBackend(namedTarget, backend);
                }
                var absoluteOutput = Path.GetFullPath(outputPath);
                var outputDirectory = Path.GetDirectoryName(absoluteOutput);
                if (string.IsNullOrEmpty(outputDirectory))
                {
                    throw new InvalidOperationException(
                        "Windows build output directory could not be resolved.");
                }

                // Never validate against stale files from another backend.
                // These two menu commands own their dedicated output folders.
                ResetOutputDirectory(outputDirectory);
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
                if (backendChanged)
                {
                    PlayerSettings.SetScriptingBackend(
                        namedTarget,
                        previousBackend);
                }
            }
        }

        private static void ResetOutputDirectory(string outputDirectory)
        {
            Exception lastException = null;
            for (var attempt = 0;
                 attempt < OutputCleanupAttemptCount &&
                 Directory.Exists(outputDirectory);
                 attempt++)
            {
                try
                {
                    ClearReadOnlyFileAttributes(outputDirectory);
                    Directory.Delete(outputDirectory, true);
                    lastException = null;
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException)
                {
                    lastException = exception;
                    if (attempt + 1 < OutputCleanupAttemptCount)
                    {
                        Thread.Sleep(
                            OutputCleanupRetryDelayMilliseconds *
                            (attempt + 1));
                    }
                }
            }

            if (Directory.Exists(outputDirectory))
            {
                throw new IOException(
                    $"Could not clean the Windows build output after " +
                    $"{OutputCleanupAttemptCount} attempts: {outputDirectory}",
                    lastException);
            }

            Directory.CreateDirectory(outputDirectory);
        }

        private static void ClearReadOnlyFileAttributes(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(
                         directory,
                         "*",
                         SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(
                        file,
                        attributes & ~FileAttributes.ReadOnly);
                }
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
