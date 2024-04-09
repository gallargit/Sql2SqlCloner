using Microsoft.Data.ConnectionUI;
using Microsoft.Data.SqlClient;
using Sql2SqlCloner.Components;
using Sql2SqlCloner.Core.DataTransfer;
using Sql2SqlCloner.Core.SchemaTransfer;
using System;
using System.Collections.Generic;
using System.Configuration;
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
        private bool EnablePreload = ConfigurationManager.AppSettings["EnablePreload"]?.ToLowerInvariant() == "true";
        private bool AutoRun;
        private Task<SqlSchemaTransfer> tskPreload;
        private readonly CancellationTokenSource tokenSource = new CancellationTokenSource();
        private CancellationToken cancelToken;
        private const string TrustServerCertificateDesc = "TrustServerCertificate";
        private Point originalLocation = new Point(0, 0);

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
            var success = false;
            if (ConfigurationManager.AppSettings["ConnectionDialog"]?.ToLower() == "microsoft")
            {
                var dcd = new DataConnectionDialog(connectionString);
                if (dcd.Show() == DialogResult.OK)
                {
                    success = true;
                    connectionString = dcd.ConnectionString;
                }
            }
            else
            {
                success = new SQLConnectionDialog(Icon).ShowDialog(ref connectionString) == DialogResult.OK;
            }
            if (success)
            {
                return connectionString;
            }
            return "";
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
            if (originalLocation.X == 0)
            {
                originalLocation.X = Location.X;
            }
            if (originalLocation.Y == 0)
            {
                originalLocation.Y = Location.Y;
            }
            Icon = Icon.FromHandle(Properties.Resources.Clone.Handle);
            sourceConnection = Properties.Settings.Default.SourceServer;
            txtSource.Text = GetConnection(sourceConnection);
            destinationConnection = Properties.Settings.Default.DestinationServer;
            txtDestination.Text = GetConnection(destinationConnection);
            isSchema.Checked = Properties.Settings.Default.CopySchema;
            isData.Checked = Properties.Settings.Default.CopyData;
            decryptObjects.Checked = Properties.Settings.Default.DecryptObjects;

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
                    tskPreload = Task.Run(() => new SqlSchemaTransfer(Properties.Settings.Default.SourceServer, Properties.Settings.Default.DestinationServer, false, null, cancelToken), cancelToken);
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
            Visible = false;
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
                MessageBox.Show("Please tick what to copy (schema/data)", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

            string DACConnectionString = null;
            if (decryptObjects.Checked && isSchema.Visible)
            {
                var builder = new SqlConnectionStringBuilder //ojo he cambiado esto y no he probado
                {
                    ConnectionString = sourceConnection
                };
                if (!builder.TryGetValue("Initial Catalog", out object objField))
                {
                    throw new Exception("DAC Database not found");
                }
                var DACDATABASE = objField.ToString();
                if (!builder.TryGetValue("Data Source", out objField))
                {
                    throw new Exception("DAC Host not found");
                }
                var DACHOST = objField.ToString();

                var DACUSER = "sa";
                var DACPASSWORD = "";
                if (builder.TryGetValue("User Id", out objField) &&
                    objField.ToString() == DACUSER &&
                    builder.TryGetValue("Password", out objField))
                {
                    DACPASSWORD = objField.ToString();
                }
                if (string.IsNullOrEmpty(DACPASSWORD))
                {
                    new InputBoxValidate("Enter password", "Please enter 'sa' password", hideChars: true).ShowDialog(ref DACPASSWORD);
                    if (string.IsNullOrEmpty(DACPASSWORD))
                    {
                        return;
                    }
                }

                //if using DAC connection, preload should not be considered
                AbortBackgroundTask();
                strtskSource = null;
                DACConnectionString = $"Packet Size=4096;User Id={DACUSER};Password={DACPASSWORD};Data Source=ADMIN:{DACHOST};Initial Catalog={DACDATABASE};{TrustServerCertificateDesc}= true";
            }
            Properties.Settings.Default.SourceServer = sourceConnection;
            Properties.Settings.Default.DestinationServer = destinationConnection;
            Properties.Settings.Default.CopySchema = isSchema.Checked;
            Properties.Settings.Default.CopyData = isData.Checked;
            Properties.Settings.Default.DecryptObjects = decryptObjects.Checked;
            Properties.Settings.Default.Save();

            var successConnecting = true;
            SqlSchemaTransfer schemaTransfer = null;
            IList<SqlSchemaTable> tablesToCopy = null;
            try
            {
                SetFormControls(false);
                Application.DoEvents();
                if (tskPreload != null && strtskSource == sourceConnection && strtskDestination == destinationConnection)
                {
                    tskPreload.Wait(cancelToken);
                    schemaTransfer = tskPreload.Result;
                }
                else
                {
                    AbortBackgroundTask();
                    cancelToken = new CancellationToken();
                    schemaTransfer = new SqlSchemaTransfer(Properties.Settings.Default.SourceServer, Properties.Settings.Default.DestinationServer, false, DACConnectionString, cancelToken);
                    lblPleaseWait.Visible = false;
                }
                strtskSource = strtskDestination = null;
                tskPreload = null;

                if (schemaTransfer.SameDatabase &&
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
                lblPleaseWait.Visible = false;
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
            var firstStepOk = false;
            if (schemaTransfer.SourceObjects?.Count > 0)
            {
                var chooseSchema = new ChooseSchemas(schemaTransfer, isData.Checked, isData.Checked && !isSchema.Checked, !isData.Checked && isSchema.Checked, autoRun);
                chooseSchema.Location = new Point(originalLocation.X - ((chooseSchema.Width - Width) / 2), originalLocation.Y - ((chooseSchema.Height - Height) / 2));
                var resultdiag = chooseSchema.ShowDialog();
                if (resultdiag == DialogResult.Abort || resultdiag == DialogResult.Cancel)
                {
                    Environment.Exit(0);
                    return;
                }
                else if (resultdiag == DialogResult.OK)
                {
                    firstStepOk = true;
                    if (chooseSchema.SelectedObjects != null)
                    {
                        tablesToCopy = chooseSchema.SelectedObjects.OfType<SqlSchemaTable>().Where(c => c.CopyData).ToList();
                    }
                    else
                    {
                        Environment.Exit(0);
                        return;
                    }
                }
                if (resultdiag == DialogResult.Retry)
                {
                    RestartForm();
                    return;
                }
            }
            else
            {
                MessageBox.Show("No SQL objects found in source database to copy from");
                Environment.Exit(0);
                return;
            }

            if (isData.Checked)
            {
                SqlDataTransfer datatransfer;
                try
                {
                    datatransfer = new SqlDataTransfer(Properties.Settings.Default.SourceServer, Properties.Settings.Default.DestinationServer, schemaTransfer.LstPostExecutionExecute);
                    if (tablesToCopy == null)
                    {
                        MessageBox.Show("No tables found, exiting");
                        Visible = false;
                        Environment.Exit(0);
                        return;
                    }
                    else if (!tablesToCopy.Any(t => t.CopyData))
                    {
                        //no tables selected, no need to copy data
                        MessageBox.Show("Finished. No tables selected to copy data from");
                        Visible = false;
                        Environment.Exit(0);
                        return;
                    }
                }
                catch (Exception exc)
                {
                    MessageBox.Show(exc.Message);
                    return;
                }
                var copyTableData = new CopyTabledata(tablesToCopy, datatransfer, schemaTransfer, firstStepOk || autoRun,
                    Properties.Settings.Default.CopyCollation == SqlCollationAction.Set_destination_db_collation,
                    isData.Checked && !isSchema.Checked, isSchema.Checked ? initialTime : (DateTime?)null);
                copyTableData.Location = new Point(originalLocation.X - ((copyTableData.Width - Width) / 2), originalLocation.Y - ((copyTableData.Height - Height) / 2));
                copyTableData.ShowDialog();
                if (copyTableData.DialogResult == DialogResult.Retry)
                {
                    RestartForm();
                }
                else
                {
                    Environment.Exit(0);
                }
                return;
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

        private void isSchema_CheckedChanged(object sender, EventArgs e)
        {
            decryptObjects.Visible = isSchema.Checked;
        }

        private void ChooseConnections_FormClosing(object sender, FormClosingEventArgs e)
        {
            Visible = false;
            AbortBackgroundTask();
        }

        private void RestartForm()
        {
            AbortBackgroundTask();
            strtskSource = strtskDestination = null;
            SetFormControls(true);
            ChooseConnections_Load(null, null);
            Visible = true;
        }

        private void AbortBackgroundTask()
        {
            if (tskPreload?.IsCanceled == false && !tskPreload.IsCompleted)
            {
                tokenSource.Cancel();
                try
                {
                    tskPreload.Wait(cancelToken);
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
    }
}
