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
 *    vers 3.01 - Added ping test timeout as an optional parameter.
 *    vers 3.03 - Added control to limit writes to EventLog on failed gateway ping
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
 *        -t nnn  Ping timeout (msec) - number of milliseconds to wait for ping response when
 *                 performing default gateway tests. Default is 800 msec (some switches have very
 *                 low priority on response to gateway pings).
 *        -l n    Sets the number of failed pings that may occur before recording in the
 *                 EventLog. Default is 1.
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
 *  Reconfigure: sc config binpath= "\"c:\bin\apipamon.exe\" -i 10 -g 30 -t 100 -f 1 -h 25"                
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

    //
    // Class to track data of interest in IPV4 enabled NICS being monitored
    //
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

    // APIPA Monitor main Service class
    public partial class apipamon : ServiceBase
    {
        // Event log information
        private static string eventSource = "apipamon";
        private static string startingMsg = "APIPA monitoring service starting - Version 3.03";
        // Load ping buffer with identifying information for anyone sniffing traffic
        private static byte[] pingData = Encoding.ASCII.GetBytes("APIPA Monitor ver 3.03 8/30/2017 - Gateway Test");
        // Globals
        private System.Timers.Timer pollTimer;
        private int gwFailsCounter = 0;
        private long lastReset, lastPingTest;
        private NIC[] nicList;
        private int nNICS = 0;
        // Default values below are overridden first by Service registry arguments if present
        // and those by Service start args in the GUI if those are present
        private int pollInterval = 10 * 1000;                   // -i 10 secs as msec
        private long gwTestInterval = 30L * 10000000L;          // -g 30 secs as ticks (100ns per tick)
        private int pingTimeout = 500;                          // -t 500 msecs allowed for ping response
        private int maxPingFailsBeforeLogging = 1;              // -l default is don't log first failed ping
        private int maxGwFails = 1;                             // -f max gw fails before reset, always resets on APIPA
        private long holdOffInterval = 25L * 10000000L;         // -h 25 secs as ticks 
       

        public apipamon(string[] args)
        {
            InitializeComponent();
            SysUtils.EventLogSource = eventSource;
            processArgs(args);		// command line args from registry: CurrentControlSet, Services
        }

        protected override void OnStart(string[] args)
        {
            SysUtils.WriteAppEventLog(startingMsg, eventCode: 1011);
            processArgs(args);		// command line args from Service GUI properties (one time override)
            SysUtils.WriteAppEventLog(string.Format(
                "pollInterval={0}, gwTestInterval={1}, pingTimeout={2}, maxgwFails={3}, holdOffInterval={4}",
                 pollInterval / 1000, 
                 Math.Round((double)gwTestInterval / 10000000.0), 
                 pingTimeout,
                 maxGwFails, 
                 Math.Round((double)holdOffInterval / 10000000.0)), eventCode: 1012);
            lastReset = lastPingTest = DateTime.Now.Ticks;
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
             // First foreach loop counts up how many NICS the service will monitor
            foreach (NetworkInterface a in adapters)
            {
                string nicName = a.Name.ToUpper();
                try
                {    // skip loopback, NICS with "APIPA" in the name, MS cluster NIC, and non-IPV4 enabled NICS
                    if (nicName.Contains("APIPA") == false && nicName.Contains("LOOPBACK") == false
                      && a.Description.ToUpper().Contains("MICROSOFT FAILOVER CLUSTER VIRTUAL ADAPTER") == false
                      && a.GetIPProperties().GetIPv4Properties() != null)
                        ++nNICS;
                }
                catch { }
            }
            nicList = new NIC[nNICS];               // allocate NICS array
            int i = 0;
             // Second foreach loop initializes NIC objects in NICS array
            foreach (NetworkInterface a in adapters)
            {
                string nicName = a.Name.ToUpper();
                try
                {
                    if (nicName.Contains("APIPA") == false && nicName.Contains("LOOPBACK") == false
                      && a.Description.ToUpper().Contains("MICROSOFT FAILOVER CLUSTER VIRTUAL ADAPTER") == false
                      && a.GetIPProperties().GetIPv4Properties() != null)
                    {
                        nicList[i] = new NIC(a);    // initialize NIC holding data
                        ++i;
                    }
                }
                catch { }
            }
             // Start the Service activation timer
            pollTimer = new System.Timers.Timer(pollInterval);  // default every 10 seconds
            pollTimer.Elapsed += new System.Timers.ElapsedEventHandler(this.pollTimer_Elapsed);
            pollTimer.Start();
        }

        // 
        // Process arguments - called for both Registry based args and then  Service GUI args in that order,
        //  so Service GUI args (if present) override any in the registry
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
                    if (cmd == "pollinterval" || cmd == "i")
                        pInterval = argval;
                    else if (cmd == "gwtestinterval" || cmd == "g")
                        gwInterval = argval;
                    else if (cmd == "pingtimeout" || cmd == "t")
                        pingTimeout = argval;
                    else if (cmd == "allowedpingfails" || cmd == "l")
                        maxPingFailsBeforeLogging = argval;
                    else if (cmd == "maxgwfails" || cmd == "f")
                        maxGwFails = argval;
                    else if (cmd == "holdoffinterval" || cmd == "h")
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


        // Service activated process
        private void pollTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            bool doGatewayPingTest = false;

            if (lastReset + holdOffInterval > e.SignalTime.Ticks)
                return;         // exit if a reset was performed within the holdoff period 
            if (gwTestInterval > 0 && lastPingTest + gwTestInterval < e.SignalTime.Ticks)
            {                   // set flag if time to do a gateway test & update "last ran"
                doGatewayPingTest = true;
                lastPingTest = e.SignalTime.Ticks;
            }
            UpdateNicStatus();  // get current state of NICS
            foreach (NIC n in nicList)
            {                   // they should all be up - if not, try and bring it up
                if (n.stat != OperationalStatus.Up)
                    ResetAdapter(n, "Stuck down", 10002, e.SignalTime.Ticks);
            }
            foreach (NIC n in nicList)
            {
                if (n.stat != OperationalStatus.Up)
                    continue;   // no need to check APIPA or gateway if NIC is down
                if (n.prop.IsAutomaticPrivateAddressingActive == true)
                {               // test for 169.254 address & reset if the NIC has one for an IP
                    ResetAdapter(n, "APIPA address", 9999, e.SignalTime.Ticks);
                    continue;
                }
                 // Do the 3-ping gateway test if enabled and time to do so
                if (doGatewayPingTest == true)
                {
                    Ping png = new Ping();
                    PingOptions pngOpt = new PingOptions();
                    pngOpt.DontFragment = true;
                    bool isGood = false;
                    foreach (IPAddress gwAddress in n.gwAddrs)
                    {
                        // try 3 times to get a ping response (default 50ms response time), all good if one works
                        for (int i = 1; i < 4 && isGood == false; ++i)
                        {
                            try
                            {
                                isGood = png.Send(gwAddress, pingTimeout, pingData, pngOpt).Status == IPStatus.Success;
                            }
                            catch (Exception ex)
                            {
                                SysUtils.WriteAppEventLog(ex.Message, EventLogEntryType.Information, 10);
                                isGood = false;
                            }
                            if (isGood == false)
                            {
                                if (i > maxPingFailsBeforeLogging)
                                    SysUtils.WriteAppEventLog($"Ping {i} failed", EventLogEntryType.Warning, i);
                                if (i < 3)
                                    Thread.Sleep(2000);
                            }
                        }
                        if (isGood == true)         // if ping succeeded, reset number of gateway test fails counter
                            gwFailsCounter = 0;
                        else
                        {                           // if all 3 failed, update gw failed counter & reset NIC
                            if (++gwFailsCounter >= maxGwFails) //                          if limit reached
                            {
                                ResetAdapter(n, "Pings failing", 9998, e.SignalTime.Ticks);
                                gwFailsCounter = 0;
                            }
                            continue;
                        }
                    }
                }
            }
        }

        // Bounce the NIC
        private void ResetAdapter(NIC a, string reason, int eventCode, long currentTicks)
        {
            SysUtils.WriteAppEventLog(reason + " on interface: " + a.name + ", resetting", EventLogEntryType.Error, eventCode);
            lastReset = currentTicks;       // update timer value for when the NIC was last reset - used for holdoff
            SetNIC(a, enabling:false);      // disable the NIC
            Thread.Sleep(1000);             // wait one second
            SetNIC(a, enabling:true);       // enable the NIC - this normally clears the APIPA condition
        }

        // Get current state of NICS being monitored
        void UpdateNicStatus()
        {
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NIC n in nicList)
            {
                bool found = false;
                foreach (NetworkInterface a in adapters)
                {
                    if (a.Name.ToUpper() == n.name)  // found a NIC being monitored
                    {
                        found = true;
                        n.prop = a.GetIPProperties().GetIPv4Properties(); // holds APIPA state
                        n.stat = a.OperationalStatus;                     // determines if online
                        break;
                    }
                }
                if (found == false)                                       // hits this if NIC disabled
                    n.stat = OperationalStatus.NotPresent;
            }
        }

        // Enables / disables NICs based on 'enabling' = true or false. Invokes "netsh.exe" to do the work.
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
            lastPingTest = lastReset = DateTime.Now.Ticks;  // reset "last ticks" if restarting after a pause
            pollTimer.Start();
        }
    }
}
