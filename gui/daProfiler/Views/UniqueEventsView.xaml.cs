using Profiler.Data;
using Profiler.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Profiler.Views
{
    /// <summary>
    /// Interaction logic for UniqueEventsView.xaml
    /// </summary>
    public partial class UniqueEventsView : UserControl
    {
        public UniqueEventsView()
        {
            InitializeComponent();
        }

		private void UniqueEventsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			List<UniqueEvent> indices = new List<UniqueEvent>();

			foreach (UniqueEvent sample in UniqueEventsDataGrid.SelectedItems)
				indices.Add(sample);

			if (indices.Count > 0)
			{
				UniqueEventsViewModel vm = DataContext as UniqueEventsViewModel;
				if (vm != null)
					vm.OnDataClick(this, indices);
			}
		}
		public void CopyUniqueInfo(object sender, RoutedEventArgs e)
        {
			if (sender is MenuItem)
            {
                UniqueEventsViewModel vm = DataContext as UniqueEventsViewModel;
                if (vm != null)
                {
                    List<UniqueEvent> stats = vm.Selected;
					String info = $"Name       :\t|\tCalls\t|\tFrames\t|\tMinCall\t|\tMaxCall\t|\tAvgCall\t|\tOnFrame\t|\tTotal\n";
					foreach (UniqueEvent ue in stats)
					{
						info += $"{ue.Description}\t|\t{ue.Calls:00000}\t|\t{ue.Frames:0000}\t|\t{Data.Utils.ConvertMsToString(ue.MinTime)}\t|\t{Data.Utils.ConvertMsToString(ue.MaxTime)}\t|\t{Data.Utils.ConvertMsToString(ue.AvgCall)}\t|\t{Data.Utils.ConvertMsToString(ue.AvgFrame)}\t|\t{Data.Utils.ConvertMsToString(ue.TotalTime)}\n";
					}
					if (stats.Count != 0)
						Clipboard.SetText(info);
				}
			}
		}
	}
}
