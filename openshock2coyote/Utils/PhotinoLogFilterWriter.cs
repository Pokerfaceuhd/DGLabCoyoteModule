using System.Text;

namespace openshock2coyote.Utils;

public class PhotinoLogFilterWriter(TextWriter inner) : TextWriter
{
    private const string PhotinoPrefix = "Photino.NET: ";

    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value) => inner.Write(value);

    public override void Write(string? value) => inner.Write(value);

    public override void Write(char[] buffer, int index, int count) => inner.Write(buffer, index, count);

    public override void WriteLine() => inner.WriteLine();

    public override void WriteLine(string? value)
    {
        if (value != null && value.StartsWith(PhotinoPrefix))
            return;

        inner.WriteLine(value);
    }

    public override void Flush() => inner.Flush();
}
