//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.ConnectionDialog.ConnectionUIDialog;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Microsoft.Data.ConnectionUI
{
    public partial class SqlConnectionUIControl : UserControl, IDataConnectionUIControl, ISuccess
    {
        private readonly string InitialConnectionString;
        private const int IntegratedAuthenticationSelected = 0;
        private bool _loading;
        private readonly List<object> _servers = new List<object>();
        private readonly List<object> _databases = new List<object>();
        private readonly Thread _uiThread;
        private Task _serverEnumerationTask;
        private Task _databaseEnumerationTask;
        private string currentOleDBProvider;
        private bool currentUserInstanceSetting;
        private ControlProperties _controlProperties;
        private readonly object loadingItem = "(loading...)";

        public SqlConnectionUIControl(string initialConnectionString)
        {
            InitializeComponent();
            RightToLeft = RightToLeft.Inherit;
            authenticationDropdown.Items.Add("Integrated");
            authenticationDropdown.Items.Add("SQL Server");
            foreach (var auth in Enum.GetValues(typeof(SqlAuthenticationMethod)))
            {
                if ((int)auth > 1)
                {
                    authenticationDropdown.Items.Add(AddSpacesToSentence(auth.ToString()));
                }
            }

            InitialConnectionString = initialConnectionString;
            int requiredHeight = LayoutUtils.GetPreferredCheckBoxHeight(trustServerCertificateCheckBox);
            if (trustServerCertificateCheckBox.Height < requiredHeight)
            {
                trustServerCertificateCheckBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
                loginTableLayoutPanel.Height += loginTableLayoutPanel.Margin.Bottom;
                loginTableLayoutPanel.Margin = new Padding(loginTableLayoutPanel.Margin.Left, loginTableLayoutPanel.Margin.Top, loginTableLayoutPanel.Margin.Right, 0);
            }

            // Apparently WinForms automatically sets the accessible name for text boxes
            // based on a label previous to it, but does not do the same when it is
            // proceeded by a radio button.  So, simulate that behavior here
            selectDatabaseComboBox.AccessibleName = TextWithoutMnemonics(selectDatabaseRadioButton.Text);
            attachDatabaseTextBox.AccessibleName = TextWithoutMnemonics(attachDatabaseRadioButton.Text);

            _uiThread = Thread.CurrentThread;
        }

        private string AddSpacesToSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }
            StringBuilder newText = new StringBuilder(text.Length * 2);
            newText.Append(text[0]);
            for (int i = 1; i < text.Length; i++)
            {
                if (char.IsUpper(text[i]))
                {
                    if ((text[i - 1] != ' ' && !char.IsUpper(text[i - 1])) ||
                        (char.IsUpper(text[i - 1]) && i < text.Length - 1 && !char.IsUpper(text[i + 1])))
                    {
                        newText.Append(' ');
                    }
                }
                newText.Append(text[i]);
            }
            return newText.ToString();
        }

        public void Initialize(IDataConnectionProperties connectionProperties)
        {
            if (connectionProperties == null)
            {
                throw new ArgumentNullException(nameof(connectionProperties));
            }

            if (!(connectionProperties is SqlConnectionProperties))
            {
                throw new ArgumentException("The connection properties object must be of type SqlConnectionProperties");
            }

            _controlProperties = new ControlProperties(connectionProperties);
        }

        public void LoadProperties()
        {
            _loading = true;

            Task.Run(() => EnumerateServers());

            if (currentOleDBProvider != Properties.Provider)
            {
                selectDatabaseComboBox.Items.Clear(); // a provider change requires a refresh here
                currentOleDBProvider = Properties.Provider;
            }

            try
            {
                var cb = new SqlConnectionStringBuilder(InitialConnectionString);
                if (cb.IntegratedSecurity)
                {
                    Properties.UseWindowsAuthentication = true;
                }
                else
                {
                    Properties.UseWindowsAuthentication = false;
                    Properties.SelectedAuthenticationMethod = cb.Authentication;
                    if (cb.Authentication == SqlAuthenticationMethod.NotSpecified && !string.IsNullOrEmpty(cb.UserID))
                    {
                        Properties.SelectedAuthenticationMethod = SqlAuthenticationMethod.SqlPassword;
                    }
                }
                if (!string.IsNullOrEmpty(cb.DataSource))
                {
                    Properties.ServerName = cb.DataSource;
                }
                if (!string.IsNullOrEmpty(cb.UserID))
                {
                    Properties.UserName = cb.UserID;
                }
                if (!string.IsNullOrEmpty(cb.Password))
                {
                    Properties.Password = cb.Password;
                }
                if (!string.IsNullOrEmpty(cb.InitialCatalog))
                {
                    Properties.DatabaseName = cb.InitialCatalog;
                }
                if (!string.IsNullOrEmpty(cb.AttachDBFilename))
                {
                    Properties.DatabaseFile = cb.InitialCatalog;
                }
                Properties.TrustServerCertificate = cb.TrustServerCertificate;
            }
            catch { }

            serverComboBox.Text = Properties.ServerName;
            if (Properties.UseWindowsAuthentication)
            {
                authenticationDropdown.SelectedIndex = 0;
            }
            else
            {
                authenticationDropdown.SelectedIndex = (int)Properties.SelectedAuthenticationMethod;
            }
            if (currentUserInstanceSetting != Properties.UserInstance)
            {
                selectDatabaseComboBox.Items.Clear(); // this change requires a refresh here
            }
            currentUserInstanceSetting = Properties.UserInstance;
            userNameTextBox.Text = Properties.UserName;
            passwordTextBox.Text = Properties.Password;
            if (!_loading)
            {
                trustServerCertificateCheckBox.Checked = Properties.TrustServerCertificate = true;
            }

            if (string.IsNullOrEmpty(Properties.DatabaseFile))
            {
                selectDatabaseRadioButton.Checked = true;
                selectDatabaseComboBox.Text = Properties.DatabaseName;
                attachDatabaseTextBox.Text = null;
                logicalDatabaseNameTextBox.Text = null;
            }
            else
            {
                attachDatabaseRadioButton.Checked = true;
                selectDatabaseComboBox.Text = null;
                attachDatabaseTextBox.Text = Properties.DatabaseFile;
                logicalDatabaseNameTextBox.Text = Properties.LogicalDatabaseName;
            }
            //try to pre-load databases if the selected server is localhost
            if (Properties.ServerName == "." || Properties.ServerName.StartsWith("(local)") || Properties.ServerName.StartsWith("localhost") ||
                Properties.ServerName.StartsWith("127.0.0.1"))
            {
                _databaseEnumerationTask = Task.Run(() => EnumerateDatabases(true, 1));
            }

            _loading = false;
        }

        // Simulate RTL mirroring
        protected override void OnRightToLeftChanged(EventArgs e)
        {
            base.OnRightToLeftChanged(e);
            if (ParentForm?.RightToLeftLayout == true && RightToLeft == RightToLeft.Yes)
            {
                LayoutUtils.MirrorControl(serverLabel, serverTableLayoutPanel);
                LayoutUtils.MirrorControl(authenticationDropdown);
                LayoutUtils.MirrorControl(loginTableLayoutPanel);
                LayoutUtils.MirrorControl(selectDatabaseRadioButton);
                LayoutUtils.MirrorControl(selectDatabaseComboBox);
                LayoutUtils.MirrorControl(attachDatabaseRadioButton);
                LayoutUtils.MirrorControl(attachDatabaseTableLayoutPanel);
                LayoutUtils.MirrorControl(logicalDatabaseNameLabel);
                LayoutUtils.MirrorControl(logicalDatabaseNameTextBox);
            }
            else
            {
                LayoutUtils.UnmirrorControl(logicalDatabaseNameTextBox);
                LayoutUtils.UnmirrorControl(logicalDatabaseNameLabel);
                LayoutUtils.UnmirrorControl(attachDatabaseTableLayoutPanel);
                LayoutUtils.UnmirrorControl(attachDatabaseRadioButton);
                LayoutUtils.UnmirrorControl(selectDatabaseComboBox);
                LayoutUtils.UnmirrorControl(selectDatabaseRadioButton);
                LayoutUtils.UnmirrorControl(loginTableLayoutPanel);
                LayoutUtils.UnmirrorControl(authenticationDropdown);
                LayoutUtils.UnmirrorControl(serverLabel, serverTableLayoutPanel);
            }
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            Size baseSize = Size;
            MinimumSize = Size.Empty;
            base.ScaleControl(factor, specified);
            MinimumSize = new Size(
                (int)Math.Round((float)baseSize.Width * factor.Width),
                (int)Math.Round((float)baseSize.Height * factor.Height));
        }

        [UIPermission(SecurityAction.LinkDemand, Window = UIPermissionWindow.AllWindows)]
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (ActiveControl == selectDatabaseRadioButton &&
                (keyData & Keys.KeyCode) == Keys.Down)
            {
                attachDatabaseRadioButton.Focus();
                return true;
            }
            if (ActiveControl == attachDatabaseRadioButton &&
                (keyData & Keys.KeyCode) == Keys.Down)
            {
                selectDatabaseRadioButton.Focus();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent == null)
            {
                OnFontChanged(e);
            }
        }

        private void HandleComboBoxDownKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down)
            {
                if (sender == serverComboBox)
                {
                    EnumerateServers(sender, e);
                }
                if (sender == selectDatabaseComboBox)
                {
                    EnumerateDatabases(sender, e);
                }
            }
        }

        private void EnumerateServers(object sender, EventArgs e)
        {
            if (serverComboBox.Items.Count == 0)
            {
                _servers.AddRange(SqlHelper.ListLocalSqlInstances());
                try
                {
                    if (_serverEnumerationTask?.IsCompleted != false)
                    {
                        EnumerateServers();
                    }
                }
                catch { }
            }
        }

        private void SetServer(object sender, EventArgs e)
        {
            if (!_loading)
            {
                Properties.ServerName = serverComboBox.Text;
                if (serverComboBox.Items.Count == 0 && _serverEnumerationTask == null)
                {
                    // Start an enumeration of servers
                    _serverEnumerationTask = Task.Run(() => EnumerateServers());
                }
            }
            SetDatabaseGroupBoxStatus(sender, e);
            selectDatabaseComboBox.Items.Clear(); // a server change requires a refresh here
        }

        private void RefreshServers(object sender, EventArgs e)
        {
            serverComboBox.Items.Clear();
            EnumerateServers(sender, e);
        }

        private void SetAuthenticationOption(object sender, EventArgs e)
        {
            if (!_loading)
            {
                if (authenticationDropdown.SelectedIndex == IntegratedAuthenticationSelected)
                {
                    Properties.UseWindowsAuthentication = true;
                }
                else
                {
                    Properties.SelectedAuthenticationMethod = (SqlAuthenticationMethod)authenticationDropdown.SelectedIndex;
                }
            }
            if (authenticationDropdown.SelectedIndex == IntegratedAuthenticationSelected ||
                authenticationDropdown.SelectedIndex == (int)SqlAuthenticationMethod.ActiveDirectoryIntegrated ||
                authenticationDropdown.SelectedIndex == (int)SqlAuthenticationMethod.ActiveDirectoryDeviceCodeFlow)
            {
                if (!_loading)
                {
                    Properties.UserName = null;
                    Properties.Password = null;
                    Properties.TrustServerCertificate = true;
                }
                userNameTextBox.Enabled = passwordTextBox.Enabled = false;
            }
            else if (authenticationDropdown.SelectedIndex == (int)SqlAuthenticationMethod.SqlPassword ||
                authenticationDropdown.SelectedIndex == (int)SqlAuthenticationMethod.ActiveDirectoryPassword ||
                authenticationDropdown.SelectedIndex == (int)SqlAuthenticationMethod.ActiveDirectoryServicePrincipal)
            {
                if (!_loading)
                {
                    Properties.UseWindowsAuthentication = false;
                    SetUserName(sender, e);
                    SetPassword(sender, e);
                    SetTrustServerCertificate(sender, e);
                }
                userNameTextBox.Enabled = passwordTextBox.Enabled = true;
            }
            else
            {
                if (!_loading)
                {
                    Properties.UseWindowsAuthentication = false;
                    SetUserName(sender, e);
                    Properties.Password = null;
                    SetTrustServerCertificate(sender, e);
                }
                userNameTextBox.Enabled = true;
                passwordTextBox.Enabled = false;
            }
            trustServerCertificateCheckBox.Checked = Properties.TrustServerCertificate;
            SetDatabaseGroupBoxStatus(sender, e);
            selectDatabaseComboBox.Items.Clear(); // an authentication change requires a refresh here
        }

        private void SetUserName(object sender, EventArgs e)
        {
            if (!_loading)
            {
                Properties.UserName = userNameTextBox.Text;
            }
            SetDatabaseGroupBoxStatus(sender, e);
            selectDatabaseComboBox.Items.Clear(); // a user name change requires a refresh here
        }

        private void SetPassword(object sender, EventArgs e)
        {
            if (!_loading)
            {
                Properties.Password = passwordTextBox.Text;
                passwordTextBox.Text = passwordTextBox.Text; // forces reselection of all text
            }
            selectDatabaseComboBox.Items.Clear(); // a password change requires a refresh here
        }

        private void SetTrustServerCertificate(object sender, EventArgs e)
        {
            if (!_loading)
            {
                Properties.TrustServerCertificate = trustServerCertificateCheckBox.Checked;
            }
        }

        private void SetDatabaseGroupBoxStatus(object sender, EventArgs e)
        {
            databaseGroupBox.Enabled = serverComboBox.Text.Trim().Length > 0 &&
                (authenticationDropdown.SelectedIndex == IntegratedAuthenticationSelected ||
                userNameTextBox.Text.Trim().Length > 0);
        }

        private void SetDatabaseOption(object sender, EventArgs e)
        {
            if (selectDatabaseRadioButton.Checked)
            {
                SetDatabase(sender, e);
                SetAttachDatabase(sender, e);
                selectDatabaseComboBox.Enabled = true;
                attachDatabaseTableLayoutPanel.Enabled = false;
                logicalDatabaseNameLabel.Enabled = false;
                logicalDatabaseNameTextBox.Enabled = false;
            }
            else
            {
                SetAttachDatabase(sender, e);
                SetLogicalFilename(sender, e);
                selectDatabaseComboBox.Enabled = false;
                attachDatabaseTableLayoutPanel.Enabled = true;
                logicalDatabaseNameLabel.Enabled = true;
                logicalDatabaseNameTextBox.Enabled = true;
            }
        }

        private void SetDatabase(object sender, EventArgs e)
        {
            if (!_loading)
            {
                Properties.DatabaseName = selectDatabaseComboBox.Text;
                if (selectDatabaseComboBox.Items.Count == 0 && _databaseEnumerationTask?.IsCompleted != true)
                {
                    // Start an enumeration of databases
                    _databaseEnumerationTask = Task.Run(() => EnumerateDatabases(sender == null && e == null));
                }
            }
        }

        private void EnumerateDatabases(object sender, EventArgs e)
        {
            if (selectDatabaseComboBox.Items.Count == 0)
            {
                selectDatabaseComboBox.Items.Add(loadingItem);
                var currentCursor = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
                try
                {
                    if (_databaseEnumerationTask?.IsCompleted != true)
                    {
                        _databaseEnumerationTask = Task.Run(() => EnumerateDatabases());
                    }
                }
                finally
                {
                    Cursor.Current = currentCursor;
                }
            }
        }

        private void SetAttachDatabase(object sender, EventArgs e)
        {
            if (!_loading)
            {
                if (selectDatabaseRadioButton.Checked)
                {
                    Properties.DatabaseFile = null;
                }
                else /* if (attachDatabaseRadioButton.Checked) */
                {
                    Properties.DatabaseFile = attachDatabaseTextBox.Text;
                }
            }
        }

        private void SetLogicalFilename(object sender, EventArgs e)
        {
            if (!_loading)
            {
                if (selectDatabaseRadioButton.Checked)
                {
                    Properties.LogicalDatabaseName = null;
                }
                else /* if (attachDatabaseRadioButton.Checked) */
                {
                    Properties.LogicalDatabaseName = logicalDatabaseNameTextBox.Text;
                }
            }
        }

        private void Browse(object sender, EventArgs e)
        {
            var fileDialog = new OpenFileDialog
            {
                Title = "Select SQL Server Database File",
                Multiselect = false,
                RestoreDirectory = true,
                Filter = "Microsoft SQL Server Databases (*.mdf)|*.mdf|All Files (*.*)|*.*",
                DefaultExt = "mdf"
            };
            Container?.Add(fileDialog);
            try
            {
                DialogResult result = fileDialog.ShowDialog(ParentForm);
                if (result == DialogResult.OK)
                {
                    attachDatabaseTextBox.Text = fileDialog.FileName.Trim();
                }
            }
            finally
            {
                Container?.Remove(fileDialog);
                fileDialog.Dispose();
            }
        }

        private void TrimControlText(object sender, EventArgs e)
        {
            (sender as Control).Text.Trim();
        }

        private void EnumerateServers()
        {
            _servers.Clear();
            _servers.AddRange(SqlHelper.ListLocalSqlInstances());
            // Populate the server combo box items (must occur on the UI thread)
            if (Thread.CurrentThread == _uiThread)
            {
                PopulateServerComboBox();
            }
            else if (IsHandleCreated)
            {
                BeginInvoke(new ThreadStart(PopulateServerComboBox));
            }

            // Perform the enumeration
            System.Data.DataTable dataTable = null;
            try
            {
                dataTable = Sql.SqlDataSourceEnumerator.Instance.GetDataSources();
            }
            catch
            {
                dataTable = new System.Data.DataTable
                {
                    Locale = System.Globalization.CultureInfo.InvariantCulture
                };
            }
            if (dataTable?.Rows.Count > 0)
            {
                // Create the object array of server names (with instances appended)
                var tempList = new List<object>();
                for (int i = 0; i < dataTable.Rows.Count; i++)
                {
                    string name = dataTable.Rows[i]["ServerName"].ToString();
                    string instance = dataTable.Rows[i]["InstanceName"].ToString();
                    string tempItem = name;
                    if (instance.Length > 0)
                    {
                        tempItem += "\\" + instance;
                    }
                    tempList.Add(tempItem);
                }
                // Sort the list
                _servers.AddRange(tempList.OrderBy(a => a));

                // Populate the server combo box items (must occur on the UI thread)
                if (Thread.CurrentThread == _uiThread)
                {
                    PopulateServerComboBox();
                }
                else if (IsHandleCreated)
                {
                    BeginInvoke(new ThreadStart(PopulateServerComboBox));
                }
            }
        }

        private void PopulateServerComboBox()
        {
            if (serverComboBox.Items.Count == 0)
            {
                if (_servers.Count > 0)
                {
                    serverComboBox.Items.AddRange(_servers.ToArray());
                }
                else
                {
                    serverComboBox.Items.Add(string.Empty);
                }
            }
        }

        private void EnumerateDatabases(bool force = false, int timeOut = 0)
        {
            // Perform the enumeration
            System.Data.DataTable dataTable = null;
            SqlConnection connection = null;
            System.Data.IDataReader reader = null;
            if (force || Properties.UseWindowsAuthentication || Properties.SelectedAuthenticationMethod == SqlAuthenticationMethod.SqlPassword)
            {
                try
                {
                    // Get a basic connection
                    connection = Properties.GetBasicConnection(timeOut);

                    // Create a command to check if the database is on SQL Azure.
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT CASE WHEN SERVERPROPERTY(N'EDITION') = 'SQL Data Services' OR SERVERPROPERTY(N'EDITION') = 'SQL Azure' THEN 1 ELSE 0 END";

                    // Open the connection
                    connection.Open();

                    // SQL Azure doesn't support HAS_DBACCESS at this moment.
                    // Change the command text to get database names accordingly
                    if ((int)command.ExecuteScalar() == 1)
                    {
                        command.CommandText = "SELECT name FROM master.dbo.sysdatabases ORDER BY name";
                    }
                    else
                    {
                        command.CommandText = "SELECT name FROM master.dbo.sysdatabases WHERE HAS_DBACCESS(name) = 1 ORDER BY name";
                    }

                    // Execute the command
                    reader = command.ExecuteReader();

                    // Read into the data table
                    dataTable = new System.Data.DataTable
                    {
                        Locale = System.Globalization.CultureInfo.CurrentCulture
                    };
                    dataTable.Load(reader);
                }
                catch
                {
                    if (selectDatabaseComboBox.Items.Count == 1 && selectDatabaseComboBox.Items[0] == loadingItem)
                    {
                        selectDatabaseComboBox.Invoke((Action)delegate
                        {
                            try
                            {
                                selectDatabaseComboBox.Items.Clear();
                            }
                            catch { }
                        });
                    }
                    dataTable = new System.Data.DataTable
                    {
                        Locale = System.Globalization.CultureInfo.InvariantCulture
                    };
                }
                finally
                {
                    reader?.Dispose();
                    connection?.Dispose();
                }
            }

            // Create the object array of database names
            _databases.Clear();
            if (dataTable != null)
            {
                for (int i = 0; i < dataTable.Rows.Count; i++)
                {
                    _databases.Add(dataTable.Rows[i]["name"]);
                }
            }

            // Populate the database combo box items (must occur on the UI thread)
            if (Thread.CurrentThread == _uiThread)
            {
                PopulateDatabaseComboBox();
            }
            else if (IsHandleCreated)
            {
                BeginInvoke(new ThreadStart(PopulateDatabaseComboBox));
            }
        }

        private void PopulateDatabaseComboBox()
        {
            if (selectDatabaseComboBox.Items.Count == 1 && selectDatabaseComboBox.Items[0] == loadingItem)
            {
                try
                {
                    selectDatabaseComboBox.Items.Clear();
                }
                catch { }
            }
            if (selectDatabaseComboBox.Items.Count == 0 && _databases.Count > 0)
            {
                selectDatabaseComboBox.Items.AddRange(_databases.ToArray());
            }
            _databaseEnumerationTask = null;
        }

        private static string TextWithoutMnemonics(string text)
        {
            if (text == null)
            {
                return null;
            }

            int index = text.IndexOf('&');
            if (index == -1)
            {
                return text;
            }

            StringBuilder str = new StringBuilder(text.Substring(0, index));
            for (; index < text.Length; ++index)
            {
                if (text[index] == '&')
                {
                    // Skip this & and copy the next character instead
                    index++;
                }
                if (index < text.Length)
                {
                    str.Append(text[index]);
                }
            }

            return str.ToString();
        }

        public void TestButtonSuccess(string username)
        {
            if (!string.IsNullOrEmpty(username) && userNameTextBox.Enabled)
            {
                userNameTextBox.Text = username;
            }

            SetDatabase(null, null);
        }

        private ControlProperties Properties
        {
            get
            {
                return _controlProperties;
            }
        }

        private class ControlProperties
        {
            private readonly IDataConnectionProperties _properties;

            public ControlProperties(IDataConnectionProperties properties)
            {
                _properties = properties;
            }

            public string Provider
            {
                get
                {
                    return null;
                }
            }

            public string ServerName
            {
                get
                {
                    return _properties[ServerNameProperty] as string;
                }
                set
                {
                    if (value?.Trim().Length > 0)
                    {
                        _properties[ServerNameProperty] = value.Trim();
                    }
                    else
                    {
                        _properties.Reset(ServerNameProperty);
                    }
                }
            }

            public bool UserInstance
            {
                get
                {
                    if (_properties is SqlConnectionProperties)
                    {
                        return (bool)_properties["User Instance"];
                    }
                    return false;
                }
            }

            public SqlAuthenticationMethod SelectedAuthenticationMethod
            {
                get
                {
                    if (_properties is SqlConnectionProperties)
                    {
                        return (SqlAuthenticationMethod)_properties["Authentication"];
                    }
                    return SqlAuthenticationMethod.SqlPassword;
                }
                set
                {
                    if (_properties is SqlConnectionProperties)
                    {
                        _properties["Authentication"] = value;
                    }
                }
            }

            public bool UseWindowsAuthentication
            {
                get
                {
                    if (_properties is SqlConnectionProperties)
                    {
                        return (bool)_properties["Integrated Security"];
                    }
                    return false;
                }
                set
                {
                    if (_properties is SqlConnectionProperties)
                    {
                        if (value)
                        {
                            _properties["Integrated Security"] = value;
                            _properties.Reset("Authentication");
                        }
                        else
                        {
                            _properties.Reset("Integrated Security");
                        }
                    }
                }
            }

            public string UserName
            {
                get
                {
                    return _properties[UserNameProperty] as string;
                }
                set
                {
                    if (value?.Trim().Length > 0)
                    {
                        _properties[UserNameProperty] = value.Trim();
                    }
                    else
                    {
                        _properties.Reset(UserNameProperty);
                    }
                }
            }

            public string Password
            {
                get
                {
                    return _properties[PasswordProperty] as string;
                }
                set
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        _properties[PasswordProperty] = value;
                    }
                    else
                    {
                        _properties.Reset(PasswordProperty);
                    }
                }
            }

            public bool TrustServerCertificate
            {
                get
                {
                    return (bool)_properties["Trust Server Certificate"];
                }
                set
                {
                    if (value)
                    {
                        _properties["Trust Server Certificate"] = value;
                    }
                    else
                    {
                        _properties.Reset("Trust Server Certificate");
                    }
                }
            }

            public string DatabaseName
            {
                get
                {
                    return _properties[DatabaseNameProperty] as string;
                }
                set
                {
                    if (value?.Trim().Length > 0)
                    {
                        _properties[DatabaseNameProperty] = value.Trim();
                    }
                    else
                    {
                        _properties.Reset(DatabaseNameProperty);
                    }
                }
            }

            public string DatabaseFile
            {
                get
                {
                    return _properties[DatabaseFileProperty] as string;
                }
                set
                {
                    if (value?.Trim().Length > 0)
                    {
                        _properties[DatabaseFileProperty] = value.Trim();
                    }
                    else
                    {
                        _properties.Reset(DatabaseFileProperty);
                    }
                }
            }

            public string LogicalDatabaseName
            {
                get
                {
                    return DatabaseName;
                }
                set
                {
                    DatabaseName = value;
                }
            }

            public SqlConnection GetBasicConnection(int timeOut = 0)
            {
                SqlConnection connection = null;
                if (_properties is SqlConnectionProperties)
                {
                    var builder = new SqlConnectionStringBuilder
                    {
                        DataSource = ServerName,
                        Pooling = false,
                        TrustServerCertificate = TrustServerCertificate,
                        UserInstance = UserInstance,
                        IntegratedSecurity = UseWindowsAuthentication
                    };
                    if (timeOut > 0)
                    {
                        builder.CommandTimeout = timeOut;
                    }
                    if (!UseWindowsAuthentication)
                    {
                        builder.Authentication = SelectedAuthenticationMethod;
                        if (!string.IsNullOrEmpty(UserName))
                        {
                            builder.UserID = UserName;
                        }
                        if (!string.IsNullOrEmpty(Password))
                        {
                            builder.Password = Password;
                        }
                        if (SelectedAuthenticationMethod != SqlAuthenticationMethod.SqlPassword)
                        {
                            //azure connection
                            builder.Encrypt = SqlConnectionEncryptOption.Strict;
                        }
                    }
                    connection = new SqlConnection(builder.ConnectionString);
                }

                return connection;
            }

            private string ServerNameProperty
            {
                get
                {
                    return (_properties is SqlConnectionProperties) ? "Data Source" : null;
                }
            }

            private string UserNameProperty
            {
                get
                {
                    return (_properties is SqlConnectionProperties) ? "User ID" : null;
                }
            }

            private string PasswordProperty
            {
                get
                {
                    return (_properties is SqlConnectionProperties) ? "Password" : null;
                }
            }

            private string DatabaseNameProperty
            {
                get
                {
                    return (_properties is SqlConnectionProperties) ? "Initial Catalog" : null;
                }
            }

            private string DatabaseFileProperty
            {
                get
                {
                    return (_properties is SqlConnectionProperties) ? "AttachDbFilename" : null;
                }
            }
        }
    }
}
