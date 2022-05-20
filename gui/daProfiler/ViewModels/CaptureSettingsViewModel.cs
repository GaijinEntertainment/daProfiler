using Profiler.Controls;
using Profiler.Data;
using Profiler.InfrastructureMvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Profiler.ViewModels
{
	public interface INotifier
	{
		void Notify();
	}
	public class CaptureSettingsViewModel : BaseViewModel, INotifier
	{
		public static bool SetIfChanged<T>(ref T field, T value)
		{
			if (EqualityComparer<T>.Default.Equals(field, value)) return false;
			field = value;
			return true;
		}
		public class Setting : BaseViewModel
		{
			public String Name { get; set; }
			public String Description { get; set; }
			public INotifier Notifier;

			public Setting(String name, String description)
			{
				Name = name;
				Description = description;
			}
		}

		public class Flag : Setting
		{
			bool _isEnabled;
			public bool IsEnabled
			{
				get { return _isEnabled; }
				set
				{
					if (SetIfChanged(ref _isEnabled, value))
					{
						Notifier?.Notify();
						OnPropertyChanged();
					}
				}
			}

			public Mode Mask { get; set; }
			public Flag(String name, String description, Mode mask, bool isEnabled) : base(name, description)
			{
				Mask = mask;
				IsEnabled = isEnabled;
				OnPropertyChanged();
			}
		}


		public class Numeric : Setting
		{
			public Numeric(String name, String description) : base(name, description) { }
			double _value;
			public virtual double Value
			{
				get { return _value; }
				set { if (SetIfChanged(ref _value, value)) { Notifier?.Notify(); OnPropertyChanged(); } }
			}
		}

		public class NumericDelegate : Numeric
		{
			public NumericDelegate(string name, string description) : base(name, description) { }
			public Func<double> Getter { get; set; }
			public Action<double> Setter { get; set; }
			public override double Value
			{
				get {return Getter();}
				set {Setter(value);}
			}

		}

		public enum SamplingFrequency
		{
			None = 0,
			VeryLow  = 100,//for ETW we should increase it severily
			Low      = 200,
			Medium   = 500,
			High     = 1000,
			VeryHigh = 2000,
			Max      = 10000,
		}
		public ObservableCollection<Flag> FlagSettings { get; set; } = new ObservableCollection<Flag>(new Flag[]
		{
			new Flag("Profiling", "Collect Events", Mode.EVENTS, true),
			new Flag("Sampling", "Collect callstacks", Mode.SAMPLING, true),
			new Flag("Tags", "Collect DA_PROFILE_TAG events", Mode.TAGS, true),
			new Flag("GPU", "Collect GPU events", Mode.GPU, false),
			new Flag("Spikes", "Auto save spikes", Mode.SAVE_SPIKES, true),
			new Flag("ETW", "Sample using ETW CaptureTrace (kernel)", Mode.ETW, false),
			new Flag("Switch Contexts", "Collect Switch Context events (kernel)", Mode.ETW_CTX_SWITCH, false),
			new Flag("SysCalls", "Collect system calls (kernel)", Mode.ETW_SYS_CALLS, false),
			new Flag("All Processes", "Collects information about other processes (thread pre-emption) (kernel)", Mode.ETW_PROCESSES, false),
		});

		public Array SamplingFrequencyList
		{
			get {
				List<String> ret = new List<string>();
				foreach (SamplingFrequency freq in (SamplingFrequency[])Enum.GetValues(typeof(SamplingFrequency)))
					ret.Add(FrequencyToString((uint)freq));
				return ret.ToArray();
			}
		}
		public INotifier notifier { get; set; }
		public void Notify()  { notifier?.Notify(); }
		private uint _samplingFrequency = (uint)SamplingFrequency.Low;
		private uint _samplingSpikeFrequency = (uint)SamplingFrequency.High;
		public uint StringToFrequency(String v)
        {
			if (v == null)
				return 0u;
			List<String> s = v.Trim().Split(' ').ToList();
			if (s.Count == 0)
				return 0u;
			v = s[0];
			SamplingFrequency freq;
			if (Enum.TryParse(v, out freq))
				return (uint)freq;
			uint freqU;
			if (UInt32.TryParse(v, out freqU))
				return freqU;
			return 0u;
		}
		public String FrequencyToString(uint v)
		{
			if (Enum.IsDefined(typeof(SamplingFrequency), (int)v))
				return String.Format("{1} ({0}/sec)", v,  ((SamplingFrequency)v).ToString());
			return String.Format("{0}/sec", v);
		}
		public String SamplingFrequencyHz
		{
			get { return FrequencyToString(_samplingFrequency); }
			set
			{
				if (value == null)
					return;
				if (SetIfChanged(ref _samplingFrequency, StringToFrequency(value)))
					Notify();
				OnPropertyChanged();
			}
		}
		public String SpikeSamplingFrequencyHz
		{
			get { return FrequencyToString(_samplingSpikeFrequency); }
			set
			{
				if (value == null)
					return;
				if (SetIfChanged(ref _samplingSpikeFrequency, StringToFrequency(value)))
					Notify();
				OnPropertyChanged();
			}

		}

		// Frame Limits
		Numeric ThreadsSamplingRate = new Numeric("Thread sampling rate", "Other threads are sampled each Nth sampling ") { Value = 2 };
		Numeric MaxSpikeLimitMs = new Numeric("Min Spike (ms)", "Automatically capture spike of not shorter than") { Value = 100 };
		Numeric SpikeMulAvg = new Numeric("Spike Average Mul", "Automatically capture spike of not shorter than avg*mul + add") { Value = 3 };
		Numeric SpikeAvgAddMs = new Numeric("Spike Average Add (ms)", "Automatically capture spike of not shorter than avg*mul + add") { Value = 1 };
		Numeric SaveFramesBeforeSpike = new Numeric("Frames before spike", "Saves N frames predecessing spiked frame") { Value = 3 };
		Numeric SaveFramesAfterSpike = new Numeric("Frames after spike", "Saves N frames following spiked frame") { Value = 2 };
		Numeric ProfilingFramesLimit  = new Numeric("Profile frames limit", "Automatically dump capture if captured limit frames (0 - inf)") { Value = 0 };
		Numeric ProfileSizeLimitMb = new Numeric("Profile size limit (mb)", "Automatically stops capture if captured size limit exceeds (0 - inf)") { Value = 0 };


		public ObservableCollection<Numeric> SamplingParams { get; set; } = new ObservableCollection<Numeric>();
		public ObservableCollection<Numeric> SpikesParams { get; set; } = new ObservableCollection<Numeric>();
		public ObservableCollection<Numeric> CaptureLimits { get; set; } = new ObservableCollection<Numeric>();

		// Timeline Settings
		public NumericDelegate TimelineMinThreadDepth { get; private set; } = new NumericDelegate("Collapsed Thread Depth", "Limits the maximum visualization depth for each thread in collapsed mode")
		{
			Getter = () => Settings.LocalSettings.Data.ThreadSettings.CollapsedMaxThreadDepth,
			Setter = (val) => { Settings.LocalSettings.Data.ThreadSettings.CollapsedMaxThreadDepth = (int)val; Settings.LocalSettings.Save(); }
		};

		public NumericDelegate TimelineMaxThreadDepth { get; private set; } = new NumericDelegate("Expanded Thread Depth", "Limits the maximum visualization depth for each thread in expanded modes")
		{
			Getter = ()=> Controls.Settings.LocalSettings.Data.ThreadSettings.ExpandedMaxThreadDepth,
			Setter = (val) => { Controls.Settings.LocalSettings.Data.ThreadSettings.ExpandedMaxThreadDepth = (int)val; Settings.LocalSettings.Save(); }
		};

		public Array ExpandModeList
		{
			get { return Enum.GetValues(typeof(ExpandMode)); }
		}

		public ExpandMode ExpandMode 
		{
			get
			{
				return Controls.Settings.LocalSettings.Data.ThreadSettings.ThreadExpandMode;
			}
			set
			{
				Controls.Settings.LocalSettings.Data.ThreadSettings.ThreadExpandMode = value;
				Controls.Settings.LocalSettings.Save();
			}
		}

		public ObservableCollection<Numeric> TimelineSettings { get; set; } = new ObservableCollection<Numeric>();

		public CaptureSettingsViewModel()
		{
			SpikesParams.Add(MaxSpikeLimitMs);
			SpikesParams.Add(SpikeMulAvg);
			SpikesParams.Add(SpikeAvgAddMs);
			SpikesParams.Add(SaveFramesBeforeSpike);
			SpikesParams.Add(SaveFramesAfterSpike);
			SamplingParams.Add(ThreadsSamplingRate);
			CaptureLimits.Add(ProfilingFramesLimit);
			CaptureLimits.Add(ProfileSizeLimitMb);			

			TimelineSettings.Add(TimelineMinThreadDepth);
			TimelineSettings.Add(TimelineMaxThreadDepth);
			foreach (Setting setting in SpikesParams)
				setting.Notifier = this;
			foreach (Setting setting in SamplingParams)
				setting.Notifier = this;
			foreach (Setting setting in CaptureLimits)
				setting.Notifier = this;

			foreach (Flag flag in FlagSettings)
				flag.Notifier = this;
		}

		public CaptureSettings GetSettings()
		{
			CaptureSettings settings = new CaptureSettings();

			foreach (Flag flag in FlagSettings)
				if (flag.IsEnabled)
					settings.Mode = settings.Mode | flag.Mask;

			settings.SamplingFrequencyHz = (uint)StringToFrequency(SamplingFrequencyHz);
			settings.SpikeSamplingFrequencyHz = (uint)StringToFrequency(SpikeSamplingFrequencyHz);
			settings.ThreadsSamplingRate = (uint)(ThreadsSamplingRate.Value);
			settings.MaxSpikeLimitMs = (uint)(MaxSpikeLimitMs.Value);
			settings.SpikeMulAvg = (uint)(SpikeMulAvg.Value);
			settings.SpikeAvgAddMs = (uint)(SpikeAvgAddMs.Value);
			settings.SaveFramesBeforeSpike = (uint)(SaveFramesBeforeSpike.Value);
			settings.SaveFramesAfterSpike = (uint)(SaveFramesAfterSpike.Value);
			settings.ProfilingFramesLimit = (uint)(ProfilingFramesLimit.Value);
			settings.ProfileSizeLimitMb  = (uint)(ProfileSizeLimitMb.Value);

			return settings;
		}

		public void SetSettings(CaptureSettings settings)
		{
			foreach (Flag flag in FlagSettings)
				flag.IsEnabled = (settings.Mode & flag.Mask) != 0;

			INotifier old = notifier;
			notifier = null;
			SamplingFrequencyHz = FrequencyToString(settings.SamplingFrequencyHz);
			SpikeSamplingFrequencyHz = FrequencyToString(settings.SpikeSamplingFrequencyHz);
			ThreadsSamplingRate.Value = settings.ThreadsSamplingRate;
			MaxSpikeLimitMs.Value = settings.MaxSpikeLimitMs;
			SpikeMulAvg.Value = settings.SpikeMulAvg;
			SpikeAvgAddMs.Value = settings.SpikeAvgAddMs;
			SaveFramesBeforeSpike.Value = settings.SaveFramesBeforeSpike;
			SaveFramesAfterSpike.Value = settings.SaveFramesAfterSpike;
			ProfilingFramesLimit.Value = settings.ProfilingFramesLimit;
			ProfileSizeLimitMb.Value = settings.ProfileSizeLimitMb;
			OnPropertyChanged();
			notifier = old;
		}
	}

}
