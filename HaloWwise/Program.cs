using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HaloWwise
{
    class Program
    {
        static void Main(string[] args)
        {
            // Make Debug Write and Console Write the same thing with Trace
            var listeners = new TraceListener[] { new TextWriterTraceListener(Console.Out) };
            Debug.Listeners.AddRange(listeners);

            if (args.Length < 2)
            {
                Trace.WriteLine("Un-packs *.wem and *.bnk files from a Halo 4/5 Wwise *.pck file.");
                Trace.WriteLine("Usage: ");
                Trace.WriteLine("\tHaloWwise.exe <input.pck> <extraction path>");
                Trace.WriteLine("\tHaloWwise.exe <input directory (recursive)> <extraction path>");

                Trace.WriteLine("\n\nNote: If you place ww2ogg.exe and revorb.exe (with the codebook) in the same Path as this Exe, it will attempt to convert *.wem files to *.ogg during extraction (H5 only)\n");
                return;
            }



            var input = args[0];
            var output = args[1];

            // If output isn't Directory, or not there, bail
            try
            {
                if (!File.GetAttributes(output).HasFlag(FileAttributes.Directory))
                {
                    Trace.WriteLine("Error: Output is not a Directory");
                    return;
                }
            } catch
            {
                Trace.WriteLine("Error: Output directory not found");
                return;
            }


            try
            {
                // Chage what we do, depending on if input is a file or a folder
                if (File.GetAttributes(input).HasFlag(FileAttributes.Directory))
                {
                    // Find all Packs, extract them to output
                    var packs = Directory.GetFiles(input, "*.pck", SearchOption.AllDirectories);
                    foreach (string pack in packs)
                    {
                        var packManager = new PackManager(pack, output);
                        packManager.ExtractPack();
                    }
                }
                else
                {
                    // Extract just the one Pack
                    var packManager = new PackManager(input, output);
                    packManager.ExtractPack();
                }
            }
            catch
            {
                Trace.WriteLine("Error: Unable to process Input");
                return;
            }
        }
    }
}
