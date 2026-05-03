namespace DXFER.Core.Documents;

public readonly record struct EntityId(string Value)
{
    public static EntityId Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Entity ID cannot be empty.", nameof(value));
        }

        return new EntityId(value);
    }

    public override string ToString() => Value;
}
