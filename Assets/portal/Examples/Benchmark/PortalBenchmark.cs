using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Замер стоимости порталов в кадре. Прогоняет набор настроек, для каждой
/// считает время кадра и пишет отчёт.
///
/// Меряется время кадра целиком, а не время одного прохода: портал добавляет к
/// кадру целые камеры со своими проходами, и разница между «портал выключен» и
/// «портал включён» — это и есть его цена в том виде, в каком её платит игра.
/// </summary>
public sealed class PortalBenchmark : MonoBehaviour
{
    /// <summary>Кадры на прогрев. Первые кадры после смены настроек не считаются.</summary>
    private const int WarmupFrames = 60;

    /// <summary>Кадры на замер. Больше сотни смысла не имеет, разброс уже устоялся.</summary>
    private const int SampleFrames = 180;

    private readonly List<string> _report = new List<string>();

    private Portal[] _portals;
    private Transform _player;
    private Transform _head;

    private IEnumerator Start()
    {
        // Иначе меряется частота монитора, а не стоимость кадра.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;

        _player = GameObject.Find("Player").transform;
        _head = _player.Find("Head");
        _player.GetComponent<PortalDemoController>().enabled = false;

        _portals = Object.FindObjectsByType<Portal>(FindObjectsSortMode.None);

        // Все замеры с одной позы: игрок стоит в холодной комнате и смотрит
        // прямо в проём, то есть портал занимает заметную часть экрана и цена
        // его видна. Пара на рекурсию отсюда не видна и в замер не попадает.
        Pose(new Vector3(0f, 0.1f, -3.5f), 0f);

        _report.Add("resolution " + Screen.width + "x" + Screen.height);
        _report.Add("");
        _report.Add(Row("configuration", "median ms", "p95 ms", "fps by median"));
        _report.Add(Row("---", "---", "---", "---"));

        yield return Measure("portals off", () => SetAll(false, 0, 1));
        yield return Measure("1 pair, depth 0", () => SetAll(true, 0, 1));
        yield return Measure("1 pair, depth 1", () => SetAll(true, 1, 1));
        yield return Measure("1 pair, depth 2", () => SetAll(true, 2, 1));
        yield return Measure("1 pair, depth 4", () => SetAll(true, 4, 1));
        yield return Measure("1 pair, depth 2, divider 2", () => SetAll(true, 2, 2));
        yield return Measure("1 pair, depth 2, divider 4", () => SetAll(true, 2, 4));

        // Портал за спиной: показывает, сколько экономит отсечение по видимости.
        yield return Measure("depth 2, portal behind", () =>
        {
            SetAll(true, 2, 1);
            Pose(new Vector3(0f, 0.1f, -3.5f), 180f);
        });

        Pose(new Vector3(0f, 0.1f, -3.5f), 0f);

        // Без подмены глубины: цена самого композитного прохода.
        yield return Measure("depth 2, no content depth", () =>
        {
            SetAll(true, 2, 1);
            foreach (Portal portal in _portals)
            {
                portal.writeContentDepth = false;
            }
        });

        File.WriteAllLines("portal-benchmark.md", _report);
        Debug.Log("[Benchmark]\n" + string.Join("\n", _report));

        Application.Quit();
    }

    private void Pose(Vector3 position, float yaw)
    {
        _player.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        _head.localRotation = Quaternion.identity;
    }

    private void SetAll(bool enabled, int depth, int divider)
    {
        foreach (Portal portal in _portals)
        {
            portal.enabled = enabled;
            portal.recursionDepth = depth;
            portal.resolutionDivider = divider;
            portal.writeContentDepth = true;

            // Квад гасится вместе с порталом: иначе выключенный портал остаётся
            // в кадре прямоугольником с последним, что на нём лежало, и в замер
            // попадает его отрисовка.
            if (portal.screen != null)
            {
                portal.screen.enabled = enabled;
            }
        }
    }

    private IEnumerator Measure(string name, System.Action configure)
    {
        configure();

        for (int i = 0; i < WarmupFrames; i++)
        {
            yield return null;
        }

        var samples = new List<float>(SampleFrames);
        for (int i = 0; i < SampleFrames; i++)
        {
            yield return null;
            samples.Add(Time.unscaledDeltaTime * 1000f);
        }

        samples.Sort();
        float median = samples[samples.Count / 2];
        float p95 = samples[Mathf.Min(samples.Count - 1, (int)(samples.Count * 0.95f))];

        _report.Add(Row(
            name,
            median.ToString("F2"),
            p95.ToString("F2"),
            (1000f / median).ToString("F0")));

        Debug.Log("[Benchmark] " + name + " median=" + median.ToString("F2")
            + " p95=" + p95.ToString("F2"));
    }

    private static string Row(string a, string b, string c, string d)
    {
        var line = new StringBuilder("| ");
        line.Append(a).Append(" | ").Append(b).Append(" | ").Append(c).Append(" | ").Append(d).Append(" |");
        return line.ToString();
    }
}
