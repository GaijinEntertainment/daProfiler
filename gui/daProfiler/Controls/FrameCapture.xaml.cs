using MahApps.Metro.Controls;
using Profiler.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.ComponentModel;
using System.Runtime.CompilerServices;            //CallerMemberName
using Profiler.ViewModels;
using Profiler.InfrastructureMvvm;
using Autofac;
using Profiler.Controls.ViewModels;

namespace Profiler.Controls
{
    /// <summary>
    /// Interaction logic for FrameCapture.xaml
    /// </summary>
    public partial class FrameCapture : UserControl, INotifyPropertyChanged, INotifier
	{
        private string _captureName;

        public event PropertyChangedEventHandler PropertyChanged;
		void INotifier.Notify()
        {
			timeLine.SendSettings(CaptureSettingsVM.GetSettings());
		}

		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        SummaryViewerModel _summaryVM;
        CaptureSettingsViewModel _captureSettingsVM;
		public CaptureSettingsViewModel CaptureSettingsVM
        {
            get { return _captureSettingsVM; }
            set
            {
                _captureSettingsVM = value;
                OnPropertyChanged("CaptureSettingsVM");
            }
        }

		public SummaryViewerModel SummaryVM
        {
            get { return _summaryVM; }
            set
            {
                _summaryVM = value;
                OnPropertyChanged("SummaryVM");
            }
        }

        AddressBarViewModel AddressBarVM { get; set; }
		public double DefaultWarningTimerInSeconds { get; set; } = 15.0;

		public FrameCapture()
		{
            using (var scope = BootStrapperBase.Container.BeginLifetimeScope())
            {
                SummaryVM = scope.Resolve<SummaryViewerModel>();
            }


            InitializeComponent();

			this.AddHandler(GlobalEvents.FocusFrameEvent, new FocusFrameEventArgs.Handler(this.OpenFrame));
            this.AddHandler(ThreadViewControl.HighlightFrameEvent, new ThreadViewControl.HighlightFrameEventHandler(this.ThreadView_HighlightEvent));
            this.AddHandler(GlobalEvents.ThreadViewZoomChangedEvent, new ThreadViewZoomChangedEventArgs.Handler(this.ThreadView_ThreadViewZoomChanged));

            ProfilerClient.Get().ConnectionChanged += MainWindow_ConnectionChanged;

			WarningTimer = new DispatcherTimer(TimeSpan.FromSeconds(DefaultWarningTimerInSeconds), DispatcherPriority.Background, OnWarningTimeout, Application.Current.Dispatcher);


			timeLine.NewConnection += TimeLine_NewConnection;
			timeLine.UpdateSettings += TimeLine_UpdateSettings;
			timeLine.StopCapture += TimeLine_StopCapture;
			timeLine.ShowWarning += TimeLine_ShowWarning;
			timeLine.UpdateStatus += TimeLine_UpdateStatus;
			warningBlock.Visibility = Visibility.Collapsed;

            AddressBarVM = (AddressBarViewModel)FindResource("AddressBarVM");
            FunctionSummaryVM = (FunctionSummaryViewModel)FindResource("FunctionSummaryVM");
			FunctionInstanceVM = (FunctionInstanceViewModel)FindResource("FunctionInstanceVM");
			CaptureSettingsVM = (CaptureSettingsViewModel)FindResource("CaptureSettingsVM");
			CaptureSettingsVM.notifier = this;

			FunctionSamplingVM = (SamplingViewModel)FindResource("FunctionSamplingVM");
			SysCallsSamplingVM = (SamplingViewModel)FindResource("SysCallsSamplingVM");

			EventThreadViewControl.Settings = Settings.LocalSettings.Data.ThreadSettings;
			Settings.LocalSettings.OnChanged += LocalSettings_OnChanged;
		}

		private void LocalSettings_OnChanged()
		{
			EventThreadViewControl.Settings = Settings.LocalSettings.Data.ThreadSettings;
		}

		private void StopCapture()
		{
			StartButton.IsChecked = false;
		}

		private void TimeLine_StopCapture(object sender, RoutedEventArgs e)
		{
			StopCapture();
		}

		FunctionSummaryViewModel FunctionSummaryVM { get; set; }
		FunctionInstanceViewModel FunctionInstanceVM { get; set; }

		SamplingViewModel FunctionSamplingVM { get; set; }
		SamplingViewModel SysCallsSamplingVM { get; set; }

