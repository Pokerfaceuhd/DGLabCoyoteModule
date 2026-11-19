using System.Text.Json.Serialization;

namespace openshock2coyote.Config.Actions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(OpenshockAction), "openshock")]
public abstract class BaseAction
{
    public required string Name { get; set; } 
    public Guid Guid { get; set; } = Guid.NewGuid();
}