using System;
using System.Collections.Generic;
using System.Threading;
using Flatline.Logging;

namespace Flatline
{
    public class PeriodicTaskEntry
    {
        public string Name = "";
        public Timer Timer;
        public Action Callback;
    }

    /* Server-wide periodic-event scheduler. Holds one Timer per registered
     * task and fires its callback on a fixed interval. Used today for the
     * session-cleanup sweep; intended to be reusable for any future
     * "run this every N minutes" background work. */
    public static class PeriodicTasks
    {
        private static List<PeriodicTaskEntry> s_Entries = new List<PeriodicTaskEntry>();
        private static readonly object s_Lock = new object();

        public static void Register(string name, TimeSpan interval, Action callback)
        {
            if (callback == null)
            {
                return;
            }
            PeriodicTaskEntry entry = new PeriodicTaskEntry();
            entry.Name = name;
            entry.Callback = callback;
            entry.Timer = new Timer(OnTimerFired, entry, interval, interval);
            lock (s_Lock)
            {
                s_Entries.Add(entry);
            }
            Log.Info("Periodic task '" + name + "' registered every " + interval.TotalMinutes + " min.");
        }

        public static void StopAll()
        {
            lock (s_Lock)
            {
                int entryCount = s_Entries.Count;
                for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
                {
                    s_Entries[entryIndex].Timer.Dispose();
                }
                s_Entries.Clear();
            }
        }

        private static void OnTimerFired(object stateObject)
        {
            PeriodicTaskEntry entry = (PeriodicTaskEntry)stateObject;
            try
            {
                entry.Callback();
            }
            catch (Exception taskException)
            {
                Log.Exception(taskException, "Periodic task '" + entry.Name + "' threw");
            }
        }
    }
}
