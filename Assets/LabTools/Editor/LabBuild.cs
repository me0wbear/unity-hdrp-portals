using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Produces a Windows player for the lab scene. A real player is used for capture because
/// batch-mode editor play does not reliably reach WaitForEndOfFrame.
/// </summary>
public static class LabBuild
{
    private const string OutputDirectory = "Build";
    private const string ExecutableName = "PortalLab.exe";

    public static void BuildPlayer()
    {
        LabSceneBuilder.Build(true);

        string root = Path.Combine(Directory.GetCurrentDirectory(), OutputDirectory);
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { LabSceneBuilder.ScenePath },
            locationPathName = Path.Combine(root, ExecutableName),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[LabBuild] result=" + report.summary.result
            + " errors=" + report.summary.totalErrors
            + " time=" + report.summary.totalTime);

        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
