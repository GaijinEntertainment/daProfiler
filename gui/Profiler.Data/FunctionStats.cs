using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Profiler.Data
{
    static public class Partitions
    {
        /// <summary>
        /// Partitions the given list around a pivot element such that all elements on left of pivot are <= pivot
        /// and the ones at thr right are > pivot. This method can be used for sorting, N-order statistics such as
        /// as median finding algorithms.
        /// Pivot is selected ranodmly if random number generator is supplied else its selected as last element in the list.
        /// Reference: Introduction to Algorithms 3rd Edition, Corman et al, pp 171
        /// </summary>
        private static int Partition<T>(this IList<T> list, int start, int end, Random rnd = null) where T : IComparable<T>
        {
            if (rnd != null)
                Swap(list, end, rnd.Next(start, end+1));

            var pivot = list[end];
            var lastLow = start - 1;
            for (var i = start; i < end; i++)
            {
                if (list[i].CompareTo(pivot) <= 0)
                    Swap(list, i, ++lastLow);
            }
            Swap(list, end, ++lastLow);
            return lastLow;
        }

        /// <summary>
        /// Returns Nth smallest element from the list. Here n starts from 0 so that n=0 returns minimum, n=1 returns 2nd smallest element etc.
        /// Note: specified list would be mutated in the process.
        /// Reference: Introduction to Algorithms 3rd Edition, Corman et al, pp 216
        /// </summary>
        public static T NthOrderStatistic<T>(this IList<T> list, int n, Random rnd = null) where T : IComparable<T>
        {
            return NthOrderStatistic(list, n, 0, list.Count - 1, rnd);
        }
        private static T NthOrderStatistic<T>(this IList<T> list, int n, int start, int end, Random rnd) where T : IComparable<T>
        {
            while (true)
            {
                var pivotIndex = Partition(list, start, end, rnd);
                if (pivotIndex == n)
                    return list[pivotIndex];

                if (n < pivotIndex)
                    end = pivotIndex - 1;
                else
                    start = pivotIndex + 1;
            }
        }

        public static void Swap<T>(this IList<T> list, int i, int j)
        {
            if (i==j)   //This check is not required but Partition function may make many calls so its for perf reason
                return;
            var temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }

    }
	public class FunctionStats
	{

		public class Sample
		{
			public String Name { get; set; }
			public int Index { get; set; }
			public double Total { get; set; }
			public double Work { get; set; }
			public double Wait { get { return Total - Work; } }
			public int Count { get; set; }
			public List<Entry> Entries { get; set; }

			public Sample()
			{
				Entries = new List<Entry>();
			}

			public Sample(Entry e) : this()
			{
				Add(e);
			}

			public void Add(Entry e)
			{
				Total = Total + e.Duration;
				Work = Work + e.CalculateWork();
				Count = Count + 1;
				Entries.Add(e);
			}
		}

		public enum Origin
		{
			MainThread,
			IndividualCalls,
		}

		public List<Sample> Samples { get; set; }
		public FrameGroup Group { get; set; }
		public EventDescription Description { get; set; }

		// Time spent in this function during an average frame
        public double AvgTotal
        {
            get { return Samples.Count > 0 ? Samples.Average(s => s.Total) : 0.0; }
        }

        // Time spent working in this function during an average frame
        public double AvgWork
        {
            get { return Samples.Count > 0 ? Samples.Average(s => s.Work) : 0.0; }
        }

        // Time spent waiting in this function during an average frame
        public double AvgWait
        {
            get { return Samples.Count > 0 ? Samples.Average(s => s.Wait) : 0.0; }
        }
        // Fastest time
        public double MinPerCall
        {
            get
            {
                double res = Double.MaxValue;
                foreach (Sample s in Samples)
                {
                    foreach (Entry e in s.Entries)
                    {
                        if (res > e.Duration)
                        {
                            res = e.Duration;
                        }
                    }
                }

                return res;
            }
        }

        // Slowest time
        public double MaxPerCall
        {
            get
            {
                double res = 0.0;
                foreach (Sample s in Samples)
                {
                    foreach (Entry e in s.Entries)
                    {
                        if (res < e.Duration)
                        {
                            res = e.Duration;
                        }
                    }
                }

                return res;
            }
        }

        // Average function time (averaged over calls, not frames)
        public double AvgTotalPerCall
        {
            get
            {
                int numCalls = 0;
                double sum = 0.0;
                foreach (Sample s in Samples)
                {
                    numCalls += s.Count;
                    sum += s.Total;
                }

                return sum / numCalls;
            }
        }

        // Standard deviation of the individual function times
        public double StdDevPerCall
        {
            get
            {
                double avg = this.AvgTotalPerCall;

                double sum = 0.0;
                int num = 0;
                foreach (Sample s in Samples)
                {
                    num += s.Count;
                    foreach (Entry e in s.Entries)
                    {
                        double x = e.Duration - avg;
                        sum += x * x;
                    }
                }

                return num > 0 ? Math.Sqrt(sum / num) : 0.0;
            }
        }

        public double P90PerCall
        {
            get
            {
                List<double> durations = new List<double>(Samples.Count);
                foreach (Sample s in Samples)
                    foreach (Entry e in s.Entries)
                        durations.Add(e.Duration);

                return Partitions.NthOrderStatistic(durations, durations.Count*9/10);
            }
        }

        public FunctionStats(FrameGroup group, EventDescription desc)
		{
			Group = group;
			Description = desc;
		}

		public void Load(Origin origin = Origin.MainThread, FrameList.Type fType = FrameList.Type.None, int thread_index = -1)
		{
			Samples = new List<Sample>();

			if (origin == Origin.MainThread)
			{
				Group.UpdateDescriptionMask(Description);

				if (Description.Mask != null)
				{
					ThreadMask mask = fType == FrameList.Type.GPU ? ThreadMask.GPU : (Description.Mask.Value&~ThreadMask.GPU);
					FrameList frameList = Group.GetFocusThread(mask);
					if (frameList == null)
					{
						frameList = Group.GetFocusThread(mask = Description.Mask.Value);
					}
					if (frameList != null)
					{
						List<FrameData> frames = frameList.Events;

						for (int i = 0; i < frames.Count; ++i)
						{
							Sample sample = new Sample() { Name = String.Format("Frame {0:000}", i), Index = i };

							long start = frames[i].Start;
							long finish = i == frames.Count - 1 ? frames[i].Finish : frames[i + 1].Start;

							foreach (ThreadData thread in Group.Threads)
							{
								if ((thread.Description.Mask & (int)ThreadMask.GPU) != (int)(mask & ThreadMask.GPU))
									continue;
								if (thread_index >= 0 && thread_index != thread.Description.ThreadIndex)
									continue;
								Utils.ForEachInsideInterval(thread.Events, start, finish, (frame) =>
								{
									List<Entry> shortEntries = null;
									if (frame.ShortBoard.TryGetValue(Description, out shortEntries))
									{
										foreach (Entry e in shortEntries)
										{
											if (e.Start >= start && e.Start < finish)
												sample.Add(e);
										}
									}
								});
							}

							Samples.Add(sample);
						}
					}
					else
					{
						// Fallback to Individual Calls
						Load(Origin.IndividualCalls);
					}
				}
			}

			if (origin == Origin.IndividualCalls)
			{
				foreach (ThreadData thread in Group.Threads)
				{
					foreach (EventFrame frame in thread.Events)
					{
						List<Entry> shortEntries = null;
						if (frame.ShortBoard.TryGetValue(Description, out shortEntries))
						{
							foreach (Entry e in shortEntries)
							{
								Samples.Add(new Sample(e) { Index = Samples.Count, Name = Description.Name });
							}
						}
					}
				}

				Samples.Sort((a, b) => (a.Entries[0].CompareTo(b.Entries[0])));
			}
		}
	}
}
