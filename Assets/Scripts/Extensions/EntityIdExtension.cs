using UnityEngine;

public static class EntityIdExtension
{
    //"To ensure compatibility, avoid the following patterns:
    // Casting EntityId to or from an integer type."
    // cry about it
    public static int ToInt(this EntityId id)
    {
        ulong value = EntityId.ToULong(id);
        int i = (int)value >> 0;
        return i;
    }
}