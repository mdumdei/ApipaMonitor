/*
 *  APIPAMON - Michael Dumdei, Texarkana College
 *   Installer code: Me, Google denizens, et al
*/
using System;
using System.ServiceProcess;
using System.Collections;
using System.Configuration.Install;
using Microsoft.Win32;

namespace APIPA_Monitor
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// 
        static string svcName = "apipamon";
        static System.Reflection.Assembly svcType = typeof(apipamon).Assembly;

        static void Main(string[] args)
        {
            string[] svcArgs = args;
            if (args.Length > 0)
            {
                foreach (string s in args)
                {
                    if (s.ToLower() == "-uninstall")
                    {
                        StopService();
                        UninstallService();
                        return;
                    }
                    if (s.ToLower() == "-install")
                    {
                        InstallService();
                        svcArgs = ConfigureArgs(args);
                        StartService();
                        return;
                    }
                }
            }
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new apipamon(svcArgs)
            };
            ServiceBase.Run(ServicesToRun);
        }


        // Process command line args, stripping out -install or -uninstall switch so that
        //  what is left are the args for the service itself. It then writes those args
        //  to the HKLM/SYSTEM/CurrentControlSet/Services/apimon ImagePath entry and
        //  returns the processed arg array.
        private static string[] ConfigureArgs(string[] args)
        {
            string svcArgs = string.Empty;
            bool first = true;
            if (args.Length > 1)
            {
                string path = System.Reflection.Assembly.GetEntryAssembly().Location;
                foreach (string s in args)
                {
                    if (s.ToLower() != "-install" && s.ToLower() != "-uninstall")
                    {
                        svcArgs += ((first) ? string.Empty : " ") + s;
                        first = false;
                    }
                }
                RegistryKey key = Registry.LocalMachine.OpenSubKey("SYSTEM", true);
                key = key.OpenSubKey("CurrentControlSet", true).OpenSubKey("Services", true);
                key = key.CreateSubKey("apipamon");
                key.SetValue("ImagePath", string.Format("\"{0}\" {1}", path, svcArgs));
            }
            return (svcArgs == string.Empty) ? new string[0] : svcArgs.Split(new char[] { ' ' });
        }

#region Googled Installer Code with some modifications to make it work and more generic
         // Code to install and uninstall the service. For the most part, taken from code found
         //  on the Internet (StackOverflow).
        private static bool IsInstalled()
        {
            using (ServiceController controller = new ServiceController(svcName))
            {
                try
                {
                    string name = controller.ServiceName;  // mod from checking status
                }
                catch
                {
                    return false;
                }
                return true;
            }
        }

        private static bool IsRunning()
        {
            using (ServiceController controller = new ServiceController(svcName))
            {
                if (!IsInstalled())
                    return false;
                return (controller.Status == ServiceControllerStatus.Running);
            }
        }

        private static AssemblyInstaller GetInstaller()
        {
            AssemblyInstaller installer = new AssemblyInstaller(svcType, null);
            installer.UseNewContext = true;
            return installer;
        }

        private static void InstallService()
        {
            if (IsInstalled())
                return;
            try
            {
                using (AssemblyInstaller installer = GetInstaller())
                {
                    IDictionary state = new Hashtable();
                    try
                    {
                        installer.Install(state);
                        installer.Commit(state);
                    }
                    catch
                    {
                        try
                        {
                            installer.Rollback(state);
                        }
                        catch { }
                        throw;
                    }
                }
            }
            catch
            {
                throw;
            }
        }

        private static void UninstallService()
        {
            if (!IsInstalled())
                return;
            try
            {
                using (AssemblyInstaller installer = GetInstaller())
                {
                    IDictionary state = new Hashtable();
                    try
                    {
                        installer.Uninstall(state);
                    }
                    catch
                    {
                        throw;
                    }
                }
            }
            catch
            {
                throw;
            }
        }

        private static void StartService()
        {
            if (!IsInstalled())
                return;
            using (ServiceController controller =  new ServiceController(svcName))
            {
                try
                {
                    if (controller.Status != ServiceControllerStatus.Running)
                    {
                        controller.Start();
                        controller.WaitForStatus(ServiceControllerStatus.Running,
                            TimeSpan.FromSeconds(10));
                    }
                }
                catch
                {
                    throw;
                }
            }
        }

        private static void StopService()
        {
            if (!IsInstalled())
                return;
            using (ServiceController controller = new ServiceController(svcName))
            {
                try
                {
                    if (controller.Status != ServiceControllerStatus.Stopped)
                    {
                        controller.Stop();
                        controller.WaitForStatus(ServiceControllerStatus.Stopped,
                             TimeSpan.FromSeconds(10));
                    }
                }
                catch
                {
                    throw;
                }
            }
        }
#endregion
    }

}
