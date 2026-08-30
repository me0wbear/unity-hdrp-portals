using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Рендер вида одного портала. Владеет виртуальными камерами по уровням рекурсии
/// и их таргетами.
///
/// Виртуальная камера — настоящий объект <see cref="Camera"/>, который HDRP
/// рендерит в штатном цикле. Только так у каждого уровня своя история кадров:
/// через RenderPipeline.SubmitRenderRequest это не работает, потому что HDRP
/// выдаёт всем запросам один и тот же канал истории и уровни затирали бы друг друга.
/// </summary>
public sealed class PortalRenderer
{
    /// <summary>
    /// На сколько порядок отрисовки виртуальных камер ниже, чем у наблюдателя.
    /// Меньшая глубина рисуется раньше, поэтому самый глубокий уровень успевает
    /// посчитаться до того, как его результат понадобится уровню выше.
    /// </summary>
    private const int DepthOffset = 100;

    /// <summary>
    /// Ближняя плоскость виртуальной камеры. Меньше ближней плоскости
    /// наблюдателя, чтобы косое отсечение оставалось применимым вплотную к
    /// плоскости выхода. Ниже опускать смысла нет: начинает сыпаться точность
    /// буфера глубины.
    /// </summary>
    private const float MinimumNearClip = 0.02f;

    private readonly Portal _portal;

    private Camera[] _cameras = System.Array.Empty<Camera>();
    private RenderTexture[] _targets = System.Array.Empty<RenderTexture>();
    private bool _subscribed;
    private bool _reported;

    /// <summary>Рендерит ли портал прямо сейчас. Приостановленный бюджет не занимает.</summary>
    private bool _active;

    // Глубина и векторы движения снимаются только с нулевого уровня: именно он
    // виден игроку напрямую, и только его пиксели попадают в буферы главной
    // камеры. Уровни глубже видны уже внутри чужого таргета, и своя глубина им
    // не нужна.
    private RTHandle _contentDepth;
    private RTHandle _contentMotion;

    /// <summary>Глубина того, что видно сквозь проём, в кодировке устройства.</summary>
    public RTHandle ContentDepth => _active ? _contentDepth : null;

    /// <summary>Экранное движение того, что видно сквозь проём.</summary>
    public RTHandle ContentMotion => _active ? _contentMotion : null;

    /// <summary>
    /// Обратная матрица проекции нулевого уровня. Нужна, чтобы развернуть глубину
    /// обратно в расстояние: проекция косая, и обычная линеаризация по ближней и
    /// дальней плоскости для неё неверна.
    /// </summary>
    public Matrix4x4 ContentInverseProjection { get; private set; } = Matrix4x4.identity;

    public PortalRenderer(Portal portal)
    {
        _portal = portal;
    }

    /// <summary>Сколько уровней рендерится прямо сейчас. Приостановленный портал даёт ноль.</summary>
    public int LevelCount => _active ? _cameras.Length : 0;

    /// <summary>
    /// Готовит позы и таргеты уровней. Вызывается раз в кадр из
    /// <see cref="PortalSystem"/>, до того как пайплайн начнёт рендерить камеры.
    /// </summary>
    public void Render(Camera viewer, int levels)
    {
        if (_portal.exitPortal == null || _portal.screen == null || viewer == null || levels < 1)
        {
            Suspend();
            return;
        }

        if (!IsVisible(viewer))
        {
            Suspend();
            return;
        }

        Resume();
        Subscribe();
        EnsureCapacity(levels, viewer);

        for (int level = 0; level < _cameras.Length; level++)
        {
            Matrix4x4 pose = PortalMath.EntranceToExit(
                    _portal.transform, _portal.exitPortal.transform, level + 1)
                * viewer.transform.localToWorldMatrix;

            _cameras[level].transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
            ApplyProjection(viewer, _cameras[level]);

            if (level == 0)
            {
                ContentInverseProjection = _cameras[0].projectionMatrix.inverse;
            }
        }
    }

    /// <summary>
    /// Приостанавливает рендер, не разрушая камеры и таргеты. Именно это делается,
    /// когда портал ушёл из видимости: он вернётся туда через кадр-другой, а
    /// пересоздание камер треплет внутренний кеш пайплайна и стоит заметно
    /// дороже, чем выключенная камера.
    /// </summary>
    private void Suspend()
    {
        _active = false;

        if (_cameras.Length == 0)
        {
            return;
        }

        for (int i = 0; i < _cameras.Length; i++)
        {
            if (_cameras[i] != null)
            {
                _cameras[i].enabled = false;
            }
        }

        // Привязки текстур намеренно остаются как были. Таргеты при приостановке
        // живы, отвязывать их незачем, а обнуление дало бы чёрную вспышку в тот
        // кадр, когда портал вернётся в поле зрения. Снимает привязки только
        // Release, вместе с самими таргетами.
    }

