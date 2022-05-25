using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Profiler.Data;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using System.Windows;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace Profiler
{
	public class ProfilerClient
	{
		private Object criticalSection = new Object();
		private static ProfilerClient profilerClient = new ProfilerClient();

		ProfilerClient()
		{

		}

		private void Reconnect(bool reuse_socket = true)
		{
			if (client.Client.Connected)
				client.Client.Disconnect(reuse_socket);

			client.Close();
			client = new TcpClient();
		}

		public IPAddress IpAddress
		{
			get { return ipAddress; }
			set
			{
				if (!value.Equals(ipAddress))
				{
					ipAddress = value;
					Reconnect();
				}
			}
		}

		public UInt16 Port
		{
			get { return port; }
			set
			{
				if (port != value)
				{
					port = value;
					Reconnect();
				}
			}
		}

		public static ProfilerClient Get() { return profilerClient; }

		TcpClient client = new TcpClient();
		#region SocketWork

		public DataResponse RecieveMessage()
		{
			try
			{
				NetworkStream stream = null;

				lock (criticalSection)
				{
					if (!client.Connected)
						return null;

					stream = client.GetStream();
				}

				return DataResponse.Create(stream, IpAddress, Port);
			}
			catch (System.IO.IOException ex)
			{
				lock (criticalSection)
				{
					if (clientStatus == ClientStatus.Active)//if we asked for disconnect, it is normal to not be able to finish 
					{
						Application.Current.Dispatcher.BeginInvoke(new Action(() =>
						{
							ConnectionChanged?.Invoke(IpAddress, Port, State.Disconnected, ex.Message);
						}));

					}

					Reconnect();
				}
			}

			return null;
		}

		private IPAddress ipAddress;
		private UInt16 port = UInt16.MaxValue;

		const UInt16 PORT_RANGE = 4;
		public bool IsConnected()
		{
			lock (criticalSection)
			{
				return client.Connected;
			}
		}

		private bool CheckConnection()
		{
			if (!client.Connected)
			{
				for (UInt16 currentPort = port; currentPort < port + PORT_RANGE; ++currentPort)
				{
					try
					{
						Application.Current.Dispatcher.BeginInvoke(new Action(() =>
						{
							ConnectionChanged?.Invoke(IpAddress, currentPort, State.Connecting, String.Empty);
						}));
						var task = client.ConnectAsync(ipAddress, currentPort);
						var waited = task.Wait(TimeSpan.FromSeconds(2));
						if (client.Connected)
						{
							Application.Current.Dispatcher.BeginInvoke(new Action(() =>
							{
								ConnectionChanged?.Invoke(ipAddress, currentPort, State.Connected, String.Empty);
							}));
							clientStatus = ClientStatus.Active;
							return true;
						}
						else
						{
							client = new TcpClient();
						}
					}
					catch (SocketException ex)
					{
						Debug.Print(ex.Message);
					}
				}
				Application.Current.Dispatcher.BeginInvoke(new Action(() =>
				{
					ConnectionChanged?.Invoke(ipAddress, port, State.Disconnected, "Can't connect");
				}));
			}
			return false;
		}

		public enum State
		{
			Connecting,
			Connected,
			Disconnected,
		}
		public delegate void ConnectionStateEventHandler(IPAddress address, UInt16 port, State state, String message);
		public event ConnectionStateEventHandler ConnectionChanged;

		public bool SendMessage(Message message, bool autoconnect = false)
		{
			try
			{
				lock (criticalSection)
				{
					if (!client.Connected && !autoconnect)
						return false;
					CheckConnection();
					if (!client.Connected)
						return false;
					MemoryStream buffer = new MemoryStream();
					message.Write(new BinaryWriter(buffer));
					buffer.Flush();

					UInt32 length = (UInt32)buffer.Length;

					NetworkStream stream = client.GetStream();

					BinaryWriter writer = new BinaryWriter(stream);
					writer.Write(Message.MESSAGE_MARK);
					writer.Write(length);

					buffer.WriteTo(stream);
					stream.Flush();
				}

				return true;
			}
			catch (Exception ex)
			{
				lock (criticalSection)
				{
					if (clientStatus == ClientStatus.Active)//if we asked for disconnect, it is normal to not be able to finish 
					{
						Application.Current.Dispatcher.BeginInvoke(new Action(() =>
						{
							ConnectionChanged?.Invoke(IpAddress, Port, State.Disconnected, ex.Message);
						}));
					}

					Reconnect();
				}
			}

			return false;
		}
		enum ClientStatus { Closing, Closed, Active};
		ClientStatus clientStatus = ClientStatus.Closed;
		public void Close()
		{
			lock (criticalSection)
			{
				clientStatus = ClientStatus.Closing;
				SendMessage(new DisconnectMessage());
				Reconnect(false);
				clientStatus = ClientStatus.Closed;
				//ConnectionChanged?.Invoke(IpAddress, Port, State.Disconnected, String.Empty);
				Application.Current.Dispatcher.BeginInvoke(new Action(() =>
				{
					ConnectionChanged?.Invoke(IpAddress, Port, State.Disconnected, String.Empty);
				}));
			}
		}

		#endregion
	}
}
