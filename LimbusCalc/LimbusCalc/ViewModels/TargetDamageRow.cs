using System.Globalization;
using LimbusCalc.Calculation;

namespace LimbusCalc.ViewModels;

/// <summary>По какому столбцу таблицы распределения она отсортирована.</summary>
public enum TargetSortKey
{
    /// <summary>Порядок как в расчёте: основная цель, дальше по позициям.</summary>
    None,

    /// <summary>Название цели — по алфавиту.</summary>
    Title,

    /// <summary>Урон одной монеты.</summary>
    Coin,

    /// <summary>Итог по цели.</summary>
    Total,
}

/// <summary>Строка таблицы распределения: одна цель, по клетке на монету плюс итог.</summary>
public sealed class TargetDamageRow
{
    public required string Title { get; init; }

    /// <summary>Урон по монетам без Time Moratorium — как и в главном окне.</summary>
    public required IReadOnlyList<string> CoinDamage { get; init; }

    /// <summary>
    /// Тот же урон числами, для сортировки. Пусто там, где монета по цели не бьёт:
    /// это не ноль урона, а отсутствие удара, поэтому такие строки уходят вниз.
    /// </summary>
    public required IReadOnlyList<double?> CoinValues { get; init; }

    /// <summary>Сумма по монетам без Time Moratorium.</summary>
    public required double BaseTotal { get; init; }

    /// <summary>Множитель Time Moratorium этой цели; единица — моратория нет.</summary>
    public required double MoratoriumBuff { get; init; }

    /// <summary>Итог по цели с учётом Time Moratorium.</summary>
    public required double Total { get; init; }

    /// <summary>Мораторий как-то изменил урон по этой цели.</summary>
    public bool Affected => Total != BaseTotal;

    /// <summary>
    /// Все монеты, бьющие по этой цели, дали один и тот же множитель. Если нет,
    /// единого числа не существует, и равенство показывать нельзя: отношение итогов
    /// выглядело бы как множитель, которого никто не задавал.
    /// </summary>
    public required bool UniformBuff { get; init; }

    /// <summary>Показывать ли в итоговой клетке равенство вместо одного числа.</summary>
    public bool ShowEquation => Affected && UniformBuff;

    /// <summary>Итог задет мораторием, но разложить его равенством нельзя.</summary>
    public bool ShowPlainAffected => Affected && !UniformBuff;

    /// <summary>
    /// Первое число в клетке итога: при равенстве это его левая часть, иначе сразу итог.
    /// </summary>
    public string LeadingText => Format(ShowEquation ? BaseTotal : Total);

    public string BaseTotalText => Format(BaseTotal);

    public string BuffText => MoratoriumBuff.ToString("0.##", CultureInfo.InvariantCulture);

    public string TotalText => Format(Total);