    /// <summary>Включает камеры обратно после приостановки.</summary>
    private void Resume()
    {
        _active = true;

        for (int i = 0; i < _cameras.Length; i++)
        {
            if (_cameras[i] != null && !_cameras[i].enabled)
            {
                _cameras[i].enabled = true;
            }
        }
    }

    /// <summary>
    /// Полностью разрушает камеры и таргеты. Вызывается, только когда портал
    /// выключен или уничтожен: на обычное уход из видимости есть Suspend.
    ///
    /// Уничтожение только отложенное. DestroyImmediate здесь пробовать не надо:
    /// на выходе из приложения HDRP не переживает синхронного исчезновения
    /// камеры, потому что свои HDCamera она прибирает в цикле рендера, которого
    /// в этот момент уже не будет. Приложение падает после того, как вся уборка
    /// модуля отработала, и выглядит это как чужая ошибка.
    /// </summary>
    public void Release()
    {
        Unsubscribe();
        ReleaseContentBuffers();

        // Снять свои таргеты с квада парного портала. Уровни рекурсии кладут их
        // именно туда, и без этой уборки парный портал остался бы со ссылкой на
        // освобождённую память: следующий же кадр сэмплит её и валит приложение.
        ClearBindingOnExitPortal();

        // Порядок важен. Object.Destroy откладывает уничтожение до конца кадра,
        // а RenderTexture.Release освобождает память на видеокарте немедленно.
        // Если сначала освободить таргет, камера доживёт кадр со ссылкой на
        // уже освобождённую память, и приложение упадёт. Поэтому камера сперва
        // отвязывается от таргета и выключается, а таргеты уничтожаются так же
        // отложенно, без ручного Release.
        for (int i = 0; i < _cameras.Length; i++)
        {
            if (_cameras[i] != null)
            {
                _cameras[i].targetTexture = null;
                _cameras[i].enabled = false;
                Object.Destroy(_cameras[i].gameObject);
            }
        }

        for (int i = 0; i < _targets.Length; i++)
        {
            if (_targets[i] != null)
            {
                Object.Destroy(_targets[i]);
            }
        }

        _cameras = System.Array.Empty<Camera>();
        _targets = System.Array.Empty<RenderTexture>();
        _active = false;

        if (_portal != null)
        {
            _portal.SetViewTexture(null);
        }
    }

    /// <summary>
    /// Разовая проверка того, что вид вообще способен дойти до экрана. Ловит
    /// случай, когда таргет исправно считается, а в проёме пусто: выключенный
    /// рендерер, потерянный материал, неподдерживаемый шейдер. Молчит, когда
    /// всё в порядке, — в лог попадает только то, что требует вмешательства.
    /// </summary>
    private void WarnOnBrokenScreenOnce()
    {
        if (_reported)
        {
            return;
        }

        _reported = true;

        MeshRenderer screen = _portal.screen;
        if (screen == null)
        {
            return;
        }

        if (!screen.enabled || !screen.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("[Portal] " + _portal.name
                + ": the screen renderer is disabled, the opening will stay empty");
        }

        Material material = screen.sharedMaterial;
        if (material == null)
        {
            Debug.LogWarning("[Portal] " + _portal.name
                + ": the screen has no material, assign PortalScreenMat");
            return;
        }

        if (material.shader == null || !material.shader.isSupported)
        {
            Debug.LogWarning("[Portal] " + _portal.name
                + ": shader " + (material.shader != null ? material.shader.name : "NONE")
                + " is not supported on this platform");
        }

        if (!material.HasProperty("_MainTex"))
        {
            Debug.LogWarning("[Portal] " + _portal.name
                + ": material " + material.name + " has no _MainTex, the view cannot be bound");
        }
    }

