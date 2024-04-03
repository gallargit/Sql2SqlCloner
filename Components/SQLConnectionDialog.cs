// SQLConnectionDialog: connect to SQL Server and return a connection string

using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sql2SqlCloner.Components
{
    public class SQLConnectionDialog : Form
    {
        private readonly string[] controlsToCreate = new[] { "txtServer_Name", "lstAuthentication", "txtUser_Name", "txtPassword", "cboDatabase_Name", "chkTrust_Server_Certificate" };

        public SQLConnectionDialog(Icon icon = null)
        {
            InitializeComponent();
            if (icon != null)
            {
                Icon = icon;
            }
            Text = "Connection parameters";
            var current = 0;
            var lstControls = new List<Control>();
            foreach (var currObject in controlsToCreate)
            {
                var objectPrefix = currObject.Substring(0, 3);
                var objectName = currObject.Substring(3);
                var objLabel = new Label
                {
                    AutoSize = true,
                    Name = $"lbl{objectName}",
                    Text = objectName.Replace("_", " ")
                };
                objLabel.SetBounds(9, 20 + (current * 30), 200, 13);
                lstControls.Add(objLabel);
                Control objControl;
                if (objectPrefix == "cbo")
                {
                    objControl = new ComboBox
                    {
                        AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                        DropDownStyle = ComboBoxStyle.DropDown
                    };
                }
                else if (currObject == "lstAuthentication")
                {
                    var objCombo = new ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList
                    };
                    objCombo.Items.Add("Windows");
                    objCombo.Items.Add("SQL Server");
                    objCombo.Items.Add("Azure AD");
                    objCombo.SelectedIndex = 0;
                    objCombo.SelectedIndexChanged += (s, e) =>
                    {
                        Controls["User_Name"].Enabled = objCombo.SelectedIndex != 0;
                        Controls["Password"].Enabled = objCombo.SelectedIndex == 1;
                    };
                    objCombo.SelectedIndexChanged += (s, e) => TryFillDatabases(false);
                    objControl = objCombo;
                }
                else if (objectPrefix == "chk")
                {
                    objControl = new CheckBox
                    {
                        Checked = true
                    };
                    objControl.SetBounds(130, 20 + (current * 30), 14, 14);
                }
                else
                {
                    objControl = new TextBox();
                    if (objectName.Contains("Password"))
                    {
                        (objControl as TextBox).PasswordChar = '*';
                    }
                    objControl.Anchor |= AnchorStyles.Right;
                    objControl.TextChanged += (s, e) => TryFillDatabases(false);
                }
                if (objControl.Left == 0)
                {
                    objControl.SetBounds(100, 18 + (current * 30), 204, 13);
                }
                objControl.Name = objectName;
                lstControls.Add(objControl);
                current++;
            }
            var buttonTest = new Button
            {
                Name = "Test",
                Text = "Test Connection"
            };
            buttonTest.Click += (s, e) => TestConnectionString();
            buttonTest.SetBounds(10, lstControls.Last().Top + 36, 120, 23);
            var buttonOk = new Button
            {
                Name = nameof(DialogResult.OK),
                Text = nameof(DialogResult.OK),
                DialogResult = DialogResult.OK
            };
            buttonOk.SetBounds(180, buttonTest.Top, Convert.ToInt32(buttonTest.Width / 2.3), buttonTest.Height);
            AcceptButton = buttonOk;
            var buttonCancel = new Button
            {
                Name = nameof(DialogResult.Cancel),
                Text = nameof(DialogResult.Cancel),
                DialogResult = DialogResult.Cancel
            };
            buttonCancel.SetBounds(242, buttonTest.Top, buttonTest.Width / 2, buttonTest.Height);
            CancelButton = buttonCancel;
            lstControls.Add(buttonTest);
            lstControls.Add(buttonOk);
            lstControls.Add(buttonCancel);
            ClientSize = new Size(320, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = MaximizeBox = false;
            Controls.AddRange(lstControls.ToArray());
            Controls["User_Name"].Enabled = false;
            Controls["Password"].Enabled = false;
        }

        private bool IsDataFilledOK()
        {
            return !string.IsNullOrEmpty(Controls["Server_Name"].Text) && (
                (Controls["Authentication"] as ComboBox).SelectedIndex == 0 ||
                ((Controls["Authentication"] as ComboBox).SelectedIndex == 1 && !string.IsNullOrEmpty(Controls["User_Name"].Text) && !string.IsNullOrEmpty(Controls["Password"].Text))
            );
        }

        private void TryFillDatabases(bool force)
        {
            if (Visible)
            {
                var cboDatabaseName = Controls["Database_Name"] as ComboBox;
                Invoke(new Action(() =>
                {
                    cboDatabaseName.Items.Clear();
                    cboDatabaseName.DropDownHeight = 106;
                    cboDatabaseName.DropDownWidth = 204;
                }));
                if (force || IsDataFilledOK())
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            var connectionString = "";
                            Invoke(new Action(() => connectionString = GetConnectionString()));
                            var connectionStringItems = connectionString.Split(';').ToList();
                            connectionStringItems = connectionStringItems.Where(c => c.IndexOf("initial catalog", StringComparison.OrdinalIgnoreCase) < 0).ToList();
                            connectionString = string.Join(";", connectionStringItems);
                            if (!string.IsNullOrEmpty(connectionString))
                            {
                                var sourceConnection = new ServerConnection
                                {
                                    ConnectionString = $"{connectionString};Connection Timeout=1"
                                };
                                var server = new Server(sourceConnection);
                                server.ConnectionContext.Connect();
                                var dblist = new List<string>();
                                foreach (Database db in server.Databases)
                                {
                                    dblist.Add(db.Name);
                                }
                                Invoke(new Action(() => FillDatabaseCombo(dblist)));
                            }
                        }
                        catch { }
                    });
                }
            }
        }

        private void FillDatabaseCombo(IList<string> dblist)
        {
            var cboDatabaseName = Controls["Database_Name"] as ComboBox;
            cboDatabaseName.DropDownHeight = 106;
            cboDatabaseName.DropDownWidth = 204;
            var width = cboDatabaseName.DropDownWidth;
            foreach (var db in dblist)
            {
                var newWidth = (int)cboDatabaseName.CreateGraphics().MeasureString(db, cboDatabaseName.Font).Width;
                if (width < newWidth)
                {
                    width = newWidth;
                }
            }
            cboDatabaseName.Items.Clear();
            cboDatabaseName.Items.AddRange(dblist.OrderBy(db => db).ToArray());
            cboDatabaseName.DropDownWidth = width +
                ((cboDatabaseName.Items.Count > cboDatabaseName.MaxDropDownItems) ? SystemInformation.VerticalScrollBarWidth : 0);
            //prevent dropdown to look like it's focused
            cboDatabaseName.SelectedText = cboDatabaseName.SelectedText;
        }

        public DialogResult ShowDialog(ref string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                var fields = DecodeConnectionString(value);
                if (fields.ContainsKey("Data Source"))
                {
                    Controls["Server_Name"].Text = fields["Data Source"];
                }
                if (fields.ContainsKey("Initial Catalog"))
                {
                    Controls["Database_Name"].Text = fields["Initial Catalog"];
                }
                if (fields.ContainsKey("TrustServerCertificate"))
                {
                    (Controls["Trust_Server_Certificate"] as CheckBox).Checked = string.Equals(fields["TrustServerCertificate"], "true", StringComparison.OrdinalIgnoreCase);
                }
                if (fields.ContainsKey("Integrated Security") && string.Equals(fields["Integrated Security"], "true", StringComparison.OrdinalIgnoreCase))
                {
                    (Controls["Authentication"] as ComboBox).SelectedIndex = 0;
                }
                else
                {
                    if (fields.ContainsKey("Authentication") && fields["Authentication"] == "ActiveDirectoryInteractive")
                    {
                        (Controls["Authentication"] as ComboBox).SelectedIndex = 2;
                    }
                    else
                    {
                        (Controls["Authentication"] as ComboBox).SelectedIndex = 1;
                    }
                    if (fields.ContainsKey("User ID"))
                    {
                        Controls["User_Name"].Text = fields["User ID"];
                    }
                    if (fields.ContainsKey("Password"))
                    {
                        Controls["Password"].Text = fields["Password"];
                    }
                }
            }
            var dialogResult = ShowDialog();
            if (dialogResult == DialogResult.OK)
            {
                value = GetConnectionString();
            }
            return dialogResult;
        }

        private string GetConnectionString()
        {
            var builder = new SqlConnectionStringBuilder();
            var authentication = (Controls["Authentication"] as ComboBox).SelectedIndex;

            builder["Data Source"] = Controls["Server_Name"].Text;
            builder["Initial Catalog"] = Controls["Database_Name"].Text;
            builder["TrustServerCertificate"] = (Controls["Trust_Server_Certificate"] as CheckBox).Checked;
            if (authentication == 0) //Windows
            {
                builder["Integrated Security"] = true;
            }
            if (authentication == 1) //SQL Server
            {
                builder["User ID"] = Controls["User_Name"].Text;
                builder["Password"] = Controls["Password"].Text;
            }
            if (authentication == 2) //Azure AD
            {
                builder["Authentication"] = "Active Directory Interactive";
                builder["User ID"] = Controls["User_Name"].Text;
                builder["Encrypt"] = "True"; // must be string, not boolean
            }
            return builder.ConnectionString;
        }

        private Dictionary<string, string> DecodeConnectionString(string connectionString)
        {
            var result = new Dictionary<string, string>();
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                var defaultBuilder = new SqlConnectionStringBuilder();
                foreach (var currentkey in builder.Keys)
                {
                    var key = currentkey.ToString();
                    if (builder[key].ToString() != defaultBuilder[key].ToString())
                    {
                        result[key] = builder[key].ToString();
                    }
                }
            }
            catch { }
            return result;
        }

        private bool TestConnectionString()
        {
            bool? result = null;
            var enabledControls = Controls.OfType<Control>().Where(c => c.Enabled).ToList();
            var originalText = Controls["Test"].Text;
            try
            {
                Controls["Test"].Text = "Please Wait";
                enabledControls.ForEach(c => c.Enabled = false);
                var authSelectedIndex = (Controls["Authentication"] as ComboBox).SelectedIndex;
                var cboDatabaseName = Controls["Database_Name"] as ComboBox;
                var realDatabaseName = cboDatabaseName.Text;
                var serverConnection = new ServerConnection(new SqlConnection(GetConnectionString()));
                serverConnection.SqlConnectionObject.Open();
                var serverInstance = serverConnection.ServerInstance;
                try
                {
                    //try to get the proper case database name
                    var server = new Server(serverConnection);
                    foreach (Database database in server.Databases)
                    {
                        if (string.Equals(database.Name, realDatabaseName, StringComparison.OrdinalIgnoreCase))
                        {
                            realDatabaseName = database.Name;
                            break;
                        }
                    }
                    //disconnect server
                    serverConnection.SqlConnectionObject.Close();
                    server = null;
                    serverConnection = null;
                    if (cboDatabaseName.Items.Count == 0 || realDatabaseName?.Length == 0)
                    {
                        TryFillDatabases(true);
                    }
                    if (realDatabaseName?.Length == 0)
                    {
                        MessageBox.Show("The connection was successful but no database was selected", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        result = false;
                    }
                }
                catch { }
                if (result == null)
                {
                    cboDatabaseName.Text = realDatabaseName;
                    var currentUserName = Controls["User_Name"].Text;
                    if (authSelectedIndex == 0)
                    {
                        currentUserName = SystemInformation.UserName;
                    }
                    MessageBox.Show($"Success connecting to: {serverInstance}/{realDatabaseName}", $"Connected as: {currentUserName}", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    result = true;
                }
            }
            catch (Exception ex)
            {
                var errorMessage = ex.Message;
                var innerEx = ex.InnerException;
                while (innerEx != null)
                {
                    errorMessage += Environment.NewLine + innerEx.Message;
                    innerEx = innerEx.InnerException;
                }
                MessageBox.Show($"Error connecting to {Controls["Server_Name"].Text}: {errorMessage}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            enabledControls.ForEach(c => c.Enabled = true);
            Controls["Test"].Text = originalText;
            return result ?? false;
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            Name = "SQLConnectionDialog";
            Shown += SQLConnectionDialog_Shown;
            ResumeLayout(false);
        }

        private void SQLConnectionDialog_Shown(object sender, EventArgs e)
        {
            TryFillDatabases(false);
        }
    }
}
