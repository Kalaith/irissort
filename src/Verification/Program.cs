using System;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;
using IrisSort.Services;

// Minimal mock/stub for logging
public class ConsoleLogger : Serilog.ILogger
{
    public void Write(Serilog.Events.LogEvent logEvent) => Console.WriteLine(logEvent.RenderMessage());
}

// Verification script
public class Program
{
    public static async Task Main()
    {
        var logLines = new System.Collections.Generic.List<string>();
        void Log(string msg) {
            Console.WriteLine(msg);
            logLines.Add(msg);
        }

        Log("Starting Verification...");
        
        var resizer = new ImageResizerService();
        var tempFile = Path.GetTempFileName() + ".png";
        
        try
        {
            // 1. Create a large dummy image with specific aspect ratio (3000x2000 => 1.5)
            Log("Creating dummy image (3000x2000)...");
            using (var surface = SKSurface.Create(new SKImageInfo(3000, 2000)))
            {
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Red);
                using (var image = surface.Snapshot())
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                using (var stream = File.OpenWrite(tempFile))
                {
                    data.SaveTo(stream);
                }
            }
            
            Log($"Created image at {tempFile}. Size: {new FileInfo(tempFile).Length / 1024} KB");

            // 2. Test Resize
            int maxDim = 1000;
            Log($"Resizing to max dimension {maxDim}...");
            
            var resizedPath = await resizer.CreateResizedCopyAsync(tempFile, maxDim);
            
            // 3. Verify
            Log($"Resized image created at {resizedPath}");
            
            using (var stream = File.OpenRead(resizedPath))
            using (var codec = SKCodec.Create(stream))
            {
                Log($"Resized dimensions: {codec.Info.Width}x{codec.Info.Height}");
                
                double originalRatio = 3000.0 / 2000.0;
                double newRatio = (double)codec.Info.Width / codec.Info.Height;
                
                Log($"Original Ratio: {originalRatio:F4}");
                Log($"New Ratio: {newRatio:F4}");
                
                if (Math.Abs(originalRatio - newRatio) > 0.01)
                {
                     Log("FAILED: Aspect ratio not preserved!");
                }
                else
                {
                     Log("SUCCESS: Aspect ratio preserved.");
                }

                if (codec.Info.Width > maxDim || codec.Info.Height > maxDim)
                {
                   Log("FAILED: Image is still too large!");
                }
                else if (codec.Info.Width != 1000)
                {
                     Log($"WARNING: Expected width 1000, got {codec.Info.Width}");
                }
                else
                {
                     Log("SUCCESS: Dimensions are correct.");
                }
            }
            
            // Cleanup resized
            resizer.DeleteTemporaryFile(resizedPath);
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex}");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            resizer.Dispose();
            await File.WriteAllLinesAsync("results.txt", logLines);
        }
    }
}
