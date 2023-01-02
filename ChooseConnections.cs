using Microsoft.Data.ConnectionUI;
using Sql2SqlCloner.Core.DataTransfer;
using Sql2SqlCloner.Core.SchemaTransfer;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Common;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sql2SqlCloner
{
    public partial class ChooseConnections : Form
    {
        private string strtskSource, strtskDestination, sourceConnection, destinationConnection;
        private bool EnablePreload = ConfigurationManager.AppSettings["EnablePreload"]?.ToString().ToLower() == "true";
        private bool AutoRun;
        private Task<SqlSchemaTransfer> tskPreload;
        private readonly CancellationTokenSource tokenSource = new CancellationTokenSource();
        private CancellationToken cancelToken;

        public ChooseConnections()
        {
            InitializeComponent();
            SqlServerTypes.Utilities.LoadNativeAssemblies(AppDomain.CurrentDomain.BaseDirectory);
            AutoRun = string.Equals(ConfigurationManager.AppSettings["Autorun"], "true", StringComparison.InvariantCultureIgnoreCase);
        }

        private string GetConnection(string conn)
        {
            if (string.IsNullOrWhiteSpace(conn) ||
                !string.Equals(ConfigurationManager.AppSettings["CensorPasswords"], "true", StringComparison.InvariantCultureIgnoreCase))
            {
                return conn;
            }

            var sb = new StringBuilder();
            foreach (var s in conn.Split(';'))
            {
                if (sb.Length > 0)
                {
                    sb.Append(";");
                }
                if (s.StartsWith("password=", StringComparison.InvariantCultureIgnoreCase))
                {
                    sb.Append(s, 0, 9);
                    sb.Append("***");
                }
                else
                {
                    sb.Append(s);
                }
            }
            return sb.ToString();
        }

        private string GetConnectionString(string connectionString)
        {
            string tempconnectionString;
            var trustServerCertificate = "";
            try
            {
                var builder = new DbConnectionStringBuilder
                {
                    ConnectionString = connectionString
                };
                //"Trust Server Certificate" is not supported by Connection Dialog
                if (builder.ContainsKey("Trust Server Certificate"))
                {
                    trustServerCertificate = ";Trust Server Certificate=" + builder["Trust Server Certificate"];
                    builder.Remove("Trust Server Certificate");
                }
                tempconnectionString = builder.ConnectionString;
            }
            catch
            {
                tempconnectionString = null; //bad connection string
            }

            string conn = null;
            using (var dialog = new DataConnectionDialog())
            {
                dialog.DataSources.Add(DataSource.SqlDataSource);
                if (!string.IsNullOrEmpty(tempconnectionString))
                {
                    dialog.ConnectionString = tempconnectionString;
                }

                if (DataConnectionDialog.Show(dialog) == DialogResult.OK)
                {
                    conn = dialog.ConnectionString + trustServerCertificate;
                }
            }
            return conn;
        }

        private void SetFormControls(bool active)
        {
            foreach (Control c in Controls)
            {
                c.Enabled = active;
            }
            lblPleaseWait.Visible = lblPleaseWait.Enabled = !active;
        }

        private void ChooseConnections_Load(object sender, EventArgs e)
        {
            Icon = Icon.FromHandle(Properties.Resources.Clone.Handle);
            sourceConnection = Properties.Settings.Default.SourceServer;
            txtSource.Text = GetConnection(sourceConnection);
            destinationConnection = Properties.Settings.Default.DestinationServer;
            txtDestination.Text = GetConnection(destinationConnection);
            isSchema.Checked = Properties.Settings.Default.CopySchema;
            isData.Checked = Properties.Settings.Default.CopyData;
            trustServerCertificates.Checked = Properties.Settings.Default.AlwaysTrustServerCertificates;

            if (sender != null && e != null && AutoRun)
            {
                btnNext_Click(sender, e);
            }
            else
            {
                //start preloading the saved databases' info
                if (EnablePreload && !string.IsNullOrEmpty(sourceConnection) && !string.IsNullOrEmpty(destinationConnection))
                {
                    EnablePreload = false;
                    strtskSource = strtskDestination = destinationConnection;
                    tskPreload = Task.Run(() => new SqlSchemaTransfer(Properties.Settings.Default.SourceServer, Properties.Settings.Default.DestinationServer, cancelToken), cancelToken);
                }
            }
        }

        private void btnDestination_Click(object sender, EventArgs e)
        {
            var newcx = GetConnectionString(destinationConnection);
            if (!string.IsNullOrEmpty(newcx))
            {
                destinationConnection = newcx;
                txtDestination.Text = GetConnection(destinationConnection);
            }
        }

        private void btnSource_Click(object sender, EventArgs e)
        {
            var newcx = GetConnectionString(sourceConnection);
            if (!string.IsNullOrEmpty(newcx))
            {
                sourceConnection = newcx;
                txtSource.Text = GetConnection(sourceConnection);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            //Application.Exit() cannot be called because the SQL Connection dialog prevents
            //the application from exiting if the Cancel button is clicked
            Environment.Exit(0);
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            var autoRun = ModifierKeys.HasFlag(Keys.Shift) || AutoRun;
            //global autorun should work only the first time
            AutoRun = false;
            var initialTime = DateTime.Now;
            if (!isData.Checked && !isSchema.Checked)
            {
                MessageBox.Show("Please tick what to copy (schema/data)");
                return;
            }
            if (string.IsNullOrEmpty(txtSource.Text))
            {
                MessageBox.Show("Please select source connection");
                txtSource.Focus();
                return;
            }
            if (string.IsNullOrEmpty(txtDestination.Text))
            {
                MessageBox.Show("Please select destination connection");
                txtDestination.Focus();
                return;
            }
            if (trustServerCertificates.Checked)
            {
                if (sourceConnection.IndexOf("trust server certificate", StringComparison.InvariantCultureIgnoreCase) < 0)
                {
                    if (!sourceConnection.EndsWith(";"))
                    {
                        sourceConnection += ";";
                    }

                    sourceConnection += "Trust Server Certificate=True";
                }
                if (destinationConnection.IndexOf("trust server certificate", StringComparison.InvariantCultureIgnoreCase) < 0)
                {
                    if (!destinationConnection.EndsWith(";"))
                    {
                        destinationConnection += ";";
                    }

                    destinationConnection += "Trust Server Certificate=True";
                }
            }
            Properties.Settings.Default.SourceServer = sourceConnection;
            Properties.Settings.Default.DestinationServer = destinationConnection;
            Properties.Settings.Default.CopySchema = isSchema.Checked;
            Properties.Settings.Default.CopyData = isData.Checked;
            Properties.Settings.Default.AlwaysTrustServerCertificates = trustServerCertificates.Checked;
            Properties.Settings.Default.Save();

            var firststepok = false;
            var successConnecting = true;
            SqlSchemaTransfer schematransfer = null;
            List<SqlSchemaObject> tablesToCopy = null;
            try
            {
                SetFormControls(false);
                Application.DoEvents();
                if (tskPreload != null && strtskSource == sourceConnection && strtskDestination == destinationConnection)
                {
                    tskPreload.Wait();
                    schematransfer = tskPreload.Result;
                }
                else
                {
                    AbortBackgroundTask();
                    cancelToken = new CancellationToken();
                    schematransfer = new SqlSchemaTransfer(Properties.Settings.Default.SourceServer, Properties.Settings.Default.DestinationServer, cancelToken);
                }
                strtskSource = strtskDestination = null;
                tskPreload = null;

                if (schematransfer.SameDatabase &&
                    MessageBox.Show("Source and destination databases are the same, do you want to continue?", "Warning",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                {
                    successConnecting = false;
                }
                else
                {
                    Hide();
                }
            }
            catch (Exception exc)
            {
                tskPreload = null;
                successConnecting = false;
                var errorMsg = exc.Message;
                var ex = exc.InnerException;
                while (ex != null)
                {
                    errorMsg += Environment.NewLine + ex.Message;
                    ex = ex.InnerException;
                }
                MessageBox.Show(errorMsg);
            }
            lblPleaseWait.Visible = lblPleaseWait.Enabled = false;
            if (!successConnecting)
            {
                foreach (Control c in Controls)
                {
                    c.Enabled = true;
                }
                tskPreload = null;
                return;
            }
            if (schematransfer.SourceObjects?.Count > 0)
            {
                firststepok = false;
                var chooseSchema = new ChooseSchemas(schematransfer, isData.Checked, isData.Checked && !isSchema.Checked, !isData.Checked && isSchema.Checked, autoRun);
                var resultdiag = chooseSchema.ShowDialog();
                if (resultdiag == DialogResult.Abort || resultdiag == DialogResult.Cancel)
                {
                    Environment.Exit(0);
                    return;
                }
                else if (resultdiag == DialogResult.OK)
                {
                    firststepok = true;
                    if (chooseSchema.SelectedObjects != null)
                    {
                        tablesToCopy = chooseSchema.SelectedObjects.ToList();
                    }
                    else
                    {
                        Environment.Exit(0);
                        return;
                    }
                }
            }
            else
            {
                {
                    MessageBox.Show("No SQL objects found in source database to copy");
                    Environment.Exit(0);
                    return;
                }
            }

            if (isData.Checked)
            {
                SqlDataTransfer datatransfer;
                List<SqlDataObject> itemsToCopy;
                try
                {
                    datatransfer = new SqlDataTransfer(Properties.Settings.Default.SourceServer, Properties.Settings.Default.DestinationServer,
                        schematransfer.LstPostExecutionExecute);
                    itemsToCopy = new List<SqlDataObject>();
                    if (tablesToCopy != null)
                    {
                        tablesToCopy.Where(t => t.CopyData).ToList().ForEach(item =>
                            itemsToCopy.Add(new SqlDataObject
                            {
                                Table = item.Object.ToString(),
                                TopRecords = ((SqlSchemaTable)item).TopRecords,
                                WhereFilter = ((SqlSchemaTable)item).WhereFilter,
                                HasRelationships = ((SqlSchemaTable)item).HasRelationships,
                                RowCount = ((SqlSchemaTable)item).RowCount
                            })
                        );
                        if (itemsToCopy.Count == 0)
                        {
                            //no tables selected, no need to copy data
                            Environment.Exit(0);
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show("No tables found, exiting");
                        Environment.Exit(0);
                        return;
                    }
                }
                catch (Exception exc)
                {
                    MessageBox.Show(exc.Message);
                    return;
                }

                if (itemsToCopy.Count > 0)
                {
                    var copyTableData = new CopyTabledata(itemsToCopy, datatransfer, schematransfer, firststepok || autoRun,
                        Properties.Settings.Default.CopyCollation == SqlCollationAction.Set_destination_db_collation,
                        isData.Checked && !isSchema.Checked, isSchema.Checked ? initialTime : (DateTime?)null)
                    {
                        Visible = false
                    };
                    copyTableData.ShowDialog();
                    if (copyTableData.DialogResult == DialogResult.Retry)
                    {
                        AbortBackgroundTask();
                        strtskSource = strtskDestination = null;
                        SetFormControls(true);
                        ChooseConnections_Load(null, null);
                        Visible = true;
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                    return;
                }
                else
                {
                    MessageBox.Show("No tables found in source database to copy data from");
                    Environment.Exit(0);
                    return;
                }
            }
            if (!isData.Checked && !isSchema.Checked)
            {
                MessageBox.Show("Please select your operation to proceed further");
            }
            else
            {
                Close();
            }
        }

        private void txtSource_TextChanged(object sender, EventArgs e)
        {
            if (txtSource.Text != GetConnection(sourceConnection))
            {
                sourceConnection = txtSource.Text;
            }
        }

        private void txtDestination_TextChanged(object sender, EventArgs e)
        {
            if (txtDestination.Text != GetConnection(destinationConnection))
            {
                destinationConnection = txtDestination.Text;
            }
        }

        private void ChooseConnections_FormClosing(object sender, FormClosingEventArgs e)
        {
            Visible = false;
            AbortBackgroundTask();
        }

        private void AbortBackgroundTask()
        {
            if (tskPreload?.IsCanceled == false && !tskPreload.IsCompleted)
            {
                tokenSource.Cancel();
                try
                {
                    tskPreload.Wait();
                }
                catch
                {
                }
                finally
                {
                    tokenSource.Dispose();
                }
            }
            tskPreload = null;
        }

        //Experimental Multifactor authentication for Azure Databases as seen at
        //https://stackoverflow.com/questions/60564462/how-to-connect-to-a-database-using-active-directory-login-and-multifactor-authen
        private void MFAConnection()
        {
            string server = "tcp:XXXXXXXX.database.windows.net,1433";
            string dbname = "db";
            string username = "user@user.com";

            System.Data.Odbc.OdbcConnection odbccon = new System.Data.Odbc.OdbcConnection($"Driver={{ODBC Driver 17 for SQL Server}};Server={server};Database={dbname};Encrypt=yes;TrustServerCertificate=no;Authentication=ActiveDirectoryInteractive;UID={username};Connection Timeout=30");
            odbccon.Open();

            odbccon.Close();
        }
    }
}
