/*
 *  APIPAMON - Michael Dumdei, Texarkana College
 *   Update versioning in project properties, OnStart event, and ping buffer when changed
 *    vers 1.00 - Initial code 
 *    vers 1.10 - Added ping the default gateway testing
 *    vers 1.20 - Added fix for Microsoft Failover cluster adapters that have APIPA addresses
 *    vers 2.00 - Added code to verify interface comes back up
 *    vers 2.01 - Put a 2 second pause between ping try attempts 1 & 2 and 2 & 3
 *    vers 2.10 - Added optional command line integer argument to control resets.
 *    vers 3.00 - Complete rewrite.
 *    
 *  Service to monitor network ports for APIPA address assignement or 3 consecutive failed 
 *  pings to the default gateway. Does a reset of the network interface if either occurs.
 *  
 *  Args: -i nnn  Poll interval (secs) - how often the service activates. Tests for APIPA on every 
 *                 activation and resets adapter if APIPA address is active. Optional gateway 
 *                 ping test at specified intervals. Default 10 secs.
 *        -g nnn  Gateway test interval (secs) - how often to run ping tests against the default 
 *                 gateway. Test is a series of 3 pings at 2 sec intervals. If all fail, the test 
 *                 fails. Default every 30 secs.
 *        -f nnn  Number of gateway ping tests that are allowed to fail before the adapter is
 *                 reset due no response from gateway. Default is 1, reset on 1st failure.
 *        -h nnn  Number of seconds to hold-off between adapter resets. This is to prevent
 *                 back to back resets. Default is 25 secs.
 *
 *  Install: Copy to the folder where you want the EXE to reside
 *           Run: "APIPA Monitor.exe" -install ARGS  
 *                 Where args is the list of settings you want - none if you like the defaults
 *                 
 *  Uninstall: "APIPA Moniter.exe" -uninstall
 *           
 *  Reconfigure: sc config binpath= "c:\bin\apipamon.exe -i 10 -g 30 -f 1 -h 25"                
 *                 
 */
using System;
using System.Text;
using System.Diagnostics;
using System.Timers;
using System.Threading;
using System.Net.NetworkInformation;
using System.Net;
using System.Collections.Generic;
using System.Net.Sockets;
using System.ServiceProcess;

namespace APIPA_Monitor
{
    class NIC
    {
        public string name { get; set; }
        public OperationalStatus stat { get; set; }
        public List<IPAddress> gwAddrs { get; set; }
        public IPv4InterfaceProperties prop { get; set; }

        public NIC(NetworkInterface a)
        {
            name = a.Name.ToUpper();
            stat = a.OperationalStatus;
            prop = a.GetIPProperties().GetIPv4Properties();
            GatewayIPAddressInformationCollection gws = a.GetIPProperties().GatewayAddresses;
            gwAddrs = new List<IPAddress>();
            foreach (GatewayIPAddressInformation g in gws)
            {
                if (g.Address.AddressFamily == AddressFamily.InterNetwork)
                    gwAddrs.Add(g.Address);
            }
        }
    }

    public partial class apipamon : ServiceBase
    {
        private System.Timers.Timer pollTimer;
        private int pollInterval = 10 * 1000;                   // 10 secs as msec
        private long gwTestInterval = 30L * 10000000L;          // 30 secs as ticks (100ns per tick)
        private int maxGwFails = 1;                             // max gw fails before reset, always resets on APIPA
        private long holdOffInterval = 25L * 10000000L;         // 25 secs as ticks 
        private int gwFailsCounter = 0;
        private long lastReset, lastPingTest;

        private NIC[] nicList;
        private int nNICS = 0;

        public apipamon(string[] args)
        {
            InitializeComponent();
            SysUtils.EventLogSource = "apipamon";
            processArgs(args);		// command line args from registry
        }

