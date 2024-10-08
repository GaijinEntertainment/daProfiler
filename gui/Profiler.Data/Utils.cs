using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Profiler.Data
{
	public enum CompressionAlgorithm { UNCOMPRESSED = 0, ZLIB_ALGO = 1, ZSTD_ALGO = 2};
	public static class Utils
	{
		public static String ConvertMsToString(double duration)
    	{
			if (duration < 0.0001)
			{
     			return String.Format(CultureInfo.InvariantCulture, "{0:0.0} ns", duration * 1000000);
			} else if (duration < 0.01)
			{
     			return String.Format(CultureInfo.InvariantCulture, "{0:0.00} us", duration * 1000);
			} else if (duration < 0.1)
			{
     			return String.Format(CultureInfo.InvariantCulture, "{0:0.0} us", duration * 1000);
			} else if (duration < 1.0)
			{
    			return String.Format(CultureInfo.InvariantCulture, "{0:0} us", duration * 1000);
			}
			else if (duration < 10.0)
    		{
				return String.Format(CultureInfo.InvariantCulture, "{0:0.000} ms", duration);
			}
			else if (duration < 100.0)
    		{
				return String.Format(CultureInfo.InvariantCulture, "{0:0.00} ms", duration);
			}
			else if (duration < 1000.0)
			{
				return String.Format(CultureInfo.InvariantCulture, "{0:0.0} ms", duration);
    		}
			else if (duration < 10000.0)
			{
				return String.Format(CultureInfo.InvariantCulture, "{0:0.000} sec", duration / 1000.0);
			}
			else
			{
				return String.Format(CultureInfo.InvariantCulture, "{0:0.0} sec", duration / 1000.0);
			}
		}
		public static int BinarySearchIndex<T, U>(IList<T> frames, U value, Func<T, U> mapping) where U : IComparable
		{
			if (frames == null || frames.Count == 0)
				return -1;

			int left = 0;
			int right = frames.Count - 1;

			if (value.CompareTo(mapping(frames[0])) <= 0)
				return left;

			if (value.CompareTo(mapping(frames[frames.Count - 1])) >= 0)
				return right;

			while (left != right)
			{
				int index = (left + right + 1) / 2;

				if (value.CompareTo(mapping(frames[index])) < 0)
					right = index - 1;
				else
					left = index;
			}

			return left;
		}

		public static int BinarySearchClosestIndex<T>(IList<T> frames, long value) where T : ITick
		{
			if (frames == null || frames.Count == 0)
				return -1;

			int left = 0;
			int right = frames.Count - 1;

			if (value <= frames[0].Start)
				return left;

			if (value >= frames[frames.Count - 1].Start)
				return right;

			while (left != right)
			{
				int index = (left + right + 1) / 2;

				if (frames[index].Start > value)
					right = index - 1;
				else
					left = index;
			}
			while (left > 0 && frames[left-1].Start == value)
			  --left;

			return left;
		}

		public static int BinarySearchExactIndex<T>(List<T> frames, long value) where T : IDurable
		{
			int index = BinarySearchClosestIndex(frames, value);
			if (index < 0)
				return -1;

			for (int i = index; i < Math.Min(index + 2, frames.Count); ++i)
			{
				if (frames[i].Start <= value && value < frames[i].Finish)
					return i;
			}

			return -1;
		}

		public static void ForEachInsideInterval<T>(List<T> frames, Durable interval, Action<T> action) where T : ITick
		{
			ForEachInsideInterval(frames, interval.Start, interval.Finish, action);
		}

		public static void ForEachInsideInterval<T>(List<T> frames, long start, long finish, Action<T> action) where T : ITick
		{
			int left = BinarySearchClosestIndex(frames, start);
			int right = BinarySearchClosestIndex(frames, finish);

			for (int i = left; i <= right && i != -1; ++i)
			{
				action(frames[i]);
			}
		}

		public static void ForEachInsideInterval<T>(List<T> frames, Durable interval, Action<T, int> action) where T : ITick
		{
			ForEachInsideInterval(frames, interval.Start, interval.Finish, action);
		}

		public static void ForEachInsideInterval<T>(List<T> frames, long start, long finish, Action<T, int> action) where T : ITick
		{
			int left = BinarySearchClosestIndex(frames, start);
			int right = BinarySearchClosestIndex(frames, finish);

			for (int i = left; i <= right && i != -1; ++i)
			{
				action(frames[i], i);
			}
		}

		public static void ForEachInsideIntervalStrict<T>(List<T> frames, Durable interval, Action<T> action) where T : ITick
		{
			ForEachInsideIntervalStrict(frames, interval.Start, interval.Finish, action);
		}

		public static void ForEachInsideIntervalStrict<T>(List<T> frames, long start, long finish, Action<T> action) where T : ITick
		{
			int left = BinarySearchClosestIndex(frames, start);
			int right = BinarySearchClosestIndex(frames, finish);

			for (int i = left; i <= right && i != -1; ++i)
			{
				if (start <= frames[i].Start && frames[i].Start < finish)
					action(frames[i]);
			}
		}

		public static String ReadBinaryString(BinaryReader reader)
		{
			return System.Text.Encoding.ASCII.GetString(reader.ReadBytes(reader.ReadInt32()));
		}

        public static uint ReadVlqUInt(BinaryReader r)
        {
            byte b = r.ReadByte();

            // the first byte has 6 bits of the raw value
            uint raw = (uint)(b & 0x7F);

            // first continue flag is the second bit from the top, shift it into the sign
            bool cont = (b & 0x80) != 0;

			for (int shift = 7; shift < 32 && cont; shift += 7)
			{
				b = r.ReadByte();
				cont = (b & 0x80) != 0;
				b &= 0x7F;
				// these bytes have 7 bits of the raw value
				raw |= ((uint)b) << shift;
			}
			return (uint)raw;
        }

        public static int ReadVlqInt(BinaryReader r)
        {
            byte b = r.ReadByte();

            // the first byte has 6 bits of the raw value
            ulong raw = (ulong)(b & 0x3F);

            bool negative = (b & 0x40) != 0;

            // first continue flag is the second bit from the top, shift it into the sign
            bool cont = (b & 0x80) != 0;

			for (int shift = 7; shift < 32 && cont; shift += 7)
			{
				b = r.ReadByte();
				cont = (b & 0x80) != 0;
				b &= 0x7F;

				// these bytes have 7 bits of the raw value
				raw |= ((ulong)b) << shift;
            }

            // We use unchecked here to handle int.MinValue
            return negative ? unchecked(-(int)raw) : (int)raw;
        }

		public static String ReadVlqString(BinaryReader reader)
		{
			return System.Text.Encoding.ASCII.GetString(reader.ReadBytes((int)ReadVlqUInt(reader)));
		}

        public static void WriteBinaryString(BinaryWriter writer, String text)
        {
            writer.Write(text != null ? text.Length : 0);
            if (text != null)
            {
                byte[] data = System.Text.Encoding.ASCII.GetBytes(text);
                writer.Write(data);
            }
        }

        public static String ReadBinaryWideString(BinaryReader reader)
		{
			return System.Text.Encoding.Unicode.GetString(reader.ReadBytes(reader.ReadInt32()));
		}


		public static bool IsSorted<T>(this List<T> list) where T : IComparable<T>
		{
			for (int i = 0; i < list.Count - 1; ++i)
				if (list[i].CompareTo(list[i + 1]) > 0)
					return false;

			return true;
		}

		public static String GenerateShortGUID()
		{
			return Regex.Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "[/+=]", "");
		}

		public static void CopyStream(Stream from, Stream to, Action<double> onProgress, int bufferSize = 64 << 10)
		{
			byte[] buffer = new byte[bufferSize];

			int read = 0;
			int totalRead = 0;

			while ((read = from.Read(buffer, 0, bufferSize)) > 0)
			{
				to.Write(buffer, 0, read);
				totalRead += read;
				onProgress((double)totalRead / from.Length);
			}
		}

		public static int ComputeLevenshteinDistance(string s, string t)
		{
			int n = s.Length;
			int m = t.Length;
			int[,] d = new int[n + 1, m + 1];

			// Step 1
			if (n == 0)
			{
				return m;
			}

			if (m == 0)
			{
				return n;
			}

			// Step 2
			for (int i = 0; i <= n; d[i, 0] = i++)
			{
			}

			for (int j = 0; j <= m; d[0, j] = j++)
			{
			}

			// Step 3
			for (int i = 1; i <= n; i++)
			{
				//Step 4
				for (int j = 1; j <= m; j++)
				{
					// Step 5
					int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

					// Step 6
					d[i, j] = Math.Min(
						Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
						d[i - 1, j - 1] + cost);
				}
			}
			// Step 7
			return d[n, m];
		}

        public static String GetUnsecureBase64String(SecureString text)
        {
            String password = new System.Net.NetworkCredential(string.Empty, text).Password;
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(password);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        public static SecureString GetSecureStringFromBase64String(String text)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(text);
            return new System.Net.NetworkCredential("", System.Text.Encoding.UTF8.GetString(base64EncodedBytes)).SecurePassword;
        }

		public const double LuminanceThreshold = 0.2;

		public static double GetLuminance(System.Windows.Media.Color color)
		{
			return 0.2126 * color.ScR + 0.7152 * color.ScG + 0.0722 * color.ScB;
		}
		public static bool EqualColors(System.Windows.Media.Color a, System.Windows.Media.Color b)
		{
		    if (System.Windows.Media.Color.AreClose(a,b))
			    return true;
			System.Windows.Media.Color c = a - b;
			c.ScR = Math.Abs(c.ScR);c.ScG = Math.Abs(c.ScG); c.ScB = Math.Abs(c.ScB);
			return Utils.GetLuminance(c) < 0.1;
		}
	}
}
