namespace Atria.Core.Runtime;

/// <summary>Rejects another command from a menu context that just changed semantically.</summary>
internal sealed class MobileMenuCommandGuard
{
    private ulong? invalidatedContext;

    public bool Allows(ulong context)
    {
        if (invalidatedContext == context)
        {
            return false;
        }

        if (invalidatedContext is not null)
        {
            invalidatedContext = null;
        }

        return true;
    }

    public void Invalidate(ulong context) => invalidatedContext = context;
}
