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

namespace Profiler
{
	public delegate void ClearAllFramesHandler();
	public interface LiveFramesViewProcessor
	{
		void process(BinaryReader reader);
	}

	/// <summary>
	/// Interaction logic for TimeLine.xaml
	/// </summary>
	public partial class TimeLine : UserControl
	{
		FrameCollection frames = new FrameCollection();
		Thread socketThread = null;
		public LiveFramesViewProcessor liveFramesView = null;

		public FrameCollection Frames
		{
			get
			{
				return frames;
			}
		}
		private SolidColorBrush _optickBackground;
		public SolidColorBrush OptickBackground { get { if (_optickBackground == null) _optickBackground = FindResource("OptickContentBackground") as SolidColorBrush; return _optickBackground; } }

		public void TimeLineViewControl()
		{
			InitializeComponent();
			UpdateScrollSize();

			LayoutUpdated += (source, args) => layoutChanged();
			surface.SizeChanged += new SizeChangedEventHandler(On_SizeChanged);
			surface.OnDraw += OnDraw;
			//CompositionTarget.Rendering += AnimationTick;
			InitInputEvent();
		}
		double scrollLeft = 0;
		private void UpdateSurface(){surface.Update();}
		void DrawSelection(DirectX.DirectXCanvas canvas){}
		void DrawHover(DirectXCanvas canvas){}
		Mesh HoverFrameMeshForeground, HoverFrameMeshBackground;
		Mesh ThreadViewTimeLineMesh;
		Mesh BackgroundMesh { get; set; }
		Mesh BoardsMesh { get; set; }
		DynamicMesh ThreadViewTimeLineFrame;
		private void InitBackgroundMesh()
		{
			if (BackgroundMesh != null)
				BackgroundMesh.Dispose();

			DynamicMesh backgroundBuilder = surface.CreateMesh();
			backgroundBuilder.Projection = Mesh.ProjectionType.Pixel;
			backgroundBuilder.AddRect(new Rect(0.0, 0, ScrollSize.Width, ScrollSize.Height), OptickBackground.Color);
			BackgroundMesh = backgroundBuilder.Freeze(surface.RenderDevice);
			InitHoverFrameMesh();
			ThreadViewTimeLineFrame = surface.CreateMesh();
			ThreadViewTimeLineFrame.Geometry = Mesh.GeometryType.Lines;
			ThreadViewTimeLineFrame.Projection = Mesh.ProjectionType.Pixel;
		}

        private void InitFrameMesh(ref Mesh mesh, Color solidColor, Color frameColor)
		{
			if (mesh != null)
				mesh.Dispose();
			DynamicMesh builder = surface.CreateMesh();
			builder.Projection = Mesh.ProjectionType.Pixel;
			builder.AddRect(new Rect(0.0, 0, FrameWidth* RenderSettings.dpiScaleX, ScrollSize.Height), solidColor);

			if (frameColor.A != 0)
			{
				builder.AddRect(new Rect(0, 0, FrameWidth, 1), frameColor);
				builder.AddRect(new Rect(0, ScrollSize.Height - 1, FrameWidth* RenderSettings.dpiScaleX, 1), frameColor);
				builder.AddRect(new Rect(0, 0, 1, ScrollSize.Height), frameColor);
				builder.AddRect(new Rect(FrameWidth* RenderSettings.dpiScaleX - 1, 0, 1, ScrollSize.Height), frameColor);
			}
			mesh = builder.Freeze(surface.RenderDevice);
			mesh.UseAlpha = true;
		}
		private void InitHoverFrameMesh()
		{
			InitFrameMesh(ref HoverFrameMeshBackground, Color.FromArgb(0xff, 0xff, 0xff, 0xff), Color.FromArgb(0xff, 0xff, 0xff, 0xff));
			InitFrameMesh(ref HoverFrameMeshForeground, Color.FromArgb(32, 0, 0xff, 0), Color.FromArgb(127, 0, 0xff, 0));
			InitFrameMesh(ref ThreadViewTimeLineMesh, Color.FromArgb(32, 0, 0xff, 0), Color.FromArgb(0, 0, 0, 0));
		}
		void OnDraw(DirectX.DirectXCanvas canvas, DirectXCanvas.Layer layer)
		{
			if (layer == DirectXCanvas.Layer.Background)
				canvas.Draw(BackgroundMesh);
			Render(canvas, layer);
		}
		Rect ScrollSize = new Rect();
		void layoutChanged()
        {
			surface.Height = Math.Max(0, this.ActualHeight - scrollBar.ActualHeight);// (FullWidth > ScrollSize.Width ? 14 : 0);
		}
		void UpdateScrollSize()
        {
			ScrollSize.Width = surface.ActualWidth * RenderSettings.dpiScaleX;
			ScrollSize.Height = surface.ActualHeight * RenderSettings.dpiScaleY;
		}
		void On_SizeChanged(object sender, EventArgs e)
		{
			UpdateScrollSize();
			InitBackgroundMesh();
		}

		class InputState
		{
			public bool IsDrag { get; set; }
			public bool IsSelect { get; set; }
			public System.Drawing.Point SelectStartPosition { get; set; }
			public System.Drawing.Point DragPosition { get; set; }
			public System.Drawing.Point MousePosition { get; set; }

			public InputState()
			{
				//MeasureInterval = new Durable();
			}
		}

