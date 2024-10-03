using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VideoTranscodingApp.Services
{
    public class VideoTranscodingService
    {
        private readonly ILogger<VideoTranscodingService> _logger;

        public VideoTranscodingService(ILogger<VideoTranscodingService> logger)
        {
            _logger = logger;
        }

        public void QueueVideo(string filePath)
        {
            try
            {
                // Start transcoding in a background task
                Task.Run(() => TranscodeVideo(filePath));
            }
            catch (Exception ex)
            {
                _logger.LogError("Error queuing video: {Message}", ex.Message);
            }
        }

private void TranscodeVideo(string filePath)
{
    string outputDir = Path.Combine(Path.GetDirectoryName(filePath), "HLSOutput");
    Directory.CreateDirectory(outputDir); // Skapa mappen om den inte finns

    string outputFilePath = Path.Combine(outputDir, "output.m3u8"); // Spara .m3u8-filen här

    var startInfo = new ProcessStartInfo
    {
        FileName = "ffmpeg", // Se till att ffmpeg är installerat
        Arguments = $"-i \"{filePath}\" -codec: copy -start_number 0 -hls_time 10 -hls_list_size 0 -f hls \"{outputFilePath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    try
    {
        using (var process = Process.Start(startInfo))
        {
            if (process == null)
            {
                _logger.LogError("FFmpeg process failed to start.");
                return;
            }

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            _logger.LogInformation("Transcoding output: {Output}", output);
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogError("Transcoding error: {Error}", error);
            }
        }

        _logger.LogInformation("Transcoding complete: {OutputFilePath}", outputFilePath);
    }
    catch (Exception ex)
    {
        _logger.LogError("Error during transcoding: {Message}", ex.Message);
    }
}

    }
}
