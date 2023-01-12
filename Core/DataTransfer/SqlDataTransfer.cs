using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows.Forms;

namespace Sql2SqlCloner.Core.DataTransfer
{
    public class SqlDataTransfer : SqlTransfer
    {
        private SqlBulkCopy BulkCopy;

        public SqlDataTransfer(string src, string dest, IList<string> lstPostExecutionExecute) : base(src, dest, lstPostExecutionExecute)
        {
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
            const string SQLEnableConstraints = @"SELECT 
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
                    sys.foreign_keys 
                WHERE 
                    is_disabled = 1 OR is_not_trusted = 1";

            var finished = false;
            var incompliantDataDeletion = ConfigurationManager.AppSettings["IncompliantDataDeletion"].ToLowerInvariant();
            while (!finished)
            {
                try
                {
                    EnableDestinationConstraints();
                    finished = true;
                    RunInDestination(SQLEnableConstraints);
                    //recreate objects, such as security policies
                    LstPostExecutionExecute.ToList().ForEach(item => RunInDestination($"SELECT '{item}'"));
                }
                catch (Exception ex)
                {
                    if (incompliantDataDeletion != "true" && incompliantDataDeletion != "false")
                    {
                        incompliantDataDeletion = MessageBox.Show(
                        "Could not enable constraints. Delete incompliant data?" + Environment.NewLine + Environment.NewLine +
                        $"Last error was: {ex.Message}", "Error",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes
                        ? "true"
                        : "false";
                    }
                    if (Convert.ToBoolean(incompliantDataDeletion))
                    {
                        //delete data which prevents constraints from being enabled
                        DisableAllDestinationConstraints();
                        const string sql = @"SELECT
                            fk.name AS fk_constraint_name,
                            fk_cols.constraint_column_id AS fk_constraint_column_id,
                            QUOTENAME(schema_name(tab.schema_id)) + '.' + QUOTENAME(tab.name) AS fk_foreign_table,
                            QUOTENAME(col.name) AS fk_column,
                            QUOTENAME(schema_name(pk_tab.schema_id)) + '.' + QUOTENAME(pk_tab.name) AS primary_table,
                            QUOTENAME(pk_col.name) AS primary_column
                            FROM sys.tables tab
                            INNER JOIN sys.columns col ON col.object_id = tab.object_id
                            INNER JOIN sys.foreign_key_columns fk_cols
                                ON fk_cols.parent_object_id = tab.object_id AND fk_cols.parent_column_id = col.column_id
                            INNER JOIN sys.foreign_keys fk ON fk.object_id = fk_cols.constraint_object_id
                            INNER JOIN sys.tables pk_tab ON pk_tab.object_id = fk_cols.referenced_object_id
                            INNER JOIN sys.columns pk_col
                                ON pk_col.column_id = fk_cols.referenced_column_id AND pk_col.object_id = fk_cols.referenced_object_id
                            ORDER BY 5,3,1,2";

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
                            DisableDisabledObjects();
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
                command.CommandText = @"SELECT sche.name AS schemaName, tab.name AS tableName, col.name AS colName,
                    ISNULL(COLUMNPROPERTY(tab.OBJECT_ID,col.name,'IsComputed'),0) AS is_computed
                    FROM (sys.columns col
                    INNER JOIN sys.tables tab ON tab.object_id=col.object_id
                    INNER JOIN sys.schemas sche ON sche.schema_id=tab.schema_id)
                    LEFT JOIN sys.computed_columns ccl ON ccl.object_id=col.object_id AND ccl.column_id=col.column_id
                    WHERE sche.name=@schema AND tab.name=@table
                    AND ISNULL(COLUMNPROPERTY(tab.OBJECT_ID,col.name,'IsComputed'),0)=0 --exclude computed columns
                    AND ISNULL(COLUMNPROPERTY(tab.OBJECT_ID,col.name,'GeneratedAlwaysType'),0)=0 --exclude generated columns
                    ORDER BY sche.name,tab.name,col.column_id";
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
            if (dbSource.IsRunningMinimumSQLVersion(SQL_Versions.SQL_2016_Version) && dbDestination.IsRunningMinimumSQLVersion(SQL_Versions.SQL_2016_Version))
            {
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"SELECT QUOTENAME(sche.name) + '.' + QUOTENAME(tab.name) AS MasterHistoryTable
                        FROM (sys.tables tab INNER JOIN sys.schemas sche ON sche.schema_id=tab.schema_id)
                        WHERE history_table_id =
                        (
                            SELECT object_id
                            FROM (sys.tables tab INNER JOIN sys.schemas sche ON sche.schema_id=tab.schema_id)
                            WHERE sche.name=@schema AND tab.name=@table
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
                }
            }
            return null;
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
                    BulkCopy = new SqlBulkCopy(destinationConnectionString, SqlBulkCopyOptions.KeepIdentity)
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
