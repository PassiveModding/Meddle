using System.Diagnostics;

namespace Meddle.Utils;

public static class RangeExtensions
{
    private static void AssertNotFromEnd(this Range range)
    {
        if (range.Start.IsFromEnd || range.End.IsFromEnd)
            throw new NotSupportedException("Range must not be from end");
    }

    public static bool OverlapsWith(this Range first, Range second)
    {
        first.AssertNotFromEnd();
        second.AssertNotFromEnd();

        return
            first.End.Value.CompareTo(second.Start.Value) > 0 &&
            second.End.Value.CompareTo(first.Start.Value) > 0;
    }

    public static Range UnionWith(this Range first, Range second)
    {
        Debug.Assert(first.OverlapsWith(second));
        return new Range(Math.Min(first.Start.Value, second.Start.Value),
                         Math.Max(first.End.Value, second.End.Value));
    }

    // https://stackoverflow.com/a/43120545
    public static IEnumerable<Range> AsConsolidated(this IEnumerable<Range> ranges)
    {
        var stack = new Stack<Range>();

        foreach (var range in ranges.OrderBy(i => i.Start.Value))
        {
            if (stack.Count == 0)
                stack.Push(range);
            else
            {
                var prev = stack.Peek();

                if (range.OverlapsWith(prev))
                {
                    stack.Pop();
                    stack.Push(range.UnionWith(prev));
                }
                else
                    stack.Push(range);
            }
        }

        return stack;
    }

    public static IEnumerable<int> GetEnumerator(this Range range)
    {
        range.AssertNotFromEnd();
        return Enumerable.Range(range.Start.Value, range.End.Value - range.Start.Value);
    }
}
