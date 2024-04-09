//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.Data.ConnectionUI
{
    public enum DataConnectionDialogContext
    {
        Main = 0x02000000,
        MainDataSourceTextBox = 0x02100001,
        MainChangeDataSourceButton = 0x02100002,
        MainConnectionUIControl = 0x02200000,
        MainSqlConnectionUIControl = 0x02200001,
        MainGenericConnectionUIControl = 0x022FFFFF,
        MainAdvancedButton = 0x02400000,
        MainTestConnectionButton = 0x02800001,
        MainAcceptButton = 0x0280000E,
        MainCancelButton = 0x0280000F,
        Advanced = 0x04000000,
        AdvancedPropertyGrid = 0x04000001,
        AdvancedTextBox = 0x04000002,
        AdvancedOkButton = 0x04000003,
        AdvancedCancelButton = 0x04000004,
        AddProperty = 0x08000000,
        AddPropertyTextBox = 0x08000001,
        AddPropertyOkButton = 0x0800000E,
        AddPropertyCancelButton = 0x0800000F
    }
}
