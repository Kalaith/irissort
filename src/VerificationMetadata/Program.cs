using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using IrisSort.Services;
using IrisSort.Core.Models;
using Serilog;
using TagLib;

namespace VerificationMetadata
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
            
            // Files
            string brokenSource = @"h:\claude\irissort\annular_eclipse_view.jpg";
            string testOutput = @"h:\claude\irissort\test_service_write.jpg";
            string reencodedOutput = @"h:\claude\irissort\test_service_reencoded.jpg";

            var result = new ImageAnalysisResult
            {
                Title = "Service Test Title",
                Description = "Service Test Description",
                Tags = new List<string> { "ServiceTag1", "ServiceTag2" },
                Authors = "Service Author",
                Copyright = "Service Copyright",
                Comments = "Service Comment"
            };

            var service = new MetadataWriterService(Log.Logger);

            Console.WriteLine("=== SERVICE TEST PHASE ===");
            if (System.IO.File.Exists(brokenSource))
            {
                 // Test 1: Direct Write using Service (Should now work with the XMP fix)
                 System.IO.File.Copy(brokenSource, testOutput, true);
                 Console.WriteLine($"Attempting Service Write to: {Path.GetFileName(testOutput)}");
                 
                 // Use the Service
                 bool success = await service.WriteMetadataAsync(result, testOutput);
                 Console.WriteLine($"Service Write Result: {success}");
                 
                 InspectJpeg("DIRECT_SERVICE", testOutput);
                 
                 // Optional: Test Re-encode too just to be sure it doesn't break
                 ReEncode(brokenSource, reencodedOutput);
                 bool reSuccess = await service.WriteMetadataAsync(result, reencodedOutput);
                 if (reSuccess) InspectJpeg("REENCODED_SERVICE", reencodedOutput);
            }
        }

        static void ReEncode(string source, string dest)
        {
            Console.WriteLine($"\n[Re-Encode] Processing {Path.GetFileName(source)}...");
            try 
            {
                using var inputStream = System.IO.File.OpenRead(source);
                using var originalBitmap = SkiaSharp.SKBitmap.Decode(inputStream);
                using var image = SkiaSharp.SKImage.FromBitmap(originalBitmap);
                using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 100);
                
                using var outputStream = System.IO.File.OpenWrite(dest);
                data.SaveTo(outputStream);
                Console.WriteLine("Re-encode complete.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Re-encode failed: {ex.Message}");
            }
        }

        static void InspectJpeg(string label, string path)
        {
            Console.WriteLine($"[{label}] Inspecting: {Path.GetFileName(path)}");
            if (!System.IO.File.Exists(path)) return;

            try
            {
                using var file = TagLib.File.Create(path);
                Console.WriteLine($"Title: {file.Tag.Title}");
                Console.WriteLine($"Comment: {file.Tag.Comment}");
                Console.WriteLine($"Genres: {string.Join(", ", file.Tag.Genres)}");
                
                // Check if XMP is actually present in tag types
                Console.WriteLine($"TagTypes: {file.TagTypes}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Inspect Error: {ex.Message}");
            }
        }
    }
}