		InputState Input = new InputState();
		private void InitInputEvent()
		{
			surface.RenderCanvas.MouseWheel += RenderCanvas_MouseWheel;
			surface.RenderCanvas.MouseDown += RenderCanvas_MouseDown;
			surface.RenderCanvas.MouseUp += RenderCanvas_MouseUp;
			surface.RenderCanvas.MouseMove += RenderCanvas_MouseMove;
			surface.RenderCanvas.MouseLeave += RenderCanvas_MouseLeave;

			scrollBar.Scroll += ScrollBar_Scroll;
		}
		private void RenderCanvas_MouseLeave(object sender, EventArgs e)
		{
			Mouse.OverrideCursor = null;
			Input.IsDrag = false;
			Input.IsSelect = false;
			HoverFrameAt = -1;//ToolTipPanel?.Reset();//fixme!
			UpdateSurface();
		}
		private void RenderCanvas_MouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			Input.MousePosition = e.Location;
			bool updateSurface = false;

			if (Input.IsDrag)
			{
				double deltaPixel = e.X - Input.DragPosition.X;

				scrollLeft -= deltaPixel;

				UpdateBar();
				updateSurface = true;

				Input.DragPosition = e.Location;
				HoverFrameAt = -1;
			}
			else
			{
				double clickAt = (e.Location.X + (double)scrollLeft)/RenderSettings.dpiScaleX;
				int frameAt = (int)(clickAt / (double)FrameWidthWithSpacing);
				if (frameAt < 0 || frameAt >= frames.Count)
				{
					HoverFrameAt = -1;
				}
				else
				{
					//select
					HoverFrameAt = frameAt;
				}
				updateSurface = true;
			}

			if (updateSurface)
				UpdateSurface();
		}
		private void RenderCanvas_MouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			if (e.Button == System.Windows.Forms.MouseButtons.Right)
			{
				Mouse.OverrideCursor = null;
				Input.IsDrag = false;
			}

			if (e.Button == System.Windows.Forms.MouseButtons.Left)
			{
				if (!(Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
				{
					Input.IsSelect = true;
					Input.SelectStartPosition = e.Location;
					MouseClickLeft(e);
				}
			}
		}
		private void RenderCanvas_MouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			if (e.Button == System.Windows.Forms.MouseButtons.Right)//todo: check if inside event timeline zoom
			{
				Mouse.OverrideCursor = Cursors.ScrollWE;
				Input.IsDrag = true;
				Input.DragPosition = e.Location;
			}
			else if (e.Button == System.Windows.Forms.MouseButtons.Left)
			{
			}
		}
		private void MouseClickLeft(System.Windows.Forms.MouseEventArgs args)
		{
			System.Drawing.Point e = new System.Drawing.Point(args.X, args.Y);
			double clickAt = (args.X + (double)scrollLeft)/RenderSettings.dpiScaleX;
			int frameAt = (int)(clickAt / (double)FrameWidthWithSpacing);
			if (frameAt < 0 || frameAt >= frames.Count)
            {
				//deselect?
			}
            else
            {
				//select
				SelectFrame(frameAt);
				FocusOnFrame(frames[frameAt]);
			}
		}
		int FirstFrameInSelectedBoard = 0;
		private void SelectFrame(int frame)
        {
			SelectedFrameAt = frame;
			
			for (FirstFrameInSelectedBoard = SelectedFrameAt-1; FirstFrameInSelectedBoard >= 0 && frames[frame].Group == frames[FirstFrameInSelectedBoard].Group; --FirstFrameInSelectedBoard)
			{

			}
			FirstFrameInSelectedBoard++;
		}


