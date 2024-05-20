//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Microsoft.Data.ConnectionUI
{
    public class SqlConnectionProperties : AdoDotNetConnectionProperties
    {
        private const int SqlError_CannotOpenDatabase = 4060;
        public SqlConnectionProperties()
        {
            LocalReset();
        }

        public override void Reset()
        {
            base.Reset();
            LocalReset();
        }

        public override bool IsComplete
        {
            get
            {
                if (!(ConnectionStringBuilder.DataSource is string) || ConnectionStringBuilder.DataSource.Length == 0)
                {
                    return false;
                }
                if (!(bool)ConnectionStringBuilder.IntegratedSecurity &&
                    (!(ConnectionStringBuilder.UserID is string) ||
                    (ConnectionStringBuilder.UserID).Length == 0))
                {
                    return false;
                }
                return true;
            }
        }

        public override string Test()
        {
            string dataSource = ConnectionStringBuilder.DataSource;
            if (string.IsNullOrEmpty(dataSource))
            {
                throw new InvalidOperationException("This connection cannot be tested because no server name has been specified.");
            }
            string database = ConnectionStringBuilder.InitialCatalog;
            try
            {
                return base.Test();
            }
            catch (SqlException e) when (e.Number == SqlError_CannotOpenDatabase && database?.Length > 0)
            {
                throw new InvalidOperationException("This connection cannot be tested because the specified database does not exist or is not visible to the specified user.");
            }
        }

        protected override PropertyDescriptor DefaultProperty
        {
            get
            {
                return GetProperties(new Attribute[0])["DataSource"];
            }
        }

        protected override string ToTestString()
        {
            bool savedPooling = ConnectionStringBuilder.Pooling;
            bool wasDefault = !ConnectionStringBuilder.ShouldSerialize("Pooling");
            ConnectionStringBuilder.Pooling = false;
            string testString = ConnectionStringBuilder.ConnectionString;
            ConnectionStringBuilder.Pooling = savedPooling;
            if (wasDefault)
            {
                ConnectionStringBuilder.Remove("Pooling");
            }
            return testString;
        }

        protected override void Inspect(SqlConnection connection)
        {
            if (connection.ServerVersion.StartsWith("07", StringComparison.Ordinal) ||
                connection.ServerVersion.StartsWith("08", StringComparison.Ordinal))
            {
                throw new NotSupportedException("This server version is not supported. You must have Microsoft SQL Server 2005 or later.");
            }
        }

        private void LocalReset()
        {
            // We always start with integrated security turned on
            this["Integrated Security"] = true;
        }
    }

    public class SqlFileConnectionProperties : SqlConnectionProperties
    {
        private readonly string _defaultDataSource;

        public SqlFileConnectionProperties()
            : this(null)
        {
        }

        public SqlFileConnectionProperties(string defaultInstanceName)
        {
            _defaultDataSource = ".";
            if (!string.IsNullOrEmpty(defaultInstanceName))
            {
                _defaultDataSource += "\\" + defaultInstanceName;
            }
            else
            {
                DataSourceConverter conv = new DataSourceConverter();
                TypeConverter.StandardValuesCollection coll = conv.GetStandardValues(null);
                if (coll.Count > 0)
                {
                    _defaultDataSource = coll[0] as string;
                }
            }
            LocalReset();
        }

        public override void Reset()
        {
            base.Reset();
            LocalReset();
        }

        public override bool IsComplete
        {
            get
            {
                if (!base.IsComplete)
                {
                    return false;
                }
                if (!(ConnectionStringBuilder.AttachDBFilename is string) || (ConnectionStringBuilder.AttachDBFilename).Length == 0)
                {
                    return false;
                }
                return true;
            }
        }

        public override string Test()
        {
            string attachDbFilename = ConnectionStringBuilder.AttachDBFilename;
            try
            {
                if (string.IsNullOrEmpty(attachDbFilename))
                {
                    throw new InvalidOperationException("No database file was specified.");
                }
                ConnectionStringBuilder.AttachDBFilename = System.IO.Path.GetFullPath(attachDbFilename);
                if (!System.IO.File.Exists(ConnectionStringBuilder.AttachDBFilename as string))
                {
                    throw new InvalidOperationException("This connection cannot be tested because the specified database file does not exist.");
                }
                return base.Test();
            }
            catch (SqlException e)
            {
                if (e.Number == -2) // timeout
                {
                    throw new ApplicationException(e.Errors[0].Message + Environment.NewLine + "Common reasons for this problem include that the server is not installed, the service is not started or the server is upgrading your database.  If an upgrade is in process, you will be able to connect to the database when the upgrade is complete.");
                }
                throw;
            }
            finally
            {
                if (!string.IsNullOrEmpty(attachDbFilename))
                {
                    ConnectionStringBuilder.AttachDBFilename = attachDbFilename;
                }
            }
        }

        protected override PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            PropertyDescriptorCollection descriptors = base.GetProperties(attributes);
            PropertyDescriptor dataSourceDescriptor = descriptors.Find("DataSource", true);
            if (dataSourceDescriptor != null)
            {
                int index = descriptors.IndexOf(dataSourceDescriptor);
                PropertyDescriptor[] descriptorArray = new PropertyDescriptor[descriptors.Count];
                descriptors.CopyTo(descriptorArray, 0);
                descriptorArray[index] = new DynamicPropertyDescriptor(dataSourceDescriptor, new TypeConverterAttribute(typeof(DataSourceConverter)));
                (descriptorArray[index] as DynamicPropertyDescriptor).CanResetValueHandler = new CanResetValueHandler(CanResetDataSource);
                (descriptorArray[index] as DynamicPropertyDescriptor).ResetValueHandler = new ResetValueHandler(ResetDataSource);
                descriptors = new PropertyDescriptorCollection(descriptorArray, true);
            }
            return descriptors;
        }

        private void LocalReset()
        {
            this["Data Source"] = _defaultDataSource;
            this["User Instance"] = true;
            this["Connection Timeout"] = 30;
        }

        private bool CanResetDataSource(object component)
        {
            return !(this["Data Source"] is string) || !(this["Data Source"] as string).Equals(_defaultDataSource, StringComparison.OrdinalIgnoreCase);
        }

        private void ResetDataSource(object component)
        {
            this["Data Source"] = _defaultDataSource;
        }

        private class DataSourceConverter : StringConverter
        {
            private StandardValuesCollection _standardValues;

            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                if (_standardValues == null)
                {
                    string[] dataSources = null;

                    if (HelpUtils.IsWow64())
                    {
                        List<String> dataSourceList = new List<String>();
                        // Read 64 registry key of SQL Server Instances Names. 
                        dataSourceList.AddRange(HelpUtils.GetValueNamesWow64("SOFTWARE\\Microsoft\\Microsoft SQL Server\\Instance Names\\SQL", NativeMethods.KEY_WOW64_64KEY | NativeMethods.KEY_QUERY_VALUE));
                        // Read 32 registry key of SQL Server Instances Names. 
                        dataSourceList.AddRange(HelpUtils.GetValueNamesWow64("SOFTWARE\\Microsoft\\Microsoft SQL Server\\Instance Names\\SQL", NativeMethods.KEY_WOW64_32KEY | NativeMethods.KEY_QUERY_VALUE));
                        dataSources = dataSourceList.ToArray();
                    }
                    else
                    {
                        // Look in the registry for all local SQL Server instances
                        Win32.RegistryKey key = Win32.Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Microsoft SQL Server\\Instance Names\\SQL");
                        if (key != null)
                        {
                            using (key)
                            {
                                dataSources = key.GetValueNames();
                            } // key is Disposed here
                        }
                    }

                    if (dataSources != null)
                    {
                        for (int i = 0; i < dataSources.Length; i++)
                        {
                            if (String.Equals(dataSources[i], "MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
                            {
                                dataSources[i] = ".";
                            }
                            else
                            {
                                dataSources[i] = ".\\" + dataSources[i];
                            }
                        }
                        _standardValues = new StandardValuesCollection(dataSources);
                    }
                    else
                    {
                        _standardValues = new StandardValuesCollection(new string[0]);
                    }
                }
                return _standardValues;
            }
        }
    }
}