        protected override void OnStart(string[] args)
        {
            SysUtils.WriteAppEventLog("APIPA monitoring service starting - Version 3.00", eventCode: 1011);
            processArgs(args);		// command line args from Service properties (one time)
            SysUtils.WriteAppEventLog(string.Format(
                "pollInterval={0}, gwTestInterval={1}, maxgwFails={2}, holdOffInterval={3}",
                 pollInterval / 1000, 
                 (gwTestInterval + 510000L) / 10000000L, 
                 maxGwFails, 
                 (holdOffInterval + 510000L) / 10000000L), eventCode: 1012);
            lastReset = lastPingTest = DateTime.Now.Ticks;
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface a in adapters)
            {
                string nicName = a.Name.ToUpper();
                try
                {
                    if (nicName.Contains("APIPA") == false && nicName.Contains("LOOPBACK") == false
                      && a.Description.ToUpper().Contains("MICROSOFT FAILOVER CLUSTER VIRTUAL ADAPTER") == false
                      && a.GetIPProperties().GetIPv4Properties() != null)
                        ++nNICS;
                }
                catch { }
            }
            nicList = new NIC[nNICS];
            int i = 0;
            foreach (NetworkInterface a in adapters)
            {
                string nicName = a.Name.ToUpper();
                try
                {
                    if (nicName.Contains("APIPA") == false && nicName.Contains("LOOPBACK") == false
                      && a.Description.ToUpper().Contains("MICROSOFT FAILOVER CLUSTER VIRTUAL ADAPTER") == false
                      && a.GetIPProperties().GetIPv4Properties() != null)
                    {
                        nicList[i] = new NIC(a);
                        ++i;
                    }
                }
                catch { }
            }
            pollTimer = new System.Timers.Timer(pollInterval);  // default every 10 seconds
            pollTimer.Elapsed += new System.Timers.ElapsedEventHandler(this.pollTimer_Elapsed);
            pollTimer.Start();
        }

        void processArgs(string[] args)
        {
            long gwInterval = gwTestInterval / 10000000L, hoInterval = holdOffInterval / 10000000L;
            int pInterval = pollInterval / 1000;
            bool status = true;
            for (int i = 0; i < args.Length && status; i += 2)
            {
                int argval;
                if ((args[i][0] != '-' && args[i][0] != '/') || (i + 1) >= args.Length || int.TryParse(args[i + 1], out argval) == false)
                    status = false;
                else
                {
                    string cmd = args[i].Substring(1).ToLower();
                    if (cmd == "pollinterval" || cmd == "poll" || cmd == "p" || cmd == "i")
                        pInterval = argval;
                    else if (cmd == "gwtestinterval" || cmd == "gwtest" || cmd == "g" || cmd == "t")
                        gwInterval = argval;
                    else if (cmd == "maxgwfails" || cmd == "gwfails" || cmd == "fails" || cmd == "f")
                        maxGwFails = argval;
                    else if (cmd == "holdoffinterval" || cmd == "resetinterval" || cmd == "h" || cmd == "r")
                        hoInterval = argval;
                    else
                        status = false;
                }
            }
            if (status == false)
            {
                SysUtils.WriteAppEventLog("Invalid arguments passed to service", EventLogEntryType.Error, 99);
                throw new Exception("Invalid arguments");
            }
             // Back off the tick-based counters by 50 msec if they are exact multiples of the
             // polling interval, so they happen on the correct poll and not the one that follows.
            pollInterval = pInterval * 1000;
            gwTestInterval = gwInterval * 10000000;
            if (gwInterval % pInterval == 0)
                gwTestInterval -= 500000L;
            holdOffInterval = hoInterval * 10000000;
            if (hoInterval % pInterval == 0)
                holdOffInterval -= 500000;
        }

        private void pollTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            bool doGatewayPingTest = false;

