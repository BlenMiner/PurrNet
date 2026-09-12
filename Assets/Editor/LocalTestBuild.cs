using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Local (non-CI) build entry point for running the network-test scenarios on Windows.
// Invoke via: Unity -batchmode -quit -executeMethod LocalTestBuild.BuildWindowsPlayer
//             [-purrBuildOutput <path/to/PurrNetTests.exe>]
public static class LocalTestBuild
{
    private const string OutputPath = "Builds/LocalNetworkTests/PurrNetTests.exe";

    public static void BuildWindowsPlayer()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        var outputPath = GetCommandLineValue("-purrBuildOutput") ?? OutputPath;
        var release = Array.IndexOf(Environment.GetCommandLineArgs(), "-purrRelease") >= 0;

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = release ? BuildOptions.None : BuildOptions.Development
        };

        Debug.Log($"[LocalTestBuild] Building StandaloneWindows64 -> {outputPath}");
        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        Debug.Log($"[LocalTestBuild] Result={summary.result} size={summary.totalSize} errors={summary.totalErrors}");

        EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    private static string GetCommandLineValue(string key)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == key)
                return args[i + 1];
        }

        return null;
    }
}
