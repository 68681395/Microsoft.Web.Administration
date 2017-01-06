// Copyright (c) Lex Li. All rights reserved. 
//  
// Licensed under the MIT license. See LICENSE file in the project root for full license information. 


using System;
using System.Collections.Generic;
using System.Configuration;
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

            var evs = GetEnvironmentVariables(site);
            if (evs != null)
            {
                startInfo.EnvironmentVariables["LAUNCHER_PATH"] = evs.Item1;
                startInfo.EnvironmentVariables["LAUNCHER_ARGS"] = evs.Item2;
            }

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

        private static Tuple<string, string> GetEnvironmentVariables(Site site)
        {
            var filename = site.GetWebConfiguration()?.FileContext?.FileName;
            if (File.Exists(filename))
            {
                var webconfig = ConfigurationManager.OpenMappedExeConfiguration(new ExeConfigurationFileMap() { ExeConfigFilename = filename }, ConfigurationUserLevel.None);

                var lunch = webconfig.AppSettings?.Settings["Jexus_LAUNCHER_PATH"]?.Value;
                var args = webconfig.AppSettings?.Settings["Jexus_LAUNCHER_ARGS"]?.Value;
                if (!string.IsNullOrEmpty(lunch) || !string.IsNullOrEmpty(args))
                {
                    return Tuple.Create(lunch, args);
                }
            }

            var projectPath = site.Applications[0].VirtualDirectories[0].PhysicalPath.ExpandIisExpressEnvironmentVariables();
            var binPath = Path.Combine(projectPath, "bin");
            var folderName = new DirectoryInfo(projectPath).Name;
            // search dll of the project folder name
            var targetFiles = Directory.EnumerateFiles(binPath, $"{folderName}.dll", SearchOption.AllDirectories);
            if (targetFiles.Any())
            {
                //netcore for cross platform
                return Tuple.Create("dotnet", targetFiles.LastOrDefault());
            }
            else
            {
                // search exe of the project folder name
                targetFiles = Directory.EnumerateFiles(binPath, $"{folderName}.exe", SearchOption.AllDirectories);
                if (targetFiles.Any())
                {
                    //netcore for windows
                    return Tuple.Create(targetFiles.LastOrDefault(x => File.Exists(x + ".config")), "");
                }
            }
            // search dll of the site name 
            targetFiles = Directory.EnumerateFiles(binPath, $"{site.Name}.dll", SearchOption.AllDirectories);
            if (targetFiles.Any())
            {
                //netcore for cross platform
                return Tuple.Create("dotnet", targetFiles.LastOrDefault());
            }
            else
            {
                // search exe of the site name 
                targetFiles = Directory.EnumerateFiles(binPath, $"{site.Name}.exe", SearchOption.AllDirectories);
                if (targetFiles.Any())
                {
                    //netcore for windows
                    return Tuple.Create(targetFiles.LastOrDefault(x => File.Exists(x + ".config")), "");
                }
            }

            return null;
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
