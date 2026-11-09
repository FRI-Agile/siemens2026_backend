namespace Kernel.Files;

/// <summary>
/// Declares which columns of a module's table hold file ids, so the Files module confirms a file when a row gains it and
/// releases it when the row loses it, from the table's outbox changes (docs/api/files/files.md §9). The owning module
/// registers one per file-bearing table as a singleton; the table's entity must be an <c>ITrackedEntity</c> on a
/// context with change capture, or no change ever reaches the Files module.
/// </summary>
/// <param name="Module">The producing module, i.e. its schema (e.g. <c>recruitment</c>).</param>
/// <param name="EntityType">The table (e.g. <c>application_document</c>).</param>
/// <param name="Columns">The file-bearing properties, each with the purposes it accepts.</param>
public sealed record FileReference(string Module, string EntityType, IReadOnlyList<FileColumn> Columns);

/// <summary>One file-bearing property of a <see cref="FileReference"/>'s entity.</summary>
/// <param name="Property">
/// The <b>CLR property name</b> (e.g. <c>FileId</c>), which is how the change-capture interceptor keys a row's values,
/// not the snake_case column name.
/// </param>
/// <param name="Purposes">
/// The <see cref="IFilePurpose.Key"/>s this property accepts. A file of any other purpose is never confirmed through it,
/// and losing it does not release that file: the uploader picks the purpose, and the purpose carries the content rules.
/// </param>
public sealed record FileColumn(string Property, IReadOnlyCollection<string> Purposes);
