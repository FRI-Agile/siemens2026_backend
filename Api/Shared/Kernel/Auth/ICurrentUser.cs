namespace Kernel.Auth;

/// <summary>
/// Ambient accessor for the staff user acting in the current request, so a handler can stamp e.g. <c>created_by</c>
/// without the endpoint threading it through the command. The implementation lives in the host (it reads the
/// validated access token); the Kernel only depends on this abstraction.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The acting user's Entra object id (<see cref="StaffClaims.ObjectId"/>), or <c>null</c> when there is none.</summary>
    Guid? Id { get; }
}

/// <summary>Helpers for working with <see cref="ICurrentUser"/>.</summary>
public static class CurrentUserExtensions
{
    /// <summary>
    /// The acting user's object id. Every staff policy requires the claim, so behind one it is always present; a
    /// validated token already proves the user exists, so never re-query it. Fails fast rather than attributing a
    /// row to nobody.
    /// </summary>
    /// <param name="currentUser">The ambient current-user accessor.</param>
    /// <exception cref="InvalidOperationException">The request is not behind a staff policy.</exception>
    public static Guid RequireId(this ICurrentUser currentUser) =>
        currentUser.Id ?? throw new InvalidOperationException(
            "The caller has no object id, RequireId is only valid on an endpoint behind a staff policy.");
}
