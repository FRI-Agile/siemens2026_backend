using Kernel.Auth;

namespace Tests.Shared.Auth;

/// <summary>An <see cref="ICurrentUser"/> fixed to <paramref name="id"/>, for a handler test with no request.</summary>
internal sealed class StubCurrentUser(Guid? id) : ICurrentUser
{
    public Guid? Id => id;
}
