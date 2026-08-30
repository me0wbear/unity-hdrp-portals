using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Turns the lab scene into something a person can walk around in: the automated capture that
/// drives the player along a fixed route is removed, the demo controller is switched on, and the
/// transition probe is attached so a play session still produces a measurement.
/// </summary>
public static class LabProbeInstaller
{
    private const string ScenePath = "Assets/LabTools/PortalLab.unity";

    public static void Install()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // The capture component moves the player itself, which is why nothing responds to input.
        LabCapture[] captures = Object.FindObjectsByType<LabCapture>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (LabCapture capture in captures)
        {
            Object.DestroyImmediate(capture.gameObject);
        }

        PortalTraveller traveller = Object.FindFirstObjectByType<PortalTraveller>();
        if (traveller == null)
        {
            Debug.LogError("[Lab] No PortalTraveller in " + ScenePath);
            EditorApplication.Exit(1);
            return;
        }

        GameObject player = traveller.gameObject;

        PortalDemoController movement = player.GetComponent<PortalDemoController>();
        if (movement == null)
        {
            movement = player.AddComponent<PortalDemoController>();
        }

        movement.enabled = true;

        // The controller needs the eye transform, which is the same one the portal tracks.
        SerializedObject movementObject = new SerializedObject(movement);
        SerializedProperty headProperty = movementObject.FindProperty("head");
        if (headProperty.objectReferenceValue == null && traveller.ViewPoint != null)
        {
            headProperty.objectReferenceValue = traveller.ViewPoint;
            movementObject.ApplyModifiedPropertiesWithoutUndo();
        }

        PortalTransitionProbe probe = player.GetComponent<PortalTransitionProbe>();
        if (probe == null)
        {
            probe = player.AddComponent<PortalTransitionProbe>();
        }

        probe.traveller = traveller;

        var summary = new StringBuilder();
        summary.Append("[Lab] Player '").Append(player.name).Append("' now carries: ");
        foreach (Component component in player.GetComponents<Component>())
        {
            summary.Append(component.GetType().Name).Append(' ');
        }

        summary.Append("\n[Lab] Removed ").Append(captures.Length).Append(" capture driver(s).");
        summary.Append("\n[Lab] Head: ").Append(
            headProperty.objectReferenceValue != null
                ? headProperty.objectReferenceValue.name
                : "MISSING");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(summary.ToString());
        EditorApplication.Exit(0);
    }
}
