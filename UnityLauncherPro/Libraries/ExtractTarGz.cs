// source https://gist.github.com/Su-s/438be493ae692318c73e30367cbc5c2a

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TarLib
{
    public class Tar
    {
        /// <summary>
        /// Extracts a <i>.tar.gz</i> archive to the specified directory.
        /// </summary>
        /// <param name="filename">The <i>.tar.gz</i> to decompress and extract.</param>
        /// <param name="outputDir">Output directory to write the files.</param>
        public static void ExtractTarGz(string filename, string outputDir)
        {
            using (var stream = File.OpenRead(filename)) ExtractTarGz(stream, outputDir);
        }

        /// <summary>
        /// Extracts a <i>.tar.gz</i> archive stream to the specified directory.
        /// </summary>
        /// <param name="stream">The <i>.tar.gz</i> to decompress and extract.</param>
        /// <param name="outputDir">Output directory to write the files.</param>
        public static void ExtractTarGz(Stream stream, string outputDir)
        {
            using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
            {
                ExtractTar(gzip, outputDir);
            }
        }

        /// <summary>
        /// Extractes a <c>tar</c> archive to the specified directory.
        /// </summary>
        /// <param name="filename">The <i>.tar</i> to extract.</param>
        /// <param name="outputDir">Output directory to write the files.</param>
        public static void ExtractTar(string filename, string outputDir)
        {
            using (var stream = File.OpenRead(filename))
                ExtractTar(stream, outputDir);
        }

        /// <summary>
        /// Extractes a <c>tar</c> archive to the specified directory.
        /// </summary>
        /// <param name="stream">The <i>.tar</i> to extract.</param>
        /// <param name="outputDir">Output directory to write the files.</param>
        public static void ExtractTar(Stream stream, string outputDir)
        {
            var buffer = new byte[100];
            // store current position here
            long pos = 0;
            while (true)
            {
                pos += stream.Read(buffer, 0, 100);
                var name = Encoding.ASCII.GetString(buffer).Trim('\0');
                if (String.IsNullOrWhiteSpace(name)) break;
                FakeSeekForward(stream, 24);
                pos += 24;

                pos += stream.Read(buffer, 0, 12);
                var size = Convert.ToInt64(Encoding.UTF8.GetString(buffer, 0, 12).Trim('\0').Trim(), 8);
                FakeSeekForward(stream, 376);
                pos += 376;

                var output = Path.Combine(outputDir, name);

                // only include these folders
                var include = (output.IndexOf("package/ProjectData~/Assets/") > -1);
                include |= (output.IndexOf("package/ProjectData~/ProjectSettings/") > -1);
                include |= (output.IndexOf("package/ProjectData~/Packages/") > -1);

                // rename output path from "package/ProjectData~/Assets/" into "Assets/"
                output = output.Replace("package/ProjectData~/", "");

                if (include == true && !Directory.Exists(Path.GetDirectoryName(output))) Directory.CreateDirectory(Path.GetDirectoryName(output));

                if (!name.Equals("./", StringComparison.InvariantCulture))
                {
                    if (include == true)
                    {
                        //Console.WriteLine("output=" + output);
                        using (var str = File.Open(output, FileMode.OpenOrCreate, FileAccess.Write))
                        {
                            var buf = new byte[size];
                            pos += stream.Read(buf, 0, buf.Length);
                            // take only data from this folder
                            str.Write(buf, 0, buf.Length);
                        }
                    }
                    else
                    {
                        //pos += size;
                        var buf = new byte[size];
                        pos += stream.Read(buf, 0, buf.Length);
                        //FakeSeekForward(stream, (int)size);
                    }
                }

                var offset = (int)(512 - (pos % 512));
                if (offset == 512) offset = 0;
                FakeSeekForward(stream, offset);
                pos += offset;
            }
        }

        private static void FakeSeekForward(Stream stream, int offset)
        {
            if (stream.CanSeek)
                stream.Seek(offset, SeekOrigin.Current);
            else
            {
                int bytesRead = 0;
                var buffer = new byte[offset];
                while (bytesRead < offset)
                {
                    int read = stream.Read(buffer, bytesRead, offset - bytesRead);
                    if (read == 0)
                        throw new EndOfStreamException();
                    bytesRead += read;
                }
            }
        }
    }
}