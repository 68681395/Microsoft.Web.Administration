// Copyright (c) Lex Li. All rights reserved. 
//  
// Licensed under the MIT license. See LICENSE file in the project root for full license information. 


using Microsoft.Build.Evaluation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Microsoft.Web.Administration
{
    public sealed class IisExpressServerManager : ServerManager
    {
        public IisExpressServerManager(bool readOnly, string applicationHostConfigurationPath)
            : base(readOnly, applicationHostConfigurationPath)
        {
            Mode = WorkingMode.IisExpress;
        }

        public IisExpressServerManager(string applicationHostConfigurationPath)
            : this(false, applicationHostConfigurationPath)
        {
        }

        internal override async Task<bool> GetSiteStateAsync(Site site)
        {
            var items = Process.GetProcessesByName("iisexpress");
            return items.Any(item =>
         item.GetCommandLine().EndsWith(site.CommandLine, StringComparison.Ordinal));
            // return found.Any();
        }

        internal override async Task<bool> GetPoolStateAsync(ApplicationPool pool)
        {
            return true;
        }

        private ProjectCollection projects = new ProjectCollection();
        internal override async Task StartAsync(Site site)
        {
            var name = site.Applications[0].ApplicationPoolName;
            var pool = ApplicationPools.FirstOrDefault(item => item.Name == name);
            var fileName =
                Path.Combine(
                    Environment.GetFolderPath(
                        pool != null && pool.Enable32BitAppOnWin64
                            ? Environment.SpecialFolder.ProgramFilesX86
                            : Environment.SpecialFolder.ProgramFiles),
                    "IIS Express",
                    "iisexpress.exe");
            if (!File.Exists(fileName))
            {
                fileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "IIS Express",
                    "iisexpress.exe");
            }
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = site.CommandLine,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            var projectPath = site.Applications[0].VirtualDirectories[0].PhysicalPath.ExpandIisExpressEnvironmentVariables();
            var projectFullPath = Directory.EnumerateFiles(projectPath, "*.*proj").Where(x=>x.Contains(site.Name)).FirstOrDefault();
            if (!string.IsNullOrEmpty(projectFullPath))
            { 
                var p = projects.LoadProject(projectFullPath);
                
                foreach (var i in p.Properties)
                {
                    Debug.WriteLine($"{i.Name} \t={i.EvaluatedValue}");
                }
            }


            startInfo.EnvironmentVariables["LAUNCHER_PATH"] = "";
            startInfo.EnvironmentVariables["LAUNCHER_ARGS"] = "";
            var process = new Process
            {
                StartInfo = startInfo
            };
            try
            {
                process.Start();
                process.WaitForExit(5000);
                if (process.HasExited)
                {
                    throw new InvalidOperationException("process terminated");
                }

                site.State = ObjectState.Started;
            }
            catch (Exception ex)
            {
                throw new COMException(
                    string.Format("cannot start site: {0}, {1}", ex.Message, process.StandardOutput.ReadToEnd()));
            }
            finally
            {
                site.State = process.HasExited ? ObjectState.Stopped : ObjectState.Started;
            }
        }

        internal override async Task StopAsync(Site site)
        {
            var items = Process.GetProcessesByName("iisexpress");
            var found = items.Where(item =>
                item.GetCommandLine().EndsWith(site.CommandLine, StringComparison.Ordinal));
            foreach (var item in found)
            {
                item.Kill();
                item.WaitForExit();
            }

            site.State = ObjectState.Stopped;
        }

        internal override IEnumerable<string> GetSchemaFiles()
        {
            var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "IIS Express",
                    "config",
                    "schema");
            if (Directory.Exists(directory))
            {
                return Directory.GetFiles(directory);
            }

            // IMPORTANT: for x86 IIS 7 Express
            var x86 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "IIS Express",
                "config",
                "schema");
            return Directory.Exists(x86) ? Directory.GetFiles(x86) : base.GetSchemaFiles();
        }
    }
}
