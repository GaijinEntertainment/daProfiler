using Profiler.Data;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Xml.Serialization;

namespace Profiler
{
	public class Platform
	{
		public enum Type
		{
			unknown,
			win32,
			win64,
			iOS,
			android,
			macosx,
			ps3,
			ps4,
			xbox360,
			linux64,
			linux32,
			xboxOne,
			xboxScarlett,
			tvOS,
			nswitch,
			ps5
		}

		public class Connection
		{
			public Platform.Type Target { get; set; }
			public string Name { get; set; }
            public string Address { get; set; }
            public UInt16 Port { get; set; }
            [XmlIgnore]
            public SecureString Password { get; set; }
            [XmlElement("Password")]
            public string PasswordForXml
            {
                get { return Utils.GetUnsecureBase64String(Password); }
                set { Password = Utils.GetSecureStringFromBase64String(value); }
            }
        }

		public static IPAddress GetPS4Address()
		{
			return IPAddress.None;
		}

		public static IPAddress GetXONEAddress()
		{
			return IPAddress.None;
		}

		public static List<IPAddress> GetPCAddresses()
		{
			List<IPAddress> result = new List<IPAddress>();
			foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
				if (ip.AddressFamily == AddressFamily.InterNetwork)
					result.Add(ip);

			if (result.Count == 0)
				result.Add(IPAddress.Parse("127.0.0.1"));

			return result;
		}
	}
}
