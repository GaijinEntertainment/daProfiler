using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Profiler.Data
{
    public class CaptureSettings
    {
		public Mode	Mode { get; set; } = (Mode.EVENTS | Mode.TAGS | Mode.SAMPLING);
		public UInt32 SamplingFrequencyHz { get; set; } = 100;
		public UInt32 SpikeSamplingFrequencyHz { get; set; } = 1000;
		public UInt32 ThreadsSamplingRate { get; set; } = 2;
		public UInt32 MaxSpikeLimitMs { get; set; } = 0;
		public UInt32 SpikeMulAvg { get; set; } = 3;
		public UInt32 SpikeAvgAddMs { get; set; } = 1;
		public UInt32 ProfilingFramesLimit { get; set; } = 0;
		public UInt32 ProfileSizeLimitMb { get; set; } = 0;
		public void Write(BinaryWriter writer)
		{
            writer.Write((UInt32)Mode);

			writer.Write(SamplingFrequencyHz);
			writer.Write(SpikeSamplingFrequencyHz);
			writer.Write(ThreadsSamplingRate);

			writer.Write(MaxSpikeLimitMs);
			writer.Write(SpikeMulAvg);
			writer.Write(SpikeAvgAddMs);

			writer.Write(ProfilingFramesLimit);
			writer.Write(ProfileSizeLimitMb);
		}
		public void Read(BinaryReader reader)
		{
            Mode = (Mode)reader.ReadUInt32();

			SamplingFrequencyHz = reader.ReadUInt32();
			SpikeSamplingFrequencyHz = reader.ReadUInt32();
			ThreadsSamplingRate = reader.ReadUInt32();

			MaxSpikeLimitMs = reader.ReadUInt32();
			SpikeMulAvg=  reader.ReadUInt32();
			SpikeAvgAddMs = reader.ReadUInt32();
			ProfilingFramesLimit = reader.ReadUInt32();
			ProfileSizeLimitMb = reader.ReadUInt32();
		}
	}
}
