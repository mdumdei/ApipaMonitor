using System;
using System.Diagnostics;
using System.Threading;

namespace APIPA_Monitor
{
    public class SysUtils
    {
        public static string EventLogSource;        // set this before using: SysUtils.EventLogSource = "xxxxxx";
        public static bool EventLogStat = false;

        /// <summary>
        /// Write an entry to the EventLog
        /// </summary>
        /// <param name="txt">Text to write</param>
        /// <param name="typ">Event level</param>
        /// <param name="eventCode">Event ID</param>
        public static void WriteAppEventLog(string txt, EventLogEntryType typ = EventLogEntryType.Information, int eventCode = 1000)
        {
            // Powershell to create this log: New-Eventlog -LogName Application -Source XXXXXX
            if (EventLogStat == false)
            {
                if (EventLogSource == null)
                    throw new Exception("Coding error: EventLogSource not set, need SysUtils.EventLogSource = \"xxxxx\";");
                else
                {
                    try
                    {
                        int i = 0;
                        while (!(EventLogStat = EventLog.SourceExists(EventLogSource)) && i < 2)
                        {
                            if (i == 0)
                                EventLog.CreateEventSource(EventLogSource, "Application");
                            Thread.Sleep(1000);     // delay 1 sec to allow newly created source to register
                            ++i;
                        }
                    }
                    catch (Exception ex) { }
                }
                if (EventLogStat == false)
                {
                    throw new Exception("Unable to create eventlog source, run as admin or use PS cmd: "
                      + "New-EventLog -LogName Application -Source " + EventLogSource + " to create.");
                }
            }
            EventLog.WriteEntry(EventLogSource, txt, typ, eventCode);
        }
    }
}
