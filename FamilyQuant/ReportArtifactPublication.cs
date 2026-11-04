using FamilyQuant.Operations;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyQuant;

/// <summary>An export could not be fully rolled back. Keep its private staging folder for recovery.</summary>
public sealed class ReportPublicationRecoveryException : IOException
{
    public string RecoveryDirectory { get; }
    internal ReportPublicationRecoveryException(string directory, Exception failure)
        : base("Report publication could not be fully restored. Recovery copies remain in " + directory + ".", failure)
        => RecoveryDirectory = directory;
}

/// <summary>Publishes staged Word, workbook and HTML in one operation commit. A failure or cancellation
/// between renames restores previous files and removes new files. A late successful commit wins Cancel.</summary>
public static class ReportArtifactPublication
{
    public static void Publish(string stagedWord, string stagedWorkbook, string wordDestination,
        OperationContext operation, Action<int>? afterPublication = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        string stageWord = Path.GetFullPath(stagedWord), stageBook = Path.GetFullPath(stagedWorkbook);
        string stageHtml = HtmlReportWriter.PathForReport(stageWord);
        string destinationWord = Path.GetFullPath(wordDestination), destinationBook = ReportSupplementWorkbook.PathForReport(destinationWord);
        string destinationHtml = HtmlReportWriter.PathForReport(destinationWord);
        string directory = Path.GetDirectoryName(stageWord)!;
        if (!File.Exists(stageWord) || !File.Exists(stageBook) || !File.Exists(stageHtml))
            throw new FileNotFoundException("The staged Word report, supplement workbook and HTML report must all be complete before publication.");
        if (!string.Equals(directory, Path.GetDirectoryName(stageBook), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetPathRoot(directory), Path.GetPathRoot(destinationWord), StringComparison.OrdinalIgnoreCase)
            || string.Equals(stageWord, destinationWord, StringComparison.OrdinalIgnoreCase)
            || string.Equals(stageBook, destinationBook, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Report artifacts must be staged together beside their destination on the same volume.");
        var source = new[] { stageWord, stageBook, stageHtml };
        var destination = new[] { destinationWord, destinationBook, destinationHtml };
        var backup = new[] { Path.Combine(directory, ".previous-word-" + Guid.NewGuid().ToString("N")),
            Path.Combine(directory, ".previous-workbook-" + Guid.NewGuid().ToString("N")),
            Path.Combine(directory, ".previous-html-" + Guid.NewGuid().ToString("N")) };
        var published = new bool[source.Length];
        var existed = new bool[source.Length];
        bool recovery = false;
        try
        {
            operation.Commit(() =>
            {
                try
                {
                    for (int i = 0; i < source.Length; i++)
                    {
                        operation.ThrowIfCancellationRequested();
                        existed[i] = File.Exists(destination[i]);
                        if (existed[i]) File.Copy(destination[i], backup[i]);
                    }
                    for (int i = 0; i < source.Length; i++)
                    {
                        operation.ThrowIfCancellationRequested();
                        File.Move(source[i], destination[i], true);
                        published[i] = true;
                        afterPublication?.Invoke(i + 1);
                        operation.ThrowIfCancellationRequested();
                    }
                }
                catch (Exception failure)
                {
                    var failures = new List<Exception> { failure };
                    for (int i = source.Length - 1; i >= 0; i--)
                    {
                        if (!published[i]) continue;
                        try
                        {
                            if (existed[i]) File.Move(backup[i], destination[i], true);
                            else File.Delete(destination[i]);
                        }
                        catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException) { failures.Add(rollback); }
                    }
                    if (failures.Count > 1)
                    {
                        recovery = true;
                        throw new ReportPublicationRecoveryException(directory, new AggregateException(failures));
                    }
                    throw;
                }
            });
        }
        finally
        {
            if (!recovery)
                foreach (string path in backup)
                    try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
