//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.Data.ConnectionUI
{
    public interface IDataConnectionUIControl
    {
        void Initialize(IDataConnectionProperties connectionProperties);
        void LoadProperties();
    }
}
