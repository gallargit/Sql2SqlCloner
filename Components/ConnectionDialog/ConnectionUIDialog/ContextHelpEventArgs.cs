//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System.Drawing;
using System.Windows.Forms;

namespace Microsoft.Data.ConnectionUI
{
    public class ContextHelpEventArgs : HelpEventArgs
    {
        public DataConnectionDialogContext Context { get; }

        public ContextHelpEventArgs(DataConnectionDialogContext context, Point mousePos) : base(mousePos)
        {
            Context = context;
        }
    }
}