		public bool LoadFile(string path)
		{
			if (timeLine.LoadFile(path))
			{
				_captureName = path;
				return true;
			}
			return false;
		}

		private void MainWindow_ConnectionChanged(IPAddress address, UInt16 port, ProfilerClient.State state, String message)
		{
			switch (state)
			{
				case ProfilerClient.State.Connecting:
					StatusText.Text = $"Connecting {(address == null ? "null" : address.ToString())}:{port}...";
					StatusText.Foreground = new SolidColorBrush(Colors.LightGreen);
					ShowWarning(String.Empty, String.Empty);
					break;

				case ProfilerClient.State.Disconnected:
					ClientStatusText.Visibility = Visibility.Collapsed;
					StatusText.Text = "Offline";
					StatusText.Foreground = new SolidColorBrush(String.IsNullOrEmpty(message) ? Colors.Black : Colors.Red);
					ConnectButton.IsChecked = false;
					StartButton.IsChecked = false;
					if (!String.IsNullOrEmpty(message))
						ShowWarning("Connection stopped! " + message, String.Empty);
					break;

				case ProfilerClient.State.Connected:
					StatusText.Text = $"Connected to {address.ToString()}:{port}.";
					StatusText.Foreground = new SolidColorBrush(Colors.White);
					ConnectButton.IsChecked = true;
					ShowWarning(String.Empty, String.Empty);
                    break;
			}
		}

		private void TimeLine_ShowWarning(object sender, RoutedEventArgs e)
		{
			TimeLine.ShowWarningEventArgs args = e as TimeLine.ShowWarningEventArgs;
			ShowWarning(args.Message, args.URL.ToString());
		}

		private void TimeLine_UpdateStatus(object sender, RoutedEventArgs e)
		{
			TimeLine.UpdateStatusEventArgs args = e as TimeLine.UpdateStatusEventArgs;
			ClientStatusText.Text = args.Text;
			ClientStatusText.Visibility = String.IsNullOrEmpty(ClientStatusText.Text) ? Visibility.Collapsed : Visibility.Visible;
		}

		private void TimeLine_NewConnection(object sender, RoutedEventArgs e)
		{
			TimeLine.NewConnectionEventArgs args = e as TimeLine.NewConnectionEventArgs;
            AddressBarVM?.Update(args.Connection);
        }

		private void TimeLine_UpdateSettings(object sender, RoutedEventArgs e)
		{
			TimeLine.UpdateSettingsEventArgs args = e as TimeLine.UpdateSettingsEventArgs;
            CaptureSettingsVM?.SetSettings(args.Settings);
        }

		private void OpenFrame(object source, FocusFrameEventArgs args)
		{
			Data.Frame frame = args.Frame;

            if (frame is EventFrame)
				EventThreadViewControl.Highlight(frame as EventFrame, null);

			if (frame is EventFrame)
			{
				EventFrame eventFrame = frame as EventFrame;
				FrameGroup group = eventFrame.Group;

				if (eventFrame.RootEntry != null)
				{
					EventDescription desc = eventFrame.RootEntry.Description;

					FunctionSummaryVM.Load(group, desc, eventFrame.Header.FrameType);
					FunctionInstanceVM.Load(group, desc, eventFrame.Header.FrameType);

					FunctionSamplingVM.Load(group, desc);
					SysCallsSamplingVM.Load(group, desc);

					FrameInfoControl.SetFrame(frame, null);
				}
			}

			if (frame != null && frame.Group != null)
			{
                if (!ReferenceEquals(SummaryVM.Summary, frame.Group.Summary))
                {
                    SummaryVM.Summary = frame.Group.Summary;
                    SummaryVM.CaptureName = _captureName;
                }
            }
		}

        private void ThreadView_HighlightEvent(object sender, HighlightFrameEventArgs e)
        {
			EventThreadViewControl.Highlight(e.Items);
        }

        private void ThreadView_ThreadViewZoomChanged(object sender, ThreadViewZoomChangedEventArgs e)
        {
			timeLine.SetThreadViewTime(e.Time);
        }

		public void Close()
		{
			timeLine.Close();
			ProfilerClient.Get().Close();
		}

		private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
		{
			Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
			e.Handled = true;
		}

		DispatcherTimer WarningTimer { get; set; }

