using System;
using System.IO;
using System.Text;
using System.Linq;
using TagLib;

namespace VerificationMetadata
{
    class Program
    {
        static void Main(string[] args)
        {
            string path = @"h:\claude\irissort\fantasy-warrior-monsters.jpg";
            InspectFile(path);
        }

        static void InspectFile(string path)
        {
            Console.WriteLine($"Inspecting: {Path.GetFileName(path)}");
            if (!File.Exists(path)) return;

            // 1. Check TIFF Endianness (Exif)
            try
            {
                using var fs = File.OpenRead(path);
                // Scan for Exif header: 0xFF 0xE1 (APP1)
                byte[] buffer = new byte[128];
                fs.Read(buffer, 0, 128);
                
                // Simple heuristic scan
                int exifIdx = -1;
                for(int i=0; i<buffer.Length-6; i++)
                {
                    // Exif\0\0 header
                    if(buffer[i] == 0x45 && buffer[i+1] == 0x78 && buffer[i+2] == 0x69 && 
                       buffer[i+3] == 0x66 && buffer[i+4] == 0x00 && buffer[i+5] == 0x00)
                    {
                        exifIdx = i + 6;
                        break;
                    }
                }

                if(exifIdx != -1)
                {
                    // Next 2 bytes are TIFF Byte Order
                    string order = $"{(char)buffer[exifIdx]}{(char)buffer[exifIdx+1]}";
                    Console.WriteLine($"TIFF Byte Order: {order}"); // II = Little, MM = Big
                    if (order == "MM") Console.WriteLine(">> WARNING: File is Big Endian!");
                }
                else
                {
                    Console.WriteLine("Could not locate simple Exif header in first 128 bytes.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Header Read Error: {ex.Message}");
            }

            // 2. TagLib Inspection
            try
            {
                using var file = TagLib.File.Create(path);
                Console.WriteLine($"Comment: {file.Tag.Comment}");
                
                // Check Raw Tags if possible (TagLib abstracts this, but we can check specific directories)
                if (file.GetTag(TagTypes.Tiff) is TagLib.Image.ImageTag tiffTag)
                {
                     // Try to access XPComment (0x9C9C)
                     // TagLib# usually exposes this via specific properties or raw tags... 
                     // Actually TagLib# maps XPComment to Comment for display, but we want raw.
                     Console.WriteLine("Inspecting directories...");
                     // We can't easy get Raw tags via standard TagLib API without casting to internal types or ImageTag internals
                }
                
                // Let's print the hex dump of the comment if we can access it
                var comment = file.Tag.Comment;
                if(!string.IsNullOrEmpty(comment))
                {
                    Console.WriteLine($"Comment Chars: {string.Join(" ", comment.Take(10).Select(c => ((int)c).ToString("X4")))}");
                    if (comment.Length > 0 && comment[0] == 0x4120) Console.WriteLine(">> CONFIRMED: TagLib sees the Mojibake characters!");
                }
            }
            catch (Exception ex)
            {
                 Console.WriteLine($"TagLib Error: {ex.Message}");
            }
        }
    }
}