		private void RenderCanvas_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e){}//todo zoom
		private void UpdateBar()
		{
			double w = Math.Max(FullWidth* RenderSettings.dpiScaleX, ScrollSize.Width);
			double normalSize = ScrollSize.Width / w;
			scrollBar.Maximum = 1.0 - normalSize;
			scrollBar.ViewportSize = normalSize;
			ScrollArea.HorizontalScrollBarVisibility = w > ScrollSize.Width ? ScrollBarVisibility.Visible : ScrollBarVisibility.Auto;
			scrollLeft = Math.Max(Math.Min(scrollLeft, FullWidth* RenderSettings.dpiScaleX - ScrollSize.Width * 0.8), 0);
			scrollBar.Value = scrollLeft / w;
		}
		private void GetScrollBar()
        {
			double w = Math.Max(FullWidth * RenderSettings.dpiScaleX, ScrollSize.Width);
			scrollLeft = scrollBar.Value * w;
			UpdateSurface();
		}
		private void ScrollBar_Scroll(object sender, ScrollEventArgs e)
		{
			GetScrollBar();
		}

		
		List<Mesh> FrameRects { get; set; }
		//public FrameGroup Group { get; set; }

		//public void AddGroup(FrameGroup group)
		//{
		//	Group = group;//todo: add
		//}

		const int FramesQuant = 16;
		public static double FrameWidth = 16, FrameSpace = 2, FrameWidthWithSpacing = FrameWidth + FrameSpace * 2;
		public double FullWidth { get {return FrameWidthWithSpacing * frames.Count; } }

		static Color Epic = Color.FromArgb(0xff, 0xb0, 0x80, 0xff), Good = Color.FromArgb(0xff, 0, 0xff, 0), Okay = Color.FromArgb(0xff, 0xff, 0xa5, 0), Poor = Color.FromArgb(0xff, 0xff, 0, 0);
		private void ClearMesh()
        {
			if (FrameRects != null)
			{
				foreach (Mesh mesh in FrameRects)
					mesh.Dispose();
			}
			FrameRects = null;
			if (BoardsMesh != null)
				BoardsMesh.Dispose();
			BoardsMesh = null;

		}
		public void BuildMesh(DirectX.DirectXCanvas canvas)
		{
			int framePacks = frames.Count / FramesQuant;
			List<DynamicMesh> builder = new List<DynamicMesh>(framePacks);
			DynamicMesh backBuilder = surface.CreateMesh();
			backBuilder.Projection = Mesh.ProjectionType.Pixel;
			Color barColor = Colors.DimGray, alternateBarColor = Colors.Gray;
			Color badColor = Colors.DarkRed, alternateBadColor = Color.FromArgb(255, (byte)(badColor.R * 0.8), (byte)(badColor.G * 0.8), (byte)(badColor.B * 0.8));
			Color boardColor = OptickBackground.Color, alternateBoardColor = Color.FromArgb(255, (byte)(boardColor.R + 20), (byte)(boardColor.G + 20), (byte)(boardColor.B + 20));
			EventDescriptionBoard currentBoard = null;
			float maxWidth = (float)FullWidth;
			double currentRectLeft = FrameSpace; int framesCount = 0, firstGroupIndex = 0;
			bool alternateBoard = false;
			foreach (EventFrame frame in frames)
			{
				// Build Mesh
				int fPack = framesCount / FramesQuant;
				if (builder.Count <= fPack)
				{
					DynamicMesh b = surface.CreateMesh();
					b.Projection = Mesh.ProjectionType.Pixel;
					builder.Add(b);
				}
				if (frame.DescriptionBoard != currentBoard)
				{
					if (framesCount != 0)
					{
						builder[fPack].AddRect(new Rect((currentRectLeft - FrameSpace-0.5) *RenderSettings.dpiScaleX, 0, 1*RenderSettings.dpiScaleX, 100000), Colors.Orange);
						backBuilder.AddRect(new Rect((firstGroupIndex * FrameWidthWithSpacing + FrameSpace*0.5) * RenderSettings.dpiScaleX, 0,
														((framesCount - firstGroupIndex) * FrameWidthWithSpacing + FrameSpace) * RenderSettings.dpiScaleX, 100000),
							alternateBoard ? boardColor : alternateBoardColor);
						alternateBoard = !alternateBoard;
					}
					//alternateBoardColor = Color.FromArgb(255, (byte)(boardColor.R * 0.8), (byte)(boardColor.G * 0.8), (byte)(boardColor.B * 0.8));
					currentBoard = frame.DescriptionBoard;
					firstGroupIndex = framesCount;
				}
				double frameTime = frame.Duration;
				Color color = frameTime > MaxPresentableFrameDuration ? ((framesCount & 1) == 0 ? badColor : alternateBadColor) :
					((framesCount & 1) == 0 ? barColor : alternateBarColor);

				builder[fPack].AddRect(new Rect(currentRectLeft * RenderSettings.dpiScaleX, 0, FrameWidth * RenderSettings.dpiScaleX, frameTime), color);
				currentRectLeft += FrameWidthWithSpacing;
				++framesCount;
			}
			if (framesCount != 0)
			{
				backBuilder.AddRect(new Rect((firstGroupIndex * FrameWidthWithSpacing + FrameSpace * 0.5) * RenderSettings.dpiScaleX, 0,
												((framesCount - firstGroupIndex) * FrameWidthWithSpacing + FrameSpace) * RenderSettings.dpiScaleX, 100000),
					alternateBoard ? boardColor : alternateBoardColor);
				alternateBoard = !alternateBoard;
			}
			FrameRects = new List<Mesh>(framesCount / FramesQuant);
			if (BoardsMesh != null)
				BoardsMesh.Dispose();
			BoardsMesh = backBuilder.Freeze(canvas.RenderDevice);
			foreach (DynamicMesh mesh in builder)
				FrameRects.Add(mesh.Freeze(canvas.RenderDevice));
		}

		public Matrix GetWorldMatrix(double left, double maxDuration)
		{
			return new Matrix(1.0, 0.0, 0.0, -ScrollSize.Height / maxDuration,//-1./maxDuration
							  -left,
							  ScrollSize.Height);//height
		}
		public Matrix GetOffsetMatrix(double left, double scale = 1)
		{
			return new Matrix(1.0, 0.0, 0.0, -scale,//-1./maxDuration
							  -left, ScrollSize.Height);//height
		}
		public double MaxPresentableFrameDuration { get {return Math.Max(0.0001, MedianFrameDuration + StdFrameDuration * 3.0); } }
		int HoverFrameAt = -1, SelectedFrameAt = -1;
		public void Render(DirectX.DirectXCanvas canvas, DirectXCanvas.Layer layer)
		{
			//scrollBar.Scroll += ScrollBar_Scroll;
			//surface.ActualWidth * RenderSettings.dpiScaleX
			//surface.ActualHeight * RenderSettings.dpiScaleY
			double maxDisplayableFrame = MaxPresentableFrameDuration;
			double scrollPos = Math.Floor(scrollLeft);
			Matrix world = GetWorldMatrix(scrollPos, maxDisplayableFrame);
			double fpsMul = MedianFrameDuration + StdFrameDuration < 8.7 ? 0.25 : MedianFrameDuration + StdFrameDuration < 16.99 ? 0.5 : 1.0;//We are targeting either 240, 120fps or 60fps
			if (layer == DirectXCanvas.Layer.Background)
			{

				int firstFrame = Math.Max(0, (int)(scrollPos / RenderSettings.dpiScaleX / FrameWidthWithSpacing));
				int lastFrame = Math.Min(frames.Count-1, firstFrame + 1 + ((int)(surface.ActualWidth / FrameWidthWithSpacing)));
				if (BoardsMesh != null)
				{
					BoardsMesh.WorldTransform = world;
					canvas.Draw(BoardsMesh);
				}

				if (FrameRects != null)
				{
					int firstFramePack = firstFrame / FramesQuant, endFramePack = Math.Min(FrameRects.Count, (lastFrame + FramesQuant) / FramesQuant);
					for (int frame = firstFramePack; frame < endFramePack; ++frame)
                    {
						Mesh mesh = FrameRects[frame];
						mesh.WorldTransform = world;
						canvas.Draw(mesh);
					}
				}

				if (HoverFrameAt >= 0 && HoverFrameAt < frames.Count)
				{
					HoverFrameMeshBackground.WorldTransform = GetOffsetMatrix(scrollPos - (HoverFrameAt * FrameWidthWithSpacing + FrameSpace)*RenderSettings.dpiScaleX, frames[HoverFrameAt].Duration / maxDisplayableFrame);
					canvas.Draw(HoverFrameMeshBackground);
				}

				double currentFrameTextAt = (firstFrame * FrameWidthWithSpacing + FrameWidthWithSpacing / 2) * RenderSettings.dpiScaleX - scrollPos;
				for (int frame = firstFrame; frame <= lastFrame; ++frame, currentFrameTextAt += FrameWidthWithSpacing* RenderSettings.dpiScaleX)
				{
					double duration = frames[frame].Duration;
					Color textColor = duration < 16.6*fpsMul ? Epic : duration < 33.3*fpsMul ? Good : duration < 20*fpsMul ? Okay : Poor;
					String str = Profiler.Data.Utils.ConvertMsToString(duration);
					Size sz = canvas.Text.Measure(str);
					double centerAt = currentFrameTextAt - sz.Height / 2;
					//shadow
					canvas.Text.DrawVertical(DirectX.TextManager.VerticalDirection.CCW, new Point((centerAt+1), (canvas.Height - 4-1) * RenderSettings.dpiScaleY),
									 str,
									 Colors.Black,
									 TextAlignment.Left);
					canvas.Text.DrawVertical(DirectX.TextManager.VerticalDirection.CCW, new Point(centerAt, (canvas.Height - 4) * RenderSettings.dpiScaleY),
									 str,
									 textColor,
									 TextAlignment.Left);
				}
			}
			else if (layer == DirectXCanvas.Layer.Foreground)
            {
				if (HoverFrameAt >= 0 && HoverFrameAt < frames.Count)
				{
					HoverFrameMeshForeground.WorldTransform = GetOffsetMatrix(scrollPos - (HoverFrameAt * FrameWidthWithSpacing + FrameSpace) * RenderSettings.dpiScaleX);
					canvas.Draw(HoverFrameMeshForeground);
				}
				if (SelectedFrameAt >= 0 && SelectedFrameAt < frames.Count && ThreadViewTime != null && ThreadViewTime.IsValid)
                {
					int firstFrame = -1, lastFrame = -1;
					FrameGroup group = frames[SelectedFrameAt].Group;
					for (int frame = FirstFrameInSelectedBoard; frame < frames.Count && group == frames[frame].Group; ++frame)
                    {
						if (!(frames[frame] is EventFrame))
							break;
						if (((EventFrame)frames[frame]).Entries.Count == 0)
							break;
						if (((EventFrame)frames[frame]).Entries[0].Start > ThreadViewTime.Start)
							break;
						firstFrame = frame;
					}
					for (int frame = firstFrame; frame >= 0 && frame < frames.Count && group == frames[frame].Group; ++frame)
					{
						if (!(frames[frame] is EventFrame))
							break;
						if (((EventFrame)frames[frame]).Entries.Count == 0)
							break;
						if (((EventFrame)frames[frame]).Entries[0].Start < ThreadViewTime.Finish)
							lastFrame = frame;
						if (((EventFrame)frames[frame]).Entries[0].Finish > ThreadViewTime.Finish)
							break;
					}
					if (firstFrame <= lastFrame && firstFrame >= 0)
					{
						Entry firstEntry = ((EventFrame)frames[firstFrame]).Entries[0], lastEntry = ((EventFrame)frames[lastFrame]).Entries[0];
						double offsetFirst = ((double)ThreadViewTime.Start- firstEntry.Start) / (firstEntry.Finish - firstEntry.Start);
						double offsetLast = ((double)ThreadViewTime.Finish - lastEntry.Start) / (lastEntry.Finish - lastEntry.Start);
						double start = (firstFrame * FrameWidthWithSpacing + FrameWidthWithSpacing * offsetFirst)* RenderSettings.dpiScaleX;
						double end = (lastFrame * FrameWidthWithSpacing + FrameWidthWithSpacing * offsetLast)*RenderSettings.dpiScaleX;
						double sz = end - start;
						ThreadViewTimeLineMesh.WorldTransform =
							new Matrix(sz / FrameWidth/ RenderSettings.dpiScaleX, 0.0,
									   0.0, -1.0,
									   -scrollPos + start, ScrollSize.Height);//height
						canvas.Draw(ThreadViewTimeLineMesh);

						//draw two vertical lines
						start -= scrollPos;
						end -= scrollPos;
						ThreadViewTimeLineFrame.AddLine(new Point(start-0.5, 0.0), new Point(start-0.5, ScrollSize.Height), Colors.LightGreen);
						ThreadViewTimeLineFrame.AddLine(new Point(start, 0.0), new Point(start, ScrollSize.Height), Colors.LightGreen);
						ThreadViewTimeLineFrame.AddLine(new Point(end, 0.0), new Point(end, ScrollSize.Height), Colors.LightGreen);
						ThreadViewTimeLineFrame.AddLine(new Point(end+0.5, 0.0), new Point(end+0.5, ScrollSize.Height), Colors.LightGreen);

						ThreadViewTimeLineFrame.Update(canvas.RenderDevice);
						canvas.Draw(ThreadViewTimeLineFrame);
					}
					else
					{

						Durable SelectedTimeSlice = frames[SelectedFrameAt].Group.Board.TimeSlice;
						double selectedTimeSliceDuration = SelectedTimeSlice.Finish - SelectedTimeSlice.Start;
						double relativeThreadViewStart = (ThreadViewTime.Start - SelectedTimeSlice.Start) / selectedTimeSliceDuration;
						double relativeThreadViewSize = (ThreadViewTime.Finish - ThreadViewTime.Start) / selectedTimeSliceDuration;
						//fixme we need take into account slected time slice firstFrame, and selected TimeSlice total with (in frames)
						ThreadViewTimeLineMesh.WorldTransform =
							new Matrix(relativeThreadViewSize * FullWidth / FrameWidth, 0.0,
									   0.0, -1.0,
									   -scrollPos + relativeThreadViewStart * FullWidth, ScrollSize.Height);//height

						canvas.Draw(ThreadViewTimeLineMesh);
					}

				}
			}
		}