    private void Subscribe()
    {
        WarnOnBrokenScreenOnce();

        if (!_subscribed)
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            _subscribed = true;
        }
    }

    private void Unsubscribe()
    {
        if (_subscribed)
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _subscribed = false;
        }
    }

    /// <summary>
    /// Привязка содержимого выполняется здесь, а не в LateUpdate, потому что все
    /// виртуальные камеры рендерятся после обновления скриптов: привязка из цикла
    /// была бы перетёрта до того, как хоть одна камера успела отрисоваться.
    /// </summary>
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (_portal == null || _portal.exitPortal == null || _cameras.Length == 0)
        {
            return;
        }

        // Между отпиской и последним вызовом события таргеты могли уже уйти
        // на уничтожение: обращаться к ним в этот кадр нельзя.
        if (_targets.Length == 0 || _targets[0] == null)
        {
            return;
        }

        // Наблюдатель рисуется последним, поэтому свой вид портал возвращает себе
        // здесь, после того как все уровни уже посчитаны.
        if (ReferenceEquals(camera, _portal.playerCamera))
        {
            _portal.SetViewTexture(_targets[0]);
            _portal.SetContentBuffers(_contentDepth, _contentMotion, ContentInverseProjection);
            return;
        }

        int level = IndexOf(camera);
        if (level < 0)
        {
            return;
        }

        // Камера уровня k смотрит на парный портал. В её кадре парный портал
        // должен показывать то, что нарисовал уровень k+1. У самого глубокого
        // уровня источника нет, и проём заливается цветом заглушки.
        Texture content = level + 1 < _targets.Length ? _targets[level + 1] : null;
        _portal.exitPortal.SetViewTexture(content);
    }

    /// <summary>
    /// Снимает с квада парного портала текстуру, если там лежит один из наших
    /// таргетов. Чужую привязку не трогает: парный портал показывает свой вид,
    /// и обнулять его вслепую значило бы гасить его проём на кадр.
    /// </summary>
    private void ClearBindingOnExitPortal()
    {
        Portal exit = _portal != null ? _portal.exitPortal : null;
        if (exit == null || exit.ViewTexture == null)
        {
            return;
        }

        for (int i = 0; i < _targets.Length; i++)
        {
            if (ReferenceEquals(_targets[i], exit.ViewTexture))
            {
                exit.SetViewTexture(null);
                return;
            }
        }
    }

    /// <summary>Свой ли это уровень. Сравнение по ссылке, без аллокаций.</summary>
    private int IndexOf(Camera camera)
    {
        for (int i = 0; i < _cameras.Length; i++)
        {
            if (ReferenceEquals(_cameras[i], camera))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Портал не виден, если смотрит от наблюдателя или его проём вне пирамиды
    /// видимости. Второе проверяется по границам квада, а не по корню портала:
    /// корень лежит в плоскости проёма и уходит из пирамиды раньше, чем сам проём.
    /// </summary>
    private bool IsVisible(Camera viewer)
    {
        if (!_portal.cullWhenOffscreen)
        {
            return true;
        }

        if (PortalMath.SignedDistance(_portal.transform, viewer.transform.position) <= 0f)
        {
            return false;
        }

        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(viewer);
        return GeometryUtility.TestPlanesAABB(planes, _portal.screen.bounds);
    }

    private void EnsureCapacity(int levels, Camera viewer)
    {
        int width = Mathf.Max(1, viewer.pixelWidth / _portal.resolutionDivider);
        int height = Mathf.Max(1, viewer.pixelHeight / _portal.resolutionDivider);

        bool matches = _cameras.Length == levels
            && _targets.Length == levels
            && _targets.Length > 0
            && _targets[0] != null
            && _targets[0].width == width
            && _targets[0].height == height;

        if (matches)
        {
            return;
        }

        Release();

        _cameras = new Camera[levels];
        _targets = new RenderTexture[levels];

        for (int level = 0; level < levels; level++)
        {
            _targets[level] = CreateTarget(width, height, level);
            _cameras[level] = CreateCamera(viewer, level, _targets[level]);
        }

        RequestContentBuffers(width, height);
        Subscribe();
    }

    /// <summary>
    /// Просит у пайплайна глубину и векторы движения нулевого уровня. Это
    /// единственный публичный способ получить промежуточные буферы кадра, а не
    /// только итоговую картинку: пайплайн сам скопирует их в выданные здесь
    /// текстуры в конце своего кадра.
    /// </summary>
    private void RequestContentBuffers(int width, int height)
    {
        if (_cameras.Length == 0 || !_portal.writeContentDepth)
        {
            return;
        }

        _contentDepth = RTHandles.Alloc(
            width, height,
            colorFormat: GraphicsFormat.R32_SFloat,
            filterMode: FilterMode.Point,
            wrapMode: TextureWrapMode.Clamp,
            name: _portal.name + "_ContentDepth");

        _contentMotion = RTHandles.Alloc(
            width, height,
            colorFormat: GraphicsFormat.R16G16_SFloat,
            filterMode: FilterMode.Point,
            wrapMode: TextureWrapMode.Clamp,
            name: _portal.name + "_ContentMotion");

        if (!_cameras[0].TryGetComponent(out HDAdditionalCameraData data))
        {
            return;
        }

        var builder = new AOVRequestBuilder();
        builder.Add(
            AOVRequest.NewDefault(),
            AllocateContentBuffer,
            null,
            new[] { AOVBuffers.DepthStencil, AOVBuffers.MotionVectors },
            (cmd, buffers, properties) => { });

        data.SetAOVRequests(builder.Build());
    }

    private RTHandle AllocateContentBuffer(AOVBuffers bufferId)
    {
        switch (bufferId)
        {
            case AOVBuffers.DepthStencil:
                return _contentDepth;
            case AOVBuffers.MotionVectors:
                return _contentMotion;
            default:
                return null;
        }
    }

    private void ReleaseContentBuffers()
    {
        if (_cameras.Length > 0
            && _cameras[0] != null
            && _cameras[0].TryGetComponent(out HDAdditionalCameraData data))
        {
            data.SetAOVRequests(null);
        }

        if (_contentDepth != null)
        {
            RTHandles.Release(_contentDepth);
            _contentDepth = null;
        }

        if (_contentMotion != null)
        {
            RTHandles.Release(_contentMotion);
            _contentMotion = null;
        }
    }

    private RenderTexture CreateTarget(int width, int height, int level)
    {
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf)
        {
            name = _portal.name + "_View_" + level,
            antiAliasing = 1,
            useMipMap = false,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        target.Create();
        return target;
    }

    /// <summary>
    /// Виртуальная камера рендерит без пост-обработки: экспозиция, тонемап, блум
    /// и временное сглаживание выполняются главной камерой по всему кадру разом,
    /// включая композит портала. Иначе в проёме была бы своя экспозиция, свой
    /// тонемап и своя история кадров, и проём читался бы как экран, а не как проём.
    /// </summary>
    private Camera CreateCamera(Camera viewer, int level, RenderTexture target)
    {
        // Без hideFlags: объект живёт только в рантайме и должен выгружаться
        // обычным порядком. HideAndDontSave снимает его с автоматической
        // очистки, и камера переживает выгрузку сцены вместе со своим таргетом.
        // Заодно её видно в иерархии во время игры, что помогает при разборе.
        var cameraObject = new GameObject(_portal.name + "_Camera_" + level);
        cameraObject.transform.SetParent(_portal.transform, false);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = true;
        camera.targetTexture = target;
        camera.cullingMask = viewer.cullingMask;
        camera.clearFlags = CameraClearFlags.Skybox;

        // Больший уровень — меньшая глубина, поэтому самый глубокий рисуется первым.
        camera.depth = viewer.depth - DepthOffset - level;

        HDAdditionalCameraData data = cameraObject.AddComponent<HDAdditionalCameraData>();
        data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
        data.hasPersistentHistory = true;
        data.customRenderingSettings = true;

        Override(data, FrameSettingsField.Postprocess, false);

        // Без контроля экспозиции HDRP отдаёт нейтральный множитель, и в таргет
        // попадают абсолютные значения яркости. Приведением к экспозиции главной
        // камеры занимается шейдер квада: два независимых автоматических
        // экспонирования разошлись бы, и проём отличался бы яркостью от окружения.
        Override(data, FrameSettingsField.ExposureControl, false);

        CopyLens(viewer, camera);
        return camera;
    }

    private static void Override(HDAdditionalCameraData data, FrameSettingsField field, bool value)
    {
        data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
        data.renderingPathCustomFrameSettings.SetEnabled(field, value);
    }

    /// <summary>
    /// Ставит виртуальной камере проекцию наблюдателя, срезанную плоскостью выхода
    /// и сдвинутую тем же субпиксельным джиттером.
    ///
    /// Косая ближняя плоскость отсекает всё, что стоит между парным порталом и
    /// виртуальной камерой. Без неё в проём попадает геометрия позади выхода:
    /// стены, арки, косяки — всё, мимо чего наблюдатель на самом деле уже прошёл.
    ///
    /// Джиттер копируется у наблюдателя, потому что содержимое портала должно
    /// дрожать в тех же долях пикселя, что и остальная геометрия. Иначе временное
    /// сглаживание главной камеры получает по проёму постоянную выборку и
    /// обрабатывает его иначе, чем всё вокруг.
    ///
    /// Порядок важен: поза камере уже выставлена, потому что плоскость считается
    /// в её пространстве через worldToCameraMatrix.
    /// </summary>
    private void ApplyProjection(Camera viewer, Camera camera)
    {
        CopyLens(viewer, camera);

        Transform exit = _portal.exitPortal.transform;

        // Косая плоскость двигает ближнюю плоскость камеры на себя, поэтому она
        // обязана остаться дальше ближней плоскости: иначе матрица вырождается
        // и в таргет попадает мусор.
        //
        // В последние сантиметры перед переходом виртуальная камера подходит к
        // плоскости выхода вплотную, и штатной ближней плоскости уже не хватает.
        // Просто отключить там отсечение нельзя: всё, что стоит за выходом,
        // влетает в кадр разом, и за кадр до перехода игрок видит вспышку.
        // Вместо этого ближняя плоскость виртуальной камеры уменьшается — она
        // наша, и точность глубины на последних сантиметрах роли не играет.
        // Расстояние от камеры до самой плоскости отсечения, с учётом сдвига.
        float standoff = Mathf.Abs(PortalMath.SignedDistance(exit, camera.transform.position))
            + _portal.clippingOffset;

        // Косое отсечение применимо, только пока плоскость заведомо дальше
        // ближней плоскости камеры. Проходя ровно через камеру, матрица уходит
        // в бесконечность. Ближняя плоскость у виртуальной камеры своя и
        // маленькая (см. CopyLens), поэтому порог срабатывает только в последние
        // миллиметры перед переходом.
        bool obliqueUsable = standoff > camera.nearClipPlane * 2f;

        // Обязательно: CalculateObliqueMatrix строит результат поверх текущей
        // матрицы проекции, а там лежит косая матрица прошлого кадра. Без сброса
        // наклон накапливался бы кадр за кадром, и вид схлопнулся бы в полосу.
        camera.ResetProjectionMatrix();

        Matrix4x4 projection = camera.projectionMatrix;
        if (obliqueUsable)
        {
            Vector4 plane = PortalMath.CameraSpacePlane(
                camera, exit.position, exit.forward, _portal.clippingOffset);
            Matrix4x4 oblique = camera.CalculateObliqueMatrix(plane);

            // Последняя проверка перед отправкой на видеокарту: одно
            // нечисловое значение в матрице роняет приложение целиком.
            if (IsFinite(oblique))
            {
                projection = oblique;
            }
        }

        // taaJitter уже поделён на размер экрана в компонентах z и w, что и
        // требуется для сдвига матрицы проекции.
        Vector4 jitter = HDCamera.GetOrCreate(viewer).taaJitter;
        projection.m02 += jitter.z;
        projection.m12 += jitter.w;

        camera.projectionMatrix = projection;
    }

    /// <summary>
    /// Все ли элементы матрицы — числа. Косая матрица вырождается, когда
    /// плоскость отсечения проходит через камеру, и отправка такой матрицы на
    /// видеокарту роняет приложение без внятного сообщения.
    /// </summary>
    private static bool IsFinite(Matrix4x4 matrix)
    {
        for (int i = 0; i < 16; i++)
        {
            float value = matrix[i];
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Копирует объектив наблюдателя, кроме ближней плоскости.
    ///
    /// Ближняя плоскость у виртуальной камеры своя и заметно меньше. Косое
    /// отсечение двигает её на плоскость выхода, а к ней камера подходит
    /// вплотную в последние сантиметры перед переходом. С ближней плоскостью
    /// наблюдателя отсечение пришлось бы там отключать, и всё, что стоит за
    /// выходом, влетало бы в кадр разом — за кадр до перехода игрок видел бы
    /// вспышку. Точность глубины на этих сантиметрах роли не играет.
    ///
    /// Значение выставляется один раз и дальше не меняется: правка ближней
    /// плоскости каждый кадр вместе с подменой матрицы проекции роняет
    /// приложение в драйвере.
    /// </summary>
    private static void CopyLens(Camera viewer, Camera camera)
    {
        camera.fieldOfView = viewer.fieldOfView;
        camera.aspect = viewer.aspect;
        camera.farClipPlane = viewer.farClipPlane;
        camera.nearClipPlane = Mathf.Min(viewer.nearClipPlane, MinimumNearClip);
    }
}
