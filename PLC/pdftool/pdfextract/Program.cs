using System;
using System.IO;
using System.Text;
using UglyToad.PdfPig;

class Program
{
    static void Main(string[] args)
    {
        string pdfPath = @"D:\前后端分离\PLC\Omron—Fins通讯手册.pdf";
        string outPath = @"D:\前后端分离\PLC\Omron_Fins_text.txt";

        if (!File.Exists(pdfPath))
        {
            Console.WriteLine("PDF not found: " + pdfPath);
            return;
        }

        var sb = new StringBuilder();
        int totalPages = 0;
        using (var pdf = PdfDocument.Open(pdfPath))
        {
            totalPages = pdf.NumberOfPages;
            sb.AppendLine($"Total pages: {totalPages}");
            sb.AppendLine("========================================");

            for (int i = 1; i <= totalPages; i++)
            {
                var page = pdf.GetPage(i);
                sb.AppendLine($"\n--- Page {i} ---");
                sb.AppendLine(page.Text);
            }
        }

        File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine("Done! Extracted " + totalPages + " pages to: " + outPath);
    }
}
