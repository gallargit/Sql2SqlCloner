using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
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
        protected readonly int SqlTimeout;
        public IList<string> LstPostExecutionExecute { get; } = new List<string>();

        public SqlTransfer(IList<string> lstPostExecutionExecute)
        {
            if (!int.TryParse(ConfigurationManager.AppSettings["SqlTimeout"], out SqlTimeout))
            {
                SqlTimeout = 1800; //30 minutes
            }
            lstPostExecutionExecute?.ToList().ForEach(item => LstPostExecutionExecute.Add(item));
        }

        public ServerConnection SourceConnection
        {
            get
            {
                if (sourceConnection.SqlConnectionObject.State != ConnectionState.Open)
                {
                    sourceConnection.SqlConnectionObject.Open();
                }
                return sourceConnection;
            }
        }

        public ServerConnection DestinationConnection
        {
            get
            {
                if (destinationConnection.SqlConnectionObject.State != ConnectionState.Open)
                {
                    destinationConnection.SqlConnectionObject.Open();
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
                commandSource.CommandTimeout = SqlTimeout;
                using (var reader = commandSource.ExecuteReader())
                {
                    try
                    {
                        using (SqlCommand commandDestination = DestinationConnection.SqlConnectionObject.CreateCommand())
                        {
                            commandDestination.CommandType = CommandType.Text;
                            commandDestination.CommandTimeout = SqlTimeout;
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
                commandSource.CommandTimeout = SqlTimeout;
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
                    commandDestination.CommandTimeout = SqlTimeout;
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
            RunInDestination("SELECT 'ENABLE TRIGGER ' + QUOTENAME(name) + ' ON DATABASE' from sys.triggers WHERE parent_class_desc='DATABASE'");
        }

        public void DeleteDestinationDatabase()
        {
            using (SqlCommand command = new SqlCommand())
            {
                command.Connection = DestinationConnection.SqlConnectionObject;
                command.CommandType = CommandType.Text;
                command.CommandTimeout = 0; //no timeout for deleting

                command.CommandText = "EXEC sp_MSforeachtable @command1='SET QUOTED_IDENTIFIER ON; DELETE FROM ?', @whereand='AND o.id NOT IN (SELECT history_table_id FROM sys.tables WHERE temporal_type=2)'";

                command.ExecuteNonQuery();

                //does not work command.CommandText = "DBCC CHECKIDENT(''?'', RESEED, 0)";
                //command.ExecuteNonQuery();
            }
        }

        //this will disable the constraints which were not enabled in the source database
        //but that were enabled while copying
        public void DisableDisabledObjects()
        {
            //disable everything that's disabled
            CopyToDestination(@"select 'ALTER TABLE ' + QUOTENAME(schema_name(tab.schema_id)) + '.' + QUOTENAME(tab.name) + ' NOCHECK CONSTRAINT ' + QUOTENAME(i.name)
                    from sys.check_constraints i inner join sys.objects tab on i.parent_object_id=tab.object_id
                    where is_disabled=1 or is_not_trusted=1
                UNION
                select 'ALTER INDEX ' + QUOTENAME(i.name) + ' ON ' + QUOTENAME(schema_name(tab.schema_id)) + '.' + QUOTENAME(tab.name) + ' DISABLE'
                    from sys.indexes i inner join sys.objects tab on i.object_id=tab.object_id
                    where is_disabled=1
                UNION
                select 'ALTER TABLE ' + QUOTENAME(schema_name(tab.schema_id)) + '.' + QUOTENAME(tab.name) + ' DISABLE TRIGGER ' + QUOTENAME(trig.name)
                    from sys.triggers trig inner join sys.objects tab on trig.parent_id=tab.object_id
                    where is_disabled=1
                UNION
                select 'DISABLE TRIGGER ' + QUOTENAME(name) + ' ON DATABASE'
                    from sys.triggers where parent_class_desc='DATABASE' and is_disabled=1
                select 'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(sys.tables.schema_id)) + '.' + QUOTENAME(Object_Name(sys.foreign_keys.parent_object_id)) + ' NOCHECK CONSTRAINT ' + QUOTENAME(sys.foreign_keys.name)
                    from sys.foreign_keys inner join sys.tables on sys.foreign_keys.parent_object_id=sys.tables.object_id where is_disabled=1 or is_not_trusted=1
            ");
            //re-enable the "enabled-nocheck" items
            CopyToDestination(@"select 'ALTER TABLE ' + QUOTENAME(schema_name(tab.schema_id)) + '.' + QUOTENAME(tab.name) + ' CHECK CONSTRAINT ' + QUOTENAME(i.name)
                    from sys.check_constraints i inner join sys.objects tab on i.parent_object_id=tab.object_id
                    where is_disabled=0 AND is_not_trusted=1
                UNION
                    select 'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(sys.tables.schema_id)) + '.' + QUOTENAME(Object_Name(sys.foreign_keys.parent_object_id)) + ' CHECK CONSTRAINT ' + QUOTENAME(sys.foreign_keys.name)
                    from sys.foreign_keys inner join sys.tables on sys.foreign_keys.parent_object_id=sys.tables.object_id where is_disabled=0 or is_not_trusted=1
            ");
        }

        public void DisableAllDestinationConstraints()
        {
            RunInDestination("SELECT 'DISABLE TRIGGER ' + QUOTENAME(name) + ' ON DATABASE' from sys.triggers WHERE parent_class_desc='DATABASE'");
            using (SqlCommand command = new SqlCommand())
            {
                command.Connection = DestinationConnection.SqlConnectionObject;
                command.CommandTimeout = 0;

                command.CommandText = "EXEC sp_MSforeachtable \"ALTER TABLE ? NOCHECK CONSTRAINT all\"";
                command.ExecuteNonQuery();

                command.CommandText = "EXEC sp_MSforeachtable \"ALTER TABLE ? DISABLE TRIGGER ALL\"";
                command.ExecuteNonQuery();
            }
        }
    }
}
