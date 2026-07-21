using System.Xml;
using System.Xml.Linq;

namespace URSUS.Parsers;

public sealed record SeoulXmlPage(
    int TotalCount,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows,
    bool HasDuplicateIdentity,
    bool IsComplete);

internal readonly record struct SeoulXmlPageSummary(int TotalCount, int RowCount);

public static class SeoulXmlStreamParser
{
    public static SeoulXmlPage Parse(
        Stream stream,
        IEnumerable<string> projectedFields,
        Func<IReadOnlyDictionary<string, string>, string> stableIdentity)
    {
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        bool duplicate = false;
        SeoulXmlPageSummary summary = ParseRows(stream, projectedFields, row =>
        {
            string identity = stableIdentity(row);
            if (!identities.Add(identity)) duplicate = true;
            rows.Add(row);
        }, CancellationToken.None);
        return new SeoulXmlPage(summary.TotalCount, rows, duplicate,
            summary.TotalCount >= 0 && !duplicate && summary.TotalCount == rows.Count);
    }

    internal static SeoulXmlPageSummary ParseRows(
        Stream stream,
        IEnumerable<string> projectedFields,
        Action<IReadOnlyDictionary<string, string>> consumeRow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(projectedFields);
        ArgumentNullException.ThrowIfNull(consumeRow);
        var fields = projectedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int total = -1;
        int rowCount = 0;
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = false,
            MaxCharactersInDocument = 16 * 1024 * 1024,
        });
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element &&
                reader.LocalName.Equals("list_total_count", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(reader.ReadElementContentAsString(), out int parsed)) total = parsed;
                break;
            }
        }

        bool hasRow = reader.NodeType == XmlNodeType.Element &&
                      reader.LocalName.Equals("row", StringComparison.OrdinalIgnoreCase);
        if (!hasRow) hasRow = reader.ReadToFollowing("row");
        while (hasRow)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var subtree = reader.ReadSubtree();
            var element = XElement.Load(subtree, LoadOptions.None);
            var row = element.Elements()
                .Where(child => fields.Contains(child.Name.LocalName))
                .ToDictionary(child => child.Name.LocalName, child => child.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase);
            consumeRow(row);
            rowCount++;
            hasRow = reader.ReadToFollowing("row");
        }
        return new SeoulXmlPageSummary(total, rowCount);
    }

    internal static async Task<SeoulXmlPageSummary> ParseRowsAsync(
        Stream stream,
        IEnumerable<string> projectedFields,
        Func<IReadOnlyDictionary<string, string>, CancellationToken, ValueTask> consumeRow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(projectedFields);
        ArgumentNullException.ThrowIfNull(consumeRow);
        var fields = projectedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int total = -1;
        int rowCount = 0;
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = false,
            MaxCharactersInDocument = 16 * 1024 * 1024,
        });
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element &&
                reader.LocalName.Equals("list_total_count", StringComparison.OrdinalIgnoreCase))
            {
                string content = await reader.ReadElementContentAsStringAsync().ConfigureAwait(false);
                if (int.TryParse(content, out int parsed)) total = parsed;
                break;
            }
        }

        bool hasRow = reader.NodeType == XmlNodeType.Element &&
                      reader.LocalName.Equals("row", StringComparison.OrdinalIgnoreCase);
        if (!hasRow)
            hasRow = await ReadToFollowingAsync(reader, "row", cancellationToken)
                .ConfigureAwait(false);
        while (hasRow)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var subtree = reader.ReadSubtree();
            var element = await XElement.LoadAsync(subtree, LoadOptions.None, cancellationToken)
                .ConfigureAwait(false);
            var row = element.Elements()
                .Where(child => fields.Contains(child.Name.LocalName))
                .ToDictionary(child => child.Name.LocalName, child => child.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase);
            await consumeRow(row, cancellationToken).ConfigureAwait(false);
            rowCount++;
            hasRow = await ReadToFollowingAsync(reader, "row", cancellationToken)
                .ConfigureAwait(false);
        }
        return new SeoulXmlPageSummary(total, rowCount);
    }

    private static async Task<bool> ReadToFollowingAsync(
        XmlReader reader,
        string localName,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element &&
                reader.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
