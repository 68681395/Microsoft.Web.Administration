// Copyright (c) Lex Li. All rights reserved.
// 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Web.Administration;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Web.AdministrationTests.BindingFixture
{
    public static class TestCases
    {
        public static void TestIisBindingFixture(IisServerManager server, ITestOutputHelper output)
        {
            Assert.Equal(9, server.ApplicationPools.Count);
            Assert.True(server.ApplicationPools.AllowsAdd);
            Assert.False(server.ApplicationPools.AllowsClear);
            Assert.False(server.ApplicationPools.AllowsRemove);
            Assert.Equal(
                new[] { '\\', '/', '"', '|', '<', '>', ':', '*', '?', ']', '[', '+', '=', ';', ',', '@', '&' },
                ApplicationPoolCollection.InvalidApplicationPoolNameCharacters());

            Assert.Equal(4, server.Sites.Count);
            Assert.True(server.Sites.AllowsAdd);
            Assert.False(server.Sites.AllowsClear);
            Assert.False(server.Sites.AllowsRemove);
            Assert.Equal(
                new[] { '\\', '/', '?', ';', ':', '@', '&', '=', '+', '$', ',', '|', '"', '<', '>' },
                SiteCollection.InvalidSiteNameCharacters());

            var siteDefaults = server.SiteDefaults;
            //Assert.Equal("[http] :8080:localhost", server.Sites[0].Bindings[0].ToString());

            /*
             * Default Website Bindings
              <binding protocol="net.tcp" bindingInformation="808:*" />
              <binding protocol="net.msmq" bindingInformation="localhost" />
              <binding protocol="msmq.formatname" bindingInformation="localhost" />
              <binding protocol="net.pipe" bindingInformation="*:80" />
              <binding protocol="net.tcp" bindingInformation="*:808" />
              <binding protocol="http" bindingInformation="*:808:" />
              <binding protocol="https" bindingInformation="169.254.224.100:443:" sslFlags="0" />
              <binding protocol="http" bindingInformation="169.254.224.100:80:www.tsharp.org" />
             */
            var bindings = server.Sites[0].Bindings;

            string[] expecteds = {
                "808:* (net.tcp)",
                "localhost (net.msmq)",
                "localhost (msmq.formatname)",
                "*:80 (net.pipe)",
                "*:808 (net.tcp)",
                "*:808 (http)",
                "169.254.224.100:443 (https)",
                "www.tsharp.org on 169.254.224.100:80 (http)"
            };

            for (int i = 0, n = bindings.Count; i < n; i++)
            {
                Assert.Equal(expecteds[i], bindings[i].ToShortString());
            }

            foreach (Site siteItem in server.SiteCollection)
            {
                output.WriteLine(siteItem.Name);
                for ( int i =0,n =siteItem.Bindings.Count;i<n;i++)
                {
                    Binding siteItemBinding = siteItem.Bindings[i];
                    output.WriteLine(
                        $"-#{i} \t{siteItemBinding.ToUri()}\t\t\t>{siteItemBinding.ToShortString()}\t\t> Binding:{siteItemBinding.BindingInformation}"
                        );
                    
                }
            }

            Assert.Equal(LogFormat.W3c, siteDefaults.LogFile.LogFormat);
            Assert.False(siteDefaults.TraceFailedRequestsLogging.Enabled);
            Assert.True(siteDefaults.LogFile.Enabled);
            Assert.Equal("DefaultAppPool", server.ApplicationDefaults.ApplicationPoolName);

            var pool = server.ApplicationPools[0];

            var model = pool.GetChildElement("processModel");
            var item = model["maxProcesses"];
            Assert.Equal("DefaultAppPool", pool.Name);
            Assert.Equal("v4.0", pool.ManagedRuntimeVersion);
            Assert.Equal(1, pool.ProcessModel.MaxProcesses);
            Assert.Equal(string.Empty, pool.ProcessModel.UserName);
#if IIS
            Assert.Equal(1U, item);
#else
            Assert.Equal(1U, item);
#endif

            // TODO: why it should be int.
#if IIS
            Assert.Equal(0L, pool.GetAttributeValue("managedPipelineMode"));
            pool.SetAttributeValue("managedPipelineMode", 1);
            Assert.Equal(1L, pool.GetAttributeValue("managedPipelineMode"));
            pool.SetAttributeValue("managedPipelineMode", "Integrated");
            Assert.Equal(0L, pool.GetAttributeValue("managedPipelineMode"));
#else
            Assert.Equal(0L, pool.GetAttributeValue("managedPipelineMode"));
            pool.SetAttributeValue("managedPipelineMode", 1);
            Assert.Equal(1L, pool.GetAttributeValue("managedPipelineMode"));
            pool.SetAttributeValue("managedPipelineMode", "Integrated");
            Assert.Equal(0L, pool.GetAttributeValue("managedPipelineMode"));

            Assert.Equal(14, pool.ApplicationCount);
#endif
            var name = Assert.Throws<COMException>(() => pool.SetAttributeValue("name", ""));
            Assert.Equal("Invalid application pool name\r\n", name.Message);

            var time = Assert.Throws<COMException>(() => pool.ProcessModel.IdleTimeout = TimeSpan.MaxValue);
            Assert.Equal("Timespan value must be between 00:00:00 and 30.00:00:00 seconds inclusive, with a granularity of 60 seconds\r\n", time.Message);

            var site = server.Sites[0];
            Assert.Equal("Default Web Site", site.Name);
            Assert.Equal(8, site.Bindings.Count);
            var binding = site.Bindings[5];
            Assert.Equal(IPAddress.Any, binding.EndPoint.Address);
            Assert.Equal(808, binding.EndPoint.Port);
            Assert.Equal("", binding.Host);
            Assert.Equal("*:808:", binding.ToString());
            Assert.Equal("*:808:", binding.BindingInformation);
            Assert.True(site.Bindings.AllowsAdd);
            Assert.True(site.Bindings.AllowsClear);
            Assert.False(site.Bindings.AllowsRemove);
            Assert.Equal(null, site.LogFile.Directory);

            var sslSite = server.Sites[3];
            var sslBinding = sslSite.Bindings[1];
            Assert.Equal(SslFlags.None, sslBinding.SslFlags);

            var app = site.Applications[0];
            Assert.True(site.Applications.AllowsAdd);
            Assert.False(site.Applications.AllowsClear);
            Assert.False(site.Applications.AllowsRemove);
            Assert.Equal(
                new[] { '\\', '?', ';', ':', '@', '&', '=', '+', '$', ',', '|', '"', '<', '>', '*' },
                ApplicationCollection.InvalidApplicationPathCharacters());
            Assert.Equal("DefaultAppPool", app.ApplicationPoolName);

            Assert.Equal("/", app.Path);
            var vDir = app.VirtualDirectories[0];
            Assert.True(app.VirtualDirectories.AllowsAdd);
            Assert.False(app.VirtualDirectories.AllowsClear);
            Assert.False(app.VirtualDirectories.AllowsRemove);
            Assert.Equal(
                new[] { '\\', '?', ';', ':', '@', '&', '=', '+', '$', ',', '|', '"', '<', '>', '*' },
                VirtualDirectoryCollection.InvalidVirtualDirectoryPathCharacters());


            {
                var config = server.GetApplicationHostConfiguration();
                var section = config.GetSection("system.applicationHost/log");
                var mode = section.Attributes["centralLogFileMode"];
#if IIS
                Assert.Equal(0L, mode.Value);
#else
                Assert.Equal(0L, mode.Value);
#endif
                var encoding = section.Attributes["logInUTF8"];
                Assert.Equal(true, encoding.Value);
                ConfigurationSection httpLoggingSection = config.GetSection("system.webServer/httpLogging");
                Assert.Equal(false, httpLoggingSection["dontLog"]);

                ConfigurationSection defaultDocumentSection = config.GetSection("system.webServer/defaultDocument");
                Assert.Equal(true, defaultDocumentSection["enabled"]);
                ConfigurationElementCollection filesCollection = defaultDocumentSection.GetCollection("files");
                Assert.Equal(7, filesCollection.Count);

                var errorsSection = config.GetSection("system.webServer/httpErrors");
                var errorsCollection = errorsSection.GetCollection();
                Assert.Equal(9, errorsCollection.Count);

                var anonymousSection = config.GetSection("system.webServer/security/authentication/anonymousAuthentication");
                var anonymousEnabled = (bool)anonymousSection["enabled"];
                Assert.True(anonymousEnabled);
                var value = anonymousSection["password"];
                Assert.Equal(string.Empty, value);

                var windowsSection = config.GetSection("system.webServer/security/authentication/windowsAuthentication");
                var windowsEnabled = (bool)windowsSection["enabled"];
                Assert.False(windowsEnabled);

                windowsSection["enabled"] = true;
                server.CommitChanges();
            }

            {
                var config = server.GetApplicationHostConfiguration();
                var locations = config.GetLocationPaths();
                Assert.Equal(1, locations.Length);


                var anonymousSection = config.GetSection(
                    "system.webServer/security/authentication/anonymousAuthentication",
                    "vcdesign");
                var anonymousEnabled = (bool)anonymousSection["enabled"];
                Assert.True(anonymousEnabled);
                Assert.Equal("IUSR", anonymousSection["userName"]);

                // Assert.Equal("123456", anonymousSection["password"]);
            }

            {
                // server config "Website1"
                var config = server.GetApplicationHostConfiguration();

                // enable Windows authentication
                var windowsSection = config.GetSection("system.webServer/security/authentication/windowsAuthentication", "virtocommerce.cn");
                Assert.Equal(OverrideMode.Inherit, windowsSection.OverrideMode);
                Assert.Equal(OverrideMode.Deny, windowsSection.OverrideModeEffective);
                Assert.Equal(false, windowsSection.IsLocked);
                Assert.Equal(true, windowsSection.IsLocallyStored);

                var windowsEnabled = (bool)windowsSection["enabled"];
                Assert.True(windowsEnabled);
                windowsSection["enabled"] = false;
                Assert.Equal(false, windowsSection["enabled"]);

                {
                    // disable logging. Saved in applicationHost.config, as it cannot be overridden in web.config.
                    ConfigurationSection httpLoggingSection = config.GetSection("system.webServer/httpLogging", "virtocommerce.cn");
                    Assert.Equal(false, httpLoggingSection["dontLog"]);
                    httpLoggingSection["dontLog"] = true;
                }

                {
                    ConfigurationSection httpLoggingSection = config.GetSection("system.webServer/httpLogging", "virtocommerce.cn/test");
                    // TODO:
                    // Assert.Equal(true, httpLoggingSection["dontLog"]);
                    httpLoggingSection["dontLog"] = false;
                }
            }

            var siteName = Assert.Throws<COMException>(() => server.Sites[0].Name = "");
            Assert.Equal("Invalid site name\r\n", siteName.Message);

            var limit = Assert.Throws<COMException>(() => server.Sites[0].Limits.MaxBandwidth = 12);
            Assert.Equal("Integer value must not be between 0 and 1023 inclusive\r\n", limit.Message);

            var appPath = Assert.Throws<COMException>(() => server.Sites[0].Applications[0].Path = "");
            Assert.Equal("Invalid application path\r\n", appPath.Message);

            var vDirPath =
                Assert.Throws<COMException>(() => server.Sites[0].Applications[0].VirtualDirectories[0].Path = "");
            Assert.Equal("Invalid virtual directory path\r\n", vDirPath.Message);





            server.CommitChanges();
        }
        
    }
}
