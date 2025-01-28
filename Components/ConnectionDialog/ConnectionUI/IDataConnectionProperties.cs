//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;

namespace Microsoft.Data.ConnectionUI
{
    public interface IDataConnectionProperties
    {
        bool IsComplete { get; }
        bool IsExtensible { get; }
        object this[string propertyName] { get; set; }
        void Add(string propertyName);
        bool Contains(string propertyName);
        void Parse(string s);
        event EventHandler PropertyChanged;
        void Remove(string propertyName);
        void Reset();
        void Reset(string propertyName);
        string Test();
        string ToDisplayString();
        string ToFullString();
    }
}
