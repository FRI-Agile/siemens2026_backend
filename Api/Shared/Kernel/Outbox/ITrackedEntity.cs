namespace Kernel.Outbox;

/// <summary>
/// Marks an entity whose inserts, updates and deletes are captured onto its module's outbox
/// (<see cref="ChangeCaptureInterceptor"/>), so other modules can react to them. An entity without it is invisible to
/// every consumer.
/// </summary>
public interface ITrackedEntity;