            if (lastReset + holdOffInterval > e.SignalTime.Ticks)
                return;         // prevent back to back resets
            if (gwTestInterval > 0 && lastPingTest + gwTestInterval < e.SignalTime.Ticks)
            {
                doGatewayPingTest = true;
                lastPingTest = e.SignalTime.Ticks;
            }
            UpdateNicStatus();
            foreach (NIC n in nicList)
            {
                if (n.stat != OperationalStatus.Up)
                    ResetAdapter(n, "Stuck down", 10002, e.SignalTime.Ticks);
            }
            foreach (NIC n in nicList)
            {
                if (n.stat != OperationalStatus.Up)
                    continue;
                if (n.prop.IsAutomaticPrivateAddressingActive == true)
                {
                    ResetAdapter(n, "APIPA address", 9999, e.SignalTime.Ticks);
                    continue;
                }
                if (doGatewayPingTest == true)
                {
                    Ping png = new Ping();
                    PingOptions pngOpt = new PingOptions();
                    pngOpt.DontFragment = true;
                    bool isGood = false;
                    byte[] buf = Encoding.ASCII.GetBytes("APIPA Monitor ver 3.00 8/3/2017 - Gateway Test");
                    foreach (IPAddress gwAddress in n.gwAddrs)
                    {
                        // try 3 times to get a ping response (50ms response time), all good if one works
                        for (int i = 1; i < 4 && isGood == false; ++i)
                        {
                            try
                            {
                                isGood = png.Send(gwAddress, 50, buf, pngOpt).Status == IPStatus.Success;
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
                            gwFailsCounter = 0;
                        else
                        {
                            if (++gwFailsCounter >= maxGwFails)
                            {
                                ResetAdapter(n, "Pings failing", 9998, e.SignalTime.Ticks);  // reset if not pinging
                                gwFailsCounter = 0;
                            }
                            continue;
                        }
                    }
                }
            }
        }

        private void ResetAdapter(NIC a, string reason, int eventCode, long currentTicks)
        {
            SysUtils.WriteAppEventLog(reason + " on interface: " + a.name + ", resetting", EventLogEntryType.Error, eventCode);
            lastReset = currentTicks;
            SetNIC(a, enabling:false);
            Thread.Sleep(1000);
            SetNIC(a, enabling:true);
        }

        void UpdateNicStatus()
        {
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NIC n in nicList)
            {
                bool found = false;
                foreach (NetworkInterface a in adapters)
                {
                    if (a.Name.ToUpper() == n.name)
                    {
                        found = true;
                        n.prop = a.GetIPProperties().GetIPv4Properties();
                        n.stat = a.OperationalStatus;
                        break;
                    }
                }
                if (found == false)
                    n.stat = OperationalStatus.NotPresent;
            }
        }

        private void SetNIC(NIC a, bool enabling)
        {
            Process p = new Process();
            bool success = false;
            try
            {
                for (int i = 0; i < 5 && success == false; ++i)
                {
                    string cmd, args;
                    cmd = Environment.SystemDirectory + "\\netsh.exe";
                    args = "interface set interface \"" + a.name + "\" " + ((enabling) ? "enable" : "disable");
                    p.StartInfo = new ProcessStartInfo(cmd, args);
                    p.StartInfo.UseShellExecute = false;
                    p.Start();
                    Thread.Sleep(500);
                    for (int j = 0; j < 10 && success == false; ++j)
                    {
                        UpdateNicStatus();
                        if ((enabling ^ (a.stat == OperationalStatus.Up)) == false)
                            success = true;
                        Thread.Sleep(250);
                    }
                }
            }
            catch { }
            finally
            {
                p.Close();
                if (success == false)
                {
                    if (enabling)
                        SysUtils.WriteAppEventLog("Failed to re-enable interface \"" + a.name + "\"", EventLogEntryType.Error, 10001);
                    else
                        SysUtils.WriteAppEventLog("Failed to disable interface \"" + a.name + "\"", EventLogEntryType.Error, 10000);
                }
            }
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
