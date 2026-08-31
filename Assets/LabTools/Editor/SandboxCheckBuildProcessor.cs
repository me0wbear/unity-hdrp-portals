using System;
using System.IO;
using System.Linq;
using Portals.Lab.Validation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public sealed class SandboxCheckBuildProcessor : IProcessSceneWithReport
{
    public const string ScenePath = "Assets/portal/Examples/PortalSandbox.unity";
    public int callbackOrder => -9999;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (!BuildPipeline.isBuildingPlayer || report == null || scene.path != ScenePath) return;
        string check = Environment.GetEnvironmentVariable("PORTAL_CHECK_NAME");
        if (check != "SandboxParity" && check != "Performance" && check != "Visibility") return;
        PortalCheckRun[] contexts = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PortalCheckRun>(true)).ToArray();
        if (contexts.Length != 1 || new SerializedObject(contexts[0]).FindProperty("identity.check").stringValue != check)
            throw new BuildFailedException("Sandbox probe requires exactly one matching embedded build identity.");
        Inject(scene, check);
    }

    public static void Inject(Scene scene, string check)
    {
        if (check != "SandboxParity" && check != "Performance" && check != "Visibility") return;
        Type selected = check == "SandboxParity" ? typeof(SandboxParityCheck)
            : check == "Performance" ? typeof(PortalPerformanceCheck) : typeof(PortalVisibilityCheck);
        Type[] probes = { typeof(SandboxParityCheck), typeof(PortalPerformanceCheck), typeof(PortalVisibilityCheck) };
        Component[] existing = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(selected, true)).ToArray();
        if (existing.Length > 1 || probes.Any(other => other != selected
            && scene.GetRootGameObjects().Any(root => root.GetComponentsInChildren(other, true).Length != 0)))
            throw new BuildFailedException("Sandbox build contains duplicate or conflicting probes.");
        Component probe;
        if (existing.Length == 1) probe = existing[0];
        else
        {
            var host = new GameObject(selected.Name);
            SceneManager.MoveGameObjectToScene(host, scene);
            probe = host.AddComponent(selected);
        }
        if (probe is SandboxParityCheck parity)
        {
            parity.leakageShader = Shader.Find("HDRP/Unlit");
            if (parity.leakageShader == null) throw new BuildFailedException("HDRP Unlit shader for leakage positive control is missing.");
        }
    }

    public static void Build(string check, string directory, string executable)
    {
        if (Environment.GetEnvironmentVariable("PORTAL_CHECK_NAME") != check)
            throw new BuildFailedException("Builder check does not match PORTAL_CHECK_NAME.");
        GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
        if (apis.Length == 0 || apis[0] != GraphicsDeviceType.Direct3D12)
            throw new BuildFailedException("Sandbox check requires the existing D3D12-first Windows graphics configuration.");
        string output = Path.Combine(Directory.GetCurrentDirectory(), directory);
        Directory.CreateDirectory(output);
        // BuildPipeline обрабатывает копию исходной сцены; SaveScene/SaveAssets здесь не вызываются.
        var options = PortalCheckBuildIdentity.PrepareOptions(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath }, locationPathName = Path.Combine(output, executable),
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        }, check);
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[SandboxCheckBuild] " + check + " " + report.summary.result);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
