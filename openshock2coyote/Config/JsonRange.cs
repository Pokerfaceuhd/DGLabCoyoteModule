namespace openshock2coyote.Config;

public class JsonRange<T> where T : struct
{
    public required T Min { get; set; }
    public required T Max { get; set; }
}