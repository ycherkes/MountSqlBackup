using System.IO;

namespace MountSqlBackup
{
    internal class MemoryStorageStreamDecorator : IMemoryStorage
    {
        private readonly Stream _stream;

        public MemoryStorageStreamDecorator(Stream stream)
        {
            _stream = stream;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, count);
        }

        public long Seek(long offset, SeekOrigin origin)
        {
            return _stream.Seek(offset, origin);
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            _stream.Write(buffer, offset, count);
        }

        public void SetLength(long length)
        {
            _stream.SetLength(length);
        }
    }
}
