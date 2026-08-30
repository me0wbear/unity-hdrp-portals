using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Builds a player containing only the SETUP.md walkthrough scene.</summary>
public static class SetupCheckBuild
{
    public static void BuildPlayer()
    {
        SetupCheckBuilder.Build();

        string root = Path.Combine(Directory.GetCurrentDirectory(), "BuildSetupCheck");
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { SetupCheckBuilder.ScenePath },
            locationPathName = Path.Combine(root, "SetupCheck.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[SetupCheckBuild] result=" + report.summary.result
            + " errors=" + report.summary.totalErrors);

        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
