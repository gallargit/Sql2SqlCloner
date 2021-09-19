using Microsoft.Data.ConnectionUI;
using Sql2SqlCloner.Core.Data;
using Sql2SqlCloner.Core.Schema;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sql2SqlCloner
{
    public partial class ChooseConnections : Form
    {
        private string strtskSource, strtskDestination;
        private Task<SqlSchemaTransfer> tskPreload;
        private readonly CancellationTokenSource tokenSource = new CancellationTokenSource();
        private CancellationToken token;

        public ChooseConnections()
        {
            InitializeComponent();
        }

        private void ChooseConnections_Load(object sender, EventArgs e)
        {
            Icon = System.Drawing.Icon.FromHandle(Properties.Resources.Clone.Handle);
            txtSource.Text = Properties.Settings.Default.SourceServer;
            txtDestination.Text = Properties.Settings.Default.DestinationServer;
            isSchema.Checked = Properties.Settings.Default.CopySchema;
            isData.Checked = Properties.Settings.Default.CopyData;
            if (string.Equals(ConfigurationManager.AppSettings["Autorun"], "true", StringComparison.InvariantCultureIgnoreCase))
            {
                btnNext_Click(sender, e);
            }
            else
            {
                //start preloading the saved databases' info
                if (ConfigurationManager.AppSettings["EnablePreload"]?.ToString().ToLower() == "true")
                {
                    //token = tokenSource.Token;
                    if (!string.IsNullOrEmpty(txtSource.Text) && !string.IsNullOrEmpty(txtDestination.Text))
                    {
                        strtskSource = txtSource.Text;
                        strtskDestination = txtDestination.Text;
                        tskPreload = Task.Run(() => new SqlSchemaTransfer(Properties.Settings.Default.SourceServer, Properties.Settings.Default.DestinationServer, token), token);
                    }
                }
            }
        }

        private string GetConnectionString(string connectionString)
        {
            try
            {
                _ = new DbConnectionStringBuilder
                {
                    ConnectionString = connectionString
                };
            }
            catch
            {
                connectionString = null; //bad connection string
            }

            string conn = null;
            using (var dialog = new DataConnectionDialog())
            {
                dialog.DataSources.Add(DataSource.SqlDataSource);
                if (!string.IsNullOrEmpty(connectionString))
                    dialog.ConnectionString = connectionString;

                if (DataConnectionDialog.Show(dialog) == DialogResult.OK)
                {
                    conn = dialog.ConnectionString;
                }
            }
            return conn;
        }

        private void btnDestination_Click(object sender, EventArgs e)
        {
            var newcx = GetConnectionString(txtDestination.Text);
            if (!string.IsNullOrEmpty(newcx))
                txtDestination.Text = newcx;
        }

        private void btnSource_Click(object sender, EventArgs e)
        {
            var newcx = GetConnectionString(txtSource.Text);
            if (!string.IsNullOrEmpty(newcx))
                txtSource.Text = newcx;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            //Application.Exit() cannot be called because the SQL Connection dialog prevents
            //the application from exiting if the Cancel button is clicked
            Environment.Exit(0);
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
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
            Properties.Settings.Default.SourceServer = txtSource.Text;
            Properties.Settings.Default.DestinationServer = txtDestination.Text;
            Properties.Settings.Default.CopySchema = isSchema.Checked;
            Properties.Settings.Default.CopyData = isData.Checked;
            Properties.Settings.Default.Save();

            var firststepok = false;
            var successConnecting = true;
            SqlSchemaTransfer transferSchema = null;
            List<SqlSchemaObject> tablesToCopy = null;
            try
            {
                foreach (Control c in Controls)
                {
                    c.Enabled = false;
                }
                lblPleaseWait.Visible = lblPleaseWait.Enabled = true;
                Application.DoEvents();
                if (tskPreload != null && strtskSource == txtSource.Text && strtskDestination == txtDestination.Text)
                {
                    tskPreload.Wait();
                    transferSchema = tskPreload.Result;
                }
                else
                {
                    AbortBackgroundTask();
                    token = new CancellationToken();
                    transferSchema = new SqlSchemaTransfer(Properties.Settings.Default.SourceServer, Properties.Settings.Default.DestinationServer, token);
                }
                tskPreload = null;

                if (transferSchema.SameDatabase &&
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
            if (transferSchema.SourceObjects?.Count > 0)
            {
                firststepok = false;
                var next = new ChooseSchemas(transferSchema, isData.Checked, isData.Checked && !isSchema.Checked, !isData.Checked && isSchema.Checked);
                var resultdiag = next.ShowDialog();
                if (resultdiag == DialogResult.Abort || resultdiag == DialogResult.Cancel)
                {
                    Environment.Exit(0);
                    return;
                }
                else if (resultdiag == DialogResult.OK)
                {
                    firststepok = true;
                    if (next.SelectedObjects != null)
                    {
                        tablesToCopy = next.SelectedObjects.ToList();
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
                MessageBox.Show("No SQL objects found in source database to copy");
                Environment.Exit(0);
                return;
            }
            if (isData.Checked)
            {
                SqlDataTransfer transfer;
                List<SqlDataObject> itemsToCopy;
                try
                {
                    transfer = new SqlDataTransfer(Properties.Settings.Default.SourceServer, Properties.Settings.Default.DestinationServer);
                    itemsToCopy = new List<SqlDataObject>();
                    if (tablesToCopy != null)
                    {
                        tablesToCopy.Where(t => t.CopyData).ToList().ForEach(item =>
                            itemsToCopy.Add(new SqlDataObject
                            {
                                Table = item.Object.ToString(),
                                TopRecords = ((SqlSchemaTable)item).TopRecords,
                                WhereFilter = ((SqlSchemaTable)item).WhereFilter,
                                HasRelationships = ((SqlSchemaTable)item).HasRelationships
                            })
                        );
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
                    var next = new CopyTabledata(itemsToCopy, transfer, firststepok);
                    next.ShowDialog();
                    Environment.Exit(0);
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
