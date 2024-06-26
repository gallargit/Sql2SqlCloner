﻿namespace Sql2SqlCloner
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
            this.decryptObjects = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // txtSource
            // 
            this.txtSource.Location = new System.Drawing.Point(12, 61);
            this.txtSource.Name = "txtSource";
            this.txtSource.Size = new System.Drawing.Size(422, 20);
            this.txtSource.TabIndex = 1;
            this.txtSource.TextChanged += new System.EventHandler(this.txtSource_TextChanged);
            // 
            // txtDestination
            // 
            this.txtDestination.Location = new System.Drawing.Point(12, 112);
            this.txtDestination.Name = "txtDestination";
            this.txtDestination.Size = new System.Drawing.Size(422, 20);
            this.txtDestination.TabIndex = 3;
            this.txtDestination.TextChanged += new System.EventHandler(this.txtDestination_TextChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 45);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(128, 13);
            this.label1.TabIndex = 3;
            this.label1.Text = "Source Connection String";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(12, 96);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(147, 13);
            this.label2.TabIndex = 4;
            this.label2.Text = "Destination Connection String";
            // 
            // btnSource
            // 
            this.btnSource.Location = new System.Drawing.Point(440, 58);
            this.btnSource.Name = "btnSource";
            this.btnSource.Size = new System.Drawing.Size(75, 23);
            this.btnSource.TabIndex = 2;
            this.btnSource.Text = "Choose";
            this.btnSource.UseVisualStyleBackColor = true;
            this.btnSource.Click += new System.EventHandler(this.btnSource_Click);
            // 
            // btnDestination
            // 
            this.btnDestination.Location = new System.Drawing.Point(440, 109);
            this.btnDestination.Name = "btnDestination";
            this.btnDestination.Size = new System.Drawing.Size(75, 23);
            this.btnDestination.TabIndex = 4;
            this.btnDestination.Text = "Choose";
            this.btnDestination.UseVisualStyleBackColor = true;
            this.btnDestination.Click += new System.EventHandler(this.btnDestination_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(359, 218);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 9;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // btnNext
            // 
            this.btnNext.Location = new System.Drawing.Point(440, 218);
            this.btnNext.Name = "btnNext";
            this.btnNext.Size = new System.Drawing.Size(75, 23);
            this.btnNext.TabIndex = 10;
            this.btnNext.Text = "Next";
            this.btnNext.UseVisualStyleBackColor = true;
            this.btnNext.Click += new System.EventHandler(this.btnNext_Click);
            // 
            // isSchema
            // 
            this.isSchema.AutoSize = true;
            this.isSchema.Checked = true;
            this.isSchema.CheckState = System.Windows.Forms.CheckState.Checked;
            this.isSchema.Location = new System.Drawing.Point(15, 153);
            this.isSchema.Name = "isSchema";
            this.isSchema.Size = new System.Drawing.Size(92, 17);
            this.isSchema.TabIndex = 5;
            this.isSchema.Text = "Copy Schema";
            this.isSchema.UseVisualStyleBackColor = true;
            this.isSchema.CheckedChanged += new System.EventHandler(this.isSchema_CheckedChanged);
            // 
            // isData
            // 
            this.isData.AutoSize = true;
            this.isData.Checked = true;
            this.isData.CheckState = System.Windows.Forms.CheckState.Checked;
            this.isData.Location = new System.Drawing.Point(129, 153);
            this.isData.Name = "isData";
            this.isData.Size = new System.Drawing.Size(76, 17);
            this.isData.TabIndex = 6;
            this.isData.Text = "Copy Data";
            this.isData.UseVisualStyleBackColor = true;
            // 
            // lblPleaseWait
            // 
            this.lblPleaseWait.AutoSize = true;
            this.lblPleaseWait.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblPleaseWait.ForeColor = System.Drawing.SystemColors.Highlight;
            this.lblPleaseWait.Location = new System.Drawing.Point(164, 196);
            this.lblPleaseWait.Name = "lblPleaseWait";
            this.lblPleaseWait.Size = new System.Drawing.Size(119, 20);
            this.lblPleaseWait.TabIndex = 11;
            this.lblPleaseWait.Text = "Please Wait...";
            this.lblPleaseWait.Visible = false;
            // 
            // decryptObjects
            // 
            this.decryptObjects.AutoSize = true;
            this.decryptObjects.Checked = true;
            this.decryptObjects.CheckState = System.Windows.Forms.CheckState.Checked;
            this.decryptObjects.Location = new System.Drawing.Point(306, 153);
            this.decryptObjects.Name = "decryptObjects";
            this.decryptObjects.Size = new System.Drawing.Size(209, 17);
            this.decryptObjects.TabIndex = 8;
            this.decryptObjects.Text = "Decrypt objects using DAC connection";
            this.decryptObjects.UseVisualStyleBackColor = true;
            // 
            // ChooseConnections
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(534, 261);
            this.Controls.Add(this.decryptObjects);
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
        private System.Windows.Forms.CheckBox decryptObjects;
    }
}
