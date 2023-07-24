using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Threading;

namespace Sql2SqlCloner.Core
{
    public enum SQL_DB_Compatibility
    {
        DB_2008 = 100,
        DB_2012 = 110,
        DB_2016 = 130
    }

    public enum SQL_Server_Major_Versions
    {
        SERVER_2008 = 10,
        SERVER_2012 = 11,
        SERVER_2016 = 13
    }

    public static class SmoExtensions
    {
        public static bool IsRunningMinimumSQLVersion(this Database db, SQL_DB_Compatibility version)
        {
            if (version == SQL_DB_Compatibility.DB_2008)
            {
                return (int)db.CompatibilityLevel >= (int)SQL_DB_Compatibility.DB_2008 && db.ServerVersion.Major >= (int)SQL_Server_Major_Versions.SERVER_2008;
            }
            else if (version == SQL_DB_Compatibility.DB_2012)
            {
                return (int)db.CompatibilityLevel >= (int)SQL_DB_Compatibility.DB_2012 && db.ServerVersion.Major >= (int)SQL_Server_Major_Versions.SERVER_2012;
            }
            else if (version == SQL_DB_Compatibility.DB_2016)
            {
                return (int)db.CompatibilityLevel >= (int)SQL_DB_Compatibility.DB_2016 && db.ServerVersion.Major >= (int)SQL_Server_Major_Versions.SERVER_2016;
            }
            return false;
        }

        public static bool IsAzureDatabase(this Database db)
        {
            return db.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase;
        }

        public static bool GetTableProperty(this Table t, string propertyName)
        {
            int retries = 3;
            var lasterror = "";
            while (retries > 0)
            {
                try
                {
                    Database db = t.Parent;

                    if (propertyName == nameof(t.ChangeTrackingEnabled) && !db.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2012))
                    {
                        return false;
                    }
                    else if ((propertyName == nameof(t.IsMemoryOptimized) || propertyName == nameof(t.IsSystemVersioned))
                        && !db.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2016))
                    {
                        return false;
                    }
                    else
                    {
                        return (bool)t.GetType().GetProperty(propertyName).GetValue(t);
                    }
                }
                catch (Exception ex)
                {
                    //quick retry to workaround deadlocks
                    lasterror = ex.Message;
                    if (ex.InnerException != null)
                    {
                        if (ex.InnerException.Message.StartsWith("Transaction") && ex.InnerException.Message.Contains("was deadlocked"))
                        {
                            retries--;
                            lasterror = ex.InnerException.Message;
                            Thread.Sleep(200 + (DateTime.Now.Millisecond % 100));
                        }
                        else
                        {
                            retries = 0;
                        }
                    }
                    else
                    {
                        retries = 0;
                    }
                }
            }
            throw new Exception(lasterror);
        }
    }
}
