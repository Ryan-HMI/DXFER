namespace DXFER.Core.Documents;

public sealed record DrawingDocumentWarning(
    string Code,
    DrawingDocumentWarningSeverity Severity,
    string Message);

public enum DrawingDocumentWarningSeverity
{
    Info,
    Warning,
    Error
}