		void OnWarningTimeout(object sender, EventArgs e)
		{
			warningBlock.Visibility = Visibility.Collapsed;
		}

		public void ShowWarning(String message, String url)
		{
			if (!String.IsNullOrEmpty(message))
			{
				warningText.Text = message;
				warningUrl.NavigateUri = !String.IsNullOrWhiteSpace(url) ? new Uri(url) : null;
				warningBlock.Visibility = Visibility.Visible;
			}
			else
			{
				warningBlock.Visibility = Visibility.Collapsed;
			}
		}

		private void ClearButton_Click(object sender, System.Windows.RoutedEventArgs e)
		{
			timeLine.Clear();
			EventThreadViewControl.Group = null;
			SummaryVM.Summary = null;
            SummaryVM.CaptureName = null;

			FunctionSummaryVM.Load(null, null);
			FunctionInstanceVM.Load(null, null);

			FunctionSamplingVM.Load(null, null);
			SysCallsSamplingVM.Load(null, null);

			FrameInfoControl.DataContext = null;
			SampleInfoControl.DataContext = null; 
			SysCallInfoControl.DataContext = null;

			//SamplingTreeControl.SetDescription(null, null);

			ProfilerClient.Get().SendMessage(new CancelInfiniteMessage());
		}

		private void Button_Capture(object sender, System.Windows.RoutedEventArgs e)
		{
            var platform = AddressBarVM.Selection;

			if (platform == null)
				return;

            IPAddress address = null;
            if (!IPAddress.TryParse(platform.Address, out address))
                return;

			MainViewModel vm = DataContext as MainViewModel;
			timeLine.GetCapture(address, platform.Port, platform.Password);
		}

        private void ClearSamplingButton_Click(object sender, System.Windows.RoutedEventArgs e)
		{
			Task.Run(() => ProfilerClient.Get().SendMessage(new TurnSamplingMessage(false), true));
		}

		private void ConnectButton_Unchecked(object sender, System.Windows.RoutedEventArgs e)
		{
			ProfilerClient.Get().Close();
		}

		private void ConnectButton_Checked(object sender, System.Windows.RoutedEventArgs e)
		{
            var platform = AddressBarVM.Selection;

			if (platform == null)
				return;

            IPAddress address = null;
            if (!IPAddress.TryParse(platform.Address, out address))
                return;

			timeLine.Connect(address, platform.Port);
		}

		private void StartButton_Unchecked(object sender, System.Windows.RoutedEventArgs e)
		{
			Task.Run(() => ProfilerClient.Get().SendMessage(new StopMessage()));
		}

		private void StartButton_Checked(object sender, System.Windows.RoutedEventArgs e)
		{
            var platform = AddressBarVM.Selection;

			if (platform == null)
				return;

            IPAddress address = null;
            if (!IPAddress.TryParse(platform.Address, out address))
                return;

			CaptureSettings settings = CaptureSettingsVM.GetSettings();
			timeLine.StartCapture(address, platform.Port, settings, platform.Password);
		}

		private void OnOpenCommandExecuted(object sender, ExecutedRoutedEventArgs args)
		{
			System.Windows.Forms.OpenFileDialog dlg = new System.Windows.Forms.OpenFileDialog();
			dlg.Filter = "daProfiler Capture (*.dap)|*.dap|Chrome Trace (*.json)|*.json|FTrace Capture (*.ftrace)|*.ftrace";
			dlg.Title = "Load profiler results?";
			if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
			{
				RaiseEvent(new OpenCaptureEventArgs(dlg.FileName));
			}
		}

		private void OnSaveCommandExecuted(object sender, ExecutedRoutedEventArgs args)
		{
			String path = timeLine.Save();
			if (path != null)
			{
				RaiseEvent(new SaveCaptureEventArgs(path));
			}
		}

		private void OnSearchCommandExecuted(object sender, ExecutedRoutedEventArgs args)
		{
			EventThreadViewControl.OpenFunctionSearch();
		}

		private void OnShowDebugInfoCommandExecuted(object sender, ExecutedRoutedEventArgs args)
		{
			MemoryStatsViewModel vm = new MemoryStatsViewModel();
			timeLine.ForEachResponse((group, response) => vm.Load(response));
			vm.Update();
			DebugInfoPopup.DataContext = vm;
			DebugInfoPopup.IsOpen = true;
		}

        private void timeLine_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}