//==================================================================================================================================

		public TimeLine()
		{
			//this.InitializeComponent();
			this.DataContext = frames;
		    TimeLineViewControl();

			statusToError.Add(TracerStatus.TRACER_ERROR_ACCESS_DENIED, new KeyValuePair<string, string>("ETW can't start: launch your Game/VisualStudio/UE4Editor as administrator to collect context switches", "https://github.com/bombomby/optick/wiki/Event-Tracing-for-Windows"));
			statusToError.Add(TracerStatus.TRACER_ERROR_ALREADY_EXISTS, new KeyValuePair<string, string>("ETW session already started (Reboot should help)", "https://github.com/bombomby/optick/wiki/Event-Tracing-for-Windows"));
			statusToError.Add(TracerStatus.TRACER_FAILED, new KeyValuePair<string, string>("ETW session failed (Run your Game or Visual Studio as Administrator to get ETW data)", "https://github.com/bombomby/optick/wiki/Event-Tracing-for-Windows"));
			statusToError.Add(TracerStatus.TRACER_INVALID_PASSWORD, new KeyValuePair<string, string>("Tracing session failed: invalid root password. Run the game as a root or pass a valid password through daProfiler GUI", "https://github.com/bombomby/optick/wiki/Event-Tracing-for-Windows"));
			statusToError.Add(TracerStatus.TRACER_NOT_IMPLEMENTED, new KeyValuePair<string, string>("Tracing sessions are not supported yet on the selected platform! Stay tuned!", "https://github.com/bombomby/optick"));

			ProfilerClient.Get().ConnectionChanged += TimeLine_ConnectionChanged;

			socketThread = new Thread(RecieveMessage);
			socketThread.Start();
		}
		Durable ThreadViewTime;
		public void SetThreadViewTime(Durable time)
        {
			ThreadViewTime = time;
			UpdateSurface();
		}

		private void TimeLine_ConnectionChanged(IPAddress address, UInt16 port, ProfilerClient.State state, String message)
		{
			switch (state)
			{
				case ProfilerClient.State.Connecting:
					break;

				case ProfilerClient.State.Disconnected:
					break;

				case ProfilerClient.State.Connected:
					break;
			}
		}

		public bool LoadFile(string file)
		{
			if (File.Exists(file))
			{
				bool ret = false;
			    try {
					RaiseEvent(new UpdateStatusEventArgs($"Opening {file}"));
					using (new WaitCursor())
					{
						if (System.IO.Path.GetExtension(file) == ".trace")
						{
							Clear();
							ret = OpenTrace<FTraceGroup>(file);
						}
						else if (System.IO.Path.GetExtension(file) == ".json")
						{
							Clear();
							ret = OpenTrace<ChromeTracingGroup>(file);
						}
						else
						{
							using (Stream stream = Data.Capture.Open(file))
							{
								ret = Open(file, stream);
							}
						}
					}
				} catch (System.IO.IOException) {}
				RaiseEvent(new UpdateStatusEventArgs($"{(ret ? "Opened" : "Can't open")} {System.IO.Path.GetFileName(file)}" ));
			    return ret;
			}
			return false;
		}

		private bool Open(String name, Stream stream)
		{
			DataResponse response = DataResponse.Create(stream);
			while (response != null)
			{
				if (!ApplyResponse(response))
					return false;

				response = DataResponse.Create(stream);
				if (response == null)
				{
					Stream stream2 = Data.Capture.ReOpen(stream);
					if (stream2 != null)
					{
						stream = stream2;
						response = DataResponse.Create(stream);
					}
				}
			}

			frames.UpdateName(name);
			frames.Flush();
			BuildMesh(surface);
			ScrollToEnd();

			return true;
		}

		private bool OpenTrace<T>(String path) where T : ITrace, new()
		{
			if (File.Exists(path))
			{
				using (Stream stream = File.OpenRead(path))
				{
					T trace = new T();
					trace.Init(path, stream);
					frames.AddGroup(trace.MainGroup);
					frames.Add(trace.MainFrame);
					FocusOnFrame(trace.MainFrame);
				}
				return true;
			}
			return false;
		}

		Dictionary<DataResponse.Type, int> testResponses = new Dictionary<DataResponse.Type, int>();

		private void SaveTestResponse(DataResponse response)
		{
			if (!testResponses.ContainsKey(response.ResponseType))
				testResponses.Add(response.ResponseType, 0);

			int count = testResponses[response.ResponseType]++;

			String data = response.SerializeToBase64();
			String path = response.ResponseType.ToString() + "_" + String.Format("{0:000}", count) + ".bin";
			File.WriteAllText(path, data);

		}

		public class ThreadDescription
		{
			public UInt32 ThreadID { get; set; }
			public String Name { get; set; }

			public override string ToString()
			{
				return String.Format("[{0}] {1}", ThreadID, Name);
			}
		}

		enum TracerStatus
		{
			TRACER_OK = 0,
			TRACER_ERROR_ALREADY_EXISTS = 1,
			TRACER_ERROR_ACCESS_DENIED = 2,
			TRACER_FAILED = 3,
			TRACER_INVALID_PASSWORD = 4,
			TRACER_NOT_IMPLEMENTED = 5,
		}

		Dictionary<TracerStatus, KeyValuePair<String, String>> statusToError = new Dictionary<TracerStatus, KeyValuePair<String, String>>();

		public static double AverageFrameDuration = 1, MedianFrameDuration = 1, StdFrameDuration = 0;
		public static double Square(double a) { return a * a; }
		int lastFramesCount = 0;
		public void rebuildMesh()
        {
			BuildMesh(surface);
			UpdateBar();
			UpdateSurface();
		}
		public void RecalcHeight()
        {
			AverageFrameDuration = 0;
			lastFramesCount = frames.Count;
			foreach (Frame frame in frames)
				AverageFrameDuration += frame.Duration;

			if (frames.Count > 0)
				AverageFrameDuration /= frames.Count;
			MedianFrameDuration = AverageFrameDuration;//fixme: incorrect!
			StdFrameDuration = 0;
			if (frames.Count > 1)
			{
				double var = 0;
				foreach (Frame frame in frames)
					var += Square(frame.Duration - AverageFrameDuration);
				var /= frames.Count - 1;
				StdFrameDuration = Math.Sqrt(var);
			}
		}
		public void StopCaptureNow()
		{
			RaiseEvent(new UpdateStatusEventArgs(String.Empty));
			RaiseEvent(new StopCaptureEventArgs());
			lock (frames)
			{
				frames.Flush();
				ScrollToEnd();
			}
		}

		private bool ApplyResponse(DataResponse response)
		{
			if (response.Version >= NetworkProtocol.NETWORK_PROTOCOL_MIN_VERSION)
			{
				//SaveTestResponse(response);

				switch (response.ResponseType)
				{
					case DataResponse.Type.ReportProgress:
						RaiseEvent(new UpdateStatusEventArgs(Data.Utils.ReadVlqString(response.Reader)));
						break;
					case DataResponse.Type.ReportLiveFrameTime:
					    if (liveFramesView != null)
					    	liveFramesView.process(response.Reader);
						break;

					case DataResponse.Type.Plugins:
					{
						int count = response.Reader.ReadInt32();
						Dictionary<String, bool> plugins = new Dictionary<String, bool>(count);
						for (int i = 0; i < count; ++i)
						  plugins[Data.Utils.ReadBinaryString(response.Reader)] = response.Reader.ReadBoolean();
						RaiseEvent(new UpdatePluginsEventArgs(plugins));
						break;
					}
					case DataResponse.Type.SettingsPack:
					    CaptureSettings settings = new CaptureSettings();
					    settings.Read(response.Reader);
						RaiseEvent(new UpdateSettingsEventArgs(settings));
						break;

					case DataResponse.Type.NullFrame:
						StopCaptureNow();
						break;

					case DataResponse.Type.Handshake:
						TracerStatus status = (TracerStatus)response.Reader.ReadUInt32();

						KeyValuePair<string, string> warning;
						if (statusToError.TryGetValue(status, out warning))
						{
							RaiseEvent(new ShowWarningEventArgs(warning.Key, warning.Value));
						}

						Platform.Connection connection = new Platform.Connection() {
							Address = response.Source.Address.ToString(),
							Port = response.Source.Port
						};
						Platform.Type target = Platform.Type.unknown;
						String targetName = Data.Utils.ReadBinaryString(response.Reader);
						Enum.TryParse(targetName, true, out target);
						connection.Target = target;
						connection.Name = Data.Utils.ReadBinaryString(response.Reader);
						RaiseEvent(new NewConnectionEventArgs(connection));

						break;
					case DataResponse.Type.UniqueName:
						String uniqueName = Data.Utils.ReadVlqString(response.Reader);
						lock (frames)
						{
							if (frames.uniqueRunName != uniqueName)
							{
								frames.uniqueRunName = uniqueName;
								ClearFrames();
							}
						}
						break;
					

					default:
						lock (frames)
						{
							frames.Add(response);
							//ScrollToEnd();
						}
						break;
				}
			}
			else
			{
				RaiseEvent(new ShowWarningEventArgs("Invalid NETWORK_PROTOCOL_VERSION", String.Empty));
				return false;
			}
			return true;
		}

		private void ScrollToEnd()
		{
			if (frames.Count > 0)
			{
				RecalcHeight();
				rebuildMesh();
				scrollBar.Value = scrollBar.Maximum;
				GetScrollBar();
			}
		}

		public void RecieveMessage()
		{
			uint processedResponses = 0, lastProcessedResponses = 0;
			while (true)
			{
				DataResponse response = ProfilerClient.Get().RecieveMessage();
				uint currentProcessed = processedResponses;

				if (response != null)
				{
					if (response.ResponseType != DataResponse.Type.Heartbeat)
					{
						Application.Current.Dispatcher.BeginInvoke(new Action(() => ApplyResponse(response)));
						processedResponses++;
					}
				}
				else
				{
					Thread.Sleep(1000);
					Task.Run(() => ProfilerClient.Get().SendMessage(new HeartbeatMessage(), false));
				}
				if (currentProcessed == processedResponses && lastProcessedResponses != processedResponses)
				{
					Application.Current.Dispatcher.BeginInvoke(new Action(() => UpdateCanvas()));
				}
			}
		}
		private void UpdateCanvas()
		{
			lock (frames)
			{
				if (frames.Count != lastFramesCount)
				{
					RecalcHeight();
					rebuildMesh();
				}
			}
		}


		#region FocusFrame
		private void FocusOnFrame(Data.Frame frame)
		{
			FocusFrameEventArgs args = new FocusFrameEventArgs(GlobalEvents.FocusFrameEvent, frame);
			RaiseEvent(args);
		}

        public class ShowWarningEventArgs : RoutedEventArgs
		{
			public String Message { get; set; }
			public String URL { get; set; }

			public ShowWarningEventArgs(String message, String url) : base(ShowWarningEvent)
			{
				Message = message;
				URL = url;
			}
		}

        public class UpdateStatusEventArgs : RoutedEventArgs
		{
			public String Text { get; set; }

			public UpdateStatusEventArgs(String text) : base(UpdateStatusEvent)
			{
				Text = text;
			}
		}

		public class NewConnectionEventArgs : RoutedEventArgs
		{
			public Platform.Connection Connection { get; set; }

			public NewConnectionEventArgs(Platform.Connection connection) : base(NewConnectionEvent)
			{
				Connection = connection;
			}
		}

		public class UpdateSettingsEventArgs : RoutedEventArgs
		{
			public CaptureSettings Settings { get; set; }

			public UpdateSettingsEventArgs(CaptureSettings settings) : base(UpdateSettingsEvent)
			{
				Settings = settings;
			}
		}

		public class UpdatePluginsEventArgs : RoutedEventArgs
		{
			public Dictionary<String, bool> Plugins { get; set; }

			public UpdatePluginsEventArgs(Dictionary<String, bool> plugins) : base(UpdatePluginsEvent)
			{
				Plugins = plugins;
			}
		}

		public class StopCaptureEventArgs : RoutedEventArgs
		{
			public StopCaptureEventArgs() : base(StopCaptureEvent)
			{
			}
		}

		public delegate void ShowWarningEventHandler(object sender, ShowWarningEventArgs e);
		public delegate void UpdateStatusEventHandler(object sender, UpdateStatusEventArgs e);
		public delegate void NewConnectionEventHandler(object sender, NewConnectionEventArgs e);
		public delegate void UpdateSettingsEventHandler(object sender, UpdateSettingsEventArgs e);
		public delegate void UpdatePluginsEventHandler(object sender, UpdatePluginsEventArgs e);
		public delegate void StopCaptureEventHandler(object sender, StopCaptureEventArgs e);

		public static readonly RoutedEvent ShowWarningEvent = EventManager.RegisterRoutedEvent("ShowWarning", RoutingStrategy.Bubble, typeof(ShowWarningEventArgs), typeof(TimeLine));
		public static readonly RoutedEvent UpdateStatusEvent = EventManager.RegisterRoutedEvent("UpdateStatus", RoutingStrategy.Bubble, typeof(UpdateStatusEventArgs), typeof(TimeLine));
		public static readonly RoutedEvent NewConnectionEvent = EventManager.RegisterRoutedEvent("NewConnection", RoutingStrategy.Bubble, typeof(NewConnectionEventHandler), typeof(TimeLine));
		public static readonly RoutedEvent UpdateSettingsEvent = EventManager.RegisterRoutedEvent("UpdateSettings", RoutingStrategy.Bubble, typeof(UpdateSettingsEventHandler), typeof(TimeLine));
		public static readonly RoutedEvent UpdatePluginsEvent = EventManager.RegisterRoutedEvent("UpdatePlugins", RoutingStrategy.Bubble, typeof(UpdatePluginsEventHandler), typeof(TimeLine));
		public static readonly RoutedEvent StopCaptureEvent = EventManager.RegisterRoutedEvent("StopCapture", RoutingStrategy.Bubble, typeof(StopCaptureEventHandler), typeof(TimeLine));

		public event RoutedEventHandler FocusFrame
		{
			add { AddHandler(GlobalEvents.FocusFrameEvent, value); }
			remove { RemoveHandler(GlobalEvents.FocusFrameEvent, value); }
		}

		public event RoutedEventHandler ShowWarning
		{
			add { AddHandler(ShowWarningEvent, value); }
			remove { RemoveHandler(ShowWarningEvent, value); }
		}

		public event RoutedEventHandler UpdateStatus
		{
			add { AddHandler(UpdateStatusEvent, value); }
			remove { RemoveHandler(UpdateStatusEvent, value); }
		}

		public event RoutedEventHandler NewConnection
		{
			add { AddHandler(NewConnectionEvent, value); }
			remove { RemoveHandler(NewConnectionEvent, value); }
		}

		public event RoutedEventHandler UpdateSettings
		{
			add { AddHandler(UpdateSettingsEvent, value); }
			remove { RemoveHandler(UpdateSettingsEvent, value); }
		}

		public event RoutedEventHandler UpdatePlugins
		{
			add { AddHandler(UpdatePluginsEvent, value); }
			remove { RemoveHandler(UpdatePluginsEvent, value); }
		}

		public event StopCaptureEventHandler StopCapture
		{
			add { AddHandler(StopCaptureEvent, value); }
			remove { RemoveHandler(StopCaptureEvent, value); }
		}
		#endregion


		public void ForEachResponse(Action<FrameGroup, DataResponse> action)
		{
			FrameGroup currentGroup = null;
			foreach (Frame frame in frames)
			{
				if (frame is EventFrame)
				{
					EventFrame eventFrame = frame as EventFrame;
					if (eventFrame.Group != currentGroup && currentGroup != null)
					{
						currentGroup.Responses.ForEach(response => action(currentGroup, response));
					}
					currentGroup = eventFrame.Group;
				}
				else if (frame is SamplingFrame)
				{
					if (currentGroup != null)
					{
						currentGroup.Responses.ForEach(response => action(currentGroup, response));
						currentGroup = null;
					}

					action(null, (frame as SamplingFrame).Response);
				}
			}

			currentGroup?.Responses.ForEach(response => action(currentGroup, response));
		}

		public void Save(Stream stream)
		{
			ForEachResponse((group, response) => response.Serialize(stream));
		}

		public String Save()
		{
			SaveFileDialog dlg = new SaveFileDialog();
			dlg.Filter = "daProfiler Performance Capture (*.dap)|*.dap";
			dlg.Title = "Where should I save profiler results?";

			if (dlg.ShowDialog() == true)
			{
				lock (frames)
				{
					using (Stream stream = Capture.Create(dlg.FileName))
						Save(stream);

					frames.UpdateName(dlg.FileName, true);
				}
				return dlg.FileName;
			}

			return null;
		}

		public void Close()
		{
			if (socketThread != null)
			{
				socketThread.Abort();
				socketThread = null;
			}
		}

		private void ClearFrames()
		{
			frames.Clear();
			ClearMesh();
			SelectedFrameAt = HoverFrameAt = -1;
		}
		public void Clear()
		{
			lock (frames)
			{
				ClearFrames();
			}
		}

		//private void FrameFilterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		//{
		//	ICollectionView view = CollectionViewSource.GetDefaultView(frameList.ItemsSource);
		//	view.Filter = new Predicate<object>((item) => { return (item is Frame) ? (item as Frame).Duration >= FrameFilterSlider.Value : true; });
		//}

		public void StartCapture(IPAddress address, UInt16 port, CaptureSettings settings, SecureString password)
		{
            ProfilerClient.Get().IpAddress = address;
            ProfilerClient.Get().Port = port;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
			{
				RaiseEvent(new UpdateStatusEventArgs($"Capturing..."));
			}));

			Task.Run(() =>
			{
				ProfilerClient.Get().SendMessage(new SetSettingsMessage() {Settings = settings}, true);
				ProfilerClient.Get().SendMessage(new StartCaptureMessage(), true);
			});
		}

		public void GetCapture(IPAddress address, UInt16 port, SecureString password)
		{
            ProfilerClient.Get().IpAddress = address;
            ProfilerClient.Get().Port = port;

			Task.Run(() =>
			{
				ProfilerClient.Get().SendMessage(new GetCaptureMessage(), true);
			});
		}

		public void SendSettings(CaptureSettings settings)
		{
			Task.Run(() => { ProfilerClient.Get().SendMessage(new SetSettingsMessage() {Settings = settings}, false); });
		}

		public void SendPlugins(Dictionary<String, bool> plugins)
		{
			Task.Run(() => { ProfilerClient.Get().SendMessage(new PluginCommandMessage() { pluginCommands = plugins}, false); });
		}

		public void Connect(IPAddress address, UInt16 port)
		{
            ProfilerClient.Get().IpAddress = address;
            ProfilerClient.Get().Port = port;

			Task.Run(() => { ProfilerClient.Get().SendMessage(new ConnectMessage() , true); });
		}
	}

	public class WaitCursor : IDisposable
	{
		private Cursor _previousCursor;

		public WaitCursor()
		{
			_previousCursor = Mouse.OverrideCursor;

			Mouse.OverrideCursor = Cursors.Wait;
		}

		public void Dispose()
		{
			Mouse.OverrideCursor = _previousCursor;
		}
	}
}
