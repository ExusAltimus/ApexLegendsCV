using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OCR
{
    public static class FindWindow
    {
        public static IEnumerable<IntPtr> GetWindows(string windowName = null)
        {
            Process[] processlist = Process.GetProcesses();

            foreach (Process process in processlist)
            {
                if (!String.IsNullOrEmpty(process.MainWindowTitle))
                {
                    if (string.IsNullOrEmpty(windowName) || (!string.IsNullOrEmpty(windowName) && windowName == process.MainWindowTitle))
                        yield return process.MainWindowHandle;
                }
            }
        }

    }

}
