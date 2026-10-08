using System.Text.Json;

namespace LimbusCalc.Storage;

/// <summary>How to write JSON that the user opens and edits by hand.</summary>
public static class JsonFormat
{
    /// <summary>
    /// Indented, one value per line. The file gets about three times larger, but people read
    /// it. Internal files in the profile are written without indentation: they are rewritten
    /// on every edit and nobody opens them.
    /// </summary>
    public static readonly JsonSerializerOptions Readable = new() { WriteIndented = true };
}
