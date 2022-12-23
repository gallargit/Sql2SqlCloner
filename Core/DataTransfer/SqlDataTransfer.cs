using Microsoft.SqlServer.Management.Common;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Windows.Forms;

namespace Sql2SqlCloner.Core.DataTransfer
{
    public class SqlDataTransfer : SqlTransfer
    {
        private SqlBulkCopy BulkCopy;
        private readonly string DestinationConnectionString;

        public SqlDataTransfer(string src, string dest)
        {
            DestinationConnectionString = dest;
            sourceConnection = new ServerConnection(new SqlConnection(src));
            destinationConnection = new ServerConnection(new SqlConnection(dest));
        }

        public void EnableTableConstraints(string tableName)
        {
            try
            {
                new SqlCommand($"ALTER TABLE {tableName} WITH CHECK CHECK CONSTRAINT all", DestinationConnection.SqlConnectionObject)
                {
                    CommandTimeout = SqlTimeout
                }.ExecuteNonQuery();
            }
            catch { }
        }

        public void EnableAllDestinationConstraints()
        {
            //enable constraints one by one, this will enable all disabled constraints (which can be enabled)
            //in a broken database and also remove the untrusted bit in all keys
            var SQLEnableConstraints = @"SELECT 
	            'ALTER TABLE ' + [t].[name] + N' WITH CHECK CHECK CONSTRAINT ' + QUOTENAME([c].[name])
                FROM
                    sys.tables AS t
                    INNER JOIN sys.check_constraints AS c ON t.[object_id] = c.parent_object_id
                WHERE
                    c.is_disabled = 1
                UNION
                SELECT 
	            'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME([schema_id])) + N'.' + QUOTENAME(OBJECT_NAME([parent_object_id])) + N' WITH CHECK CHECK CONSTRAINT ' + QUOTENAME([name])
                FROM 
                    sys.[foreign_keys] 
                WHERE 
                    [is_disabled] = 1 OR [is_not_trusted] = 1";

            var finished = false;
            var incompliantDataDeletion = ConfigurationManager.AppSettings["IncompliantDataDeletion"].ToLowerInvariant();
            while (!finished)
            {
                try
                {
                    EnableDestinationConstraints();
                    finished = true;
                    RunInDestination(SQLEnableConstraints);
                    EnableDestinationDDLTriggers();
                }
                catch (Exception ex)
                {
                    if (incompliantDataDeletion != "true" && incompliantDataDeletion != "false")
                    {
                        incompliantDataDeletion = MessageBox.Show(
                        "Could not enable constraints. Delete incompliant data?" + Environment.NewLine + Environment.NewLine +
                        "Last error was: " + ex.Message, "Error",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes
                        ? "true"
                        : "false";
                    }
                    if (Convert.ToBoolean(incompliantDataDeletion))
                    {
                        //delete data which prevents constraints from being enabled
                        DisableAllDestinationConstraints();
                        const string sql = @"select
                            fk.name as fk_constraint_name,
                            fk_cols.constraint_column_id as fk_constraint_column_id,
                            QUOTENAME(schema_name(tab.schema_id)) + '.' + QUOTENAME(tab.name) as fk_foreign_table,
                            QUOTENAME(col.name) as fk_column,
                            QUOTENAME(schema_name(pk_tab.schema_id)) + '.' + QUOTENAME(pk_tab.name) as primary_table,
                            QUOTENAME(pk_col.name) as primary_column
                            from sys.tables tab
                            inner join sys.columns col on col.object_id = tab.object_id
                            inner join sys.foreign_key_columns fk_cols
                            on fk_cols.parent_object_id = tab.object_id
                            and fk_cols.parent_column_id = col.column_id
                            inner join sys.foreign_keys fk on fk.object_id = fk_cols.constraint_object_id
                            inner join sys.tables pk_tab on pk_tab.object_id = fk_cols.referenced_object_id
                            inner join sys.columns pk_col
                            on pk_col.column_id = fk_cols.referenced_column_id
                            and pk_col.object_id = fk_cols.referenced_object_id
                            order by 5,3,1,2";

                        using (SqlCommand command = DestinationConnection.SqlConnectionObject.CreateCommand())
                        {
                            command.CommandText = sql;
                            command.CommandTimeout = SqlTimeout;
                            command.CommandType = CommandType.Text;
                            var lstDelete = new List<string>();
                            using (var reader = command.ExecuteReader())
                            {
                                var deletesentence = "";
                                var previousconstraint = "";
                                while (reader.Read())
                                {
                                    if (previousconstraint != reader["fk_constraint_name"].ToString())
                                    {
                                        if (deletesentence != "")
                                        {
                                            lstDelete.Add(deletesentence += ")");
                                        }

                                        deletesentence = $"DELETE FROM {reader["fk_foreign_table"]} WHERE NOT EXISTS(SELECT 1 FROM {reader["primary_table"]} WHERE ";
                                    }
                                    else
                                    {
                                        deletesentence += " AND ";
                                    }
                                    deletesentence += $"{reader["fk_foreign_table"]}.{reader["fk_column"]}={reader["primary_table"]}.{reader["primary_column"]}";
                                    previousconstraint = reader["fk_constraint_name"].ToString();
                                }
                                if (deletesentence != "")
                                {
                                    lstDelete.Add(deletesentence += ")");
                                }
                            }
                            var deletedrows = 0;
                            using (SqlCommand cmdDelete = new SqlCommand())
                            {
                                cmdDelete.Connection = DestinationConnection.SqlConnectionObject;
                                cmdDelete.CommandTimeout = SqlTimeout;
                                lstDelete.ForEach(deletecommand =>
                                {
                                    cmdDelete.CommandText = deletecommand;
                                    deletedrows += cmdDelete.ExecuteNonQuery();
                                });
                            }
                            if (deletedrows == 0)
                            {
                                MessageBox.Show($"No data left to delete, could not enable constraints. Last error was: {ex.Message}");
                                throw new Exception(ex.Message);
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            RunInDestination(SQLEnableConstraints);
                        }
                        catch { }
                        throw new Exception("Could not enable constraints");
                    }
                }
            }
        }

        private IEnumerable<string> GetMapping(SqlConnection cxSource, SqlConnection cxTarget, string tableName)
        {
            return GetSchema(cxSource, tableName).Intersect(GetSchema(cxTarget, tableName), StringComparer.OrdinalIgnoreCase);
        }

        private IEnumerable<string> GetSchema(SqlConnection connection, string tableName)
        {
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = @"select sche.name schemaName, tab.name tableName, col.name colName,
                    isnull(COLUMNPROPERTY(tab.OBJECT_ID,col.name,'IsComputed'),0) is_computed
                    from (sys.columns col
                    inner join sys.tables tab on tab.object_id=col.object_id
                    inner join sys.schemas sche on sche.schema_id=tab.schema_id)
                    left join sys.computed_columns ccl on ccl.object_id=col.object_id and ccl.column_id=col.column_id
                    where sche.name=@schema and tab.name=@table
                    and ISNULL(COLUMNPROPERTY(tab.OBJECT_ID,col.name,'IsComputed'),0)=0 --exclude computed columns
                    and ISNULL(COLUMNPROPERTY(tab.OBJECT_ID,col.name,'GeneratedAlwaysType'),0)=0 --exclude generated columns
                    order by 1,2,col.column_id";
                command.CommandTimeout = SqlTimeout;
                command.CommandType = CommandType.Text;
                var tablesplit = tableName.Split('.');
                command.Parameters.Add("@schema", SqlDbType.NVarChar).Value = tablesplit[0].Replace("[", "").Replace("]", "");
                command.Parameters.Add("@table", SqlDbType.NVarChar).Value = tablesplit[1].Replace("[", "").Replace("]", "");
                var lst = new List<string>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lst.Add((string)reader["colName"]);
                    }
                }
                return lst.AsEnumerable();
            }
        }

