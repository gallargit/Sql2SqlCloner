using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sql2SqlCloner.Core.SchemaTransfer
{
    public class SqlSchemaTransfer : SqlTransfer
    {
        private const int SQL_2008_Version = 655;
        private const int SQL_2012_Version = 706;
        private const int SQL_2016_Version = 852;
        private readonly string sourceConnectionString;
        private readonly string destinationConnectionString;
        private Server sourceServer;
        private Server destinationServer;
        private Database sourceDatabase;
        private Database destinationDatabase;
        private readonly Transfer transfer;
        private readonly List<string> existingschemas = new List<string> { "dbo" };
        private readonly CancellationToken token;
        public readonly bool SameDatabase;
        public List<SqlSchemaObject> SourceObjects { get; private set; }
        public List<SqlSchemaObject> DestinationObjects { get; private set; }
        public List<SqlSchemaObject> RecreateObjects { get; } = new List<SqlSchemaObject>();

        public bool IncludeExtendedProperties
        {
            get
            {
                return transfer.Options.ExtendedProperties;
            }
            set
            {
                transfer.Options.ExtendedProperties = value;
            }
        }
        public bool IncludePermissions
        {
            get
            {
                return transfer.Options.Permissions;
            }
            set
            {
                transfer.Options.Permissions = value;
            }
        }
        public bool IncludeDatabaseRoleMemberships
        {
            get
            {
                return transfer.Options.IncludeDatabaseRoleMemberships;
            }
            set
            {
                transfer.Options.IncludeDatabaseRoleMemberships = value;
            }
        }
        public bool NoCollation
        {
            get
            {
                return transfer.Options.NoCollation;
            }
            set
            {
                transfer.Options.NoCollation = value;
            }
        }
        public bool IgnoreFileGroup
        {
            get
            {
                return transfer.Options.NoFileGroup;
            }
            set
            {
                transfer.Options.NoFileGroup = value;
            }
        }

        public SqlSchemaTransfer(string src, string dst, CancellationToken ct)
        {
            token = ct;
            sourceConnectionString = src;
            destinationConnectionString = dst;
            RefreshAll();

            SameDatabase = SameServer() &&
                string.Equals(sourceConnection.DatabaseName, destinationConnection.DatabaseName, StringComparison.InvariantCultureIgnoreCase);

            transfer = new Transfer(sourceDatabase)
            {
                CopySchema = true,
                CopyData = false,
                DestinationServer = destinationConnection.ServerInstance,
                DestinationDatabase = destinationConnection.DatabaseName,
                DestinationLogin = destinationConnection.Login,
                DestinationPassword = destinationConnection.Password,
                DropDestinationObjectsFirst = false,
                DestinationLoginSecure = destinationConnectionString.IndexOf("integrated security=true", StringComparison.InvariantCultureIgnoreCase) >= 0,
                Options = new ScriptingOptions
                {
                    ContinueScriptingOnError = true,
                    NoFileGroup = false,
                    NoExecuteAs = true,
                    WithDependencies = false,
                    DriDefaults = true,
                    Default = true,
                    DriAll = false,
                    ExtendedProperties = false,
                    Permissions = false
                }
            };
        }

        private void InitServer(Server serv)
        {
            // set the default properties we want upon partial instantiation -
            // smo is *really* slow if you don't do this
            serv.SetDefaultInitFields(typeof(Schema), "IsSystemObject", "Name");
            serv.SetDefaultInitFields(typeof(Table), "Schema", "IsSystemObject", "Name");
            serv.SetDefaultInitFields(typeof(StoredProcedure), "IsSystemObject", "Name", "Owner");
            serv.SetDefaultInitFields(typeof(UserDefinedFunction), "IsSystemObject", "Name", "Owner");
            serv.SetDefaultInitFields(typeof(View), "Schema", "IsSystemObject", "Name");
            serv.SetDefaultInitFields(typeof(Column), "Identity", "Name");
            serv.SetDefaultInitFields(typeof(Index), "IndexKeyType", "Name");
            serv.SetDefaultInitFields(typeof(Trigger), "IsSystemObject", "Name");
            serv.SetDefaultInitFields(typeof(User), "IsSystemObject", "Name");
            serv.SetDefaultInitFields(typeof(DatabaseRole), "Name");
            serv.SetDefaultInitFields(typeof(UserDefinedDataType), "Name");
            serv.SetDefaultInitFields(typeof(UserDefinedTableType), "Schema", "Name");
            serv.SetDefaultInitFields(typeof(XmlSchemaCollection), "Name");
            serv.SetDefaultInitFields(typeof(Default), "Schema", "Name");
        }

        private void ResetTransfer()
        {
            transfer.Options.NoFileGroup = true;
            transfer.CopyAllDatabaseTriggers = false;
            transfer.CopyAllDefaults = false;
            transfer.CopyAllLogins = false;
            transfer.CopyAllObjects = false;
            transfer.CopyAllPartitionFunctions = false;
            transfer.CopyAllPartitionSchemes = false;
            transfer.CopyAllRoles = false;
            transfer.CopyAllRules = false;
            transfer.CopyAllSchemas = false;
            transfer.CopyAllSqlAssemblies = false;
            transfer.CopyAllStoredProcedures = false;
            transfer.CopyAllSynonyms = false;
            transfer.CopyAllTables = false;
            transfer.CopyAllUserDefinedAggregates = false;
            transfer.CopyAllUserDefinedDataTypes = false;
            transfer.CopyAllUserDefinedFunctions = false;
            transfer.CopyAllUserDefinedTypes = false;
            transfer.CopyAllUsers = false;
            transfer.CopyAllViews = false;
            transfer.CopyAllXmlSchemaCollections = false;
            transfer.CreateTargetDatabase = false;
            transfer.PrefetchObjects = false;
            transfer.SourceTranslateChar = false;
        }

        private void CreateLogin(string login, string password = null)
        {
            const string sql = @"IF NOT EXISTS
                (SELECT name FROM master.sys.server_principals WHERE name = '{0}')
                BEGIN
                    CREATE LOGIN [{0}] WITH PASSWORD=N'{1}'
                END";
            new SqlCommand(string.Format(sql, login,
                string.IsNullOrWhiteSpace(password) ?
                    (ConfigurationManager.AppSettings["DefaultPassword"] ?? "D3F@u1TP@s$W0rd!")
                    : password),
                destinationConnection.SqlConnectionObject).ExecuteNonQuery();
        }

        public void CreateObject(NamedSmoObject obj, bool dropIfExists, bool overrideCollation, bool useSourceCollation,
            bool alterInsteadOfCreate, bool? removeSchemaBinding)
        {
            using (SqlCommand command = new SqlCommand())
            {
                command.Connection = destinationConnection.SqlConnectionObject;
                if ((obj is User && new string[] { "dbo", "INFORMATION_SCHEMA", "guest", "sys" }.Contains(obj.Name)) ||
                    (obj is DatabaseRole && new string[] { "db_accessadmin", "db_backupoperator", "db_datareader", "db_datawriter",
                    "db_ddladmin", "db_denydatareader", "db_denydatawriter", "db_owner", "db_securityadmin", "public" }.Contains(obj.Name)))
                {
                    return;
                }

                ResetTransfer();
                transfer.ObjectList.Clear();
                transfer.ObjectList.Add(obj);

                var schema = obj.GetType().GetProperty("Schema");
                var namewithschema = obj.Name;
                if (schema != null)
                {
                    namewithschema = $"{obj.GetType().GetProperty("Schema").GetValue(obj, null)}.{namewithschema}";
                }
                if (dropIfExists)
                {
                    if (DestinationObjects.Any(d => d.Name == namewithschema || d.Name == obj.Name))
                    {
                        transfer.Options.ScriptDrops = true;
                        foreach (var script in transfer.ScriptTransfer())
                        {
                            command.CommandText = script;
                            command.ExecuteNonQuery();
                        }
                    }
                    transfer.Options.ScriptDrops = false;
                }
                bool copyAzureUserToNonAzureDB = (obj is User) &&
                    sourceServer.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase && destinationServer.DatabaseEngineType != DatabaseEngineType.SqlAzureDatabase;
                //system-versioned tables should have their PK created right away
                transfer.Options.DriPrimaryKey = obj is Table table && (GetTableProperty(table, "IsSystemVersioned") || GetTableProperty(table, "IsMemoryOptimized"));
                transfer.Options.IncludeIfNotExists = obj is Schema;

                var scripts = transfer.ScriptTransfer();
                var incompatibleErrorMsg = "";
                if (transfer.IncompatibleObjects.Count > 0)
                {
                    incompatibleErrorMsg = "Incompatible subitems in this object:";
                    foreach (var incobj in transfer.IncompatibleObjects)
                    {
                        incompatibleErrorMsg += " " + incobj.Value.Substring(6 + incobj.Value.LastIndexOf("@Name="));
                        incompatibleErrorMsg = incompatibleErrorMsg.Substring(0, incompatibleErrorMsg.Length - 1) + " (" + incobj.Type + ")";
                    }
                    transfer.IncompatibleObjects.Clear();
                }

                if (scripts.Count == 0)
                {
                    throw new Exception($"Could not script object {namewithschema}");
                }

                foreach (var script in scripts)
                {
                    if (script.Contains("Incompatible object not scripted"))
                    {
                        continue;
                    }
                    //create schema if not exists
                    var schemaname = obj.GetType().GetProperty("Schema")?.GetValue(obj, null).ToString();
                    if (!string.IsNullOrEmpty(schemaname) && !existingschemas.Contains(schemaname))
                    {
                        existingschemas.Add(schemaname);
                        command.CommandText = $"IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'{schemaname}') EXEC('CREATE SCHEMA [{schemaname}]')";
                        command.ExecuteNonQuery();
                    }
                    var scriptRun = ConvertScriptProperCase(script, obj);
                    if (alterInsteadOfCreate)
                    {
                        if (scriptRun.StartsWith("CREATE", StringComparison.InvariantCultureIgnoreCase))
                        {
                            scriptRun = "ALTER" + scriptRun.Substring(6);
                        }
                        if (removeSchemaBinding.HasValue)
                        {
                            if (removeSchemaBinding.Value)
                            {
                                scriptRun = scriptRun.Replace("WITH SCHEMABINDING", "/*WITH SCHEMABINDING*/");
                            }
                        }
                    }

                    if (scriptRun.StartsWith("CREATE USER", StringComparison.InvariantCultureIgnoreCase))
                    {
                        string password = null;
                        if (scriptRun.Contains("WITH PASSWORD="))
                        {
                            password = scriptRun.Substring(16 + scriptRun.IndexOf("WITH PASSWORD"));
                            password = password.Substring(0, password.IndexOf("',"));
                        }
                        CreateLogin(obj.Name, password);
                        if (copyAzureUserToNonAzureDB)
                        {
                            scriptRun = scriptRun.Replace(" FROM EXTERNAL PROVIDER", "").Replace(" FROM  EXTERNAL PROVIDER", "");
                        }
                    }
                    try
                    {
                        if (overrideCollation && destinationDatabase.Collation != sourceDatabase.Collation)
                        {
                            //change Collation for all objects: script.Replace("Latin1_General_100_CI_AS_SC_UTF8", "SQL_Latin1_General_CP1_CI_AS")
                            var indexCollate = scriptRun.IndexOf(" COLLATE ");

                            while (indexCollate > 0)
                            {
                                var scriptRun1 = scriptRun.Substring(0, indexCollate + 9);
                                var scriptRun2 = scriptRun.Substring(indexCollate + 9);
                                //prevent collate replacement for strings like ' COLLATE ' (with quotes)
                                var checkQuote = false;
                                try
                                {
                                    checkQuote = scriptRun1.Substring(scriptRun1.Length - 15).Substring(0, 7).
                                    ToCharArray().ToList().Contains('\'');
                                    checkQuote = scriptRun2.Substring(0, 5).
                                    ToCharArray().ToList().Contains('\'');
                                }
                                catch { }
                                if (!checkQuote && !scriptRun2.StartsWith("database_default", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    while (scriptRun2.Substring(0, 1) == " ")
                                    {
                                        scriptRun2 = scriptRun2.Substring(1);
                                    }
                                    while (scriptRun2.Substring(0, 1) != " " && scriptRun2.Substring(0, 1) != "," && scriptRun2.Substring(0, 1) != ")" && scriptRun2.Substring(0, 1) != "\r" && scriptRun2.Substring(0, 1) != "\n")
                                    {
                                        scriptRun2 = scriptRun2.Substring(1);
                                    }
                                    scriptRun = scriptRun1 +
                                        (useSourceCollation ? sourceDatabase.Collation : destinationDatabase.Collation)
                                        + scriptRun2;
                                }
                                indexCollate = scriptRun.IndexOf(" COLLATE ", scriptRun1.Length);
                            }
                        }
                        command.CommandText = scriptRun;
                        command.ExecuteNonQuery();
                    }
                    catch (Exception ex) when (ex.Message.StartsWith("Cannot find the user") && script.StartsWith("GRANT", StringComparison.OrdinalIgnoreCase))
                    {
                        //for non-existing users, do not fail at GRANT
                    }
                    catch (Exception ex) when (ex.Message.Contains("Property cannot be added. Property 'MS_Description' already exists"))
                    {
                        //for existing descriptions, do not fail
                    }
                    catch (Exception ex) when (ex.Message.Contains("Cannot alter the role") &&
                                                ex.Message.Contains("because it does not exist or you do not have permission."))
                    {
                        //non-existing roles will get their permissions later, do not fail
                    }
                }
                if (!string.IsNullOrEmpty(incompatibleErrorMsg))
                {
                    throw new Exception(incompatibleErrorMsg);
                }
            }
        }

        private string GetTypeDescription(string objType)
        {
            switch (objType)
            {
                case "SqlAssembly":
                    return "ASSEMBLY";
                case "FullTextCatalog":
                    return "FULLTEXT CATALOG";
                case "FullTextStopList":
                    return "FULLTEXT STOPLIST";
                case "DatabaseRole":
                    return "ROLE";
                case "UserDefinedDataType":
                    return "TYPE";
                case "UserDefinedTableType":
                    return "TYPE";
                case "XmlSchemaCollection":
                    return "XML SCHEMA COLLECTION";
                case "StoredProcedure":
                    return "PROCEDURE";
                case "UserDefinedFunction":
                    return "FUNCTION";
                case "PartitionFunction":
                    return "PARTITION FUNCTION";
                case "PartitionScheme":
                    return "PARTITION SCHEME";
                case "ExternalDataSource":
                    return "EXTERNAL DATA SOURCE";
                case "ColumnMasterKey":
                    return "COLUMN MASTER KEY";
                case "ColumnEncryptionKey":
                    return "COLUMN ENCRYPTION KEY";
            }
            return objType.ToUpperInvariant();
        }

        private string ConvertScriptProperCase(string oldScript, NamedSmoObject obj)
        {
            var typeName = GetTypeDescription(obj.GetType().Name);
            var newScript = oldScript;
            if (newScript.StartsWith("SET"))
            {
                return newScript;
            }
            var newline = new[] { '\n', '\r' };
            var separators = new[] { '\n', '\r', '\t', ' ', '(' };
            var separatorsExtended = new[] { '\n', '\r', '\t', ' ', '(', '[' };
            while (separators.Contains(newScript[0]))
            {
                newScript = newScript.Substring(1);
            }
            if ((obj is ColumnMasterKey || obj is ColumnEncryptionKey) &&
                newScript.StartsWith("USE", StringComparison.InvariantCultureIgnoreCase))
            {
                while (!newline.Contains(newScript[0]))
                {
                    newScript = newScript.Substring(1);
                }
                while (separators.Contains(newScript[0]))
                {
                    newScript = newScript.Substring(1);
                }
            }
            //CREATE
            if (newScript.StartsWith("CREATE", StringComparison.InvariantCultureIgnoreCase))
            {
                newScript = newScript.Substring(6);
                while (separators.Contains(newScript[0]))
                {
                    newScript = newScript.Substring(1);
                }
                //remove TABLE VIEW PROCEDURE...
                var spacesInName = typeName.Split(' ').Length;
                while (spacesInName > 0)
                {
                    while (!separatorsExtended.Contains(newScript[0]) && spacesInName > 0)
                    {
                        newScript = newScript.Substring(1);
                    }
                    spacesInName--;
                    if (spacesInName > 0)
                    {
                        newScript = newScript.Substring(1);
                    }
                }
                while (separators.Contains(newScript[0]))
                {
                    newScript = newScript.Substring(1);
                }
                //remove name
                if (newScript.StartsWith("["))
                {
                    while (newScript[0] != ']')
                    {
                        newScript = newScript.Substring(1);
                    }
                    newScript = newScript.Substring(1);
                    if (newScript.Length > 0)
                    {
                        if (newScript.StartsWith("."))
                        {
                            //this was the schema name, remove the object name
                            newScript = newScript.Substring(1);
                            if (newScript.StartsWith("["))
                            {
                                while (newScript[0] != ']')
                                {
                                    newScript = newScript.Substring(1);
                                }
                                newScript = newScript.Substring(1);
                            }
                            else
                            {
                                while (!separators.Contains(newScript[0]) && !newScript.StartsWith("--"))
                                {
                                    newScript = newScript.Substring(1);
                                }
                            }
                        }
                        else
                        {
                            while (!separators.Contains(newScript[0]))
                            {
                                newScript = newScript.Substring(1);
                            }
                        }
                    }
                }
                if (newScript.Length > 0)
                {
                    if (!newScript.StartsWith("@") && !newScript.StartsWith("("))
                    {
                        if (!newScript.StartsWith("--") && !newScript.StartsWith("/*"))
                        {
                            while (!separators.Contains(newScript[0]))
                            {
                                if (newScript.StartsWith("\""))
                                {
                                    newScript = newScript.Substring(1);
                                    newScript = newScript.Substring(1 + newScript.IndexOf("\""));
                                }
                                else
                                {
                                    if (newScript[0] != '@')
                                    {
                                        newScript = newScript.Substring(1);
                                    }
                                    else
                                    {
                                        newScript = " " + newScript;
                                    }
                                }
                            }
                        }
                    }
                }
                //new CREATE sentence
                newScript = "CREATE " + typeName + " " + obj.ToString() + newScript;
            }
            return newScript;
        }

        public void CopyExtendedProperties(IEnumerable<NamedSmoObject> namedSmoObjects)
        {
            var lst = namedSmoObjects.ToList();
            lst.Add(sourceDatabase);
            foreach (Schema schema in sourceDatabase.Schemas)
            {
                lst.Add(schema);
            }

            /* This is the fastest "copy properties" code, unfortunately it won't work on Azure databases

            lst = lst.Where(o => (o.GetType()) != typeof(Schema)).ToList();
            lst = lst.Where(o => (o.GetType()) != typeof(XmlSchemaCollection)).ToList();
            lst = lst.Where(o => (o.GetType()) != typeof(Trigger)).ToList();
            lst = lst.Where(o => (o.GetType()) != typeof(DatabaseDdlTrigger)).ToList();
            foreach (var o in lst.ToList())
            {
                if (o is Table currentTable)
                {
                    lst.Remove(o);
                    foreach (Index sub in currentTable.Indexes)
                    {
                        lst.Add(sub);
                    }
                    foreach (ForeignKey sub in currentTable.ForeignKeys)
                    {
                        lst.Add(sub);
                    }
                    foreach (Check sub in currentTable.Checks)
                    {
                        lst.Add(sub);
                    }
                }
                else if (o is View currentView)
                {
                    lst.Remove(o);
                    foreach (Column sub in currentView.Columns)
                    {
                        lst.Add(sub);
                    }
                    foreach (Index sub in currentView.Indexes)
                    {
                        lst.Add(sub);
                    }
                }
                else if (o is StoredProcedure currentProcedure)
                {
                    lst.Remove(o);
                }
                else if (o is UserDefinedFunction currentFunction)
                {
                    lst.Remove(o);
                    foreach (Column sub in currentFunction.Columns)
                    {
                        lst.Add(sub);
                    }
                    foreach (Check sub in currentFunction.Checks)
                    {
                        lst.Add(sub);
                    }
                }
            }
            */

            //extract all extended properties, SMO does not extract all of them by default (in azure environments they are not extracted at all)
            //therefore some will be duplicated
            foreach (var o in lst.ToList())
            {
                if (o is Table currentTable)
                {
                    foreach (Column sub in currentTable.Columns)
                    {
                        lst.Add(sub);
                        if (sub.DefaultConstraint != null)
                        {
                            lst.Add(sub.DefaultConstraint);
                        }
                    }
                    foreach (Index sub in currentTable.Indexes)
                    {
                        lst.Add(sub);
                    }
                    foreach (ForeignKey sub in currentTable.ForeignKeys)
                    {
                        lst.Add(sub);
                    }
                    foreach (Trigger sub in currentTable.Triggers)
                    {
                        lst.Add(sub);
                    }
                    foreach (Check sub in currentTable.Checks)
                    {
                        lst.Add(sub);
                    }
                }
                else if (o is View currentView)
                {
                    foreach (Column sub in currentView.Columns)
                    {
                        lst.Add(sub);
                    }
                    foreach (Index sub in currentView.Indexes)
                    {
                        lst.Add(sub);
                    }
                    foreach (Trigger sub in currentView.Triggers)
                    {
                        lst.Add(sub);
                    }
                }
                else if (o is StoredProcedure currentProcedure)
                {
                    foreach (Parameter sub in currentProcedure.Parameters)
                    {
                        lst.Add(sub);
                    }
                }
                else if (o is UserDefinedFunction currentFunction)
                {
                    foreach (Column sub in currentFunction.Columns)
                    {
                        lst.Add(sub);
                    }
                    foreach (Check sub in currentFunction.Checks)
                    {
                        lst.Add(sub);
                    }
                    foreach (Parameter sub in currentFunction.Parameters)
                    {
                        lst.Add(sub);
                    }
                }
            }

            foreach (IExtendedProperties item in lst.OfType<IExtendedProperties>().Where(p => p.ExtendedProperties?.Count > 0))
            {
                foreach (ExtendedProperty property in item.ExtendedProperties)
                {
                    try
                    {
                        CreateObject(property, true, false, false, false, null);
                    }
                    catch
                    {
                        //do not fail when copying extended properties
                    }
                }
            }

            //Clustered indexes properties cannot be obtained via SMO, do a direct copy instead
            CopyToDestination(@"SELECT 'EXEC sys.sp_addextendedproperty N''MS_Description'', N''' + CONVERT(VARCHAR(2000), p.[value]) + ''', ''SCHEMA'', N''' +
                QUOTENAME(SCHEMA_NAME(t.schema_id)) + ''', ''TABLE'', N''' + t.name + ''', ''INDEX'', N''' + i.[name]+ ''''
                FROM sys.indexes i INNER JOIN sys.extended_properties p ON p.major_id=i.object_id AND p.minor_id=i.index_id
                INNER JOIN sys.tables t ON t.object_id = i.object_id
                WHERE p.class=7 AND (i.type=1 OR is_primary_key=1)");
        }

        public void RemoveSchemaBindingFromDestination()
        {
            RecreateObjects.Clear();
            RefreshDestinationObjects();
            foreach (var obj in DestinationObjects.Where(o => o.Object != null && (
                (o.Object is View && (o.Object as View).IsSchemaBound) ||
                (o.Object is UserDefinedFunction && (o.Object as UserDefinedFunction).IsSchemaBound) ||
                (o.Object is StoredProcedure && (o.Object as StoredProcedure).IsSchemaBound))
            ))
            {
                RecreateObjects.Add(SourceObjects.Single(s => s.Name == obj.Name && s.Type == obj.Type));
                try
                {
                    if (DestinationObjects.Any(d => d.Type == obj.Type && d.Name == obj.Name))
                    {
                        CreateObject(obj.Object, false, false, false, true, true);
                    }
                }
                catch //"not found"
                {
                }
            }
            RefreshDestinationObjects();
        }

        public void ReAddSchemaBindingToDestination()
        {
            var previousErrors = -1;
            var currentErrors = 0;
            while (currentErrors != previousErrors)
            {
                previousErrors = currentErrors;
                currentErrors = 0;
                foreach (var item in RecreateObjects)
                {
                    try
                    {
                        CreateObject(item.Object, false, false, false, true, false);
                    }
                    catch
                    {
                        currentErrors++;
                    }
                }
            }
        }

        public void CopySchemaAuthorization()
        {
            CopyToDestination(@"SELECT 'ALTER AUTHORIZATION ON SCHEMA :: ' + QUOTENAME(s.name) + ' TO ' + QUOTENAME(u.name)
                                FROM sys.schemas s INNER JOIN sys.sysusers u
                                    ON u.uid = s.principal_id
                                WHERE s.name NOT IN('public','dbo','guest','INFORMATION_SCHEMA','sys')
                                    and (u.uid & 16384 = 0)");
        }

        //not needed by now
        public void CopyPermissions()
        {
            CopyToDestination(@"SELECT 'GRANT ' + permission_name COLLATE DATABASE_DEFAULT + ' ON ' +
                isnull(schema_name(o.uid)+'.','') + OBJECT_NAME(major_id) +
                ' TO ' + QUOTENAME(USER_NAME(grantee_principal_id)) as grantStatement
                FROM sys.database_permissions dp
                LEFT OUTER JOIN sysobjects o ON o.id = dp.major_id
                WHERE OBJECT_NAME(major_id) is not null");
        }

        public void CopyRolePermissions()
        {
            CopyToDestination(@"SELECT 'EXEC sp_addrolemember N'''+ DP1.name + ''', N''' + isnull (DP2.name, 'No members') + ''''
                FROM sys.database_role_members AS DRM
                RIGHT OUTER JOIN sys.database_principals AS DP1
                   ON DRM.role_principal_id = DP1.principal_id
                LEFT OUTER JOIN sys.database_principals AS DP2
                   ON DRM.member_principal_id = DP2.principal_id
                WHERE DP1.type='R' AND DP1.is_fixed_role=0 AND DP2.is_fixed_role=0");
        }

        private List<SqlSchemaObject> GetSqlObjects(ServerConnection connection, Database db)
        {
            var items = new List<SqlSchemaObject>();
            var isRunningMinimumSQL2008 = db.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase;
            if (!isRunningMinimumSQL2008)
            {
                isRunningMinimumSQL2008 = db.Version >= SQL_2008_Version;
            }
            var isRunningMinimumSQL2012 = db.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase;
            if (!isRunningMinimumSQL2012)
            {
                isRunningMinimumSQL2012 = db.Version >= SQL_2012_Version;
            }
            var isRunningMinimumSQL2016 = db.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase;
            if (!isRunningMinimumSQL2016)
            {
                isRunningMinimumSQL2016 = db.Version >= SQL_2016_Version;
            }

            foreach (SqlAssembly item in db.Assemblies)
            {
                if (!item.IsSystemObject)
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
            }
            foreach (FullTextCatalog item in db.FullTextCatalogs)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }

            if (isRunningMinimumSQL2008)
            {
                foreach (FullTextStopList item in db.FullTextStopLists)
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            foreach (User item in db.Users)
            {
                if (!item.IsSystemObject)
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
            }
            foreach (DatabaseRole item in db.Roles)
            {
                if (!item.IsFixedRole && item.Name != "public")
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            foreach (Schema item in db.Schemas)
            {
                if (!item.IsSystemObject)
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            foreach (UserDefinedDataType item in db.UserDefinedDataTypes)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            if (isRunningMinimumSQL2008)
            {
                foreach (UserDefinedTableType item in db.UserDefinedTableTypes)
                {
                    items.Add(new SqlSchemaObject { Name = $"{item.Schema}.{item.Name}", Object = item, Type = item.GetType().Name });
                }
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            foreach (XmlSchemaCollection item in db.XmlSchemaCollections)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            foreach (PartitionFunction item in db.PartitionFunctions)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }
            foreach (Synonym item in db.Synonyms)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            foreach (PartitionScheme item in db.PartitionSchemes)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            if (isRunningMinimumSQL2012)
            {
                foreach (Sequence item in db.Sequences)
                {
                    items.Add(new SqlSchemaObject { Name = $"{item.Schema}.{item.Name}", Object = item, Type = item.GetType().Name });
                }
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            var alwaysIncludeTables = ConfigurationManager.AppSettings["AlwaysIncludeTables"];
            IList<string> alwaysIncludeTablesList = new List<string>();
            if (!string.IsNullOrEmpty(alwaysIncludeTables))
            {
                alwaysIncludeTablesList = alwaysIncludeTables.Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).
                Select(s => s.Replace("[", "").Replace("]", "")).ToList();
            }
            var dicTables = new Dictionary<string, long>(StringComparer.InvariantCultureIgnoreCase);
            using (var command = connection.SqlConnectionObject.CreateCommand())
            {
                command.CommandText = @"SELECT (SCHEMA_NAME(sOBJ.schema_id)) + '.' + (sOBJ.name) AS TableName ,SUM(sPTN.Rows) AS RowCountNum
                    FROM
                    sys.objects AS sOBJ
                    INNER JOIN sys.partitions AS sPTN
                    ON sOBJ.object_id = sPTN.object_id
                    WHERE
                    sOBJ.type = 'U'
                    AND sOBJ.is_ms_shipped = 0
                    AND index_id < 2
                    GROUP BY
                    sOBJ.schema_id, sOBJ.name
                    ORDER BY 2 DESC";
                command.CommandType = CommandType.Text;
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        dicTables[reader["TableName"].ToString()] = (long)reader["RowCountNum"];
                    }
                }
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            foreach (Default item in db.Defaults)
            {
                items.Add(new SqlSchemaObject { Name = $"{item.Schema}.{item.Name}", Object = item, Type = item.GetType().Name });
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            if (isRunningMinimumSQL2016)
            {
                foreach (ColumnMasterKey item in db.ColumnMasterKeys)
                {
                    items.Add(new SqlSchemaObject { Name = $"{item.Name}", Object = item, Type = item.GetType().Name });
                }
                if (token.IsCancellationRequested)
                {
                    return items;
                }
                foreach (ColumnEncryptionKey item in db.ColumnEncryptionKeys)
                {
                    items.Add(new SqlSchemaObject { Name = $"{item.Name}", Object = item, Type = item.GetType().Name });
                }
                if (token.IsCancellationRequested)
                {
                    return items;
                }
            }

            foreach (Table item in db.Tables)
            {
                var tableName = $"{item.Schema}.{item.Name}";
                if (!item.IsSystemObject || alwaysIncludeTablesList.Any(s => s.Equals(tableName, StringComparison.OrdinalIgnoreCase)))
                {
                    var table = new SqlSchemaTable
                    {
                        Name = tableName,
                        Object = item,
                        Type = item.GetType().Name,
                        RowCount = dicTables[tableName],
                        HasRelationships = item.ForeignKeys.Count > 0
                    };
                    items.Add(table);
                    foreach (Trigger trigger in item.Triggers)
                    {
                        items.Add(new SqlSchemaObject { Parent = table, Name = trigger.Name, Object = trigger, Type = trigger.GetType().Name });
                    }
                }
            }

            if (token.IsCancellationRequested)
            {
                return items;
            }

            foreach (View item in db.Views)
            {
                if (!item.IsSystemObject)
                {
                    var view = new SqlSchemaObject
                    {
                        Name = $"{item.Schema}.{item.Name}",
                        Object = item,
                        Type = item.GetType().Name
                    };
                    items.Add(view);
                    foreach (Trigger trigger in item.Triggers)
                    {
                        items.Add(new SqlSchemaObject { Parent = view, Name = trigger.Name, Object = trigger, Type = trigger.GetType().Name });
                    }
                }
            }
            if (isRunningMinimumSQL2016)
            {
                foreach (ExternalDataSource item in db.ExternalDataSources)
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            foreach (UserDefinedFunction item in db.UserDefinedFunctions)
            {
                if (!item.IsSystemObject || item.Owner != "sys")
                {
                    items.Add(new SqlSchemaObject { Name = $"{item.Schema}.{item.Name}", Object = item, Type = item.GetType().Name });
                }
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            foreach (StoredProcedure item in db.StoredProcedures)
            {
                if (!item.IsSystemObject || item.Owner != "sys")
                {
                    items.Add(new SqlSchemaObject { Name = $"{item.Schema}.{item.Name}", Object = item, Type = item.GetType().Name });
                }
            }
            if (token.IsCancellationRequested)
            {
                return items;
            }

            foreach (DatabaseDdlTrigger item in db.Triggers)
            {
                if (!item.IsSystemObject)
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
            }
            return items;
        }

        private TableViewBase GetDestinationTableOrViewByName(NamedSmoObject obj)
        {
            if (obj is Table tb)
            {
                try
                {
                    return destinationDatabase.Tables[tb.Name, tb.Schema];
                }
                catch
                {
                    throw new Exception($"Table {tb.Owner}.{tb.Schema} not found");
                }
            }
            else
            {
                var vw = obj as View;
                try
                {
                    return destinationDatabase.Views[vw.Name, vw.Schema] ??
                        (TableViewBase)destinationDatabase.Views[vw.Name, vw.Owner];
                }
                catch
                {
                    throw new Exception($"View {vw.Owner}.{vw.Name} not found");
                }
            }
        }

        public bool GetTableProperty(Table t, string propertyName)
        {
            Database db = t.Parent;
            var isRunningMinimumSQL2012 = db.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase;
            if (!isRunningMinimumSQL2012)
            {
                isRunningMinimumSQL2012 = db.Version >= SQL_2012_Version;
            }
            var isRunningMinimumSQL2016 = db.DatabaseEngineType == DatabaseEngineType.SqlAzureDatabase;
            if (!isRunningMinimumSQL2016)
            {
                isRunningMinimumSQL2016 = db.Version >= SQL_2016_Version;
            }
            if (propertyName == nameof(t.ChangeTrackingEnabled) && !isRunningMinimumSQL2012)
            {
                return false;
            }
            else if ((propertyName == nameof(t.IsMemoryOptimized) || propertyName == nameof(t.IsSystemVersioned))
                && !isRunningMinimumSQL2016)
            {
                return false;
            }
            else
            {
                return (bool)t.GetType().GetProperty(propertyName).GetValue(t);
            }
        }
        public void ApplyIndexes(NamedSmoObject obj, bool CopyFullText)
        {
            var destinationTable = GetDestinationTableOrViewByName(obj);
            if (destinationTable == null)
            {
                return;
            }
            //clustered indexes should be processed first
            var indexesSorted = new List<Index>();
            foreach (Index srcindex in (obj as TableViewBase)?.Indexes)
            {
                if (srcindex.IndexType == IndexType.ClusteredIndex)
                {
                    indexesSorted.Insert(0, srcindex);
                }
                else
                {
                    indexesSorted.Add(srcindex);
                }
            }
            foreach (Index srcindex in indexesSorted)
            {
                var existingIndex = false;
                if (obj is View)
                {
                    foreach (Index destIndex in (destinationTable as View)?.Indexes)
                    {
                        if (destIndex.Name == srcindex.Name)
                        {
                            //index already exists in this view
                            existingIndex = true;
                            break;
                        }
                    }
                }
                if (existingIndex)
                {
                    continue;
                }
                //primary keys for system-versioned tables are already created
                if (destinationTable is Table table && GetTableProperty(table, "IsSystemVersioned") && !GetTableProperty(table, "IsMemoryOptimized")
                    && (srcindex.IndexKeyType == IndexKeyType.DriPrimaryKey))
                {
                    continue;
                }
                if (destinationTable is Table table2 && GetTableProperty(table2, "IsMemoryOptimized"))
                {
                    continue;
                }
                try
                {
                    Index index = new Index(destinationTable, srcindex.Name)
                    {
                        IndexKeyType = srcindex.IndexKeyType,
                        IsClustered = srcindex.IsClustered,
                        IsUnique = srcindex.IsUnique,
                        CompactLargeObjects = srcindex.CompactLargeObjects,
                        IgnoreDuplicateKeys = srcindex.IgnoreDuplicateKeys,
                        IsFullTextKey = srcindex.IsFullTextKey,
                        PadIndex = srcindex.PadIndex,
                        FillFactor = srcindex.FillFactor,
                        DisallowPageLocks = srcindex.DisallowPageLocks,
                        DisallowRowLocks = srcindex.DisallowRowLocks,
                    };

                    //FilterDefinition property is not available for all SQL Server editions
                    try
                    {
                        index.FilterDefinition = srcindex.FilterDefinition;
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(srcindex.FileGroup))
                    {
                        index.FileGroup = "PRIMARY";
                        if (srcindex.FileGroup != "PRIMARY")
                        {
                            foreach (FileGroup fg in destinationDatabase.FileGroups)
                            {
                                if (fg.Name == srcindex.FileGroup)
                                {
                                    index.FileGroup = destinationDatabase.FileGroups[fg.Name].Name;
                                    break;
                                }
                            }
                        }
                    }

                    foreach (IndexedColumn srccol in srcindex.IndexedColumns)
                    {
                        index.IndexedColumns.Add(new IndexedColumn(index, srccol.Name, srccol.Descending)
                        {
                            IsIncluded = srccol.IsIncluded
                        });
                    }

                    if (srcindex.IndexType == IndexType.SecondaryXmlIndex)
                    {
                        index.IndexType = srcindex.IndexType;
                        index.ParentXmlIndex = srcindex.ParentXmlIndex;
                        index.SecondaryXmlIndexType = srcindex.SecondaryXmlIndexType;
                    }

                    if (srcindex.IndexType == IndexType.ClusteredColumnStoreIndex || srcindex.IndexType == IndexType.NonClusteredColumnStoreIndex)
                    {
                        index.IndexType = srcindex.IndexType;
                    }

                    if (obj is Table && destinationDatabase.Tables[obj.Name]?.FileGroup != null)
                    {
                        index.FileGroup = destinationDatabase.Tables[obj.Name].FileGroup;
                    }
                    index.Create();
                }
                catch
                {
                    throw;
                }
            }
            if (obj is Table sTab && GetTableProperty(sTab, "ChangeTrackingEnabled"))
            {
                /*
                this does not work, direct SQL is used instead
                (GetDestinationTableOrViewByName(sTable) as Table).ChangeTrackingEnabled = true;
                if (sTab.TrackColumnsUpdatedEnabled)
                {
                    (GetDestinationTableOrViewByName(sTable) as Table).TrackColumnsUpdatedEnabled = true;
                }
                */
                new SqlCommand($"ALTER TABLE {obj} ENABLE CHANGE_TRACKING WITH(TRACK_COLUMNS_UPDATED = {(sTab.TrackColumnsUpdatedEnabled ? "ON" : "OFF")})", destinationConnection.SqlConnectionObject).ExecuteNonQuery();
            }

            if (CopyFullText)
            {
                FullTextIndex fulltextind = (obj as TableViewBase)?.FullTextIndex;
                if (fulltextind != null)
                {
                    try
                    {
                        FullTextIndex index = new FullTextIndex(destinationTable)
                        {
                            CatalogName = fulltextind.CatalogName,
                            FilegroupName = fulltextind.FilegroupName,
                            SearchPropertyListName = fulltextind.SearchPropertyListName,
                            StopListName = fulltextind.StopListName,
                            StopListOption = fulltextind.StopListOption,
                            UniqueIndexName = fulltextind.UniqueIndexName,
                            UserData = fulltextind.UserData
                        };

                        foreach (FullTextIndexColumn srccol in fulltextind.IndexedColumns)
                        {
                            index.IndexedColumns.Add(new FullTextIndexColumn(index, srccol.Name)
                            {
                                TypeColumnName = srccol.TypeColumnName
                            });
                        }
                        index.Create();
                    }
                    catch
                    {
                        throw;
                    }
                }
            }
        }

        public void ApplyForeignKeys(NamedSmoObject obj, bool disableNotForReplication)
        {
            foreach (ForeignKey sourcefk in (obj as Table)?.ForeignKeys)
            {
                try
                {
                    ForeignKey foreignkey = new ForeignKey(GetDestinationTableOrViewByName(obj) as Table, sourcefk.Name)
                    {
                        DeleteAction = sourcefk.DeleteAction,
                        IsChecked = sourcefk.IsChecked,
                        IsEnabled = sourcefk.IsEnabled,
                        NotForReplication = !disableNotForReplication && sourcefk.NotForReplication,
                        ReferencedTable = sourcefk.ReferencedTable,
                        ReferencedTableSchema = sourcefk.ReferencedTableSchema,
                        UpdateAction = sourcefk.UpdateAction
                    };

                    foreach (ForeignKeyColumn scol in sourcefk.Columns)
                    {
                        foreignkey.Columns.Add(new ForeignKeyColumn(foreignkey, scol.Name, scol.ReferencedColumn));
                    }

                    foreignkey.Create();
                }
                catch
                {
                    throw;
                }
            }
        }

        public void ApplyChecks(NamedSmoObject obj, bool disableNotForReplication)
        {
            foreach (Check chkConstr in (obj as Table)?.Checks)
            {
                try
                {
                    new Check(GetDestinationTableOrViewByName(obj), chkConstr.Name)
                    {
                        IsChecked = chkConstr.IsChecked,
                        IsEnabled = chkConstr.IsEnabled,
                        NotForReplication = !disableNotForReplication && chkConstr.NotForReplication,
                        Text = chkConstr.Text
                    }.Create();
                }
                catch
                {
                    throw;
                }
            }
        }

        private bool SameServer()
        {
            return sourceServer.ConnectionContext.TrueName == destinationServer.ConnectionContext.TrueName &&
            sourceServer.InstanceName == destinationServer.InstanceName &&
            sourceServer.BuildNumber == destinationServer.BuildNumber &&
            sourceServer.VersionString == destinationServer.VersionString;
        }

        public void RefreshSource()
        {
            sourceConnection = new ServerConnection(new SqlConnection(sourceConnectionString));
            sourceServer = new Server(sourceConnection);
            InitServer(sourceServer);
            sourceDatabase = sourceServer.Databases[sourceConnection.DatabaseName];
        }

        public void RefreshDestination()
        {
            destinationConnection = new ServerConnection(new SqlConnection(destinationConnectionString));
            destinationServer = new Server(destinationConnection);
            InitServer(destinationServer);
            destinationDatabase = destinationServer.Databases[destinationConnection.DatabaseName];
        }

        public void RefreshDestinationObjects()
        {
            RefreshDestination();
            DestinationObjects = GetSqlObjects(destinationConnection, destinationDatabase);
        }

        public void RefreshAll()
        {
            RefreshSource();
            RefreshDestination();

            var sameserver = SameServer();
            var tskSource = Task.Run(() =>
            {
                sourceDatabase.PrefetchObjects(typeof(Table), new ScriptingOptions());
                if (!token.IsCancellationRequested)
                {
                    SourceObjects = GetSqlObjects(sourceConnection, sourceDatabase);
                }
            });
            if (sameserver || (ConfigurationManager.AppSettings["EnablePreload"]?.ToString().ToLower() != "true"))
            {
                tskSource.Wait();
            }

            var tskDestination = Task.Run(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    DestinationObjects = GetSqlObjects(destinationConnection, destinationDatabase);
                }
            });

            tskSource.Wait();
            tskDestination.Wait();
        }

        public void ClearDestinationDatabase(List<SqlSchemaObject> lstDelete = null, Action<NamedSmoObject> callback = null)
        {
            var lastError = "";
            if (DestinationObjects.Count == 0 || lstDelete?.Count == 0)
            {
                return;
            }

            var lastCount = 0;
            int remaining = DestinationObjects.Count;
            //it usually happens that drop scripts are not generated if the source server is different from the
            //destination server (property "transfer.Scripter" is always the source server, not the destination)
            //therefore instead of using the "this" object a new one is created and all of the drop operations
            //will be performed there
            while (remaining > 0 && lastCount != remaining)
            {
                var transferDrop = new SqlSchemaTransfer(destinationConnectionString, destinationConnectionString, token);
                transferDrop.transfer.Options.ScriptDrops = true;
                transferDrop.transfer.Options.IncludeIfNotExists = true;
                transferDrop.transfer.Options.ContinueScriptingOnError = true;
                transferDrop.transfer.CopyAllObjects = false;
                transferDrop.ResetTransfer();
                List<NamedSmoObject> destinations;
                if (lstDelete != null)
                {
                    destinations = lstDelete.ConvertAll(o => o.Object);
                }
                else
                {
                    //get all objects to remove placing schemas at the end
                    destinations = transferDrop.DestinationObjects.ConvertAll(o => o.Object).Where(p => !(p is Schema))
                           .Union(transferDrop.DestinationObjects.ConvertAll(s => s.Object)
                           .Where(t => t is Schema)).ToList();
                }
                lastCount = destinations.Count;
                if (remaining > 0)
                {
                    try
                    {
                        if (lstDelete == null)
                        {
                            transferDrop.DisableAllDestinationConstraints();
                        }

                        using (SqlCommand command = new SqlCommand())
                        {
                            command.Connection = transferDrop.destinationConnection.SqlConnectionObject;
                            command.CommandTimeout = SqlTimeout;
                            if (lstDelete == null)
                            {
                                foreach (Table table in transferDrop.DestinationObjects.OfType<SqlSchemaTable>().Select(o => o.Object).Cast<Table>())
                                {
                                    if (token.IsCancellationRequested)
                                    {
                                        return;
                                    }

                                    if (GetTableProperty(table, "IsSystemVersioned"))
                                    {
                                        table.IsSystemVersioned = false;
                                    }
                                    if (GetTableProperty(table, "IsMemoryOptimized"))
                                    {
                                        table.IsMemoryOptimized = false;
                                    }
                                    foreach (ForeignKey fk in table.ForeignKeys)
                                    {
                                        destinations.Insert(0, fk);
                                    }
                                }
                            }
                            var processed = destinations.Count;
                            while (processed > 0)
                            {
                                processed = 0;
                                foreach (NamedSmoObject obj in destinations.ToList())
                                {
                                    if (token.IsCancellationRequested)
                                    {
                                        return;
                                    }

                                    try
                                    {
                                        transferDrop.transfer.ObjectList.Clear();
                                        transferDrop.transfer.ObjectList.Add(obj);
                                        foreach (string scriptRun in transferDrop.transfer.ScriptTransfer())
                                        {
                                            command.CommandText = scriptRun;
                                            command.ExecuteNonQuery();
                                            destinations.Remove(obj);
                                            processed++;
                                            callback?.Invoke(obj);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        if (!string.IsNullOrEmpty(ex.Message))
                                        {
                                            lastError = $". {ex.Message} Affected object: {obj.Name}";
                                        }
                                    }
                                }
                                if (lstDelete != null)
                                {
                                    processed = remaining = 0;//do not keep on retrying if just deleting specific objects
                                }
                                else
                                {
                                    transferDrop.RefreshDestinationObjects();
                                    destinations = transferDrop.DestinationObjects.ConvertAll(o => o.Object);
                                }
                                if (destinations.Count == 0)
                                {
                                    processed = 0; //finished
                                }
                            }
                        }
                        try
                        {
                            if (lstDelete == null)
                            {
                                transferDrop.destinationDatabase.RemoveFullTextCatalogs();
                            }
                        }
                        catch { }
                    }
                    catch
                    {
                        throw;
                    }
                    //refresh local object before exiting
                    RefreshDestinationObjects();
                    if (lstDelete == null)
                    {
                        remaining = DestinationObjects.Count;
                    }
                }
            }
            if (lstDelete == null && DestinationObjects.Count > 0)
            {
                throw new Exception($"Could not delete items{lastError}");
            }
        }
    }
}
