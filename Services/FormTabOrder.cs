using System;
using System.Collections.Generic;
using System.Linq;

namespace KillerPDF.Services;

/// <summary>
/// Fork: the order Tab visits form fields - the way a form is read, row by row, left to right.
/// Fields whose vertical centers lie within half a field height of the row's first field share a
/// row, so a small check box beside a taller text field is not pushed after it.
/// </summary>
internal static class FormTabOrder
{
    internal static List<T> Order<T>(IEnumerable<T> items, Func<T, (double X, double Y, double W, double H)> bounds)
    {
        var byCenter = items
            .Select(item => (Item: item, Box: bounds(item)))
            .OrderBy(entry => entry.Box.Y + entry.Box.H / 2)
            .ThenBy(entry => entry.Box.X)
            .ToList();
        var ordered = new List<T>(byCenter.Count);
        int start = 0;
        while (start < byCenter.Count)
        {
            var first = byCenter[start].Box;
            double rowCenter = first.Y + first.H / 2;
            int end = start + 1;
            while (end < byCenter.Count)
            {
                var box = byCenter[end].Box;
                double tolerance = Math.Max(first.H, box.H) / 2;
                if (box.Y + box.H / 2 - rowCenter > tolerance) break;
                end++;
            }
            ordered.AddRange(byCenter.Skip(start).Take(end - start).OrderBy(entry => entry.Box.X).Select(entry => entry.Item));
            start = end;
        }
        return ordered;
    }
}
