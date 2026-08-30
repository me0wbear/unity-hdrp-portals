using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Автоматизация сборки сцены с порталами. Все операции доступны и из меню, и как
/// публичные методы, чтобы их можно было вызвать из скрипта сборки сцены.
/// </summary>
public static class PortalSetupTools
{
    private const string PrefabPath = "Assets/portal/Portal.prefab";

    [MenuItem("Tools/Portals/Create Portal Pair")]
    private static void CreatePortalPairMenu()
    {
        CreatePortalPair(null);
    }

    /// <summary>
    /// Создаёт связанную пару порталов с уже назначенной камерой. Пара ставится
    /// на расстоянии друг от друга, чтобы виды не пересекались: дальше их
    /// расставляют руками.
    /// </summary>
    public static void CreatePortalPair(Transform parent)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError("[PortalSetupTools] " + PrefabPath + " not found");
            return;
        }

        Camera camera = FindPlayerCamera();

        Portal first = InstantiatePortal(
            prefab, parent, "Portal_A", new Vector3(0f, 1.5f, 0f), Quaternion.Euler(0f, 180f, 0f));
        Portal second = InstantiatePortal(
            prefab, parent, "Portal_B", new Vector3(30f, 1.5f, 0f), Quaternion.Euler(0f, 180f, 0f));

        Link(first, second, camera);
        Link(second, first, camera);

        Debug.Log("[PortalSetupTools] created pair " + first.name + " and " + second.name);
    }

    /// <summary>Связывает все несвязанные порталы попарно и раздаёт им одну камеру.</summary>
    [MenuItem("Tools/Portals/Wire Scene")]
    public static void WireScene()
    {
        Camera camera = FindPlayerCamera();
        Portal[] portals = Object.FindObjectsByType<Portal>(FindObjectsSortMode.InstanceID);

        var unpaired = new List<Portal>();
        foreach (Portal portal in portals)
        {
            if (portal.playerCamera == null)
            {
                portal.playerCamera = camera;
                EditorUtility.SetDirty(portal);
            }

            if (portal.exitPortal == null)
            {
                unpaired.Add(portal);
            }
        }

        // Соседние по списку становятся парой. Список отсортирован по порядку
        // создания объектов, поэтому пара, добавленная вместе, и свяжется вместе.
        for (int i = 0; i + 1 < unpaired.Count; i += 2)
        {
            Link(unpaired[i], unpaired[i + 1], camera);
            Link(unpaired[i + 1], unpaired[i], camera);
            Debug.Log("[PortalSetupTools] paired " + unpaired[i].name
                + " with " + unpaired[i + 1].name);
        }

        if (unpaired.Count % 2 != 0)
        {
            Debug.LogWarning("[PortalSetupTools] " + unpaired[unpaired.Count - 1].name
                + " has no pair: the number of unpaired portals is odd");
        }
    }

    /// <summary>Докладывает про каждый портал, чего ему не хватает для работы.</summary>
    [MenuItem("Tools/Portals/Validate Scene")]
    public static void ValidateScene()
    {
        Portal[] portals = Object.FindObjectsByType<Portal>(FindObjectsSortMode.InstanceID);
        if (portals.Length == 0)
        {
            Debug.LogWarning("[PortalSetupTools] scene contains no portals");
        }

        foreach (Portal portal in portals)
        {
            if (portal.exitPortal == null)
            {
                Debug.LogWarning("[PortalSetupTools] " + portal.name + ": exitPortal is not set");
            }
            else if (portal.exitPortal.exitPortal != portal)
            {
                Debug.LogWarning("[PortalSetupTools] " + portal.name
                    + ": the pair is linked in one direction only");
            }

            if (portal.playerCamera == null)
            {
                Debug.LogWarning("[PortalSetupTools] " + portal.name + ": playerCamera is not set");
            }

            if (portal.screen == null)
            {
                Debug.LogWarning("[PortalSetupTools] " + portal.name + ": screen is not set");
            }
            else if (portal.screen.transform.parent != portal.transform)
            {
                Debug.LogWarning("[PortalSetupTools] " + portal.name
                    + ": screen must be a direct child of the portal root");
            }

            if (!portal.TryGetComponent(out Collider trigger) || !trigger.isTrigger)
            {
                Debug.LogWarning("[PortalSetupTools] " + portal.name
                    + ": a trigger collider is required to detect crossings");
            }
        }

        PortalTraveller[] travellers =
            Object.FindObjectsByType<PortalTraveller>(FindObjectsSortMode.InstanceID);
        if (travellers.Length == 0)
        {
            Debug.LogWarning("[PortalSetupTools] no PortalTraveller in the scene");
        }

        foreach (PortalTraveller traveller in travellers)
        {
            if (traveller.ViewPoint == traveller.transform)
            {
                Debug.LogWarning("[PortalSetupTools] " + traveller.name
                    + ": viewPoint is not set, the root is used instead");
            }
        }

        Debug.Log("[PortalSetupTools] validated " + portals.Length + " portals");
    }

    private static Portal InstantiatePortal(
        GameObject prefab, Transform parent, string name, Vector3 position, Quaternion rotation)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.name = name;
        instance.transform.SetPositionAndRotation(position, rotation);
        return instance.GetComponent<Portal>();
    }

    private static void Link(Portal portal, Portal exit, Camera camera)
    {
        portal.exitPortal = exit;
        portal.playerCamera = camera;
        EditorUtility.SetDirty(portal);
    }

    private static Camera FindPlayerCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            camera = Object.FindFirstObjectByType<Camera>();
        }

        return camera;
    }
}
