using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Сборка песочницы с замерщиком. Саму сцену на диске не меняет.</summary>
public static class BenchmarkBuilder
{
    public static void BuildPlayer()
    {
        Scene scene = EditorSceneManager.OpenScene(
            "Assets/portal/Examples/PortalSandbox.unity", OpenSceneMode.Single);

        new GameObject("Benchmark").AddComponent<PortalBenchmark>();

        string temporary = "Assets/portal/Examples/Benchmark/Temporary.unity";
        EditorSceneManager.SaveScene(scene, temporary, true);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { temporary },
            locationPathName = "BuildBenchmark/Benchmark.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        EditorApplication.Exit(report.summary.result
            == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
