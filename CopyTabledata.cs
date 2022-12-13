﻿using Microsoft.SqlServer.Management.Smo;
using Sql2SqlCloner.Core.DataTransfer;
using Sql2SqlCloner.Core.SchemaTransfer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sql2SqlCloner
{
    public partial class CopyTabledata : Form
    {
        private readonly SqlDataTransfer DataTransfer;
        private readonly SqlSchemaTransfer SchemaTransfer;
        private readonly bool SelectOnlyTables;
        private int currrow;
        private int errorCount;
        private int percentage;
        private string currentlyCopying = "";
        private string lastError = "";
        private readonly DateTime? initialTime;
        private readonly object inProgress = new object();
        private readonly Stopwatch stopwatch1 = new Stopwatch();
        private readonly ManualResetEvent pause = new ManualResetEvent(true);
        private readonly object objLock = new object();

        public CopyTabledata(List<SqlDataObject> list, SqlDataTransfer initialdatatransfer, SqlSchemaTransfer initialschematransfer, bool startImmediately, bool convertCollation, bool selectOnlyTables, DateTime? initialTime)
        {
            this.initialTime = initialTime;
            var CopyRows = new ConcurrentBag<DataGridViewRow>();
            //show form while initializating
            InitializeComponent();
            Visible = true;
            Cursor = Cursors.WaitCursor;
            dataGridView1.Rows.Add(Properties.Resources.waiting, "Calculating...", "", null, "");
            stopwatch1.Start();
            Timer1.Start();

            Application.DoEvents();

            DataTransfer = initialdatatransfer;
            SchemaTransfer = initialschematransfer;
            SelectOnlyTables = selectOnlyTables;

            var tabDictionarySource = new Dictionary<string, Table>();
            SchemaTransfer.SourceObjects.OfType<SqlSchemaTable>().Select(o => o.Object as Table).ToList()
                .ForEach(t => tabDictionarySource[t.ToString()] = t);
            if (SchemaTransfer.DestinationObjects.Count == 0)
            {
                SchemaTransfer.RefreshDestinationObjects();
            }
            var tabDictionaryDestination = new Dictionary<string, Table>();
            SchemaTransfer.DestinationObjects.OfType<SqlSchemaTable>().Select(o => o.Object as Table).ToList()
                .ForEach(t => tabDictionaryDestination[t.ToString()] = t);

            Task tskDeleteRecords = null;
            if (SelectOnlyTables)
            {
                if (Properties.Settings.Default.DeleteDestinationTables)
                {
                    tskDeleteRecords = Task.Run(() =>
                    {
                        DataTransfer.DisableAllDestinationConstraints();
                        DataTransfer.DeleteDestinationDatabase();
                    });
                }
            }

            if (!long.TryParse(ConfigurationManager.AppSettings["GlobalTOP"], out long GLOBALTOP))
            {
                GLOBALTOP = 0;
            }

            if (GLOBALTOP < 0)
            {
                GLOBALTOP = 0;
            }

            //Doing this process with two threads improves performance
            //adding more threads does not improve results
            var NUMTHREADS = 2;
            var tasks = new List<Task>();
            for (int i = 0; i < NUMTHREADS; i++)
            {
                var CURRENTTHREAD = i;
                tasks.Add(Task.Run(() =>
                {
                    var lastRefresh = DateTime.Now;
                    var sublist = list.Skip(CURRENTTHREAD * (list.Count / NUMTHREADS))
                        //last thread takes "the rest" of the items
                        .Take(CURRENTTHREAD == (NUMTHREADS - 1) ? list.Count : list.Count / NUMTHREADS);

                    foreach (var item in sublist)
                    {
                        long TOP = item.TopRecords;
                        var sTOP = "";
                        if (TOP <= 0)
                        {
                            TOP = GLOBALTOP;
                        }

                        if (GLOBALTOP > 0 && TOP > 0 && TOP > GLOBALTOP)
                        {
                            TOP = GLOBALTOP;
                        }

                        if (TOP > 0)
                        {
                            sTOP = $" TOP {TOP}";
                        }

                        var fields = " *";
                        if (convertCollation)
                        {
                            fields = "";
                            var sourceTable = tabDictionarySource[item.Table];
                            var selectList = new StringBuilder();
                            var destinationTable = tabDictionaryDestination[item.Table];
                            foreach (Column col in sourceTable.Columns)
                            {
                                if (!col.Computed)
                                {
                                    selectList.Append(selectList.Length == 0 ? " " : ",");
                                    if (!string.IsNullOrEmpty(col.Collation))
                                    {
                                        lock (objLock)
                                        {
                                            selectList.Append(col.ToString()).Append(" COLLATE ").Append(
                                                destinationTable.Columns[col.Name].Collation).Append(" AS ").Append(col.ToString());
                                        }
                                    }
                                    else
                                    {
                                        selectList.Append(col.ToString());
                                    }
                                }
                            }
                            fields = selectList.ToString();
                        }

                        var sql = $"SELECT{sTOP}{fields} FROM {item.Table} WITH(NOLOCK) {item.WhereFilter}";
                        var row = (DataGridViewRow)dataGridView1.Rows[0].Clone();
                        row.SetValues(Properties.Resources.empty, item.Table, sql.Trim(), null, item.HasRelationships.ToString().ToLowerInvariant());
                        CopyRows.Add(row);
                        if (CURRENTTHREAD == 0)
                        {
                            //Prevent "ContextSwitchDeadlock Was Detected" exceptions
                            if ((DateTime.Now - lastRefresh).TotalSeconds > 9)
                            {
                                Application.DoEvents();
                                lastRefresh = DateTime.Now;
                            }
                        }
                    }
                }
                ));
            }

            tasks.ForEach(tt => tt.Wait());

            tskDeleteRecords?.Wait();

            dataGridView1.Rows.Clear();
            dataGridView1.Rows.AddRange(CopyRows.OrderBy(a => a.Cells[1].Value.ToString()).ToArray());
            btnCancel.Enabled = btnCopyMessages.Enabled = btnNext.Enabled = btnPause.Enabled = true;
            Cursor = Cursors.Default;
            label1.Text = "Click on the 'Copy' button to start copying the below listed SQL tables' data";
            Application.DoEvents();

            if (startImmediately)
            {
                btnNext_Click(null, null);
            }
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            if (btnNext.Text == "Start over")
            {
                DialogResult = DialogResult.Retry;
                Close();
            }
            else
            {
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
        }

        private void backgroundWorker1_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            double current = 0;
            double max = dataGridView1.Rows.Count;

            DataTransfer.DisableAllDestinationConstraints();

            foreach (DataGridViewRow item in dataGridView1.Rows)
            {
                pause.WaitOne(Timeout.Infinite);
                currrow++;
                if (item.IsNewRow)
                {
                    continue;
                }

                if (backgroundWorker1.CancellationPending)
                {
                    break;
                }

                try
                {
                    var tableName = item.Cells["Table"].Value.ToString();
                    currentlyCopying = $"Copying {tableName.Replace("[", "").Replace("]", "")}...";
                    DataTransfer.TransferData(item.Cells["Table"].Value.ToString(), item.Cells["SqlCommand"].Value.ToString());
                    //enable table constraints for standalone tables to avoid a single fat transaction at the end
                    if (string.Equals(item.Cells["HasRelationships"].Value.ToString(), "false", StringComparison.InvariantCultureIgnoreCase))
                    {
                        DataTransfer.EnableTableConstraints(tableName);
                    }
                    item.Cells["Status"].Value = Properties.Resources.success;
                    ((System.Drawing.Bitmap)item.Cells["Status"].Value).Tag = "OK";
                }
                catch (Exception exc)
                {
                    item.Cells["Status"].Value = Properties.Resources.failure;
                    ((System.Drawing.Bitmap)item.Cells["Status"].Value).Tag = "ERROR";
                    item.Cells["Error"].Value = exc.Message;
                    if (exc.InnerException != null)
                    {
                        item.Cells["Error"].Value = exc.InnerException.Message;
                    }

                    errorCount++;
                }
                backgroundWorker1.ReportProgress((int)((++current) / max * 100.0));
            }
            try
            {
                SchemaTransfer?.ReAddSchemaBindingToDestination();
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                MessageBox.Show($"Error: {lastError}");
                errorCount++;
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
                        {
                            dataGridView1.FirstDisplayedScrollingRowIndex = currrow - 8;
                        }
                        else if (currrow < 10)
                        {
                            dataGridView1.Refresh();
                        }
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
                label1.Text = $"Operation completed {(errorCount == 0 ? "successfully" : $"with {errorCount} errors")}" +
                    (string.IsNullOrEmpty(lastError) ? "" : $". Last error was: {lastError}");
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
                    Application.DoEvents();
                    btnCancel.Enabled = false;
                    DateTime endTime;
                    var msgResult = "";
                    try
                    {
                        DataTransfer.EnableAllDestinationConstraints();
                        if (string.Equals(ConfigurationManager.AppSettings["DisableDisabledObjects"], "true", StringComparison.OrdinalIgnoreCase))
                        {
                            DataTransfer.DisableDisabledObjects();
                        }
                        endTime = DateTime.Now;
                        msgResult = "Success";
                        label1.Text = savelabeltext;
                    }
                    catch (Exception ex)
                    {
                        label1.Text = $"Completed with errors, constraints not enabled: {ex.Message}";
                        btnCopyMessages.Visible = true;
                        endTime = DateTime.Now;
                        msgResult = ex.Message;
                    }
                    if (initialTime.HasValue)
                    {
                        var timeDiff = endTime - initialTime.Value;
                        label2.Text += $", Total running time {(timeDiff.Days == 0 ? "" : timeDiff.Days + " days ") + timeDiff.ToString("hh\\:mm\\:ss")}";
                    }
                    MessageBox.Show(msgResult);
                    btnNext.Text = "Start over";
                    btnNext.Enabled = true;
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
            {
                backgroundWorker1.CancelAsync();
            }

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
            return $" Elapsed time: {(days > 0 ? $"{days}:" : "")}{TimeSpan.FromMilliseconds(stopwatch1.ElapsedMilliseconds):hh\\:mm\\:ss}";
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
                if (row.Visible)
                {
                    if (firstRow)
                    {
                        firstRow = false;
                    }
                    else
                    {
                        sb.Append(Environment.NewLine);
                    }

                    int counter = 0;
                    var firstCell = true;
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        if (counter < 4)
                        {
                            if (firstCell)
                            {
                                firstCell = false;
                            }
                            else
                            {
                                sb.Append('\t');
                            }

                            if (cell is DataGridViewImageCell)
                            {
                                if (cell.Value == null)
                                {
                                    sb.Append("N/A");
                                }
                                else
                                {
                                    sb.Append(((System.Drawing.Bitmap)cell.Value).Tag.ToString());
                                }
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