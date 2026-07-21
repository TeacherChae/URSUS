using System.Xml;
using System.Xml.Linq;

namespace URSUS.Parsers;

public sealed record SeoulXmlPage(
    int TotalCount,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows,
    bool HasDuplicateIdentity,
    bool IsComplete);

public static class SeoulXmlStreamParser
{
    public static SeoulXmlPage Parse(
        Stream stream,
        IEnumerable<string> projectedFields,
        Func<IReadOnlyDictionary<string, string>, string> stableIdentity)
    {
        var fields = projectedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        bool duplicate = false;
        int total = -1;
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = false,
        });
        while (reader.Read())
        {
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
            using var subtree = reader.ReadSubtree();
            var element = XElement.Load(subtree, LoadOptions.None);
            var row = element.Elements()
                .Where(child => fields.Contains(child.Name.LocalName))
                .ToDictionary(child => child.Name.LocalName, child => child.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase);
            string identity = stableIdentity(row);
            if (!identities.Add(identity)) duplicate = true;
            rows.Add(row);
            hasRow = reader.ReadToFollowing("row");
        }
        if (total < 0) total = rows.Count;
        return new SeoulXmlPage(total, rows, duplicate,
            !duplicate && total == rows.Count);
    }
}
