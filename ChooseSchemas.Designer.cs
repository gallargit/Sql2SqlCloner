using Sql2SqlCloner.Components;

namespace Sql2SqlCloner
{
	partial class ChooseSchemas
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
            this.components = new System.ComponentModel.Container();
            this.btnNext = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.btnSelectSchema = new System.Windows.Forms.Button();
            this.btnSortNodesBy = new System.Windows.Forms.Button();
            this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
            this.label2 = new System.Windows.Forms.Label();
            this.clearDestinationDatabase = new System.Windows.Forms.CheckBox();
            this.dropAndRecreateObjects = new System.Windows.Forms.CheckBox();
            this.copyConstraints = new System.Windows.Forms.CheckBox();
            this.copyFullText = new System.Windows.Forms.CheckBox();
            this.copyExtendedProperties = new System.Windows.Forms.CheckBox();
            this.copySecurity = new System.Windows.Forms.CheckBox();
            this.copyPermissions = new System.Windows.Forms.CheckBox();
            this.stopIfErrors = new System.Windows.Forms.CheckBox();
            this.treeView1 = new Sql2SqlCloner.Components.TriStateTreeView();
            this.lblCopyCollation = new System.Windows.Forms.Label();
            this.copyCollation = new System.Windows.Forms.ComboBox();
            this.flowLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnNext
            // 
            this.btnNext.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnNext.Location = new System.Drawing.Point(512, 276);
            this.btnNext.Name = "btnNext";
            this.btnNext.Size = new System.Drawing.Size(75, 23);
            this.btnNext.TabIndex = 10;
            this.btnNext.Text = "Next";
            this.btnNext.UseVisualStyleBackColor = true;
            this.btnNext.Click += new System.EventHandler(this.btnNext_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(597, 276);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 11;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(9, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(181, 13);
            this.label1.TabIndex = 15;
            this.label1.Text = "Select the list of SQL objects to copy";
            // 
            // btnSelectSchema
            // 
            this.btnSelectSchema.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.btnSelectSchema.Location = new System.Drawing.Point(408, 2);
            this.btnSelectSchema.Name = "btnSelectSchema";
            this.btnSelectSchema.Size = new System.Drawing.Size(93, 26);
            this.btnSelectSchema.TabIndex = 13;
            this.btnSelectSchema.Text = "Select schemas";
            this.btnSelectSchema.UseVisualStyleBackColor = true;
            this.btnSelectSchema.Click += new System.EventHandler(this.btnSelectSchema_Click);
            // 
            // btnSortNodesBy
            // 
            this.btnSortNodesBy.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.btnSortNodesBy.Location = new System.Drawing.Point(315, 2);
            this.btnSortNodesBy.Name = "btnSortNodesBy";
            this.btnSortNodesBy.Size = new System.Drawing.Size(87, 26);
            this.btnSortNodesBy.TabIndex = 12;
            this.btnSortNodesBy.Text = "Sort by records";
            this.btnSortNodesBy.UseVisualStyleBackColor = true;
            this.btnSortNodesBy.Click += new System.EventHandler(this.btnSortNodesBy_Click);
            // 
            // flowLayoutPanel1
            // 
            this.flowLayoutPanel1.Controls.Add(this.label2);
            this.flowLayoutPanel1.Controls.Add(this.clearDestinationDatabase);
            this.flowLayoutPanel1.Controls.Add(this.dropAndRecreateObjects);
            this.flowLayoutPanel1.Controls.Add(this.copyConstraints);
            this.flowLayoutPanel1.Controls.Add(this.copyFullText);
            this.flowLayoutPanel1.Controls.Add(this.copyExtendedProperties);
            this.flowLayoutPanel1.Controls.Add(this.copySecurity);
            this.flowLayoutPanel1.Controls.Add(this.copyPermissions);
            this.flowLayoutPanel1.Controls.Add(this.stopIfErrors);
            this.flowLayoutPanel1.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.flowLayoutPanel1.Location = new System.Drawing.Point(506, 9);
            this.flowLayoutPanel1.Name = "flowLayoutPanel1";
            this.flowLayoutPanel1.Size = new System.Drawing.Size(177, 180);
            this.flowLayoutPanel1.TabIndex = 17;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, ((System.Drawing.FontStyle)((System.Drawing.FontStyle.Bold | System.Drawing.FontStyle.Underline))), System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(3, 0);
            this.label2.Margin = new System.Windows.Forms.Padding(3, 0, 3, 6);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(50, 13);
            this.label2.TabIndex = 21;
            this.label2.Text = "Options";
            // 
            // clearDestinationDatabase
            // 
            this.clearDestinationDatabase.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.clearDestinationDatabase.AutoSize = true;
            this.clearDestinationDatabase.Location = new System.Drawing.Point(1, 20);
            this.clearDestinationDatabase.Margin = new System.Windows.Forms.Padding(1);
            this.clearDestinationDatabase.Name = "clearDestinationDatabase";
            this.clearDestinationDatabase.Size = new System.Drawing.Size(151, 17);
            this.clearDestinationDatabase.TabIndex = 1;
            this.clearDestinationDatabase.Text = "Clear destination database";
            this.clearDestinationDatabase.UseVisualStyleBackColor = true;
            // 
            // dropAndRecreateObjects
            // 
            this.dropAndRecreateObjects.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.dropAndRecreateObjects.AutoSize = true;
            this.dropAndRecreateObjects.Location = new System.Drawing.Point(1, 39);
            this.dropAndRecreateObjects.Margin = new System.Windows.Forms.Padding(1);
            this.dropAndRecreateObjects.Name = "dropAndRecreateObjects";
            this.dropAndRecreateObjects.Size = new System.Drawing.Size(86, 17);
            this.dropAndRecreateObjects.TabIndex = 2;
            this.dropAndRecreateObjects.Text = "Drop if exists";
            this.dropAndRecreateObjects.UseVisualStyleBackColor = true;
            // 
            // copyConstraints
            // 
            this.copyConstraints.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.copyConstraints.AutoSize = true;
            this.copyConstraints.Checked = true;
            this.copyConstraints.CheckState = System.Windows.Forms.CheckState.Checked;
            this.copyConstraints.Location = new System.Drawing.Point(1, 58);
            this.copyConstraints.Margin = new System.Windows.Forms.Padding(1);
            this.copyConstraints.Name = "copyConstraints";
            this.copyConstraints.Size = new System.Drawing.Size(135, 17);
            this.copyConstraints.TabIndex = 3;
            this.copyConstraints.Text = "Copy keys and indexes";
            this.copyConstraints.UseVisualStyleBackColor = true;
            this.copyConstraints.CheckedChanged += new System.EventHandler(this.copyConstraints_CheckedChanged);
            // 
            // copyFullText
            // 
            this.copyFullText.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.copyFullText.AutoSize = true;
            this.copyFullText.Checked = true;
            this.copyFullText.CheckState = System.Windows.Forms.CheckState.Checked;
            this.copyFullText.Location = new System.Drawing.Point(1, 77);
            this.copyFullText.Margin = new System.Windows.Forms.Padding(1);
            this.copyFullText.Name = "copyFullText";
            this.copyFullText.Size = new System.Drawing.Size(129, 17);
            this.copyFullText.TabIndex = 4;
            this.copyFullText.Text = "Copy FullText indexes";
            this.copyFullText.UseVisualStyleBackColor = true;
            // 
            // copyExtendedProperties
            // 
            this.copyExtendedProperties.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.copyExtendedProperties.AutoSize = true;
            this.copyExtendedProperties.Checked = true;
            this.copyExtendedProperties.CheckState = System.Windows.Forms.CheckState.Checked;
            this.copyExtendedProperties.Location = new System.Drawing.Point(1, 96);
            this.copyExtendedProperties.Margin = new System.Windows.Forms.Padding(1);
            this.copyExtendedProperties.Name = "copyExtendedProperties";
            this.copyExtendedProperties.Size = new System.Drawing.Size(146, 17);
            this.copyExtendedProperties.TabIndex = 5;
            this.copyExtendedProperties.Text = "Copy extended properties";
            this.copyExtendedProperties.UseVisualStyleBackColor = true;
            // 
            // copySecurity
            // 
            this.copySecurity.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.copySecurity.AutoSize = true;
            this.copySecurity.Location = new System.Drawing.Point(1, 115);
            this.copySecurity.Margin = new System.Windows.Forms.Padding(1);
            this.copySecurity.Name = "copySecurity";
            this.copySecurity.Size = new System.Drawing.Size(89, 17);
            this.copySecurity.TabIndex = 6;
            this.copySecurity.Text = "Copy security";
            this.copySecurity.UseVisualStyleBackColor = true;
            // 
            // copyPermissions
            // 
            this.copyPermissions.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.copyPermissions.AutoSize = true;
            this.copyPermissions.Checked = true;
            this.copyPermissions.CheckState = System.Windows.Forms.CheckState.Checked;
            this.copyPermissions.Location = new System.Drawing.Point(1, 134);
            this.copyPermissions.Margin = new System.Windows.Forms.Padding(1);
            this.copyPermissions.Name = "copyPermissions";
            this.copyPermissions.Size = new System.Drawing.Size(107, 17);
            this.copyPermissions.TabIndex = 7;
            this.copyPermissions.Text = "Copy permissions";
            this.copyPermissions.UseVisualStyleBackColor = true;
            // 
            // stopIfErrors
            // 
            this.stopIfErrors.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.stopIfErrors.AutoSize = true;
            this.stopIfErrors.Checked = true;
            this.stopIfErrors.CheckState = System.Windows.Forms.CheckState.Checked;
            this.stopIfErrors.Location = new System.Drawing.Point(1, 153);
            this.stopIfErrors.Margin = new System.Windows.Forms.Padding(1);
            this.stopIfErrors.Name = "stopIfErrors";
            this.stopIfErrors.Size = new System.Drawing.Size(85, 17);
            this.stopIfErrors.TabIndex = 9;
            this.stopIfErrors.Text = "Stop if errors";
            this.stopIfErrors.UseVisualStyleBackColor = true;
            // 
            // treeView1
            // 
            this.treeView1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.treeView1.Location = new System.Drawing.Point(11, 28);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(490, 271);
            this.treeView1.TabIndex = 0;
            this.treeView1.TriStateStyleProperty = Sql2SqlCloner.Components.TriStateTreeView.TriStateStyles.Standard;
            this.treeView1.AfterCheck += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterCheck);
            this.treeView1.NodeMouseClick += new System.Windows.Forms.TreeNodeMouseClickEventHandler(this.treeView1_NodeMouseClick);
            // 
            // lblCopyCollation
            // 
            this.lblCopyCollation.AutoSize = true;
            this.lblCopyCollation.Location = new System.Drawing.Point(507, 194);
            this.lblCopyCollation.Name = "lblCopyCollation";
            this.lblCopyCollation.Size = new System.Drawing.Size(50, 13);
            this.lblCopyCollation.TabIndex = 22;
            this.lblCopyCollation.Text = "Collation:";
            // 
            // copyCollation
            // 
            this.copyCollation.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.copyCollation.FormattingEnabled = true;
            this.copyCollation.ItemHeight = 13;
            this.copyCollation.Location = new System.Drawing.Point(506, 209);
            this.copyCollation.Margin = new System.Windows.Forms.Padding(2);
            this.copyCollation.Name = "copyCollation";
            this.copyCollation.Size = new System.Drawing.Size(172, 21);
            this.copyCollation.TabIndex = 21;
            // 
            // ChooseSchemas
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(684, 311);
            this.Controls.Add(this.lblCopyCollation);
            this.Controls.Add(this.copyCollation);
            this.Controls.Add(this.flowLayoutPanel1);
            this.Controls.Add(this.btnSortNodesBy);
            this.Controls.Add(this.btnSelectSchema);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnNext);
            this.Controls.Add(this.treeView1);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ChooseSchemas";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Sql2SqlCloner - Schema";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ChooseSchemas_FormClosing);
            this.Load += new System.EventHandler(this.ChooseSchemas_Load);
            this.flowLayoutPanel1.ResumeLayout(false);
            this.flowLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion

		private TriStateTreeView treeView1;
		private System.Windows.Forms.Button btnNext;
		private System.Windows.Forms.Button btnCancel;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Button btnSelectSchema;
		private System.Windows.Forms.Button btnSortNodesBy;
		private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
		private System.Windows.Forms.CheckBox copyConstraints;
		private System.Windows.Forms.CheckBox copySecurity;
		private System.Windows.Forms.CheckBox dropAndRecreateObjects;
		private System.Windows.Forms.CheckBox copyExtendedProperties;
		private System.Windows.Forms.CheckBox copyPermissions;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.CheckBox copyFullText;
		private System.Windows.Forms.CheckBox stopIfErrors;
		private System.Windows.Forms.CheckBox clearDestinationDatabase;
        private System.Windows.Forms.Label lblCopyCollation;
        private System.Windows.Forms.ComboBox copyCollation;
    }
}
