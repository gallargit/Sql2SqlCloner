# Sql2SqlCloner
SQL Server database cloning tool, based on [SqlDbCloner](https://www.codeproject.com/Articles/994806/SQL-Server-Database-Cloning-Tool-using-Csharp)

# Features
* Copy a SQL Server database from one server to another (schema and/or data). Many enhancements were made over the original tool. Works with SQL Server 2005 or greater.

* New features (some of them can be configured at the `app.config` file):  
  * Copy several schemas
  * Copy a broader range of objects and data types: partitions, xmlschemas, extended properties, spatial data, etc.
  * Exclude objects/data from copy operation
  * Select which objects/schemas/data to copy
  * View the number of records that will be copied
  * Specify filters on the data: TOP / WHERE (right click on the table name to change)
  * Use multithreading and prefetch wherever possible to decrease copying time
  * Incompliant data deletion (if foreign keys can't be activated after copying data you can choose whether to delete the incompliant data or not)
  * Copy users and permissions (GRANT)
  * Convert database collation option
  * Copy system-versioned tables
  * Copy change-tracking tables
  * Some Azure-specific modifications  

* Fixes:
  * Better error handling
  * Exclude computed columns
  * Several retries to avoid errors whenever copying dependent objects
  * No DLLs, nuget packages are used instead

# How to use
Run the application, select the source and destination connections and click OK. It's usually a good idea copying to an empty database.

You can select a number of options in the screens, tables can be right-clicked so you can specify some additional options there.

Many tests were doing using Microsoft's sample Adventure Works database, which can be downloaded here:
https://github.com/Microsoft/sql-server-samples/releases/download/adventureworks/AdventureWorks2019.bak

In the `app.config` file some parameters are included to demonstrate how to parametrize the application

# Known issues
- Ever since Visual Studio release 17.4.2 sometimes the build process does not work properly, I think it's an error with MSBuild. When the application is compiled for the first time, two DLLs belonging to two nuget packages are not copied to the output directory, they are:
  - SqlServerSpatial160.dll (Microsoft.SqlServer.Types package)
  - Microsoft.Data.SqlClient.SNI.x64.dll (Microsoft.Data.SqlClient.SNI package)

These two DLLs should be copied at build time, according to these two lines in the Csproj file:

`<Import Project="packages\Microsoft.SqlServer.Types.160.1000.6\build\net462\Microsoft.SqlServer.Types.props" Condition="Exists('packages\Microsoft.SqlServer.Types.160.1000.6\build\net462\Microsoft.SqlServer.Types.props')" />`

`<Import Project="packages\Microsoft.Data.SqlClient.SNI.5.0.1\build\net46\Microsoft.Data.SqlClient.SNI.targets" Condition="Exists('packages\Microsoft.Data.SqlClient.SNI.5.0.1\build\net46\Microsoft.Data.SqlClient.SNI.targets')" />`

My guess is that the first time they are not copied because the packages have not been downloaded yet, and I could not figure out a way to force package download. If you first build; then rebuild the project, they will be copied and everything will work. But since this is annoying and inconvenient a quick workaround has been made in the `Loader.cs` file. If the DLLs are not present, they will be copied at runtime from the `packages` folder.

- Packages Microsoft.SqlServer.SqlManagementObjects, Microsoft.Data.SqlClient and Microsoft.Data.SqlClient.SNI 5.1.0 break compatibility with SQL Server 2005, hence they will not be upgraded

# New Experimental Features
* Ability to decrypt encrypted objects using DAC connection. Triggers and functions do not work.
* Multithreaded schema processing.
* Incremental data copy (available when copying data only)

# To be done
* Copy Azure databases using Azure Multi Factor Authentication as seen [here](https://stackoverflow.com/questions/60564462/how-to-connect-to-a-database-using-active-directory-login-and-multifactor-authen)
* Decrypt and re-encrypt columns as seen here https://learn.microsoft.com/en-us/sql/t-sql/functions/decryptbykey-transact-sql?view=sql-server-ver16