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
        public readonly string SourceConnectionString;
        public readonly string DestinationConnectionString;

        protected ServerConnection sourceConnection;
        protected ServerConnection destinationConnection;
        protected readonly Database dbSource;
        protected readonly Database dbDestination;
        protected readonly int sqlTimeout;

        private bool SourceSqlCommandDisposed = true;
        private bool DestinationSqlCommandDisposed = true;

        public IList<string> LstPostExecutionExecute { get; } = new List<string>();

        public SqlTransfer(string src, string dest, IList<string> lstPostExecutionExecute)
        {
            SourceConnectionString = src;
            DestinationConnectionString = dest;
            sourceConnection = new ServerConnection(new SqlConnection(src));
            destinationConnection = new ServerConnection(new SqlConnection(dest));
            dbSource = new Server(SourceConnection).Databases[SourceDatabaseName];
            dbDestination = new Server(DestinationConnection).Databases[DestinationDatabaseName];

            if (!int.TryParse(ConfigurationManager.AppSettings["SqlTimeout"], out sqlTimeout))
            {
                sqlTimeout = 1800; //30 minutes
            }
            lstPostExecutionExecute?.ToList().ForEach(item => LstPostExecutionExecute.Add(item));
        }

        protected ServerConnection SourceConnection
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

        protected ServerConnection DestinationConnection
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

        public string SourceDatabaseName
        {
            get
            {
                return SourceConnection.DatabaseName;
            }
        }

        public string DestinationDatabaseName
        {
            get
            {
                return DestinationConnection.DatabaseName;
            }
        }

        protected void CopyToDestination(string sql)
        {
            using (var commandSource = GetSourceSqlCommand(sqlTimeout, sql))
            {
                using (var reader = commandSource.ExecuteReader())
                {
                    try
                    {
                        using (var commandDestination = GetDestinationSqlCommand(sqlTimeout))
                        {
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
            using (var commandSource = GetDestinationSqlCommand(sqlTimeout, sql))
            using (var reader = commandSource.ExecuteReader())
            {
                while (reader.Read())
                {
                    lstToRun.Add((string)reader[0]);
                }
            }
            if (lstToRun.Count > 0)
            {
                using (var commandDestination = GetDestinationSqlCommand(sqlTimeout))
                {
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
            using (var command = GetDestinationSqlCommand(0)) //no timeout for enabling checks
            {
                command.CommandText = "EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL'";
                command.ExecuteNonQuery();

                command.CommandText = "EXEC sp_MSforeachtable 'ALTER TABLE ? ENABLE TRIGGER ALL'";
                command.ExecuteNonQuery();
            }
        }

        public void EnableDestinationDDLTriggers()
        {
            RunInDestination("SELECT 'ENABLE TRIGGER ' + QUOTENAME(name) + ' ON DATABASE' FROM sys.triggers WHERE parent_class_desc='DATABASE'");
        }

        public void DeleteDestinationDatabase()
        {
            using (var command = GetDestinationSqlCommand(0)) //no timeout for deleting
            {
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
                    FROM sys.foreign_keys INNER JOIN sys.tables ON sys.foreign_keys.parent_object_id=sys.tables.object_id WHERE is_disabled=1 OR is_not_trusted=1");
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
                    WHERE is_disabled=0");
        }

        public void DisableAllDestinationConstraints()
        {
            RunInDestination("SELECT 'DISABLE TRIGGER ' + QUOTENAME(name) + ' ON DATABASE' FROM sys.triggers WHERE parent_class_desc='DATABASE'");
            using (var command = GetDestinationSqlCommand(0)) //no timeout for trigger disable
            {
                command.CommandText = "EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL'";
                command.ExecuteNonQuery();

                command.CommandText = "EXEC sp_MSforeachtable 'ALTER TABLE ? DISABLE TRIGGER ALL'";
                command.ExecuteNonQuery();
            }
        }

        private SqlCommand CreateSqlCommand(SqlConnection sqlConnectionObject, EventHandler disposedEvent, int? timeout = null, string sql = null)
        {
            var sqlCommand = new SqlCommand()
            {
                Connection = sqlConnectionObject,
                CommandType = CommandType.Text,
                CommandTimeout = timeout ?? sqlTimeout
            };
            if (!string.IsNullOrEmpty(sql))
            {
                sqlCommand.CommandText = sql;
            }

            sqlCommand.Disposed += disposedEvent;

            return sqlCommand;
        }

        public SqlCommand GetSourceSqlCommand(int? timeout, string sql = null)
        {
            if (!SourceSqlCommandDisposed)
            {
                throw new Exception("Fatal error, source command is still active");
            }

            SourceSqlCommandDisposed = false;

            return CreateSqlCommand(SourceConnection.SqlConnectionObject,
                (object sender, EventArgs e) => SourceSqlCommandDisposed = true, timeout, sql);
        }

        public SqlCommand GetDestinationSqlCommand(int? timeout, string sql = null)
        {
            if (!DestinationSqlCommandDisposed)
            {
                throw new Exception("Fatal error, destination command is still active");
            }

            DestinationSqlCommandDisposed = false;

            return CreateSqlCommand(DestinationConnection.SqlConnectionObject,
                (object sender, EventArgs e) => DestinationSqlCommandDisposed = true, timeout, sql);
        }

        public SqlCommand GetSqlCommand(ServerConnection cx, int? timeout, string sql = null)
        {
            if (cx == SourceConnection)
            {
                return GetSourceSqlCommand(timeout, sql);
            }
            else if (cx == DestinationConnection)
            {
                return GetDestinationSqlCommand(timeout, sql);
            }
            else
            {
                throw new Exception("Wrong connection " + cx.SqlConnectionObject.ToString());
            }
        }
    }
}
