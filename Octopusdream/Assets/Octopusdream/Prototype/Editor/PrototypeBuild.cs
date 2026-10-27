using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Octopusdream.Editor
{
    public static class PrototypeBuild
    {
        public const string ExecutablePath = "Builds/Windows/Octopusdream.exe";

        [MenuItem("Octopusdream/Build Windows Prototype")]
        public static void BuildWindows()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Exit Play Mode before building.");
            if (!File.Exists(PrototypeSceneSetup.ScenePath))
                throw new BuildFailedException("The prepared park scene is missing.");
            if (!File.Exists("steam_appid.txt") || File.ReadAllText("steam_appid.txt").Trim() != "480")
                throw new BuildFailedException("The development build requires root steam_appid.txt containing 480.");
            PrototypeSceneSetup.Inspect();
            Directory.CreateDirectory(Path.GetDirectoryName(ExecutablePath));
            Directory.CreateDirectory("Logs/Prototype");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { PrototypeSceneSetup.ScenePath },
                locationPathName = ExecutablePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development
            });
            BuildSummary summary = report.summary;
            BuildResultInfo result = new BuildResultInfo
            {
                result = summary.result.ToString(),
                output = Path.GetFullPath(ExecutablePath),
                errors = summary.totalErrors,
                warnings = summary.totalWarnings,
                bytes = summary.totalSize.ToString(),
                durationSeconds = summary.totalTime.TotalSeconds,
                appId = 480,
                scene = PrototypeSceneSetup.ScenePath,
                utc = DateTime.UtcNow.ToString("o")
            };
            if (summary.result == BuildResult.Succeeded)
            {
                string outputFolder = Path.GetDirectoryName(ExecutablePath);
                File.Copy("steam_appid.txt", Path.Combine(outputFolder, "steam_appid.txt"), true);
                result.steamAppIdCopied = true;
                CopyDistributionFiles(outputFolder);
                result.thirdPartyNoticesCopied = true;
                result.readmeCopied = true;
            }
            File.WriteAllText("Logs/Prototype/build.json", JsonUtility.ToJson(result, true));
            Debug.Log("OCTOPUS_BUILD_RESULT: " + JsonUtility.ToJson(result));
            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Windows prototype build failed. See Logs/Prototype/build.json and the Unity build log.");
        }

        private static void CopyDistributionFiles(string outputFolder)
        {
            string thirdPartyFolder = Path.Combine(outputFolder, "ThirdParty");
            Directory.CreateDirectory(thirdPartyFolder);
            File.Copy("Packages/com.rlabrecque.steamworks.net/LICENSE.md", Path.Combine(thirdPartyFolder, "Steamworks.NET-LICENSE.md"), true);
            File.Copy("Assets/Octopusdream/Prototype/Networking/SteamworksDependency.md", Path.Combine(thirdPartyFolder, "SteamworksDependency.md"), true);
            File.Copy("Assets/Octopusdream/Docs/WindowsBuildReadme.md", Path.Combine(outputFolder, "README_KO.md"), true);
        }

        [Serializable]
        private sealed class BuildResultInfo
        {
            public string result;
            public string output;
            public int errors;
            public int warnings;
            public string bytes;
            public double durationSeconds;
            public uint appId;
            public bool steamAppIdCopied;
            public bool thirdPartyNoticesCopied;
            public bool readmeCopied;
            public string scene;
            public string utc;
        }
    }
}
