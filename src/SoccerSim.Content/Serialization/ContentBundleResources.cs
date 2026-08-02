using System.Reflection;

namespace SoccerSim.Content.Serialization;

/// <summary>
/// Reads a bundle out of an assembly's embedded resources.
///
/// This is how the shipped game gets its content: embedding puts the JSON inside the built
/// assembly, which ships everywhere the game does. Reading it from a path next to the
/// executable would work in the editor and then fail in an exported Godot build, where
/// <c>res://</c> lives inside the .pck and is not a real file.
/// </summary>
public static class ContentBundleResources
{
    /// <summary>
    /// Loads the bundle whose resources are named <c>{prefix}.{file}</c>, e.g.
    /// <c>SoccerDreamGame.content.manifest.json</c>.
    /// </summary>
    public static ContentBundle Read(Assembly assembly, string resourcePrefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);

        return ContentBundleFiles.Read(
            fileName =>
            {
                using Stream? stream = assembly.GetManifestResourceStream(resourcePrefix + "." + fileName);
                if (stream is null)
                    return null;

                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            },
            $"{assembly.GetName().Name}!{resourcePrefix}");
    }
}
