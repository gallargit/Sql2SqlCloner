using Babel;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.SqlParser.Parser;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sql2SqlCloner.Core.SchemaTransfer
{
    public class SqlSchemaTransfer : SqlTransfer
    {
        private const int TOKEN_DOT = 46;
        private const int TOKEN_LEFT_PARENTHESIS = 40;
        private const int TOKEN_RIGHT_PARENTHESIS = 41;
        private const int TOKEN_COMMA = 44;
        private const int TOKEN_MINUS = 45;
        private const int TOKEN_SEMICOLON = 59;
        private readonly object lockFlag = new object();
        private Server sourceServer;
        private Server destinationServer;
        private Database sourceDatabase;
        private Database destinationDatabase;
        private readonly string DACconnection;
        private readonly Transfer transfer;
        private readonly IList<string> existingschemas = new List<string> { "dbo" };
        private readonly Dictionary<string, string> schemaauths = new Dictionary<string, string>();
        public CancellationToken CancelToken { get; }
        public readonly bool SameDatabase;

        public IList<SqlSchemaObject> SourceObjects { get; set; }
        public IList<SqlSchemaObject> DestinationObjects { get; set; }
        public IList<SqlSchemaObject> RecreateObjects { get; } = new List<SqlSchemaObject>();

        public bool IncludePermissions
        {
            get => transfer.Options.Permissions;
            set => transfer.Options.Permissions = value;
        }

        public bool IncludeDatabaseRoleMemberships
        {
            get => transfer.Options.IncludeDatabaseRoleMemberships;
            set => transfer.Options.IncludeDatabaseRoleMemberships = value;
        }

        public bool NoCollation
        {
            get => transfer.Options.NoCollation;
            set => transfer.Options.NoCollation = value;
        }

        public bool IgnoreFileGroup
        {
            get => transfer.Options.NoFileGroup;
            set => transfer.Options.NoFileGroup = value;
        }

        public SqlSchemaTransfer(string src, string dst, bool skipPreload, string DACConnectionString, CancellationToken ct) : base(src, dst, null)
        {
            DACconnection = DACConnectionString;
            CancelToken = ct;
            RefreshAll(skipPreload);

            SameDatabase = SameServer() &&
                           string.Equals(SourceDatabaseName, DestinationDatabaseName,
                               StringComparison.InvariantCultureIgnoreCase);

            transfer = new Transfer(sourceDatabase)
            {
                CopySchema = true,
                CopyData = false,
                DestinationServer = DestinationConnection.ServerInstance,
                DestinationDatabase = DestinationDatabaseName,
                DestinationLogin = DestinationConnection.Login,
                DestinationPassword = DestinationConnection.Password,
                DropDestinationObjectsFirst = false,
                DestinationLoginSecure = DestinationConnectionString.IndexOf("integrated security=true",
                    StringComparison.InvariantCultureIgnoreCase) >= 0,
                Options = new ScriptingOptions
                {
                    AnsiPadding = true,
                    AnsiFile = true,
                    Bindings = true,
                    ContinueScriptingOnError = true,
                    Default = true,
                    DriAll = false,
                    DriDefaults = true,
                    ExtendedProperties = false,
                    NoExecuteAs = true,
                    NoFileGroup = false,
                    Permissions = false,
                    WithDependencies = false
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

        public IEnumerable<string> GetObjectSource(NamedSmoObject obj)
        {
            transfer.Options.Indexes =
                transfer.Options.ClusteredIndexes =
                    transfer.Options.ColumnStoreIndexes =
                        transfer.Options.DriIndexes =
                            transfer.Options.FullTextIndexes =
                                transfer.Options.NonClusteredIndexes =
                                    transfer.Options.SpatialIndexes =
                                        transfer.Options.XmlIndexes = true;
            ResetTransfer();
            transfer.ObjectList.Clear();
            transfer.ObjectList.Add(obj);
            transfer.IncompatibleObjects.Clear();
            return transfer.ScriptTransfer().Cast<string>().ToList();
        }

        public IList<string> GetDecryptedObject(string objSchema, string objName)
        {
            var result = new List<string>();
            using (var command = GetSourceSqlCommand(sqlTimeout))
            {
                command.CommandText = @"DECLARE @ObjectOwnerOrSchema NVARCHAR(128)
                        DECLARE @ObjectName NVARCHAR(128)

                        SET @ObjectOwnerOrSchema = '$objSchema$'
                        SET @ObjectName = '$objName$'

                        DECLARE @i INT
                        DECLARE @ObjectDataLength INT
                        DECLARE @ContentOfEncryptedObject NVARCHAR(MAX)
                        DECLARE @ContentOfDecryptedObject VARCHAR(MAX)
                        DECLARE @ContentOfFakeObject NVARCHAR(MAX)
                        DECLARE @ContentOfFakeEncryptedObject NVARCHAR(MAX)
                        DECLARE @ObjectType NVARCHAR(128)
                        DECLARE @ObjectID INT

                        SET NOCOUNT ON

                        SET @ObjectID = OBJECT_ID('[' + @ObjectOwnerOrSchema + '].[' + @ObjectName + ']')

                        -- Check that the provided object exists in the database.
                        IF @ObjectID IS NULL
                        BEGIN
                        RAISERROR('Object not found in the database.', 16, 1)
                        RETURN
                        END

                        -- Check that the provided object is encrypted.
                        IF NOT EXISTS(SELECT TOP 1 * FROM syscomments WHERE id = @ObjectID AND encrypted = 1)
                        BEGIN
                        RAISERROR('Object is not encrypted.', 16, 1)
                        RETURN
                        END

                        -- Determine the type of the object
                        IF OBJECT_ID('[' + @ObjectOwnerOrSchema + '].[' + @ObjectName + ']', 'PROCEDURE') IS NOT NULL
                        SET @ObjectType = 'PROCEDURE'
                        ELSE
                        IF OBJECT_ID('[' + @ObjectOwnerOrSchema + '].[' + @ObjectName + ']', 'TRIGGER') IS NOT NULL
                        SET @ObjectType = 'TRIGGER'
                        ELSE
                        IF OBJECT_ID('[' + @ObjectOwnerOrSchema + '].[' + @ObjectName + ']', 'VIEW') IS NOT NULL
                        SET @ObjectType = 'VIEW'
                        ELSE
                        SET @ObjectType = 'FUNCTION'

                        -- Get the binary representation of the object- syscomments no longer holds the content of encrypted object.
                        SELECT TOP 1 @ContentOfEncryptedObject = imageval
                        FROM sys.sysobjvalues
                        WHERE objid = OBJECT_ID('[' + @ObjectOwnerOrSchema + '].[' + @ObjectName + ']')
                        AND valclass = 1 and subobjid = 1

                        SET @ObjectDataLength = DATALENGTH(@ContentOfEncryptedObject)/2

                        -- We need to alter the existing object and make it into a dummy object
                        -- in order to decrypt its content. This is done in a transaction
                        -- (which is later rolled back) to ensure that all changes have a minimal
                        -- impact on the database.
                        SET @ContentOfFakeObject = N'ALTER ' + @ObjectType + N' [' + @ObjectOwnerOrSchema + N'].[' + @ObjectName + N'] WITH ENCRYPTION AS'

                        WHILE DATALENGTH(@ContentOfFakeObject)/2 < @ObjectDataLength
                        BEGIN
                        IF DATALENGTH(@ContentOfFakeObject)/2 + 8000 < @ObjectDataLength
                        SET @ContentOfFakeObject = @ContentOfFakeObject + REPLICATE(N'-', 8000)
                        ELSE
                        SET @ContentOfFakeObject = @ContentOfFakeObject + REPLICATE(N'-', @ObjectDataLength - (DATALENGTH(@ContentOfFakeObject)/2))
                        END

                        -- Since we need to alter the object in order to decrypt it, this is done in a transaction
                        SET XACT_ABORT OFF
                        BEGIN TRAN

                        EXEC(@ContentOfFakeObject)

                        IF @@ERROR <> 0
                        ROLLBACK TRAN

                        -- Get the encrypted content of the new fake object.
                        SELECT TOP 1 @ContentOfFakeEncryptedObject = imageval
                        FROM sys.sysobjvalues
                        WHERE objid = OBJECT_ID('[' + @ObjectOwnerOrSchema + '].[' + @ObjectName + ']')
                        AND valclass = 1 and subobjid = 1

                        IF @@TRANCOUNT > 0
                        ROLLBACK TRAN

                        -- Generate a CREATE script for the dummy object text.
                        SET @ContentOfFakeObject = N'CREATE ' + @ObjectType + N' [' + @ObjectOwnerOrSchema + N'].[' + @ObjectName + N'] WITH ENCRYPTION AS'

                        WHILE DATALENGTH(@ContentOfFakeObject)/2 < @ObjectDataLength
                        BEGIN
                        IF DATALENGTH(@ContentOfFakeObject)/2 + 8000 < @ObjectDataLength
                        SET @ContentOfFakeObject = @ContentOfFakeObject + REPLICATE(N'-', 8000)
                        ELSE
                        SET @ContentOfFakeObject = @ContentOfFakeObject + REPLICATE(N'-', @ObjectDataLength - (DATALENGTH(@ContentOfFakeObject)/2))
                        END

                        SET @i = 1

                        --Fill the variable that holds the decrypted data with a filler character
                        SET @ContentOfDecryptedObject = N''

                        WHILE DATALENGTH(@ContentOfDecryptedObject)/2 < @ObjectDataLength
                        BEGIN
                        IF DATALENGTH(@ContentOfDecryptedObject)/2 + 8000 < @ObjectDataLength
                        SET @ContentOfDecryptedObject = @ContentOfDecryptedObject + REPLICATE(N'A', 8000)
                        ELSE
                        SET @ContentOfDecryptedObject = @ContentOfDecryptedObject + REPLICATE(N'A', @ObjectDataLength - (DATALENGTH(@ContentOfDecryptedObject)/2))
                        END

                        WHILE @i <= @ObjectDataLength BEGIN
                        --xor real & fake & fake encrypted
                        SET @ContentOfDecryptedObject = STUFF(@ContentOfDecryptedObject, @i, 1,
                        NCHAR(
                        UNICODE(SUBSTRING(@ContentOfEncryptedObject, @i, 1)) ^
                        (
                        UNICODE(SUBSTRING(@ContentOfFakeObject, @i, 1)) ^
                        UNICODE(SUBSTRING(@ContentOfFakeEncryptedObject, @i, 1))
                        )))

                        SET @i = @i + 1
                        END

                        SELECT SUBSTRING(@ContentOfDecryptedObject, 1, @ObjectDataLength) AS [processing-instruction(x)]"
                    .Replace("$objSchema$", objSchema).Replace("$objName$", objName);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(reader[0].ToString());
                    }
                }
            }

            return result;
        }

        public void CreateObject(NamedSmoObject obj, bool dropIfExists, bool overrideCollation, bool useSourceCollation,
            bool alterInsteadOfCreate, bool? removeSchemaBinding)
        {
            using (var command = GetDestinationSqlCommand(sqlTimeout))
            {
                if ((obj is User && new[] { "dbo", "INFORMATION_SCHEMA", "guest", "sys" }.Contains(obj.Name)) ||
                    (obj is DatabaseRole && new[]
                    {
                        "db_accessadmin", "db_backupoperator", "db_datareader", "db_datawriter",
                        "db_ddladmin", "db_denydatareader", "db_denydatawriter", "db_owner", "db_securityadmin",
                        "public"
                    }.Contains(obj.Name)))
                {
                    return;
                }

                if (obj is View && alterInsteadOfCreate && removeSchemaBinding == false)
                {
                    transfer.Options.Indexes =
                        transfer.Options.ClusteredIndexes =
                            transfer.Options.ColumnStoreIndexes =
                                transfer.Options.DriIndexes =
                                    transfer.Options.FullTextIndexes =
                                        transfer.Options.NonClusteredIndexes =
                                            transfer.Options.SpatialIndexes =
                                                transfer.Options.XmlIndexes = true;
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
                                                 sourceServer.DatabaseEngineType ==
                                                 DatabaseEngineType.SqlAzureDatabase &&
                                                 destinationServer.DatabaseEngineType !=
                                                 DatabaseEngineType.SqlAzureDatabase;
                //system-versioned tables should have their PK created right away
                transfer.Options.DriPrimaryKey = obj is Table table && (table.GetTableProperty("IsSystemVersioned") ||
                                                                        table.GetTableProperty("IsMemoryOptimized"));
                transfer.Options.IncludeIfNotExists = obj is Schema;

                var scripts = transfer.ScriptTransfer().Cast<string>().ToList();
                var incompatibleErrorMsg = "";
                var decryptedObjectList = new List<string>();
                if (transfer.IncompatibleObjects.Count > 0)
                {
                    incompatibleErrorMsg = "";
                    foreach (var incobj in transfer.IncompatibleObjects)
                    {
                        var incompatSchema = "dbo";
                        var incobjValue = incobj.Value.Replace(incobj.Parent.Value, "").Substring(1 + incobj.Type.Length);
                        if (incobjValue.IndexOf("@Schema=", StringComparison.Ordinal) > 0)
                        {
                            incompatSchema = incobjValue.Substring(8 + incobjValue.IndexOf("@Schema=", StringComparison.Ordinal));
                            if (incompatSchema.IndexOf("/", StringComparison.Ordinal) > 1)
                            {
                                incompatSchema = incompatSchema.Substring(0, incompatSchema.IndexOf("/", StringComparison.Ordinal) - 1);
                            }

                            while (incompatSchema.Length > 0 && !incompatSchema.EndsWith("'"))
                            {
                                incompatSchema = incompatSchema.Substring(0, incompatSchema.Length - 1);
                            }

                            incompatSchema = incompatSchema.Replace("'", "");
                        }

                        var objectName = incobjValue.Substring(6 + incobjValue.LastIndexOf("@Name=", StringComparison.Ordinal));
                        var currindex = 1;
                        while (objectName[currindex] != '\'')
                        {
                            currindex++;
                        }

                        objectName = objectName.Substring(0, currindex + 1).Replace("'", "");
                        if (!Properties.Settings.Default.DecryptObjects || DACconnection == null)
                        {
                            incompatibleErrorMsg += $" {(incompatSchema + "." + objectName).Replace("'", "")}";
                        }
                        else
                        {
                            try
                            {
                                var decrypted = GetDecryptedObject(incompatSchema, objectName);
                                foreach (var decItem in decrypted.ToList())
                                {
                                    decrypted.Remove(decItem);
                                    int encLocation = decItem.IndexOf(" WITH ENCRYPTION", StringComparison.Ordinal);
                                    string result = decItem.Remove(encLocation, " WITH ENCRYPTION".Length)
                                        .Insert(encLocation, "");
                                    decrypted.Add(result);
                                }

                                decryptedObjectList.AddRange(decrypted);
                            }
                            catch
                            {
                                incompatibleErrorMsg += $" {(incompatSchema + "." + objectName).Replace("'", "")}";
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(incompatibleErrorMsg))
                    {
                        incompatibleErrorMsg = "Incompatible subitems in this object:" + incompatibleErrorMsg;
                    }

                    transfer.IncompatibleObjects.Clear();
                }

                if (scripts.Count == 0)
                {
                    throw new Exception($"Could not script object {namewithschema}");
                }

                var alreadyAltered = false;
                foreach (var script in scripts.Concat(decryptedObjectList))
                {
                    if (script.Contains("Incompatible object not scripted"))
                    {
                        continue;
                    }

                    //create schema if not exists
                    var schemaname = obj.GetType().GetProperty("Schema")?.GetValue(obj, null).ToString();
                    if (!string.IsNullOrEmpty(schemaname) && !existingschemas.Contains(schemaname))
                    {
                        command.CommandText =
                            $"IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name=N'{schemaname}') EXEC('CREATE SCHEMA [{schemaname}]{GetSchemaAuthorization(obj.Name)}')";
                        command.ExecuteNonQuery();
                        existingschemas.Add(schemaname);
                    }

                    string scriptRun;
                    if (alterInsteadOfCreate && removeSchemaBinding == false && alreadyAltered)
                    {
                        scriptRun = script;
                    }
                    else
                    {
                        scriptRun = ConvertScriptProperCase(script, obj, overrideCollation, useSourceCollation,
                            alterInsteadOfCreate, removeSchemaBinding, command);
                        if (scriptRun.StartsWith("ALTER"))
                        {
                            //ALTER VIEW already done, do not convert case for subsequent objects, such as indexes
                            alreadyAltered = true;
                        }
                    }

                    if (scriptRun.StartsWith("CREATE USER", StringComparison.InvariantCultureIgnoreCase))
                    {
                        string password = null;
                        if (scriptRun.IndexOf("WITH PASSWORD=", StringComparison.InvariantCultureIgnoreCase) >= 0)
                        {
                            password = scriptRun.Substring(16 + scriptRun.IndexOf("WITH PASSWORD",
                                StringComparison.InvariantCultureIgnoreCase));
                            password = password.Substring(0, password.IndexOf("',", StringComparison.Ordinal));
                        }

                        if (scriptRun.IndexOf("WITHOUT LOGIN", StringComparison.InvariantCultureIgnoreCase) < 0)
                        {
                            const string createLoginSql = @"IF NOT EXISTS
                                (SELECT name FROM master.sys.server_principals WHERE name='{0}')
                                BEGIN
                                    CREATE LOGIN [{0}] WITH PASSWORD=N'{1}'
                                END";
                            command.CommandText = string.Format(createLoginSql, obj.Name,
                                string.IsNullOrWhiteSpace(password)
                                    ? (ConfigurationManager.AppSettings["DefaultPassword"] ?? "D3F@u1TP@s$W0rd!")
                                    : password);
                            command.ExecuteNonQuery();
                        }

                        if (copyAzureUserToNonAzureDB)
                        {
                            scriptRun = scriptRun.Replace(" FROM EXTERNAL PROVIDER", "")
                                .Replace(" FROM  EXTERNAL PROVIDER", "");
                        }
                    }

                    try
                    {
                        command.CommandText = scriptRun;
                        command.ExecuteNonQuery();
                        if (obj is Schema && !existingschemas.Contains(obj.Name))
                        {
                            existingschemas.Add(obj.Name);
                        }

                        if (obj is SecurityPolicy)
                        {
                            LstPostExecutionExecute.Add(scriptRun);
                        }
                    }
                    catch (Exception ex) when (ex.Message.StartsWith("Cannot find the user") &&
                                               script.StartsWith("GRANT", StringComparison.OrdinalIgnoreCase))
                    {
                        //for non-existing users, do not fail at GRANT
                    }
                    catch (Exception ex) when (ex.Message.Contains(
                                                   "Property cannot be added. Property 'MS_Description' already exists"))
                    {
                        //for existing descriptions, do not fail
                    }
                    catch (Exception ex) when (ex.Message.Contains("Cannot alter the role") &&
                                               ex.Message.Contains(
                                                   "because it does not exist or you do not have permission."))
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
            while ((token = scanner.GetNext(ref state, out int start, out int end, out bool _, out bool _)) !=
                   (int)Tokens.EOF)
            {
                var currentToken = new TokenInfoExtended()
                {
                    StartIndex = start,
                    EndIndex = end,
                    Separators = "",
                    SQL = sql.Substring(start, end - start + 1),
                    Type = (TokenType)token,
                    Token = token
                };
                if (tokensResult.Count > 0)
                {
                    if (string.IsNullOrEmpty(currentToken.SQL))
                    {
                        previousSpace = true;
                    }
                    else if (previousSpace && !string.IsNullOrEmpty(currentToken.SQL))
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

                if (removeSchemaBinding == true &&
                    token == (int)Tokens.TOKEN_ID && string.Equals(currentToken.SQL, "SCHEMABINDING",
                        StringComparison.InvariantCultureIgnoreCase) &&
                    previousToken?.Token == (int)Tokens.TOKEN_WITH)
                {
                    previousToken.Separators +=
                        $"/* disable SCHEMABINDING {System.Diagnostics.Process.GetCurrentProcess().ProcessName} ";
                    nextSeparator = " disable SCHEMABINDING */";
                }

                tokensResult.Add(currentToken);
                lastTokenEnd = end;
                previousToken = currentToken;
            }

            if (tokensResult.Any())
            {
                tokensResult.Last().IsLastToken = true;
            }

            return tokensResult;
        }

        private string ConvertScriptProperCase(string script, NamedSmoObject obj, bool overrideCollation,
            bool useSourceCollation, bool alterInsteadOfCreate, bool? removeSchemaBinding, SqlCommand command)
        {
            if (script != "SET QUOTED_IDENTIFIER ON" && script != "SET ANSI_NULLS ON")
            {
                var first_create = true;
                var next_collate = false;
                var creating_name = false;
                var objectname_already_replaced = false;
                var triggerON = false;
                var RaiserrorTransform = string.Equals(ConfigurationManager.AppSettings["RaiserrorTransform"], "true",
                    StringComparison.InvariantCultureIgnoreCase);
                if (RaiserrorTransform &&
                    ((sourceDatabase.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2012) &&
                      destinationDatabase.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2012)) ||
                     (!sourceDatabase.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2012) &&
                      !destinationDatabase.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2012))))
                {
                    //both databases are SQL2012 or newer, or both are older than SQL2012, RAISERROR should not be transformed
                    RaiserrorTransform = false;
                }

                var raiserrorON = false;
                var minusON = false;
                var strTokenTest = "";
                var tokenRaiseIndex = 1;
                TokenInfoExtended[] raiserrorTOKENS = null;
                IList<TokenInfoExtended> raiserrorTOKENSOld = null;
                IList<string> raiserrorParameters = null;
                var tokenIDForTrigger = 0;
                var replaceNameObjects = new[]
                {
                    (int)Tokens.TOKEN_DEFAULT,
                    (int)Tokens.TOKEN_FUNCTION,
                    (int)Tokens.TOKEN_PROCEDURE,
                    (int)Tokens.TOKEN_RULE,
                    (int)Tokens.TOKEN_TABLE,
                    (int)Tokens.TOKEN_VIEW,
                };
                var sb = new StringBuilder();
                TokenInfoExtended previousToken = null;
                foreach (var currentToken in ParseSqlScript(script, removeSchemaBinding))
                {
                    //Experimental: enable RAISERROR transform from old format to new format
                    if (raiserrorON)
                    {
                        if (!sourceDatabase.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2012) &&
                            destinationDatabase.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2012))
                        {
                            //source is old, destination is new, apply new syntax
                            if (raiserrorTOKENS == null)
                            {
                                raiserrorParameters = new List<string>();
                                minusON = false;
                                //format 1: RAISERROR('error message', 16, 1)
                                //format 2: RAISERROR(50001, 16, 1)
                                raiserrorTOKENSOld = new List<TokenInfoExtended>();
                                raiserrorTOKENS = new TokenInfoExtended[8];
                                raiserrorTOKENS[0] = new TokenInfoExtended()
                                { SQL = "(", Token = TOKEN_LEFT_PARENTHESIS };

                                raiserrorTOKENS[1] = new TokenInfoExtended()
                                { SQL = "50001", Token = (int)Tokens.TOKEN_INTEGER }; //id
                                raiserrorTOKENS[2] = new TokenInfoExtended()
                                { SQL = null, Token = (int)Tokens.TOKEN_STRING }; //error msg

                                raiserrorTOKENS[3] = new TokenInfoExtended() { SQL = ",", Token = TOKEN_COMMA };
                                raiserrorTOKENS[4] = new TokenInfoExtended()
                                { SQL = "16", Token = (int)Tokens.TOKEN_INTEGER }; //severity

                                raiserrorTOKENS[5] = new TokenInfoExtended() { SQL = ",", Token = TOKEN_COMMA };
                                raiserrorTOKENS[6] = new TokenInfoExtended()
                                { SQL = "1", Token = (int)Tokens.TOKEN_INTEGER }; //state

                                raiserrorTOKENS[7] = new TokenInfoExtended()
                                { SQL = ")", Token = TOKEN_RIGHT_PARENTHESIS };
                                tokenRaiseIndex = 1;
                            }

                            if (currentToken.Token == (int)Tokens.TOKEN_INTEGER ||
                                currentToken.Token == (int)Tokens.TOKEN_VARIABLE)
                            {
                                if (currentToken.Token == (int)Tokens.TOKEN_VARIABLE)
                                {
                                    raiserrorParameters.Add("DECLARE " + currentToken.SQL + " INT=" +
                                                            raiserrorTOKENS[tokenRaiseIndex].SQL);
                                }

                                raiserrorTOKENS[tokenRaiseIndex].SQL = (minusON ? "-" : "") + currentToken.SQL;
                                if (tokenRaiseIndex == 1)
                                {
                                    tokenRaiseIndex++;
                                }

                                tokenRaiseIndex += 2;
                                minusON = false;
                                raiserrorTOKENSOld.Add(currentToken);
                                if (!currentToken.IsLastToken)
                                {
                                    continue;
                                }
                            }
                            else if (currentToken.Token == (int)Tokens.TOKEN_STRING)
                            {
                                raiserrorTOKENS[2].SQL = currentToken.SQL;
                                tokenRaiseIndex += 2;
                                raiserrorTOKENSOld.Add(currentToken);
                                if (!currentToken.IsLastToken)
                                {
                                    continue;
                                }
                            }

                            if (currentToken.Token == TOKEN_LEFT_PARENTHESIS)
                            {
                                //new syntax detected, no need to replace; use whatever was found and go on
                                sb.Append(strTokenTest);
                                raiserrorTOKENSOld.Add(currentToken);
                                raiserrorTOKENSOld.ToList().ForEach(t => sb.Append(t.Separators).Append(t.SQL));
                                raiserrorTOKENS = null;
                                raiserrorON = false;
                                continue;
                            }

                            if (currentToken.Token == (int)Tokens.LEX_WHITE ||
                                currentToken.Token == (int)Tokens.LEX_END_OF_LINE_COMMENT ||
                                currentToken.Token == (int)Tokens.LEX_MULTILINE_COMMENT ||
                                currentToken.Token == (int)Tokens.TOKEN_VARIABLE ||
                                currentToken.Token == TOKEN_SEMICOLON ||
                                currentToken.Token == TOKEN_COMMA ||
                                currentToken.Token == TOKEN_LEFT_PARENTHESIS ||
                                currentToken.Token == TOKEN_RIGHT_PARENTHESIS)
                            {
                                raiserrorTOKENSOld.Add(currentToken);
                                if (!currentToken.IsLastToken)
                                {
                                    continue;
                                }
                            }

                            if (currentToken.Token == TOKEN_MINUS)
                            {
                                minusON = true;
                                if (!currentToken.IsLastToken)
                                {
                                    continue;
                                }
                            }
                        }
                        else if (!sourceDatabase.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2012) &&
                                 destinationDatabase.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2012))
                        {
                            //source is old, destination is new, keep old sntax
                            sb.Append(strTokenTest);
                        }

                        //here either there was nothing to do or we have already passed Raiserror, create it if needed and go on
                        if (raiserrorON)
                        {
                            if (raiserrorTOKENS[2].SQL != null)
                            {
                                //error message has been set, remove error number (usually "50000")
                                raiserrorTOKENS = raiserrorTOKENS.Take(1).Concat(raiserrorTOKENS.Skip(2)).ToArray();
                            }
                            else
                            {
                                //error message has not been set, throw error number (usually "50000")
                                raiserrorTOKENS = raiserrorTOKENS.Take(2).Concat(raiserrorTOKENS.Skip(3)).ToArray();
                                if (!raiserrorTOKENS[1].SQL.StartsWith("@"))
                                {
                                    //since 50000 is not allowed, change it to 50001 to avoid the following error:
                                    //Error number 50000 is invalid. The number must be from 13000 through 2147483647 and it cannot be 50000.
                                    int intErrorNumber = 50001;
                                    if (int.TryParse(raiserrorTOKENS[1].SQL, out int parseIntSQL))
                                    {
                                        if (parseIntSQL < 13000)
                                        {
                                            intErrorNumber = 13000;
                                        }
                                        else if (parseIntSQL == 50000)
                                        {
                                            intErrorNumber = 50001;
                                        }
                                        else
                                        {
                                            intErrorNumber = parseIntSQL;
                                        }
                                    }

                                    raiserrorTOKENS[1].SQL = intErrorNumber.ToString();
                                }
                            }

                            var strTokenTestExecute = strTokenTest;
                            raiserrorTOKENSOld.ToList().ForEach(t => strTokenTestExecute += t.Separators + t.SQL);
                            foreach (var parameter in raiserrorParameters)
                            {
                                strTokenTestExecute = parameter + Environment.NewLine + strTokenTestExecute;
                            }

                            if (raiserrorTOKENS != null)
                            {
                                //check if current RAISERROR syntax is good                                
                                try
                                {
                                    command.ExecuteNonQuery();
                                }
                                catch (SqlException e)
                                {
                                    if (e.Number == 102 && e.Message.StartsWith("Incorrect syntax near"))
                                    {
                                        //old RAISERROR syntax detected, use new syntax
                                        strTokenTestExecute = strTokenTest;
                                        foreach (var parameter in raiserrorParameters)
                                        {
                                            strTokenTestExecute = parameter + Environment.NewLine + strTokenTestExecute;
                                        }

                                        raiserrorTOKENS.ToList()
                                            .ForEach(t => strTokenTestExecute += t.Separators + t.SQL);
                                        try
                                        {
                                            command.CommandText = strTokenTestExecute;
                                            command.ExecuteNonQuery();
                                        }
                                        catch (Exception newex)
                                        {
                                            if (raiserrorTOKENS[1].SQL.Contains(newex.Message) ||
                                                (newex.Message.StartsWith("Error") &&
                                                 newex.Message.Contains("was raised")))
                                            {
                                                //new syntax works
                                                sb.Append(strTokenTest);
                                                raiserrorTOKENS.ToList().ForEach(t =>
                                                    sb.Append(t.Separators).Append(t.SQL));
                                            }
                                            else
                                            {
                                                //new syntax is also wrong
                                                throw e;
                                            }
                                        }
                                    }
                                    else if ((e.Message.StartsWith("Error") && e.Message.Contains("was raised")) ||
                                             e.Number == 50000)
                                    {
                                        //old syntax was good, no error
                                        sb.Append(strTokenTest);
                                        raiserrorTOKENSOld.ToList().ForEach(t => sb.Append(t.Separators).Append(t.SQL));
                                    }
                                    else
                                    {
                                        throw;
                                    }
                                }
                            }

                            raiserrorON = false;
                            raiserrorTOKENS = null;
                        }
                    }

                    //replace the scripted object's name with the actual name, explained below
                    if (creating_name &&
                        (currentToken.Token == (int)Tokens.TOKEN_ID || currentToken.Token == TOKEN_DOT) &&
                        !replaceNameObjects.Contains(currentToken.Token) &&
                        tokenIDForTrigger < 2)
                    {
                        if (obj is Trigger)
                        {
                            //AFTER or UPDATE keywords are treated as TOKEN_ID
                            if (currentToken.Token == (int)Tokens.TOKEN_ID)
                            {
                                //first pass, either schemaname or tablename
                                tokenIDForTrigger++;
                            }

                            if (currentToken.Token == TOKEN_DOT)
                            {
                                tokenIDForTrigger--;
                            }
                        }

                        if (tokenIDForTrigger < 2)
                        {
                            sb.Append(currentToken.Separators);
                        }

                        if (!objectname_already_replaced)
                        {
                            if (triggerON)
                            {
                                //due to an SMO bug sometimes the table's schemaname is not scripted
                                //replace here the trigger's table name with the proper one
                                sb.Append((obj as Trigger)?.Parent);
                                triggerON = false;
                            }
                            else
                            {
                                sb.Append(obj);
                            }

                            objectname_already_replaced = true;
                        }

                        if (tokenIDForTrigger < 2)
                        {
                            //this is the object name, do not go on as the name was set before
                            continue;
                        }
                        else
                        {
                            //this is an AFTER or BEFORE token, process it as usual
                            creating_name = false;
                        }
                    }

                    if (creating_name &&
                        currentToken.Token != (int)Tokens.LEX_END_OF_LINE_COMMENT &&
                        currentToken.Token != (int)Tokens.LEX_MULTILINE_COMMENT &&
                        !replaceNameObjects.Contains(currentToken.Token))
                    {
                        creating_name = false;
                        if (!objectname_already_replaced)
                        {
                            sb.Append(obj);
                            objectname_already_replaced = true;
                        }
                    }

                    if (obj is Trigger && currentToken.Token == (int)Tokens.TOKEN_ON && !objectname_already_replaced)
                    {
                        triggerON = true;
                        creating_name = true;
                        tokenIDForTrigger = 0;
                    }

                    if (RaiserrorTransform && currentToken.Token == (int)Tokens.TOKEN_RAISERROR)
                    {
                        strTokenTest = currentToken.Separators + currentToken.SQL;
                        raiserrorON = true;
                        continue;
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
                        if (currentToken.SQL.IndexOf("database_default", StringComparison.InvariantCultureIgnoreCase) <
                            0)
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
                    else if ((obj is Default ||
                              obj is Rule ||
                              obj is View ||
                              obj is StoredProcedure ||
                              obj is Table ||
                              obj is UserDefinedFunction) &&
                             first_create &&
                             currentToken.Token == (int)Tokens.TOKEN_CREATE)
                    {
                        sb.Append(alterInsteadOfCreate
                            ? "ALTER"
                            : currentToken.SQL.ToUpperInvariant()); //CREATE always in capitals

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
                        if (!triggerON && creating_name && replaceNameObjects.Contains(currentToken.Token))
                        {
                            //replace object name at the top, it could be replaced here but
                            //if there are comments around, it wouldn't be done properly
                            //for example: CREATE VIEW /*comment*/ dbo.ViewName /*othercomment*/ AS...
                            objectname_already_replaced = false;
                        }
                    }

                    previousToken = currentToken;
                }

                var newScript = sb.ToString();

                //Add "AUTHORIZATION" to schema objects, sometimes it's not added automatically
                if (obj is Schema)
                {
                    var auth = GetSchemaAuthorization(obj.ToString());
                    if (!string.IsNullOrEmpty(auth) && newScript.EndsWith("'") &&
                        newScript.IndexOf(" AUTHORIZATION", StringComparison.InvariantCultureIgnoreCase) < 0)
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
                using (var command = GetSourceSqlCommand(sqlTimeout))
                {
                    command.CommandText =
                        "SELECT QUOTENAME(name) AS schema_name,QUOTENAME(user_name(principal_id)) AS schema_owner FROM sys.schemas WHERE name<>'dbo'";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            schemaauths[reader["schema_name"].ToString()] = reader["schema_owner"].ToString();
                        }
                    }
                }
            }

            if (schemaauths.TryGetValue(schemaname, out var schemaauth))
            {
                return $" AUTHORIZATION {schemaauth}";
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
            foreach (var obj in lst
                         .Where(o => o is Table || o is View || o is StoredProcedure || o is UserDefinedFunction)
                         .ToList())
            {
                if (obj is Table currentTable)
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
                else if (obj is View currentView)
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
                else if (obj is StoredProcedure currentProcedure)
                {
                    foreach (Microsoft.SqlServer.Management.Smo.Parameter sub in currentProcedure.Parameters)
                    {
                        lst.Add(sub);
                    }
                }
                else if (obj is UserDefinedFunction currentFunction)
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

            foreach (IExtendedProperties item in lst.OfType<IExtendedProperties>()
                         .Where(p => p.ExtendedProperties?.Count > 0))
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
            AddClusteredIndexesDescriptions(true, true);
        }

        public void RemoveSchemaBindingFromDestination()
        {
            var firstRun = true;
            RecreateObjects.Clear();
            RefreshDestinationObjects();
            foreach (var obj in DestinationObjects.Where(o => o.Object != null && (
                         (o.Object is View view && view.IsSchemaBound) ||
                         (o.Object is UserDefinedFunction function && function.IsSchemaBound) ||
                         (o.Object is StoredProcedure procedure && procedure.IsSchemaBound))
                     ))
            {
                if (firstRun)
                {
                    RunInDestination(
                        "SELECT 'DROP SECURITY POLICY ' + QUOTENAME(s.name) + '.' + QUOTENAME(p.name) FROM sys.security_policies p INNER JOIN sys.schemas s ON p.schema_id=s.schema_id");
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
            var processList = RecreateObjects.ToList();
            while (currentErrors != previousErrors)
            {
                previousErrors = currentErrors;
                currentErrors = 0;
                foreach (var item in processList.ToList())
                {
                    try
                    {
                        CreateObject(item.Object, false, false, false, true, false);
                        processList.Remove(item);
                    }
                    catch
                    {
                        currentErrors++;
                    }
                }
            }

            //add description to views' clustered indexes
            AddClusteredIndexesDescriptions(false, true);
        }

        private void AddClusteredIndexesDescriptions(bool tables, bool views)
        {
            if (tables)
            {
                CopyToDestination(
                    @"SELECT 'EXEC sys.sp_addextendedproperty N''MS_Description'', N''' + CONVERT(VARCHAR(2000), p.[value]) + ''', ''SCHEMA'', N''' +
                    PARSENAME(SCHEMA_NAME(t.schema_id),1) + ''', ''TABLE'', N''' + t.name + ''', ''INDEX'', N''' + i.[name]+ ''''
                    FROM sys.indexes i INNER JOIN sys.extended_properties p ON p.major_id=i.object_id AND p.minor_id=i.index_id
                    INNER JOIN sys.tables t ON t.object_id=i.object_id
                    WHERE p.class=7 AND (i.type=1 OR is_primary_key=1)");
            }

            if (views)
            {
                CopyToDestination(
                    @"SELECT 'EXEC sys.sp_addextendedproperty N''MS_Description'', N''' + CONVERT(VARCHAR(2000), p.[value]) + ''', ''SCHEMA'', N''' +
                    PARSENAME(SCHEMA_NAME(v.schema_id),1) + ''', ''VIEW'', N''' + v.name + ''', ''INDEX'', N''' + i.[name]+ ''''
                    FROM sys.indexes i INNER JOIN sys.extended_properties p ON p.major_id=i.object_id AND p.minor_id=i.index_id
                    INNER JOIN sys.views v ON v.object_id=i.object_id
                    WHERE p.class=7");
            }
        }

        public void CopySchemaAuthorization()
        {
            CopyToDestination(
                @"SELECT 'ALTER AUTHORIZATION ON SCHEMA :: ' + QUOTENAME(s.name) + ' TO ' + QUOTENAME(u.name)
                                FROM sys.schemas s INNER JOIN sys.sysusers u
                                    ON u.uid=s.principal_id
                                WHERE s.name NOT IN('public','dbo','guest','INFORMATION_SCHEMA','sys')
                                    AND (u.uid & 16384 = 0)");
        }

        //This method is not needed by now
        /*
        public void CopyPermissions()
        {
            CopyToDestination(@"SELECT 'GRANT ' + permission_name COLLATE DATABASE_DEFAULT + ' ON ' +
                ISNULL(schema_name(o.uid)+'.','') + OBJECT_NAME(major_id) +
                ' TO ' + QUOTENAME(USER_NAME(grantee_principal_id))
                FROM sys.database_permissions dp
                LEFT OUTER JOIN sysobjects o ON o.id=dp.major_id
                WHERE OBJECT_NAME(major_id) IS NOT NULL");
        }
        */

        public void CopyRolePermissions()
        {
            CopyToDestination(
                @"SELECT 'EXEC sp_addrolemember N'''+ DP1.name + ''', N''' + ISNULL(DP2.name, 'No members') + ''''
                FROM sys.database_role_members AS DRM
                RIGHT OUTER JOIN sys.database_principals AS DP1
                   ON DRM.role_principal_id=DP1.principal_id
                LEFT OUTER JOIN sys.database_principals AS DP2
                   ON DRM.member_principal_id=DP2.principal_id
                WHERE DP1.type='R' AND DP1.is_fixed_role=0 AND DP2.is_fixed_role=0");
        }

        private IList<SqlSchemaObject> GetSqlObjects(ServerConnection connection, Database db)
        {
            var items = new List<SqlSchemaObject>();

            foreach (SqlAssembly item in db.Assemblies.Cast<SqlAssembly>().AsQueryable().Where(a => !a.IsSystemObject))
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }

            foreach (FullTextCatalog item in db.FullTextCatalogs)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }

            if (db.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2008))
            {
                foreach (FullTextStopList item in db.FullTextStopLists)
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
            }

            if (db.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2012) &&
                !dbSource.IsAzureDatabase() &&
                !dbDestination.IsAzureDatabase())
            {
                //searchpropertylists are not supported in Azure, ignore them if either of the databases is on Azure
                foreach (SearchPropertyList item in db.SearchPropertyLists)
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (User item in db.Users.Cast<User>().AsQueryable().Where(u => !u.IsSystemObject))
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }

            foreach (DatabaseRole item in db.Roles)
            {
                if (!item.IsFixedRole && item.Name != "public")
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (Schema item in db.Schemas.Cast<Schema>().AsQueryable().Where(s => !s.IsSystemObject))
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (Rule item in db.Rules)
            {
                items.Add(new SqlSchemaObject
                { Name = $"{item.Schema}.{item.Name}", Object = item, Type = item.GetType().Name });
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (PlanGuide item in db.PlanGuides)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (Default item in db.Defaults)
            {
                items.Add(new SqlSchemaObject
                { Name = $"{item.Schema}.{item.Name}", Object = item, Type = item.GetType().Name });
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (UserDefinedDataType item in db.UserDefinedDataTypes)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            if (db.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2008))
            {
                foreach (UserDefinedTableType item in db.UserDefinedTableTypes)
                {
                    items.Add(new SqlSchemaObject
                    { Name = $"{item.Schema}.{item.Name}", Object = item, Type = item.GetType().Name });
                }
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (XmlSchemaCollection item in db.XmlSchemaCollections)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }

            if (CancelToken.IsCancellationRequested)
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

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (PartitionScheme item in db.PartitionSchemes)
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            if (db.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2012))
            {
                foreach (Sequence item in db.Sequences)
                {
                    items.Add(new SqlSchemaObject
                    { Name = $"{item.Schema}.{item.Name}", Object = item, Type = item.GetType().Name });
                }
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            var alwaysIncludeTables = ConfigurationManager.AppSettings["AlwaysIncludeTables"];
            IList<string> alwaysIncludeTablesList = new List<string>();
            if (!string.IsNullOrEmpty(alwaysIncludeTables))
            {
                alwaysIncludeTablesList = alwaysIncludeTables.Split(',').Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Replace("[", "").Replace("]", "")).ToList();
            }

            var dicTables = new Dictionary<string, long>(StringComparer.InvariantCultureIgnoreCase);
            using (var command = GetSqlCommand(connection, sqlTimeout))
            {
                command.CommandText =
                    @"SELECT (SCHEMA_NAME(sOBJ.schema_id)) + '.' + (sOBJ.name) AS TableName,SUM(sPTN.Rows) AS RowCountNum
                    FROM sys.objects AS sOBJ INNER JOIN sys.partitions AS sPTN
                        ON sOBJ.object_id=sPTN.object_id
                    WHERE sOBJ.type='U'
                        AND sOBJ.is_ms_shipped=0
                        AND index_id<2
                    GROUP BY sOBJ.schema_id,sOBJ.name
                    ORDER BY 2 DESC";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        dicTables[reader["TableName"].ToString()] = (long)reader["RowCountNum"];
                    }
                }
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (Table item in db.Tables)
            {
                var tableName = $"{item.Schema}.{item.Name}";
                if (!item.IsSystemObject ||
                    alwaysIncludeTablesList.Any(s => s.Equals(tableName, StringComparison.OrdinalIgnoreCase)))
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
                        items.Add(new SqlSchemaObject
                        { Parent = table, Name = trigger.Name, Object = trigger, Type = trigger.GetType().Name });
                    }
                }
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (View item in db.Views.Cast<View>().AsQueryable().Where(v => !v.IsSystemObject))
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
                    items.Add(new SqlSchemaObject
                    { Parent = view, Name = trigger.Name, Object = trigger, Type = trigger.GetType().Name });
                }
            }

            if (db.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2016))
            {
                foreach (SecurityPolicy item in db.SecurityPolicies)
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }

                if (CancelToken.IsCancellationRequested)
                {
                    return items;
                }

                foreach (ColumnMasterKey item in db.ColumnMasterKeys)
                {
                    items.Add(new SqlSchemaObject { Name = $"{item.Name}", Object = item, Type = item.GetType().Name });
                }

                if (CancelToken.IsCancellationRequested)
                {
                    return items;
                }

                foreach (ColumnEncryptionKey item in db.ColumnEncryptionKeys)
                {
                    items.Add(new SqlSchemaObject { Name = $"{item.Name}", Object = item, Type = item.GetType().Name });
                }

                if (CancelToken.IsCancellationRequested)
                {
                    return items;
                }

                foreach (ExternalDataSource item in db.ExternalDataSources)
                {
                    items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
                }
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (UserDefinedFunction item in db.UserDefinedFunctions.Cast<UserDefinedFunction>().AsQueryable()
                         .Where(f => !f.IsSystemObject || f.Owner != "sys"))
            {
                items.Add(new SqlSchemaObject
                { Name = $"{item.Schema}.{item.Name}", Object = item, Type = item.GetType().Name });
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (StoredProcedure item in db.StoredProcedures.Cast<StoredProcedure>().AsQueryable()
                         .Where(p => !p.IsSystemObject || p.Owner != "sys"))
            {
                items.Add(new SqlSchemaObject
                { Name = $"{item.Schema}.{item.Name}", Object = item, Type = item.GetType().Name });
            }

            if (CancelToken.IsCancellationRequested)
            {
                return items;
            }

            foreach (DatabaseDdlTrigger item in db.Triggers.Cast<DatabaseDdlTrigger>().AsQueryable()
                         .Where(t => !t.IsSystemObject))
            {
                items.Add(new SqlSchemaObject { Name = item.Name, Object = item, Type = item.GetType().Name });
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
                //clustered index should be first
                if (srcindex.IndexType == IndexType.ClusteredIndex)
                {
                    indexesSorted.Insert(0, srcindex);
                }
                //primary xml indexes should be first, but second to clustered indexes
                else if (srcindex.IndexType == IndexType.PrimaryXmlIndex)
                {
                    var insertIndex = 0;
                    foreach (var indextemp in indexesSorted)
                    {
                        if (indextemp.IndexType == IndexType.ClusteredIndex)
                        {
                            insertIndex++;
                        }
                    }

                    indexesSorted.Insert(insertIndex, srcindex);
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
                if (destinationTable is Table table && table.GetTableProperty("IsSystemVersioned") &&
                    !table.GetTableProperty("IsMemoryOptimized")
                    && (srcindex.IndexKeyType == IndexKeyType.DriPrimaryKey))
                {
                    continue;
                }

                if (destinationTable is Table table2 && table2.GetTableProperty("IsMemoryOptimized"))
                {
                    continue;
                }

                Index index = new Index(destinationTable, srcindex.Name)
                {
                    DisallowPageLocks = srcindex.DisallowPageLocks,
                    DisallowRowLocks = srcindex.DisallowRowLocks,
                    FillFactor = srcindex.FillFactor,
                    IgnoreDuplicateKeys = srcindex.IgnoreDuplicateKeys,
                    IndexKeyType = srcindex.IndexKeyType,
                    IsClustered = srcindex.IsClustered,
                    IsFullTextKey = srcindex.IsFullTextKey,
                    IsUnique = srcindex.IsUnique,
                    NoAutomaticRecomputation = srcindex.NoAutomaticRecomputation,
                    PadIndex = srcindex.PadIndex,
                };

                //FilterDefinition property is not available for all SQL Server editions
                try
                {
                    index.FilterDefinition = srcindex.FilterDefinition;
                }
                catch
                {
                }

                foreach (ExtendedProperty ep in srcindex.ExtendedProperties)
                {
                    index.ExtendedProperties.Add(new ExtendedProperty(index, ep.Name, ep.Value));
                }

                //set other properties if they exist and are distinct
                foreach (string property in new[]
                             { "CompresionDelay", "CompactLargeObjects", "MaximumDegreeOfParallelism" })
                {
                    if (srcindex.GetType().GetProperty(property) != null &&
                        !index.GetType().GetProperty(property).GetValue(index)
                            .Equals(srcindex.GetType().GetProperty(property).GetValue(srcindex)))
                    {
                        index.GetType().GetProperty(property).SetValue(index,
                            srcindex.GetType().GetProperty(property).GetValue(srcindex), null);
                    }
                }

                if (sourceDatabase.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2016) &&
                    destinationDatabase.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2016))
                {
                    foreach (string property in new[] { "CompressAllRowGroups" })
                    {
                        if (srcindex.GetType().GetProperty(property) != null &&
                            !index.GetType().GetProperty(property).GetValue(index)
                                .Equals(srcindex.GetType().GetProperty(property).GetValue(srcindex)))
                        {
                            index.GetType().GetProperty(property).SetValue(index,
                                srcindex.GetType().GetProperty(property).GetValue(srcindex), null);
                        }
                    }
                }

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

                if (srcindex.IndexType == IndexType.ClusteredColumnStoreIndex ||
                    srcindex.IndexType == IndexType.NonClusteredColumnStoreIndex)
                {
                    index.IndexType = srcindex.IndexType;
                }

                if (obj is Table && destinationDatabase.Tables[obj.Name]?.FileGroup != null)
                {
                    index.FileGroup = destinationDatabase.Tables[obj.Name].FileGroup;
                }

                if ((sourceDatabase.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2016) ||
                     sourceDatabase.IsAzureDatabase()) &&
                    (destinationDatabase.IsRunningMinimumSQLVersion(SQL_DB_Compatibility.DB_2016) ||
                     destinationDatabase.IsAzureDatabase()) &&
                    srcindex.SpatialIndexType != SpatialIndexType.None)
                {
                    index.BoundingBoxXMax = srcindex.BoundingBoxXMax;
                    index.BoundingBoxXMin = srcindex.BoundingBoxXMin;
                    index.BoundingBoxYMax = srcindex.BoundingBoxYMax;
                    index.BoundingBoxYMin = srcindex.BoundingBoxYMin;
                    index.CellsPerObject = srcindex.CellsPerObject;
                    index.Level1Grid = srcindex.Level1Grid;
                    index.Level2Grid = srcindex.Level2Grid;
                    index.Level3Grid = srcindex.Level3Grid;
                    index.Level4Grid = srcindex.Level4Grid;
                    index.SpatialIndexType = srcindex.SpatialIndexType;
                }

                index.Create();
            }

            if (obj is Table sTab && sTab.GetTableProperty("ChangeTrackingEnabled"))
            {
                /*
                this does not work, direct SQL is used instead
                (GetDestinationTableOrViewByName(sTable) as Table).ChangeTrackingEnabled = true;
                if (sTab.TrackColumnsUpdatedEnabled)
                {
                    (GetDestinationTableOrViewByName(sTable) as Table).TrackColumnsUpdatedEnabled = true;
                }
                */
                using (var command = GetDestinationSqlCommand(null,
                           $"ALTER TABLE {obj} ENABLE CHANGE_TRACKING WITH(TRACK_COLUMNS_UPDATED={(sTab.TrackColumnsUpdatedEnabled ? "ON" : "OFF")})"))
                {
                    command.ExecuteNonQuery();
                }
            }

            if (CopyFullText)
            {
                FullTextIndex fulltextind = (obj as TableViewBase)?.FullTextIndex;
                if (fulltextind != null)
                {
                    FullTextIndex index = new FullTextIndex(destinationTable)
                    {
                        CatalogName = fulltextind.CatalogName,
                        FilegroupName = fulltextind.FilegroupName,
                        StopListName = fulltextind.StopListName,
                        StopListOption = fulltextind.StopListOption,
                        UniqueIndexName = fulltextind.UniqueIndexName,
                        UserData = fulltextind.UserData
                    };
                    if (!string.IsNullOrEmpty(fulltextind.SearchPropertyListName) && !dbDestination.IsAzureDatabase())
                    {
                        index.SearchPropertyListName = fulltextind.SearchPropertyListName;
                    }

                    foreach (FullTextIndexColumn srccol in fulltextind.IndexedColumns)
                    {
                        index.IndexedColumns.Add(new FullTextIndexColumn(index, srccol.Name)
                        {
                            TypeColumnName = srccol.TypeColumnName
                        });
                    }

                    index.Create();
                }
            }
        }

        public void ApplyForeignKeys(NamedSmoObject obj, bool disableNotForReplication)
        {
            foreach (ForeignKey sourcefk in (obj as Table)?.ForeignKeys)
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
        }

        public void ApplyChecks(NamedSmoObject obj, bool disableNotForReplication)
        {
            foreach (Check chkConstr in (obj as Table)?.Checks)
            {
                new Check(GetDestinationTableOrViewByName(obj), chkConstr.Name)
                {
                    IsChecked = chkConstr.IsChecked,
                    IsEnabled = chkConstr.IsEnabled,
                    NotForReplication = !disableNotForReplication && chkConstr.NotForReplication,
                    Text = chkConstr.Text
                }.Create();
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
            SqlConnection sourceSQLConnection;
            if (!string.IsNullOrEmpty(DACconnection))
            {
                sourceSQLConnection = new SqlConnection(DACconnection);
                sourceSQLConnection.Open();
            }
            else
            {
                sourceSQLConnection = new SqlConnection(SourceConnectionString);
                sourceSQLConnection.Open();
            }
            sourceConnection = new ServerConnection(sourceSQLConnection);
            sourceServer = new Server(sourceConnection);
            InitServer(sourceServer);
            sourceDatabase = sourceServer.Databases[SourceDatabaseName];
        }

        public void RefreshDestination()
        {
            destinationConnection = new ServerConnection(new SqlConnection(DestinationConnectionString));
            destinationServer = new Server(destinationConnection);
            InitServer(destinationServer);
            destinationDatabase = destinationServer.Databases[DestinationDatabaseName];
        }

        public void RefreshDestinationObjects()
        {
            RefreshDestination();
            DestinationObjects = GetSqlObjects(DestinationConnection, destinationDatabase);
        }

        public void RefreshAll(bool skipPreload)
        {
            RefreshSource();
            RefreshDestination();

            if (!skipPreload)
            {
                var sameserver = SameServer();
                var tskSource = Task.Run(() =>
                {
                    sourceDatabase.PrefetchObjects(typeof(Table), new ScriptingOptions());
                    if (!CancelToken.IsCancellationRequested)
                    {
                        SourceObjects = GetSqlObjects(SourceConnection, sourceDatabase);
                    }
                }, CancelToken);
                if (sameserver || (ConfigurationManager.AppSettings["EnablePreload"]?.ToLower() != "true"))
                {
                    tskSource.Wait(CancelToken);
                }

                var tskDestination = Task.Run(() =>
                {
                    if (!CancelToken.IsCancellationRequested)
                    {
                        DestinationObjects = GetSqlObjects(DestinationConnection, destinationDatabase);
                    }
                }, CancelToken);
                tskSource.Wait(CancelToken);
                tskDestination.Wait(CancelToken);
            }
        }

        public void ClearDestinationDatabase(Action<NamedSmoObject> callback = null)
        {
            var mainContext = Thread.CurrentContext;

            var lastError = "";
            if (DestinationObjects.Count == 0)
            {
                return;
            }

            var lastCount = 0;
            int remaining = DestinationObjects.Count;
            //it usually happens that drop scripts are not generated if the source server is different from the
            //destination server (property "transfer.Scripter" is always the source server, not the destination)
            //therefore instead of using the "this" object a new one is created and all of the drop operations
            //will be performed there
            var transferDrop = new SqlSchemaTransfer(DestinationConnectionString, DestinationConnectionString, true, null, CancelToken)
            { transfer = { CopyAllObjects = false } };
            transferDrop.transfer.Options.ScriptDrops = transferDrop.transfer.Options.IncludeIfNotExists =
                transferDrop.transfer.Options.ContinueScriptingOnError = true;
            transferDrop.ResetTransfer();

            //Restore default database principals if necessary
            RunInDestination(@"SELECT 'ALTER AUTHORIZATION ON SCHEMA::' + QUOTENAME(name) + ' TO dbo'
                            FROM sys.schemas WHERE schema_id<>principal_id
                            AND name IN ('dbo','guest','INFORMATION_SCHEMA','sys','db_owner','db_accessadmin','db_securityadmin',
                            'db_ddladmin','db_backupoperator','db_datareader','db_datawriter','db_denydatareader','db_denydatawriter')");

            while (remaining > 0 && lastCount != remaining)
            {
                var destinations = new BlockingCollection<NamedSmoObject>();
                var retrylist = new List<NamedSmoObject>();
                DestinationObjects.Select(o => o.Object).Where(p => !(p is Schema))
                    .Union(DestinationObjects.Select(s => s.Object))
                    .Where(nt => !(nt is Table) && !(nt is Trigger))
                    .ToList()
                    .ForEach(item => destinations.Add(item, CancelToken));

                lastCount = destinations.Count;
                //get tables with FKs and schemas
                var producerTablesAndSchemas = Task.Run(() =>
                {
                    foreach (Table table in DestinationObjects.OfType<SqlSchemaTable>().Select(o => o.Object).Cast<Table>())
                    {
                        if (CancelToken.IsCancellationRequested)
                        {
                            return;
                        }
                        try
                        {
                            Monitor.Enter(lockFlag);
                            try
                            {
                                if (table.GetTableProperty("IsSystemVersioned"))
                                {
                                    table.IsSystemVersioned = false;
                                }
                            }
                            catch
                            {
                            }
                            try
                            {
                                if (table.GetTableProperty("IsMemoryOptimized"))
                                {
                                    table.IsMemoryOptimized = false;
                                }
                            }
                            catch
                            {
                            }
                            foreach (ForeignKey fk in table.ForeignKeys)
                            {
                                destinations.Add(fk, CancelToken);
                            }
                        }
                        finally
                        {
                            Monitor.Exit(lockFlag);
                        }
                        destinations.Add(table, CancelToken);
                    }
                    //place schemas at the end
                    DestinationObjects.Select(o => o.Object).Where(p => p is Schema)
                        .ToList()
                        .ForEach(item => destinations.Add(item, CancelToken));

                    destinations.CompleteAdding();
                }, CancelToken);

                //process objects
                using (var command = transferDrop.GetDestinationSqlCommand(sqlTimeout))
                {
                    var processed = destinations.Count;
                    while (!destinations.IsAddingCompleted || processed > 0)
                    {
                        processed = 0;
                        foreach (var obj in destinations.GetConsumingEnumerable())
                        {
                            if (CancelToken.IsCancellationRequested)
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
                                    processed++;
                                    Monitor.Enter(lockFlag);
                                    mainContext.DoCallBack(() => callback?.Invoke(obj));
                                    Monitor.Exit(lockFlag);
                                }
                            }
                            catch (Exception ex)
                            {
                                if (!string.IsNullOrEmpty(ex.Message))
                                {
                                    lastError = $". {ex.Message} Affected object: {obj.Name}";
                                }
                                if (!retrylist.Contains(obj))
                                {
                                    retrylist.Add(obj);
                                }
                            }
                        }

                        if (retrylist.Count > 0 && destinations.IsAddingCompleted)
                        {
                            destinations = new BlockingCollection<NamedSmoObject>();
                            retrylist.ForEach(r => destinations.Add(r, CancelToken));
                            retrylist.Clear();
                            destinations.CompleteAdding();
                        }

                        if (destinations.Count == 0)
                        {
                            processed = 0; //finished
                        }
                    }
                }

                producerTablesAndSchemas.Wait(CancelToken);

                if (CancelToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    transferDrop.destinationDatabase.RemoveFullTextCatalogs();
                }
                catch
                {
                }

                //refresh local objects before exiting
                RefreshDestinationObjects();
                remaining = DestinationObjects.Count;
            }

            if (DestinationObjects.Count > 0)
            {
                throw new Exception($"Could not delete items{lastError}");
            }
            else
            {
                RunInDestination("SELECT 'DBCC SHRINKFILE(''' + name + ''',0)' FROM sys.database_files");
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
            return $"{FormatInstance(SourceConnection.ServerInstance.Replace("ADMIN:", ""))}/{SourceDatabaseName}";
        }

        public string DestinationCxInfo()
        {
            return $"{FormatInstance(DestinationConnection.ServerInstance)}/{DestinationDatabaseName}";
        }
    }
}
