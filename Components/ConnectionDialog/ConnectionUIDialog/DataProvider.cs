//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Data.ConnectionUI
{
    public class DataProvider
    {
        private readonly string _displayName;
        private readonly string _description;
        private readonly IDictionary<string, string> _dataSourceDescriptions;
        private readonly IDictionary<string, Type> _connectionUIControlTypes;
        private readonly IDictionary<string, Type> _connectionPropertiesTypes;

        public DataProvider(string name, string displayName, string shortDisplayName)
            : this(name, displayName, shortDisplayName, null, null)
        {
        }

        public DataProvider(string name, string displayName, string shortDisplayName, string description)
            : this(name, displayName, shortDisplayName, description, null)
        {
        }

        public DataProvider(string name, string displayName, string shortDisplayName, string description, Type targetConnectionType)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _displayName = displayName;
            ShortDisplayName = shortDisplayName;
            _description = description;
            TargetConnectionType = targetConnectionType;
        }

        public DataProvider(string name, string displayName, string shortDisplayName, string description, Type targetConnectionType, Type connectionPropertiesType)
            : this(name, displayName, shortDisplayName, description, targetConnectionType)
        {
            if (connectionPropertiesType == null)
            {
                throw new ArgumentNullException(nameof(connectionPropertiesType));
            }

            _connectionPropertiesTypes = new Dictionary<string, Type>
            {
                { string.Empty, connectionPropertiesType }
            };
        }

        public DataProvider(string name, string displayName, string shortDisplayName, string description, Type targetConnectionType, Type connectionUIControlType, Type connectionPropertiesType)
            : this(name, displayName, shortDisplayName, description, targetConnectionType, connectionPropertiesType)
        {
            if (connectionUIControlType == null)
            {
                throw new ArgumentNullException(nameof(connectionUIControlType));
            }

            _connectionUIControlTypes = new Dictionary<string, Type>
            {
                { string.Empty, connectionUIControlType }
            };
        }

        public DataProvider(string name, string displayName, string shortDisplayName, string description, Type targetConnectionType, IDictionary<string, Type> connectionUIControlTypes, Type connectionPropertiesType)
            : this(name, displayName, shortDisplayName, description, targetConnectionType, connectionPropertiesType)
        {
            _connectionUIControlTypes = connectionUIControlTypes;
        }

        public DataProvider(string name, string displayName, string shortDisplayName, string description, Type targetConnectionType, IDictionary<string, string> dataSourceDescriptions, IDictionary<string, Type> connectionUIControlTypes, Type connectionPropertiesType)
            : this(name, displayName, shortDisplayName, description, targetConnectionType, connectionUIControlTypes, connectionPropertiesType)
        {
            _dataSourceDescriptions = dataSourceDescriptions;
        }

        public DataProvider(string name, string displayName, string shortDisplayName, string description, Type targetConnectionType, IDictionary<string, string> dataSourceDescriptions, IDictionary<string, Type> connectionUIControlTypes, IDictionary<string, Type> connectionPropertiesTypes)
            : this(name, displayName, shortDisplayName, description, targetConnectionType)
        {
            _dataSourceDescriptions = dataSourceDescriptions;
            _connectionUIControlTypes = connectionUIControlTypes;
            _connectionPropertiesTypes = connectionPropertiesTypes;
        }

        public static DataProvider SqlDataProvider
        {
            get
            {
                if (_sqlDataProvider == null)
                {
                    Dictionary<string, string> descriptions = new Dictionary<string, string>
                    {
                        { DataSource.SqlDataSource.Name, "Use this selection to connect to Microsoft SQL Server 2005 or above using the .NET Framework Data Provider for SQL Server." }
                    };

                    Dictionary<string, Type> uiControls = new Dictionary<string, Type>
                    {
                        { DataSource.SqlDataSource.Name, typeof(SqlConnectionUIControl) },
                        { string.Empty, typeof(SqlConnectionUIControl) }
                    };

                    Dictionary<string, Type> properties = new Dictionary<string, Type>
                    {
                        { string.Empty, typeof(SqlConnectionProperties) }
                    };

                    _sqlDataProvider = new DataProvider(
                        "Microsoft.Data.SqlClient",
                        ".NET Framework Data Provider for SQL Server",
                        "SqlClient",
                        "Use this data provider to connect to Microsoft SQL Server 2005 or above.",
                        typeof(SqlClient.SqlConnection),
                        descriptions,
                        uiControls,
                        properties);
                }
                return _sqlDataProvider;
            }
        }
        private static DataProvider _sqlDataProvider;

        public string Name { get; }

        public string DisplayName
        {
            get
            {
                return _displayName ?? Name;
            }
        }

        public string ShortDisplayName { get; }

        public string Description
        {
            get
            {
                return GetDescription(null);
            }
        }

        public Type TargetConnectionType { get; }

        public virtual string GetDescription(DataSource dataSource)
        {
            if (_dataSourceDescriptions != null && dataSource != null &&
                _dataSourceDescriptions.ContainsKey(dataSource.Name))
            {
                return _dataSourceDescriptions[dataSource.Name];
            }
            else
            {
                return _description;
            }
        }

        public IDataConnectionUIControl CreateConnectionUIControl(string initialConnectionString)
        {
            return CreateConnectionUIControl(initialConnectionString);
        }

        public virtual IDataConnectionUIControl CreateConnectionUIControl(DataSource dataSource, string initialConnectionString)
        {
            string key;
            if ((_connectionUIControlTypes != null &&
                dataSource != null && _connectionUIControlTypes.ContainsKey(key = dataSource.Name)) ||
                _connectionUIControlTypes.ContainsKey(key = string.Empty))
            {
                return Activator.CreateInstance(_connectionUIControlTypes[key], initialConnectionString) as IDataConnectionUIControl;
            }
            else
            {
                return null;
            }
        }

        public IDataConnectionProperties CreateConnectionProperties()
        {
            return CreateConnectionProperties(null);
        }

        public virtual IDataConnectionProperties CreateConnectionProperties(DataSource dataSource)
        {
            string key;
            if (_connectionPropertiesTypes != null &&
                ((dataSource != null && _connectionPropertiesTypes.ContainsKey(key = dataSource.Name)) ||
                _connectionPropertiesTypes.ContainsKey(key = string.Empty)))
            {
                return Activator.CreateInstance(_connectionPropertiesTypes[key]) as IDataConnectionProperties;
            }
            else
            {
                return null;
            }
        }
    }
}
