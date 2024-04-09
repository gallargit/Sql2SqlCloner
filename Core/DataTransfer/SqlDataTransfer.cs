using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using System;
using System.Collections.Generic;
using System.Configuration;
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
                using (var command = GetDestinationSqlCommand(sqlTimeout, $"ALTER TABLE {tableName} WITH CHECK CHECK CONSTRAINT ALL"))
                {
                    command.ExecuteNonQuery();
                }
            }
            catch { }
        }

        public void EnableAllDestinationConstraints()
        {
            //enable constraints one by one, this will enable all disabled constraints (which can be enabled)
            //in a broken database and also remove the untrusted bit in all keys
            const string SQLEnableConstraints = @"SELECT 'ALTER TABLE ' + [t].[name] + N' WITH CHECK CHECK CONSTRAINT ' + QUOTENAME([c].[name])
                FROM sys.tables AS t INNER JOIN sys.check_constraints AS c ON t.[object_id]=c.parent_object_id
                WHERE c.is_disabled=1
            UNION
                SELECT 'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME([schema_id])) + N'.' + QUOTENAME(OBJECT_NAME([parent_object_id])) + N' WITH CHECK CHECK CONSTRAINT ' + QUOTENAME([name])
                FROM sys.foreign_keys 
                WHERE is_disabled=1 OR is_not_trusted=1";

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
                            INNER JOIN sys.columns col ON col.object_id=tab.object_id
                            INNER JOIN sys.foreign_key_columns fk_cols
                                ON fk_cols.parent_object_id = tab.object_id AND fk_cols.parent_column_id=col.column_id
                            INNER JOIN sys.foreign_keys fk ON fk.object_id=fk_cols.constraint_object_id
                            INNER JOIN sys.tables pk_tab ON pk_tab.object_id=fk_cols.referenced_object_id
                            INNER JOIN sys.columns pk_col
                                ON pk_col.column_id=fk_cols.referenced_column_id AND pk_col.object_id=fk_cols.referenced_object_id
                            ORDER BY 5,3,1,2";

                        var lstDelete = new List<string>();
                        using (var command = GetDestinationSqlCommand(sqlTimeout, sql))
                        {
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
                                            lstDelete.Add(deletesentence + ")");
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
                                    lstDelete.Add(deletesentence + ")");
                                }
                            }
                        }
                        var deletedrows = 0;
                        using (var cmdDelete = GetDestinationSqlCommand(sqlTimeout))
                        {
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

        private IEnumerable<string> GetMapping(ServerConnection cxSource, ServerConnection cxTarget, string tableName)
        {
            return GetSchema(cxSource, tableName).Intersect(GetSchema(cxTarget, tableName), StringComparer.OrdinalIgnoreCase);
        }

        private IEnumerable<string> GetSchema(ServerConnection connection, string tableName)
        {
            using (var command = GetSqlCommand(connection, sqlTimeout))
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
                var tablesplit = tableName.Split('.');
                command.Parameters.Add("@schema", System.Data.SqlDbType.NVarChar).Value = tablesplit[0].Replace("[", "").Replace("]", "");
                command.Parameters.Add("@table", System.Data.SqlDbType.NVarChar).Value = tablesplit[1].Replace("[", "").Replace("]", "");
                var lst = new List<string>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        lst.Add(reader["colName"].ToString());
                    }
                }
                return lst.AsEnumerable();
            }
        }

        private IDictionary<string, string> GetUniqueColumns(ServerConnection connection, string tableName)
        {
            using (var command = GetSqlCommand(connection, sqlTimeout))
            {
                command.CommandText = @"SELECT c.[name] AS constraint_name, SUBSTRING(column_names, 1, len(column_names)-1) AS constraint_columns
                FROM sys.objects t LEFT OUTER JOIN sys.indexes i ON t.object_id=i.object_id
                    LEFT OUTER JOIN sys.key_constraints c ON i.object_id=c.parent_object_id AND i.index_id=c.unique_index_id
                    CROSS APPLY (SELECT QUOTENAME(col.[name]) + ','
                                    FROM sys.index_columns ic
                                    INNER JOIN sys.columns col ON ic.object_id=col.object_id AND ic.column_id=col.column_id
                                    WHERE ic.object_id=t.object_id AND ic.index_id=i.index_id
                                    ORDER BY col.column_id
                                    FOR XML PATH ('')) D (column_names)
                WHERE is_unique=1 AND t.is_ms_shipped<>1
                AND QUOTENAME(schema_name(t.schema_id)) + '.' + QUOTENAME(t.[name])='" + tableName + "'";
                var lst = new Dictionary<string, string>();
                using (var reader = command.ExecuteReader())
                {
                    int rowcount = 0;
                    while (reader.Read())
                    {
                        rowcount++;
                        var key = (reader["constraint_name"] == DBNull.Value) ? ("null" + rowcount) : (string)reader["constraint_name"];
                        while (lst.ContainsKey(key))
                        {
                            key += "x";
                        }
                        lst.Add(key, reader["constraint_columns"].ToString());
                    }
                }
                return lst;
            }
        }

        private string GetMasterHistoryTable(ServerConnection connection, string tableName)
        {
            if (dbSource.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2016) && dbDestination.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2016))
            {
                using (var command = GetSqlCommand(connection, sqlTimeout))
                {
                    command.CommandText = @"SELECT QUOTENAME(sche.name) + '.' + QUOTENAME(tab.name) AS MasterHistoryTable
                        FROM (sys.tables tab INNER JOIN sys.schemas sche ON sche.schema_id=tab.schema_id)
                        WHERE history_table_id=
                        (
                            SELECT object_id
                            FROM (sys.tables tab INNER JOIN sys.schemas sche ON sche.schema_id=tab.schema_id)
                            WHERE sche.name=@schema AND tab.name=@table
                        )";
                    var tablesplit = tableName.Split('.');
                    command.Parameters.Add("@schema", System.Data.SqlDbType.NVarChar).Value = tablesplit[0].Replace("[", "").Replace("]", "");
                    command.Parameters.Add("@table", System.Data.SqlDbType.NVarChar).Value = tablesplit[1].Replace("[", "").Replace("]", "");
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

        public bool TransferData(string tableName, string query)
        {
            SqlDataReader reader = null;
            var strDropIndex = new List<string>();
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
                        BulkCopyTimeout = sqlTimeout
                    };
                }

                if (Properties.Settings.Default.IncrementalDataCopy)
                {
                    var uniqueColumns = GetUniqueColumns(destinationConnection, tableName);
                    if (uniqueColumns.Any())
                    {
                        using (var command = GetDestinationSqlCommand(sqlTimeout))
                        {
                            foreach (var uqIndex in uniqueColumns)
                            {
                                //create a temporary name
                                var indexname = $"index{DateTime.Now.Ticks}TMP{Math.Abs(uqIndex.GetHashCode() % 10000)}";
                                command.CommandText = $"CREATE UNIQUE NONCLUSTERED INDEX {indexname} ON {tableName} ({uqIndex.Value}" +
                                     ") WITH(PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = ON, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]";
                                command.ExecuteNonQuery();
                                strDropIndex.Add($"DROP INDEX {indexname} ON {tableName}");
                            }
                        }
                    }
                }

                var masterhistorytable = GetMasterHistoryTable(DestinationConnection, tableName);
                if (!string.IsNullOrEmpty(masterhistorytable))
                {
                    using (var command = GetDestinationSqlCommand(sqlTimeout, $"ALTER TABLE {masterhistorytable} SET(SYSTEM_VERSIONING = OFF)"))
                    {
                        command.ExecuteNonQuery();
                    }
                }

                BulkCopy.DestinationTableName = tableName;
                BulkCopy.ColumnMappings.Clear();
                GetMapping(SourceConnection, DestinationConnection, tableName).ToList().
                    ForEach(columnName => BulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(columnName, columnName)));

                using (var command = GetSourceSqlCommand(sqlTimeout, query))
                {
                    reader = command.ExecuteReader();
                    BulkCopy.WriteToServer(reader);
                    reader.Close();
                }

                if (!string.IsNullOrEmpty(masterhistorytable))
                {
                    using (var command = GetDestinationSqlCommand(sqlTimeout, $"ALTER TABLE {masterhistorytable} SET(SYSTEM_VERSIONING=ON (HISTORY_TABLE = {tableName}, DATA_CONSISTENCY_CHECK=ON))"))
                    {
                        command.ExecuteNonQuery();
                    }
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

                try
                {
                    if (strDropIndex.Any())
                    {
                        using (var command = GetDestinationSqlCommand(sqlTimeout))
                        {
                            strDropIndex.ForEach(strDrop =>
                            {
                                command.CommandText = strDrop;
                                command.ExecuteNonQuery();
                            });
                        }
                    }
                }
                catch { }
            }
        }
    }
}
