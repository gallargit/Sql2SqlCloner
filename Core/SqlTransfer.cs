using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;

namespace Sql2SqlCloner.Core
{
    public class SqlTransfer
    {
        protected ServerConnection sourceConnection;
        protected ServerConnection destinationConnection;
        protected readonly Database dbSource;
        protected readonly Database dbDestination;
        protected readonly string sourceConnectionString;
        protected readonly string destinationConnectionString;
        protected readonly int sqlTimeout;

        public IList<string> LstPostExecutionExecute { get; } = new List<string>();

        public SqlTransfer(string src, string dest, IList<string> lstPostExecutionExecute)
        {
            sourceConnectionString = src;
            destinationConnectionString = dest;
            sourceConnection = new ServerConnection(new SqlConnection(src));
            destinationConnection = new ServerConnection(new SqlConnection(dest));
            dbSource = new Server(SourceConnection).Databases[SourceConnection.DatabaseName];
            dbDestination = new Server(DestinationConnection).Databases[DestinationConnection.DatabaseName];

            if (!int.TryParse(ConfigurationManager.AppSettings["SqlTimeout"], out sqlTimeout))
            {
                sqlTimeout = 1800; //30 minutes
            }
            lstPostExecutionExecute?.ToList().ForEach(item => LstPostExecutionExecute.Add(item));
        }

