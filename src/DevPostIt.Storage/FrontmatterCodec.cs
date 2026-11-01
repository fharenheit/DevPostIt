using DevPostIt.Core;
using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace DevPostIt.Storage;

public sealed class MissingNoteIdException() : FormatException("명시적으로 가져오기를 해야 메모 ID를 부여할 수 있습니다.");

public sealed class FrontmatterCodec
{
    public Note Parse(string raw, string relativePath) => Read(raw, relativePath, false);
    public Note Import(string raw, string relativePath) => Read(raw, relativePath, true);

    private static Note Read(string raw, string relativePath, bool importing)
    {
        var (header, body) = Split(raw);
        var fields = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        if (header is not null)
        {
            try
            {
                var yaml = new YamlStream();
                yaml.Load(new StringReader(header));
                if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode mapping)
                    throw new FormatException("Frontmatter는 단일 YAML 매핑이어야 합니다.");
                foreach (var pair in mapping.Children)
                {
                    var key = Scalar(pair.Key);
                    if (key is not ("id" or "title" or "created" or "updated" or "tags"))
                        throw new FormatException($"보존할 수 없는 메타데이터: {key}");
                    fields.Add(key, pair.Value);
                }
            }
            catch (YamlException error) { throw new FormatException("잘못된 YAML입니다.", error); }
        }

        var id = fields.TryGetValue("id", out var idNode)
            ? Guid.TryParse(Scalar(idNode), out var parsed) && parsed != Guid.Empty
                ? parsed : throw new FormatException("유효한 메모 ID가 필요합니다.")
            : importing ? Guid.NewGuid() : throw new MissingNoteIdException();
        var now = DateTimeOffset.UtcNow;
        var title = fields.TryGetValue("title", out var titleNode) ? Scalar(titleNode)
            : importing ? Path.GetFileNameWithoutExtension(relativePath) : throw new FormatException("제목이 없습니다.");
        var created = ReadDate("created", now);
        var updated = ReadDate("updated", created);
        IReadOnlyList<string> tags = fields.TryGetValue("tags", out var tagsNode)
            ? tagsNode is YamlSequenceNode sequence ? sequence.Children.Select(Scalar).ToArray()
                : throw new FormatException("태그는 문자열 목록이어야 합니다.")
            : importing ? [] : throw new FormatException("태그가 없습니다.");
        return new Note(id, relativePath, title, body, tags, created, updated);

        DateTimeOffset ReadDate(string key, DateTimeOffset fallback)
        {
            if (!fields.TryGetValue(key, out var value))
                return importing ? fallback : throw new FormatException($"{key}가 없습니다.");
            var text = Scalar(value);
            // 시간대가 없는 입력을 개발 컴퓨터의 지역 설정으로 해석하지 않습니다.
            if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$") ||
                !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new FormatException($"잘못된 시각: {key}");
            return date;
        }
    }

    private static string Scalar(YamlNode node) => node is YamlScalarNode { Value: not null } scalar
        ? scalar.Value : throw new FormatException("문자열이 필요합니다.");

    private static (string? Header, string Body) Split(string raw)
    {
        var offset = raw.StartsWith('\uFEFF') ? 1 : 0;
        var start = offset;
        var opening = Line();
        if (opening != "---") return (null, raw);
        var headerStart = offset;
        while (offset < raw.Length)
        {
            start = offset;
            if (Line() == "---") return (raw[headerStart..start], raw[offset..]);
        }
        throw new FormatException("Frontmatter의 닫는 구분자가 없습니다.");

        string Line()
        {
            var end = raw.IndexOf('\n', offset);
            if (end < 0) end = raw.Length;
            var line = raw[offset..end].TrimEnd('\r');
            offset = end < raw.Length ? end + 1 : end;
            return line;
        }
    }

    public string Serialize(Note note)
    {
        if (note.Id == Guid.Empty) throw new FormatException("빈 메모 ID는 저장할 수 없습니다.");
        static YamlScalarNode Text(string value) => new(value) { Style = ScalarStyle.DoubleQuoted };
        var mapping = new YamlMappingNode
        {
            { "id", Text(note.Id.ToString()) },
            { "title", Text(note.Title) },
            { "created", Text(note.Created.ToString("O", CultureInfo.InvariantCulture)) },
            { "updated", Text(note.Updated.ToString("O", CultureInfo.InvariantCulture)) },
            { "tags", new YamlSequenceNode(note.Tags.Select(Text)) }
        };
        using var writer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        new YamlStream(new YamlDocument(mapping)).Save(writer, false);
        return "---\n" + writer + "---\n" + note.Markdown;
    }
}
