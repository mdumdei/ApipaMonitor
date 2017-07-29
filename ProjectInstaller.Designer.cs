namespace APIPA_Monitor
{
    partial class ProjectInstaller
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
            this.APIPAMonitorProcessInstaller = new System.ServiceProcess.ServiceProcessInstaller();
            this.APIPAMonitorSvcInstaller = new System.ServiceProcess.ServiceInstaller();
            // 
            // APIPAMonitorProcessInstaller
            // 
            this.APIPAMonitorProcessInstaller.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
            this.APIPAMonitorProcessInstaller.Password = null;
            this.APIPAMonitorProcessInstaller.Username = null;
            this.APIPAMonitorProcessInstaller.AfterInstall += new System.Configuration.Install.InstallEventHandler(this.APIPAMonitorProcessInstaller_AfterInstall);
            // 
            // APIPAMonitorSvcInstaller
            // 
            this.APIPAMonitorSvcInstaller.DelayedAutoStart = true;
            this.APIPAMonitorSvcInstaller.Description = "Disables/enables network adapter if APIPA address detected";
            this.APIPAMonitorSvcInstaller.DisplayName = "APIPA monitor";
            this.APIPAMonitorSvcInstaller.ServiceName = "apipamon";
            this.APIPAMonitorSvcInstaller.StartType = System.ServiceProcess.ServiceStartMode.Automatic;
            this.APIPAMonitorSvcInstaller.AfterInstall += new System.Configuration.Install.InstallEventHandler(this.APIPAMonitorSvcInstaller_AfterInstall);
            // 
            // ProjectInstaller
            // 
            this.Installers.AddRange(new System.Configuration.Install.Installer[] {
            this.APIPAMonitorProcessInstaller,
            this.APIPAMonitorSvcInstaller});

        }

        #endregion

        private System.ServiceProcess.ServiceProcessInstaller APIPAMonitorProcessInstaller;
        private System.ServiceProcess.ServiceInstaller APIPAMonitorSvcInstaller;
    }
}