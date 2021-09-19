﻿using Microsoft.SqlServer.Management.Common;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Windows.Forms;

namespace Sql2SqlCloner.Core.Data
{
    public class SqlDataTransfer : SqlTransfer
    {
        private readonly SqlBulkCopy bulkCopy;

        public SqlDataTransfer(string src, string dest)
        {
            if (!int.TryParse(ConfigurationManager.AppSettings["BatchSize"], out int batchSize))
            {
                batchSize = 5000;
            }

            bulkCopy = new SqlBulkCopy(dest, SqlBulkCopyOptions.KeepIdentity)
            {
                BatchSize = batchSize,
                NotifyAfter = batchSize * 2,
                BulkCopyTimeout = SqlTimeout
            };

            sourceConnection = new ServerConnection(new SqlConnection(src));
            destinationConnection = new ServerConnection(new SqlConnection(dest));
            destinationConnection.SqlConnectionObject.Open();
        }

        public void EnableTableConstraints(string tableName)
        {
            try
            {
                new SqlCommand($"ALTER TABLE {tableName} WITH CHECK CHECK CONSTRAINT all", destinationConnection.SqlConnectionObject)
                {
                    CommandTimeout = SqlTimeout
                }.ExecuteNonQuery();
            }
            catch { }
        }

        public void EnableAllDestinationConstraints()
        {
            var finished = false;
            var incompliantDataDeletion = ConfigurationManager.AppSettings["IncompliantDataDeletion"].ToLowerInvariant();
            while (!finished)
            {
                try
                {
                    EnableDestinationConstraints();
                    finished = true;
                }
                catch (Exception ex)
                {
                    if (incompliantDataDeletion != "true" && incompliantDataDeletion != "false")
                    {
                        incompliantDataDeletion = MessageBox.Show(
                                "Could not enable constraints. Delete incompliant data?", "Error",
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

                        using (SqlCommand command = destinationConnection.SqlConnectionObject.CreateCommand())
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
                                            lstDelete.Add(deletesentence += ")");

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
                                    lstDelete.Add(deletesentence += ")");
                            }
                            var deletedrows = 0;
                            using (SqlCommand cmdDelete = new SqlCommand())
                            {
                                cmdDelete.Connection = destinationConnection.SqlConnectionObject;
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
            if (connection.State != ConnectionState.Open)
                connection.Open();
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = @"select sche.name schemaName, tab.name tableName, col.name colName,
                    isnull(COLUMNPROPERTY(tab.OBJECT_ID,col.name,'IsComputed'),0) is_computed
                    from (sys.columns col
                    inner join sys.tables tab on tab.object_id=col.object_id
                    inner join sys.schemas sche on sche.schema_id=tab.schema_id)
                    left join sys.computed_columns ccl on ccl.object_id=col.object_id and ccl.column_id=col.column_id
                    where sche.name=@schema and tab.name=@table
                    and COLUMNPROPERTY(tab.OBJECT_ID,col.name,'IsComputed')=0 --exclude computed columns
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

        public bool TransferData(string table, string query)
        {
            SqlDataReader reader = null;
            try
            {
                bulkCopy.DestinationTableName = table;
                bulkCopy.ColumnMappings.Clear();
                GetMapping(sourceConnection.SqlConnectionObject, destinationConnection.SqlConnectionObject, table).ToList().
                    ForEach(columnName => bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(columnName, columnName)));

                SqlCommand myCommand = new SqlCommand(query, sourceConnection.SqlConnectionObject)
                {
                    CommandTimeout = SqlTimeout
                };
                reader = myCommand.ExecuteReader();
                bulkCopy.WriteToServer(reader);
                return true;
            }
            finally
            {
                if (reader?.IsClosed == false)
                    reader.Close();
            }
        }
    }
}
