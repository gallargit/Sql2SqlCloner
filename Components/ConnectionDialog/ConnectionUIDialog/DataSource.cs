//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.Data.ConnectionUI
{
    public class DataSource
    {
        private readonly string _displayName;
        private DataProvider _defaultProvider;

        private DataSource()
        {
            _displayName = "<other>";
            Providers = new DataProviderCollection(this);
        }

        public DataSource(string name, string displayName)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _displayName = displayName;
            Providers = new DataProviderCollection(this);
        }

        public static void AddStandardDataSources(DataConnectionDialog dialog)
        {
            dialog.DataSources.Add(SqlDataSource);
            dialog.UnspecifiedDataSource.Providers.Add(DataProvider.SqlDataProvider);
            dialog.DataSources.Add(dialog.UnspecifiedDataSource);
        }

        public static DataSource SqlDataSource
        {
            get
            {
                if (_sqlDataSource == null)
                {
                    _sqlDataSource = new DataSource("MicrosoftSqlServer", "Microsoft SQL Server");
                    _sqlDataSource.Providers.Add(DataProvider.SqlDataProvider);
                    _sqlDataSource.DefaultProvider = DataProvider.SqlDataProvider;
                }
                return _sqlDataSource;
            }
        }
        private static DataSource _sqlDataSource;

        public string Name { get; }

        public string DisplayName
        {
            get
            {
                return _displayName ?? Name;
            }
        }

        public DataProvider DefaultProvider
        {
            get
            {
                switch (Providers.Count)
                {
                    case 0:
                        Debug.Assert(_defaultProvider == null);
                        return null;
                    case 1:
                        // If there is only one data provider, it must be the default
                        IEnumerator<DataProvider> e = Providers.GetEnumerator();
                        e.MoveNext();
                        return e.Current;
                    default:
                        return (Name != null) ? _defaultProvider : null;
                }
            }
            set
            {
                if (Providers.Count == 1 && _defaultProvider != null && _defaultProvider != value)
                {
                    throw new InvalidOperationException("The default data provider cannot be changed when there is only one data provider available.");
                }
                if (value != null && !Providers.Contains(value))
                {
                    throw new InvalidOperationException("The data provider was not found.");
                }
                _defaultProvider = value;
            }
        }

        public ICollection<DataProvider> Providers { get; }

        internal static DataSource CreateUnspecified()
        {
            return new DataSource();
        }

        private class DataProviderCollection : ICollection<DataProvider>
        {
            private readonly ICollection<DataProvider> _list;
            private readonly DataSource _source;

            public DataProviderCollection(DataSource source)
            {
                Debug.Assert(source != null);

                _list = new List<DataProvider>();
                _source = source;
            }

            public int Count
            {
                get
                {
                    return _list.Count;
                }
            }

            public bool IsReadOnly
            {
                get
                {
                    return false;
                }
            }

            public void Add(DataProvider item)
            {
                if (item == null)
                {
                    throw new ArgumentNullException(nameof(item));
                }
                if (!_list.Contains(item))
                {
                    _list.Add(item);
                }
            }

            public bool Contains(DataProvider item)
            {
                return _list.Contains(item);
            }

            public bool Remove(DataProvider item)
            {
                bool result = _list.Remove(item);
                if (item == _source._defaultProvider)
                {
                    _source._defaultProvider = null;
                }
                return result;
            }

            public void Clear()
            {
                _list.Clear();
                _source._defaultProvider = null;
            }

            public void CopyTo(DataProvider[] array, int arrayIndex)
            {
                _list.CopyTo(array, arrayIndex);
            }

            public IEnumerator<DataProvider> GetEnumerator()
            {
                return _list.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return _list.GetEnumerator();
            }
        }
    }
}
