using System.Collections.Generic;
using DokanNet;

namespace MountSqlBackup
{
    internal class FileContainer
    {
        public FileContainer()
        {
            OverWrittenData = new SortedDictionary<Range, byte[]>();
        }

        public SortedDictionary<Range, byte[]> OverWrittenData { get; }
        public FileInformation FileInformation { get; set; }
        public IMemoryStorage DataStream { get; set; }
    }
}