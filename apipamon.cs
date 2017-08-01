/*
 *  APIPAMON - Michael Dumdei 
 *   Update versioning in project properties, OnStart event, and ping buffer when changed
 *    vers 1.00 - Initial code 
 *    vers 1.10 - Added ping the default gateway testing
 *    vers 1.20 - Added fix for Microsoft Failover cluster adapters that have APIPA addresses
 *    vers 2.00 - Added code to verify interface comes back up
 *    vers 2.01 - Put a 2 second pause between ping try attempts 1 & 2 and 2 & 3
 *    vers 2.10 - Added optional command line integer argument to control resets based on 
 *               ping failures. Set to number of times 3-ping test has to fail before 
 *               resetting adapter. Defaults to 1 (first failure).
 *    
 *  Service to monitor network ports for APIPA address assignement or 3 consecutive failed 
 *  pings to the default gateway. Does a reset of the network interface if either occurs.
 */
using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Timers;
using System.Threading;
using System.Net.NetworkInformation;
using System.Text;

namespace APIPA_Monitor
{

    public partial class apipamon : ServiceBase
    {
        private System.Timers.Timer pollTimer;
        private int counter = 0;
        private int resetWait = 0;
        private int pingFailCounter = 0;
        private int max3PingTestFails = 1;
        static private int pingSecsCounter = (30 / 10) - 1;
        static NetworkInterface downInterface = null;
        public apipamon(string[] args)
        {
            InitializeComponent();
            SysUtils.EventLogSource = "apipamon";
            string arg = "1";
            if (args.Length > 0)    // command line args from HKLM\SYSTEM\CurrentControlSet\Services\ApipaMon
                arg = args[0];      //  ex: sc config ApipaMon binPath= "c:\bin\APIPA Monitor.exe 5"
            if (int.TryParse(arg, out max3PingTestFails) == false)
                max3PingTestFails = 1;
        }

        protected override void OnStart(string[] args)
        {
            if (args.Length > 0)    // command line args from Service properties
            {
                if (int.TryParse(args[0], out max3PingTestFails) == false)
                    max3PingTestFails = 1;
            }
            SysUtils.WriteAppEventLog("APIPA monitoring service started - Version 2.10, 3-ping fail count = " 
                + max3PingTestFails.ToString(), eventCode: 1011);
            pollTimer = new System.Timers.Timer(10000);  // poll every 10 seconds
            pollTimer.Elapsed += new System.Timers.ElapsedEventHandler(this.pollTimer_Elapsed);
            pollTimer.Start();
        }

        private void pollTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (resetWait > 0)
            {
                --resetWait;        // prevent back to back resets
                return;
            }
            ++counter;
            if (downInterface != null)
                ResetAdapter(downInterface, "Stuck down", 10002);
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface a in adapters)
            {
                if (a.OperationalStatus != OperationalStatus.Up)
                    continue;
                IPInterfaceProperties p = a.GetIPProperties();
                if (p.GetIPv4Properties().IsAutomaticPrivateAddressingActive == true && a.Name.ToUpper().Contains("APIPA") == false
                 && a.Description.ToUpper().Contains("MICROSOFT FAILOVER CLUSTER VIRTUAL ADAPTER") == false)
                {
                    ResetAdapter(a, "APIPA address", 9999);
                    continue;
                }
                if (counter > pingSecsCounter)    // every 30 sec ping the default gateway
                {
                    Ping png = new Ping();
                    PingOptions pngOpt = new PingOptions();
                    pngOpt.DontFragment = true;
                    bool isGood = false;
                    byte[] buf = Encoding.ASCII.GetBytes("APIPA Monitor ver 2.10 8/1/2017");
                    foreach (GatewayIPAddressInformation g in p.GatewayAddresses)
                    {
                        // try 3 times to get a ping response (50ms response time), all good if one works
                        for (int i = 1; i < 4 && isGood == false; ++i)
                        {
                            try
                            {
                                isGood = png.Send(g.Address, 50, buf, pngOpt).Status == IPStatus.Success;
                            }
                            catch (Exception ex)
                            {
                                SysUtils.WriteAppEventLog(ex.Message, EventLogEntryType.Information, 10);
                                isGood = false;
                            }
                            if (isGood == false)
                            {
                                SysUtils.WriteAppEventLog($"Ping {i} failed", EventLogEntryType.Warning, i);
                                if (i < 3)
                                    Thread.Sleep(2000);
                            }
                        }
                        if (isGood == true)
                            pingFailCounter = 0;
                        else
                        {
                            if (++pingFailCounter >= max3PingTestFails)
                            {
                                ResetAdapter(a, "Pings failing", 9998);  // reset adapter if not pinging
                                pingFailCounter = 0;
                            }
                            continue;
                        }
                    }
                }
            }
            if (counter > pingSecsCounter)
                counter = 0;
        }

        private void ResetAdapter(NetworkInterface a, string reason, int eventCode)
        {
            SysUtils.WriteAppEventLog(reason + " on interface: " + a.Name + ", resetting", EventLogEntryType.Error, eventCode);
            Disable(a);
            Thread.Sleep(1000);
            Enable(a);
            resetWait = 3;
            counter = 0;
        }

        private void Enable(NetworkInterface a)
        {
            for (int i = 0; i < 5; ++i)
            {
                ProcessStartInfo psi = new ProcessStartInfo("netsh", "interface set interface \"" + a.Name + "\" enable");
                Process p = new Process();
                p.StartInfo = psi;
                p.Start();
                Thread.Sleep(500);
                for (int j = 0; j < 10; ++j)
                {
                    if (a.OperationalStatus == OperationalStatus.Up)
                    {
                        downInterface = null;
                        return;
                    }
                    Thread.Sleep(250);
                }
            }
            SysUtils.WriteAppEventLog("Failed to re-enable interface \"" + a.Name + "\"", EventLogEntryType.Error, 10001);
            downInterface = a;
        }

        void Disable(NetworkInterface a)
        {
            for (int i = 0; i < 5; ++i)
            { 
                ProcessStartInfo psi =
                    new ProcessStartInfo("netsh", "interface set interface \"" + a.Name + "\" disable");
                Process p = new Process();
                p.StartInfo = psi;
                p.Start();
                Thread.Sleep(500);
                for (int j = 0; j < 10; ++j)
                {
                    if (a.OperationalStatus != OperationalStatus.Up)
                        return;
                    Thread.Sleep(250);
                }
            }
            SysUtils.WriteAppEventLog("Failed to disable interface \"" + a.Name + "\"", EventLogEntryType.Error, 10000);
        }

        protected override void OnStop()
        {
            pollTimer.Stop();
            pollTimer.Elapsed -= pollTimer_Elapsed;
            // SysUtils.WriteAppEventLog("APIPA monitoring service stopped", eventCode: 1001);
        }

        protected override void OnPause()
        {
            base.OnPause();
            pollTimer.Stop();
        }

        protected override void OnContinue()
        {
            base.OnContinue();
            pollTimer.Start();
        }
    }
}
