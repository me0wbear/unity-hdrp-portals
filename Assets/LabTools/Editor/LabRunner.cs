using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Headless entry points: build the scene, then run it in play mode until
/// <see cref="LabCapture"/> has written its frames.
/// </summary>
public static class LabRunner
{
    public static void BuildScene()
    {
        LabSceneBuilder.Build();
        EditorApplication.Exit(0);
    }

    public static void RunCapture()
    {
        EditorSceneManager.OpenScene(LabSceneBuilder.ScenePath, OpenSceneMode.Single);
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }

        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Debug.Log("[LabRunner] play mode finished");
        EditorApplication.Exit(0);
    }
}
