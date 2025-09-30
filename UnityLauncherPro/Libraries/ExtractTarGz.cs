// source https://gist.github.com/Su-s/438be493ae692318c73e30367cbc5c2a
// updated source https://gist.github.com/Matheos96/da8990030dfe3e27b0a48722042d9c0b

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
            using (var stream = File.OpenRead(filename))
            {
                ExtractTarGz(stream, outputDir);
            }
        }

        /// <summary>
        /// Extracts a <i>.tar.gz</i> archive stream to the specified directory.
        /// </summary>
        /// <param name="stream">The <i>.tar.gz</i> to decompress and extract.</param>
        /// <param name="outputDir">Output directory to write the files.</param>
        public static void ExtractTarGz(Stream stream, string outputDir)
        {
            int read;
            const int chunk = 4096;
            var buffer = new byte[chunk];

            // A GZipStream is not seekable, so copy it first to a MemoryStream
            using (var gzipStream = new GZipStream(stream, CompressionMode.Decompress))
            {
                using (var memStream = new MemoryStream())
                {
                    //For .NET 6+
                    while ((read = gzipStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        memStream.Write(buffer, 0, read);
                    }
                    memStream.Seek(0, SeekOrigin.Begin);

                    //ExtractTar(gzip, outputDir);
                    ExtractTar(memStream, outputDir);
                }
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
            {
                ExtractTar(stream, outputDir);
            }
        }

        /// <summary>
        /// Extractes a <c>tar</c> archive to the specified directory.
        /// </summary>
        /// <param name="stream">The <i>.tar</i> to extract.</param>
        /// <param name="outputDir">Output directory to write the files.</param>
        public static void ExtractTar(Stream stream, string outputDir)
        {
            var buffer = new byte[100];
            var longFileName = string.Empty;
            while (true)
            {
                stream.Read(buffer, 0, 100);
                string name = string.IsNullOrEmpty(longFileName) ? Encoding.ASCII.GetString(buffer).Trim('\0') : longFileName; //Use longFileName if we have one read

                if (String.IsNullOrWhiteSpace(name)) break;
                stream.Seek(24, SeekOrigin.Current);
                stream.Read(buffer, 0, 12);
                var size = Convert.ToInt64(Encoding.UTF8.GetString(buffer, 0, 12).Trim('\0').Trim(), 8);
                stream.Seek(20, SeekOrigin.Current); //Move head to typeTag byte
                var typeTag = stream.ReadByte();
                stream.Seek(355L, SeekOrigin.Current); //Move head to beginning of data (byte 512)

                if (typeTag == 'L')
                {
                    //If Type Tag is 'L' we have a filename that is longer than the 100 bytes reserved for it in the header.
                    //We read it here and save it temporarily as it will be the file name of the next block where the actual data is
                    var buf = new byte[size];
                    stream.Read(buf, 0, buf.Length);
                    longFileName = Encoding.ASCII.GetString(buf).Trim('\0');
                }
                else
                {
                    longFileName = string.Empty; //Reset longFileName if current entry is not indicating one

                    var output = Path.Combine(outputDir, name);

                    // only include these folders
                    var include = (output.IndexOf("package/ProjectData~/Assets/") > -1);
                    include |= (output.IndexOf("package/ProjectData~/ProjectSettings/") > -1);
                    include |= (output.IndexOf("package/ProjectData~/Packages/") > -1);

                    // rename output path from "package/ProjectData~/Assets/" into "Assets/"
                    output = output.Replace("package/ProjectData~/", "");

                    if (include == true && !Directory.Exists(Path.GetDirectoryName(output))) Directory.CreateDirectory(Path.GetDirectoryName(output));

                    // not folder
                    //if (name.Equals("./", StringComparison.InvariantCulture) == false)
                    if (name.EndsWith("/") == false) //Directories are zero size and don't need anything written
                    {
                        if (include == true)
                        {
                            //Console.WriteLine("output=" + output);
                            using (var str = File.Open(output, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                            {
                                var buf = new byte[size];
                                stream.Read(buf, 0, buf.Length);
                                // take only data from this folder
                                str.Write(buf, 0, buf.Length);
                            }
                        }
                        else
                        {
                            var buf = new byte[size];
                            stream.Read(buf, 0, buf.Length);
                        }
                    }
                }

                //Move head to next 512 byte block 
                var pos = stream.Position;
                var offset = 512 - (pos % 512);
                if (offset == 512) offset = 0;

                stream.Seek(offset, SeekOrigin.Current);
            }
        }
    }  // class Tar
} // namespace TarLib


/*
This software is available under 2 licenses-- choose whichever you prefer.
------------------------------------------------------------------------------
ALTERNATIVE A - MIT License
Copyright (c) 2017 Sean Barrett
Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
------------------------------------------------------------------------------
ALTERNATIVE B - Public Domain (www.unlicense.org)
This is free and unencumbered software released into the public domain.
Anyone is free to copy, modify, publish, use, compile, sell, or distribute this
software, either in source code form or as a compiled binary, for any purpose,
commercial or non-commercial, and by any means.
In jurisdictions that recognize copyright laws, the author or authors of this
software dedicate any and all copyright interest in the software to the public
domain.We make this dedication for the benefit of the public at large and to
the detriment of our heirs and successors. We intend this dedication to be an
overt act of relinquishment in perpetuity of all present and future rights to
this software under copyright law.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
