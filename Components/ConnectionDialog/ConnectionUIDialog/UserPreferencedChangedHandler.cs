//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace Microsoft.Data.ConnectionUI
{
    internal sealed class UserPreferenceChangedHandler : IComponent
    {
        private readonly Form _form;
        public event EventHandler Disposed;

        public UserPreferenceChangedHandler(Form form)
        {
            Debug.Assert(form != null);
            SystemEvents.UserPreferenceChanged += HandleUserPreferenceChanged;
            _form = form;
        }

        ~UserPreferenceChangedHandler()
        {
            Dispose(false);
        }

        public ISite Site
        {
            get
            {
                return _form.Site;
            }
            set
            {
                // This shouldn't be called
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void HandleUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Need to update the font
            IUIService uiService = (_form.Site != null) ? _form.Site.GetService(typeof(IUIService)) as IUIService : null;
            if (uiService?.Styles["DialogFont"] is Font newFont)
            {
                _form.Font = newFont;
            }
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.UserPreferenceChanged -= HandleUserPreferenceChanged;
                Disposed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
