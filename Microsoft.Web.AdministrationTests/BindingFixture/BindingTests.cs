﻿using System;
using System.IO;
using Microsoft.Web.Administration;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Web.AdministrationTests.BindingFixture
{
    public class BindingTests
    {

        private readonly ITestOutputHelper output;
        public BindingTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact()]
        public void BindingTest()
        {
          

            const string Current = @"BindingFixture\\applicationHost.config";
            var basePath = Directory.GetCurrentDirectory();
            var directoryName = basePath;

#if IIS
            var server = new IisServerManager(Path.Combine(directoryName, Current));
#else
            var server = new IisExpressServerManager(Path.Combine(directoryName, Current));
#endif
            var config = server.GetApplicationHostConfiguration();
            var section = config.GetSection("configProtectedData");
            Assert.Equal("RsaProtectedConfigurationProvider", section["defaultProvider"]);
            var collection = section.GetCollection("providers");
            Assert.Equal(7, collection.Count);
            TestCases.TestIisBindingFixture(server, output);
        }

        [Fact()]
        public void ToStringTest()
        {

        }
    }
}