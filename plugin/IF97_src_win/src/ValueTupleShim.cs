/*
 * System.ValueTuple shim for .NET Framework 2.0.
 *
 * IF97's source targets netstandard2.0 and uses tuple syntax heavily:
 *
 *     static readonly ValueTuple<int, int, double>[] data = {
 *         (0, 0, -0.23872489924521E+03),
 *         ...
 *     };
 *
 * .NET 2.0 has no System.ValueTuple, and shipping the NuGet System.ValueTuple
 * assembly would not load on a CLR 2.0 host. Defining the types here makes the
 * compiler bind the tuple literals to them, so the original IF97 source can be
 * compiled completely unmodified.
 *
 * Only the shapes the source actually uses are provided (2, 3 and 4 elements).
 * Fields are public and mutable to match the real ValueTuple layout.
 */

namespace System
{
    public struct ValueTuple<T1, T2>
    {
        public T1 Item1;
        public T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }

        public override string ToString()
        {
            return "(" + Item1 + ", " + Item2 + ")";
        }
    }

    public struct ValueTuple<T1, T2, T3>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;

        public ValueTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }

        public override string ToString()
        {
            return "(" + Item1 + ", " + Item2 + ", " + Item3 + ")";
        }
    }

    public struct ValueTuple<T1, T2, T3, T4>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
        }

        public override string ToString()
        {
            return "(" + Item1 + ", " + Item2 + ", " + Item3 + ", " + Item4 + ")";
        }
    }
}
