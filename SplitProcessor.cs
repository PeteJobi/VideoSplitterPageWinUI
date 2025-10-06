using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VideoSplitter;

namespace VideoSplitterPage
{
    public class SplitProcessor
    {
        static readonly string[] allowedExts = { ".mkv", ".mp4", ".mp3" };
        private Process? currentProcess;
        private bool hasBeenKilled;
        private const string FileNameLongError =
            "The source file name is too long. Shorten it to get the total number of characters in the destination directory lower than 256.\n\nDestination directory: ";

        public async Task SpecificSplit(string fileName, string ffmpegPath, SplitRange[] ranges, double progressMax, IProgress<FileProgress> fileProgress, IProgress<ValueProgress> valueProgress, Action<string> setOutputFolder, Action<string> error)
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
                await StartProcess(ffmpegPath, $"-ss {range.Start:hh\\:mm\\:ss\\.fff} -i \"{fileName}\" -c copy -map 0 -to {segmentDuration:hh\\:mm\\:ss\\.fff} -avoid_negative_ts make_zero \"{folder}\\{ExtendedName(fileName, i.ToString("D3"))}\"", null, (sender, args) =>
                {
                    if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                    Debug.WriteLine(args.Data);
                    if (FileNameLongErrorSplit(args.Data, error)) return;
                    if (!args.Data.StartsWith("frame")) return;
                    if (CheckNoSpaceDuringBreakMerge(args.Data, error)) return;
                    var matchCollection = Regex.Matches(args.Data, @"^frame=\s*\d+\s.+?time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    IncrementSpecificSplitProgress(segmentDuration, TimeSpan.Parse(matchCollection[0].Groups[1].Value), durationElapsed, totalDuration, progressMax, valueProgress);
                });
                if (HasBeenKilled()) return;
                durationElapsed += segmentDuration;
            }
            AllDone(total, progressMax, fileProgress, valueProgress);
        }

