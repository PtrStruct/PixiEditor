﻿using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace PixiEditor.Models.Processes
{
    public static class ProcessHelper
    {
        public static Process RunAsAdmin(string path)
        {
            Process proc = new Process();
            try
            {
                proc.StartInfo.FileName = path;
                proc.StartInfo.Verb = "runas";
                proc.StartInfo.UseShellExecute = true;
                proc.Start();
            }
            catch (Win32Exception)
            {
                throw;
            }

            return proc;
        }

        public static bool IsRunningAsAdministrator()
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
