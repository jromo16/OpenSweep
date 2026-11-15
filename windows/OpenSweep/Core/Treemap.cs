using System.Windows;

namespace OpenSweep.Core;

/// <summary>
/// Treemap «cuadrado» (Bruls, Huizing y van Wijk): reparte un rectángulo en rectángulos proporcionales
/// a cada valor, lo más parecidos posible a un cuadrado para que se lean bien.
/// </summary>
public static class Treemap
{
    public static List<(T item, Rect rect)> Layout<T>(IEnumerable<(T item, double value)> source, Rect area)
    {
        var items = source.Where(i => i.value > 0).OrderByDescending(i => i.value).ToList();
        var result = new List<(T, Rect)>();
        if (items.Count == 0 || area.Width <= 0 || area.Height <= 0) return result;
        double total = items.Sum(i => i.value);
        double scale = area.Width * area.Height / total;   // valor -> área en píxeles
        var queue = items.Select(i => (i.item, area: i.value * scale)).ToList();
        Squarify(queue, 0, [], area, result);
        return result;
    }

    private static void Squarify<T>(List<(T item, double area)> items, int index, List<(T item, double area)> row, Rect rect, List<(T, Rect)> output)
    {
        while (index < items.Count)
        {
            double side = Math.Min(rect.Width, rect.Height);
            var next = items[index];
            if (row.Count == 0 || Worst(row, side) >= Worst([.. row, next], side))
            {
                row.Add(next);
                index++;
            }
            else
            {
                rect = PlaceRow(row, rect, output);
                row = [];
            }
        }
        if (row.Count > 0) PlaceRow(row, rect, output);
    }

    /// <summary>Peor relación de aspecto de una fila (cuanto más cerca de 1, más cuadrados).</summary>
    private static double Worst<T>(List<(T item, double area)> row, double side)
    {
        double sum = row.Sum(r => r.area);
        double max = row.Max(r => r.area), min = row.Min(r => r.area);
        double s2 = side * side, sum2 = sum * sum;
        return Math.Max(s2 * max / sum2, sum2 / (s2 * min));
    }

    private static Rect PlaceRow<T>(List<(T item, double area)> row, Rect rect, List<(T, Rect)> output)
    {
        double sum = row.Sum(r => r.area);
        if (rect.Width >= rect.Height)
        {
            // Columna a la izquierda
            double w = rect.Height > 0 ? sum / rect.Height : 0;
            double y = rect.Y;
            foreach (var (item, area) in row)
            {
                double h = w > 0 ? area / w : 0;
                output.Add((item, new Rect(rect.X, y, w, h)));
                y += h;
            }
            return new Rect(rect.X + w, rect.Y, Math.Max(0, rect.Width - w), rect.Height);
        }
        else
        {
            // Fila arriba
            double h = rect.Width > 0 ? sum / rect.Width : 0;
            double x = rect.X;
            foreach (var (item, area) in row)
            {
                double w = h > 0 ? area / h : 0;
                output.Add((item, new Rect(x, rect.Y, w, h)));
                x += w;
            }
            return new Rect(rect.X, rect.Y + h, rect.Width, Math.Max(0, rect.Height - h));
        }
    }
}
