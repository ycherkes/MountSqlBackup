# MountSqlBackup

MountSqlBackup is an open-source tool which allows to attach an SQL Server Database backup to the SQL Server directly without backup-restore operation.

It uses a demo version of [YCherkes.SqlBackupReader](https://github.com/ycherkes/YCherkes.SqlBackupReader.Demo) (it's free for non-commercial purposes)

## Installation

* Install Dokany: [DokanSetup_redist.exe](https://github.com/dokan-dev/dokany/releases/download/v1.4.0.1000/DokanSetup_redist.exe)
* Build the MountSqlBackup sources or download the [release](https://github.com/ycherkes/MountSqlBackup/releases)

## Usage

* Run mountbck.exe as described below:
	* mountbck.exe <DriveLetter> <BackupPath>
	* both parameters are mandatory
	* example: ```Batchfile mountbck.exe S C:\Temp\AdventureWorks2014.bak```
* Go to mounted drive (in example it's a drive S:)
* Double click on S:\AttachDb.sql - Sql Server Managemant Studio will open this file
* Press F5, or click Execute button
* Before you close (Ctrl+C) the mountbck.exe, don't forget to detach a database:
	* uncomment "Detaching Db..." block of AttachDb.sql (Select it and press Ctrl + K + U)
	* Press F5, or click Execute button (only "Detaching Db..." block must be selected)

## Features

* Supported backup formats:
  * Uncompressed SQL Server Full Db backups (more informations please read here: [YCherkes.SqlBackupReader](https://github.com/ycherkes/YCherkes.SqlBackupReader.Demo))
* Limitations:
  * Databases with Memory-Optimized Tables are not supported (because SQL Server doesn't allow to rebuild the log for this kind of Db)

# Bug Report

See the [Issues Report](https://github.com/ycherkes/MountSqlBackup/issues) section of website.