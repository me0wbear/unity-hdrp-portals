using UnityEngine;

/// <summary>
/// Контроллер вида, устроенный так же, как большинство готовых FPS-наборов:
/// угол взгляда он держит у себя в полях и каждый кадр переписывает из них
/// трансформы тела и головы.
///
/// Именно эта порода контроллеров ломает порталы, и ради неё пример и написан.
/// Портал на переходе поворачивает корень игрока, но про поля этого контроллера
/// он ничего не знает — и уже на следующем кадре LateUpdate возвращает взгляд в
/// прежнюю мировую сторону. Снаружи это выглядит так, будто игрок прошёл сквозь
/// повёрнутый портал и его развернуло обратно.
///
/// Чинит это <see cref="ExampleLookPortalBridge"/>. Он и есть то, что нужно
/// повторить у себя, если камерой управляет ваш собственный контроллер.
/// </summary>
public sealed class ExampleLookController : MonoBehaviour
{
    [Header("Что поворачивать")]
    [Tooltip("Тело: несёт рыскание. Обычно корень игрока.")]
    public Transform body;

    [Tooltip("Голова: несёт тангаж. Обычно объект с камерой.")]
    public Transform head;

    [Header("Сохранённый угол")]
    [Tooltip("Рыскание в мировых градусах. Его и поворачивает мост на переходе.")]
    public float yaw;

    [Tooltip("Тангаж в градусах. Переход его не меняет.")]
    public float pitch;

    [Header("Управление")]
    public float mouseSensitivity = 2.2f;
    public float pitchLimit = 85f;
    public float walkSpeed = 3.5f;
    public float gravity = 18f;

    private CharacterController _controller;
    private float _verticalSpeed;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();

        if (body == null)
        {
            body = transform;
        }

        yaw = body.eulerAngles.y;
    }

    private void Update()
    {
        yaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        pitch = Mathf.Clamp(
            pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, -pitchLimit, pitchLimit);

        Move();
    }

    private void Move()
    {
        if (_controller == null)
        {
            return;
        }

        Vector3 wish = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.ClampMagnitude(wish, 1f);

        _verticalSpeed = _controller.isGrounded
            ? -1f
            : _verticalSpeed - gravity * Time.deltaTime;

        Vector3 velocity = direction * walkSpeed;
        velocity.y = _verticalSpeed;
        _controller.Move(velocity * Time.deltaTime);
    }

    /// <summary>
    /// Позы переписываются в LateUpdate, то есть после всей игровой логики. В
    /// этом и суть: что бы ни повернуло игрока за кадр, последнее слово остаётся
    /// за сохранённым углом.
    /// </summary>
    private void LateUpdate()
    {
        if (body != null)
        {
            body.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        if (head != null)
        {
            head.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
