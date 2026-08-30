using UnityEngine;

/// <summary>
/// Простая ходилка от первого лица для демо-сцены и ручной проверки порталов.
/// Не часть модуля порталов: в своём проекте вместо неё будет собственный
/// контроллер игрока. Нужна затем, чтобы модуль можно было пощупать сразу,
/// не подключая ничего постороннего.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public sealed class PortalDemoController : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Transform камеры. Тангаж применяется к нему, рыскание — к корню.")]
    private Transform head;

    [Header("Движение")]
    [SerializeField] private float walkSpeed = 3.5f;
    [SerializeField] private float runSpeed = 6f;
    [SerializeField] private float gravity = 18f;

    [Header("Обзор")]
    [SerializeField] private float mouseSensitivity = 2.2f;
    [SerializeField] private float pitchLimit = 85f;

    private CharacterController _controller;
    private float _pitch;
    private float _verticalSpeed;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (head == null)
        {
            head = GetComponentInChildren<Camera>()?.transform;
        }
    }

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Тангаж хранится здесь, а не читается из трансформа: после перехода
        // портал поворачивает корень, и накопленный угол должен пережить поворот.
        if (head != null)
        {
            _pitch = NormalizeAngle(head.localEulerAngles.x);
        }
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        Look();
        Move();
    }

    private void Look()
    {
        float yawDelta = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float pitchDelta = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        // Рыскание крутит корень, поэтому портал, повернувший корень при переходе,
        // разворачивает и взгляд — ничего дополнительно пересчитывать не нужно.
        transform.Rotate(0f, yawDelta, 0f, Space.Self);

        _pitch = Mathf.Clamp(_pitch - pitchDelta, -pitchLimit, pitchLimit);
        if (head != null)
        {
            head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }

    private void Move()
    {
        float forward = Input.GetAxisRaw("Vertical");
        float strafe = Input.GetAxisRaw("Horizontal");

        Vector3 direction = transform.forward * forward + transform.right * strafe;
        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;

        if (_controller.isGrounded && _verticalSpeed < 0f)
        {
            // Небольшая прижимающая скорость, иначе контроллер отрывается от пола
            // на стыках коллайдеров и isGrounded мигает.
            _verticalSpeed = -2f;
        }
        else
        {
            _verticalSpeed -= gravity * Time.deltaTime;
        }

        Vector3 motion = direction * speed;
        motion.y = _verticalSpeed;
        _controller.Move(motion * Time.deltaTime);
    }

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        return degrees > 180f ? degrees - 360f : degrees;
    }
}
