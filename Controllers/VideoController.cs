using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging; // Include logging
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VideoTranscodingApp.Services;
using Microsoft.AspNetCore.Http;

namespace VideoStreamingBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VideoController : ControllerBase
    {
        private readonly string _videoPath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedVideos"); // Full path
        private readonly VideoTranscodingService _videoTranscodingService;
        private readonly ILogger<VideoController> _logger; // Logger

        public VideoController(VideoTranscodingService videoTranscodingService, ILogger<VideoController> logger)
        {
            _videoTranscodingService = videoTranscodingService;
            _logger = logger;

            // Create directory for videos if it doesn't exist
            if (!Directory.Exists(_videoPath))
            {
                Directory.CreateDirectory(_videoPath);
                _logger.LogInformation("Created video upload directory at: {VideoPath}", _videoPath);
            }
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadVideo(IFormFile videoFile)
        {
            _logger.LogInformation("UploadVideo called.");

            // Validate that a file has been provided
            if (videoFile == null || videoFile.Length == 0)
            {
                _logger.LogWarning("Upload failed: Video file is missing.");
                return BadRequest("Video file is missing.");
            }

            // Validate file type
            var allowedExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv" };
            var extension = Path.GetExtension(videoFile.FileName);
            if (!allowedExtensions.Contains(extension.ToLower()))
            {
                _logger.LogWarning("Upload failed: Invalid file type. Allowed types: {AllowedTypes}", string.Join(", ", allowedExtensions));
                return BadRequest("Invalid file type. Allowed types: " + string.Join(", ", allowedExtensions));
            }

            // Check file size (e.g., limit to 10 GB)
            const long maxFileSize = 10L * 1024 * 1024 * 1024; // 10 GB in bytes
            if (videoFile.Length > maxFileSize)
            {
                _logger.LogWarning("Upload failed: File size exceeds the limit of 10 GB. Actual size: {FileSize} bytes", videoFile.Length);
                return BadRequest("File size exceeds the limit of 10 GB.");
            }

            // Set file path and ensure the file does not already exist
            var fileName = Path.GetFileName(videoFile.FileName);
            var filePath = Path.Combine(_videoPath, fileName);

            // Prevent overwriting by adding a timestamp or unique identifier
            if (System.IO.File.Exists(filePath))
            {
                var uniqueFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMddHHmmss}{extension}";
                filePath = Path.Combine(_videoPath, uniqueFileName);
                _logger.LogInformation("File already exists. New unique file name: {UniqueFileName}", uniqueFileName);
            }

            try
            {
                // Save the file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    _logger.LogInformation("Saving video file to {FilePath}", filePath);
                    await videoFile.CopyToAsync(stream);
                }

                // Check if transcoding is needed
                bool transcodingNeeded = IsTranscodingNeeded(GetVideoMetadata(filePath));
                if (transcodingNeeded)
                {
                    _logger.LogInformation("Video transcoding needed for file: {FilePath}", filePath);
                    // Call method to queue the video for transcoding
                    _videoTranscodingService.QueueVideo(filePath);
                    return Ok(new 
                    {
                        OriginalFilePath = filePath,
                        Message = "Video queued for transcoding."
                    });
                }

                _logger.LogInformation("Upload successful for file: {FilePath}", filePath);
                return Ok(new 
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    FileSize = videoFile.Length
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while uploading the file: {FileName}", videoFile.FileName);
                return StatusCode(500, "Internal server error. Please try again later.");
            }
        }

        private string GetVideoMetadata(string filePath)
        {
            _logger.LogInformation("Getting video metadata for file: {FilePath}", filePath);
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_format -show_streams \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    _logger.LogError("Failed to start FFprobe process for file: {FilePath}", filePath);
                    return string.Empty;
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                // Log output and error messages
                _logger.LogInformation("FFprobe output: {FFprobeOutput}", output);
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogWarning("FFprobe error: {FFprobeError}", error);
                }

                return output;
            }
        }

        private bool IsTranscodingNeeded(string ffprobeOutput)
        {
            _logger.LogInformation("Checking if transcoding is needed.");
            var videoCodecMatch = Regex.Match(ffprobeOutput, @"codec_name=(\w+)", RegexOptions.Multiline);
            var audioCodecMatch = Regex.Match(ffprobeOutput, @"codec_name=(\w+)", RegexOptions.Multiline);

            string acceptedAudioCodec = "aac";

            if (videoCodecMatch.Success)
            {
                string videoCodec = videoCodecMatch.Groups[1].Value;
                _logger.LogInformation("Video Codec: {VideoCodec}", videoCodec);
            }
            else
            {
                _logger.LogWarning("No video codec found.");
            }

            if (audioCodecMatch.Success)
            {
                string audioCodec = audioCodecMatch.Groups[1].Value;
                _logger.LogInformation("Audio Codec: {AudioCodec}", audioCodec);
                if (audioCodec != acceptedAudioCodec)
                {
                    _logger.LogWarning("Transcoding needed: {AudioCodec} != {AcceptedAudioCodec}", audioCodec, acceptedAudioCodec);
                    return true; // Transcoding needed
                }
            }
            else
            {
                _logger.LogWarning("No audio codec found.");
            }

            return false; // No transcoding needed
        }

[HttpGet("stream/{fileName}")]
public IActionResult StreamManifest(string fileName)
{
    var manifestDirectory = Path.Combine(_videoPath, "HLSOutput");
    var manifestPath = Path.Combine(manifestDirectory, fileName);

    // Kontrollera om manifestfilen finns
    if (!System.IO.File.Exists(manifestPath))
    {
        _logger.LogWarning("Manifest file not found: {ManifestPath}", manifestPath);
        return NotFound();
    }

    try
    {
        // Öppna filström för manifestet
        var fileStream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read);
        
        // Returnera manifestet med korrekt MIME-typ
        return File(fileStream, "application/vnd.apple.mpegurl", enableRangeProcessing: true);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error while streaming manifest: {ManifestPath}", manifestPath);
        return StatusCode(500, "Internal server error while processing the request.");
    }
}




    }
}
