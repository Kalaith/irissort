using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IrisSort.Services;
using IrisSort.Core.Models;
using Serilog;

namespace VerificationMetadata
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Setup logger
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .MinimumLevel.Debug()
                .CreateLogger();

            var logger = Log.Logger;
            var writer = new PngWebpMetadataWriter(logger);

            // Use image from user
            string originalPath = @"h:\claude\irissort\effbzwxnqc7g1.png";
            string testPath = @"h:\claude\irissort\effbzwxnqc7g1_metadata_test.png";

            Console.WriteLine($"Original Exists: {File.Exists(originalPath)}");
            if (!File.Exists(originalPath))
            {
                logger.Error("Original file not found.");
                return;
            }

            // Copy to test path
            File.Copy(originalPath, testPath, overwrite: true);
            Console.WriteLine($"Copied to {testPath}");

            var result = new ImageAnalysisResult
            {
                OriginalPath = testPath,
                Title = "Test Title",
                Description = "Test Description",
                Tags = new List<string> { "tag1", "tag2" },
                Authors = "Test Author",
                Copyright = "Test Copyright",
                Comments = "Test Comments"
            };

            // Hack: manually populate FinalTags since getter normally does it
            // Actually ImageAnalysisResult Logic:
            // FinalTags => Tags (if not empty)
            
            try
            {
                Console.WriteLine($"Writing metadata to {testPath}...");
                bool success = await writer.WriteMetadataAsync(result, testPath);
                
                if (success)
                {
                    Console.WriteLine("Write returned true. Verifying readback...");
                    
                    try 
                    {
                        using var file = TagLib.File.Create(testPath);
                        Console.WriteLine($"Title: {file.Tag.Title}");
                        Console.WriteLine($"Comment: {file.Tag.Comment}");
                        Console.WriteLine($"Keywords: {string.Join(", ", file.Tag.Genres ?? Array.Empty<string>())}");
                        
                        if (file.Tag is TagLib.Image.CombinedImageTag imageTag)
                        {
                             Console.WriteLine($"ImageTag Keywords: {string.Join(", ", imageTag.Keywords ?? Array.Empty<string>())}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Readback failed: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("Write FAILED (returned false).");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EXCEPTION: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
