namespace FilesModule.Domain;

/// <summary>Names that identify the module.</summary>
public static class ModuleTypes
{
    /// <summary>The module's schema and route-group name.</summary>
    public const string ModuleName = "files";
}

/// <summary>The feature event names the module's commands carry (<c>ICommand.EventName</c>).</summary>
public static class ModuleEvents
{
    /// <summary>Someone asked for an upload URL; the file is pending until a row references it.</summary>
    public const string UploadIntentCreated = "upload_intent_created";
}
