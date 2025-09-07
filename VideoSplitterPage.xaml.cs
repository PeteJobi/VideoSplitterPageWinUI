using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using VideoSplitterPage;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using VideoSplitterPage.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VideoSplitter
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class VideoSplitterPage : Page
    {
        private SplitMainModel viewModel;
        private Splitter<SplitRangeModel> splitter;
        private readonly SplitProcessor splitProcessor;
        private string ffmpegPath, videoPath;
        private readonly ScrollingScrollOptions scrollAnimationDisabled = new(ScrollingAnimationMode.Disabled);
        private readonly double progressMax = 1_000_000;
        private string outputFolder;
        private readonly List<string> outputFolders = [];
        private string? navigateTo;
        private CancellationTokenSource playSectionTokenSource = new ();

        public VideoSplitterPage()
        {
            InitializeComponent();
            viewModel = new SplitMainModel { SplitModel = new SplitViewModel<SplitRangeModel>() };
            splitProcessor = new SplitProcessor();
            viewModel.SplitModel.SplitRanges.CollectionChanged += SplitRangesOnCollectionChanged;
            var bindingProxy = (BindingProxy)Application.Current.Resources["GlobalBindingProxy"];
            bindingProxy.Data = viewModel;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            var props = (SplitterProps)e.Parameter;
            ffmpegPath = props.FfmpegPath;
            videoPath = props.VideoPath;
            navigateTo = props.TypeToNavigateTo;
            VideoName.Text = Path.GetFileName(videoPath);
            VideoPlayer.Source = MediaSource.CreateFromUri(new Uri(videoPath));
            VideoPlayer.MediaPlayer.PlaybackSession.NaturalDurationChanged += PlaybackSessionOnNaturalDurationChanged;
        }

        private void PlaybackSessionOnNaturalDurationChanged(MediaPlaybackSession sender, object args)
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                viewModel.Duration = sender.NaturalDuration;
                VideoProgressSlider.Maximum = sender.NaturalDuration.TotalSeconds;
                VideoProgressSlider.Value = 0;
                var progressValueBinding = new Binding
                {
                    Path = new PropertyPath("SplitModel.VideoProgress"),
                    Source = viewModel,
                    Converter = new VideoProgressValueConverter { Duration = viewModel.Duration },
                    Mode = BindingMode.TwoWay
                };
                VideoProgressSlider.SetBinding(Slider.ValueProperty, progressValueBinding);
                VideoDurationText.Text = $@" / {VideoPlayer.MediaPlayer.PlaybackSession.NaturalDuration:hh\:mm\:ss\.fff}";
                splitter = new Splitter<SplitRangeModel>(viewModel.SplitModel, SplitterCanvas, VideoPlayer.MediaPlayer, SectionElementSet, ffmpegPath, videoPath);
            });
        }

        private void PlayPause(object sender, RoutedEventArgs e)
        {
            viewModel.SplitModel.IsPlaying = !viewModel.SplitModel.IsPlaying;
        }

        private void SplitClicked(object sender, RoutedEventArgs e)
        {
            splitter.SplitSection();
        }

        private void ScaleTimelineDown(object sender, RoutedEventArgs e) => TimelineScaleSlider.Value -= 0.25;
        private void ScaleTimelineUp(object sender, RoutedEventArgs e) => TimelineScaleSlider.Value += 0.25;

        private void SplitRangesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (SplitRangeModel item in e.NewItems)
                {
                    item.PropertyChanged += Range_PropertyChanged;
                }
            } else if (e.OldItems != null)
            {
                foreach (SplitRangeModel item in e.OldItems)
                {
                    item.PropertyChanged -= Range_PropertyChanged;
                }
            }
            viewModel.RangesAvailable = viewModel.SplitModel.SplitRanges.Any();
            if (!viewModel.RangesAvailable)
            { //Have to use queue because of weird bug in ListView.SelectionModeChanged when you remove all items and change selection mode
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    viewModel.InMultiSelectMode = false;
                });
            }
            viewModel.AllAreSelected = viewModel.RangesAvailable && viewModel.SplitModel.SplitRanges.All(r => r.IsMultiSelected);
            viewModel.SelectedRange = viewModel.SplitModel.SplitRanges.FirstOrDefault(r => r.IsSelected);
            if (!viewModel.InMultiSelectMode) RangeListView.SelectedItem = viewModel.SelectedRange;
        }

        private void Range_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(SplitRangeModel.IsSelected):
                    var range = (SplitRangeModel)sender!;
                    if (range.IsSelected)
                    {
                        viewModel.SelectedRange = range;
                        foreach (var splitRange in viewModel.SplitModel.SplitRanges)
                        {
                            if(splitRange != range) splitRange.IsSelected = false;
                        }
                        if(!viewModel.InMultiSelectMode) RangeListView.SelectedItem = range;
                    }
                    break;
                case nameof(SplitRangeModel.IsMultiSelected):
                    viewModel.AllAreSelected = viewModel.SplitModel.SplitRanges.All(r => r.IsMultiSelected);
                    if (!viewModel.InMultiSelectMode) return;
                    var range2 = (SplitRangeModel)sender!;
                    if (range2.IsMultiSelected) RangeListView.SelectedItems.Add(sender);
                    else RangeListView.SelectedItems.Remove(sender);
                        break;
            }
        }

        private void SectionElementSet(FrameworkElement section, SplitRangeModel range)
        {
            section.Tapped += (s, e) =>
            {
                if (viewModel.InMultiSelectMode)
                {
                    range.IsMultiSelected = !range.IsMultiSelected;
                    if(range.IsMultiSelected) RangeListView.SelectedItems.Add(range);
                    else RangeListView.SelectedItems.Remove(range);
                }
                else
                {
                    viewModel.SelectedRange = range;
                    RangeListView.SelectedItem = range;
                }
                range.IsSelected = true;
            };
            var deleteButton = (MenuFlyoutItem)section.FindName("Delete");
            deleteButton.Click += (s, e) => viewModel.SplitModel.SplitRanges.Remove(range);
            var playSectionButton = (MenuFlyoutItem)section.FindName("Play");
            playSectionButton.Click += async (s, e) => await splitter.PlaySection(range.Start, range.End);
        }

        private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (SplitRangeModel range in e.RemovedItems)
            {
                if(viewModel.InMultiSelectMode) range.IsMultiSelected = false;
                else range.IsSelected = false;
            }

            foreach (SplitRangeModel range in e.AddedItems)
            {
                if(viewModel.InMultiSelectMode) range.IsMultiSelected = true;
                else
                {
                    range.IsSelected = true;
                    splitter.BringSectionHandleToTop(range);
                }
            }
        }

        private async void MultiSelectModeChanged(object sender, RoutedEventArgs e)
        {
            var selectedRange = viewModel.SelectedRange;
            await Task.Delay(100);
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                if (!viewModel.InMultiSelectMode)
                {
                    foreach (var range in viewModel.SplitModel.SplitRanges)
                    {
                        range.IsMultiSelected = false;
                    }
                }
                if (selectedRange == null) return;
                if (viewModel.InMultiSelectMode) selectedRange.IsMultiSelected = true;
                else selectedRange.IsSelected = true;
            });
        }

        private async void PlaySelectedSection(object sender, RoutedEventArgs e)
        {
            try
            {
                if (viewModel.SelectedRange != null)
                    await splitter.PlaySection(viewModel.SelectedRange.Start, viewModel.SelectedRange.End, playSectionTokenSource.Token);
            }
            catch (TaskCanceledException)
            {
                playSectionTokenSource = new CancellationTokenSource();
            }
        }

        private void StepBackClicked(object sender, RoutedEventArgs e) => VideoPlayer.MediaPlayer.StepBackwardOneFrame();

        private void StepForwardClicked(object sender, RoutedEventArgs e) => VideoPlayer.MediaPlayer.StepForwardOneFrame();

        private void PreviousSectionClicked(object sender, RoutedEventArgs e)
        {
            var selectedIndex = viewModel.SplitModel.SplitRanges.IndexOf(viewModel.SelectedRange);
            if (selectedIndex <= 0) return;
            viewModel.SplitModel.SplitRanges[selectedIndex - 1].IsSelected = true;
            viewModel.SplitModel.VideoProgress = viewModel.SelectedRange.Start;
        }

        private void NextSectionClicked(object sender, RoutedEventArgs e)
        {
            var selectedIndex = viewModel.SplitModel.SplitRanges.IndexOf(viewModel.SelectedRange);
            if(selectedIndex < 0 || selectedIndex == viewModel.SplitModel.SplitRanges.Count) return;
            viewModel.SplitModel.SplitRanges[selectedIndex + 1].IsSelected = true;
            viewModel.SplitModel.VideoProgress = viewModel.SelectedRange.Start;
        }

        private void DeleteSection(object sender, RoutedEventArgs e)
        {
            var range = (SplitRangeModel)((FrameworkElement)sender).DataContext;
            viewModel.SplitModel.SplitRanges.Remove(range);
        }

        private async void PlaySection(object sender, RoutedEventArgs e)
        {
            var range = (SplitRangeModel)((FrameworkElement)sender).DataContext;
            try
            {
                await splitter.PlaySection(range.Start, range.End);
            }
            catch (TaskCanceledException)
            {
                playSectionTokenSource = new CancellationTokenSource();
            }
        }

        private void PrepareAddRange(object sender, RoutedEventArgs e)
        {
            AddStart.Value = viewModel.SplitModel.SplitRanges.LastOrDefault()?.End ?? TimeSpan.Zero;
            AddEnd.Value = AddStart.Value + TimeSpan.FromSeconds(1);
            Debug.WriteLine($"A {AddStart.Value} {AddEnd.Value}");
        }

        private void AddRange(object sender, RoutedEventArgs e)
        {
            viewModel.SplitModel.SplitRanges.Add(new SplitRangeModel{ Start = AddStart.Value, End = AddEnd.Value });
            AddFlyout.Hide();
        }

        private void IntervalSplit(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var range = button.DataContext as SplitRangeModel;
            var container = button.Parent;
            var grid = (Grid)((FrameworkElement)container).Parent;
            var duration = (TimespanTextBox)grid.FindName("Duration");
            var amount = (NumberBox)grid.FindName("Amount");
            var amountRadio = (RadioButton)grid.FindName("AmountRadioButton");
            if (amountRadio.IsChecked == true)
            {
                if(amount.Value == 0) return;
                var chunkDuration = (range != null ? range.End - range.Start : viewModel.Duration) / amount.Value;
                splitter.SplitIntervals(chunkDuration, range);
            }else splitter.SplitIntervals(duration.Value, range);
            while (container != null && container is not FlyoutPresenter)
            {
                container = VisualTreeHelper.GetParent(container);
            }
            var flyoutPresenter = (FlyoutPresenter)container;
            var popup = flyoutPresenter.Parent as Popup;
            popup.IsOpen = false;
        }

        private void IntervalTypeChecked(object sender, RoutedEventArgs e)
        {
            var radio = (RadioButton)sender;
            if (radio.Parent == null) return;
            var grid = (Grid)((FrameworkElement)radio.Parent).Parent;
            var duration = (TimespanTextBox)grid.FindName("Duration");
            var amount = (NumberBox)grid.FindName("Amount");
            var amountRadio = (RadioButton)grid.FindName("AmountRadioButton");
            duration.Visibility = amountRadio.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
            amount.Visibility = amountRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SelectDeselectAll(object sender, RoutedEventArgs e)
        {
            var allAreSelected = viewModel.AllAreSelected;
            viewModel.InMultiSelectMode = true;
            foreach (var range in viewModel.SplitModel.SplitRanges)
            {
                range.IsMultiSelected = !allAreSelected;
            }
        }

        private void DeleteSelected(object sender, RoutedEventArgs e)
        {
            for (var i = 0; i < viewModel.SplitModel.SplitRanges.Count; i++)
            {
                var range = viewModel.SplitModel.SplitRanges[i];
                if (!range.IsMultiSelected) continue;
                viewModel.SplitModel.SplitRanges.Remove(range);
                i--; // Adjust index after removal
            }
            DeleteFlyout.Hide();
        }

        private void JoinSelected(object sender, RoutedEventArgs e)
        {
            splitter.JoinSections(viewModel.SplitModel.SplitRanges.Where(r => r.IsMultiSelected).ToArray());
            JoinFlyout.Hide();
        }

        private void ScrollView_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var wheelDelta = e.GetCurrentPoint(ScrollView).Properties.MouseWheelDelta;
            var scrollAmount = wheelDelta * -1;
            ScrollView.ScrollBy(scrollAmount, 0, scrollAnimationDisabled);
            e.Handled = true;
        }

        private void ScrollView_OnViewChanged(ScrollView sender, object args)
        {
            var offset = ScrollView.HorizontalOffset;
            var snappedOffset = Math.Round(offset);
            if (Math.Abs(offset - snappedOffset) > 0.01)
            {
                ScrollView.ScrollTo(snappedOffset, 0, scrollAnimationDisabled);
            }
        }

        private async void ProcessSplit(object sender, RoutedEventArgs e)
        {
            var rangesToProcess = (viewModel.InMultiSelectMode ? 
                viewModel.SplitModel.SplitRanges.Where(r => r.IsMultiSelected) : 
                viewModel.SplitModel.SplitRanges).Where(r => r.Start != r.End).Cast<SplitRange>().ToArray();
            if (!rangesToProcess.Any()) return;
            viewModel.State = OperationState.DuringOperation;

            var fileProgress = new Progress<FileProgress>(progress =>
            {
                if (progress.TotalRangeCount != null) TotalRangeCount.Text = progress.TotalRangeCount;
                if (progress.CurrentRangeFileName != null)
                    CurrentRangeFileName.Text = progress.CurrentRangeFileName;
            });
            var valueProgress = new Progress<ValueProgress>(progress =>
            {
                OverallSplitProgress.Value = progress.OverallProgress;
                CurrentSplitProgress.Value = progress.CurrentActionProgress;
                CurrentSplitProgressText.Text = progress.CurrentActionProgressText;
            });
            var failed = false;
            string? errorMessage = null;

            var (isInterval, interval) = IsIntervalSplit(rangesToProcess);
            try
            {
                if (isInterval)
                {
                    await splitProcessor.IntervalSplit(videoPath, ffmpegPath, interval, progressMax, fileProgress,
                        valueProgress, SetOutputFolder, ErrorActionFromFfmpeg);
                }
                else
                {
                    await splitProcessor.SpecificSplit(videoPath, ffmpegPath, rangesToProcess.ToArray(), progressMax,
                        fileProgress, valueProgress, SetOutputFolder, ErrorActionFromFfmpeg);
                }

                if (viewModel.State == OperationState.BeforeOperation) return; //Canceled
                if (failed)
                {
                    viewModel.State = OperationState.BeforeOperation;
                    await ErrorAction(errorMessage!);
                    await splitProcessor.Cancel(outputFolder);
                    return;
                }

                viewModel.State = OperationState.AfterOperation;
                CurrentRangeFileName.Text = "Done";
                outputFolders.Add(outputFolder);
            }
            catch (Exception ex)
            {
                await ErrorAction(ex.Message);
                viewModel.State = OperationState.BeforeOperation;
            }

            void ErrorActionFromFfmpeg(string message)
            {
                failed = true;
                errorMessage = message;
            }

            void SetOutputFolder(string folder)
            {
                outputFolder = folder;
            }

            async Task ErrorAction(string message)
            {
                ErrorDialog.Title = "Split operation failed";
                ErrorDialog.Content = message;
                await ErrorDialog.ShowAsync();
            }
        }

        private (bool IsInterval, TimeSpan Interval) IsIntervalSplit(SplitRange[] ranges)
        {
            if (ranges.First().Start != TimeSpan.Zero || ranges.Last().End != viewModel.Duration) return (false, TimeSpan.Zero);
            var lastStop = ranges.First().Start;
            var interval = ranges.First().End - ranges.First().Start;
            foreach (var range in ranges)
            {
                if(range.Start != lastStop) return (false, TimeSpan.Zero);
                if(range.End - range.Start > interval || (range.End - range.Start < interval && range != ranges.Last())) return (false, TimeSpan.Zero);
                lastStop = range.End;
            }

            return (true, interval);
        }

        private void PauseOrViewSplit_OnClick(object sender, RoutedEventArgs e)
        {
            if(viewModel.State == OperationState.AfterOperation)
            {
                splitProcessor.ViewFiles(outputFolder);
                return;
            }

            if(viewModel.ProcessPaused)
            {
                splitProcessor.Resume();
                viewModel.ProcessPaused = false;
            }
            else
            {
                splitProcessor.Pause();
                viewModel.ProcessPaused = true;
            }
        }

        private void CancelOrCloseSplit_OnClick(object sender, RoutedEventArgs e)
        {
            if (viewModel.State == OperationState.AfterOperation)
            {
                viewModel.State = OperationState.BeforeOperation;
                return;
            }

            FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
        }

        private async void CancelProcess(object sender, RoutedEventArgs e)
        {
            await splitProcessor.Cancel(outputFolder);
            viewModel.State = OperationState.BeforeOperation;
            viewModel.ProcessPaused = false;
            CancelFlyout.Hide();
        }

        private void GoBack(object sender, RoutedEventArgs e)
        {
            _ = splitProcessor.Cancel(outputFolder);
            _ = splitter.Dispose();
            VideoPlayer.MediaPlayer.Pause();
            playSectionTokenSource.Cancel();
            if (navigateTo == null) Frame.GoBack();
            else Frame.NavigateToType(Type.GetType(navigateTo), outputFolders, new FrameNavigationOptions { IsNavigationStackEnabled = false });
        }
    }

    public class SplitterProps
    {
        public string FfmpegPath { get; set; }
        public string VideoPath { get; set; }
        public string? TypeToNavigateTo { get; set; }
    }

    public class VideoProgressValueConverter : IValueConverter
    {
        public TimeSpan Duration { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (Duration == TimeSpan.Zero) return 0d;
            return (TimeSpan)value / Duration * Duration.TotalSeconds;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (Duration == TimeSpan.Zero) return TimeSpan.Zero;
            return (double)value / Duration.TotalSeconds * Duration;
        }
    }

    public class BindingProxy : DependencyObject
    {
        public SplitMainModel Data
        {
            get => (SplitMainModel)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));
    }
}
