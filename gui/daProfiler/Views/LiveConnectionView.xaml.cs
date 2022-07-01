using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using System.Windows.Threading;
using Profiler.Data;
using Frame = Profiler.Data.Frame;
using Microsoft.Win32;
using System.Xml;
using System.Net.Cache;
using System.Reflection;
using System.Diagnostics;
using System.Web;
using System.Net.NetworkInformation;
using System.ComponentModel;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Security;
using Profiler.Controls;
using Profiler.DirectX;
using System.Windows.Controls.Primitives;

namespace Profiler.Views
{

	/// <summary>
	/// Interaction logic for LiveConnectionView.xaml
	/// </summary>
	public partial class LiveConnectionView : UserControl, Profiler.LiveFramesViewProcessor
	{
		private void UpdateSurface(){surface.Update();}
		DynamicMesh LiveMeshForeground;
		private void InitLiveMesh()
		{
			if (LiveMeshForeground == null)
			{
				LiveMeshForeground = surface.CreateMesh();
				LiveMeshForeground.Projection = Mesh.ProjectionType.Pixel;
			}
		}

		private void UpdateLiveMesh(DirectX.DirectXCanvas canvas, List<float> liveFrames, uint current_available)
		{
			int framesToDraw = Math.Min(liveFrames.Count, 256);
			if (framesToDraw > 0)
			{
				InitLiveMesh();
				double width = surface.ActualWidth * RenderSettings.dpiScaleX, height = (surface.ActualHeight-4) * RenderSettings.dpiScaleY;
				int startFrame = Math.Max(0, liveFrames.Count - framesToDraw);
				double avg = 0, maxDuration, minDuration;
				maxDuration = minDuration = liveFrames[startFrame];
				for (int i = startFrame, e = i + framesToDraw; i < e; ++i)
				{
					double time = liveFrames[i];
					avg += time;
					maxDuration = Math.Max(maxDuration, time);
					minDuration = Math.Min(minDuration, time);
				}
				avg /= framesToDraw;
				double std = 0;
				double variance = 0;
				for (int i = startFrame, e = i + framesToDraw; i < e; ++i)
					variance += (liveFrames[i] - avg) * (liveFrames[i] - avg);
				variance /= framesToDraw;
				std = Math.Sqrt(variance);
				double scaleDuration = Math.Max(0.0001, avg * 2.0);

				double rw = RenderSettings.dpiScaleY;
				double livePos = 2*rw, at = livePos;
				for (int i = startFrame, e = i + framesToDraw; i < e; ++i, at += rw)
				{
					double time = liveFrames[i];
					double h = height*liveFrames[i] / scaleDuration;
					LiveMeshForeground.AddRect(new Rect(at, height - h, rw, h), time < scaleDuration ? Colors.Green : Colors.Red);
				}
				double textSz = 14*RenderSettings.dpiScaleY;
				canvas.Text.Draw(new Point(livePos, height - textSz*2), FormattableString.Invariant($"frames: {current_available:000}"), Colors.White, TextAlignment.Left, width);
				double pos = 2*rw;
				canvas.Text.Draw(new Point(livePos + pos, height - textSz), FormattableString.Invariant($"avg: {avg:0.#}"), Colors.White, TextAlignment.Left, width - pos); pos += 8 * 8*rw;
				canvas.Text.Draw(new Point(livePos + pos, height - textSz), FormattableString.Invariant($"min: {minDuration:0.#}"), Colors.White, TextAlignment.Left, width - pos); pos += 8 * 8*rw;
				canvas.Text.Draw(new Point(livePos + pos, height - textSz), FormattableString.Invariant($"max: {maxDuration:0.#}"), Colors.White, TextAlignment.Left, width - pos); pos += 8 * 8*rw;
				canvas.Text.Draw(new Point(livePos + pos, height - textSz), FormattableString.Invariant($"std: {std:0.#}"), Colors.White, TextAlignment.Left, width - pos); pos += 8 * 8*rw;
			}
			if (LiveMeshForeground == null)
				return;
			LiveMeshForeground.Update(canvas.RenderDevice);
			canvas.Draw(LiveMeshForeground);
		}
		void OnDraw(DirectX.DirectXCanvas canvas, DirectXCanvas.Layer layer)
		{
			if (layer == DirectXCanvas.Layer.Foreground)
			{
				UpdateLiveMesh(canvas, currentLiveFrames, currentFramesAvailableForDump);
			}
		}
//==================================================================================================================================

		public LiveConnectionView()
		{
			//this.InitializeComponent();
			//this.DataContext = currentLiveFrames;
			this.InitializeComponent();

            ProfilerClient.Get().ConnectionChanged += LiveConnection_ConnectionChanged;
			surface.OnDraw += OnDraw;
		}
		public void process(BinaryReader reader)
		{
			uint available = reader.ReadUInt32();
			uint framesToRead = reader.ReadUInt32();
			lock (currentLiveFrames)
			{
				Show();
				for (uint i = 0; i < framesToRead; ++i)
					currentLiveFrames.Add(reader.ReadSingle());
				int maxFramesHistory = 512;
				if (currentLiveFrames.Count > maxFramesHistory)
					currentLiveFrames.RemoveRange(0, currentLiveFrames.Count - maxFramesHistory);
				currentFramesAvailableForDump = available;
			}
			Application.Current.Dispatcher.BeginInvoke(new Action(() => UpdateSurface()));
		}
		public List<float> currentLiveFrames = new List<float>();
		uint currentFramesAvailableForDump = 0;
		private void LiveConnection_ConnectionChanged(IPAddress address, UInt16 port, ProfilerClient.State state, String message)
		{
			switch (state)
			{
				case ProfilerClient.State.Connecting:
					break;

				case ProfilerClient.State.Disconnected:
					Clear();
					Application.Current.Dispatcher.BeginInvoke(new Action(() => UpdateSurface()));
					break;

				case ProfilerClient.State.Connected:
					break;
			}
		}
		private void Hide()
        {
			//surface.Visibility = Visibility.Hidden;
			Visibility = Visibility.Collapsed;
		}
		private void Show()
		{
			//surface.Visibility = Visibility.Hidden;
			Visibility = Visibility.Visible;
		}
		public void Clear()
		{
			lock (currentLiveFrames) { currentLiveFrames.Clear(); Hide();}
		}
	}
}
