using Sql2SqlCloner.Core.Schema;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Sql2SqlCloner
{
    public partial class CopySchema : Form
    {
        public List<SqlSchemaObject> CopyList { get; }
        private readonly SqlSchemaTransfer transfer;
        private readonly bool CloseIfSuccess;
        private string lastError = "";
        private string currentlyCopying = "";
        private double current;
        private int errorCount;
        private int currrow;
        private int percentage;
        private bool windowClosing;
        private bool btnPauseEnabled = true;
        private readonly object inProgress = new object();
        private readonly Stopwatch stopwatch1 = new Stopwatch();
        private readonly ManualResetEvent pause = new ManualResetEvent(true);

        public CopySchema(SqlSchemaTransfer transferSchema, List<SqlSchemaObject> lstObjects, bool closeIfSuccess, bool autoStart)
        {
            InitializeComponent();
            CloseIfSuccess = closeIfSuccess;
            transfer = transferSchema;
            CopyList = lstObjects.ToList();

            if (!Properties.Settings.Default.CopySecurity)
            {
                CopyList = CopyList.Where(t => t.Type != "User" && t.Type != "DatabaseRole").ToList();
            }
            if (!(Properties.Settings.Default.CopyFullText && Properties.Settings.Default.CopyConstraints))
            {
                CopyList = CopyList.Where(t => t.Type != "FullTextCatalog" && t.Type != "FullTextStopList").ToList();
            }
            if (CopyList.Count == 0)
            {
                MessageBox.Show("No SQL objects found in source database to proceed");
                DialogResult = DialogResult.Abort;
                Environment.Exit(0);
                return;
            }
            dataGridView1.DataSource = CopyList;

            label1.Text = "Click on the 'Copy' button to start copying below listed SQL objects";
            if (autoStart)
                btnNext_Click(null, null);
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            if (btnNext.Text == "Continue")
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            stopwatch1.Start();
            Timer1.Start();
            btnNext.Enabled = false;
            Cursor = Cursors.WaitCursor;
            backgroundWorker1.DoWork += backgroundWorker1_DoWork;
            backgroundWorker1.RunWorkerCompleted += backgroundWorker1_RunWorkerCompleted;
            backgroundWorker1.ProgressChanged += backgroundWorker1_ProgressChanged;
            backgroundWorker1.WorkerSupportsCancellation = true;
            backgroundWorker1.WorkerReportsProgress = true;
            currentlyCopying = label1.Text = "Processing schema...";
            progressBar1.Value = 0;
            backgroundWorker1.RunWorkerAsync();
        }

        private void backgroundWorker1_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            if (Properties.Settings.Default.ClearDestinationDatabase)
            {
                CopyList.FirstOrDefault().Status = Properties.Resources.waiting;
                currentlyCopying = "Clearing destination database...";
                backgroundWorker1.ReportProgress(0);
                try
                {
                    transfer.ClearDestinationDatabase();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                CopyList.FirstOrDefault().Status = null;
            }
            currentlyCopying = "Processing schema...";
            bool overrideCollation, useSourceCollation;
            overrideCollation = useSourceCollation = transfer.NoCollation = false;
            switch (Properties.Settings.Default.CopyCollation)
            {
                case 1: //no collation
                    transfer.NoCollation = true;
                    break;
                case 2: //override collation, use source db
                    overrideCollation = useSourceCollation = true;
                    break;
                case 3: //override collation, use destination db
                    overrideCollation = true;
                    useSourceCollation = false;
                    break;
                default: //0 ignore, do nothing about collation
                    break;
            }
            transfer.IncludeExtendedProperties = Properties.Settings.Default.CopyExtendedProperties;
            transfer.IncludePermissions = Properties.Settings.Default.CopyPermissions;
            double max = CopyList.Count;
            var CopyConstraints = Properties.Settings.Default.CopyConstraints;
            if (CopyConstraints)
                max += CopyList.Count(i => i.Type == "Table" || i.Type == "View") * 4.0;
            if (Properties.Settings.Default.CopyExtendedProperties)
                max += CopyList.Count / 5.0;
            if (Properties.Settings.Default.CopyPermissions)
                max += CopyList.Count / 6.0;
            var retries = 0;
            var currList = CopyList;
            var finishedPass1 = false;
            var finishedPass2 = false;

            //the first time indexes won't be available, therefore some items dependent on them
            //such as FullText objects won't be created, the second time indexes will be available
            //so that those objects will be created
            while (!finishedPass2)
            {
                //do all objects, retrying failed ones until no more objects can be created,
                //this way all dependent objects will be created
                while (!finishedPass1)
                {
                    if (windowClosing)
                        return;
                    if (retries > 0)
                    {
                        currList = currList.Where(item => !string.IsNullOrEmpty(item.Error) &&
                                                    item.Status != Properties.Resources.warning).ToList();
                        currList.ForEach(item => { item.Status = null; item.Error = string.Empty; });
                        errorCount = 0;
                    }
                    retries++;
                    foreach (var item in currList)
                    {
                        pause.WaitOne(Timeout.Infinite);
                        if (windowClosing)
                            return;
                        if (backgroundWorker1.CancellationPending)
                            break;
                        try
                        {
                            transfer.CreateObject(item.Object, Properties.Settings.Default.DropAndRecreateObjects, overrideCollation, useSourceCollation);
                            item.Status = Properties.Resources.success;
                        }
                        catch (Exception ex)
                        {
                            item.Status = Properties.Resources.failure;
                            item.Error = string.Empty;
                            var exc = ex;
                            while (exc != null)
                            {
                                lastError = $"{exc.Message} (affected object: {item.Name})";
                                if (item.Error != "")
                                    item.Error += ";";
                                item.Error += exc.Message;
                                exc = exc.InnerException;
                            }
                            errorCount++;
                        }
                        if (retries == 1)
                        {
                            backgroundWorker1.ReportProgress((int)((++current) / max * 100.0));
                            currrow++;
                        }
                    }
                    finishedPass1 = (errorCount == 0 || errorCount == currList.Count);
                }
                if (!CopyConstraints)
                {
                    finishedPass2 = true;
                }
                else
                {
                    finishedPass2 = finishedPass1 = errorCount == 0;
                    CopyConstraints = false;
                    if (windowClosing)
                        return;
                    transfer.RefreshDestination();
                    currentlyCopying = "Processing indexes...";
                    foreach (var item in CopyList.Where(i => i.Type == "Table" || i.Type == "View").ToList())
                    {
                        try
                        {
                            pause.WaitOne(Timeout.Infinite);
                            transfer.ApplyIndexes(item.Object, Properties.Settings.Default.CopyFullText && Properties.Settings.Default.CopyConstraints);
                        }
                        catch (Exception ex)
                        {
                            HandleWarning(item, ex);
                        }
                        backgroundWorker1.ReportProgress((int)((current += 2) / max * 100.0));
                    }
                    if (windowClosing)
                        return;
                    currentlyCopying = "Processing foreign keys...";
                    var savecurrent = current;
                    foreach (var item in CopyList.Where(i => i.Type == "Table").ToList())
                    {
                        try
                        {
                            pause.WaitOne(Timeout.Infinite);
                            transfer.ApplyForeignKeys(item.Object);
                        }
                        catch (Exception ex)
                        {
                            HandleWarning(item, ex);
                        }
                        backgroundWorker1.ReportProgress((int)((++current) / max * 100.0));
                    }
                    current = savecurrent + CopyList.Count(i => i.Type == "Table" || i.Type == "View");

                    if (windowClosing)
                        return;
                    savecurrent = current;
                    currentlyCopying = "Processing checks...";
                    foreach (var item in CopyList.Where(i => i.Type == "Table"))
                    {
                        try
                        {
                            pause.WaitOne(Timeout.Infinite);
                            transfer.ApplyChecks(item.Object);
                            backgroundWorker1.ReportProgress((int)((++current) / max * 100.0));
                        }
                        catch (Exception ex)
                        {
                            HandleWarning(item, ex);
                        }
                    }
                    current = savecurrent + CopyList.Count(i => i.Type == "Table" || i.Type == "View");
                    if (!finishedPass2)
                        currentlyCopying = "Retrying failed objects";
                }
            }

            btnPauseEnabled = false;
            if (windowClosing)
                return;
            if (Properties.Settings.Default.CopyExtendedProperties)
            {
                currentlyCopying = "Processing extended properties...";
                try
                {
                    backgroundWorker1.ReportProgress((int)(current / max * 100.0));
                    transfer.CopyExtendedProperties(CopyList.ConvertAll(o => o.Object));
                    backgroundWorker1.ReportProgress((int)((current += CopyList.Count / 5.0) / max * 100.0));
                }
                catch (Exception ex)
                {
                    if (Properties.Settings.Default.StopIfErrors)
                        MessageBox.Show($"Error copying extended properties: {ex.Message}");
                    lastError = ex.Message;
                    errorCount++;
                }
            }

            if (Properties.Settings.Default.CopyPermissions)
            {
                currentlyCopying = "Processing permissions...";
                try
                {
                    //not needed transfer.CopyPermissions();
                    transfer.CopyRolePermissions();
                    backgroundWorker1.ReportProgress((int)((current += CopyList.Count / 6.0) / max * 100.0));
                }
                catch (Exception ex)
                {
                    if (Properties.Settings.Default.StopIfErrors)
                        MessageBox.Show($"Error copying permissions: {ex.Message}");
                    lastError = ex.Message;
                    errorCount++;
                }
            }
            transfer.EnableDestinationConstraints();
            transfer.DisableDisabledObjects();
            if (Properties.Settings.Default.CopySecurity)
            {
                transfer.CopySchemaAuthorization();
            }
            percentage = 100;
            backgroundWorker1.ReportProgress(100);
        }

        private void backgroundWorker1_ProgressChanged(object sender, System.ComponentModel.ProgressChangedEventArgs e)
        {
            lock (inProgress)
            {
                btnPause.Enabled = btnPauseEnabled;
                if (progressBar1.Value < 100 && e.ProgressPercentage >= progressBar1.Minimum && e.ProgressPercentage <= progressBar1.Maximum)
                {
                    percentage = progressBar1.Value = e.ProgressPercentage;
                    try
                    {
                        progressBar1.Value = e.ProgressPercentage > 100 ? 100 : e.ProgressPercentage;
                        if (autoScrollGrid.Checked)
                        {
                            if (currrow < dataGridView1.RowCount && currrow > 7 && dataGridView1.FirstDisplayedScrollingRowIndex != currrow - 8)
                                dataGridView1.FirstDisplayedScrollingRowIndex = currrow - 8;
                            else if (currrow < 10)
                                dataGridView1.Refresh();
                        }
                    }
                    catch { }
                }
                if (label1.Text != currentlyCopying)
                {
                    label1.Text = currentlyCopying;
                    label1.Refresh();
                }
            }
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            lock (inProgress)
            {
                btnPause.Enabled = false;
                stopwatch1.Stop();
                Timer1.Stop();
                Timer1_Tick(sender, e);
                Cursor = Cursors.Default;
                dataGridView1.Cursor = Cursors.Default;
                label1.Text = "Operation completed";
                if (errorCount == 0)
                    label1.Text += " successfully";
                else
                    label1.Text += $" with {errorCount} errors";
                btnCancel.Text = "Close";
                btnCopyMessages.Visible = true;
                dataGridView1.Refresh();
                if (CopyList.Any(t => string.IsNullOrEmpty(t.Error)))
                {
                    autoScrollGrid.Text = "Show only errors";
                    autoScrollGrid.CheckState = CheckState.Unchecked;
                }
                if (CloseIfSuccess || !Properties.Settings.Default.StopIfErrors)
                {
                    if (CopyList.All(t => string.IsNullOrEmpty(t.Error)) || !Properties.Settings.Default.StopIfErrors)
                    {
                        //no errors
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                    else
                    {
                        btnCancel.Text = "Close";
                        if (CopyList.OfType<SqlSchemaTable>().Any(t => !string.IsNullOrEmpty(t.Error)))
                        {
                            //table errors happened, can't continue
                            MessageBox.Show($"Error ocurred. Last error was: {lastError}");
                        }
                        else
                        {
                            //non-table errors, can continue
                            MessageBox.Show($"Error ocurred. Last error was: {lastError}{Environment.NewLine}{Environment.NewLine}Click on 'Continue' to go on");
                            btnNext.Text = "Continue";
                            btnPause.Enabled = false;
                            btnNext.Enabled = true;
                        }
                    }
                }
                else
                {
                    MessageBox.Show($"Finished. {label1.Text}");
                }
            }
        }

        private void HandleWarning(SqlSchemaObject item, Exception ex)
        {
            if (item.Status != Properties.Resources.failure)
            {
                item.Status = Properties.Resources.warning;
                item.Error = string.Empty;
                var exc = ex;
                while (exc != null)
                {
                    lastError = $"{exc.Message} (affected object: {item.Name})";
                    if (item.Error != "")
                        item.Error += ";";
                    item.Error += exc.Message;
                    exc = exc.InnerException;
                }
                errorCount++;
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (backgroundWorker1.IsBusy)
            {
                backgroundWorker1.CancelAsync();
                DialogResult = DialogResult.Abort;
                Environment.Exit(0);
                return;
            }
            if (current == 0 || btnCancel.Text == "Close")
            {
                DialogResult = DialogResult.Abort;
                Environment.Exit(0);
                return;
            }
            DialogResult = DialogResult.OK;
        }

        private void CopySchema_Load(object sender, EventArgs e)
        {
            Icon = System.Drawing.Icon.FromHandle(Properties.Resources.Clone.Handle);
            dataGridView1.Columns[0].Width = 40;
            dataGridView1.Columns[3].Width = 62;
            ((DataGridViewImageColumn)dataGridView1.Columns["Status"]).DefaultCellStyle.NullValue = null;
        }

        private void CopySchema_FormClosing(object sender, FormClosingEventArgs e)
        {
            windowClosing = true;
            if (((DialogResult == DialogResult.None || DialogResult == DialogResult.Cancel) && (current == 0 || btnCancel.Text == "Close")) ||
               (DialogResult == DialogResult.OK && btnCancel.Text == "Cancel"))
            {
                DialogResult = DialogResult.Abort;
                Environment.Exit(0);
            }
        }

        private string ElapsedTime()
        {
            return $"  elapsed time: {TimeSpan.FromMilliseconds(stopwatch1.ElapsedMilliseconds):hh\\:mm\\:ss}";
        }

        private void Timer1_Tick(object sender, EventArgs e)
        {
            label2.Text = $"{percentage}%  {ElapsedTime()}";
        }

        private void btnCopyMessages_Click(object sender, EventArgs e)
        {
            var sb = new StringBuilder();
            var firstRow = true;
            foreach (DataGridViewRow row in dataGridView1.Rows)
            {
                if (!firstRow)
                    sb.Append(Environment.NewLine);
                else
                    firstRow = false;
                var firstCell = true;
                foreach (DataGridViewCell cell in row.Cells)
                {
                    if (!firstCell)
                        sb.Append('\t');
                    else
                        firstCell = false;
                    if (cell is DataGridViewImageCell)
                    {
                        if (cell.Value == null)
                            sb.Append("N/A");
                        else if (((System.Drawing.Bitmap)cell.Value).Flags == Properties.Resources.success.Flags)
                            sb.Append("OK");
                        else if (((System.Drawing.Bitmap)cell.Value).Flags == Properties.Resources.warning.Flags)
                            sb.Append("WARNING");
                        else
                            sb.Append("ERROR");
                    }
                    else
                    {
                        sb.Append(cell.EditedFormattedValue?.ToString().Replace(Environment.NewLine, " ").Replace("\t", ""));
                    }
                }
            }
            Clipboard.SetText(sb.ToString());
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            if (btnPause.Text == "Pause")
            {
                pause.Reset();
                Timer1.Stop();
                stopwatch1.Stop();
                btnPause.Text = "Resume";
                btnCopyMessages.Visible = true;
            }
            else
            {
                btnCopyMessages.Visible = false;
                Timer1.Start();
                stopwatch1.Start();
                btnPause.Text = "Pause";
                pause.Set();
            }
        }

        private void autoScrollGrid_CheckedChanged(object sender, EventArgs e)
        {
            if (autoScrollGrid.Text == "Show only errors")
            {
                if (autoScrollGrid.Checked)
                    dataGridView1.DataSource = CopyList.Where(i => !string.IsNullOrEmpty(i.Error)).ToList();
                else
                    dataGridView1.DataSource = CopyList;
            }
        }
    }
}
