using System.IO.Compression;
using System.Text;
using Xunit;

namespace Factix.Core.Tests;

public class FactixTests
{
    [Fact]
    public void Emf_lee_texto_y_posicion_en_puntos()
    {
        var document = TextNormalizer.ReadBytes(EmfBuilder.FacturaPage());

        var run = Assert.Single(document.Runs);
        Assert.Equal("FACTURA", run.Text);
        Assert.Equal("Arial", run.Font);
        Assert.InRange(run.X, 71.5, 72.5);
        Assert.InRange(run.Y, 71.5, 72.5);
        Assert.InRange(document.Pages[0].Width, 611, 614);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Spool_emf_desempaqueta_la_pagina(bool sizeIncludesHeader)
    {
        var spool = EmfBuilder.WrapSpool(EmfBuilder.FacturaPage(), sizeIncludesHeader);
        var document = TextNormalizer.ReadBytes(spool);

        Assert.Equal("FACTURA", Assert.Single(document.Runs).Text);
    }

    [Fact]
    public void Xps_de_ejemplo_extrae_la_factura()
    {
        var document = TextNormalizer.ReadBytes(SampleInvoice.CreateXps());
        var result = InvoiceExtractor.Extract(document);
        var draft = result.Draft;

        Assert.True(result.Recognized);
        Assert.Equal("Factura simple", result.TemplateName);
        Assert.Equal("A-100", draft.Folio);
        Assert.Equal("2026-03-15", draft.Fecha);
        Assert.Equal("Taller Norte SA", draft.Emisor);
        Assert.Equal("TNO010101AAA", draft.RfcEmisor);
        Assert.Equal("Cliente Sur SA", draft.Receptor);
        Assert.Equal("CSU020202BBB", draft.RfcReceptor);
        Assert.Equal("380.50", draft.Subtotal);
        Assert.Equal("60.88", draft.Impuestos);
        Assert.Equal("441.38", draft.Total);
        Assert.Equal(2, draft.Conceptos.Count);
        Assert.Equal("Servicio de ajuste", draft.Conceptos[0].Descripcion);
        Assert.Equal("2", draft.Conceptos[0].Cantidad);
        Assert.Equal("150.00", draft.Conceptos[0].PrecioUnitario);
        Assert.Equal("300.00", draft.Conceptos[0].Importe);
        Assert.Equal("Refacción filtro", draft.Conceptos[1].Descripcion);
        Assert.Equal("80.50", draft.Conceptos[1].Importe);
        Assert.DoesNotContain("Folio", draft.LowConfidence.Keys);
    }

    [Fact]
    public void Sin_folio_marca_confianza_baja()
    {
        var document = TextNormalizer.ReadBytes(SampleInvoice.CreateXps());
        var page = document.Pages[0];
        var runs = page.Runs.Where(run => run.Text != "A-100").ToList();
        var result = InvoiceExtractor.Extract(new TextDocument(new[]
        {
            new TextPage(page.Number, page.Width, page.Height, runs)
        }));

        Assert.True(result.Recognized);
        Assert.Equal("", result.Draft.Folio);
        Assert.Contains("Folio", result.Draft.LowConfidence.Keys);
    }

    [Fact]
    public void Texto_sin_anclas_no_se_reconoce()
    {
        var document = new TextDocument(new[]
        {
            new TextPage(1, 612, 792, new[] { new TextRun(1, "Hola", 10, 10, 30, 12, "Arial") })
        });

        var result = InvoiceExtractor.Extract(document);

        Assert.False(result.Recognized);
        Assert.Contains("Folio", result.Draft.LowConfidence.Keys);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Glyphs_contiguos_se_unen()
    {
        var document = TextNormalizer.ReadBytes(TinyXps(("A-", 100, 40, 12), ("100", 112, 40, 12)));

        Assert.Equal("A-100", Assert.Single(document.Runs).Text);
    }

    [Fact]
    public void Pdf_incluye_los_datos_confirmados()
    {
        var draft = InvoiceExtractor.Extract(TextNormalizer.ReadBytes(SampleInvoice.CreateXps())).Draft;
        var pdf = Encoding.ASCII.GetString(InvoicePdfWriter.Write(draft));

        Assert.Contains("%PDF-1.4", pdf);
        Assert.Contains("FACTIX", pdf);
        Assert.Contains("A-100", pdf);
        Assert.Contains("Taller Norte SA", pdf);
        Assert.Contains("Servicio de ajuste", pdf);
        Assert.Contains("441.38", pdf);
    }

    [Fact]
    public void Elige_driver_gdi_antes_que_xps()
    {
        var driver = PrinterDriverSelection.Choose(new[]
        {
            "Microsoft XPS Document Writer",
            "Generic / Text Only",
            "Microsoft PS Class Driver"
        });

        Assert.Equal("Microsoft PS Class Driver", driver);
        Assert.Equal("NT EMF 1.008", PrinterDriverSelection.DatatypeFor(driver));
        Assert.Equal("RAW", PrinterDriverSelection.DatatypeFor("Microsoft XPS Document Writer"));
        Assert.Equal("RAW", PrinterDriverSelection.DatatypeFor("Generic / Text Only"));
    }

    [Fact]
    public void Job_json_respeta_el_contrato_del_procesador()
    {
        const string json = "{\"id\":\"abc\",\"documentName\":\"Factura \\\"A\\\"\",\"datatype\":\"NT EMF 1.008\",\"file\":\"spool.spl\",\"createdUtc\":\"2026-10-18T22:15:00Z\"}";
        var job = JobFolder.Parse(json, @"C:\ProgramData\Factix\jobs\abc");

        Assert.Equal("abc", job.Id);
        Assert.Equal("Factura \"A\"", job.DocumentName);
        Assert.Equal("NT EMF 1.008", job.Datatype);
        Assert.EndsWith("spool.spl", job.SourcePath.Replace('/', '\\'));
        Assert.Equal(2026, job.CreatedUtc.Year);
    }

    [Fact]
    public void PostScript_del_puerto_se_lee_como_factura()
    {
        var ps = Encoding.ASCII.GetBytes(
            "%!PS-Adobe-3.0\n" +
            "%%BoundingBox: 0 0 612 792\n" +
            "/F0 12 Tf\n" +
            "40 700 moveto (FACTURA) show\n" +
            "40 640 moveto (Folio) show\n" +
            "160 640 moveto (A-100) show\n" +
            "40 500 moveto (Total) show\n" +
            "160 500 moveto (441.38) show\n");
        var result = InvoiceExtractor.Extract(TextNormalizer.ReadBytes(ps));

        Assert.True(result.Recognized);
        Assert.Equal("A-100", result.Draft.Folio);
        Assert.Equal("441.38", result.Draft.Total);
    }

    [Fact]
    public void Texto_en_la_misma_linea_separa_etiqueta_y_valor()
    {
        var text = Encoding.ASCII.GetBytes("FACTURA\nFolio A-100\nFecha 2026-03-15\nTotal 10.00\n");
        var result = InvoiceExtractor.Extract(TextNormalizer.ReadBytes(text));

        Assert.True(result.Recognized);
        Assert.Equal("A-100", result.Draft.Folio);
        Assert.Equal("2026-03-15", result.Draft.Fecha);
        Assert.Equal("10.00", result.Draft.Total);
    }

    private static byte[] TinyXps(params (string Text, double X, double Y, double Size)[] glyphs)
    {
        var page = new StringBuilder();
        page.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        page.Append("<FixedPage xmlns=\"http://schemas.microsoft.com/xps/2005/06\" Width=\"816\" Height=\"1056\">");
        foreach (var glyph in glyphs)
        {
            page.Append("<Glyphs UnicodeString=\"").Append(glyph.Text);
            page.Append("\" OriginX=\"").Append(glyph.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
            page.Append("\" OriginY=\"").Append(glyph.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
            page.Append("\" FontRenderingEmSize=\"").Append(glyph.Size.ToString(System.Globalization.CultureInfo.InvariantCulture));
            page.Append("\" />");
        }

        page.Append("</FixedPage>");
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("Documents/1/Pages/1.fpage");
            using var entryStream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(page.ToString());
            entryStream.Write(bytes, 0, bytes.Length);
        }

        return stream.ToArray();
    }
}
