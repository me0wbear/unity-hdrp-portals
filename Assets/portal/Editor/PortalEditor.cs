using UnityEditor;
using UnityEngine;

/// <summary>
/// Инспектор портала. Показывает то, что мешает ему работать, прямо на месте:
/// добрая половина обращений «в проёме пусто» лечится одним из этих пунктов, и
/// увидеть их в инспекторе быстрее, чем искать в логе.
/// </summary>
[CustomEditor(typeof(Portal))]
[CanEditMultipleObjects]
public sealed class PortalEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        if (targets.Length > 1)
        {
            return;
        }

        var portal = (Portal)target;

        EditorGUILayout.Space();
        DrawScreenIssues(portal);
        DrawPairIssues(portal);
        DrawCameraIssues(portal);
        DrawTriggerIssues(portal);
        DrawRecursionHint(portal);

        EditorGUILayout.Space();
        if (GUILayout.Button("Проверить всю сцену"))
        {
            PortalSetupTools.ValidateScene();
        }
    }

    private static void DrawScreenIssues(Portal portal)
    {
        if (portal.screen == null)
        {
            EditorGUILayout.HelpBox(
                "Не назначен Screen. Показывать вид не на чем.", MessageType.Error);
            return;
        }

        if (portal.screen.transform.parent != portal.transform)
        {
            EditorGUILayout.HelpBox(
                "Screen должен быть прямым потомком корня портала: его трансформ "
                + "переписывается каждый кадр, и промежуточный объект это сломает.",
                MessageType.Error);
        }

        Material material = portal.screen.sharedMaterial;
        if (material == null)
        {
            EditorGUILayout.HelpBox(
                "На Screen нет материала. Назначьте PortalScreenMat.", MessageType.Error);
        }
        else if (!material.HasProperty("_MainTex"))
        {
            EditorGUILayout.HelpBox(
                "Материал " + material.name + " не имеет свойства _MainTex, "
                + "положить в него вид невозможно.", MessageType.Error);
        }

        if (portal.screen.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
        {
            EditorGUILayout.HelpBox(
                "Screen отбрасывает тень. Проём не должен затенять сцену.",
                MessageType.Warning);
        }
    }

    private static void DrawPairIssues(Portal portal)
    {
        if (portal.exitPortal == null)
        {
            EditorGUILayout.HelpBox("Не назначен Exit Portal.", MessageType.Error);
            return;
        }

        if (portal.exitPortal == portal)
        {
            EditorGUILayout.HelpBox(
                "Портал указывает сам на себя.", MessageType.Error);
            return;
        }

        if (portal.exitPortal.exitPortal != portal)
        {
            EditorGUILayout.HelpBox(
                "Пара связана только в одну сторону. Обратно пройти будет нельзя.",
                MessageType.Warning);
        }
    }

    private static void DrawCameraIssues(Portal portal)
    {
        if (portal.playerCamera == null)
        {
            EditorGUILayout.HelpBox(
                "Не назначена Player Camera. Вид считать не для кого.", MessageType.Error);
        }
    }

    private static void DrawTriggerIssues(Portal portal)
    {
        if (!portal.TryGetComponent(out Collider trigger))
        {
            EditorGUILayout.HelpBox(
                "Нет коллайдера. Он описывает зону вокруг проёма.", MessageType.Warning);
            return;
        }

        if (!trigger.isTrigger)
        {
            EditorGUILayout.HelpBox(
                "У коллайдера выключен Is Trigger: игрок упрётся в проём.",
                MessageType.Error);
        }
    }

    private static void DrawRecursionHint(Portal portal)
    {
        if (portal.recursionDepth >= 4)
        {
            EditorGUILayout.HelpBox(
                "Глубина рекурсии " + portal.recursionDepth + ": это "
                + (portal.recursionDepth + 1) + " камер и столько же наборов буферов "
                + "размером с экран на один портал.", MessageType.Warning);
        }
    }

    /// <summary>
    /// Гизмо: прямоугольник проёма, направление лицевой стороны и связь с парой.
    /// Ориентация — главный источник ошибок при расстановке, поэтому её видно
    /// всегда, а не только у выделенного портала.
    /// </summary>
    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawGizmo(Portal portal, GizmoType type)
    {
        Vector2 size = portal.OpeningSize;
        bool selected = (type & GizmoType.Selected) != 0;

        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(
            portal.transform.position, portal.transform.rotation, Vector3.one);

        Gizmos.color = selected
            ? new Color(0.35f, 0.85f, 1f, 1f)
            : new Color(0.35f, 0.85f, 1f, 0.35f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0f));

        // Лицевая сторона: локальная ось +Z обязана смотреть на игрока.
        Gizmos.color = selected ? Color.yellow : new Color(1f, 0.92f, 0.2f, 0.35f);
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.5f);
        Gizmos.DrawLine(
            Vector3.forward * 0.5f, new Vector3(0.08f, 0f, 0.35f));
        Gizmos.DrawLine(
            Vector3.forward * 0.5f, new Vector3(-0.08f, 0f, 0.35f));

        Gizmos.matrix = previous;

        if (portal.exitPortal != null && selected)
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.6f);
            Gizmos.DrawLine(portal.transform.position, portal.exitPortal.transform.position);
        }
    }
}