        private string GetMasterHistoryTable(SqlConnection connection, string tableName)
        {
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = @"select QUOTENAME(sche.name) + '.' + QUOTENAME(tab.name) AS MasterHistoryTable
                        from (sys.tables tab inner join sys.schemas sche on sche.schema_id=tab.schema_id)
                        where history_table_id =
                        (
                            select object_id
                            from (sys.tables tab inner join sys.schemas sche on sche.schema_id=tab.schema_id)
                            where sche.name=@schema and tab.name=@table
                        )";
                command.CommandTimeout = SqlTimeout;
                command.CommandType = CommandType.Text;
                var tablesplit = tableName.Split('.');
                command.Parameters.Add("@schema", SqlDbType.NVarChar).Value = tablesplit[0].Replace("[", "").Replace("]", "");
                command.Parameters.Add("@table", SqlDbType.NVarChar).Value = tablesplit[1].Replace("[", "").Replace("]", "");
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return (string)reader["MasterHistoryTable"];
                    }
                }
                return null;
            }
        }

        public bool TransferData(string table, string query)
        {
            SqlDataReader reader = null;
            try
            {
                if (BulkCopy == null)
                {
                    if (!int.TryParse(ConfigurationManager.AppSettings["BatchSize"], out int batchSize))
                    {
                        batchSize = 5000;
                    }
                    BulkCopy = new SqlBulkCopy(DestinationConnectionString, SqlBulkCopyOptions.KeepIdentity)
                    {
                        BatchSize = batchSize,
                        NotifyAfter = batchSize * 2,
                        BulkCopyTimeout = SqlTimeout
                    };
                }
                var masterhistorytable = GetMasterHistoryTable(DestinationConnection.SqlConnectionObject, table);
                if (!string.IsNullOrEmpty(masterhistorytable))
                {
                    new SqlCommand($"ALTER TABLE {masterhistorytable} SET(SYSTEM_VERSIONING = OFF)", DestinationConnection.SqlConnectionObject)
                    {
                        CommandTimeout = SqlTimeout
                    }.ExecuteNonQuery();
                }

                BulkCopy.DestinationTableName = table;
                BulkCopy.ColumnMappings.Clear();
                GetMapping(SourceConnection.SqlConnectionObject, DestinationConnection.SqlConnectionObject, table).ToList().
                    ForEach(columnName => BulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(columnName, columnName)));

                using (SqlCommand myCommand = new SqlCommand(query, SourceConnection.SqlConnectionObject))
                {
                    myCommand.CommandTimeout = SqlTimeout;
                    reader = myCommand.ExecuteReader();
                    BulkCopy.WriteToServer(reader);
                    reader.Close();
                }

                if (!string.IsNullOrEmpty(masterhistorytable))
                {
                    new SqlCommand($"ALTER TABLE {masterhistorytable} SET(SYSTEM_VERSIONING = ON (HISTORY_TABLE = {table}, DATA_CONSISTENCY_CHECK = ON))", DestinationConnection.SqlConnectionObject)
                    {
                        CommandTimeout = SqlTimeout
                    }.ExecuteNonQuery();
                }
                return true;
            }
            catch
            {
                BulkCopy = null;
                throw;
            }
            finally
            {
                if (reader?.IsClosed == false)
                {
                    reader.Close();
                }
            }
        }
    }
}
