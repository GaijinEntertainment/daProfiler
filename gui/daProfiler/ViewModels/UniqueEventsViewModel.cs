using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;
using Profiler.Controls;
using Profiler.Data;
using Profiler.InfrastructureMvvm;
using Profiler.Views;

namespace Profiler.ViewModels
{
    public class UniqueEventsViewModel : BaseViewModel
    {
        public UniqueEventsViewModel()
        {
        }

		private FrameGroup Group { get; set; }
		public EventDescription Description { get; set; }

        private String _title;
        public String Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        private bool _isLoading = false;
        public bool IsLoading
        {
            get { return _isLoading; }
            set { SetProperty(ref _isLoading, value); }
        }

        private UniqueEventsPack _uniqueEvents;
        public UniqueEventsPack UniqueEvents
        {
            get { return _uniqueEvents; }
            set { SetProperty(ref _uniqueEvents, value); }
        }

		private UniqueEvent _hoverSample;
		public UniqueEvent HoverSample
		{
			get { return _hoverSample; }
			set { SetProperty(ref _hoverSample, value); }
		}

		private List<UniqueEvent> _selected;
		public List<UniqueEvent> Selected
		{
			get { return _selected; }
			set { SetProperty(ref _selected, value); }
		}

        public virtual void OnLoaded(UniqueEventsPack stats)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                UniqueEvents = stats;
                IsLoading = false;
				OnChanged?.Invoke();
			}));
        }

        public void Load(FrameGroup group, EventDescription desc)
        {
			if (Group == group && Description == desc)
				return;

			Group = group;
			Description = desc;

            Task.Run(() =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsLoading = true;
                }));

                UniqueEventsPack pack = null;

                if (group != null && desc != null)
                {
                    pack = group.UniqueEventsPack;
                }

                OnLoaded(pack);
            });
        }

		public void OnDataClick(FrameworkElement parent, List<UniqueEvent> sel)
		{
			Selected = sel;
		}

		public void OnDataHover(FrameworkElement parent, int index)
		{
			if (UniqueEvents != null && 0 <= index && index < UniqueEvents.UniqueEvents.Count)
			{
				HoverSample = UniqueEvents.UniqueEvents[index];
			}
			else
			{
				HoverSample = null;
			}
		}


		public delegate void OnChangedHandler();
		public event OnChangedHandler OnChanged;
    }
}
