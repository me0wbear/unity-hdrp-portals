using UnityEngine;

/// <summary>
/// Держит квад портала так, чтобы он не пропадал, когда глаз подходит вплотную.
///
/// Задача. Квад стоит в плоскости проёма. Когда наблюдатель подходит ближе, чем
/// его ближняя плоскость отсечения, квад целиком уходит за неё и перестаёт
/// рисоваться — в последние сантиметры перед переходом проём гаснет, и сквозь
/// него видно то, что за ним. Это и есть просвет, который ловят CloseCheck и
/// BubbleCheck.
///
/// Решение. Вблизи квад отодвигается от наблюдателя вдоль нормали портала ровно
/// настолько, чтобы расстояние до него равнялось расстоянию удержания. Оно
/// подобрано так, чтобы квад одновременно не попадал за ближнюю плоскость и
/// оставался достаточно близко, чтобы закрывать весь экран: см.
/// PortalMath.ScreenHoldDistance.
///
/// Почему сдвиг ничего не портит. Шейдер квада выбирает вид по экранным
/// координатам, а не по развёртке, поэтому сдвиг самого квада не двигает
/// картинку в проёме. Двигать его можно свободно, лишь бы он покрывал нужные
/// пиксели.
/// </summary>
public static class PortalAperture
{
    /// <summary>
    /// Квад должен быть прямым потомком корня портала. Декоративную рамку вешать
    /// соседом квада, а не родителем: родитель сломал бы трансформ, который этот
    /// метод переписывает каждый кадр.
    /// </summary>
    public static void Fit(Portal portal, Camera viewer)
    {
        if (portal == null || portal.screen == null || viewer == null)
        {
            return;
        }

        Transform screen = portal.screen.transform;

        float hold = PortalMath.ScreenHoldDistance(
            viewer, portal.OpeningSize, portal.clippingSafetyFactor);
        float distance = PortalMath.SignedDistance(portal.transform, viewer.transform.position);

        screen.localPosition = new Vector3(0f, 0f, Offset(distance, hold));
    }

    /// <summary>
    /// Смещение квада вдоль локальной оси Z портала.
    ///
    /// Дальше расстояния удержания квад стоит в плоскости проёма. Ближе —
    /// отодвигается так, чтобы расстояние от наблюдателя до него равнялось
    /// удержанию. На самой границе обе ветки дают ноль, поэтому переход между
    /// ними непрерывный и рывка нет.
    /// </summary>
    public static float Offset(float signedDistance, float holdDistance)
    {
        if (Mathf.Abs(signedDistance) >= holdDistance)
        {
            return 0f;
        }

        float side = signedDistance >= 0f ? 1f : -1f;
        return signedDistance - side * holdDistance;
    }
}
