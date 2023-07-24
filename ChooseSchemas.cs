using Sql2SqlCloner.Components;
using Sql2SqlCloner.Core.DataTransfer;
using Sql2SqlCloner.Core.SchemaTransfer;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Sql2SqlCloner
{
    public partial class ChooseSchemas : Form
    {
        public IEnumerable<SqlSchemaObject> SelectedObjects { get; private set; }
        private IEnumerable<SqlSchemaObject> items;
        private readonly SqlSchemaTransfer transfer;
        private readonly bool CloseIfSuccess;
        private readonly bool SelectOnlyTables;
        private readonly bool CopyOnlySchema;
        private readonly bool AutoRun = false;
        private bool NextClicked;
        private bool SortByRecords;
        private readonly ContextMenu NodeContextMenu = new ContextMenu();
        private SqlSchemaTable CurrentTable;
        private TreeNode CurrentNode;
        private Dictionary<string, string> WHERECONDITIONS;
        private Dictionary<string, long> TOPROWS;

        public ChooseSchemas(SqlSchemaTransfer transferSchema, bool closeIfSuccess, bool selectOnlyTables, bool copyOnlySchema, bool autoRun)
        {
            if (transferSchema == null)
            {
                MessageBox.Show("Schema to transfer is null");
                Environment.Exit(0);
                return;
            }
            InitializeComponent();
            if (transferSchema.SourceObjects?.Any() != true)
            {
                MessageBox.Show("No items to copy, exiting");
                Environment.Exit(0);
                DialogResult = DialogResult.Abort;
                return;
            }

            CloseIfSuccess = closeIfSuccess;
            CopyOnlySchema = copyOnlySchema;
            SelectOnlyTables = selectOnlyTables;
            transfer = transferSchema;
            items = transferSchema.SourceObjects.ToList();
            btnSortNodesBy.Visible = !copyOnlySchema;
            optionsBoxSchema.Visible = !selectOnlyTables;
            optionsBoxData.Visible = selectOnlyTables;

            var menuTop = new MenuItem
            {
                Text = "Top records"
            };
            menuTop.Click += MenuTop_Click;
            NodeContextMenu.MenuItems.Add(menuTop);

            var menuWhere = new MenuItem
            {
                Text = "Where filter"
            };
            menuWhere.Click += MenuWhere_Click;
            NodeContextMenu.MenuItems.Add(menuWhere);
            if (!copyOnlySchema && long.TryParse(ConfigurationManager.AppSettings["GlobalTOP"], out long GLOBALTOP) && GLOBALTOP > 0)
            {
                label1.Text = $"Global TOP is: {GLOBALTOP}  {label1.Text}";
            }

            LoadTreeNodes(SortByRecords);
            AutoRun = autoRun;
        }

        private string FormatCopyData(long ROWCOUNT, long TOP, string WHERE)
        {
            if (CopyOnlySchema)
            {
                return "";
            }

            var extradata = $" ({ROWCOUNT:N0} records)";
            if (TOP > 0)
            {
                extradata += $", TOP {TOP}";
            }

            if (!string.IsNullOrEmpty(WHERE))
            {
                extradata += $", {WHERE.Trim()}";
            }

            return extradata;
        }

        private void CalculateFilters()
        {
            WHERECONDITIONS = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            TOPROWS = new Dictionary<string, long>(StringComparer.InvariantCultureIgnoreCase);
            var filterDataLoadingList = new List<string>();
            var filterDataLoading = ConfigurationManager.AppSettings["FilterDataLoading"];
            if (!string.IsNullOrEmpty(filterDataLoading))
            {
                filterDataLoadingList = filterDataLoading.Replace(Environment.NewLine, "").Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                foreach (var filter in filterDataLoadingList)
                {
                    var split = filter.Trim().Split(' ');
                    var key = split[0].ToUpperInvariant();
                    if (split.Length > 2 && split[1].Equals("WHERE", StringComparison.InvariantCultureIgnoreCase))
                    {
                        WHERECONDITIONS[key] = string.Join(" ", split.Skip(1).ToArray()).Trim();
                    }
                    else if (split.Length > 2 && split[1].Equals("TOP", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (long.TryParse(split[2], out long toprows))
                        {
                            TOPROWS[key] = toprows;
                        }
                    }
                }
            }

            var lstAddNobracketWHERE = new Dictionary<string, string>();
            foreach (var item in WHERECONDITIONS)
            {
                var nobracket = item.Key.Replace("[", "").Replace("]", "");
                if (nobracket != item.Key)
                {
                    lstAddNobracketWHERE[nobracket] = item.Value;
                }
            }
            lstAddNobracketWHERE.ToList().ForEach(d => WHERECONDITIONS[d.Key] = d.Value);

            var lstAddNobracketTOP = new Dictionary<string, long>();
            foreach (var item in TOPROWS)
            {
                var nobracket = item.Key.Replace("[", "").Replace("]", "");
                if (nobracket != item.Key)
                {
                    lstAddNobracketTOP[nobracket] = item.Value;
                }
            }
            lstAddNobracketTOP.ToList().ForEach(d => TOPROWS[d.Key] = d.Value);
        }

        private void LoadTreeNodes(bool sortByRecords)
        {
            var nodes = treeView1.Nodes;
            nodes.Clear();
            var root = nodes.Add("All");
            TreeNode tn = root;

            var excludeObjectsList = new List<string>();
            var excludeDataLoadingList = new List<string>();
            try
            {
                var excludeObjects = ConfigurationManager.AppSettings["ExcludeObjects"];
                if (!string.IsNullOrEmpty(excludeObjects))
                {
                    excludeObjectsList = excludeObjects.Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).
                        Select(s => s.Replace("[", "").Replace("]", "").Trim()).ToList();
                }
                var excludeDataLoading = ConfigurationManager.AppSettings["ExcludeDataLoading"];
                if (!string.IsNullOrEmpty(excludeDataLoading))
                {
                    excludeDataLoadingList = excludeDataLoading.Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).
                        Select(s => s.Replace("[", "").Replace("]", "").Trim()).ToList();
                }
            }
            catch { }
            root.Checked = true;
            if (SelectOnlyTables)
            {
                items = items.Where(i => i.Type == "Table");
                btnNext.Text = "Continue";
            }

            //Tables should be shown at the first position
            foreach (var currentitemtype in new List<string>() { "Table" }.Union(items.Select(i => i.Type).Distinct()))
            {
                var itemsCurrent = items.Where(i => i.Type == currentitemtype).ToList();
                var child = root.Nodes.Add(currentitemtype + $" ({itemsCurrent.Count})");
                child.Checked = true;
                if (sortByRecords && currentitemtype == "Table")
                {
                    itemsCurrent = itemsCurrent.OfType<SqlSchemaTable>().OrderByDescending(t => t.RowCount).ThenBy(s => s.Name).OfType<SqlSchemaObject>().ToList();
                }
                CalculateFilters();
                foreach (var currentitem in itemsCurrent)
                {
                    tn = child.Nodes.Add(currentitem.Name);
                    tn.Checked = !CheckIfInList(currentitem.Name, excludeObjectsList);
                    if (child.Text.StartsWith("Table"))
                    {
                        var currenttable = currentitem as SqlSchemaTable;
                        WHERECONDITIONS.TryGetValue(tn.Text, out string WHERE);
                        if (!string.IsNullOrEmpty(WHERE))
                        {
                            currenttable.WhereFilter = WHERE.Trim();
                        }

                        TOPROWS.TryGetValue(tn.Text, out long TOP);
                        if (TOP > 0)
                        {
                            currenttable.TopRecords = TOP;
                        }

                        if (!SelectOnlyTables && CloseIfSuccess)
                        {
                            //When copying everything add a "Copy Data" subnode to tables
                            var node = tn.Nodes.Add($"Copy Data{FormatCopyData(currenttable.RowCount, currenttable.TopRecords, currenttable.WhereFilter)}");
                            node.Checked = tn.Checked && !CheckIfInList(currentitem.Name, excludeDataLoadingList) && CloseIfSuccess;
                            node.Tag = currentitem;
                        }
                        else
                        {
                            if (SelectOnlyTables && CheckIfInList(currentitem.Name, excludeDataLoadingList))
                            {
                                tn.Checked = false;
                            }
                            if (!CopyOnlySchema)
                            {
                                tn.Checked = tn.Checked && !CheckIfInList(currentitem.Name, excludeDataLoadingList);
                            }
                            tn.Tag = currentitem;
                            tn.Text += FormatCopyData(currenttable.RowCount, currenttable.TopRecords, currenttable.WhereFilter);
                        }
                    }
                }
                if (child.Nodes.Count == 0)
                {
                    root.Nodes.Remove(child);
                }
            }
            root.ExpandAll();
            treeView1.SelectedNode = nodes[0];
        }

        private void treeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if ((!CopyOnlySchema && e.Node.Tag != null && e.Node.Tag is SqlSchemaTable) ||
                (e.Node.Nodes.Count > 0 && e.Node.Nodes[0].Tag is SqlSchemaTable))
            {
                CurrentNode = e.Node;
                if (CurrentNode.Tag == null)
                {
                    CurrentNode = CurrentNode.Nodes[0];
                }
                CurrentTable = CurrentNode.Tag as SqlSchemaTable;
                if (e.Button == MouseButtons.Right)
                {
                    NodeContextMenu.Show(treeView1, e.Location);
                }
            }
        }

        private void MenuTop_Click(object sender, EventArgs e)
        {
            string defaultValue = CurrentTable.TopRecords.ToString();
            if (new InputBoxValidate("Top records", "Enter top records to retrieve (0 for all)", true, icon: Icon)
                    .ShowDialog(ref defaultValue) == DialogResult.OK)
            {
                CurrentTable.TopRecords = long.Parse(defaultValue);
                if (CurrentTable.TopRecords < 0)
                {
                    CurrentTable.TopRecords = 0;
                }

                if (CurrentNode.Text.Contains(" ("))
                {
                    CurrentNode.Text = CurrentNode.Text.Substring(0, CurrentNode.Text.IndexOf(" ("));
                }

                CurrentNode.Text += FormatCopyData(CurrentTable.RowCount, CurrentTable.TopRecords, CurrentTable.WhereFilter);
            }
        }

        private void MenuWhere_Click(object sender, EventArgs e)
        {
            string defaultValue = CurrentTable.WhereFilter;
            if (new InputBoxValidate("Filter records", "Enter where clause to filter by (empty for all)", false, icon: Icon)
                    .ShowDialog(ref defaultValue) == DialogResult.OK)
            {
                if (string.IsNullOrWhiteSpace(defaultValue))
                {
                    CurrentTable.WhereFilter = "";
                }
                else if (!defaultValue.StartsWith("WHERE", true, System.Globalization.CultureInfo.DefaultThreadCurrentCulture))
                {
                    defaultValue = $"WHERE {defaultValue}";
                }

                CurrentTable.WhereFilter = defaultValue;
                if (CurrentNode.Text.Contains(" ("))
                {
                    CurrentNode.Text = CurrentNode.Text.Substring(0, CurrentNode.Text.IndexOf(" ("));
                }

                CurrentNode.Text += FormatCopyData(CurrentTable.RowCount, CurrentTable.TopRecords, CurrentTable.WhereFilter);
            }
        }

        private bool CheckIfInList(string item, IList<string> lstExclude)
        {
            var includedinlist = lstExclude.Any(s => s.Equals(item, StringComparison.OrdinalIgnoreCase));
            if (!includedinlist)
            {
                foreach (var itmExclude in lstExclude.Where(i => i.IndexOf("*") >= 0))
                {
                    if (item.StartsWith(itmExclude.Substring(0, itmExclude.IndexOf("*")), StringComparison.InvariantCultureIgnoreCase))
                    {
                        includedinlist = true;
                        break;
                    }
                }
            }
            return includedinlist;
        }

        protected void SelectNodes(TreeNode root, List<string> startsWith)
        {
            if (startsWith?.Any() != true)
            {
                return;
            }

            foreach (var node in root.Nodes)
            {
                if (node is TreeNode)
                {
                    var nodetv = node as TreeNode;

                    if (nodetv.Text == "Copy Data")
                    {
                        nodetv.Checked = (node as TreeNode)?.Checked ?? false;
                        return;
                    }
                    nodetv.Checked = false;
                    startsWith.ForEach(s => nodetv.Checked = nodetv.Checked || nodetv.Text.ToUpperInvariant().StartsWith($"{s.ToUpperInvariant()}."));

                    if (nodetv.Checked && nodetv.Nodes.Count > 0 && nodetv.Nodes[0].Tag != null && nodetv.Nodes[0].Tag is SqlSchemaTable)
                    {
                        nodetv.Nodes[0].Checked = true;
                    }
                    else if (nodetv.Nodes.Count > 0)
                    {
                        SelectNodes(nodetv, startsWith);
                    }
                }
            }
        }

        private void treeView1_AfterCheck(object sender, TreeViewEventArgs e)
        {
            foreach (TreeNode node in e.Node.Nodes)
            {
                node.Checked = e.Node.Checked;
            }
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            NextClicked = true;
            items.ToList().ForEach(i => { i.Status = null; i.Error = string.Empty; });

            var checkedItems = new List<string>();
            var checkedDataTables = new List<string>();
            foreach (TreeNode type in treeView1.Nodes[0].Nodes)
            {
                foreach (TreeNode item in type.Nodes)
                {
                    if (item.Checked || item.LastNode?.Checked == true)
                    {
                        if (item.Nodes.Count > 0)
                        {
                            checkedItems.Add((item.Nodes[0].Tag as SqlSchemaTable)?.Name);
                        }
                        else if (item.Tag != null && item.Tag is SqlSchemaTable)
                        {
                            checkedItems.Add((item.Tag as SqlSchemaTable)?.Name);
                        }
                        else
                        {
                            checkedItems.Add(item.Text);
                        }
                        if (item.Parent.Text.StartsWith("Table") && (SelectOnlyTables || (item.Nodes.Count > 0 && item.Nodes[0].Checked)))
                        {
                            //this table's data should be copied
                            if (item.Nodes.Count > 0)
                            {
                                checkedDataTables.Add((item.Nodes[0].Tag as SqlSchemaTable)?.Name);
                            }
                            else
                            {
                                if (item.Tag != null && item.Tag is SqlSchemaTable)
                                {
                                    checkedDataTables.Add((item.Tag as SqlSchemaTable)?.Name);
                                }
                                else
                                {
                                    checkedDataTables.Add(item.Text);
                                }
                            }
                        }
                    }
                }
            }

            if (checkedItems.Count == 0)
            {
                MessageBox.Show("Select at least one object to proceed further");
                return;
            }

            SelectedObjects = items.Join(checkedItems, i => i.Name, j => j, (i, _) => i).Distinct().ToList();
            foreach (var table in checkedDataTables)
            {
                //set the "copydata" bit to true where needed
                SelectedObjects.FirstOrDefault(s => s.Type == "Table" && s.Name == table).CopyData = true;
            }
            Hide();

            if (SelectOnlyTables)
            {
                DialogResult = DialogResult.OK;
                Properties.Settings.Default.CopyCollation = (SqlCollationAction)copyCollation.SelectedIndex;
                Properties.Settings.Default.DeleteDestinationTables = deleteDestinationTables.Checked;
                if (Properties.Settings.Default.DeleteDestinationTables)
                {
                    Properties.Settings.Default.IncrementalDataCopy = false;
                }
                else
                {
                    Properties.Settings.Default.IncrementalDataCopy = incrementalDataCopy.Checked;
                }
                Properties.Settings.Default.Save();
            }
            else
            {
                Properties.Settings.Default.CopyConstraints = copyConstraints.Checked;
                if (copyFullText.Enabled)
                {
                    Properties.Settings.Default.CopyFullText = copyFullText.Checked;
                }

                Properties.Settings.Default.DropAndRecreateObjects = dropAndRecreateObjects.Checked;
                Properties.Settings.Default.CopySecurity = copySecurity.Checked;
                Properties.Settings.Default.CopyExtendedProperties = copyExtendedProperties.Checked;
                Properties.Settings.Default.CopyPermissions = copyPermissions.Checked;
                Properties.Settings.Default.StopIfErrors = stopIfErrors.Checked;
                Properties.Settings.Default.ClearDestinationDatabase = clearDestinationDatabase.Checked;
                Properties.Settings.Default.CopyCollation = (SqlCollationAction)copyCollation.SelectedIndex;
                Properties.Settings.Default.DisableNotForReplication = disableNotForReplication.Checked;
                Properties.Settings.Default.IgnoreFileGroup = ignoreFileGroup.Checked;
                Properties.Settings.Default.Save();

                DialogResult = new CopySchema(transfer, SelectedObjects.ToList(), CloseIfSuccess,
                    treeView1.Nodes[0].Checked, disableNotForReplication.Checked).ShowDialog();
                if (DialogResult == DialogResult.Abort)
                {
                    Environment.Exit(0);
                }
            }
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Abort;
            Environment.Exit(0);
        }

        private void btnSelectSchema_Click(object sender, EventArgs e)
        {
            var defaultValue = "";
            if (new InputBoxValidate("Schemas", "Enter schema names (separated by \",\" if more than one)", false, icon: Icon)
                    .ShowDialog(ref defaultValue) == DialogResult.OK)
            {
                foreach (var node in treeView1.Nodes)
                {
                    if (node is TreeNode)
                    {
                        SelectNodes(node as TreeNode, defaultValue.Split(',').Where(s => !string.IsNullOrEmpty(s)).ToList());
                    }
                }
            }
        }

        private void ChooseSchemas_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!NextClicked)
            {
                DialogResult = DialogResult.Abort;
                Environment.Exit(0);
            }
        }

        private void btnSortNodesBy_Click(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            Application.DoEvents();
            SortByRecords = !SortByRecords;
            btnSortNodesBy.Text = $"Sort by {(SortByRecords ? "name" : "records")}";
            LoadTreeNodes(SortByRecords);
            Cursor = Cursors.Default;
        }

        private void ChooseSchemas_Load(object sender, EventArgs e)
        {
            Enum.GetNames(typeof(SqlCollationAction)).ToList().ForEach(n => copyCollation.Items.Add(n.Replace("_", " ")));
            Icon = Icon.FromHandle(Properties.Resources.Clone.Handle);
            copyConstraints.Checked = Properties.Settings.Default.CopyConstraints;
            copyFullText.Checked = Properties.Settings.Default.CopyFullText;
            dropAndRecreateObjects.Checked = Properties.Settings.Default.DropAndRecreateObjects;
            copySecurity.Checked = Properties.Settings.Default.CopySecurity;
            deleteDestinationTables.Checked = Properties.Settings.Default.DeleteDestinationTables;
            copyExtendedProperties.Checked = Properties.Settings.Default.CopyExtendedProperties;
            copyPermissions.Checked = Properties.Settings.Default.CopyPermissions;
            stopIfErrors.Checked = Properties.Settings.Default.StopIfErrors;
            clearDestinationDatabase.Checked = Properties.Settings.Default.ClearDestinationDatabase;
            copyCollation.SelectedIndex = (int)Properties.Settings.Default.CopyCollation;
            disableNotForReplication.Checked = Properties.Settings.Default.DisableNotForReplication;
            ignoreFileGroup.Checked = Properties.Settings.Default.IgnoreFileGroup;
            incrementalDataCopy.Checked = Properties.Settings.Default.IncrementalDataCopy;
            incrementalDataCopy.Enabled = !deleteDestinationTables.Checked;

            if (AutoRun)
            {
                btnNext_Click(sender, e);
            }
        }

        private void copyConstraints_CheckedChanged(object sender, EventArgs e)
        {
            copyFullText.Enabled = copyConstraints.Checked;
        }

        private void deleteDestinationTables_CheckedChanged(object sender, EventArgs e)
        {
            incrementalDataCopy.Enabled = !deleteDestinationTables.Checked;
        }
    }
}
