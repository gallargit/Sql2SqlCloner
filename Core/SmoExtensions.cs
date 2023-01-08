using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Sql2SqlCloner.Core
{
    public enum SQL_Versions
    {
        SQL_2008_Version = 655,
        SQL_2012_Version = 706,
        SQL_2016_Version = 852
    }

    public static class SmoExtensions
    {
        public static bool IsRunningMinimumSQLVersion(this Database db, SQL_Versions version)
        {
            if (version == SQL_Versions.SQL_2008_Version)
            {
                var isRunningMinimumSQL2008 = db.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase;
                if (!isRunningMinimumSQL2008)
                {
                    isRunningMinimumSQL2008 = db.Version >= (int)SQL_Versions.SQL_2008_Version;
                }
                return isRunningMinimumSQL2008;
            }
            else if (version == SQL_Versions.SQL_2012_Version)
            {
                var isRunningMinimumSQL2012 = db.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase;
                if (!isRunningMinimumSQL2012)
                {
                    isRunningMinimumSQL2012 = db.Version >= (int)SQL_Versions.SQL_2012_Version;
                }
                return isRunningMinimumSQL2012;
            }
            else if (version == SQL_Versions.SQL_2016_Version)
            {
                var isRunningMinimumSQL2016 = db.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase;
                if (!isRunningMinimumSQL2016)
                {
                    isRunningMinimumSQL2016 = db.Version >= (int)SQL_Versions.SQL_2016_Version;
                }
                return isRunningMinimumSQL2016;
            }

            return false;
        }

        public static bool GetTableProperty(this Table t, string propertyName)
        {
            Database db = t.Parent;

            if (propertyName == nameof(t.ChangeTrackingEnabled) && !db.IsRunningMinimumSQLVersion(SQL_Versions.SQL_2012_Version))
            {
                return false;
            }
            else if ((propertyName == nameof(t.IsMemoryOptimized) || propertyName == nameof(t.IsSystemVersioned))
                && !db.IsRunningMinimumSQLVersion(SQL_Versions.SQL_2016_Version))
            {
                return false;
            }
            else
            {
                return (bool)t.GetType().GetProperty(propertyName).GetValue(t);
            }
        }
    }
}
