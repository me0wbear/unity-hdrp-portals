using UnityEngine;

/// <summary>
/// Один портал. Лицевая сторона — локальная ось +Z: она должна смотреть туда,
/// откуда на портал смотрит игрок. Парный портал задаётся в <see cref="exitPortal"/>,
/// связывать нужно в обе стороны.
/// </summary>
[DisallowMultipleComponent]
public sealed class Portal : MonoBehaviour
{
    [Header("Связи")]
    [Tooltip("Квад, на котором показывается вид. Должен быть прямым потомком этого объекта.")]
    public MeshRenderer screen;

    [Tooltip("Парный портал, из которого выходит наблюдатель.")]
    public Portal exitPortal;

    [Tooltip("Камера, для которой считается вид. Обычно камера игрока.")]
    public Camera playerCamera;

    [Header("Качество")]
    [Tooltip("Делитель разрешения таргета. 1 — размер экрана, соответствие пиксель в пиксель.")]
    [Min(1)] public int resolutionDivider = 1;

    [Tooltip("Сколько раз портал виден сам через себя. 0 — без рекурсии.")]
    [Min(0)] public int recursionDepth = 2;

    [Header("Отсечение")]
    [Tooltip("Сдвиг косой ближней плоскости от плоскости выхода, в метрах.")]
    public float clippingOffset = 0.05f;

    [Tooltip("Запас, на который квад выдвигается навстречу камере вблизи.")]
    [Min(0f)] public float clippingSafetyFactor = 2f;

    [Tooltip("Не рендерить вид, когда проём вне поля зрения.")]
    public bool cullWhenOffscreen = true;

    [Header("Композит")]
    [Tooltip("Писать глубину видимого через портал в буфер главной камеры. "
        + "Нужно, чтобы туман, глубина резкости и затенение работали в проёме по "
        + "настоящему расстоянию, а не по расстоянию до квада.")]
    public bool writeContentDepth = true;

    [Tooltip("Плавно переносить состояние Volume назначения по мере приближения, "
        + "чтобы разные настройки в двух комнатах не менялись рывком после перехода.")]
    public bool blendVolumesThroughPortal = true;

    [Header("Оформление")]
    [Tooltip("Чем заполняется проём на последнем уровне рекурсии.")]
    [ColorUsage(false, true)]
    public Color fallbackColor = new Color(0.02f, 0.02f, 0.03f, 1f);

    [Tooltip("Что делать с двойником, если его материал не поддерживает отсечение.")]
    public CloneFallback cloneFallback = CloneFallback.DrawUnsliced;

    private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
    private static readonly int FallbackColorId = Shader.PropertyToID("_FallbackColor");
    private static readonly int HasTextureId = Shader.PropertyToID("_HasTexture");

    private MaterialPropertyBlock _block;

    /// <summary>Размер проёма в метрах, взятый из масштаба квада.</summary>
    public Vector2 OpeningSize
    {
        get
        {
            if (screen == null)
            {
                return Vector2.one;
            }

            Vector3 scale = screen.transform.localScale;
            return new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }
    }

    /// <summary>
    /// Кладёт текстуру вида на квад. Через блок свойств, а не через материал:
    /// материал общий для всех порталов, и запись в него склеила бы их виды.
    /// </summary>
    public void SetViewTexture(Texture texture)
    {
        if (screen == null)
        {
            return;
        }

        _block ??= new MaterialPropertyBlock();
        screen.GetPropertyBlock(_block);

        // Текстура нужна и как значение шейдера, и как признак: на последнем
        // уровне рекурсии её нет, и квад заполняется цветом заглушки.
        _block.SetTexture(MainTextureId, texture != null ? texture : Texture2D.blackTexture);
        _block.SetFloat(HasTextureId, texture != null ? 1f : 0f);
        _block.SetColor(FallbackColorId, fallbackColor);

        screen.SetPropertyBlock(_block);
    }

    private void OnEnable()
    {
        PortalSystem.Register(this);
    }

    private void OnDisable()
    {
        PortalSystem.Unregister(this);
    }
}

/// <summary>Поведение двойника, когда материал оригинала не умеет отсекаться плоскостью.</summary>
public enum CloneFallback
{
    /// <summary>Рисовать двойника целиком. Он может торчать сквозь стену рядом с выходом.</summary>
    DrawUnsliced,

    /// <summary>Не рисовать двойника вовсе. Оригинал в момент перехода пропадёт из виду.</summary>
    Hide
}
