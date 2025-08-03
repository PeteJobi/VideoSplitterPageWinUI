# Video Splitter Page WinUI 3
This is a WinUI 3 page that provides an interface for splitting videos.

<img width="1791" height="1023" alt="Screenshot 2025-07-28 104435" src="https://github.com/user-attachments/assets/c57d4552-6659-46df-ac7d-7b37da702cfc" />

# How to use
This library depends on [DraggerResizerWinUI](https://github.com/PeteJobi/DraggerResizerWinUI) and [VideoSplitterBaseWinUI](https://github.com/PeteJobi/VideoSplitterBaseWinUI). Include all three libraries into your WinUI solution and reference them in your WinUI project. Then navigate to the **VideoSplitterPage** when the user requests for it, passing a **SplitterProps** object as parameter. 
The **SplitterProps** object should contain the path to ffmpeg, the path to the video file, and optionally, the full name of the page type to navigate back to when the user is done. If this last parameter is provided, you can get a list of the folders (containing split video) that was generated on the Video Splitter page. If not, the user will be navigated back to whichever page called the Splitter page and there'll be no parameters.
```
private void GoToSplitter(){
  var ffmpegPath = Path.Join(Package.Current.InstalledLocation.Path, "Assets/ffmpeg.exe");
  var videoPath = Path.Join(Package.Current.InstalledLocation.Path, "Assets/video.mp4");
  Frame.Navigate(typeof(VideoCropper.VideoCropperPage), new SplitterProps { FfmpegPath = ffmpegPath, VideoPath = videoPath, TypeToNavigateTo = typeof(ThisPage).FullName});
}

protected override void OnNavigatedTo(NavigationEventArgs e)
{
    //splitFolders is sent only if TypeToNavigateTo was specified in SplitterProps.
    if (e.Parameter is List<string> splitFolders)
    {
        Console.WriteLine($"{splitFolders.Count} folders were generated");
    }
}
```

You may check out [VideoSplitter](https://github.com/PeteJobi/VideoSplitter) to see how a full application that uses this page.
