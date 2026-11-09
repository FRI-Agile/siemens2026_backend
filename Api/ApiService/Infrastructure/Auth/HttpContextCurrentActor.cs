using Kernel.Auth;
using Kernel.Outbox;

namespace ApiService.Infrastructure.Auth;

/// <summary>
/// <see cref="ICurrentActor"/> for the change-capture interceptor: the signed-in staff member from the token, an
/// anonymous caller (an applicant) for a request without one, and the system outside any request — a scheduled worker,
/// the outbox dispatcher.
/// </summary>
internal sealed class HttpContextCurrentActor(IHttpContextAccessor accessor, ICurrentUser currentUser) : ICurrentActor
{
    public Actor Current => accessor.HttpContext is null
        ? Actor.System
        : currentUser.Id is { } id ? Actor.ForUser(id) : Actor.Anonymous;
}
