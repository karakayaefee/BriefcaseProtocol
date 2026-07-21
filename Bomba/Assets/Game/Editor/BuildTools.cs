using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BriefcaseProtocol.Editor
{
    public static class BuildTools
    {
        private const string BuildRoot = "Builds/Windows";

        [MenuItem("Briefcase Protocol/Build/Windows Development")]
        public static void BuildWindowsDevelopment()
        {
            Build(BuildOptions.Development | BuildOptions.AllowDebugging, "BriefcaseProtocol-Dev.exe");
        }

        [MenuItem("Briefcase Protocol/Build/Windows Release")]
        public static void BuildWindowsRelease()
        {
            Build(BuildOptions.StrictMode, "BriefcaseProtocol.exe");
        }

        public static void BuildWindowsDevelopmentBatch() => BuildWindowsDevelopment();
        public static void BuildWindowsReleaseBatch() => BuildWindowsRelease();

        private static void Build(BuildOptions options, string executableName)
        {
            Directory.CreateDirectory(BuildRoot);
            PlayerSettings.companyName = "BugKnot Studios";
            PlayerSettings.productName = "Briefcase Protocol";
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes. Run Briefcase Protocol/Setup Prototype Project first.");
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(BuildRoot, executableName),
                target = BuildTarget.StandaloneWindows64,
                options = options
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"Build failed: {report.summary.result}");
            }

            Debug.Log($"Build completed: {report.summary.outputPath} ({report.summary.totalSize} bytes)");
        }
    }
}
