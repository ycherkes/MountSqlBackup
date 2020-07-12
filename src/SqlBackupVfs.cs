using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using DokanNet;
using YCherkes.SqlBackupReader;
using FileAccess = DokanNet.FileAccess;


namespace MountSqlBackup
{
    internal class SqlBackupVfs : IDokanOperations
    {
        private readonly string _path;
        private readonly Dictionary<string, FileContainer> _files;

        private const FileAccess DataAccess = FileAccess.ReadData | FileAccess.WriteData | FileAccess.AppendData |
                                              FileAccess.Execute |
                                              FileAccess.GenericExecute | FileAccess.GenericWrite | FileAccess.GenericRead;

        public SqlBackupVfs(string path, char driveLetter)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(path);

            _path = path;

            var backupReader = new SqlBackupReaderFactory().GetBackupReader(path, true);
            var dataFiles = backupReader.GetDataFileStreams().ToArray();
            _files = dataFiles.Select(GetFileContainer).ToDictionary(x => GetPath(x.FileInformation.FileName), StringComparer.OrdinalIgnoreCase);
            
            var fileNamesPart = _files.Select(x => x.Value.FileInformation.FileName).Aggregate(string.Empty, (current, fileName) => current + $"\r\n\t\t(FILENAME = N'{driveLetter}:\\{fileName}' ),");

            fileNamesPart = fileNamesPart.TrimEnd(',');

            var dbName = $"Test_{DateTime.Now:yyyyMMddHHmmss}";

            var sqlScript = $@"USE master

GO

    ---- Attaching Db...

    CREATE DATABASE {dbName}
    ON  {fileNamesPart}
    FOR ATTACH_FORCE_REBUILD_LOG;

GO

    ALTER DATABASE {dbName} SET READ_ONLY WITH NO_WAIT

GO

    ---- Detaching Db...

--      ALTER DATABASE {dbName}
--	    SET SINGLE_USER
--	    WITH ROLLBACK IMMEDIATE;
--
-- GO
--
--      EXEC sp_detach_db '{dbName}', 'true';
--
-- GO
";
            var scriptDataStream = new MemoryStream(Encoding.UTF8.GetBytes(sqlScript));

            _files.Add(GetPath("AttachDb.sql"), new FileContainer
            {
                FileInformation = new FileInformation
                {
                    Length = scriptDataStream.Length,
                    FileName = "AttachDb.sql",
                    Attributes = FileAttributes.Normal,
                    CreationTime = DateTime.Today,
                    LastAccessTime = DateTime.Today,
                    LastWriteTime = DateTime.Today
                },
                DataStream = new MemoryStorageStreamDecorator(scriptDataStream)
            });
        }

        private static FileContainer GetFileContainer(NamedStream stream, int index)
        {
            return new FileContainer
            {
                FileInformation = new FileInformation
                {
                    Length = stream.Length,
                    FileName = stream.FileName,
                    Attributes = FileAttributes.Normal,
                    CreationTime = DateTime.Today,
                    LastAccessTime = DateTime.Today,
                    LastWriteTime = DateTime.Today
                },
                DataStream = new HybridMemoryStorage(stream)
            };
        }

        //private static string GenerateDataFileName(int index)
        //{
        //    return "Data_" + index + (index == 0 ? ".mdf" : ".ndf");
        //}

        private string GetPath(string fileName)
        {
            return Path.Combine(_path, fileName.TrimStart('\\'));
        }

        private static string ToTrace(IDokanFileInfo info)
        {
            var context = info.Context != null ? "<" + info.Context.GetType().Name + ">" : "<null>";

            return string.Format(CultureInfo.InvariantCulture, "{{{0}, {1}, {2}, {3}, {4}, #{5}, {6}, {7}}}",
                context, info.DeleteOnClose, info.IsDirectory, info.NoCache, info.PagingIo, info.ProcessId, info.SynchronousIo, info.WriteToEndOfFile);
        }

        private static string ToTrace(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString(CultureInfo.CurrentCulture) : "<null>";
        }

