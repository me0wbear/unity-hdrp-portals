using UnityEngine;

/// <summary>
/// Задаёт потолок числа одновременно рисуемых уровней рекурсии на всю сцену.
///
/// Потолок нужен потому, что каждый уровень — это отдельная камера и таргет
/// размером с экран. Четыре портала с глубиной 2 просят двенадцать уровней, а
/// это двенадцать экранных буферов в памяти видеокарты и двенадцать проходов
/// рендера в кадре.
///
/// Когда запрошено больше потолка, система режет глубину рекурсии, а не сами
/// порталы: лучше показать все проёмы мельче, чем часть проёмов чёрными.
/// Порталы, до которых бюджет не дошёл, заполняются цветом заглушки — если в
/// сцене видны чёрные прямоугольники вместо вида, потолок стоит поднять.
///
/// Компонент нужен только затем, чтобы менять значение из сцены: сам потолок
/// живёт в статическом поле <see cref="PortalSystem.Budget"/> и без него
/// правится только из кода. Достаточно одного на сцену.
/// </summary>
[DisallowMultipleComponent]
public sealed class PortalBudget : MonoBehaviour
{
    public static void AllocateVisibleLevels(int[] wanted, int totalBudget, int[] output)
        => AllocateVisibleLevels(wanted, null, wanted.Length, totalBudget, output, null);

    public static void AllocateVisibleLevels(int[] wanted, float[] coverage, int count,
        int totalBudget, int[] output, int[] order)
    {
        if (wanted == null || output == null) throw new System.ArgumentNullException();
        if (count < 0 || count > wanted.Length || output.Length < count
            || (coverage != null && (coverage.Length < count || order == null || order.Length < count)))
            throw new System.ArgumentException("Insufficient caller-owned scheduling buffers.");
        System.Array.Clear(output, 0, output.Length);
        // Устойчивая сортировка индексов не меняет реестр порталов и не создаёт делегатов.
        for (int i = 0; i < count; i++)
        {
            if (coverage == null) break;
            int position = i;
            while (position > 0 && Priority(coverage[i]) > Priority(coverage[order[position - 1]]))
            {
                order[position] = order[position - 1];
                position--;
            }
            order[position] = i;
        }
        // Сначала все roots, затем по одному дополнительному уровню на проход.
        bool progress = true;
        while (totalBudget > 0 && progress)
        {
            progress = false;
            for (int i = 0; i < count && totalBudget > 0; i++)
            {
                int index = coverage == null ? i : order[i];
                if (output[index] >= wanted[index]) continue;
                output[index]++;
                totalBudget--;
                progress = true;
            }
        }
    }

    private static float Priority(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp01(value);

    [SerializeField]
    [Tooltip("Сколько уровней рекурсии разрешено рисовать всем порталам сцены вместе. "
        + "Каждый уровень стоит одной камеры и одного таргета размером с экран.")]
    [Min(1)] private int levels = 8;

    private void Awake()
    {
        PortalSystem.Budget = levels;
    }

    private void OnValidate()
    {
        // В режиме игры значение подхватывается сразу: подбирать потолок удобнее,
        // глядя на сцену, чем перезапуская её ради каждой пробы.
        if (Application.isPlaying)
        {
            PortalSystem.Budget = levels;
        }
    }
}
