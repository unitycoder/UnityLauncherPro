// keeps reference of launched unity processes, so that can close them even if project list is refreshed

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace UnityLauncherPro.Helpers
{
    public static class ProcessHandler
    {
        static Dictionary<string, Process> processes = new Dictionary<string, Process>();

        public static void Add(Project proj, Process proc)
        {
            if (proc == null) return;

            var key = proj.Path;
            if (processes.ContainsKey(key))
            {
                // already in the list, maybe trying to launch same project twice? only overwrite if previous process has closed
                if (processes[key] == null) processes[key] = proc;
            }
            else
            {
                processes.Add(key, proc);
            }

            // subscribe to process exit here, so that can update proj details row (if it was changed in Unity)
            proc.Exited += (object o, EventArgs ea) =>
            {
                // call method in mainwindow, to easy access for sourcedata and grid
                Application.Current.Dispatcher.Invoke(DispatcherPriority.Background, (Action)delegate ()
                {
                    MainWindow wnd = (MainWindow)Application.Current.MainWindow;
                    wnd.ProcessExitedCallBack(proj);
                });

                // remove closed process item
                Remove(key);
            };
        }

        public static Process Get(string key)
        {
            if (processes.ContainsKey(key)) return processes[key];
            return null;
        }

        public static bool IsRunning(string key)
        {
            return processes.ContainsKey(key) && (processes[key] != null);
        }

        public static void Remove(string key)
        {
            if (processes.ContainsKey(key)) processes.Remove(key);
        }
    }
}
