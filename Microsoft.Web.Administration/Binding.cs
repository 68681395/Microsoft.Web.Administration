// Copyright (c) Lex Li. All rights reserved.
// 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Web.Administration;
using Microsoft.Web.Management.Utility;

namespace Microsoft.Web.Administration
{
    public sealed class Binding : ConfigurationElement
    {
        private IPEndPoint _endPoint;
        private string _host;

        private bool _initialized;
        private bool _isIpPortHostBinding;

        public Binding(ConfigurationElement element, BindingCollection parent)
            : base(element, null, null, parent, null, null)
        {
            Parent = parent;
        }

        internal Binding(string protocol, string bindingInformation, byte[] hash, string store, SslFlags flags, BindingCollection parent)
            : base(null, "binding", null, parent, null, null)
        {
            BindingInformation = bindingInformation;
            Protocol = protocol;
            CertificateHash = hash;
            CertificateStoreName = store;
            SslFlags = flags;
            Parent = parent;
        }

        public override string ToString()
        {
            return string.Format(
                "{0}:{1}:{2}",
                this.EndPoint?.Address?.AddressToDisplay(),
                EndPoint?.Port,
                Host.HostToDisplay());
        }

        public string BindingInformation
        {
            get
            {
                Initialize();
                return (string)this["bindingInformation"];
            }

            set
            {
                this["bindingInformation"] = value;
                _initialized = false;
            }
        }

