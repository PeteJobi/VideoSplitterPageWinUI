using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VideoSplitterBase;
using WinUIShared.Helpers;

namespace VideoSplitterPage
{
    public class SplitProcessor(string ffmpegPath) : Processor(ffmpegPath)
    {
        public async Task SpecificSplit(string fileName, string ffmpegPath, SplitRange[] ranges, bool precise)
        {
            var total = ranges.Length;
            leftTextPrimary.Report($"0/{total}");
            rightTextPrimary.Report(ExtendedName(fileName, 0.ToString("D3")));

            var totalDuration = ranges.Select(c => c.End - c.Start).Aggregate((a, b) => a + b);
            var durationElapsed = TimeSpan.Zero;
            var folder = GetOutputFolder(fileName);
            for (var i = 0; i < total; i++)
            {
                var range = ranges[i];
                var segmentDuration = range.End - range.Start;
                leftTextPrimary.Report($"{i}/{total}");
                rightTextPrimary.Report(ExtendedName(fileName, i.ToString("D3")));
                var outputArg = $"{folder}\\{ExtendedName(fileName, i.ToString("D3"))}";
                await (!precise ? StartFfmpegProcess($"-ss {range.Start:hh\\:mm\\:ss\\.fff}  -i \"{fileName}\" -to {range.End:hh\\:mm\\:ss\\.fff} -c copy -map 0 -avoid_negative_ts make_zero {outputArg}", ProgressEventHandler)
                        : StartFfmpegTranscodingProcessDefaultQuality([fileName], outputArg, $"-ss {range.Start:hh\\:mm\\:ss\\.fff} -to {range.End:hh\\:mm\\:ss\\.fff}", ProgressEventHandler));
                if (HasBeenKilled()) return;
                durationElapsed += segmentDuration;
                continue;

                void ProgressEventHandler(double progressPercent, TimeSpan currentTime, TimeSpan duration, int currentFrame)
                {
                    IncrementSpecificSplitProgress(segmentDuration, currentTime, durationElapsed, totalDuration);
                }
            }
            AllDone(total);
        }

        public async Task IntervalSplit(string fileName, string ffmpegPath, TimeSpan segmentDuration)
        {
            var totalSegments = 0;
            var currentSegment = -1;
            await StartFfmpegProcess($"-i \"{fileName}\" -c copy -map 0 -segment_time {segmentDuration} -f segment -reset_timestamps 1 \"{GetOutputFolder(fileName)}/{ExtendedName(fileName, "%03d")}\"", (_, currentTime, duration, _) =>
            {
                if (totalSegments == 0)
                {
                    var fraction = duration / segmentDuration;
                    totalSegments = (int)Math.Ceiling(fraction);
                    leftTextPrimary.Report($"0/{totalSegments}");
                }
                IncrementIntervalSplitProgress(segmentDuration, currentTime, duration, currentSegment, totalSegments);
            }, (line) =>
            {
                if (!line.StartsWith("[segment @")) return;
                currentSegment++;
                leftTextPrimary.Report($"{currentSegment}/{totalSegments}");
                rightTextPrimary.Report(ExtendedName(fileName, currentSegment.ToString("D3")));
            });
            if (HasBeenKilled()) return;
            AllDone(totalSegments);
        }

        public List<string> GetFilePathsFromFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) throw new DirectoryNotFoundException("The specified folder path does not exist.");
            var files = Directory.GetFiles(folderPath);
            return files.ToList();
        }

        void IncrementSpecificSplitProgress(TimeSpan segmentDuration, TimeSpan currentTime, TimeSpan elapsedDuration, TimeSpan totalDuration)
        {
            var fraction = currentTime / segmentDuration;
            progressPrimary.Report(Math.Max(0, Math.Min((currentTime + elapsedDuration) / totalDuration * ProgressMax, ProgressMax)));
            progressSecondary.Report(Math.Max(0, Math.Min(fraction * ProgressMax, ProgressMax)));
            centerTextSecondary.Report($"{Math.Round(fraction * 100, 2)} %");
        }

        void IncrementIntervalSplitProgress(TimeSpan segmentDuration, TimeSpan currentTime, TimeSpan totalDuration, int currentSegment, int totalSegments)
        {
            var currentSegmentDuration = currentSegment < totalSegments - 1 ? segmentDuration : totalDuration - (currentSegment * segmentDuration);
            if (currentSegment == totalSegments - 1) Debug.WriteLine(currentSegmentDuration);
            var fraction = (currentTime - (currentSegment * segmentDuration)) / currentSegmentDuration;
            progressPrimary.Report(currentTime / totalDuration * ProgressMax);
            progressSecondary.Report(Math.Max(0, Math.Min(fraction * ProgressMax, ProgressMax)));
            centerTextSecondary.Report($"{Math.Round(fraction * 100, 2)} %");
        }

        private string ExtendedName(string fileName, string extra) => $"{Path.GetFileNameWithoutExtension(fileName)}{extra}{Path.GetExtension(fileName)}";

        string GetOutputFolder(string path)
        {
            string inputName = Path.GetFileNameWithoutExtension(path);
            string parentFolder = Path.GetDirectoryName(path) ?? throw new NullReferenceException("The specified path is null");
            string outputFolder = Path.Combine(parentFolder, $"{inputName}_SplitVideos");
            if (Directory.Exists(outputFolder))
            {
                Directory.Delete(outputFolder, true);
            }
            Directory.CreateDirectory(outputFolder);
            outputFile = outputFolder;
            return outputFolder;
        }

        void AllDone(int totalSegments)
        {
            currentProcess = null;
            leftTextPrimary.Report($"{totalSegments}/{totalSegments}");
            progressPrimary.Report(ProgressMax);
            progressSecondary.Report(ProgressMax);
            centerTextSecondary.Report("100 %");
        }
    }
}
