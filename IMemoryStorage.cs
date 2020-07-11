using System.IO;

namespace MountSqlBackup
{
    public interface IMemoryStorage
    {
        int Read(byte[] buffer, int offset, int count);
        long Seek(long offset, SeekOrigin origin);
        void Write(byte[] buffer, int offset, int count);
        void SetLength(long length);
    }
}