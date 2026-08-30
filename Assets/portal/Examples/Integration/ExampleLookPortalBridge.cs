using UnityEngine;

/// <summary>
/// Поворачивает сохранённый угол взгляда на переходе. Это тот самый десяток
/// строк, который нужно повторить у себя, если камерой управляет собственный
/// контроллер, а не Cinemachine и не UHFPS.
///
/// Зачем он нужен. Портал на переходе поворачивает трансформ игрока, но всё, что
/// хранит мировой угол отдельно от трансформа, про этот поворот не знает. Такой
/// контроллер на следующем же кадре перепишет трансформ из своего поля и вернёт
/// взгляд в прежнюю мировую сторону. Пара порталов, повёрнутых друг относительно
/// друга, после этого выглядит сломанной: игрок прошёл, а вид не повернулся.
///
/// Схема на любой контроллер одна и та же:
/// подписаться на <see cref="PortalTraveller.Teleported"/>, взять из события
/// <see cref="PortalTeleportContext.Rotation"/> и повернуть на неё своё
/// состояние. Поворачивать нужно всё, что живёт в мировых координатах: угол
/// взгляда, накопленную скорость, цель прицеливания.
/// </summary>
[RequireComponent(typeof(PortalTraveller))]
public sealed class ExampleLookPortalBridge : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Контроллер, чей сохранённый угол нужно поворачивать.")]
    private ExampleLookController look;

    [SerializeField]
    [Tooltip("Травеллер, за переходами которого нужно следить.")]
    private PortalTraveller traveller;

    private void Reset()
    {
        look = GetComponent<ExampleLookController>();
        traveller = GetComponent<PortalTraveller>();
    }

    private void OnEnable()
    {
        if (look == null)
        {
            look = GetComponent<ExampleLookController>();
        }

        if (traveller == null)
        {
            traveller = GetComponent<PortalTraveller>();
        }

        if (traveller != null)
        {
            traveller.Teleported += OnTeleported;
        }
    }

    private void OnDisable()
    {
        if (traveller != null)
        {
            traveller.Teleported -= OnTeleported;
        }
    }

    private void OnTeleported(PortalTeleportContext context)
    {
        if (look == null)
        {
            return;
        }

        // Складывается знаковая дельта, а не сам угол из eulerAngles. Тот всегда
        // лежит от 0 до 360, и поворот на минус 90 приходит оттуда как 270:
        // сложив 270 с накопленным рысканием, взгляд уводит в другую сторону на
        // те же 360 градусов, что на глаз ровно то же самое, но накопленный угол
        // после нескольких переходов уезжает.
        float yawDelta = context.Rotation.eulerAngles.y;
        yawDelta %= 360f;
        if (yawDelta > 180f)
        {
            yawDelta -= 360f;
        }

        look.yaw += yawDelta;

        // Тангаж не трогается: переход поворачивает вокруг вертикали.
    }
}