        private static NtStatus Trace(string method, string fileName, IDokanFileInfo info, NtStatus result, params string[] parameters)
        {
            var extraParameters = parameters != null && parameters.Length > 0 ? ", " + string.Join(", ", parameters) : string.Empty;

#if TRACE
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}('{1}', {2}{3}) -> {4}",
                method, fileName, ToTrace(info), extraParameters, result));
#endif

            return result;
        }

        private NtStatus Trace(string method, string fileName, IDokanFileInfo info,
                                  FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes,
                                  NtStatus result)
        {
#if TRACE
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}('{1}', {2}, [{3}], [{4}], [{5}], [{6}], [{7}]) -> {8}",
                method, fileName, ToTrace(info), access, share, mode, options, attributes, result));
#endif

            return result;
        }

        #region Implementation of IDokanOperations

        public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes,
                                      IDokanFileInfo info)
        {

            if (info.IsDirectory && mode == FileMode.CreateNew)
                return DokanResult.AccessDenied;

            if(fileName == "\\" )
                return DokanResult.Success;

            var path = GetPath(fileName);

            if (Path.GetExtension(fileName) == ".ldf" && mode == FileMode.CreateNew)
            {
                var container = new FileContainer
                {
                    DataStream = new MemoryStorageStreamDecorator(new MemoryStream()),
                    FileInformation = new FileInformation
                    {
                        Length = 0,
                        Attributes = FileAttributes.Normal,
                        CreationTime = DateTime.Today,
                        LastAccessTime = DateTime.Today,
                        LastWriteTime = DateTime.Today,
                        FileName = Path.GetFileName(fileName)
                    }
                };

                _files.Add(path, container);
                info.Context = container;
                return DokanResult.Success;
            }

            var readWriteAttributes = (access & DataAccess) == 0;

            var pathExists = _files.TryGetValue(path, out var file);

            switch (mode)
            {
                case FileMode.Open:

                    if (pathExists)
                    {
                        if (readWriteAttributes)
                            // check if driver only wants to read attributes, security info, or open directory
                        {
                            info.IsDirectory = false;
                            info.Context = new object();
                            // must set it to something if you return DokanError.Success

                            return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.Success);
                        }
                    }
                    else
                    {
                        return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
                    }
                    break;

                case FileMode.CreateNew:
                    if (pathExists)
                        return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileExists);
                    break;

                case FileMode.Truncate:
                    if (!pathExists)
                        return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
                    break;
            }

            if (!pathExists) Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);

            info.Context = file;
            
            return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.Success);
        }

        public void Cleanup(string fileName, IDokanFileInfo info)
        {
#if TRACE
            if (info.Context != null)
                Console.WriteLine(string.Format(CultureInfo.CurrentCulture, "{0}('{1}', {2} - entering",
                    "Cleanup", fileName, ToTrace(info)));
#endif

            info.Context = null;

            Trace("Cleanup", fileName, info, DokanResult.Success);
        }

        public void CloseFile(string fileName, IDokanFileInfo info)
        {
#if TRACE
            if (info.Context != null)
                Console.WriteLine(string.Format(CultureInfo.CurrentCulture, "{0}('{1}', {2} - entering",
                    "CloseFile", fileName, ToTrace(info)));
#endif

            info.Context = null;

            Trace("CloseFile", fileName, info, DokanResult.Success);
        }

        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
        {
            if (!(info.Context is FileContainer))
            {
                bytesRead = 0;
                
                return Trace("ReadFile", fileName, info, DokanResult.NotImplemented, "out " + bytesRead, offset.ToString(CultureInfo.InvariantCulture));
            }

            var containerFile = (FileContainer)info.Context;

            //if (containerFile.OverWrittenData.TryGetValue(new Range(offset), out var result))
            //{
            //    bytesRead = buffer.Length;
            //    Array.Copy(result, buffer, bytesRead);
            //}
            //else
            //{
            //    bytesRead = 0;
            //    if(new[]{".mdf", "'ndf"}.Contains(Path.GetExtension(containerFile.FileInformation.FileName)))
            //    {
            //        do
            //        {
            //            containerFile.DataStream.Seek(offset + bytesRead, SeekOrigin.Begin);
            //            bytesRead += containerFile.DataStream.Read(buffer, bytesRead,
            //                buffer.Length - bytesRead > 8192 ? 8192 : buffer.Length - bytesRead);
            //        } while (buffer.Length - bytesRead > 0);
            //    }
            //    else
            //    {
            //        containerFile.DataStream.Seek(offset, SeekOrigin.Begin);
            //        bytesRead += containerFile.DataStream.Read(buffer, 0, buffer.Length);
            //    }
            //}

            containerFile.DataStream.Seek(offset, SeekOrigin.Begin);
            bytesRead = containerFile.DataStream.Read(buffer, 0, buffer.Length);

            return Trace("ReadFile", fileName, info, DokanResult.Success, "out " + bytesRead, offset.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            if (!(info.Context is FileContainer))
            {
                bytesWritten = 0;
                
               return Trace("WriteFile", fileName, info, DokanResult.NotImplemented, "out " + bytesWritten, offset.ToString(CultureInfo.InvariantCulture));
            }

            var containerFile = (FileContainer)info.Context;

            //if (new[] { ".mdf", "'ndf" }.Contains(Path.GetExtension(containerFile.FileInformation.FileName)))
            //{
            //    var range = new Range(offset) {To = offset + buffer.Length - 1};
            //    containerFile.OverWrittenData[range] = (byte[])buffer.Clone();
            //}
            //else
            //{
            //    containerFile.DataStream.Seek(offset, SeekOrigin.Begin);
            //    containerFile.DataStream.Write(buffer, 0, buffer.Length);
            //}
            containerFile.DataStream.Seek(offset, SeekOrigin.Begin);
            containerFile.DataStream.Write(buffer, 0, buffer.Length);

            bytesWritten = buffer.Length;
            return Trace("WriteFile", fileName, info, DokanResult.Success, "out " + bytesWritten, offset.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info)
        {
            return Trace("FlushFileBuffers", fileName, info, DokanResult.Success);
        }

        public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
        {
            var path = GetPath(fileName);

            if (fileName == "\\")
            {
                fileInfo = new FileInformation
                {
                    FileName = fileName,
                    Attributes =  FileAttributes.Directory,
                    CreationTime = DateTime.Today,
                    LastAccessTime = DateTime.Today,
                    LastWriteTime = DateTime.Today,
                    Length = 0
                };

                return Trace("GetFileInformation", fileName, info, DokanResult.Success);
            }

            if (!_files.TryGetValue(path, out var container))
            {
                fileInfo = new FileInformation();
                return Trace("GetFileInformation", fileName, info, DokanResult.Error);
            }

            fileInfo = container.FileInformation;

            return Trace("GetFileInformation", fileName, info, DokanResult.Success);
        }

        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
        {
            files = new List<FileInformation>();

            if (fileName != "\\") return Trace("FindFiles", fileName, info, DokanResult.Error);

            foreach (var file in _files)
            {
                files.Add(file.Value.FileInformation);
            }

            return Trace("FindFiles", fileName, info, DokanResult.Success);
        }

        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
        {
            files = null;
            return Trace("FindFilesWithPattern", fileName, info, DokanResult.NotImplemented);
        }

        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info)
        {
            return Trace("SetFileAttributes", fileName, info, DokanResult.NotImplemented, attributes.ToString());
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime,
            IDokanFileInfo info)
        {
            return Trace("SetFileTime", fileName, info, DokanResult.NotImplemented, ToTrace(creationTime), ToTrace(lastAccessTime), ToTrace(lastWriteTime));
        }

        public NtStatus DeleteFile(string fileName, IDokanFileInfo info)
        {
            var path = GetPath(fileName);

            if (Path.GetExtension(fileName) != ".ldf")
                return Trace("DeleteFile", fileName, info, _files.ContainsKey(path) ? DokanResult.Success : DokanResult.FileNotFound);

            if (!_files.ContainsKey(path)) return Trace("DeleteFile", fileName, info, DokanResult.FileNotFound);

            _files.Remove(path);

            return Trace("DeleteFile", fileName, info, DokanResult.Success);
        }

        public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
        {
            return Trace("DeleteDirectory", fileName, info, _files.Any() ? DokanResult.DirectoryNotEmpty : DokanResult.Success);
        }

        public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
        {
            return Trace("MoveFile", oldName, info, DokanResult.NotImplemented, newName, replace.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
        {
            try
            {
                var container = (FileContainer)info.Context;

                if (container == null) return Trace("SetEndOfFile", fileName, info, DokanResult.NotImplemented, length.ToString(CultureInfo.InvariantCulture));

                container.DataStream.SetLength(length);
                container.FileInformation = new FileInformation
                {
                    Length = length,
                    Attributes = container.FileInformation.Attributes,
                    CreationTime = container.FileInformation.CreationTime,
                    LastAccessTime = container.FileInformation.LastAccessTime,
                    LastWriteTime = container.FileInformation.LastWriteTime,
                    FileName = container.FileInformation.FileName
                };

                return Trace("SetEndOfFile", fileName, info, DokanResult.Success, length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace("SetEndOfFile", fileName, info, DokanResult.DiskFull, length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info)
        {
            try
            {
                var container = (FileContainer)info.Context;

                if (container == null) return Trace("SetAllocationSize", fileName, info, DokanResult.NotImplemented,length.ToString(CultureInfo.InvariantCulture));

                container.DataStream.SetLength(length);
                container.FileInformation = new FileInformation
                {
                    Length = length,
                    Attributes = container.FileInformation.Attributes,
                    CreationTime = container.FileInformation.CreationTime,
                    LastAccessTime = container.FileInformation.LastAccessTime,
                    LastWriteTime = container.FileInformation.LastWriteTime,
                    FileName = container.FileInformation.FileName
                };

                return Trace("SetAllocationSize", fileName, info, DokanResult.Success, length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace("SetAllocationSize", fileName, info, DokanResult.DiskFull, length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            try
            {
                return Trace("LockFile", fileName, info, DokanResult.Success, offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace("LockFile", fileName, info, DokanResult.AccessDenied, offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            try
            {
                return Trace("UnlockFile", fileName, info, DokanResult.Success, offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace("UnlockFile", fileName, info, DokanResult.AccessDenied, offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus GetDiskFreeSpace(out long free, out long total, out long used, IDokanFileInfo info)
        {
            var diskInfo = DriveInfo.GetDrives().Single(di => di.RootDirectory.Name == Path.GetPathRoot(_path + "\\"));

            used = diskInfo.AvailableFreeSpace;
            total = diskInfo.TotalSize;
            free = diskInfo.TotalFreeSpace;

            return Trace("GetDiskFreeSpace", null, info, DokanResult.Success, "out " + free, "out " + total, "out " + used);
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features,
                                                out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
        {
            volumeLabel = "MountSqlBackup";
            fileSystemName = "MountSqlBackup";
            maximumComponentLength = 255;

            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch |
                       FileSystemFeatures.PersistentAcls | FileSystemFeatures.SupportsRemoteStorage |
                       FileSystemFeatures.UnicodeOnDisk;

            return Trace("GetVolumeInformation", null, info, DokanResult.Success, "out " + volumeLabel, "out " + features, "out " + fileSystemName);
        }

        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
        {
            security = new FileSecurity();
            return Trace("GetFileSecurity", fileName, info, DokanResult.NotImplemented, sections.ToString());
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
        {
            return Trace("SetFileSecurity", fileName, info, DokanResult.NotImplemented, sections.ToString());
        }

        public NtStatus Mounted(IDokanFileInfo info)
        {
            return Trace("Mounted", null, info, DokanResult.Success);
        }

        public NtStatus Unmounted(IDokanFileInfo info)
        {
            return Trace("Unmounted", null, info, DokanResult.Success);
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
        {
            streams = new FileInformation[0];
            return Trace("EnumerateNamedStreams", fileName, info, DokanResult.NotImplemented);
        }

        #endregion Implementation of IDokanOperations
    }
}