using Microsoft.SqlServer.Management.Common;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace Sql2SqlCloner.Core
{
    public class SqlTransfer
    {
        public ServerConnection sourceConnection;
        public ServerConnection destinationConnection;
        protected readonly int SqlTimeout;

        public SqlTransfer()
        {
            if (!int.TryParse(ConfigurationManager.AppSettings["SqlTimeout"], out SqlTimeout))
            {
                SqlTimeout = 1800; //30 minutes
            }
        }

        protected void CopyToDestination(string sql)
        {
            using (SqlCommand commandSource = sourceConnection.SqlConnectionObject.CreateCommand())
            {
                commandSource.CommandText = sql;
                commandSource.CommandType = CommandType.Text;
                commandSource.CommandTimeout = SqlTimeout;
                using (var reader = commandSource.ExecuteReader())
                {
                    try
                    {
                        using (SqlCommand commandDestination = destinationConnection.SqlConnectionObject.CreateCommand())
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

        public void EnableDestinationConstraints()
        {
            using (SqlCommand command = new SqlCommand())
            {
                command.Connection = destinationConnection.SqlConnectionObject;
                command.CommandType = CommandType.Text;
                command.CommandTimeout = 0; //no timeout for enabling checks

                command.CommandText = "EXEC sp_msforeachtable \"ALTER TABLE ? WITH CHECK CHECK CONSTRAINT all\"";
                command.ExecuteNonQuery();

                command.CommandText = "EXEC sp_msforeachtable \"ALTER TABLE ? ENABLE TRIGGER ALL\"";
                command.ExecuteNonQuery();

                command.CommandText = "ENABLE TRIGGER ALL ON DATABASE";
                command.ExecuteNonQuery();
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
                UNION
                select 'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + '.' + QUOTENAME(Object_Name(parent_object_id)) + ' NOCHECK CONSTRAINT ' + QUOTENAME([name])
                    from sys.foreign_keys where is_disabled=1 or is_not_trusted=1
            ");
            //re-enable the "enabled-nocheck" items
            CopyToDestination(@"select 'ALTER TABLE ' + QUOTENAME(schema_name(tab.schema_id)) + '.' + QUOTENAME(tab.name) + ' CHECK CONSTRAINT ' + QUOTENAME(i.name)
                    from sys.check_constraints i inner join sys.objects tab on i.parent_object_id=tab.object_id
                    where is_disabled=0 AND is_not_trusted=1
                UNION
                    select 'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + '.' + QUOTENAME(Object_Name(parent_object_id)) + ' CHECK CONSTRAINT ' + QUOTENAME([name])
                    from sys.foreign_keys where is_disabled=0 and is_not_trusted=1
            ");
        }

        public void DisableAllDestinationConstraints()
        {
            using (SqlCommand command = new SqlCommand())
            {
                command.Connection = destinationConnection.SqlConnectionObject;
                command.CommandTimeout = 0;

                command.CommandText = "EXEC sp_msforeachtable \"ALTER TABLE ? NOCHECK CONSTRAINT all\"";
                command.ExecuteNonQuery();

                command.CommandText = "EXEC sp_msforeachtable \"ALTER TABLE ? DISABLE TRIGGER ALL\"";
                command.ExecuteNonQuery();

                command.CommandText = "DISABLE TRIGGER ALL ON DATABASE";
                command.ExecuteNonQuery();
            }
        }
    }
}
