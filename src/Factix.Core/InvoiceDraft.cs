using System.Collections.Generic;

namespace Factix
{
    public sealed class InvoiceLineDraft
    {
        public string Descripcion { get; set; } = "";
        public string Cantidad { get; set; } = "";
        public string PrecioUnitario { get; set; } = "";
        public string Importe { get; set; } = "";
    }

    public sealed class InvoiceDraft
    {
        public string Folio { get; set; } = "";
        public string Fecha { get; set; } = "";
        public string Emisor { get; set; } = "";
        public string RfcEmisor { get; set; } = "";
        public string Receptor { get; set; } = "";
        public string RfcReceptor { get; set; } = "";
        public List<InvoiceLineDraft> Conceptos { get; set; } = new List<InvoiceLineDraft>();
        public string Subtotal { get; set; } = "";
        public string Impuestos { get; set; } = "";
        public string Total { get; set; } = "";
        public Dictionary<string, string> LowConfidence { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ExtractionResult
    {
        public string TemplateName { get; set; } = "";
        public bool Recognized { get; set; }
        public InvoiceDraft Draft { get; set; } = new InvoiceDraft();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
