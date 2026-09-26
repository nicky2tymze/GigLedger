namespace GigLedger.Core;

public interface IExportService
{
    /// <summary>
    /// FR-32: the whole ledger as one zip: a CSV per table with every version of every row, each
    /// receipt as its original file, and a manifest of counts.
    /// </summary>
    void Export(Stream zip);
}
