//------------------------------------------------------------------------------
// <copyright company="Microsoft Corporation">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using Microsoft.ConnectionDialog.ConnectionUIDialog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace Microsoft.Data.ConnectionUI
{
    public partial class DataConnectionDialog : Form
    {
        private readonly string InitialConnectionString;
        public DataConnectionDialog(string initialConnectionString)
        {
            InitialConnectionString = initialConnectionString;
            InitializeComponent();
            dataSourceTextBox.Width = 0;

            // Make sure we handle a user preference change
            components.Add(new UserPreferenceChangedHandler(this));

            // Configure initial label values
            _chooseDataSourceTitle = "Choose Data Source";
            _chooseDataSourceAcceptText = "OK";
            _dataSources = new DataSourceCollection(this)
            {
                DataSource.SqlDataSource
            };
        }

        new public DialogResult Show()
        {
            return Show(this);
        }

        public static DialogResult Show(DataConnectionDialog dialog)
        {
            return Show(dialog, null);
        }

        public static DialogResult Show(DataConnectionDialog dialog, IWin32Window owner)
        {
            if (dialog == null)
            {
                throw new ArgumentNullException(nameof(dialog));
            }
            if (dialog.DataSources.Count == 0)
            {
                throw new InvalidOperationException("No data sources are available.");
            }
            foreach (DataSource dataSource in dialog.DataSources)
            {
                if (dataSource.Providers.Count == 0)
                {
                    throw new InvalidOperationException(string.Format("The &apos;{0}&apos; data source has no association with any data providers." + dataSource.DisplayName.Replace("'", "''")));
                }
            }
            Application.ThreadException += dialog.HandleDialogException;
            dialog._showingDialog = true;
            try
            {
                // If there is no selected data source or provider, show the data connection source dialog
                if (dialog.SelectedDataSource == null || dialog.SelectedDataProvider == null)
                {
                    throw new Exception("No data sources found");
                }
                else
                {
                    dialog._saveSelection = false;
                }
                if (owner == null)
                {
                    dialog.StartPosition = FormStartPosition.CenterScreen;
                }
                for (; ; )
                {
                    return dialog.ShowDialog(owner);
                }
            }
            finally
            {
                dialog._showingDialog = false;
                Application.ThreadException -= dialog.HandleDialogException;
            }
        }

        public string Title
        {
            get
            {
                return Text;
            }
            set
            {
                Text = value;
            }
        }

        public string HeaderLabel
        {
            get
            {
                return (_headerLabel != null) ? _headerLabel.Text : string.Empty;
            }
            set
            {
                if (_showingDialog)
                {
                    throw new InvalidOperationException("The data connection dialog state cannot be programmatically modified when the dialog is visible.");
                }
                if (_headerLabel == null && (string.IsNullOrEmpty(value)))
                {
                    return;
                }
                if (_headerLabel != null && value == _headerLabel.Text)
                {
                    return;
                }
                if (!string.IsNullOrEmpty(value))
                {
                    if (_headerLabel == null)
                    {
                        _headerLabel = new Label
                        {
                            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                            FlatStyle = FlatStyle.System,
                            Location = new Point(12, 12),
                            Margin = new Padding(3),
                            Name = "dataSourceLabel",
                            Width = dataSourceTableLayoutPanel.Width,
                            TabIndex = 100
                        };
                        Controls.Add(_headerLabel);
                    }
                    _headerLabel.Text = value;
                    MinimumSize = Size.Empty;
                    _headerLabel.Height = LayoutUtils.GetPreferredLabelHeight(_headerLabel);
                    int dy =
                        _headerLabel.Bottom +
                        _headerLabel.Margin.Bottom +
                        dataSourceLabel.Margin.Top -
                        dataSourceLabel.Top;
                    containerControl.Anchor &= ~AnchorStyles.Bottom;
                    Height += dy;
                    containerControl.Anchor |= AnchorStyles.Bottom;
                    containerControl.Top += dy;
                    dataSourceTableLayoutPanel.Top += dy;
                    dataSourceLabel.Top += dy;
                    MinimumSize = Size;
                }
                else
                {
                    if (_headerLabel != null)
                    {
                        int dy = _headerLabel.Height;
                        try
                        {
                            Controls.Remove(_headerLabel);
                        }
                        finally
                        {
                            _headerLabel.Dispose();
                            _headerLabel = null;
                        }
                        MinimumSize = Size.Empty;
                        dataSourceLabel.Top -= dy;
                        dataSourceTableLayoutPanel.Top -= dy;
                        containerControl.Top -= dy;
                        containerControl.Anchor &= ~AnchorStyles.Bottom;
                        Height -= dy;
                        containerControl.Anchor |= AnchorStyles.Bottom;
                        MinimumSize = Size;
                    }
                }
            }
        }

        public bool TranslateHelpButton
        {
            get
            {
                return _translateHelpButton;
            }
            set
            {
                _translateHelpButton = value;
            }
        }

        public string ChooseDataSourceTitle
        {
            get
            {
                return _chooseDataSourceTitle;
            }
            set
            {
                if (_showingDialog)
                {
                    throw new InvalidOperationException("The data connection dialog state cannot be programmatically modified when the dialog is visible.");
                }
                if (value == null)
                {
                    value = string.Empty;
                }
                if (value == _chooseDataSourceTitle)
                {
                    return;
                }
                _chooseDataSourceTitle = value;
            }
        }

        public string ChooseDataSourceHeaderLabel
        {
            get
            {
                return _chooseDataSourceHeaderLabel;
            }
            set
            {
                if (_showingDialog)
                {
                    throw new InvalidOperationException("The data connection dialog state cannot be programmatically modified when the dialog is visible.");
                }
                if (value == null)
                {
                    value = string.Empty;
                }
                if (value == _chooseDataSourceHeaderLabel)
                {
                    return;
                }
                _chooseDataSourceHeaderLabel = value;
            }
        }

        public string ChooseDataSourceAcceptText
        {
            get
            {
                return _chooseDataSourceAcceptText;
            }
            set
            {
                if (_showingDialog)
                {
                    throw new InvalidOperationException("The data connection dialog state cannot be programmatically modified when the dialog is visible.");
                }
                if (value == null)
                {
                    value = string.Empty;
                }
                if (value == _chooseDataSourceAcceptText)
                {
                    return;
                }
                _chooseDataSourceAcceptText = value;
            }
        }

        public string ChangeDataSourceTitle
        {
            get
            {
                return _changeDataSourceTitle;
            }
            set
            {
                if (_showingDialog)
                {
                    throw new InvalidOperationException("The data connection dialog state cannot be programmatically modified when the dialog is visible.");
                }
                if (value == null)
                {
                    value = string.Empty;
                }
                if (value == _changeDataSourceTitle)
                {
                    return;
                }
                _changeDataSourceTitle = value;
            }
        }

        public string ChangeDataSourceHeaderLabel
        {
            get
            {
                return _changeDataSourceHeaderLabel;
            }
            set
            {
                if (_showingDialog)
                {
                    throw new InvalidOperationException("The data connection dialog state cannot be programmatically modified when the dialog is visible.");
                }
                if (value == null)
                {
                    value = string.Empty;
                }
                if (value == _changeDataSourceHeaderLabel)
                {
                    return;
                }
                _changeDataSourceHeaderLabel = value;
            }
        }

        public ICollection<DataSource> DataSources
        {
            get
            {
                return _dataSources;
            }
        }

        public DataSource UnspecifiedDataSource
        {
            get
            {
                return _unspecifiedDataSource;
            }
        }

        public DataSource SelectedDataSource
        {
            get
            {
                if (_dataSources == null)
                {
                    return null;
                }
                switch (_dataSources.Count)
                {
                    case 0:
                        Debug.Assert(_selectedDataSource == null);
                        return null;
                    case 1:
                        // If there is only one data source, it must be selected
                        IEnumerator<DataSource> e = _dataSources.GetEnumerator();
                        e.MoveNext();
                        return e.Current;
                    default:
                        return _selectedDataSource;
                }
            }
            set
            {
                if (SelectedDataSource != value)
                {
                    if (_showingDialog)
                    {
                        throw new InvalidOperationException("The data connection dialog state cannot be programmatically modified when the dialog is visible.");
                    }
                    SetSelectedDataSource(value, false);
                }
            }
        }

        public DataProvider SelectedDataProvider
        {
            get
            {
                return GetSelectedDataProvider(SelectedDataSource);
            }
            set
            {
                if (SelectedDataProvider != value)
                {
                    if (SelectedDataSource == null)
                    {
                        throw new InvalidOperationException("No data source is currently selected.");
                    }
                    SetSelectedDataProvider(SelectedDataSource, value);
                }
            }
        }

        public DataProvider GetSelectedDataProvider(DataSource dataSource)
        {
            if (dataSource == null)
            {
                return null;
            }
            switch (dataSource.Providers.Count)
            {
                case 0:
                    return null;
                case 1:
                    // If there is only one data provider, it must be selected
                    IEnumerator<DataProvider> e = dataSource.Providers.GetEnumerator();
                    e.MoveNext();
                    return e.Current;
                default:
                    return (_dataProviderSelections.ContainsKey(dataSource)) ? _dataProviderSelections[dataSource] : dataSource.DefaultProvider;
            }
        }

        public void SetSelectedDataProvider(DataSource dataSource, DataProvider dataProvider)
        {
            if (GetSelectedDataProvider(dataSource) != dataProvider)
            {
                if (dataSource == null)
                {
                    throw new ArgumentNullException(nameof(dataSource));
                }
                if (_showingDialog)
                {
                    throw new InvalidOperationException("The data connection dialog state cannot be programmatically modified when the dialog is visible.");
                }
                SetSelectedDataProvider(dataSource, dataProvider, false);
            }
        }

        public bool SaveSelection
        {
            get
            {
                return _saveSelection;
            }
            set
            {
                _saveSelection = value;
            }
        }

        public string DisplayConnectionString
        {
            get
            {
                string s = null;
                if (ConnectionProperties != null)
                {
                    try
                    {
                        s = ConnectionProperties.ToDisplayString();
                    }
                    catch { }
                }
                return s ?? string.Empty;
            }
        }

        public string ConnectionString
        {
            get
            {
                string s = null;
                if (ConnectionProperties != null)
                {
                    try
                    {
                        s = ConnectionProperties.ToString();
                    }
                    catch { }
                }
                return s ?? string.Empty;
            }
            set
            {
                if (_showingDialog)
                {
                    throw new InvalidOperationException("The data connection dialog state cannot be programmatically modified when the dialog is visible.");
                }
                if (SelectedDataProvider == null)
                {
                    throw new InvalidOperationException("No data provider is currently selected.");
                }
                Debug.Assert(ConnectionProperties != null);
                ConnectionProperties?.Parse(value);
            }
        }

        public string AcceptButtonText
        {
            get
            {
                return acceptButton.Text;
            }
            set
            {
                acceptButton.Text = value;
            }
        }

        public event EventHandler VerifySettings;

        public event EventHandler<ContextHelpEventArgs> ContextHelpRequested;

        public event EventHandler<ThreadExceptionEventArgs> DialogException;

        internal UserControl ConnectionUIControl
        {
            get
            {
                if (SelectedDataProvider == null)
                {
                    return null;
                }
                if (!_connectionUIControlTable.ContainsKey(SelectedDataSource))
                {
                    _connectionUIControlTable[SelectedDataSource] = new Dictionary<DataProvider, IDataConnectionUIControl>();
                }
                if (!_connectionUIControlTable[SelectedDataSource].ContainsKey(SelectedDataProvider))
                {
                    IDataConnectionUIControl uiControl = null;
                    UserControl control = null;
                    try
                    {
                        if (SelectedDataSource == UnspecifiedDataSource)
                        {
                            uiControl = SelectedDataProvider.CreateConnectionUIControl(InitialConnectionString);
                        }
                        else
                        {
                            uiControl = SelectedDataProvider.CreateConnectionUIControl(SelectedDataSource, InitialConnectionString);
                        }
                        control = uiControl as UserControl;
                        if (control == null && uiControl is IContainerControl ctControl)
                        {
                            control = ctControl.ActiveControl as UserControl;
                        }
                    }
                    catch { }
                    if (uiControl == null || control == null)
                    {
                        uiControl = new PropertyGridUIControl();
                        control = uiControl as UserControl;
                    }
                    control.Location = Point.Empty;
                    control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    control.AutoSize = false;
                    try
                    {
                        uiControl.Initialize(ConnectionProperties);
                    }
                    catch { }
                    _connectionUIControlTable[SelectedDataSource][SelectedDataProvider] = uiControl;
                    components.Add(control); // so that it is disposed when the form is disposed
                }
                if (!(_connectionUIControlTable[SelectedDataSource][SelectedDataProvider] is UserControl result))
                {
                    result = (_connectionUIControlTable[SelectedDataSource][SelectedDataProvider] as IContainerControl).ActiveControl as UserControl;
                }
                return result;
            }
        }

        internal IDataConnectionProperties ConnectionProperties
        {
            get
            {
                if (SelectedDataProvider == null)
                {
                    return null;
                }
                if (!_connectionPropertiesTable.ContainsKey(SelectedDataSource))
                {
                    _connectionPropertiesTable[SelectedDataSource] = new Dictionary<DataProvider, IDataConnectionProperties>();
                }
                if (!_connectionPropertiesTable[SelectedDataSource].ContainsKey(SelectedDataProvider))
                {
                    IDataConnectionProperties properties = null;
                    if (SelectedDataSource == UnspecifiedDataSource)
                    {
                        properties = SelectedDataProvider.CreateConnectionProperties();
                    }
                    else
                    {
                        properties = SelectedDataProvider.CreateConnectionProperties(SelectedDataSource);
                    }
                    if (properties == null)
                    {
                        properties = new BasicConnectionProperties();
                    }
                    properties.PropertyChanged += ConfigureAcceptButton;
                    _connectionPropertiesTable[SelectedDataSource][SelectedDataProvider] = properties;
                }
                return _connectionPropertiesTable[SelectedDataSource][SelectedDataProvider];
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            if (!_showingDialog)
            {
                throw new NotSupportedException("You cannot use the Form.ShowDialog() method to show the data connection dialog.  Use DataConnectionDialog.Show() instead");
            }
            ConfigureDataSourceTextBox();
            ConfigureChangeDataSourceButton();
            ConfigureContainerControl();
            ConfigureAcceptButton(this, EventArgs.Empty);
            base.OnLoad(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // Set focus to the connection UI control (if any)
            ConnectionUIControl?.Focus();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);

            dataSourceTableLayoutPanel.Anchor &= ~AnchorStyles.Right;
            containerControl.Anchor &= ~AnchorStyles.Right & ~AnchorStyles.Bottom;
            advancedButton.Anchor |= AnchorStyles.Top | AnchorStyles.Left;
            advancedButton.Anchor &= ~AnchorStyles.Right & ~AnchorStyles.Bottom;
            separatorPanel.Anchor |= AnchorStyles.Top;
            separatorPanel.Anchor &= ~AnchorStyles.Right & ~AnchorStyles.Bottom;
            testConnectionButton.Anchor |= AnchorStyles.Top;
            testConnectionButton.Anchor &= ~AnchorStyles.Bottom;
            buttonsTableLayoutPanel.Anchor |= AnchorStyles.Top | AnchorStyles.Left;
            buttonsTableLayoutPanel.Anchor &= ~AnchorStyles.Right & ~AnchorStyles.Bottom;
            Size properSize = new Size(
                containerControl.Right +
                containerControl.Margin.Right +
                Padding.Right,
                buttonsTableLayoutPanel.Bottom +
                buttonsTableLayoutPanel.Margin.Bottom +
                Padding.Bottom);
            properSize = SizeFromClientSize(properSize);
            Size dsize = Size - properSize;
            MinimumSize -= dsize;
            Size -= dsize;
            buttonsTableLayoutPanel.Anchor |= AnchorStyles.Right | AnchorStyles.Bottom;
            buttonsTableLayoutPanel.Anchor &= ~AnchorStyles.Top & ~AnchorStyles.Left;
            testConnectionButton.Anchor |= AnchorStyles.Bottom;
            testConnectionButton.Anchor &= ~AnchorStyles.Top;
            separatorPanel.Anchor |= AnchorStyles.Right | AnchorStyles.Bottom;
            separatorPanel.Anchor &= ~AnchorStyles.Top;
            advancedButton.Anchor |= AnchorStyles.Right | AnchorStyles.Bottom;
            advancedButton.Anchor &= ~AnchorStyles.Top & ~AnchorStyles.Left;
            containerControl.Anchor |= AnchorStyles.Right | AnchorStyles.Bottom;
            dataSourceTableLayoutPanel.Anchor |= AnchorStyles.Right;
        }

        protected virtual void OnVerifySettings(EventArgs e)
        {
            VerifySettings?.Invoke(this, e);
        }

        protected internal virtual void OnContextHelpRequested(ContextHelpEventArgs e)
        {
            ContextHelpRequested?.Invoke(this, e);
            if (!e.Handled)
            {
                ShowError(null, "No help is available in this context.");
                e.Handled = true;
            }
        }

        protected override void OnHelpRequested(HelpEventArgs hevent)
        {
            // Get the active control
            Control activeControl = this;
            while (activeControl is ContainerControl containrControl &&
                containrControl != ConnectionUIControl &&
                containrControl.ActiveControl != null)
            {
                activeControl = containrControl.ActiveControl;
            }

            // Figure out the context
            DataConnectionDialogContext context = DataConnectionDialogContext.Main;
            if (activeControl == dataSourceTextBox)
            {
                context = DataConnectionDialogContext.MainDataSourceTextBox;
            }
            if (activeControl == changeDataSourceButton)
            {
                context = DataConnectionDialogContext.MainChangeDataSourceButton;
            }
            if (activeControl == ConnectionUIControl)
            {
                context = DataConnectionDialogContext.MainConnectionUIControl;
                if (ConnectionUIControl is SqlConnectionUIControl)
                {
                    context = DataConnectionDialogContext.MainSqlConnectionUIControl;
                }
                if (ConnectionUIControl is PropertyGridUIControl)
                {
                    context = DataConnectionDialogContext.MainGenericConnectionUIControl;
                }
            }
            if (activeControl == advancedButton)
            {
                context = DataConnectionDialogContext.MainAdvancedButton;
            }
            if (activeControl == testConnectionButton)
            {
                context = DataConnectionDialogContext.MainTestConnectionButton;
            }
            if (activeControl == acceptButton)
            {
                context = DataConnectionDialogContext.MainAcceptButton;
            }
            if (activeControl == cancelButton)
            {
                context = DataConnectionDialogContext.MainCancelButton;
            }

            // Call OnContextHelpRequested
            ContextHelpEventArgs e = new ContextHelpEventArgs(context, hevent.MousePos);
            OnContextHelpRequested(e);
            hevent.Handled = e.Handled;
            if (!e.Handled)
            {
                base.OnHelpRequested(hevent);
            }
        }

        protected virtual void OnDialogException(ThreadExceptionEventArgs e)
        {
            if (DialogException != null)
            {
                DialogException(this, e);
            }
            else
            {
                ShowError(null, e.Exception);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                try
                {
                    OnVerifySettings(EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    if (!(ex is ExternalException exex) || exex.ErrorCode != NativeMethods.DB_E_CANCELED)
                    {
                        ShowError(null, ex);
                    }
                    e.Cancel = true;
                }
            }

            base.OnFormClosing(e);
        }

        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        protected override void WndProc(ref Message m)
        {
            if (_translateHelpButton && HelpUtils.IsContextHelpMessage(ref m))
            {
                // Force the ? in the title bar to invoke the help topic
                HelpUtils.TranslateContextHelpMessage(this, ref m);
                base.DefWndProc(ref m); // pass to the active control
                return;
            }
            base.WndProc(ref m);
        }

        internal void SetSelectedDataSourceInternal(DataSource value)
        {
            SetSelectedDataSource(value, false);
        }

        internal void SetSelectedDataProviderInternal(DataSource dataSource, DataProvider value)
        {
            SetSelectedDataProvider(dataSource, value, false);
        }

        private void SetSelectedDataSource(DataSource value, bool noSingleItemCheck)
        {
            if (!noSingleItemCheck && _dataSources.Count == 1 && _selectedDataSource != value)
            {
                IEnumerator<DataSource> e = _dataSources.GetEnumerator();
                e.MoveNext();
                if (value != e.Current)
                {
                    throw new InvalidOperationException("The selected data source cannot be changed when there is only one data source available.");
                }
            }
            if (_selectedDataSource != value)
            {
                if (value != null)
                {
                    if (!_dataSources.Contains(value))
                    {
                        throw new InvalidOperationException("The data source was not found.");
                    }
                    _selectedDataSource = value;
                    switch (_selectedDataSource.Providers.Count)
                    {
                        case 0:
                            SetSelectedDataProvider(_selectedDataSource, null, noSingleItemCheck);
                            break;
                        case 1:
                            IEnumerator<DataProvider> e = _selectedDataSource.Providers.GetEnumerator();
                            e.MoveNext();
                            SetSelectedDataProvider(_selectedDataSource, e.Current, true);
                            break;
                        default:
                            DataProvider defaultProvider = _selectedDataSource.DefaultProvider;
                            if (_dataProviderSelections.ContainsKey(_selectedDataSource))
                            {
                                defaultProvider = _dataProviderSelections[_selectedDataSource];
                            }
                            SetSelectedDataProvider(_selectedDataSource, defaultProvider, noSingleItemCheck);
                            break;
                    }
                }
                else
                {
                    _selectedDataSource = null;
                }

                if (_showingDialog)
                {
                    ConfigureDataSourceTextBox();
                }
            }
        }

        private void SetSelectedDataProvider(DataSource dataSource, DataProvider value, bool noSingleItemCheck)
        {
            Debug.Assert(dataSource != null);
            if (!noSingleItemCheck && dataSource.Providers.Count == 1 &&
                ((_dataProviderSelections.ContainsKey(dataSource) && _dataProviderSelections[dataSource] != value) ||
                (!_dataProviderSelections.ContainsKey(dataSource) && value != null)))
            {
                IEnumerator<DataProvider> e = dataSource.Providers.GetEnumerator();
                e.MoveNext();
                if (value != e.Current)
                {
                    throw new InvalidOperationException("The selected data provider cannot be changed when there is only one data provider available.");
                }
            }
            if ((_dataProviderSelections.ContainsKey(dataSource) && _dataProviderSelections[dataSource] != value) ||
                (!_dataProviderSelections.ContainsKey(dataSource) && value != null))
            {
                if (value != null)
                {
                    if (!dataSource.Providers.Contains(value))
                    {
                        throw new InvalidOperationException("The selected data source has no association with this data provider.");
                    }
                    _dataProviderSelections[dataSource] = value;
                }
                else if (_dataProviderSelections.ContainsKey(dataSource))
                {
                    _dataProviderSelections.Remove(dataSource);
                }

                if (_showingDialog)
                {
                    ConfigureContainerControl();
                }
            }
        }

        private void ConfigureDataSourceTextBox()
        {
            if (SelectedDataSource != null)
            {
                if (SelectedDataSource == UnspecifiedDataSource)
                {
                    if (SelectedDataProvider != null)
                    {
                        dataSourceTextBox.Text = SelectedDataProvider.DisplayName;
                    }
                    else
                    {
                        dataSourceTextBox.Text = null;
                    }
                    dataProviderToolTip.SetToolTip(dataSourceTextBox, null);
                }
                else
                {
                    dataSourceTextBox.Text = SelectedDataSource.DisplayName;
                    if (SelectedDataProvider != null)
                    {
                        if (SelectedDataProvider.ShortDisplayName != null)
                        {
                            dataSourceTextBox.Text = String.Format("{0} ({1})", dataSourceTextBox.Text, SelectedDataProvider.ShortDisplayName);
                        }
                        dataProviderToolTip.SetToolTip(dataSourceTextBox, SelectedDataProvider.DisplayName);
                    }
                    else
                    {
                        dataProviderToolTip.SetToolTip(dataSourceTextBox, null);
                    }
                }
            }
            else
            {
                dataSourceTextBox.Text = null;
                dataProviderToolTip.SetToolTip(dataSourceTextBox, null);
            }
            dataSourceTextBox.Select(0, 0);
        }

        private void ConfigureChangeDataSourceButton()
        {
            changeDataSourceButton.Enabled = (DataSources.Count > 1 || SelectedDataSource.Providers.Count > 1);
        }

        private void ChangeDataSource(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Ignore;
            Close();
        }

        private void ConfigureContainerControl()
        {
            if (containerControl.Controls.Count == 0)
            {
                _initialContainerControlSize = containerControl.Size;
            }
            if ((containerControl.Controls.Count == 0 && ConnectionUIControl != null) ||
                (containerControl.Controls.Count > 0 && ConnectionUIControl != containerControl.Controls[0]))
            {
                containerControl.Controls.Clear();
                if (ConnectionUIControl?.PreferredSize.Width > 0 && ConnectionUIControl.PreferredSize.Height > 0)
                {
                    // Add it to the container control
                    containerControl.Controls.Add(ConnectionUIControl);

                    // Size dialog appropriately
                    MinimumSize = Size.Empty;
                    Size currentSize = containerControl.Size;
                    containerControl.Size = _initialContainerControlSize;
                    Size preferredSize = ConnectionUIControl.PreferredSize;
                    containerControl.Size = currentSize;
                    int minimumWidth =
                        _initialContainerControlSize.Width - (Width - ClientSize.Width) -
                        Padding.Left -
                        containerControl.Margin.Left -
                        containerControl.Margin.Right -
                        Padding.Right;
                    minimumWidth = Math.Max(minimumWidth,
                        testConnectionButton.Width +
                        testConnectionButton.Margin.Right +
                        buttonsTableLayoutPanel.Margin.Left +
                        buttonsTableLayoutPanel.Width +
                        buttonsTableLayoutPanel.Margin.Right);
                    preferredSize.Width = Math.Max(preferredSize.Width, minimumWidth);
                    Size += preferredSize - containerControl.Size;
                    if (containerControl.Bottom == advancedButton.Top)
                    {
                        containerControl.Margin = new Padding(
                                containerControl.Margin.Left,
                                dataSourceTableLayoutPanel.Margin.Bottom,
                                containerControl.Margin.Right,
                                advancedButton.Margin.Top);
                        Height += containerControl.Margin.Bottom + advancedButton.Margin.Top;
                        containerControl.Height -= containerControl.Margin.Bottom + advancedButton.Margin.Top;
                    }
                    Size maximumSize =
                        SystemInformation.PrimaryMonitorMaximizedWindowSize -
                        SystemInformation.FrameBorderSize -
                        SystemInformation.FrameBorderSize;
                    if (Width > maximumSize.Width)
                    {
                        Width = maximumSize.Width;
                        if (Height + SystemInformation.HorizontalScrollBarHeight <= maximumSize.Height)
                        {
                            Height += SystemInformation.HorizontalScrollBarHeight;
                        }
                    }
                    if (Height > maximumSize.Height)
                    {
                        if (Width + SystemInformation.VerticalScrollBarWidth <= maximumSize.Width)
                        {
                            Width += SystemInformation.VerticalScrollBarWidth;
                        }
                        Height = maximumSize.Height;
                    }
                    MinimumSize = Size;

                    // The advanced button is only enabled for actual UI controls
                    advancedButton.Enabled = !(ConnectionUIControl is PropertyGridUIControl);
                }
                else
                {
                    // Size dialog appropriately
                    MinimumSize = Size.Empty;
                    if (containerControl.Bottom != advancedButton.Top)
                    {
                        containerControl.Height += containerControl.Margin.Bottom + advancedButton.Margin.Top;
                        Height -= containerControl.Margin.Bottom + advancedButton.Margin.Top;
                        containerControl.Margin = new Padding(
                                containerControl.Margin.Left,
                                0,
                                containerControl.Margin.Right,
                                0);
                    }
                    Size -= containerControl.Size - new Size(300, 0);
                    MinimumSize = Size;

                    // The advanced button is always enabled for no UI control
                    advancedButton.Enabled = true;
                }
            }
            if (ConnectionUIControl != null)
            {
                // Load properties into the connection UI control
                try
                {
                    _connectionUIControlTable[SelectedDataSource][SelectedDataProvider].LoadProperties();
                }
                catch { }
            }
        }
        private Size _initialContainerControlSize;

        private void SetConnectionUIControlDockStyle(object sender, EventArgs e)
        {
            if (containerControl.Controls.Count > 0)
            {
                DockStyle dockStyle = DockStyle.None;
                Size containerControlSize = containerControl.Size;
                Size connectionUIControlMinimumSize = containerControl.Controls[0].MinimumSize;
                if (containerControlSize.Width >= connectionUIControlMinimumSize.Width &&
                    containerControlSize.Height >= connectionUIControlMinimumSize.Height)
                {
                    dockStyle = DockStyle.Fill;
                }
                if (containerControlSize.Width - SystemInformation.VerticalScrollBarWidth >= connectionUIControlMinimumSize.Width &&
                    containerControlSize.Height < connectionUIControlMinimumSize.Height)
                {
                    dockStyle = DockStyle.Top;
                }
                if (containerControlSize.Width < connectionUIControlMinimumSize.Width &&
                    containerControlSize.Height - SystemInformation.HorizontalScrollBarHeight >= connectionUIControlMinimumSize.Height)
                {
                    dockStyle = DockStyle.Left;
                }
                containerControl.Controls[0].Dock = dockStyle;
            }
        }

        private void ShowAdvanced(object sender, EventArgs e)
        {
            DataConnectionAdvancedDialog advancedDialog = new DataConnectionAdvancedDialog(ConnectionProperties, this);
            DialogResult dialogResult = DialogResult.None;
            try
            {
                Container?.Add(advancedDialog);
                dialogResult = advancedDialog.ShowDialog(this);
            }
            finally
            {
                Container?.Remove(advancedDialog);
                advancedDialog.Dispose();
            }
            if (dialogResult == DialogResult.OK && ConnectionUIControl != null)
            {
                try
                {
                    _connectionUIControlTable[SelectedDataSource][SelectedDataProvider].LoadProperties();
                }
                catch { }
                ConfigureAcceptButton(this, EventArgs.Empty);
            }
        }

        private void TestConnection(object sender, EventArgs e)
        {
            Cursor currentCursor = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            string username = null;
            try
            {
                username = ConnectionProperties.Test();
                if (ConnectionUIControl is ISuccess)
                {
                    // fill database combobox here
                    (ConnectionUIControl as ISuccess).TestButtonSuccess(username);
                }
            }
            catch (Exception ex)
            {
                Cursor.Current = currentCursor;
                ShowError("Test results.", ex);
                return;
            }
            Cursor.Current = currentCursor;
            ShowMessage(string.IsNullOrEmpty(username) ? "Test results" : $"Connected as: {username}", "Test connection succeeded.");
        }

        private void ConfigureAcceptButton(object sender, EventArgs e)
        {
            try
            {
                acceptButton.Enabled = ConnectionProperties?.IsComplete == true;
            }
            catch
            {
                acceptButton.Enabled = true;
            }
        }

        private void HandleAccept(object sender, EventArgs e)
        {
            acceptButton.Focus(); // ensures connection properties are up to date
        }

        private void PaintSeparator(object sender, PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            Pen dark = new Pen(ControlPaint.Dark(BackColor, 0f));
            Pen light = new Pen(ControlPaint.Light(BackColor, 1f));
            int width = separatorPanel.Width;

            graphics.DrawLine(dark, 0, 0, width, 0);
            graphics.DrawLine(light, 0, 1, width, 1);
        }

        private void HandleDialogException(object sender, ThreadExceptionEventArgs e)
        {
            OnDialogException(e);
        }

        private void ShowMessage(string title, string message)
        {
            if (GetService(typeof(IUIService)) is IUIService uiService)
            {
                uiService.ShowMessage(message);
            }
            else
            {
                RTLAwareMessageBox.Show(title, message, MessageBoxIcon.Information);
            }
        }

        private void ShowError(string title, Exception ex)
        {
            if (GetService(typeof(IUIService)) is IUIService uiService)
            {
                uiService.ShowError(ex);
            }
            else
            {
                RTLAwareMessageBox.Show(title, ex.Message, MessageBoxIcon.Exclamation);
            }
        }

        private void ShowError(string title, string message)
        {
            if (GetService(typeof(IUIService)) is IUIService uiService)
            {
                uiService.ShowError(message);
            }
            else
            {
                RTLAwareMessageBox.Show(title, message, MessageBoxIcon.Exclamation);
            }
        }

        private class DataSourceCollection : ICollection<DataSource>
        {
            private readonly List<DataSource> _list = new List<DataSource>();
            private readonly DataConnectionDialog _dialog;

            public DataSourceCollection(DataConnectionDialog dialog)
            {
                Debug.Assert(dialog != null);

                _dialog = dialog;
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
                    return _dialog._showingDialog;
                }
            }

            public void Add(DataSource item)
            {
                if (item == null)
                {
                    throw new ArgumentNullException(nameof(item));
                }
                if (_dialog._showingDialog)
                {
                    throw new InvalidOperationException("The data connection dialog state cannot be programmatically modified when the dialog is visible.");
                }
                if (!_list.Contains(item))
                {
                    _list.Add(item);
                }
            }

            public bool Contains(DataSource item)
            {
                return _list.Contains(item);
            }

            public bool Remove(DataSource item)
            {
                if (_dialog._showingDialog)
                {
                    throw new InvalidOperationException("The data connection dialog state cannot be programmatically modified when the dialog is visible.");
                }
                bool result = _list.Remove(item);
                if (item == _dialog.SelectedDataSource)
                {
                    _dialog.SetSelectedDataSource(null, true);
                }
                return result;
            }

            public void Clear()
            {
                if (_dialog._showingDialog)
                {
                    throw new InvalidOperationException("The data connection dialog state cannot be programmatically modified when the dialog is visible.");
                }
                _list.Clear();
                _dialog.SetSelectedDataSource(null, true);
            }

            public void CopyTo(DataSource[] array, int arrayIndex)
            {
                _list.CopyTo(array, arrayIndex);
            }

            public IEnumerator<DataSource> GetEnumerator()
            {
                return _list.GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return _list.GetEnumerator();
            }
        }

        private class PropertyGridUIControl : UserControl, IDataConnectionUIControl
        {
            private IDataConnectionProperties connectionProperties;
            private readonly DataConnectionAdvancedDialog.SpecializedPropertyGrid propertyGrid;

            public PropertyGridUIControl()
            {
                propertyGrid = new DataConnectionAdvancedDialog.SpecializedPropertyGrid();
                SuspendLayout();
                // 
                // propertyGrid
                // 
                propertyGrid.CommandsVisibleIfAvailable = true;
                propertyGrid.Dock = DockStyle.Fill;
                propertyGrid.Location = Point.Empty;
                propertyGrid.Margin = new Padding(0);
                propertyGrid.Name = "propertyGrid";
                propertyGrid.TabIndex = 0;
                // 
                // DataConnectionDialog
                // 
                Controls.Add(propertyGrid);
                Name = "PropertyGridUIControl";
                ResumeLayout(false);
                PerformLayout();
            }

            public void Initialize(IDataConnectionProperties dataConnectionProperties)
            {
                connectionProperties = dataConnectionProperties;
            }

            public void LoadProperties()
            {
                propertyGrid.SelectedObject = connectionProperties;
            }

            public override Size GetPreferredSize(Size proposedSize)
            {
                return propertyGrid.GetPreferredSize(proposedSize);
            }
        }

        private class BasicConnectionProperties : IDataConnectionProperties
        {
            public void Reset()
            {
                _s = string.Empty;
            }

            public void Parse(string s)
            {
                _s = s;
                PropertyChanged?.Invoke(this, EventArgs.Empty);
            }

            [Browsable(false)]
            public bool IsExtensible
            {
                get
                {
                    return false;
                }
            }

            public void Add(string propertyName)
            {
                throw new NotImplementedException();
            }

            public bool Contains(string propertyName)
            {
                return propertyName == "ConnectionString";
            }

            public object this[string propertyName]
            {
                get
                {
                    if (propertyName == "ConnectionString")
                    {
                        return ConnectionString;
                    }
                    else
                    {
                        return null;
                    }
                }
                set
                {
                    if (propertyName == "ConnectionString")
                    {
                        ConnectionString = value as string;
                    }
                }
            }

            public void Remove(string propertyName)
            {
                throw new NotImplementedException();
            }

            public event EventHandler PropertyChanged;

            public void Reset(string propertyName)
            {
                Debug.Assert(propertyName == "ConnectionString");
                _s = string.Empty;
            }

            [Browsable(false)]
            public bool IsComplete
            {
                get
                {
                    return true;
                }
            }

            public string ConnectionString
            {
                get
                {
                    return ToFullString();
                }
                set
                {
                    Parse(value);
                }
            }

            public string Test()
            {
                return string.Empty;
            }

            public string ToFullString()
            {
                return _s;
            }

            public string ToDisplayString()
            {
                return _s;
            }

            private string _s;
        }

        private bool _showingDialog;
        private Label _headerLabel;
        private bool _translateHelpButton = true;
        private string _chooseDataSourceTitle;
        private string _chooseDataSourceHeaderLabel = string.Empty;
        private string _chooseDataSourceAcceptText;
        private string _changeDataSourceTitle;
        private string _changeDataSourceHeaderLabel = string.Empty;
        private readonly ICollection<DataSource> _dataSources;
        private readonly DataSource _unspecifiedDataSource = DataSource.CreateUnspecified();
        private DataSource _selectedDataSource;
        private readonly IDictionary<DataSource, DataProvider> _dataProviderSelections = new Dictionary<DataSource, DataProvider>();
        private bool _saveSelection = true;
        private readonly IDictionary<DataSource, IDictionary<DataProvider, IDataConnectionUIControl>> _connectionUIControlTable = new Dictionary<DataSource, IDictionary<DataProvider, IDataConnectionUIControl>>();
        private readonly IDictionary<DataSource, IDictionary<DataProvider, IDataConnectionProperties>> _connectionPropertiesTable = new Dictionary<DataSource, IDictionary<DataProvider, IDataConnectionProperties>>();
    }
}
