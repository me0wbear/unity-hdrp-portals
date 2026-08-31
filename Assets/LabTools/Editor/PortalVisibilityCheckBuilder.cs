using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Сборка сохранённых контрольных сцен без регенерации ассетов.</summary>
public static class PortalVisibilityCheckBuilder
{
    public static void BuildPlayer() =>
        SandboxCheckBuildProcessor.Build("Visibility", "BuildPortalVisibilityCheck", "PortalVisibilityCheck.exe");

    public static BuildPlayerOptions LegacyOptions(string check, string output)
    {
        if (check != "Ghost" && check != "Cross" && check != "Rotate")
            throw new ArgumentException("Unsupported saved legacy check.", nameof(check));
        return new BuildPlayerOptions
        {
            scenes = new[] { "Assets/LabTools/" + check + "Check.unity" },
            locationPathName = Path.Combine(output, check + "Check.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development | BuildOptions.CleanBuildCache
        };
    }

    public static void BuildLegacy()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PORTAL_CHECK_NAME")))
            throw new BuildFailedException("Saved legacy build must not carry a certified check context.");
        string output = Environment.GetEnvironmentVariable("PORTAL_LEGACY_OUTPUT");
        if (string.IsNullOrEmpty(output)) throw new BuildFailedException("PORTAL_LEGACY_OUTPUT is required.");
        Directory.CreateDirectory(output);
        BuildReport report = BuildPipeline.BuildPlayer(LegacyOptions(
            Environment.GetEnvironmentVariable("PORTAL_LEGACY_CHECK"), output));
        Debug.Log("[PortalSavedLegacyBuild] " + report.summary.result);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
