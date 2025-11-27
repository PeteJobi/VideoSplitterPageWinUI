using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VideoSplitterBase;
using WinUIShared.Helpers;

namespace VideoSplitterPage
{
    public class SplitProcessor(string ffmpegPath) : Processor(ffmpegPath)
    {
        public async Task SpecificSplit(string fileName, string ffmpegPath, SplitRange[] ranges, bool precise, IProgress<FileProgress> fileProgress, IProgress<ValueProgress> valueProgress, Action<string> setOutputFolder, Action<string> error)
        {
            var total = ranges.Length;
            fileProgress.Report(new FileProgress
            {
                TotalRangeCount = $"0/{total}",
                CurrentRangeFileName = ExtendedName(fileName, 0.ToString("D3"))
            });

            var totalDuration = ranges.Select(c => c.End - c.Start).Aggregate((a, b) => a + b);
            var durationElapsed = TimeSpan.Zero;
            var folder = GetOutputFolder(fileName, setOutputFolder);
            for (var i = 0; i < total; i++)
            {
                var range = ranges[i];
                var segmentDuration = range.End - range.Start;
                fileProgress.Report(new FileProgress { TotalRangeCount = $"{i}/{total}", CurrentRangeFileName = ExtendedName(fileName, i.ToString("D3")) });
                var command = !precise
                    ? $"-ss {range.Start:hh\\:mm\\:ss\\.fff} -i \"{fileName}\" -to {range.End:hh\\:mm\\:ss\\.fff} -c copy -map 0 -avoid_negative_ts make_zero"
                    : $"-i \"{fileName}\" -ss {range.Start:hh\\:mm\\:ss\\.fff} -to {range.End:hh\\:mm\\:ss\\.fff} -c:v libx265 -c:a copy -crf 18";
                await StartFfmpegProcess($"{command} \"{folder}\\{ExtendedName(fileName, i.ToString("D3"))}\"", (sender, args) =>
                {
                    if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                    Debug.WriteLine(args.Data);
                    if (CheckFileNameLongErrorSplit(args.Data, error)) return;
                    if (!args.Data.StartsWith("frame")) return;
                    if (CheckNoSpaceDuringProcess(args.Data, error)) return;
                    var matchCollection = Regex.Matches(args.Data, @"^frame=\s*\d+\s.+?time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    IncrementSpecificSplitProgress(segmentDuration, TimeSpan.Parse(matchCollection[0].Groups[1].Value), durationElapsed, totalDuration, valueProgress);
                });
                if (HasBeenKilled()) return;
                durationElapsed += segmentDuration;
            }
            AllDone(total, fileProgress, valueProgress);
        }

        public async Task IntervalSplit(string fileName, string ffmpegPath, TimeSpan segmentDuration, IProgress<FileProgress> fileProgress, IProgress<ValueProgress> valueProgress, Action<string> setOutputFolder, Action<string> error)
        {
            var duration = TimeSpan.MinValue;
            var totalSegments = 0;
            var currentSegment = -1;
            await StartFfmpegProcess($"-i \"{fileName}\" -c copy -map 0 -segment_time {segmentDuration} -f segment -reset_timestamps 1 \"{GetOutputFolder(fileName, setOutputFolder)}/{ExtendedName(fileName, "%03d")}\"", (sender, args) =>
            {
                Debug.WriteLine(args.Data);
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                if (CheckFileNameLongErrorSplit(args.Data, error)) return;
                if (duration == TimeSpan.MinValue)
                {
                    var matchCollection = Regex.Matches(args.Data, @"\s*Duration:\s(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    duration = TimeSpan.Parse(matchCollection[0].Groups[1].Value);
                    var fraction = duration / segmentDuration;
                    totalSegments = (int)Math.Ceiling(fraction);
                    fileProgress.Report(new FileProgress
                    {
                        TotalRangeCount = $"0/{totalSegments}"
                    });
                }
                else if (args.Data.StartsWith("[segment @"))
                {
                    currentSegment++;
                    fileProgress.Report(new FileProgress
                    {
                        TotalRangeCount = $"{currentSegment}/{totalSegments}",
                        CurrentRangeFileName = ExtendedName(fileName, currentSegment.ToString("D3"))
                    });
                }
                else if (args.Data.StartsWith("frame"))
                {
                    if (CheckNoSpaceDuringProcess(args.Data, error)) return;
                    var matchCollection = Regex.Matches(args.Data, @"^frame=\s*\d+\s.+?time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    IncrementIntervalSplitProgress(segmentDuration, TimeSpan.Parse(matchCollection[0].Groups[1].Value), duration, currentSegment, totalSegments, valueProgress);
                }
            });
            if (HasBeenKilled()) return;
            AllDone(totalSegments, fileProgress, valueProgress);
        }

        public List<string> GetFilePathsFromFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) throw new DirectoryNotFoundException("The specified folder path does not exist.");
            var files = Directory.GetFiles(folderPath);
            return files.ToList();
        }

        public bool IsAudio(string mediaPath)
        {
            var ext = Path.GetExtension(mediaPath).ToLower();
            return ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".wma";
        }

        void IncrementSpecificSplitProgress(TimeSpan segmentDuration, TimeSpan currentTime, TimeSpan elapsedDuration, TimeSpan totalDuration, IProgress<ValueProgress> progress)
        {
            var fraction = currentTime / segmentDuration;
            progress.Report(new ValueProgress
            {
                OverallProgress = Math.Max(0, Math.Min((currentTime + elapsedDuration) / totalDuration * ProgressMax, ProgressMax)),
                CurrentActionProgress = Math.Max(0, Math.Min(fraction * ProgressMax, ProgressMax)),
                CurrentActionProgressText = $"{Math.Round(fraction * 100, 2)} %"
            });
        }

        void IncrementIntervalSplitProgress(TimeSpan segmentDuration, TimeSpan currentTime, TimeSpan totalDuration, int currentSegment, int totalSegments, IProgress<ValueProgress> progress)
        {
            var currentSegmentDuration = currentSegment < totalSegments - 1 ? segmentDuration : totalDuration - (currentSegment * segmentDuration);
            if (currentSegment == totalSegments - 1) Debug.WriteLine(currentSegmentDuration);
            var fraction = (currentTime - (currentSegment * segmentDuration)) / currentSegmentDuration;
            progress.Report(new ValueProgress
            {
                OverallProgress = currentTime / totalDuration * ProgressMax,
                CurrentActionProgress = Math.Max(0, Math.Min(fraction * ProgressMax, ProgressMax)),
                CurrentActionProgressText = $"{Math.Round(fraction * 100, 2)} %"
            });
        }

        private string ExtendedName(string fileName, string extra) => $"{Path.GetFileNameWithoutExtension(fileName)}{extra}{Path.GetExtension(fileName)}";

        string GetOutputFolder(string path, Action<string> setFolder)
        {
            string inputName = Path.GetFileNameWithoutExtension(path);
            string parentFolder = Path.GetDirectoryName(path) ?? throw new NullReferenceException("The specified path is null");
            string outputFolder = Path.Combine(parentFolder, $"{inputName}_SplitVideos");
            setFolder(outputFolder);
            if (Directory.Exists(outputFolder))
            {
                Directory.Delete(outputFolder, true);
            }
            Directory.CreateDirectory(outputFolder);
            outputFile = outputFolder;
            return outputFolder;
        }

        void AllDone(int totalSegments, IProgress<FileProgress> fileProgress, IProgress<ValueProgress> valueProgress)
        {
            currentProcess = null;
            fileProgress.Report(new FileProgress
            {
                TotalRangeCount = $"{totalSegments}/{totalSegments}"
            });
            valueProgress.Report(new ValueProgress
            {
                OverallProgress = ProgressMax, CurrentActionProgress = ProgressMax, CurrentActionProgressText = "100 %"
            });
        }
    }

    public struct FileProgress
    {
        public string? TotalRangeCount { get; set; }
        public string? CurrentRangeFileName { get; set; }
    }

    public struct ValueProgress
    {
        public double OverallProgress { get; set; }
        public double CurrentActionProgress { get; set; }
        public string CurrentActionProgressText { get; set; }
    }
}
