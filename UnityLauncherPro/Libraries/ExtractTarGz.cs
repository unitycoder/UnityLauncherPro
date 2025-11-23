using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TarLib
{
    public class Tar
    {
        /// <summary>
        /// Extracts a .tar.gz archive to the specified directory.
        /// </summary>
        public static void ExtractTarGz(string filename, string outputDir)
        {
            using (var stream = File.OpenRead(filename))
            {
                ExtractTarGz(stream, outputDir);
            }
        }

        /// <summary>
        /// Extracts a .tar.gz archive stream to the specified directory.
        /// </summary>
        public static void ExtractTarGz(Stream stream, string outputDir)
        {
            const int chunk = 4096*4;
            var buffer = new byte[chunk];

            using (var gzipStream = new GZipStream(stream, CompressionMode.Decompress))
            using (var memStream = new MemoryStream())
            {
                int read;
                while ((read = gzipStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    memStream.Write(buffer, 0, read);
                }
                memStream.Seek(0, SeekOrigin.Begin);
                ExtractTar(memStream, outputDir);
            }
        }

        /// <summary>
        /// Extracts a tar archive file.
        /// </summary>
        public static void ExtractTar(string filename, string outputDir)
        {
            using (var stream = File.OpenRead(filename))
            {
                ExtractTar(stream, outputDir);
            }
        }

        /// <summary>
        /// Extracts a tar archive stream.
        /// Fixes path loss caused by ignoring the POSIX 'prefix' field and wrong header offsets.
        /// </summary>
        public static void ExtractTar(Stream stream, string outputDir)
        {
            // Tar header constants
            const int HeaderSize = 512;
            byte[] header = new byte[HeaderSize];

            string pendingLongName = null; // For GNU long name ('L') entries

            while (true)
            {
                int bytesRead = ReadExact(stream, header, 0, HeaderSize);
                if (bytesRead == 0) break; // End of stream
                if (bytesRead < HeaderSize) throw new EndOfStreamException("Unexpected end of tar stream.");

                // Detect two consecutive zero blocks (end of archive)
                bool allZero = IsAllZero(header);
                if (allZero)
                {
                    // Peek next block; if also zero -> end
                    bytesRead = ReadExact(stream, header, 0, HeaderSize);
                    if (bytesRead == 0 || IsAllZero(header)) break;
                    if (bytesRead < HeaderSize) throw new EndOfStreamException("Unexpected end of tar stream.");
                }

                // Parse fields (POSIX ustar)
                string name = GetString(header, 0, 100);
                string mode = GetString(header, 100, 8);
                string uid = GetString(header, 108, 8);
                string gid = GetString(header, 116, 8);
                string sizeOctal = GetString(header, 124, 12);
                string mtime = GetString(header, 136, 12);
                string checksum = GetString(header, 148, 8);
                char typeFlag = (char)header[156];
                string linkName = GetString(header, 157, 100);
                string magic = GetString(header, 257, 6); // "ustar\0" or "ustar "
                string version = GetString(header, 263, 2);
                string uname = GetString(header, 265, 32);
                string gname = GetString(header, 297, 32);
                string prefix = GetString(header, 345, 155);

                // Compose full name using prefix (if present and not using GNU long name override)
                if (!string.IsNullOrEmpty(prefix))
                {
                    name = prefix + "/" + name;
                }

                // If we previously read a GNU long name block, override current name
                if (!string.IsNullOrEmpty(pendingLongName))
                {
                    name = pendingLongName;
                    pendingLongName = null;
                }

                long size = ParseOctal(sizeOctal);

                // Handle GNU long name extension block: the data of this entry is the filename of next entry.
                if (typeFlag == 'L')
                {
                    byte[] longNameData = new byte[size];
                    ReadExact(stream, longNameData, 0, (int)size);
                    pendingLongName = Encoding.ASCII.GetString(longNameData).Trim('\0', '\r', '\n');
                    SkipPadding(stream, size);
                    continue; // Move to next header
                }

                // Skip PAX extended header (type 'x') - metadata only
                if (typeFlag == 'x')
                {
                    SkipData(stream, size);
                    SkipPadding(stream, size);
                    continue;
                }

                // Normalize name
                if (string.IsNullOrWhiteSpace(name)) continue;

                // Directory?
                bool isDirectory = typeFlag == '5' || name.EndsWith("/");

                // Inclusion filter (original logic)
                string originalName = name;
                bool include =
                    originalName.IndexOf("package/ProjectData~/Assets/", StringComparison.Ordinal) > -1 ||
                    originalName.IndexOf("package/ProjectData~/ProjectSettings/", StringComparison.Ordinal) > -1 ||
                    originalName.IndexOf("package/ProjectData~/Library/", StringComparison.Ordinal) > -1 ||
                    originalName.IndexOf("package/ProjectData~/Packages/", StringComparison.Ordinal) > -1;

                // Strip leading prefix.
                string cleanedName = originalName.StartsWith("package/ProjectData~/", StringComparison.Ordinal)
                    ? originalName.Substring("package/ProjectData~/".Length)
                    : originalName;

                string finalPath = Path.Combine(outputDir, cleanedName.Replace('/', Path.DirectorySeparatorChar));

                if (isDirectory)
                {
                    if (include && !Directory.Exists(finalPath))
                        Directory.CreateDirectory(finalPath);
                    // No data to read for directory; continue to next header
                    SkipData(stream, size); // size should be 0
                    SkipPadding(stream, size);
                    continue;
                }

                // Ensure directory exists
                if (include)
                {
                    string dir = Path.GetDirectoryName(finalPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                }

                // Read file data (always advance stream even if not included)
                byte[] fileData = new byte[size];
                ReadExact(stream, fileData, 0, (int)size);

                if (include)
                {
                    using (var fs = File.Open(finalPath, FileMode.Create, FileAccess.Write))
                    {
                        fs.Write(fileData, 0, fileData.Length);
                    }
                }

                // Skip padding to 512 boundary
                SkipPadding(stream, size);
            }
        }

        private static string GetString(byte[] buffer, int offset, int length)
        {
            var s = Encoding.ASCII.GetString(buffer, offset, length);
            int nullIndex = s.IndexOf('\0');
            if (nullIndex >= 0) s = s.Substring(0, nullIndex);
            return s.Trim();
        }

        private static long ParseOctal(string s)
        {
            s = s.Trim();
            if (string.IsNullOrEmpty(s)) return 0;
            try
            {
                return Convert.ToInt64(s, 8);
            }
            catch
            {
                // Fallback: treat as decimal if malformed
                long val;
                return long.TryParse(s, out val) ? val : 0;
            }
        }

        private static bool IsAllZero(byte[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i] != 0) return false;
            return true;
        }

        private static int ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, offset + total, count - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        private static void SkipData(Stream stream, long size)
        {
            if (size <= 0) return;
            const int chunk = 8192;
            byte[] tmp = new byte[Math.Min(chunk, (int)size)];
            long remaining = size;
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(tmp.Length, remaining);
                int read = stream.Read(tmp, 0, toRead);
                if (read <= 0) throw new EndOfStreamException("Unexpected end while skipping data.");
                remaining -= read;
            }
        }

        private static void SkipPadding(Stream stream, long size)
        {
            long padding = (512 - (size % 512)) % 512;
            if (padding > 0)
            {
                stream.Seek(padding, SeekOrigin.Current);
            }
        }
    }
}

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