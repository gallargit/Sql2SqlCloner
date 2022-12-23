namespace Sql2SqlCloner
{
    partial class ChooseConnections
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
            this.txtSource = new System.Windows.Forms.TextBox();
            this.txtDestination = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.btnSource = new System.Windows.Forms.Button();
            this.btnDestination = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnNext = new System.Windows.Forms.Button();
            this.isSchema = new System.Windows.Forms.CheckBox();
            this.isData = new System.Windows.Forms.CheckBox();
            this.lblPleaseWait = new System.Windows.Forms.Label();
            this.trustServerCertificates = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // txtSource
            // 
            this.txtSource.Location = new System.Drawing.Point(16, 75);
            this.txtSource.Margin = new System.Windows.Forms.Padding(4);
            this.txtSource.Name = "txtSource";
            this.txtSource.Size = new System.Drawing.Size(573, 22);
            this.txtSource.TabIndex = 1;
            this.txtSource.TextChanged += new System.EventHandler(this.txtSource_TextChanged);
            // 
            // txtDestination
            // 
            this.txtDestination.Location = new System.Drawing.Point(16, 138);
            this.txtDestination.Margin = new System.Windows.Forms.Padding(4);
            this.txtDestination.Name = "txtDestination";
            this.txtDestination.Size = new System.Drawing.Size(573, 22);
            this.txtDestination.TabIndex = 3;
            this.txtDestination.TextChanged += new System.EventHandler(this.txtDestination_TextChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(16, 55);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(157, 16);
            this.label1.TabIndex = 3;
            this.label1.Text = "Source Connection String";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(16, 118);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(181, 16);
            this.label2.TabIndex = 4;
            this.label2.Text = "Destination Connection String";
            // 
            // btnSource
            // 
            this.btnSource.Location = new System.Drawing.Point(600, 73);
            this.btnSource.Margin = new System.Windows.Forms.Padding(4);
            this.btnSource.Name = "btnSource";
            this.btnSource.Size = new System.Drawing.Size(100, 28);
            this.btnSource.TabIndex = 2;
            this.btnSource.Text = "Choose";
            this.btnSource.UseVisualStyleBackColor = true;
            this.btnSource.Click += new System.EventHandler(this.btnSource_Click);
            // 
            // btnDestination
            // 
            this.btnDestination.Location = new System.Drawing.Point(599, 134);
            this.btnDestination.Margin = new System.Windows.Forms.Padding(4);
            this.btnDestination.Name = "btnDestination";
            this.btnDestination.Size = new System.Drawing.Size(100, 28);
            this.btnDestination.TabIndex = 4;
            this.btnDestination.Text = "Choose";
            this.btnDestination.UseVisualStyleBackColor = true;
            this.btnDestination.Click += new System.EventHandler(this.btnDestination_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(599, 268);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(4);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 28);
            this.btnCancel.TabIndex = 10;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // btnNext
            // 
            this.btnNext.Location = new System.Drawing.Point(491, 268);
            this.btnNext.Margin = new System.Windows.Forms.Padding(4);
            this.btnNext.Name = "btnNext";
            this.btnNext.Size = new System.Drawing.Size(100, 28);
            this.btnNext.TabIndex = 9;
            this.btnNext.Text = "Next";
            this.btnNext.UseVisualStyleBackColor = true;
            this.btnNext.Click += new System.EventHandler(this.btnNext_Click);
            // 
            // isSchema
            // 
            this.isSchema.AutoSize = true;
            this.isSchema.Checked = true;
            this.isSchema.CheckState = System.Windows.Forms.CheckState.Checked;
            this.isSchema.Location = new System.Drawing.Point(20, 188);
            this.isSchema.Margin = new System.Windows.Forms.Padding(4);
            this.isSchema.Name = "isSchema";
            this.isSchema.Size = new System.Drawing.Size(114, 20);
            this.isSchema.TabIndex = 5;
            this.isSchema.Text = "Copy Schema";
            this.isSchema.UseVisualStyleBackColor = true;
            // 
            // isData
            // 
            this.isData.AutoSize = true;
            this.isData.Checked = true;
            this.isData.CheckState = System.Windows.Forms.CheckState.Checked;
            this.isData.Location = new System.Drawing.Point(172, 188);
            this.isData.Margin = new System.Windows.Forms.Padding(4);
            this.isData.Name = "isData";
            this.isData.Size = new System.Drawing.Size(93, 20);
            this.isData.TabIndex = 6;
            this.isData.Text = "Copy Data";
            this.isData.UseVisualStyleBackColor = true;
            // 
            // lblPleaseWait
            // 
            this.lblPleaseWait.AutoSize = true;
            this.lblPleaseWait.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblPleaseWait.ForeColor = System.Drawing.SystemColors.Highlight;
            this.lblPleaseWait.Location = new System.Drawing.Point(235, 233);
            this.lblPleaseWait.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.lblPleaseWait.Name = "lblPleaseWait";
            this.lblPleaseWait.Size = new System.Drawing.Size(146, 25);
            this.lblPleaseWait.TabIndex = 8;
            this.lblPleaseWait.Text = "Please Wait...";
            this.lblPleaseWait.Visible = false;
            // 
            // trustServerCertificates
            // 
            this.trustServerCertificates.AutoSize = true;
            this.trustServerCertificates.Checked = true;
            this.trustServerCertificates.CheckState = System.Windows.Forms.CheckState.Checked;
            this.trustServerCertificates.Location = new System.Drawing.Point(456, 188);
            this.trustServerCertificates.Margin = new System.Windows.Forms.Padding(4);
            this.trustServerCertificates.Name = "trustServerCertificates";
            this.trustServerCertificates.Size = new System.Drawing.Size(207, 20);
            this.trustServerCertificates.TabIndex = 7;
            this.trustServerCertificates.Text = "Always trust server certificates";
            this.trustServerCertificates.UseVisualStyleBackColor = true;
            // 
            // ChooseConnections
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(728, 321);
            this.Controls.Add(this.trustServerCertificates);
            this.Controls.Add(this.lblPleaseWait);
            this.Controls.Add(this.isData);
            this.Controls.Add(this.isSchema);
            this.Controls.Add(this.btnNext);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnDestination);
            this.Controls.Add(this.btnSource);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.txtDestination);
            this.Controls.Add(this.txtSource);
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ChooseConnections";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Sql2SqlCloner - Connections";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ChooseConnections_FormClosing);
            this.Load += new System.EventHandler(this.ChooseConnections_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox txtSource;
        private System.Windows.Forms.TextBox txtDestination;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button btnSource;
        private System.Windows.Forms.Button btnDestination;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnNext;
        private System.Windows.Forms.CheckBox isSchema;
        private System.Windows.Forms.CheckBox isData;
        private System.Windows.Forms.Label lblPleaseWait;
        private System.Windows.Forms.CheckBox trustServerCertificates;
    }
}
