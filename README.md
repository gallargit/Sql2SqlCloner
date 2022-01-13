# Sql2SqlCloner
SQL Server database cloning tool, based on [SqlDbCloner](https://www.codeproject.com/Articles/994806/SQL-Server-Database-Cloning-Tool-using-Csharp)

# Features
* Copy a SQL Server database from one server to another (schema and/or data). Many enhancements were made over the original tool.

* New features (some of them can be configured at the `app.config` file):  
  * Copy several schemas
  * Copy a broader range of objects: partitions, xmlschemas, extended properties, etc.
  * Exclude objects/data from copy operation
  * Select which objects/schemas/data to copy
  * View the number of records that will be copied
  * Specify filters on the data: TOP / WHERE (right click on the table name to change)
  * Use multithreading and prefetch wherever possible to decrease copying time
  * Incompliant data deletion (if foreign keys can't be activated after copying data you can choose whether to delete the incompliant data or not)
  * Copy users and permissions (GRANT)
  * Convert database collation option
  * Copy system-versioned tables
  * Some Azure-specific modifications  

* Fixes:
  * Better error handling
  * Exclude computed columns
  * Several retries to avoid errors whenever copying dependent objects
  * No DLLs, nuget packages are used instead. WARNING: Do not try to update them to the latest version as that will break the application

# How to use
Run the application, select the source and destination connections and click OK. It's usually a good idea copying to an empty database.

You can select a number of options in the screens, tables can be right-clicked so you can specify some additional options there.

Many tests were doing using Microsoft's sample Adventure Works database, which can be downloaded here:
https://github.com/Microsoft/sql-server-samples/releases/download/adventureworks/AdventureWorks2019.bak

In the `app.config` file some objects are included to demonstrate how to parametrize the application

# To be done
* Ability to decrypt encrypted objects using DAC connection
* Copy Azure databases using Azure Multi Factor Authentication as seen [here](https://stackoverflow.com/questions/60564462/how-to-connect-to-a-database-using-active-directory-login-and-multifactor-authen)