    private static string Format(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Как называется основная цель каждой монеты.</summary>
    private const string MainTitle = "Main target";

    /// <summary>
    /// Раскладывает результат расчёта по целям. Строка — это враг, а не позиция в списке:
    /// подцели с одним названием на разных монетах попадают в одну строку, с разными —
    /// в разные. Монета, которая по этому врагу не бьёт, получает прочерк — это не ноль
    /// урона, а отсутствие удара.
    /// </summary>
    /// <param name="coinSubtargetTitles">
    /// Названия подцелей по монетам: внешний список — монеты, внутренний — позиции,
    /// нулевая из которых подцель 2. Безымянную подцель называем по номеру.
    /// </param>
    public static IReadOnlyList<TargetDamageRow> Build(
        IReadOnlyList<CoinBreakdown> coins,
        IReadOnlyList<IReadOnlyList<string>>? coinSubtargetTitles = null)
    {
        int targets = 0;

        foreach (CoinBreakdown coin in coins)
        {
            targets = Math.Max(targets, coin.TargetDamage.Count);
        }

        // Порядок строк: основная цель, дальше по позициям слева направо — так новый
        // враг встаёт туда, где по нему впервые ударили.
        List<string> order = [];
        HashSet<string> known = new(StringComparer.OrdinalIgnoreCase);

        for (int t = 0; t < targets; t++)
        {
            for (int c = 0; c < coins.Count; c++)
            {
                if (t >= coins[c].TargetDamage.Count)
                {
                    continue;
                }

                string title = TitleOf(c, t, coinSubtargetTitles);

                if (known.Add(title))
                {
                    order.Add(title);
                }
            }
        }

        List<TargetDamageRow> rows = [];

        foreach (string title in order)
        {
            List<string> cells = [];
            List<double?> values = [];
            double baseTotal = 0.0;
            double total = 0.0;
            double? sharedBuff = null;
            bool uniform = true;

            for (int c = 0; c < coins.Count; c++)
            {
                CoinBreakdown coin = coins[c];
                double coinBase = 0.0;
                double coinFinal = 0.0;
                bool hits = false;

                for (int t = 0; t < coin.TargetDamage.Count; t++)
                {
                    if (!string.Equals(
                            TitleOf(c, t, coinSubtargetTitles),
                            title,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    hits = true;
                    coinBase += coin.TargetDamage[t];
                    coinFinal += coin.TargetDamageFinal[t];

                    // Множитель этой монеты по этой цели. Берём заданный формулой, а не
                    // отношение итогов: округление вниз искажало бы его на мелком уроне.
                    double buff = t < coin.TargetMoratoriumBuff.Count
                        ? coin.TargetMoratoriumBuff[t]
                        : 1.0;

                    if (sharedBuff is null)
                    {
                        sharedBuff = buff;
                    }
                    else if (Math.Abs(sharedBuff.Value - buff) > 1e-6)
                    {
                        uniform = false;
                    }
                }

                cells.Add(hits ? Format(coinBase) : "—");
                values.Add(hits ? coinBase : null);
                baseTotal += coinBase;
                total += coinFinal;
            }

            rows.Add(new TargetDamageRow
            {
                Title = title,
                CoinDamage = cells,
                CoinValues = values,
                BaseTotal = baseTotal,
                MoratoriumBuff = sharedBuff ?? 1.0,
                UniformBuff = uniform,
                Total = total,
            });
        }

        return rows;
    }

    /// <summary>
    /// Переставляет строки по выбранному столбцу. Порядок устойчивый: строки с равными
    /// значениями остаются в том порядке, в каком стоят в расчёте.
    /// </summary>
    /// <param name="coinIndex">Какая монета, если сортируем по столбцу монеты.</param>
    public static IReadOnlyList<TargetDamageRow> Sort(
        IReadOnlyList<TargetDamageRow> rows,
        TargetSortKey key,
        int coinIndex,
        bool descending)
    {
        switch (key)
        {
            case TargetSortKey.Title:
                return [.. descending
                    ? rows.OrderByDescending(row => row.Title, StringComparer.OrdinalIgnoreCase)
                    : rows.OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase)];

            case TargetSortKey.Total:
                return [.. descending
                    ? rows.OrderByDescending(row => row.Total)
                    : rows.OrderBy(row => row.Total)];

            case TargetSortKey.Coin:
                // Цели, до которых монета не достаёт, всегда внизу: у них не нулевой
                // урон, а прочерк, и в ряду чисел ему места нет.
                bool Hits(TargetDamageRow row) =>
                    coinIndex >= 0
                    && coinIndex < row.CoinValues.Count
                    && row.CoinValues[coinIndex] is not null;

                IEnumerable<TargetDamageRow> hitting = rows.Where(Hits);

                hitting = descending
                    ? hitting.OrderByDescending(row => row.CoinValues[coinIndex]!.Value)
                    : hitting.OrderBy(row => row.CoinValues[coinIndex]!.Value);

                return [.. hitting, .. rows.Where(row => !Hits(row))];

            default:
                return rows;
        }
    }

    /// <summary>
    /// Как эта монета зовёт свою цель с этой позиции. Название и определяет строку:
    /// одинаковое — один враг на всех монетах, разное — разные строки.
    /// </summary>
    private static string TitleOf(
        int coin,
        int target,
        IReadOnlyList<IReadOnlyList<string>>? coinSubtargetTitles)
    {
        if (target == 0)
        {
            return MainTitle;
        }

        int index = target - 1;

        if (coinSubtargetTitles is not null && coin < coinSubtargetTitles.Count)
        {
            IReadOnlyList<string> titles = coinSubtargetTitles[coin];

            if (index < titles.Count && !string.IsNullOrWhiteSpace(titles[index]))
            {
                return titles[index].Trim();
            }
        }

        return $"Subtarget {target + 1}";
    }
}
