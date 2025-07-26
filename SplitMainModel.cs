using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using VideoSplitter;

namespace VideoSplitterPage
{
    public class SplitMainModel : INotifyPropertyChanged
    {
        private bool _isplaying;
        public bool IsPlaying
        {
            get => _isplaying;
            set
            {
                if (_isplaying == value) return;
                _isplaying = value;
                OnPropertyChanged();
            }
        }

        private TimeSpan _duration;
        public TimeSpan Duration
        {
            get => _duration;
            set
            {
                if (_duration == value) return;
                _duration = value;
                OnPropertyChanged();
            }
        }

        private bool _inmultiselectmode;
        public bool InMultiSelectMode
        {
            get => _inmultiselectmode;
            set
            {
                if (_inmultiselectmode == value) return;
                _inmultiselectmode = value;
                OnPropertyChanged();
            }
        }

        private SplitRangeModel? _selectedrange;
        public SplitRangeModel? SelectedRange
        {
            get => _selectedrange;
            set
            {
                if (_selectedrange != value)
                {
                    _selectedrange = value;
                    OnPropertyChanged();
                }
                OnPropertyChanged(nameof(HasMoreBeforeSelected));
                OnPropertyChanged(nameof(HasMoreAfterSelected));
            }
        }

        private bool _rangesavailable;
        public bool RangesAvailable
        {
            get => _rangesavailable;
            set
            {
                _rangesavailable = value;
                OnPropertyChanged();
            }
        }

        private bool _allareselected;
        public bool AllAreSelected
        {
            get => _allareselected;
            set
            {
                _allareselected = value;
                OnPropertyChanged();
            }
        }

        private OperationState _state;
        public OperationState State
        {
            get => _state;
            set
            {
                _state = value;
                OnPropertyChanged(nameof(BeforeOperation));
                OnPropertyChanged(nameof(DuringOperation));
                OnPropertyChanged(nameof(AfterOperation));
            }
        }

        private bool _processpaused;
        public bool ProcessPaused
        {
            get => _processpaused;
            set
            {
                _processpaused = value;
                OnPropertyChanged();
            }
        }

        public bool HasMoreBeforeSelected => SelectedRange != null && SplitModel.SplitRanges.Any(r => r.Start < SelectedRange.Start);
        public bool HasMoreAfterSelected => SelectedRange != null && SplitModel.SplitRanges.Any(r => r.Start > SelectedRange.Start);
        public bool BeforeOperation => State == OperationState.BeforeOperation;
        public bool DuringOperation => State == OperationState.DuringOperation;
        public bool AfterOperation => State == OperationState.AfterOperation;

        public SplitViewModel<SplitRangeModel> SplitModel { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
    public enum OperationState
    {
        BeforeOperation, DuringOperation, AfterOperation
    }
}
