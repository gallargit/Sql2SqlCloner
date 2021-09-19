using Sql2SqlCloner.Core.Data;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Sql2SqlCloner
{
    public partial class CopyTabledata : Form
    {
        private readonly SqlDataTransfer transfer;
        private int currrow;
        private int errorCount;
        private int percentage;
        private string currentlyCopying = "";
        private readonly object inProgress = new object();
        private readonly Stopwatch stopwatch1 = new Stopwatch();
        private readonly ManualResetEvent pause = new ManualResetEvent(true);

        public CopyTabledata(List<SqlDataObject> list, SqlDataTransfer initialtransfer, bool startImmediately)
        {
            InitializeComponent();
            transfer = initialtransfer;

            if (!long.TryParse(ConfigurationManager.AppSettings["GlobalTOP"], out long GLOBALTOP))
                GLOBALTOP = 0;
            if (GLOBALTOP < 0)
                GLOBALTOP = 0;
            foreach (var item in list)
            {
                long TOP = item.TopRecords;
                var sTOP = "";
                if (TOP <= 0)
                    TOP = GLOBALTOP;
                if (GLOBALTOP > 0 && TOP > 0 && TOP > GLOBALTOP)
                    TOP = GLOBALTOP;
                if (TOP > 0)
                    sTOP = $" TOP {TOP}";
                var sql = $"SELECT{sTOP} * FROM {item.Table} WITH(NOLOCK) {item.WhereFilter}";

                dataGridView1.Rows.Add(null, item.Table, sql.Trim(), null, item.HasRelationships.ToString().ToLowerInvariant());
            }
            label1.Text = "Click on the 'Copy' button to start copying below listed SQL objects data";
            if (startImmediately)
                btnNext_Click(null, null);
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            stopwatch1.Start();
            Timer1.Start();
            btnNext.Enabled = false;
            Cursor = Cursors.WaitCursor;
            backgroundWorker1.DoWork += backgroundWorker1_DoWork;
            backgroundWorker1.RunWorkerCompleted += backgroundWorker1_RunWorkerCompleted;
            backgroundWorker1.ProgressChanged += backgroundWorker1_ProgressChanged;
            backgroundWorker1.WorkerSupportsCancellation = true;
            backgroundWorker1.WorkerReportsProgress = true;
            label1.Text = "Copying data in progress...";
            progressBar1.Value = 0;
            backgroundWorker1.RunWorkerAsync();
        }

        private void backgroundWorker1_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            double current = 0;
            double max = dataGridView1.Rows.Count;

            transfer.DisableAllDestinationConstraints();
            foreach (DataGridViewRow item in dataGridView1.Rows)
            {
                pause.WaitOne(Timeout.Infinite);
                currrow++;
                if (item.IsNewRow)
                    continue;
                if (backgroundWorker1.CancellationPending)
                    break;
                try
                {
                    var tableName = item.Cells["Table"].Value.ToString();
                    currentlyCopying = $"Copying {tableName.Replace("[", "").Replace("]", "")}...";
                    transfer.TransferData(item.Cells["Table"].Value.ToString(), item.Cells["SqlCommand"].Value.ToString());
                    //enable table constraints for standalone tables to avoid a single fat transaction at the end
                    if (string.Equals(item.Cells["HasRelationships"].Value.ToString(), "false", StringComparison.InvariantCultureIgnoreCase))
                    {
                        transfer.EnableTableConstraints(tableName);
                    }
                    item.Cells["Status"].Value = Properties.Resources.success;
                }
                catch (Exception exc)
                {
                    item.Cells["Status"].Value = Properties.Resources.failure;
                    item.Cells["Error"].Value = exc.Message;
                    if (exc.InnerException != null)
                        item.Cells["Error"].Value = exc.InnerException.Message;
                    errorCount++;
                }
                backgroundWorker1.ReportProgress((int)((++current) / max * 100.0));
            }
            percentage = 100;
            backgroundWorker1.ReportProgress(100);
        }

        private void backgroundWorker1_ProgressChanged(object sender, System.ComponentModel.ProgressChangedEventArgs e)
        {
            lock (inProgress)
            {
                if (progressBar1.Value < 100 && e.ProgressPercentage >= progressBar1.Minimum && e.ProgressPercentage <= progressBar1.Maximum)
                {
                    percentage = progressBar1.Value = e.ProgressPercentage;
                    if (autoScrollGrid.Checked)
                    {
                        if (currrow < dataGridView1.RowCount && currrow > 7 && dataGridView1.FirstDisplayedScrollingRowIndex != currrow - 8)
                            dataGridView1.FirstDisplayedScrollingRowIndex = currrow - 8;
                        else if (currrow < 10)
                            dataGridView1.Refresh();
                    }
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
                label1.Text = $"Operation completed {(errorCount == 0 ? "successfully" : $"with {errorCount} errors")}";
                btnCancel.Text = "Close";
                dataGridView1.Refresh();
                if (errorCount == 0)
                {
                    label2.Text = $"100%{ElapsedTime()}";
                    progressBar1.Value = 100;
                    progressBar1.Refresh();
                    var savelabeltext = label1.Text;
                    autoScrollGrid.Visible = false;
                    label1.Text = "Enabling constraints, please wait";
                    btnCancel.Enabled = false;
                    try
                    {
                        transfer.EnableAllDestinationConstraints();
                        transfer.DisableDisabledObjects();
                        MessageBox.Show("Success");
                        label1.Text = savelabeltext;
                    }
                    catch (Exception ex)
                    {
                        label1.Text = $"Completed with errors, constraints not enabled: {ex.Message}";
                        btnCopyMessages.Visible = true;
                        MessageBox.Show(ex.Message);
                    }
                    btnCancel.Enabled = true;
                }
                else
                {
                    btnCopyMessages.Visible = true;
                    autoScrollGrid.CheckState = CheckState.Unchecked;
                    autoScrollGrid.Text = "Show only errors";
                    autoScrollGrid.Left -= 30;
                    MessageBox.Show(label1.Text);
                }
                Cursor = Cursors.Default;
                dataGridView1.Cursor = Cursors.Default;
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (backgroundWorker1.IsBusy)
                backgroundWorker1.CancelAsync();
            DialogResult = DialogResult.Abort;
            Environment.Exit(0);
        }

        private void CopyTabledata_Load(object sender, EventArgs e)
        {
            Icon = System.Drawing.Icon.FromHandle(Properties.Resources.Clone.Handle);
            dataGridView1.Columns[0].Width = 40;
            ((DataGridViewImageColumn)dataGridView1.Columns["Status"]).DefaultCellStyle.NullValue = null;
        }

        private string ElapsedTime()
        {
            var days = 0;
            var millis = stopwatch1.ElapsedMilliseconds;
            while (millis > 86400000)
            {
                days++;
                millis -= 86400000;
            }
            return $"  elapsed time: {(days > 0 ? $"{days}:" : "")}{TimeSpan.FromMilliseconds(stopwatch1.ElapsedMilliseconds):hh\\:mm\\:ss}";
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
                int counter = 0;
                var firstCell = true;
                foreach (DataGridViewCell cell in row.Cells)
                {
                    if (counter < 4)
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
                    counter++;
                }
                Clipboard.SetText(sb.ToString());
            }
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
                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    row.Visible = !autoScrollGrid.Checked || !string.IsNullOrEmpty((string)row.Cells["Error"].Value);
                }
            }
        }
    }
}