        private bool IsHttp
        {
            get
            {
                return string.Equals(this.Protocol, UriHelper.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
            }
        }
        internal static class UriHelper
        {
            public static readonly string UriSchemeHttp = "http";

            public static readonly string UriSchemeHttps = "https";
        }
        private bool IsHttps
        {
            get
            {
                return string.Equals(this.Protocol, UriHelper.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void LoadBindingInfo()
        {
            this.IsIPPortHostBinding = false;
            this._host = string.Empty;
            this._endPoint = null;
            var bindingInfo = (string)this["bindingInformation"];
            if (string.IsNullOrEmpty(bindingInfo))
            {
                return;
            }
            //if (this.IsHttp || this.IsHttps)
            {
                string empty = string.Empty;
                IPEndPoint iPEndPoint = BindingUtility.EndPointFromBindingInformation(bindingInfo, out empty);
                if (iPEndPoint != null)
                {
                    this._host = empty;
                    this._endPoint = iPEndPoint;
                    this.IsIPPortHostBinding = true;
                }
                else
                    this._endPoint = new IPEndPoint(IPAddress.Any, 80);
            }
        }

        private void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            LoadBindingInfo();

            //var value = (string)this["bindingInformation"];
            //var last = value.LastIndexOf(':');
            //_host = value.Substring(last + 1);
            //var next = last > 0 ? value.LastIndexOf(':', last - 1) : -1;
            //var length = last - next - 1;
            //var port = length > 0 ? value.Substring(next + 1, length) : string.Empty;
            //var address = next > 0 ? value.Substring(0, next) : string.Empty;
            //_endPoint = new IPEndPoint(address.DisplayToAddress(), Int32.Parse(port));
            if (Protocol != "https" || CertificateHash != null)
            {
                return;
            }

            if (Helper.IsRunningOnMono())
            {
                // TODO: how to do it on Mono?
                return;
            }

            if (this.GetIsSni())
            {
                var sni = NativeMethods.QuerySslSniInfo(new Tuple<string, int>(_host, _endPoint.Port));
                if (sni != null)
                {
                    this.CertificateHash = sni.Hash;
                    this.CertificateStoreName = sni.StoreName;
                    return;
                }
            }

            var certificate = NativeMethods.QuerySslCertificateInfo(_endPoint);
            if (certificate != null)
            {
                CertificateHash = certificate.Hash;
                CertificateStoreName = certificate.StoreName;
            }
        }

        public byte[] CertificateHash { get; set; }
        public string CertificateStoreName { get; set; }

        public IPEndPoint EndPoint
        {
            get
            {
                Initialize();
                return _endPoint;
            }
        }

        public string Host
        {
            get
            {
                Initialize();
                return _host;
            }
        }

        // ReSharper disable once InconsistentNaming
        public bool IsIPPortHostBinding
        {
            get
            {
                Initialize();
                return _isIpPortHostBinding;
            }
            internal set { _isIpPortHostBinding = value; }
        }

        public string Protocol
        {
            get
            {
                Initialize();
                return (string)this["protocol"];
            }

            set
            {
                this["protocol"] = value;
                _initialized = false;
            }
        }

        public SslFlags SslFlags
        {
            get
            {
                Initialize();
                return (SslFlags)Enum.ToObject(typeof(SslFlags), this["sslFlags"]);
            }

            set
            {
                this["sslFlags"] = (uint)value;
                _initialized = false;
            }
        }

        public bool UseDsMapper { get; set; }

        internal BindingCollection Parent { get; private set; }

        internal string ToUri()
        {
            var address = Host;
            if (string.IsNullOrEmpty(address))
            {
                address = IPAddress.Any.Equals(EndPoint?.Address)
               ? Parent.Parent.Parent.Parent.HostName.ExtractName()
               : EndPoint?.AddressFamily == AddressFamily.InterNetwork
                   ? EndPoint?.Address?.ToString()
                   : string.Format("[{0}]", EndPoint?.Address);
            } 
            return IsDefaultPort
                ? string.Format("{0}://{1}", Protocol, address)
                : string.Format("{0}://{1}:{2}", Protocol, address, EndPoint?.Port);
        }

        private bool IsDefaultPort
        {
            get
            {
                if (Protocol == "http")
                {
                    return EndPoint.Port == 80;
                }
                else if (Protocol == "https")
                {
                    return EndPoint.Port == 443;
                }
                else if (Protocol == "ftp")
                    return EndPoint.Port == 21;

                return false;
            }
        }
    }
}



namespace Microsoft.Web.Management.Utility
{
    internal static class BindingUtility
    {

        public static IPEndPoint EndPointFromBindingInformation(string bindingInformation, out string hostHeader)
        {
            IPEndPoint result = null;
            string strIp, strPort;
            BindingUtility.ParseIPInfoFromBindingInformation(bindingInformation, out strIp, out strPort, out hostHeader);
            int port;
            if (int.TryParse(strPort, out port))
            {
                try
                {
                    if (strIp == "*" || string.IsNullOrEmpty(strIp))
                    {
                        result = new IPEndPoint(IPAddress.Any, port);
                    }
                    else
                    {
                        IPAddress address = IPAddress.Parse(strIp);
                        result = new IPEndPoint(address, port);
                    }
                }
                catch (Exception)
                {
                }
            }


            return result;
        }

        public static bool IsIPAddressValid(string ipAddress, out string formattedIPAddressString)
        {
            formattedIPAddressString = string.Empty;
            ipAddress = ipAddress.Trim();
            if (ipAddress == "*")
            {
                formattedIPAddressString = "*";
                return true;
            }
            IPAddress iPAddress;
            if (!IPAddress.TryParse(ipAddress, out iPAddress))
            {
                return false;
            }
            formattedIPAddressString = iPAddress.ToString();
            if (iPAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                formattedIPAddressString = "[" + formattedIPAddressString + "]";
            }
            return true;
        }

        public static string ParseIPInfoFromBindingInformation(string bindingInformation, int returnItem)
        {
            string result = string.Empty;
            string result2 = string.Empty;
            string result3 = string.Empty;
            string[] array = bindingInformation.Split(new char[]
            {
                ':'
            });
            if (array.Length == 3)
            {
                result = array[0];
                result2 = array[1];
                result3 = array[2];
            }
            else if (array.Length > 2)
            {
                int length = bindingInformation.LastIndexOf(':');
                string text = bindingInformation.Substring(0, length);
                int length2 = text.LastIndexOf(':');
                result = bindingInformation.Substring(0, length2);
                result2 = array[array.Length - 2];
                result3 = array[array.Length - 1];
            }
            if (returnItem == 0)
            {
                return result;
            }
            if (returnItem == 1)
            {
                return result2;
            }
            if (returnItem == 2)
            {
                return result3;
            }
            return string.Empty;
        }

        public static void ParseIPInfoFromBindingInformation(string bindingInformation, out string ipAddress, out string port, out string hostHeader)
        {
            ipAddress = string.Empty;
            port = string.Empty;
            hostHeader = string.Empty;
            string[] array = bindingInformation.Split(new char[]
            {
                ':'
            });
            if (array.Length == 3)
            {
                ipAddress = array[0];
                port = array[1];
                hostHeader = array[2];
                return;
            }
            if (array.Length > 2)
            {
                int length = bindingInformation.LastIndexOf(':');
                string text = bindingInformation.Substring(0, length);
                int length2 = text.LastIndexOf(':');
                ipAddress = bindingInformation.Substring(0, length2);
                port = array[array.Length - 2];
                hostHeader = array[array.Length - 1];
            }
        }

        public static string ParseIPInfoFromBindingInformation(string bindingProtocol, string bindingInformation, int returnItem)
        {
            string text = CultureInfo.InvariantCulture.TextInfo.ToUpper(bindingProtocol);
            if (text.Equals("HTTP") || text.Equals("HTTPS") || text.Equals("FTP"))
            {
                return BindingUtility.ParseIPInfoFromBindingInformation(bindingInformation, returnItem);
            }
            return string.Empty;
        }

        public static bool IsCentralCertStoreBinding(SslFlags sslFlags)
        {
            return (sslFlags & SslFlags.CentralCertStore) == SslFlags.CentralCertStore;
        }

    }
}
