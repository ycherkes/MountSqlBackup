using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wintellect.PowerCollections;

namespace MountSqlBackup
{

    /// <summary>
    /// During the attaching DB with an option ATTACH_FORCE_REBUILD_LOG SQL Server needs to have an ability to point the data files into a new SQL log file
    /// HybridMemoryStorage provides an ability to customize readonly SQL data file and stores changed data in a separated OrderedMultiDictionary
    /// </summary>
    public sealed class HybridMemoryStorage : IMemoryStorage
    {
        private readonly Stream _readOnlyStream;

        public const int SqlPageSize = 0x2000; // SQL Server page size

        private readonly OrderedMultiDictionary<Range, (Range range, byte[] Data)> _overwrittenChunks = new OrderedMultiDictionary<Range, (Range range, byte[] Data)>(true);

        private long _position;
        private int _chunkSize;

        public HybridMemoryStorage(Stream readOnlyStream)
            : this(SqlPageSize, readOnlyStream)
        {
        }

        private HybridMemoryStorage(int chunkSize, Stream readOnlyStream)
        {
            _readOnlyStream = readOnlyStream;
            ChunkSize = chunkSize;
            Position = 0;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            // A random stream reading is not implemented yet - just SQL Server page alignment (8192)
            // To read the data you should ask a multiple of 8192 count
            if (count % ChunkSize != 0) return count;

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (buffer.Length - offset < count)
                throw new ArgumentException(null, nameof(count));

            var range = new Range(_position + offset, count);

            var overwrittenChunks = _overwrittenChunks[range].ToDictionary(x => x.range.From);

            // just a performance optimization
            if (overwrittenChunks.Count == 0)
            {
                _readOnlyStream.Seek(_position, SeekOrigin.Begin);
                _readOnlyStream.Read(buffer, offset, buffer.Length);

                return buffer.Length;
            }

            var getChunksCollection = GetChunksFromRange(range);

            var intersections = GetIntersections(getChunksCollection.ToArray(), overwrittenChunks.Values.Select(x => x.range).ToArray())
                .OrderBy(x => x.From)
                .ToArray();

            var total = 0;

            foreach (var intersection in intersections)
            {
                if (intersection.RangeType == RangeType.Original)
                {
                    _readOnlyStream.Seek(intersection.From, SeekOrigin.Begin);
                    _readOnlyStream.Read(buffer, (int)(intersection.From - _position), (int)intersection.Count);
                }
                else
                {
                    var data = overwrittenChunks[intersection.From].Data;

                    Buffer.BlockCopy(data, 0, buffer, (int)(intersection.From - _position), (int)intersection.Count);
                }

                total += (int)intersection.Count;
            }

            return total;
        }

        private IEnumerable<Range> GetChunksFromRange(Range range)
        {
            var lowerBound = range.From / ChunkSize * ChunkSize;
            var upperBound = (range.To / ChunkSize + 1) * ChunkSize - 1;

            for (var i = lowerBound; i <= upperBound; i += ChunkSize)
            {
                yield return new Range(i, ChunkSize);
            }
        }

        private static IEnumerable<IntersectedRange> GetIntersections(IReadOnlyCollection<Range> ranges, IReadOnlyCollection<Range> overwrittenRanges)
        {
            var intersections1 = ranges.Intersect(overwrittenRanges, RangeEqualityComparer.Instance).ToArray();
            var intersections2 = overwrittenRanges.Intersect(ranges, RangeEqualityComparer.Instance).ToArray();
            var exceptions = ranges.Except(overwrittenRanges, RangeEqualityComparer.Instance);

            for (var i = 0; i < intersections1.Length; i++)
            {
                var range1 = intersections1[i];
                var range2 = intersections2[i];

                if (range1.IsExplicitlyEqualsTo(range2))
                    yield return new IntersectedRange(range1.From, range1.Count, RangeType.Overwritten);
                else
                {
                    // A random stream access is not implemented yet - just SQL Server page alignment (8192)
                    throw new NotImplementedException();
                }
            }

            foreach (var exception in exceptions)
            {
                yield return new IntersectedRange(exception.From, exception.Count, RangeType.Original);
            }
        }

        public long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;

                case SeekOrigin.Current:
                    Position += offset;
                    break;

                case SeekOrigin.End:
                    Position = Length - offset;
                    break;
            }
            return Position;
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (buffer.Length - offset < count)
                throw new ArgumentException(null, nameof(count));


            var range = new Range(_position + offset, count);

            var overwrittenChunks = _overwrittenChunks[range].ToDictionary(x => x.range.From);

            var getChunksCollection = GetChunksFromRange(range);

            var intersections = GetIntersections(getChunksCollection.ToArray(), overwrittenChunks.Values.Select(x => x.range).ToArray())
                .OrderBy(x => x.From)
                .ToArray();

            foreach (var intersection in intersections)
            {
                if (intersection.RangeType == RangeType.Original)
                {
                    var chunkArray = new byte[intersection.Count];
                    Buffer.BlockCopy(buffer, (int)(intersection.From - _position), chunkArray, 0, (int)intersection.Count);
                    var range1 = new Range(intersection.From, intersection.Count);
                    _overwrittenChunks[range1] = new List<(Range range, byte[] Data)>{ (range1, chunkArray) };
                }
                else
                {
                    var range1 = new Range(intersection.From, intersection.Count);
                    var chunk = _overwrittenChunks[range1];
                    Buffer.BlockCopy(buffer, (int)(intersection.From - _position), chunk.Single().Data, 0, (int)intersection.Count);
                }
            }
        }

        public long Length => _readOnlyStream.Length;

        public void SetLength(long length)
        {
            throw new NotImplementedException();
        }

        public int ChunkSize
        {
            get => _chunkSize;
            set
            {
                if (value <= 0 || value >= 85000)
                    throw new ArgumentOutOfRangeException(nameof(value));

                _chunkSize = value;
            }
        }

        public long Position
        {
            get => _position;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                if (value > Length)
                    throw new ArgumentOutOfRangeException(nameof(value));

                _position = value;
            }
        }
    }
}