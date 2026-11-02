using System.Security.Claims;
using Kernel.Auth;

namespace ApiService.Infrastructure.Auth;

/// <summary>
/// <see cref="ICurrentUser"/> backed by the validated Entra access token: the id is the <c>oid</c> claim, with no
/// database or directory lookup. Registered as a singleton; <see cref="IHttpContextAccessor"/> is singleton-safe and
/// yields the per-request context.
/// </summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    /// <inheritdoc />
    public Guid? Id =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(StaffClaims.ObjectId), out Guid id) ? id : null;
}
