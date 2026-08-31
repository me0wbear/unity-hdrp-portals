using System.Collections.Generic;
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

    /// <summary>Прямоугольник, покрывающий весь кадр: xy — угол, zw — размер.</summary>
    private static readonly Vector4 FullRect = new Vector4(0f, 0f, 1f, 1f);

    // Прямоугольник кадра каждой живой камеры уровня. Система читает его перед
    // рендером камеры и сообщает шейдерам глобальным параметром: квад выбирает
    // вид по экранным координатам, и камере, рисующей только часть кадра, нужно
    // знать, какую именно.
    private static readonly Dictionary<Camera, Vector4> ViewRects =
        new Dictionary<Camera, Vector4>();

    /// <summary>Прямоугольник кадра камеры уровня; полный кадр для остальных камер.</summary>
    internal static Vector4 ViewRectFor(Camera camera)
    {
        return ViewRects.TryGetValue(camera, out Vector4 rect) ? rect : FullRect;
    }

    /// <summary>Сброс статики при запуске; вызывается из PortalSystem.</summary>
    internal static void ResetStatics()
    {
        ViewRects.Clear();
    }

    private readonly Portal _portal;

    private Camera[] _cameras = System.Array.Empty<Camera>();
    private RenderTexture[] _targets = System.Array.Empty<RenderTexture>();

    /// <summary>
    /// Прямоугольники кадра по уровням: какую область кадра наблюдателя рисует
    /// каждый уровень. Полный кадр, когда ограничение области выключено.
    /// </summary>
    private Vector4[] _levelRects = System.Array.Empty<Vector4>();

    private bool _subscribed;
    private bool _reported;

    /// <summary>
    /// Сколько уровней занято в этом кадре. Ёмкость массивов может быть больше:
    /// лишние уровни выключены, но живы.
    /// </summary>
    private int _levels;

    /// <summary>
    /// Рисовался ли уровень в прошлом кадре. По этому и определяется, что его
    /// поза сменилась скачком и историю кадров надо выбросить.
    /// </summary>
    private bool[] _rendered = System.Array.Empty<bool>();

    /// <summary>Рендерит ли портал прямо сейчас. Приостановленный бюджет не занимает.</summary>
    private bool _active;

    // Глубина и векторы движения снимаются только с нулевого уровня: именно он
    // виден игроку напрямую, и только его пиксели попадают в буферы главной
    // камеры. Уровни глубже видны уже внутри чужого таргета, и своя глубина им
    // не нужна.
    private RTHandle _contentDepth;
    private RenderTexture _contentDepthTexture;

    /// <summary>Глубина того, что видно сквозь проём, в кодировке устройства.</summary>
    public RTHandle ContentDepth => _active ? _contentDepth : null;    /// <summary>
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
    public int LevelCount => _active ? _levels : 0;

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

        Subscribe();
        EnsureCapacity(levels, viewer);
        _active = true;

        // Запас проекции следа квада: пара пикселей на дрожание выборки и
        // билинейную фильтрацию по краю области.
        Vector2 padding = new Vector2(
            2f / Mathf.Max(viewer.pixelWidth, 1),
            2f / Mathf.Max(viewer.pixelHeight, 1));

        // Нулевой уровень рисует область кадра, которую занимает проём в кадре
        // наблюдателя. Каждый следующий уровень — след вложенного квада внутри
        // области своего родителя.
        Vector4 rect = _portal.restrictViewToOpening
            ? QuadRect(
                viewer.nonJitteredProjectionMatrix * viewer.worldToCameraMatrix,
                _portal.screen.transform,
                padding)
            : FullRect;

        for (int level = 0; level < _cameras.Length; level++)
        {
            Camera camera = _cameras[level];
            if (camera == null)
            {
                continue;
            }

            // Уровни сверх занятых в этом кадре выключаются, но не разрушаются.
            if (level >= _levels)
            {
                camera.enabled = false;
                _rendered[level] = false;
                continue;
            }

            Matrix4x4 pose = PortalMath.EntranceToExit(
                    _portal.transform, _portal.exitPortal.transform, level + 1)
                * viewer.transform.localToWorldMatrix;

            camera.transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);

            // Пустой след возможен только у уровней глубже нулевого — у нулевого
            // видимость уже проверена, а пустота отсекла бы рекурсию ниже.
            _levelRects[level] = rect;
            ViewRects[camera] = rect;
            camera.rect = new Rect(rect.x, rect.y, rect.z, rect.w);

            Matrix4x4 fullProjection = ApplyProjection(viewer, camera, rect);

            // Уровень, который в прошлом кадре не рисовался, приходит со
            // сведениями о прошлом кадре от своей прежней позы, а она может быть
            // где угодно: за спиной, в другой комнате, на другом конце уровня.
            // Пайплайн принимает разницу поз за настоящее движение и выдаёт
            // огромные векторы движения. Отсюда они попадают в буфер главной
            // камеры и размазывают проём: размытие в движении честно мажет по
            // тому, что ему дали. Сам HDRP историю сбрасывает только на первом
            // кадре камеры и на смене режима сглаживания, скачок позы он не ловит.
            // Сброс идёт после переустановки позы, иначе выброшенная история
            // тут же собралась бы заново от старой.
            if (!_rendered[level])
            {
                camera.enabled = true;
                HDCamera.GetOrCreate(camera).Reset();
                _rendered[level] = true;
            }

            if (level == 0)
            {
                // Буфер глубины содержимого хранит аппаратную глубину: диапазон Z
                // и направление Y должны совпадать с проекцией, использованной
                // при рендере в RT.
                ContentInverseProjection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true).inverse;
            }

            // Уровень глубже рисовать имеет смысл, только если из этой камеры
            // виден хотя бы один квад, на котором появится его результат. При
            // рабочем косом отсечении квад выхода срезан самой проекцией, а квад
            // входа попадает в кадр, лишь когда пара стоит лицом к лицу. Если не
            // виден ни один, рекурсия заканчивается здесь: более глубокие камеры
            // выключаются и бюджета системы не занимают, хотя сцена этого уровня
            // без них не отличается ни одним пикселем. Заодно считается след
            // видимых квадов — он же область кадра следующего уровня.
            if (level + 1 < _levels)
            {
                if (TryDeeperRect(camera, fullProjection, rect, padding, out Vector4 deeper))
                {
                    rect = _portal.restrictViewToOpening ? deeper : FullRect;
                }
                else if (_portal.cullWhenOffscreen)
                {
                    _levels = level + 1;
                }
                else
                {
                    rect = FullRect;
                }
            }
        }
    }

    /// <summary>
    /// Помечает историю кадров всех уровней недействительной: в следующем кадре
    /// она соберётся заново от новых поз. Нужно на телепорте наблюдателя —
    /// виртуальные камеры повторяют его позу, значит прыгают вместе с ним.
    /// </summary>
    public void ResetHistory()
    {
        for (int i = 0; i < _rendered.Length; i++)
        {
            _rendered[i] = false;
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
        _levels = 0;

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

            // Пока портал был вне видимости, наблюдатель успел уйти. История
            // кадров уровня относится к прежней позе, и после возвращения её
            // надо собирать заново, а не продолжать.
            _rendered[i] = false;
        }

        // Привязки текстур намеренно остаются как были. Таргеты при приостановке
        // живы, отвязывать их незачем, а обнуление дало бы чёрную вспышку в тот
        // кадр, когда портал вернётся в поле зрения. Снимает привязки только
        // Release, вместе с самими таргетами.
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
                ViewRects.Remove(_cameras[i]);
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
        _rendered = System.Array.Empty<bool>();
        _levelRects = System.Array.Empty<Vector4>();
        _levels = 0;
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
            Vector4 viewRect = _levelRects.Length > 0 ? _levelRects[0] : FullRect;
            _portal.SetViewTexture(_targets[0], viewRect);
            _portal.SetContentBuffers(_contentDepth, ContentInverseProjection, viewRect);
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
        bool hasDeeper = level + 1 < _levels;
        Texture content = hasDeeper ? _targets[level + 1] : null;
        Vector4 contentRect = hasDeeper ? _levelRects[level + 1] : FullRect;
        _portal.exitPortal.SetViewTexture(content, contentRect);

        // Свой квад получает то же самое, и это не перестраховка. Когда пара
        // стоит лицом к лицу, камера уровня k смотрит из-за парного портала
        // назад и видит собственный квад этого портала. На нём в этот момент
        // висит таргет уровня k — ровно та текстура, в которую эта камера сейчас
        // рисует. Видеокарта на чтение из текстуры, открытой на запись, отвечает
        // чернотой, и проём заливается ею целиком.
        //
        // Порядок делает остальное: уровни рисуются от глубокого к нулевому, а
        // наблюдатель последним, и его событие возвращает на квад таргет нулевого
        // уровня. То есть подмена живёт ровно те кадры, пока считается рекурсия.
        _portal.SetViewTexture(content, contentRect);
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

    /// <summary>
    /// Буфер плоскостей пирамиды видимости для проверки уровней. Один на рендер
    /// и переиспользуется, чтобы не выделять массив на каждый уровень каждый кадр.
    /// </summary>
    private readonly Plane[] _levelPlanes = new Plane[6];

    /// <summary>
    /// Считает след квадов, на которых появится результат более глубокого
    /// уровня, в кадре камеры уровня. Содержимое следующего уровня кладётся на
    /// квады обоих порталов пары (см. <see cref="OnBeginCameraRendering"/>),
    /// поэтому берётся объединение их следов, пересечённое с областью самого
    /// уровня: за её пределами он ничего не рисует. Пустой след означает, что
    /// более глубокий уровень не виден и не нужен.
    ///
    /// Видимость проверяется по настоящей пирамиде камеры — с суженной и косой
    /// проекцией, — а след считается полной проекцией: прямоугольники всех
    /// уровней живут в долях кадра наблюдателя, чтобы выборка по экранным
    /// координатам оставалась сквозной для всей цепочки.
    /// </summary>
    private bool TryDeeperRect(
        Camera levelCamera,
        Matrix4x4 fullProjection,
        Vector4 levelRect,
        Vector2 padding,
        out Vector4 rect)
    {
        rect = default;

        GeometryUtility.CalculateFrustumPlanes(levelCamera, _levelPlanes);
        Matrix4x4 viewProjection = fullProjection * levelCamera.worldToCameraMatrix;

        bool found = false;
        if (QuadVisible(_portal, levelCamera, _levelPlanes))
        {
            rect = QuadRect(viewProjection, _portal.screen.transform, padding);
            found = true;
        }

        if (_portal.exitPortal != null
            && QuadVisible(_portal.exitPortal, levelCamera, _levelPlanes))
        {
            Vector4 exitRect = QuadRect(
                viewProjection, _portal.exitPortal.screen.transform, padding);
            rect = found ? UnionRects(rect, exitRect) : exitRect;
            found = true;
        }

        if (!found)
        {
            return false;
        }

        rect = IntersectRects(rect, levelRect);
        return rect.z > 0f && rect.w > 0f;
    }

    /// <summary>
    /// След квада в кадре: проекция четырёх углов с запасом
    /// <paramref name="padding"/>, прижатая к границам кадра. Угол за камерой
    /// делает след полным кадром: спроецировать его нельзя, а ошибиться в
    /// тесную сторону значило бы срезать видимое. Так происходит вплотную к
    /// переходу, и ограничение области там само отключается.
    /// </summary>
    private static Vector4 QuadRect(Matrix4x4 viewProjection, Transform quad, Vector2 padding)
    {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);

        for (int corner = 0; corner < 4; corner++)
        {
            Vector3 local = new Vector3(
                (corner & 1) == 0 ? -0.5f : 0.5f,
                (corner & 2) == 0 ? -0.5f : 0.5f,
                0f);

            Vector3 world = quad.TransformPoint(local);
            Vector4 clip = viewProjection * new Vector4(world.x, world.y, world.z, 1f);

            if (clip.w <= 1e-4f)
            {
                return FullRect;
            }

            Vector2 point = new Vector2(
                clip.x / clip.w * 0.5f + 0.5f,
                clip.y / clip.w * 0.5f + 0.5f);

            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        min = Vector2.Max(min - padding, Vector2.zero);
        max = Vector2.Min(max + padding, Vector2.one);
        return new Vector4(min.x, min.y, max.x - min.x, max.y - min.y);
    }

    private static Vector4 UnionRects(Vector4 a, Vector4 b)
    {
        float minX = Mathf.Min(a.x, b.x);
        float minY = Mathf.Min(a.y, b.y);
        float maxX = Mathf.Max(a.x + a.z, b.x + b.z);
        float maxY = Mathf.Max(a.y + a.w, b.y + b.w);
        return new Vector4(minX, minY, maxX - minX, maxY - minY);
    }

    private static Vector4 IntersectRects(Vector4 a, Vector4 b)
    {
        float minX = Mathf.Max(a.x, b.x);
        float minY = Mathf.Max(a.y, b.y);
        float maxX = Mathf.Min(a.x + a.z, b.x + b.z);
        float maxY = Mathf.Min(a.y + a.w, b.y + b.w);
        return new Vector4(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Квад портала стоит лицом к камере уровня и попадает в её пирамиду
    /// видимости. Пирамида считается по текущей матрице проекции, то есть с
    /// косой ближней плоскостью: квад выхода, срезанный ею, отсеивается здесь
    /// сам, а вблизи перехода, когда косое отсечение выключено, остаётся видимым.
    /// </summary>
    private static bool QuadVisible(Portal portal, Camera levelCamera, Plane[] planes)
    {
        if (portal == null || portal.screen == null)
        {
            return false;
        }

        if (PortalMath.SignedDistance(portal.transform, levelCamera.transform.position) <= 0f)
        {
            return false;
        }

        return GeometryUtility.TestPlanesAABB(planes, portal.screen.bounds);
    }

    private void EnsureCapacity(int levels, Camera viewer)
    {
        int width = Mathf.Max(1, viewer.pixelWidth / _portal.resolutionDivider);
        int height = Mathf.Max(1, viewer.pixelHeight / _portal.resolutionDivider);

        // Ёмкости достаточно, если уровней в ней не меньше запрошенного. Именно
        // не меньше, а не ровно столько: число уровней меняется каждый раз, когда
        // бюджет системы перераспределяется между порталами, а хватает для этого
        // одного мигания видимости соседнего проёма. Пересоздание камер на каждое
        // такое изменение означало бы, что камеры рождаются заново чуть ли не
        // каждый кадр, а у новорождённой камеры нет истории кадров и её векторы
        // движения — мусор. Лишние уровни просто выключены, а памяти под них
        // отведено ровно столько, сколько портал запросил глубиной рекурсии.
        bool matches = _cameras.Length >= levels
            && _targets.Length == _cameras.Length
            && _targets.Length > 0
            && _targets[0] != null
            && _targets[0].width == width
            && _targets[0].height == height;

        if (matches)
        {
            _levels = levels;
            return;
        }

        Release();

        _cameras = new Camera[levels];
        _targets = new RenderTexture[levels];
        _rendered = new bool[levels];
        _levelRects = new Vector4[levels];
        _levels = levels;

        for (int level = 0; level < levels; level++)
        {
            _targets[level] = CreateTarget(width, height, level);
            _cameras[level] = CreateCamera(viewer, level, _targets[level]);
        }

        RequestContentBuffers(width, height);
        Subscribe();
    }

    /// <summary>
    /// Готовит текстуру глубины содержимого и подписывает нулевой уровень на её
    /// заполнение. Глубина снимается копией с уже посчитанного кадра виртуальной
    /// камеры проходом <see cref="PortalContentDepthCopyPass"/>. Запрос AOV для
    /// этого не годится: HDRP выполняет для каждого запроса AOV отдельный полный
    /// рендер камеры, то есть сцена нулевого уровня считалась бы дважды за кадр.
    /// </summary>
    private void RequestContentBuffers(int width, int height)
    {
        if (_cameras.Length == 0 || !_portal.writeContentDepth)
        {
            return;
        }

        // Текстуры создаются свои и оборачиваются, а не просятся у глобальной
        // системы RTHandle. Той системой владеет пайплайн и уничтожает её при
        // выгрузке; выданные ею дескрипторы переживали этот момент и роняли
        // приложение уже после того, как все замеры записаны. Своей текстурой
        // владеем сами и убираем её обычным порядком.
        _contentDepthTexture = CreateContentTexture(
            width, height, GraphicsFormat.R32_SFloat, _portal.name + "_ContentDepth");
        _contentDepth = RTHandles.Alloc(_contentDepthTexture);

        PortalContentDepthCopyPass.Register(_cameras[0], this);
    }

    /// <summary>Текстура глубины содержимого; для прохода копирования.</summary>
    internal RTHandle ContentDepthTarget => _contentDepth;

    /// <summary>
    /// Пиксельная область текстуры глубины, которую занимает кадр нулевого
    /// уровня. Берётся у самой камеры: пайплайн вычисляет свой вьюпорт из того
    /// же прямоугольника, и копия обязана лечь в те же пиксели, что и цвет.
    /// </summary>
    internal Rect ContentCopyViewport
    {
        get
        {
            return _cameras.Length > 0 && _cameras[0] != null
                ? _cameras[0].pixelRect
                : new Rect(0f, 0f, 0f, 0f);
        }
    }

    private static RenderTexture CreateContentTexture(
        int width, int height, GraphicsFormat format, string name)
    {
        var texture = new RenderTexture(width, height, 0, format)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false
        };

        texture.Create();
        return texture;
    }

    /// <summary>
    /// Снимает обёртку и уничтожает саму текстуру. Обёртка ничем не владеет,
    /// поэтому порядок здесь безопасен в любом случае.
    /// </summary>
    private static void ReleaseContentTexture(ref RTHandle handle, ref RenderTexture texture)
    {
        if (handle != null)
        {
            RTHandles.Release(handle);
            handle = null;
        }

        if (texture != null)
        {
            Object.Destroy(texture);
            texture = null;
        }
    }

    private void ReleaseContentBuffers()
    {
        if (_cameras.Length > 0 && _cameras[0] != null)
        {
            PortalContentDepthCopyPass.Unregister(_cameras[0]);
        }

        ReleaseContentTexture(ref _contentDepth, ref _contentDepthTexture);
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

        // Экранные эффекты, читающие глубину, по умолчанию выключены. Проекция
        // виртуальной камеры косая, а HDRP линеаризует глубину для них через
        // LinearEyeDepth, которая косую проекцию не поддерживает: точки
        // восстанавливаются на неверных расстояниях, затенение ложится грязью в
        // стыки поверхностей, и вид в проёме отличается от вида после перехода.
        // Поле портала возвращает эффекты осознанно; применяется при создании
        // камер, как и writeContentDepth.
        if (!_portal.screenSpaceEffectsInView)
        {
            Override(data, FrameSettingsField.SSAO, false);
            Override(data, FrameSettingsField.SSR, false);
            Override(data, FrameSettingsField.TransparentSSR, false);
            Override(data, FrameSettingsField.SSGI, false);
            Override(data, FrameSettingsField.ContactShadows, false);

            // Векторы движения виртуальной камеры никто не потребляет: композит
            // намеренно оставляет проёму векторы самого квада, посчитанные
            // главной камерой, сглаживание и пост-обработка на виртуальных
            // камерах выключены. Их препасс — чистый расход на каждый уровень.
            // Возвращаются вместе с экранными эффектами: отражениям нужна
            // репроекция по движению.
            Override(data, FrameSettingsField.MotionVectors, false);
        }

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
    /// <summary>
    /// Возвращает полную (несуженную) матрицу проекции камеры: по ней считаются
    /// следы квадов, потому что прямоугольники уровней живут в долях полного
    /// кадра наблюдателя.
    /// </summary>
    private Matrix4x4 ApplyProjection(Camera viewer, Camera camera, Vector4 rect)
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

        Matrix4x4 fullProjection = camera.projectionMatrix;

        // Сужение до области проёма идёт до косого отсечения: CalculateObliqueMatrix
        // читает текущую матрицу камеры, поэтому суженная назначается ей сразу.
        // Для полного кадра сужение вырождается в тождество, и путь один на оба
        // режима.
        Matrix4x4 projection = RestrictProjection(fullProjection, rect);
        camera.projectionMatrix = projection;

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

        // Матрица без дрожания сообщается отдельно и до того, как дрожание
        // добавлено. Векторы движения считаются по разнице матриц соседних
        // кадров, и брать для этого матрицу с дрожанием нельзя: дрожание меняется
        // каждый кадр по своей последовательности, разница матриц принимает его
        // за движение камеры, и в буфер уходит смещение размером с дрожание в
        // случайную сторону. Временное сглаживание послушно собирает кадр по
        // этому смещению и превращает вид в проёме в мыло, пока всё вокруг
        // остаётся резким. Поле для того и заведено, чтобы разделить эти две
        // матрицы; без него пайплайн считает поданную ему матрицу нежитерованной.
        camera.nonJitteredProjectionMatrix = projection;

        // Множитель два обязателен. Пайплайн применяет дрожание, сдвигая обе
        // границы пирамиды видимости на одну и ту же величину: для матрицы
        // проекции это даёт сдвиг элемента m02 на удвоенное значение, потому
        // что он равен сумме границ, делённой на их разность. Компоненты z и w
        // taaJitter уже поделены на размер экрана, но не удвоены. С одинарным
        // значением содержимое проёма дрожит вдвое слабее окружения, и
        // временное сглаживание собирает его иначе, чем остальной кадр.
        //
        // Деление на размер области переводит сдвиг из долей полного кадра в
        // доли суженного: содержимое должно дрожать на те же пиксели экрана,
        // а единица NDC суженной проекции покрывает лишь часть кадра.
        Vector4 jitter = HDCamera.GetOrCreate(viewer).taaJitter;
        projection.m02 += 2f * jitter.z / rect.z;
        projection.m12 += 2f * jitter.w / rect.w;

        camera.projectionMatrix = projection;
        return fullProjection;
    }

    /// <summary>
    /// Сужает матрицу проекции до прямоугольника кадра: область растягивается
    /// на всю пирамиду камеры. Плотность пикселей при вьюпорте того же
    /// прямоугольника не меняется — рисуются ровно те же пиксели, что рисовал
    /// бы полный кадр, просто без остальных. Для полного прямоугольника —
    /// тождество.
    /// </summary>
    private static Matrix4x4 RestrictProjection(Matrix4x4 projection, Vector4 rect)
    {
        // Центр области в NDC и её размер как доля полукадра.
        float centreX = rect.x * 2f + rect.z - 1f;
        float centreY = rect.y * 2f + rect.w - 1f;

        for (int column = 0; column < 4; column++)
        {
            projection[0, column] =
                (projection[0, column] - centreX * projection[3, column]) / rect.z;
            projection[1, column] =
                (projection[1, column] - centreY * projection[3, column]) / rect.w;
        }

        return projection;
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
