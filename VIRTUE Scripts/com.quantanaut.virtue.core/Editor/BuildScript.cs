using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VirtueCore.Editor
{
    // Command-line build entry point, shared by every Standalone VIRTUE
    // edition (Desktop, VR, Lite -- Mobile's Android build needs its own
    // signing-aware script). Select the platform via Unity's own
    // -buildTarget flag (applied before -executeMethod runs) rather than
    // switching it from inside the script, since SwitchActiveBuildTarget
    // in batch mode can trigger an unreliable platform reimport.
    //
    //   Unity.exe -batchmode -nographics -quit -buildTarget Win64 ^
    //     -projectPath "<project>" -executeMethod VirtueCore.Editor.BuildScript.Build ^
    //     -logFile "<log>"
    //
    // -buildTarget accepts: Win64, Win32, OSXUniversal, Linux64 (VR and
    // Lite only ship Win64; Win32 is Desktop-only). Every enabled scene
    // from Build Settings is included -- nothing here is project-specific.
    public static class BuildScript
    {
        // Builds land directly in the same folder the Steam/Zenodo upload
        // pipeline already reads from (SteamBuildScripts/*.vdf reference
        // these exact folder names via a relative LocalPath), instead of
        // inside each project's own folder -- keeps the project directories
        // clean and means the upload scripts never need to change.
        private static readonly string BuildsRoot = @"C:\Users\sbpre\Documents\VIRTUE Unity Builds";

        public static void Build()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string productName = PlayerSettings.productName;

            string folderName = ResolveOutputFolderName(productName, target);
            if (folderName == null)
            {
                Debug.LogError($"BuildScript.Build has no known output folder for product " +
                                $"'{productName}' with target {target}. Add a case to " +
                                "ResolveOutputFolderName, or pass a supported -buildTarget " +
                                "(Win64, OSXUniversal, Linux64).");
                EditorApplication.Exit(1);
                return;
            }

            string outputPath;
            if (target == BuildTarget.StandaloneOSX)
            {
                // The .app bundle IS the top-level folder here, matching the
                // existing "VIRTUE <Edition> macOS.app" layout -- no separate
                // executable file nested inside it to name.
                outputPath = Path.Combine(BuildsRoot, folderName);
                Directory.CreateDirectory(BuildsRoot);
            }
            else
            {
                outputPath = Path.Combine(BuildsRoot, folderName, ExecutableName(productName, folderName, target));
                Directory.CreateDirectory(Path.Combine(BuildsRoot, folderName));
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"Build for {target}: {summary.result}, {summary.totalSize} bytes, " +
                      $"{summary.totalErrors} errors, {summary.totalWarnings} warnings, output: {outputPath}");

            if (summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        // Matches the existing folder names already used under
        // "VIRTUE Unity Builds". Deliberately not derived from a uniform
        // formula -- Lite's Windows folder has no bit-suffix, VR's has no
        // platform suffix at all -- since the goal is for builds to land
        // exactly where the Steam/Zenodo pipeline already expects them, not
        // to impose a new naming scheme.
        private static string ResolveOutputFolderName(string productName, BuildTarget target)
        {
            switch (productName)
            {
                case "VIRTUE Desktop":
                    switch (target)
                    {
                        case BuildTarget.StandaloneWindows64: return "VIRTUE Desktop Windows 64-bit";
                        case BuildTarget.StandaloneWindows: return "VIRTUE Desktop Windows 32-bit";
                        case BuildTarget.StandaloneOSX: return "VIRTUE Desktop macOS.app";
                        case BuildTarget.StandaloneLinux64: return "VIRTUE Desktop Linux";
                        default: return null;
                    }
                case "VIRTUE Lite":
                    switch (target)
                    {
                        case BuildTarget.StandaloneWindows64: return "VIRTUE Lite Windows";
                        case BuildTarget.StandaloneOSX: return "VIRTUE Lite macOS.app";
                        case BuildTarget.StandaloneLinux64: return "VIRTUE Lite Linux";
                        default: return null;
                    }
                case "VIRTUE VR":
                    return target == BuildTarget.StandaloneWindows64 ? "VIRTUE VR" : null;
                default:
                    return null;
            }
        }

        // Every executable/bundle uses the bare product name (e.g.
        // "VIRTUE Desktop.exe", "VIRTUE Desktop.x86_64") -- the OS is
        // already unambiguous from the format itself (.exe, .app, the
        // .x86_64 extension) and from the containing folder, so repeating
        // it in the filename is redundant.
        // NOTE: this reverts both the Windows executable (was briefly
        // "VIRTUE Desktop Windows.exe") and the Linux executable (was
        // "VIRTUE Desktop Linux.x86_64") back to the bare product name.
        // Steam's Launch Options (configured on the Steamworks partner
        // website, not in any local .vdf) reference the exact filenames and
        // need a matching manual update there.
        private static string ExecutableName(string productName, string folderName, BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneWindows:
                    return productName + ".exe";
                case BuildTarget.StandaloneLinux64:
                    return productName + ".x86_64";
                default:
                    return productName;
            }
        }
    }
}
