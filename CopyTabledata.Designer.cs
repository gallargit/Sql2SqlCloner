namespace Sql2SqlCloner
{
    partial class CopyTabledata
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
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.backgroundWorker1 = new System.ComponentModel.BackgroundWorker();
            this.label1 = new System.Windows.Forms.Label();
            this.btnCancel = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.dataGridView1 = new System.Windows.Forms.DataGridView();
            this.Status = new System.Windows.Forms.DataGridViewImageColumn();
            this.Table = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.SqlCommand = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Error = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.HasRelationships = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.Timer1 = new System.Windows.Forms.Timer(this.components);
            this.btnCopyMessages = new System.Windows.Forms.Button();
            this.btnPause = new System.Windows.Forms.Button();
            this.autoScrollGrid = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            this.SuspendLayout();
            // 
            // btnNext
            // 
            this.btnNext.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnNext.Location = new System.Drawing.Point(516, 276);
            this.btnNext.Name = "btnNext";
            this.btnNext.Size = new System.Drawing.Size(75, 23);
            this.btnNext.TabIndex = 3;
            this.btnNext.Text = "Copy";
            this.btnNext.UseVisualStyleBackColor = true;
            this.btnNext.Click += new System.EventHandler(this.btnNext_Click);
            // 
            // progressBar1
            // 
            this.progressBar1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar1.Location = new System.Drawing.Point(12, 242);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(660, 23);
            this.progressBar1.TabIndex = 6;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(9, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(27, 13);
            this.label1.TabIndex = 7;
            this.label1.Text = "Title";
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.Location = new System.Drawing.Point(597, 276);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 5;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // label2
            // 
            this.label2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(15, 276);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(21, 13);
            this.label2.TabIndex = 9;
            this.label2.Text = "0%";
            // 
            // dataGridView1
            // 
            this.dataGridView1.AllowUserToAddRows = false;
            this.dataGridView1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.dataGridView1.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGridView1.BackgroundColor = System.Drawing.SystemColors.Window;
            this.dataGridView1.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.dataGridView1.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView1.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.Status,
            this.Table,
            this.SqlCommand,
            this.Error,
            this.HasRelationships});
            this.dataGridView1.Location = new System.Drawing.Point(12, 36);
            this.dataGridView1.Name = "dataGridView1";
            this.dataGridView1.RowHeadersWidth = 25;
            this.dataGridView1.Size = new System.Drawing.Size(660, 194);
            this.dataGridView1.TabIndex = 0;
            // 
            // Status
            // 
            this.Status.FillWeight = 101.5228F;
            this.Status.HeaderText = "Status";
            this.Status.Name = "Status";
            // 
            // Table
            // 
            this.Table.FillWeight = 99.49238F;
            this.Table.HeaderText = "Table";
            this.Table.Name = "Table";
            // 
            // SqlCommand
            // 
            this.SqlCommand.FillWeight = 99.49238F;
            this.SqlCommand.HeaderText = "SqlCommand";
            this.SqlCommand.Name = "SqlCommand";
            // 
            // Error
            // 
            this.Error.FillWeight = 99.49238F;
            this.Error.HeaderText = "Error";
            this.Error.Name = "Error";
            // 
            // HasRelationships
            // 
            this.HasRelationships.HeaderText = "HasRelationships";
            this.HasRelationships.Name = "HasRelationships";
            this.HasRelationships.Visible = false;
            // 
            // Timer1
            // 
            this.Timer1.Interval = 1000;
            this.Timer1.Tick += new System.EventHandler(this.Timer1_Tick);
            // 
            // btnCopyMessages
            // 
            this.btnCopyMessages.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCopyMessages.Location = new System.Drawing.Point(329, 276);
            this.btnCopyMessages.Name = "btnCopyMessages";
            this.btnCopyMessages.Size = new System.Drawing.Size(111, 23);
            this.btnCopyMessages.TabIndex = 1;
            this.btnCopyMessages.Text = "Copy messages";
            this.btnCopyMessages.UseVisualStyleBackColor = true;
            this.btnCopyMessages.Visible = false;
            this.btnCopyMessages.Click += new System.EventHandler(this.btnCopyMessages_Click);
            // 
            // btnPause
            // 
            this.btnPause.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnPause.Location = new System.Drawing.Point(446, 276);
            this.btnPause.Name = "btnPause";
            this.btnPause.Size = new System.Drawing.Size(64, 23);
            this.btnPause.TabIndex = 2;
            this.btnPause.Text = "Pause";
            this.btnPause.UseVisualStyleBackColor = true;
            this.btnPause.Click += new System.EventHandler(this.btnPause_Click);
            // 
            // autoScrollGrid
            // 
            this.autoScrollGrid.AutoSize = true;
            this.autoScrollGrid.Checked = true;
            this.autoScrollGrid.CheckState = System.Windows.Forms.CheckState.Checked;
            this.autoScrollGrid.Location = new System.Drawing.Point(597, 13);
            this.autoScrollGrid.Name = "autoScrollGrid";
            this.autoScrollGrid.Size = new System.Drawing.Size(75, 17);
            this.autoScrollGrid.TabIndex = 10;
            this.autoScrollGrid.Text = "Auto scroll";
            this.autoScrollGrid.UseVisualStyleBackColor = true;
            this.autoScrollGrid.CheckedChanged += new System.EventHandler(this.autoScrollGrid_CheckedChanged);
            // 
            // CopyTabledata
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(684, 311);
            this.Controls.Add(this.autoScrollGrid);
            this.Controls.Add(this.btnPause);
            this.Controls.Add(this.btnCopyMessages);
            this.Controls.Add(this.dataGridView1);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.progressBar1);
            this.Controls.Add(this.btnNext);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CopyTabledata";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Sql2SqlCloner - Copy Data";
            this.Load += new System.EventHandler(this.CopyTabledata_Load);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button btnNext;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.ComponentModel.BackgroundWorker backgroundWorker1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.DataGridView dataGridView1;
        private System.Windows.Forms.DataGridViewImageColumn Status;
        private System.Windows.Forms.DataGridViewTextBoxColumn Table;
        private System.Windows.Forms.DataGridViewTextBoxColumn SqlCommand;
        private System.Windows.Forms.DataGridViewTextBoxColumn Error;
        private System.Windows.Forms.DataGridViewTextBoxColumn HasRelationships;
        private System.Windows.Forms.Timer Timer1;
        private System.Windows.Forms.Button btnCopyMessages;
        private System.Windows.Forms.Button btnPause;
        private System.Windows.Forms.CheckBox autoScrollGrid;
    }
}
