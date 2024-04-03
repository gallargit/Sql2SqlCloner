using Microsoft.SqlServer.Management.Smo;
using Sql2SqlCloner.Components;
using Sql2SqlCloner.Core.DataTransfer;
using Sql2SqlCloner.Core.SchemaTransfer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sql2SqlCloner
{
    public sealed partial class CopyTabledata : Form
    {
        public IList<SqlSchemaTable> CopyList { get; }
        private readonly SqlDataTransfer DataTransfer;
        private readonly SqlSchemaTransfer SchemaTransfer;
        private int currrow;
        private int errorCount;
        private int percentage;
        private string currentlyCopying = "";
        private string lastError = "";
        private readonly bool selectOnlyTables;
        private readonly DateTime? initialTime;
        private readonly object inProgress = new object();
        private readonly Stopwatch stopwatch1 = new Stopwatch();
        private readonly ManualResetEvent pause = new ManualResetEvent(true);
        private readonly object objLock = new object();

        public CopyTabledata(IList<SqlSchemaTable> list, SqlDataTransfer initialdatatransfer, SqlSchemaTransfer initialschematransfer,
            bool startImmediately, bool convertCollation, bool selectOnlyTables, DateTime? initialTime)
        {
            this.initialTime = initialTime;
            this.selectOnlyTables = selectOnlyTables;
            CopyList = list;
            var CopyRows = new ConcurrentBag<DataGridViewRow>();
            //show form while initializing
            InitializeComponent();
            Visible = true;
            Cursor = Cursors.WaitCursor;
            dataGridView1.Rows.Add(Properties.Resources.waiting, "Calculating", "", null, "", 0);
            (dataGridView1.Rows[0].Cells[0].Value as Bitmap).Tag = Constants.WAITING;

            stopwatch1.Start();
            Timer1.Start();

            Application.DoEvents();

            DataTransfer = initialdatatransfer;
            SchemaTransfer = initialschematransfer;

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
            if (selectOnlyTables && Properties.Settings.Default.DeleteDestinationTables)
            {
                tskDeleteRecords = Task.Run(() =>
                {
                    DataTransfer.DisableAllDestinationConstraints();
                    DataTransfer.DeleteDestinationDatabase();
                });
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
            const int NUMTHREADS = 2;
            var tasks = new List<Task>();
            for (int i = 0; i < NUMTHREADS; i++)
            {
                var CURRENTTHREAD = i;
                tasks.Add(Task.Run(() =>
                {
                    var lastRefresh = DateTime.Now;
                    var sublist = list.Skip(CURRENTTHREAD * (list.Count / NUMTHREADS))
                        //last thread takes remaining items
                        .Take(CURRENTTHREAD == (NUMTHREADS - 1) ? list.Count : list.Count / NUMTHREADS);

                    foreach (var item in sublist)
                    {
                        long itemTopRecords = item.TopRecords;
                        var stritemTopRecords = "";
                        if (itemTopRecords <= 0)
                        {
                            itemTopRecords = GLOBALTOP;
                        }

                        if (GLOBALTOP > 0 && itemTopRecords > 0 && itemTopRecords > GLOBALTOP)
                        {
                            itemTopRecords = GLOBALTOP;
                        }

                        if (itemTopRecords > 0)
                        {
                            if (item.RowCount < itemTopRecords)
                            {
                                itemTopRecords = item.RowCount;
                            }
                            stritemTopRecords = $" TOP {itemTopRecords}";
                        }
                        else
                        {
                            itemTopRecords = item.RowCount;
                        }

                        var fields = " *";
                        if (convertCollation)
                        {
                            var sourceTable = tabDictionarySource[item.Name];
                            var selectList = new StringBuilder();
                            if (!tabDictionaryDestination.ContainsKey(item.Name))
                            {
                                fields = " *"; //table not found, will throw an error later
                            }
                            else
                            {
                                var destinationTable = tabDictionaryDestination[item.Name];
                                foreach (Column col in sourceTable.Columns)
                                {
                                    if (!col.Computed)
                                    {
                                        selectList.Append(selectList.Length == 0 ? " " : ",");
                                        if (!string.IsNullOrEmpty(col.Collation))
                                        {
                                            lock (objLock)
                                            {
                                                selectList.Append(col).Append(" COLLATE ").Append(
                                                    destinationTable.Columns[col.Name].Collation).Append(" AS ").Append(col);
                                            }
                                        }
                                        else
                                        {
                                            selectList.Append(col);
                                        }
                                    }
                                }
                                fields = selectList.ToString();
                            }
                        }

                        var sql = $"SELECT{stritemTopRecords}{fields} FROM {item.Name} WITH(NOLOCK) {item.WhereFilter} {item.OrderByFields}";
                        var row = (DataGridViewRow)dataGridView1.Rows[0].Clone();
                        row.SetValues(Properties.Resources.empty, item.Name, sql.Trim(), null, item.HasRelationships.ToString().ToLowerInvariant(), itemTopRecords);
                        CopyRows.Add(row);
                        if (CURRENTTHREAD == 0)
                        {
                            //Prevent "ContextSwitchDeadlock Was Detected" exceptions
                            if ((DateTime.Now - lastRefresh).TotalSeconds > 1)
                            {
                                var calculating = dataGridView1.Rows[0].Cells[1].Value.ToString();
                                calculating += ".";
                                if (calculating.EndsWith("...."))
                                {
                                    calculating = calculating.Replace("....", "");
                                }
                                dataGridView1.Rows[0].Cells[1].Value = calculating;
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
            if (Properties.Settings.Default.ClearDestinationDatabase &&
                ConfigurationManager.AppSettings["DeleteDatabaseDataConfirm"]?.Equals("true", StringComparison.InvariantCultureIgnoreCase) == true &&
                (selectOnlyTables || ConfigurationManager.AppSettings["DeleteDatabaseConfirm"]?.Equals("true", StringComparison.InvariantCultureIgnoreCase) != true) &&
                MessageBox.Show($"The data from database '{SchemaTransfer.DestinationCxInfo()}' is about to be deleted. Continue?",
                    "Database data deletion",
                    MessageBoxButtons.OKCancel) == DialogResult.Cancel)
            {
                Visible = false;
                DialogResult = DialogResult.Cancel;
                Environment.Exit(0);
                return;
            }
            btnNext.Enabled = false;
            Cursor = Cursors.WaitCursor;
            backgroundWorker1.DoWork += backgroundWorker1_DoWork;
            backgroundWorker1.RunWorkerCompleted += backgroundWorker1_RunWorkerCompleted;
            backgroundWorker1.ProgressChanged += backgroundWorker1_ProgressChanged;
            backgroundWorker1.WorkerSupportsCancellation = true;
            backgroundWorker1.WorkerReportsProgress = true;
            label1.Text = $"Copying data in progress from: '{SchemaTransfer.SourceCxInfo()}' to: '{SchemaTransfer.DestinationCxInfo()}'";
            progressBar1.Value = 0;
            backgroundWorker1.RunWorkerAsync();
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
                    currentlyCopying = $"Copying {item.Cells["TOP"].Value} records from: '{SchemaTransfer.SourceCxInfo()} / {tableName.Replace("[", "").Replace("]", "")}' to: '{SchemaTransfer.DestinationCxInfo()}' {currrow}/{dataGridView1.RowCount}";
                    if (item.Cells["TOP"].Value.ToString() != "0")
                    {
                        DataTransfer.TransferData(item.Cells["Table"].Value.ToString(), item.Cells["SqlCommand"].Value.ToString());
                    }
                    //enable table constraints for standalone tables to avoid a single fat transaction at the end
                    if (string.Equals(item.Cells["HasRelationships"].Value.ToString(), "false", StringComparison.InvariantCultureIgnoreCase))
                    {
                        DataTransfer.EnableTableConstraints(tableName);
                    }
                    item.Cells["Status"].Value = Properties.Resources.success;
                    ((Bitmap)item.Cells["Status"].Value).Tag = Constants.OK;
                    item.Cells["Result"].Value = $"{item.Cells["TOP"].Value} records copied";
                }
                catch (Exception exc)
                {
                    item.Cells["Status"].Value = Properties.Resources.failure;
                    ((Bitmap)item.Cells["Status"].Value).Tag = Constants.ERROR;
                    item.Cells["Result"].Value = exc.Message;
                    if (exc.InnerException != null)
                    {
                        item.Cells["Result"].Value = exc.InnerException.Message;
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
                    var multiline = label1.Height - label1.Padding.Top - label1.Padding.Bottom > label1.Font.Size * 2;
                    label1.Top = !multiline ? 10 : 3;
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
                string totalRecs = "";
                if (errorCount == 0)
                {
                    //prevent out of range exception when counting processed records
                    try
                    {
                        totalRecs = $" {CopyList.Sum(t => t.RowCount)} records copied from: '{SchemaTransfer.SourceCxInfo()}' to: '{SchemaTransfer.DestinationCxInfo()}'";
                    }
                    catch { }
                }
                label1.Text = $"Operation completed {(errorCount == 0 ? $"successfully.{totalRecs}" : $"with {errorCount} errors")}" +
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
                    string msgResult;
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
                    btnCopyMessages.Visible = btnCopyMessages.Enabled = true;
                    btnCancel.Enabled = true;
                }
                else
                {
                    btnCopyMessages.Visible = true;
                    autoScrollGrid.CheckState = CheckState.Unchecked;
                    autoScrollGrid.Text = "Show errors only";
                    autoScrollGrid.Left -= 30;
                    MessageBox.Show(label1.Text);
                }
                btnPause.Text = "Start over";
                btnPause.Enabled = true;
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
            Visible = false;
            DialogResult = DialogResult.Abort;
            Environment.Exit(0);
        }

        private void CopyTabledata_Load(object sender, EventArgs e)
        {
            Icon = Icon.FromHandle(Properties.Resources.Clone.Handle);
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
                        var colCounter = 0;
                        foreach (DataGridViewColumn col in dataGridView1.Columns)
                        {
                            if (colCounter < 4)
                            {
                                if (colCounter > 0)
                                {
                                    sb.Append('\t');
                                }
                                colCounter++;
                                sb.Append(col.Name);
                            }
                        }
                    }
                    sb.Append(Environment.NewLine);

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
                                sb.Append(cell.Value == null ? "N/A" : ((Bitmap)cell.Value).Tag?.ToString());
                            }
                            else
                            {
                                sb.Append(cell.EditedFormattedValue?.ToString().Replace(Environment.NewLine, " ").Replace("\t", ""));
                            }
                        }
                        counter++;
                    }
                }
            }
            Clipboard.SetText(sb.ToString());
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            if (btnPause.Text == "Start over")
            {
                DialogResult = DialogResult.Retry;
                Close();
            }
            else if (btnPause.Text == "Pause")
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
            if (autoScrollGrid.Text == "Show errors only")
            {
                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    row.Visible = !autoScrollGrid.Checked ||
                       (!string.IsNullOrEmpty((string)row.Cells["Result"].Value) &&
                       ((Bitmap)row.Cells["Status"].Value).Tag?.ToString() == Constants.ERROR);
                }
            }
        }

        private void dataGridView1_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            //sometimes if the window is resized the datagrid creates new rows for no reason
            //this prevents the default error window from being shown, as it blocks the whole process
        }

        private void dataGridView1_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            var waiting = false;
            if (dataGridView1.Rows[0].Cells[0].Value != null &&
                dataGridView1.Rows[0].Cells[0].Value is Bitmap &&
                (dataGridView1.Rows[0].Cells[0].Value as Bitmap).Tag != null)
            {
                waiting = (dataGridView1.Rows[0].Cells[0].Value as Bitmap)?.Tag.ToString() == Constants.WAITING;
            }

            if (!waiting && e.RowIndex > -1)
            {
                var objError = "";
                if (dataGridView1.Rows[e.RowIndex].Cells[3].Value?.ToString().Contains("records copied") == false)
                {
                    objError = dataGridView1.Rows[e.RowIndex].Cells[3].Value + Environment.NewLine + Environment.NewLine;
                }
                NotepadHelper.ShowMessage(objError + dataGridView1.Rows[e.RowIndex].Cells[2].Value,
                    dataGridView1.Rows[e.RowIndex].Cells[1].Value.ToString().Replace("[", "").Replace("]", ""));
            }
        }
    }
}