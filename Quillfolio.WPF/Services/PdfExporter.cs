using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Quillfolio.WPF.Services;

public static class PdfExporter
{
    public static void ExportPagesToPdf(string filePath, IReadOnlyList<(byte[] JpegBytes, int PixelWidth, int PixelHeight)> pages)
    {
        if (pages.Count == 0) return;

        using var ms = new MemoryStream();
        var offsets = new List<long> { 0 }; // object 0 is always free

        void WriteAscii(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            ms.Write(bytes, 0, bytes.Length);
        }

        int StartObj()
        {
            offsets.Add(ms.Position);
            int id = offsets.Count - 1;
            WriteAscii($"{id} 0 obj\n");
            return id;
        }

        void EndObj() => WriteAscii("endobj\n");

        WriteAscii("%PDF-1.4\n");
        ms.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 6);

        // 1: Catalog, 2: Pages
        StartObj();
        WriteAscii("<< /Type /Catalog /Pages 2 0 R >>\n");
        EndObj();

        // Reserve object 2 (Pages) — we'll build its Kids list first, so let's write page objects starting at 3
        // Actually let's compute object IDs deterministically:
        // For each page i (0..N-1):
        //   pageObjId = 3 + i * 3
        //   contentObjId = 4 + i * 3
        //   imageObjId = 5 + i * 3
        var kidsBuilder = new StringBuilder();
        for (int i = 0; i < pages.Count; i++)
        {
            kidsBuilder.Append($"{3 + i * 3} 0 R ");
        }

        StartObj(); // obj 2
        WriteAscii($"<< /Type /Pages /Kids [ {kidsBuilder}] /Count {pages.Count} >>\n");
        EndObj();

        for (int i = 0; i < pages.Count; i++)
        {
            var (jpeg, pxW, pxH) = pages[i];
            // Convert 96 DPI pixels to PDF points (72 DPI)
            double ptW = pxW * 72.0 / 96.0;
            double ptH = pxH * 72.0 / 96.0;
            string wStr = ptW.ToString("0.##", CultureInfo.InvariantCulture);
            string hStr = ptH.ToString("0.##", CultureInfo.InvariantCulture);

            int pageObj = 3 + i * 3;
            int contentObj = pageObj + 1;
            int imgObj = pageObj + 2;

            // Page object
            StartObj();
            WriteAscii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {wStr} {hStr}] " +
                       $"/Resources << /XObject << /Im0 {imgObj} 0 R >> >> /Contents {contentObj} 0 R >>\n");
            EndObj();

            // Content stream
            string contentStream = $"q\n{wStr} 0 0 {hStr} 0 0 cm\n/Im0 Do\nQ\n";
            byte[] contentBytes = Encoding.ASCII.GetBytes(contentStream);
            StartObj();
            WriteAscii($"<< /Length {contentBytes.Length} >>\nstream\n");
            ms.Write(contentBytes, 0, contentBytes.Length);
            WriteAscii("\nendstream\n");
            EndObj();

            // Image XObject
            StartObj();
            WriteAscii($"<< /Type /XObject /Subtype /Image /Width {pxW} /Height {pxH} " +
                       $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
            ms.Write(jpeg, 0, jpeg.Length);
            WriteAscii("\nendstream\n");
            EndObj();
        }

        long xrefPos = ms.Position;
        WriteAscii($"xref\n0 {offsets.Count}\n");
        WriteAscii("0000000000 65535 f \n");
        for (int i = 1; i < offsets.Count; i++)
        {
            WriteAscii($"{offsets[i]:D10} 00000 n \n");
        }

        WriteAscii($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF\n");

        File.WriteAllBytes(filePath, ms.ToArray());
    }
}
