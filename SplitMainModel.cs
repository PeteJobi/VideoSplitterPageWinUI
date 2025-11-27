using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using VideoSplitter;
using VideoSplitterBase;
using WinUIShared.Enums;

namespace VideoSplitterPage
{
    public class SplitMainModel : INotifyPropertyChanged
    {
        private TimeSpan _duration;
        public TimeSpan Duration
        {
            get => _duration;
            set => SetProperty(ref _duration, value);
        }

        private bool _inmultiselectmode;
        public bool InMultiSelectMode
        {
            get => _inmultiselectmode;
            set => SetProperty(ref _inmultiselectmode, value);
        }

        private SplitRangeModel? _selectedrange;
        public SplitRangeModel? SelectedRange
        {
            get => _selectedrange;
            set => SetProperty(ref _selectedrange, value, alsoNotify: [nameof(HasMoreBeforeSelected), nameof(HasMoreAfterSelected)]);
        }

        private bool _rangesavailable;
        public bool RangesAvailable
        {
            get => _rangesavailable;
            set => SetProperty(ref _rangesavailable, value);
        }

        private bool _allareselected;
        public bool AllAreSelected
        {
            get => _allareselected;
            set => SetProperty(ref _allareselected, value);
        }

        private bool _doprecisesplit;
        public bool DoPreciseSplit
        {
            get => _doprecisesplit;
            set => SetProperty(ref _doprecisesplit, value);
        }

        private OperationState _state;
        public OperationState State
        {
            get => _state;
            set => SetProperty(ref _state, value, alsoNotify: [nameof(BeforeOperation), nameof(DuringOperation), nameof(AfterOperation)]);
        }

        private bool _isaudio;
        public bool IsAudio
        {
            get => _isaudio;
            set => SetProperty(ref _isaudio, value);
        }

        public bool HasMoreBeforeSelected => SelectedRange != null && SplitModel.SplitRanges.Any(r => r.Start < SelectedRange.Start);
        public bool HasMoreAfterSelected => SelectedRange != null && SplitModel.SplitRanges.Any(r => r.Start > SelectedRange.Start);
        public bool BeforeOperation => State == OperationState.BeforeOperation;
        public bool DuringOperation => State == OperationState.DuringOperation;
        public bool AfterOperation => State == OperationState.AfterOperation;

        public SplitViewModel<SplitRangeModel> SplitModel { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null, params string[] alsoNotify)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            foreach (var dep in alsoNotify) OnPropertyChanged(dep);
            return true;
        }
    }

    public class SplitRangeModel : SplitRange
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if(_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        private bool _ismultiselected;
        public bool IsMultiSelected
        {
            get => _ismultiselected;
            set
            {
                if (_ismultiselected == value) return;
                _ismultiselected = value;
                OnPropertyChanged();
            }
        }
    }
}
