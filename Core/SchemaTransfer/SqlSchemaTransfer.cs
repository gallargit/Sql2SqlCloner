using Babel;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.SqlParser.Parser;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sql2SqlCloner.Core.SchemaTransfer
{
    public class SqlSchemaTransfer : SqlTransfer
    {
        private const int SQL_2008_Version = 655;
        private const int SQL_2012_Version = 706;
        private const int SQL_2016_Version = 852;
        private const int TOKEN_DOT = 46;
        private readonly string sourceConnectionString;
        private readonly string destinationConnectionString;
        private Server sourceServer;
        private Server destinationServer;
        private Database sourceDatabase;
        private Database destinationDatabase;
        private readonly Transfer transfer;
        private readonly List<string> existingschemas = new List<string> { "dbo" };
        private readonly Dictionary<string, string> schemaauths = new Dictionary<string, string>();
        private readonly CancellationToken cancelToken;
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

        public SqlSchemaTransfer(string src, string dst, CancellationToken ct) : base(null)
        {
            cancelToken = ct;
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
                DestinationConnection.SqlConnectionObject).ExecuteNonQuery();
        }

        public void CreateObject(NamedSmoObject obj, bool dropIfExists, bool overrideCollation, bool useSourceCollation,
            bool alterInsteadOfCreate, bool? removeSchemaBinding)
        {
            using (SqlCommand command = new SqlCommand())
            {
                command.Connection = DestinationConnection.SqlConnectionObject;
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
                        var incompatSchema = "";
                        if (incobj.Value.IndexOf("@Schema=") > 0)
                        {
                            incompatSchema = incobj.Value.Substring(8 + incobj.Value.IndexOf("@Schema="));
                            while (incompatSchema.Length > 0 && !incompatSchema.EndsWith("'"))
                            {
                                incompatSchema = incompatSchema.Substring(0, incompatSchema.Length - 1);
                            }
                            if (incompatSchema != "")
                            {
                                incompatSchema += ".";
                            }
                        }
                        var objectName = incobj.Value.Substring(6 + incobj.Value.LastIndexOf("@Name="));
                        var currindex = 1;
                        while (objectName[currindex] != '\'')
                        {
                            currindex++;
                        }
                        objectName = objectName.Substring(0, currindex + 1);
                        incompatibleErrorMsg += " " + incompatSchema + objectName;
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
                        command.CommandText = $"IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'{schemaname}') EXEC('CREATE SCHEMA [{schemaname}]{GetSchemaAuthorization(obj.Name)}')";
                        command.ExecuteNonQuery();
                    }

                    var scriptRun = ConvertScriptProperCase(script, obj, overrideCollation, useSourceCollation,
                         alterInsteadOfCreate, removeSchemaBinding);

                    if (scriptRun.StartsWith("CREATE USER", StringComparison.InvariantCultureIgnoreCase))
                    {
                        string password = null;
                        if (scriptRun.IndexOf("WITH PASSWORD=", StringComparison.InvariantCultureIgnoreCase) >= 0)
                        {
                            password = scriptRun.Substring(16 + scriptRun.IndexOf("WITH PASSWORD", StringComparison.InvariantCultureIgnoreCase));
                            password = password.Substring(0, password.IndexOf("',"));
                        }
                        if (scriptRun.IndexOf("WITHOUT LOGIN", StringComparison.InvariantCultureIgnoreCase) < 0)
                        {
                            CreateLogin(obj.Name, password);
                        }
                        if (copyAzureUserToNonAzureDB)
                        {
                            scriptRun = scriptRun.Replace(" FROM EXTERNAL PROVIDER", "").Replace(" FROM  EXTERNAL PROVIDER", "");
                        }
                    }
                    try
                    {
                        command.CommandText = scriptRun;
                        command.ExecuteNonQuery();
                        if (obj.GetType() == typeof(Schema) && !existingschemas.Contains(obj.Name))
                        {
                            existingschemas.Add(obj.Name);
                        }
                        if (obj.GetType() == typeof(SecurityPolicy))
                        {
                            LstPostExecutionExecute.Add(scriptRun);
                        }
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

        private IEnumerable<TokenInfoExtended> ParseSqlScript(string sql, bool? removeSchemaBinding)
        {
            var tokensResult = new List<TokenInfoExtended>();
            var parseOptions = new ParseOptions();
            var scanner = new Scanner(parseOptions);

            int state = 0, lastTokenEnd = -1, token;

            scanner.SetSource(sql, 0);
            TokenInfoExtended previousToken = null;
            var previousSpace = false;
            var nextSeparator = "";
            while ((token = scanner.GetNext(ref state, out int start, out int end, out bool _, out bool _)) != (int)Tokens.EOF)
            {
                var currentToken = new TokenInfoExtended()
                {
                    StartIndex = start,
                    EndIndex = end,
                    SQL = sql.Substring(start, end - start + 1),
                    Type = (TokenType)token,
                    Token = token,
                    Separators = ""
                };
                if (tokensResult.Count > 0)
                {
                    if (string.IsNullOrEmpty(currentToken.SQL))
                    {
                        previousSpace = true;
                    }
                    else
                    {
                        if (previousSpace && !string.IsNullOrEmpty(currentToken.SQL))
                        {
                            previousSpace = false;
                        }
                        else
                        {
                            var position = start - 1;
                            var separators = new[] { '\n', '\r', '\t', ' ' };
                            while (separators.Contains(sql[position]) && position > lastTokenEnd)
                            {
                                position--;
                            }
                            position++;

                            if (position <= start - 1)
                            {
                                currentToken.Separators = sql.Substring(position, start - position);
                            }
                            currentToken.Separators = nextSeparator + currentToken.Separators;
                            nextSeparator = "";
                        }
                    }
                }

                if (removeSchemaBinding == true &&
                    token == (int)Tokens.TOKEN_ID && string.Equals(currentToken.SQL, "SCHEMABINDING", StringComparison.InvariantCultureIgnoreCase) &&
                    previousToken?.Token == (int)Tokens.TOKEN_WITH)
                {
                    previousToken.Separators += $"/* disable SCHEMABINDING {System.Diagnostics.Process.GetCurrentProcess().ProcessName} ";
                    nextSeparator = " disable SCHEMABINDING */";
                }

                tokensResult.Add(currentToken);
                lastTokenEnd = end;
                previousToken = currentToken;
            }

            return tokensResult;
        }

        private string ConvertScriptProperCase(string script, NamedSmoObject obj, bool overrideCollation, bool useSourceCollation,
            bool alterInsteadOfCreate, bool? removeSchemaBinding)
        {
            if (script != "SET QUOTED_IDENTIFIER ON" && script != "SET ANSI_NULLS ON")
            {
                var first_create = true;
                var next_collate = false;
                var creating_name = false;
                var objectname_already_replaced = false;
                var replaceNameObjects = new int[]
                {
                    (int)Tokens.TOKEN_TABLE,
                    (int)Tokens.TOKEN_VIEW,
                    (int)Tokens.TOKEN_PROCEDURE,
                    (int)Tokens.TOKEN_FUNCTION
                };
                string newScript;
                var sb = new StringBuilder();
                TokenInfoExtended previousToken = null;
                foreach (var currentToken in ParseSqlScript(script, removeSchemaBinding))
                {
                    //replace the scripted object's name with the actual name, explained below
                    if (creating_name &&
                        (currentToken.Token == (int)Tokens.TOKEN_ID || currentToken.Token == TOKEN_DOT) &&
                        !replaceNameObjects.Contains(currentToken.Token))
                    {
                        sb.Append(currentToken.Separators);
                        if (!objectname_already_replaced)
                        {
                            sb.Append(obj.ToString());
                            objectname_already_replaced = true;
                        }
                        continue;
                    }
                    if (creating_name &&
                        currentToken.Token != (int)Tokens.LEX_END_OF_LINE_COMMENT && currentToken.Token != (int)Tokens.LEX_MULTILINE_COMMENT &&
                        !replaceNameObjects.Contains(currentToken.Token))
                    {
                        creating_name = false;
                        if (!objectname_already_replaced)
                        {
                            sb.Append(obj.ToString());
                            objectname_already_replaced = true;
                        }
                    }

                    if (previousToken != null &&
                        overrideCollation && destinationDatabase.Collation != sourceDatabase.Collation &&
                        currentToken.Token == (int)Tokens.TOKEN_COLLATE &&
                        string.Equals(currentToken.SQL, "collate", StringComparison.InvariantCultureIgnoreCase))
                    {
                        next_collate = true;
                    }

                    sb.Append(currentToken.Separators);
                    if (next_collate && currentToken.Token != (int)Tokens.TOKEN_COLLATE)
                    {
                        next_collate = false;
                        if (currentToken.SQL.IndexOf("database_default", StringComparison.InvariantCultureIgnoreCase) < 0)
                        {
                            sb.Append(useSourceCollation ? sourceDatabase.Collation : destinationDatabase.Collation);
                        }
                        else
                        {
                            sb.Append(currentToken.SQL);
                        }
                    }
                    //replace the scripted object's name with the actual name, this is a workaround
                    //for an SMO bug, sometimes the object's name is scripted without schema
                    else if ((obj.GetType() == typeof(Table) ||
                              obj.GetType() == typeof(View) ||
                              obj.GetType() == typeof(StoredProcedure) ||
                              obj.GetType() == typeof(UserDefinedFunction)) &&
                        first_create &&
                        currentToken.Token == (int)Tokens.TOKEN_CREATE)
                    {
                        if (alterInsteadOfCreate)
                        {
                            sb.Append("ALTER");
                        }
                        else
                        {
                            sb.Append(currentToken.SQL.ToUpperInvariant()); //CREATE always in capitals
                        }

                        first_create = false;
                        creating_name = true;
                    }
                    else if (alterInsteadOfCreate && currentToken.Token == (int)Tokens.TOKEN_CREATE && first_create)
                    {
                        sb.Append("ALTER");
                        first_create = false;
                    }
                    else
                    {
                        sb.Append(currentToken.SQL);
                        if (creating_name && replaceNameObjects.Contains(currentToken.Token))
                        {
                            //replace object name at the top, it could be replaced here but
                            //if there are comments around, it wouldn't be done properly
                            //for example: CREATE VIEW /*comment*/ dbo.ViewName /*othercomment*/ AS...
                            objectname_already_replaced = false;
                        }
                    }

                    previousToken = currentToken;
                }
                newScript = sb.ToString();

                //Add "AUTHORIZATION" to schema objects, sometimes it's not added automatically
                if (obj.GetType() == typeof(Schema))
                {
                    var auth = GetSchemaAuthorization(obj.ToString());
                    if (!string.IsNullOrEmpty(auth) && newScript.EndsWith("'") && newScript.IndexOf(" AUTHORIZATION", StringComparison.InvariantCultureIgnoreCase) < 0)
                    {
                        newScript = newScript.Substring(0, newScript.Length - 1) + auth + "'";
                    }
                }

                return newScript;
            }
            return script;
        }

        private string GetSchemaAuthorization(string schemaname)
        {
            if (schemaauths.Count == 0)
            {
                using (var command = SourceConnection.SqlConnectionObject.CreateCommand())
                {
                    command.CommandText = "SELECT QUOTENAME(schema_name) AS schema_name, QUOTENAME(schema_owner) AS schema_owner FROM information_schema.schemata WHERE schema_owner<>'dbo'";
                    command.CommandType = CommandType.Text;
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            schemaauths[reader["schema_name"].ToString()] = reader["schema_owner"].ToString();
                        }
                    }
                }
            }
            if (schemaauths.ContainsKey(schemaname))
            {
                return $" AUTHORIZATION {schemaauths[schemaname]}";
            }
            return "";
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
                    foreach (Microsoft.SqlServer.Management.Smo.Parameter sub in currentProcedure.Parameters)
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
                    foreach (Microsoft.SqlServer.Management.Smo.Parameter sub in currentFunction.Parameters)
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
            var firstRun = true;
            RecreateObjects.Clear();
            RefreshDestinationObjects();
            foreach (var obj in DestinationObjects.Where(o => o.Object != null && (
                (o.Object is View && (o.Object as View).IsSchemaBound) ||
                (o.Object is UserDefinedFunction && (o.Object as UserDefinedFunction).IsSchemaBound) ||
                (o.Object is StoredProcedure && (o.Object as StoredProcedure).IsSchemaBound))
            ))
            {
                if (firstRun)
                {
                    RunInDestination("SELECT 'DROP SECURITY POLICY ' + QUOTENAME(s.name) + '.' + QUOTENAME(p.name) FROM sys.security_policies p INNER JOIN sys.schemas s ON p.schema_id= s.schema_id");
                    firstRun = false;
                }

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
            if (cancelToken.IsCancellationRequested)
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
            if (cancelToken.IsCancellationRequested)
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
            if (cancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (UserDefinedDataType item in db.UserDefinedDataTypes)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }
            if (cancelToken.IsCancellationRequested)
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
            if (cancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (XmlSchemaCollection item in db.XmlSchemaCollections)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }
            if (cancelToken.IsCancellationRequested)
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
            if (cancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (PartitionScheme item in db.PartitionSchemes)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }
            if (cancelToken.IsCancellationRequested)
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
            if (cancelToken.IsCancellationRequested)
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
            if (cancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (Default item in db.Defaults)
            {
                items.Add(new SqlSchemaObject { Name = $"{item.Schema}.{item.Name}", Object = item, Type = item.GetType().Name });
            }
            if (cancelToken.IsCancellationRequested)
            {
                return items;
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

            if (cancelToken.IsCancellationRequested)
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
                foreach (SecurityPolicy item in db.SecurityPolicies)
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
                if (cancelToken.IsCancellationRequested)
                {
                    return items;
                }

                foreach (ColumnMasterKey item in db.ColumnMasterKeys)
                {
                    items.Add(new SqlSchemaObject { Name = $"{item.Name}", Object = item, Type = item.GetType().Name });
                }
                if (cancelToken.IsCancellationRequested)
                {
                    return items;
                }
                foreach (ColumnEncryptionKey item in db.ColumnEncryptionKeys)
                {
                    items.Add(new SqlSchemaObject { Name = $"{item.Name}", Object = item, Type = item.GetType().Name });
                }
                if (cancelToken.IsCancellationRequested)
                {
                    return items;
                }

                foreach (ExternalDataSource item in db.ExternalDataSources)
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
            }
            if (cancelToken.IsCancellationRequested)
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
            if (cancelToken.IsCancellationRequested)
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
            if (cancelToken.IsCancellationRequested)
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
                new SqlCommand($"ALTER TABLE {obj} ENABLE CHANGE_TRACKING WITH(TRACK_COLUMNS_UPDATED = {(sTab.TrackColumnsUpdatedEnabled ? "ON" : "OFF")})", DestinationConnection.SqlConnectionObject).ExecuteNonQuery();
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
            DestinationObjects = GetSqlObjects(DestinationConnection, destinationDatabase);
        }

        public void RefreshAll()
        {
            RefreshSource();
            RefreshDestination();

            var sameserver = SameServer();
            var tskSource = Task.Run(() =>
            {
                sourceDatabase.PrefetchObjects(typeof(Table), new ScriptingOptions());
                if (!cancelToken.IsCancellationRequested)
                {
                    SourceObjects = GetSqlObjects(SourceConnection, sourceDatabase);
                }
            });
            if (sameserver || (ConfigurationManager.AppSettings["EnablePreload"]?.ToString().ToLower() != "true"))
            {
                tskSource.Wait();
            }

            var tskDestination = Task.Run(() =>
            {
                if (!cancelToken.IsCancellationRequested)
                {
                    DestinationObjects = GetSqlObjects(DestinationConnection, destinationDatabase);
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
                var transferDrop = new SqlSchemaTransfer(destinationConnectionString, destinationConnectionString, cancelToken);
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
                    //get all objects to remove placing schemas at the end and database triggers at the beginning
                    destinations = transferDrop.DestinationObjects.ConvertAll(o => o.Object).Where(p => !(p is Schema))
                           .Union(transferDrop.DestinationObjects.ConvertAll(s => s.Object)
                           .Where(t => t is Schema)).ToList();
                }
                lastCount = destinations.Count;
                if (remaining > 0)
                {
                    try
                    {
                        using (SqlCommand command = new SqlCommand())
                        {
                            command.Connection = transferDrop.DestinationConnection.SqlConnectionObject;
                            command.CommandTimeout = SqlTimeout;
                            if (lstDelete == null)
                            {
                                foreach (Table table in transferDrop.DestinationObjects.OfType<SqlSchemaTable>().Select(o => o.Object).Cast<Table>())
                                {
                                    if (cancelToken.IsCancellationRequested)
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
                                    if (cancelToken.IsCancellationRequested)
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

        private string FormatInstance(string instanceName)
        {
            if (instanceName == ".")
            {
                return "(local)";
            }
            return instanceName;
        }

        public string SourceCxInfo()
        {
            return FormatInstance(sourceConnection.ServerInstance) + "." + sourceConnection.DatabaseName;
        }

        public string DestinationCxInfo()
        {
            return FormatInstance(destinationConnection.ServerInstance) + "." + destinationConnection.DatabaseName;
        }
    }
}