        public ServerConnection SourceConnection
        {
            get
            {
                try
                {
                    if (sourceConnection.SqlConnectionObject.State != ConnectionState.Open)
                    {
                        sourceConnection.SqlConnectionObject.Open();
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Error connecting to source server: " + sourceConnection.ServerInstance, ex);
                }
                return sourceConnection;
            }
        }

        public ServerConnection DestinationConnection
        {
            get
            {
                try
                {
                    if (destinationConnection.SqlConnectionObject.State != ConnectionState.Open)
                    {
                        destinationConnection.SqlConnectionObject.Open();
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Error connecting to destination server: " + destinationConnection.ServerInstance, ex);
                }
                return destinationConnection;
            }
        }

        protected void CopyToDestination(string sql)
        {
            using (SqlCommand commandSource = SourceConnection.SqlConnectionObject.CreateCommand())
            {
                commandSource.CommandText = sql;
                commandSource.CommandType = CommandType.Text;
                commandSource.CommandTimeout = sqlTimeout;
                using (var reader = commandSource.ExecuteReader())
                {
                    try
                    {
                        using (SqlCommand commandDestination = DestinationConnection.SqlConnectionObject.CreateCommand())
                        {
                            commandDestination.CommandType = CommandType.Text;
                            commandDestination.CommandTimeout = sqlTimeout;
                            while (reader.Read())
                            {
                                try
                                {
                                    commandDestination.CommandText = (string)reader[0];
                                    commandDestination.ExecuteNonQuery();
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        public void RunInDestination(string sql)
        {
            var lstToRun = new List<string>();
            using (SqlCommand commandSource = DestinationConnection.SqlConnectionObject.CreateCommand())
            {
                commandSource.CommandText = sql;
                commandSource.CommandType = CommandType.Text;
                commandSource.CommandTimeout = sqlTimeout;
                using (var reader = commandSource.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lstToRun.Add((string)reader[0]);
                    }
                }
            }
            if (lstToRun.Count > 0)
            {
                using (SqlCommand commandDestination = DestinationConnection.SqlConnectionObject.CreateCommand())
                {
                    commandDestination.CommandType = CommandType.Text;
                    commandDestination.CommandTimeout = sqlTimeout;
                    lstToRun.ForEach(item =>
                    {
                        try
                        {
                            commandDestination.CommandText = item;
                            commandDestination.ExecuteNonQuery();
                        }
                        catch { }
                    });
                }
            }
        }

        public void EnableDestinationConstraints()
        {
            using (SqlCommand command = new SqlCommand())
            {
                command.Connection = DestinationConnection.SqlConnectionObject;
                command.CommandType = CommandType.Text;
                command.CommandTimeout = 0; //no timeout for enabling checks

                command.CommandText = "EXEC sp_MSforeachtable \"ALTER TABLE ? WITH CHECK CHECK CONSTRAINT all\"";
                command.ExecuteNonQuery();

                command.CommandText = "EXEC sp_MSforeachtable \"ALTER TABLE ? ENABLE TRIGGER ALL\"";
                command.ExecuteNonQuery();
            }
        }

        public void EnableDestinationDDLTriggers()
        {
            RunInDestination("SELECT 'ENABLE TRIGGER ' + QUOTENAME(name) + ' ON DATABASE' FROM sys.triggers WHERE parent_class_desc='DATABASE'");
        }

        public void DeleteDestinationDatabase()
        {
            using (SqlCommand command = new SqlCommand())
            {
                command.Connection = DestinationConnection.SqlConnectionObject;
                command.CommandType = CommandType.Text;
                command.CommandTimeout = 0; //no timeout for deleting

                if (dbDestination.IsRunningMinimumSQLVersion(SQL_Versions.SQL_2016_Version))
                {
                    command.CommandText = "EXEC sp_MSforeachtable @command1='SET QUOTED_IDENTIFIER ON; DELETE FROM ?', @whereand='AND o.id NOT IN (SELECT history_table_id FROM sys.tables WHERE temporal_type=2)'";
                }
                else
                {
                    command.CommandText = "EXEC sp_MSforeachtable @command1='SET QUOTED_IDENTIFIER ON; DELETE FROM ?'";
                }

                command.ExecuteNonQuery();

                //this does not work: command.CommandText = "DBCC CHECKIDENT(''?'', RESEED, 0)";
                //command.ExecuteNonQuery();
            }
        }

        //this will disable the constraints which were not enabled in the source database
        //but that were enabled while copying
        public void DisableDisabledObjects()
        {
            //disable everything that was disabled
            CopyToDestination(@"SELECT 'ALTER TABLE ' + QUOTENAME(schema_name(tab.schema_id)) + '.' + QUOTENAME(tab.name) + ' NOCHECK CONSTRAINT ' + QUOTENAME(i.name)
                    FROM sys.check_constraints i INNER JOIN sys.objects tab ON i.parent_object_id=tab.object_id
                    WHERE is_disabled=1 OR is_not_trusted=1
                UNION
                SELECT 'ALTER INDEX ' + QUOTENAME(i.name) + ' ON ' + QUOTENAME(schema_name(tab.schema_id)) + '.' + QUOTENAME(tab.name) + ' DISABLE'
                    FROM sys.indexes i INNER JOIN sys.objects tab ON i.object_id=tab.object_id
                    WHERE is_disabled=1
                UNION
                SELECT 'ALTER TABLE ' + QUOTENAME(schema_name(tab.schema_id)) + '.' + QUOTENAME(tab.name) + ' DISABLE TRIGGER ' + QUOTENAME(trig.name)
                    FROM sys.triggers trig INNER JOIN sys.objects tab ON trig.parent_id=tab.object_id
                    WHERE is_disabled=1
                UNION
                SELECT 'DISABLE TRIGGER ' + QUOTENAME(name) + ' ON DATABASE'
                    FROM sys.triggers WHERE parent_class_desc='DATABASE' AND is_disabled=1
                UNION
                SELECT 'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(sys.tables.schema_id)) + '.' + QUOTENAME(Object_Name(sys.foreign_keys.parent_object_id)) + ' NOCHECK CONSTRAINT ' + QUOTENAME(sys.foreign_keys.name)
                    FROM sys.foreign_keys INNER JOIN sys.tables ON sys.foreign_keys.parent_object_id=sys.tables.object_id WHERE is_disabled=1 OR is_not_trusted=1
            ");
            //re-enable the "enabled-nocheck" items
            CopyToDestination(@"SELECT 'ALTER TABLE ' + QUOTENAME(schema_name(tab.schema_id)) + '.' + QUOTENAME(tab.name) + ' CHECK CONSTRAINT ' + QUOTENAME(i.name)
                    FROM sys.check_constraints i INNER JOIN sys.objects tab ON i.parent_object_id=tab.object_id
                    WHERE is_disabled=0 AND is_not_trusted=1
                UNION
                    SELECT 'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(sys.tables.schema_id)) + '.' + QUOTENAME(Object_Name(sys.foreign_keys.parent_object_id)) + ' CHECK CONSTRAINT ' + QUOTENAME(sys.foreign_keys.name)
                    FROM sys.foreign_keys INNER JOIN sys.tables ON sys.foreign_keys.parent_object_id=sys.tables.object_id WHERE is_disabled=0 OR is_not_trusted=1
                UNION
                    SELECT 'ENABLE TRIGGER ' + QUOTENAME(SCHEMA_NAME(sys.tables.schema_id)) + '.' +  QUOTENAME(sys.triggers.name) + ' ON ' +
                    QUOTENAME(SCHEMA_NAME(sys.tables.schema_id)) + '.' + QUOTENAME(sys.tables.name)
                    FROM sys.triggers INNER JOIN sys.tables ON sys.triggers.parent_id=sys.tables.object_id
                    WHERE is_disabled=0
            ");
        }

        public void DisableAllDestinationConstraints()
        {
            RunInDestination("SELECT 'DISABLE TRIGGER ' + QUOTENAME(name) + ' ON DATABASE' FROM sys.triggers WHERE parent_class_desc='DATABASE'");
            using (SqlCommand command = new SqlCommand())
            {
                command.Connection = DestinationConnection.SqlConnectionObject;
                command.CommandTimeout = 0;

                command.CommandText = "EXEC sp_MSforeachtable \"ALTER TABLE ? NOCHECK CONSTRAINT ALL\"";
                command.ExecuteNonQuery();

                command.CommandText = "EXEC sp_MSforeachtable \"ALTER TABLE ? DISABLE TRIGGER ALL\"";
                command.ExecuteNonQuery();
            }
        }
    }
}
