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
        private readonly bool AutoRun;
        private bool NextClicked;
        private bool SortByRecords;
        private readonly ContextMenu NodeContextMenu = new ContextMenu();
        private SqlSchemaTable CurrentTable;
        private TreeNode CurrentNode;
        private Dictionary<string, string> WHERECONDITIONS;
        private Dictionary<string, long> TOPROWS;
        private Dictionary<string, string> ORDERBYFIELDS;
        private Point originalLocation = new Point();

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

            var menuOrderBy = new MenuItem
            {
                Text = "Order By"
            };
            menuOrderBy.Click += MenuOrderBy_Click;
            NodeContextMenu.MenuItems.Add(menuOrderBy);

            if (!copyOnlySchema && long.TryParse(ConfigurationManager.AppSettings["GlobalTOP"], out long GLOBALTOP) && GLOBALTOP > 0)
            {
                label1.Text = $"Global TOP is: {GLOBALTOP}  {label1.Text}";
            }

            LoadTreeNodes(SortByRecords);
            AutoRun = autoRun;
        }

        private string FormatCopyData(long ROWCOUNT, long TOP, string WHERE, string ORDERBY)
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

            if (!string.IsNullOrEmpty(ORDERBY))
            {
                extradata += $", {ORDERBY.Trim()}";
            }

            return extradata;
        }

        private void CalculateFilters()
        {
            WHERECONDITIONS = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            TOPROWS = new Dictionary<string, long>(StringComparer.InvariantCultureIgnoreCase);
            ORDERBYFIELDS = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            var filterDataLoading = ConfigurationManager.AppSettings["FilterDataLoading"];
            if (!string.IsNullOrEmpty(filterDataLoading))
            {
                foreach (var filter in (IList<string>)filterDataLoading.Replace(Environment.NewLine, "").Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).ToList())
                {
                    var split = filter.Trim().Split(' ').ToList();
                    var key = AddBrackets(split[0].ToUpperInvariant());
                    if (split.Count > 2 && split[1].Equals("WHERE", StringComparison.InvariantCultureIgnoreCase))
                    {
                        WHERECONDITIONS[key] = string.Join(" ", new[] { "WHERE" }.Concat(new[] { AddBrackets(split.Skip(2).First()) }).Concat(split.Skip(3))).Trim();
                    }
                    else if (split.Count > 2 && split[1].Equals("TOP", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (long.TryParse(split[2], out long toprows))
                        {
                            TOPROWS[key] = toprows;
                        }
                    }
                    else if (split.Count > 2 && split[1].Equals("ORDER BY", StringComparison.InvariantCultureIgnoreCase))
                    {
                        ORDERBYFIELDS[key] = string.Join(" ", split.Skip(2));
                    }
                }
            }
        }

        private string AddBrackets(string item)
        {
            var itemWithBrackets = "";
            foreach (var itemSplitDot in item.Split('.'))
            {
                if (itemWithBrackets != "")
                {
                    itemWithBrackets += ".";
                }
                if (!itemSplitDot.StartsWith("["))
                {
                    itemWithBrackets += "[";
                }
                itemWithBrackets += itemSplitDot;
                if (!itemSplitDot.EndsWith("]"))
                {
                    itemWithBrackets += "]";
                }
            }
            return itemWithBrackets;
        }

        private void LoadTreeNodes(bool sortByRecords)
        {
            var nodesCopy = new TreeNode[treeView1.Nodes.Count];
            treeView1.Nodes.CopyTo(nodesCopy, 0);
            treeView1.Nodes.Clear();
            var root = treeView1.Nodes.Add("All");
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
            }

            //Tables should be shown at the first position
            var sqlSchemaObjects = items.ToList();
            foreach (var currentitemtype in new List<string> { "Table" }.Union(sqlSchemaObjects.Select(i => i.Type).Distinct()))
            {
                var itemsCurrent = sqlSchemaObjects.Where(i => i.Type == currentitemtype).ToList();
                var child = root.Nodes.Add(currentitemtype + $" ({itemsCurrent.Count})");
                child.Checked = true;
                if (sortByRecords && currentitemtype == "Table")
                {
                    itemsCurrent = itemsCurrent.OfType<SqlSchemaTable>().OrderByDescending(t => t.RowCount).ThenBy(s => s.Name).OfType<SqlSchemaObject>().ToList();
                }
                CalculateFilters();
                foreach (var currentitem in itemsCurrent)
                {
                    var tn = child.Nodes.Add(currentitem.Name);
                    //restore previously checked/unchecked items
                    bool? previousCheckedStatus = null;
                    bool? previousCheckedStatusChild = null;
                    if (nodesCopy.Any())
                    {
                        var itemTypeNode = nodesCopy[0].Nodes.OfType<TreeNode>().FirstOrDefault(f => f.Text.StartsWith($"{currentitemtype} "));
                        if (itemTypeNode != default)
                        {
                            var currentItemTypeNode = itemTypeNode.Nodes.OfType<TreeNode>().FirstOrDefault(f => f.Text == currentitem.Name);
                            if (currentItemTypeNode != default)
                            {
                                previousCheckedStatus = currentItemTypeNode.Checked;
                                if (currentItemTypeNode.Nodes.Count == 1)
                                {
                                    previousCheckedStatusChild = currentItemTypeNode.Nodes[0].Checked;
                                }
                            }
                        }
                    }

                    tn.Checked = previousCheckedStatus ?? !CheckIfInList(currentitem.Name, excludeObjectsList);
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

                        ORDERBYFIELDS.TryGetValue(tn.Text, out string ORDERBY);
                        if (!string.IsNullOrEmpty(ORDERBY))
                        {
                            currenttable.OrderByFields = ORDERBY.Trim();
                        }

                        if (!SelectOnlyTables && CloseIfSuccess)
                        {
                            //When copying everything add a "Copy Data" subnode to tables
                            var node = tn.Nodes.Add($"Copy Data{FormatCopyData(currenttable.RowCount, currenttable.TopRecords, currenttable.WhereFilter, currenttable.OrderByFields)}");
                            node.Checked = previousCheckedStatusChild ?? tn.Checked && !CheckIfInList(currentitem.Name, excludeDataLoadingList) && CloseIfSuccess;
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
                            tn.Text += FormatCopyData(currenttable.RowCount, currenttable.TopRecords, currenttable.WhereFilter, currenttable.OrderByFields);
                        }
                    }
                }
                if (child.Nodes.Count == 0)
                {
                    root.Nodes.Remove(child);
                }
            }
            root.ExpandAll();
            treeView1.SelectedNode = treeView1.Nodes[0];
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

        private void MenuWhere_Click(object sender, EventArgs e)
        {
            var defaultValue = CurrentTable.WhereFilter;
            if (new InputBoxValidate("Filter records", "Enter where clause to filter by (empty for all)", icon: Icon)
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
                    CurrentNode.Text = CurrentNode.Text.Substring(0, CurrentNode.Text.IndexOf(" (", StringComparison.Ordinal));
                }

                CurrentNode.Text += FormatCopyData(CurrentTable.RowCount, CurrentTable.TopRecords, CurrentTable.WhereFilter, CurrentTable.OrderByFields);
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
                    CurrentNode.Text = CurrentNode.Text.Substring(0, CurrentNode.Text.IndexOf(" (", StringComparison.Ordinal));
                }

                CurrentNode.Text += FormatCopyData(CurrentTable.RowCount, CurrentTable.TopRecords, CurrentTable.WhereFilter, CurrentTable.OrderByFields);
            }
        }

        private void MenuOrderBy_Click(object sender, EventArgs e)
        {
            var defaultValue = CurrentTable.OrderByFields;
            if (new InputBoxValidate("Sort records", "Enter order by clause to sort records by", icon: Icon)
                    .ShowDialog(ref defaultValue) == DialogResult.OK)
            {
                if (string.IsNullOrWhiteSpace(defaultValue))
                {
                    CurrentTable.OrderByFields = "";
                }
                else if (!defaultValue.StartsWith("ORDER BY", true, System.Globalization.CultureInfo.DefaultThreadCurrentCulture))
                {
                    defaultValue = $"ORDER BY {defaultValue}";
                }

                CurrentTable.OrderByFields = defaultValue;
                if (CurrentNode.Text.Contains(" ("))
                {
                    CurrentNode.Text = CurrentNode.Text.Substring(0, CurrentNode.Text.IndexOf(" (", StringComparison.Ordinal));
                }

                CurrentNode.Text += FormatCopyData(CurrentTable.RowCount, CurrentTable.TopRecords, CurrentTable.WhereFilter, CurrentTable.OrderByFields);
            }
        }

        private bool CheckIfInList(string item, IList<string> lstExclude)
        {
            var includedinlist = lstExclude.Any(s => s.Equals(item, StringComparison.OrdinalIgnoreCase));
            if (!includedinlist)
            {
                foreach (var itmExclude in lstExclude.Where(i => i.IndexOf("*", StringComparison.Ordinal) >= 0))
                {
                    if (item.StartsWith(itmExclude.Substring(0, itmExclude.IndexOf("*", StringComparison.Ordinal)), StringComparison.InvariantCultureIgnoreCase))
                    {
                        includedinlist = true;
                        break;
                    }
                }
            }
            return includedinlist;
        }

        protected void SelectNodes(TreeNode root, IList<string> startsWith)
        {
            if (startsWith?.Any() != true)
            {
                return;
            }

            foreach (var node in root.Nodes)
            {
                if (node is TreeNode nodetv)
                {
                    if (nodetv.Text == "Copy Data")
                    {
                        nodetv.Checked = nodetv.Checked;
                        return;
                    }
                    nodetv.Checked = false;
                    startsWith.ToList().ForEach(s => nodetv.Checked = nodetv.Checked || nodetv.Text.ToUpperInvariant().StartsWith($"{s.ToUpperInvariant()}."));
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
                        else if (item.Tag is SqlSchemaTable table)
                        {
                            checkedItems.Add(table.Name);
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
                                checkedDataTables.Add(item.Tag is SqlSchemaTable table ? table.Name : item.Text);
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
                Properties.Settings.Default.IncrementalDataCopy = !Properties.Settings.Default.DeleteDestinationTables &&
                                                                  incrementalDataCopy.Checked;
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
                if (originalLocation.X == 0)
                {
                    originalLocation.X = Left;
                }
                if (originalLocation.Y == 0)
                {
                    originalLocation.Y = Top;
                }
                var copyschema = new CopySchema(transfer, SelectedObjects.ToList(), CloseIfSuccess,
                    treeView1.Nodes[0].Checked, disableNotForReplication.Checked);
                copyschema.Location = new Point(originalLocation.X + ((copyschema.Width - Width) / 2), originalLocation.Y + ((copyschema.Height - Height) / 2));
                DialogResult = copyschema.ShowDialog();
                if (DialogResult == DialogResult.Abort)
                {
                    Environment.Exit(0);
                }
            }
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            Visible = false;
            DialogResult = DialogResult.Abort;
            Environment.Exit(0);
        }

        private void btnSelectSchema_Click(object sender, EventArgs e)
        {
            var defaultValue = "";
            if (new InputBoxValidate("Schemas", "Enter schema names (separated by \",\" if more than one)", icon: Icon)
                    .ShowDialog(ref defaultValue) == DialogResult.OK)
            {
                foreach (var node in treeView1.Nodes)
                {
                    if (node is TreeNode treeNode)
                    {
                        SelectNodes(treeNode, defaultValue.Split(',').Where(s => !string.IsNullOrEmpty(s)).ToList());
                    }
                }
            }
        }

        private void ChooseSchemas_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!NextClicked && DialogResult != DialogResult.Retry)
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

        private void clearDestinationDatabase_CheckedChanged(object sender, EventArgs e)
        {
            dropAndRecreateObjects.Enabled = !clearDestinationDatabase.Checked;
        }

        private void btnBack_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Retry;
            Close();
        }

        private void ChooseSchemas_Shown(object sender, EventArgs e)
        {
            originalLocation.X = Location.X;
            originalLocation.Y = Location.Y;
        }
    }
}
