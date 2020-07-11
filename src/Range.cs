using System;
using System.Collections.Generic;

namespace MountSqlBackup
{
    internal class Range : IComparable<Range>
    {
        public long From;
        public long To;

        public Range(long point, long count)
        {
            From = point;
            To = point + count - 1;
        }

        public int CompareTo(Range other)
        {
            // If the ranges are overlapping, they are considered equal/matching
            if (From <= other.To && To >= other.From)
            {
                return 0;
            }

            // Since the ranges are not overlapping, we can compare either end
            return From.CompareTo(other.From);
        }

        public bool IsExplicitlyEqualsTo(Range range)
        {
            return From == range.From && To == range.To;
        }

        public override string ToString()
        {
            return From == To ? From.ToString() : $"{From}-{To} ({Count})";
        }

        public long Count => To - From + 1;
    }

    internal class IntersectedRange : Range
    {
        public RangeType RangeType { get; set; }

        public IntersectedRange(long point, long count,RangeType rangeType) : base(point, count)
        {
            RangeType = rangeType;
        }
    }

    internal enum RangeType
    {
        Original,
        Overwritten
    }

    internal class RangeEqualityComparer : IEqualityComparer<Range>
    {
        public static readonly RangeEqualityComparer Instance;

        static RangeEqualityComparer()
        {
            Instance = new RangeEqualityComparer();
        }

        public bool Equals(Range x, Range y)
        {
            return x?.CompareTo(y) == 0;
        }

        // Not intended for using in hash collections
        public int GetHashCode(Range obj)
        {
            return 0;
        }
    }
}
