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
    public class AdoDotNetConnectionProperties : IDataConnectionProperties, ICustomTypeDescriptor
    {
        public event EventHandler PropertyChanged;
        public SqlConnectionStringBuilder ConnectionStringBuilder { get; }

        public AdoDotNetConnectionProperties()
        {
            ConnectionStringBuilder = new SqlConnectionStringBuilder
            {
                BrowsableConnectionString = false
            };
        }

        public virtual void Reset()
        {
            ConnectionStringBuilder.Clear();
            OnPropertyChanged(EventArgs.Empty);
        }

        public virtual void Parse(string s)
        {
            ConnectionStringBuilder.ConnectionString = s;
            OnPropertyChanged(EventArgs.Empty);
        }

        public virtual bool IsExtensible
        {
            get
            {
                return !ConnectionStringBuilder.IsFixedSize;
            }
        }

        public virtual void Add(string propertyName)
        {
            if (!ConnectionStringBuilder.ContainsKey(propertyName))
            {
                ConnectionStringBuilder.Add(propertyName, string.Empty);
                OnPropertyChanged(EventArgs.Empty);
            }
        }

        public virtual bool Contains(string propertyName)
        {
            return ConnectionStringBuilder.ContainsKey(propertyName);
        }

        public virtual object this[string propertyName]
        {
            get
            {
                // Property name must not be null
                if (propertyName == null)
                {
                    throw new ArgumentNullException(nameof(propertyName));
                }

                // If property doesn't exist, just return null
                if (!ConnectionStringBuilder.TryGetValue(propertyName, out _))
                {
                    return null;
                }

                // If property value has been set, return this value
                if (ConnectionStringBuilder.ShouldSerialize(propertyName))
                {
                    return ConnectionStringBuilder[propertyName];
                }

                // Get the property's default value (if any)
                object val = ConnectionStringBuilder[propertyName];

                // If a default value exists, return it
                if (val != null)
                {
                    return val;
                }

                // No value has been set and no default value exists, so return DBNull.Value
                return DBNull.Value;
            }
            set
            {
                // Property name must not be null
                if (propertyName == null)
                {
                    throw new ArgumentNullException(nameof(propertyName));
                }

                // Remove the value
                ConnectionStringBuilder.Remove(propertyName);

                // Handle cases where value is DBNull.Value
                if (value == DBNull.Value)
                {
                    // Leave the property in the reset state
                    OnPropertyChanged(EventArgs.Empty);
                    return;
                }

                // Get the property's default value (if any)
                ConnectionStringBuilder.TryGetValue(propertyName, out object val);

                // Set the value
                ConnectionStringBuilder[propertyName] = value;

                // If the value is equal to the default, remove it again
                if (Equals(val, value))
                {
                    ConnectionStringBuilder.Remove(propertyName);
                }

                OnPropertyChanged(EventArgs.Empty);
            }
        }

        public virtual void Remove(string propertyName)
        {
            if (ConnectionStringBuilder.ContainsKey(propertyName))
            {
                ConnectionStringBuilder.Remove(propertyName);
                OnPropertyChanged(EventArgs.Empty);
            }
        }

        public virtual void Reset(string propertyName)
        {
            if (ConnectionStringBuilder.ContainsKey(propertyName))
            {
                ConnectionStringBuilder.Remove(propertyName);
                OnPropertyChanged(EventArgs.Empty);
            }
        }

        public virtual bool IsComplete
        {
            get
            {
                return true;
            }
        }

        public virtual void Test()
        {
            string testString = ToTestString();
            // If the connection string is empty, don't even bother testing
            if (string.IsNullOrEmpty(testString))
            {
                throw new InvalidOperationException("No connection properties have been set.");
            }

            // Create a connection object
            var connection = new SqlConnection();

            // Try to open it
            try
            {
                connection.ConnectionString = testString;
                connection.Open();
                Inspect(connection);
            }
            finally
            {
                connection.Dispose();
            }
        }

        public override string ToString()
        {
            return ToFullString();
        }

        public virtual string ToFullString()
        {
            return ConnectionStringBuilder.ConnectionString;
        }

        public virtual string ToDisplayString()
        {
            PropertyDescriptorCollection sensitiveProperties = GetProperties(new Attribute[] { PasswordPropertyTextAttribute.Yes });
            List<KeyValuePair<string, object>> savedValues = new List<KeyValuePair<string, object>>();
            foreach (PropertyDescriptor sensitiveProperty in sensitiveProperties)
            {
                string propertyName = sensitiveProperty.DisplayName;
                if (ConnectionStringBuilder.ShouldSerialize(propertyName))
                {
                    savedValues.Add(new KeyValuePair<string, object>(propertyName, ConnectionStringBuilder[propertyName]));
                    ConnectionStringBuilder.Remove(propertyName);
                }
            }
            try
            {
                return ConnectionStringBuilder.ConnectionString;
            }
            finally
            {
                foreach (KeyValuePair<string, object> savedValue in savedValues)
                {
                    if (savedValue.Value != null)
                    {
                        ConnectionStringBuilder[savedValue.Key] = savedValue.Value;
                    }
                }
            }
        }

        protected virtual PropertyDescriptor DefaultProperty
        {
            get
            {
                return TypeDescriptor.GetDefaultProperty(ConnectionStringBuilder, true);
            }
        }

        protected virtual PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            return TypeDescriptor.GetProperties(ConnectionStringBuilder, attributes);
        }

        protected virtual void OnPropertyChanged(EventArgs e)
        {
            PropertyChanged?.Invoke(this, e);
        }

        protected virtual string ToTestString()
        {
            return ConnectionStringBuilder.ConnectionString;
        }

        protected virtual void Inspect(SqlConnection connection)
        {
        }

        #region ICustomTypeDescriptor implementation

        string ICustomTypeDescriptor.GetClassName()
        {
            return TypeDescriptor.GetClassName(ConnectionStringBuilder, true);
        }

        string ICustomTypeDescriptor.GetComponentName()
        {
            return TypeDescriptor.GetComponentName(ConnectionStringBuilder, true);
        }

        AttributeCollection ICustomTypeDescriptor.GetAttributes()
        {
            return TypeDescriptor.GetAttributes(ConnectionStringBuilder, true);
        }

        object ICustomTypeDescriptor.GetEditor(Type editorBaseType)
        {
            return TypeDescriptor.GetEditor(ConnectionStringBuilder, editorBaseType, true);
        }

        TypeConverter ICustomTypeDescriptor.GetConverter()
        {
            return TypeDescriptor.GetConverter(ConnectionStringBuilder, true);
        }

        PropertyDescriptor ICustomTypeDescriptor.GetDefaultProperty()
        {
            return DefaultProperty;
        }

        PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties()
        {
            return GetProperties(new Attribute[0]);
        }

        PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties(Attribute[] attributes)
        {
            return GetProperties(attributes);
        }

        EventDescriptor ICustomTypeDescriptor.GetDefaultEvent()
        {
            return TypeDescriptor.GetDefaultEvent(ConnectionStringBuilder, true);
        }

        EventDescriptorCollection ICustomTypeDescriptor.GetEvents()
        {
            return TypeDescriptor.GetEvents(ConnectionStringBuilder, true);
        }

        EventDescriptorCollection ICustomTypeDescriptor.GetEvents(Attribute[] attributes)
        {
            return TypeDescriptor.GetEvents(ConnectionStringBuilder, attributes, true);
        }

        object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor pd)
        {
            return ConnectionStringBuilder;
        }

        #endregion
    }
}
