using System;
using DokanNet;

namespace MountSqlBackup
{
    internal class Program
    {
        

#if DEBUG
        private static void Main()
        {
            try
            {
                var driveLetter = 'S';
                var backupFileName = @"C:\Program Files\Microsoft SQL Server\MSSQL14.MSSQLSERVER\MSSQL\Backup\AdventureWorks2014.bak";

                Dokan.Unmount(driveLetter);
                var backupVfs = new SqlBackupVfs(backupFileName, driveLetter);
                backupVfs.Mount($"{driveLetter}:\\", DokanOptions.NetworkDrive, 1);

                Console.WriteLine("Success");
            }
            catch (DokanException ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
#else

        private static void Main(string[] args)
        {
            try
            {
                if (args.Length != 2)
                {
                    Console.WriteLine(@"usage: mountbck.exe S 'C:\Program Files\Microsoft SQL Server\MSSQL14.MSSQLSERVER\MSSQL\Backup\AdventureWorks2014.bak'");
                    Console.WriteLine("Press any key for exit.");
                    Console.ReadKey();
                    return;
                }

                var driveLetter = args[0][0];
                var backupFileName = args[1];

                Dokan.Unmount(driveLetter);
                var backupVfs = new SqlBackupVfs(backupFileName, driveLetter);
                backupVfs.Mount($"{driveLetter}:\\", DokanOptions.NetworkDrive, 1);

                Console.WriteLine("Success");
            }
            catch (DokanException ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
#endif

    }
}