        public async Task IntervalSplit(string fileName, string ffmpegPath, TimeSpan segmentDuration, double progressMax, IProgress<FileProgress> fileProgress, IProgress<ValueProgress> valueProgress, Action<string> setOutputFolder, Action<string> error)
        {
            var duration = TimeSpan.MinValue;
            var totalSegments = 0;
            var currentSegment = -1;
            await StartProcess(ffmpegPath, $"-i \"{fileName}\" -c copy -map 0 -segment_time {segmentDuration} -f segment -reset_timestamps 1 \"{GetOutputFolder(fileName, setOutputFolder)}/{ExtendedName(fileName, "%03d")}\"", null, (sender, args) =>
            {
                Debug.WriteLine(args.Data);
                if (string.IsNullOrWhiteSpace(args.Data) || hasBeenKilled) return;
                if (FileNameLongErrorSplit(args.Data, error)) return;
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
                    if (CheckNoSpaceDuringBreakMerge(args.Data, error)) return;
                    var matchCollection = Regex.Matches(args.Data, @"^frame=\s*\d+\s.+?time=(\d{2}:\d{2}:\d{2}\.\d{2}).+");
                    if (matchCollection.Count == 0) return;
                    IncrementIntervalSplitProgress(segmentDuration, TimeSpan.Parse(matchCollection[0].Groups[1].Value), duration, currentSegment, totalSegments, progressMax, valueProgress);
                }
            });
            if (HasBeenKilled()) return;
            AllDone(totalSegments, progressMax, fileProgress, valueProgress);
        }

        public List<string> GetFilePathsFromFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) throw new DirectoryNotFoundException("The specified folder path does not exist.");
            var files = Directory.GetFiles(folderPath);
            return files.ToList();
        }

        void IncrementSpecificSplitProgress(TimeSpan segmentDuration, TimeSpan currentTime, TimeSpan elapsedDuration, TimeSpan totalDuration, double max, IProgress<ValueProgress> progress)
        {
            var fraction = currentTime / segmentDuration;
            progress.Report(new ValueProgress
            {
                OverallProgress = Math.Max(0, Math.Min((currentTime + elapsedDuration) / totalDuration * max, max)),
                CurrentActionProgress = Math.Max(0, Math.Min(fraction * max, max)),
                CurrentActionProgressText = $"{Math.Round(fraction * 100, 2)} %"
            });
        }

        void IncrementIntervalSplitProgress(TimeSpan segmentDuration, TimeSpan currentTime, TimeSpan totalDuration, int currentSegment, int totalSegments, double max, IProgress<ValueProgress> progress)
        {
            var currentSegmentDuration = currentSegment < totalSegments - 1 ? segmentDuration : totalDuration - (currentSegment * segmentDuration);
            if (currentSegment == totalSegments - 1) Debug.WriteLine(currentSegmentDuration);
            var fraction = (currentTime - (currentSegment * segmentDuration)) / currentSegmentDuration;
            progress.Report(new ValueProgress
            {
                OverallProgress = (int)(currentTime / totalDuration * max),
                CurrentActionProgress = Math.Max(0, Math.Min((int)(fraction * max), max)),
                CurrentActionProgressText = $"{Math.Round(fraction * 100, 2)} %"
            });
        }

        private string ExtendedName(string fileName, string extra) => $"{Path.GetFileNameWithoutExtension(fileName)}{extra}{Path.GetExtension(fileName)}";

        private bool CheckNoSpaceDuringBreakMerge(string line, Action<string> error)
        {
            if (!line.EndsWith("No space left on device") && !line.EndsWith("I/O error")) return false;
            SuspendProcess(currentProcess);
            error($"Process failed.\nError message: {line}");
            return true;
        }

        private static bool FileNameLongErrorSplit(string line, Action<string> error)
        {
            const string noSuchDirectory = ": No such file or directory";
            if (!line.EndsWith(noSuchDirectory)) return false;
            error(FileNameLongError + line[..^noSuchDirectory.Length]);
            return true;
        }

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
            return outputFolder;
        }

        void AllDone(int totalSegments, double max, IProgress<FileProgress> fileProgress, IProgress<ValueProgress> valueProgress)
        {
            currentProcess = null;
            fileProgress.Report(new FileProgress
            {
                TotalRangeCount = $"{totalSegments}/{totalSegments}"
            });
            valueProgress.Report(new ValueProgress
            {
                OverallProgress = max, CurrentActionProgress = max, CurrentActionProgressText = "100 %"
            });
        }

        bool HasBeenKilled()
        {
            if (!hasBeenKilled) return false;
            hasBeenKilled = false;
            return true;
        }

        public async Task Cancel(string outputFolder)
        {
            if(currentProcess == null) return;
            currentProcess.Kill();
            await currentProcess.WaitForExitAsync();
            hasBeenKilled = true;
            currentProcess = null;
            if (Directory.Exists(outputFolder)) Directory.Delete(outputFolder, true);
        }

        public void Pause()
        {
            if (currentProcess == null) return;
            foreach (ProcessThread pT in currentProcess.Threads)
            {
                var pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

                if (pOpenThread == IntPtr.Zero)
                {
                    continue;
                }

                SuspendThread(pOpenThread);

                CloseHandle(pOpenThread);
            }
        }

        public void Resume()
        {
            if (currentProcess == null) return;
            if (currentProcess.ProcessName == string.Empty)
                return;

            foreach (ProcessThread pT in currentProcess.Threads)
            {
                var pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

                if (pOpenThread == IntPtr.Zero)
                {
                    continue;
                }

                int suspendCount;
                do
                {
                    suspendCount = ResumeThread(pOpenThread);
                } while (suspendCount > 0);

                CloseHandle(pOpenThread);
            }
        }

        public void ViewFiles(string folder)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = "explorer";
            info.Arguments = $"/e, /select, \"{folder}\"";
            Process.Start(info);
        }

        async Task StartProcess(string processFileName, string arguments, DataReceivedEventHandler? outputEventHandler, DataReceivedEventHandler? errorEventHandler)
        {
            Process ffmpeg = new()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = processFileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                },
                EnableRaisingEvents = true
            };
            ffmpeg.OutputDataReceived += outputEventHandler;
            ffmpeg.ErrorDataReceived += errorEventHandler;
            ffmpeg.Start();
            ffmpeg.BeginErrorReadLine();
            ffmpeg.BeginOutputReadLine();
            currentProcess = ffmpeg;
            await ffmpeg.WaitForExitAsync();
            ffmpeg.Dispose();
            currentProcess = null;
        }

        [Flags]
        public enum ThreadAccess : int
        {
            SUSPEND_RESUME = (0x0002)
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
        [DllImport("kernel32.dll")]
        static extern uint SuspendThread(IntPtr hThread);
        [DllImport("kernel32.dll")]
        static extern int ResumeThread(IntPtr hThread);
        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        private static void SuspendProcess(Process process)
        {
            foreach (ProcessThread pT in process.Threads)
            {
                IntPtr pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

                if (pOpenThread == IntPtr.Zero)
                {
                    continue;
                }

                SuspendThread(pOpenThread);

                CloseHandle(pOpenThread);
            }
        }

        public static void ResumeProcess(Process process)
        {
            if (process.ProcessName == string.Empty)
                return;

            foreach (ProcessThread pT in process.Threads)
            {
                IntPtr pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

                if (pOpenThread == IntPtr.Zero)
                {
                    continue;
                }

                int suspendCount;
                do
                {
                    suspendCount = ResumeThread(pOpenThread);
                } while (suspendCount > 0);

                CloseHandle(pOpenThread);
            }
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
