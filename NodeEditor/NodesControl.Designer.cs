﻿namespace NodeEditor
{
    partial class NodesControl
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // NodesControl
            // 
            this.Name = "NodesControl";
            this.Size = new System.Drawing.Size(574, 401);
            this.Resize += new System.EventHandler(this.NodesControl_Resize);
            this.MouseClick += new System.Windows.Forms.MouseEventHandler(this.NodesControl_MouseClick);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.NodesControl_MouseDown);
            this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.NodesControl_MouseMove);
            this.MouseUp += new System.Windows.Forms.MouseEventHandler(this.NodesControl_MouseUp);
            this.MouseWheel += new System.Windows.Forms.MouseEventHandler(this.NodesControl_MouseWheel);
            this.ResumeLayout(false);

        }

        #endregion
    }
}
