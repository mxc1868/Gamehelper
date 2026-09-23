using System.Reflection;
using System.Runtime.Loader;

// Inspect the deployed assemblies through the host's actual plugin loader without
// starting the overlay, invoking OnEnable, reading game memory, or writing settings.
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: PluginLoad.Tests <directory containing GameHelper.dll and Plugins>");
    return 2;
}

var root = Path.GetFullPath(args[0]);
Directory.SetCurrentDirectory(root);
var hostPath = Path.Combine(root, "GameHelper.dll");
var resolver = new AssemblyDependencyResolver(hostPath);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = resolver.ResolveAssemblyToPath(name) ?? Path.Combine(root, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};

var host = AssemblyLoadContext.Default.LoadFromAssemblyPath(hostPath);
Console.WriteLine($"Host: {host.FullName}\nDirectory: {root}");
var alcType = host.GetType("GameHelper.Plugin.PluginAssemblyLoadContext", throwOnError: true)!;
var managerType = host.GetType("GameHelper.Plugin.PManager", throwOnError: true)!;
var loadPlugin = managerType.GetMethod("LoadPlugin", BindingFlags.NonPublic | BindingFlags.Static,
    [typeof(Assembly), alcType, typeof(string)])!;
var failed = false;
var discover = managerType.GetMethod("GetPluginsDirectories", BindingFlags.NonPublic | BindingFlags.Static)!;
var directories = (IEnumerable<DirectoryInfo>)discover.Invoke(null, null)!;
var names = directories.Select(directory => directory.Name).ToArray();
var renameOk = names.Contains("ShowMeWisp", StringComparer.OrdinalIgnoreCase) && !names.Contains("WhereTheWispsAt", StringComparer.OrdinalIgnoreCase);
failed |= !renameOk;
Console.WriteLine($"{(renameOk ? "PASS" : "FAIL")} discovery uses ShowMeWisp without the legacy duplicate");

foreach (var name in new[] { "ShowMeWisp", "UniqueLoot", "Radar" })
{
    AssemblyLoadContext? alc = null;
    try
    {
        var directory = Path.Combine(root, "Plugins", name);
        alc = (AssemblyLoadContext)Activator.CreateInstance(alcType, Path.Combine(directory, name + ".dll"))!;
        var assembly = alc.LoadFromAssemblyPath(Path.Combine(directory, name + ".dll"));
        var result = loadPlugin.Invoke(null, [assembly, alc, directory]);
        if (result == null) throw new InvalidOperationException("Host plugin loader rejected the plugin; see error above.");
        Console.WriteLine($"PASS {name}: loaded and instantiated by PManager");
    }
    catch (Exception ex)
    {
        failed = true;
        Console.Error.WriteLine($"FAIL {name}: {ex}");
    }
    finally { alc?.Unload(); }
}

// These bindings can be resolved after instantiation, when scanning first runs.
// Check them without invoking them, so UniqueLoot cannot pass on an unpatched host.
foreach (var typeName in new[]
{
    "GameHelper.Utils.MapProjection",
    "GameHelper.RemoteObjects.States.InGameStateObjects.EntityScanDiagnostics",
    "GameHelper.RemoteObjects.States.InGameStateObjects.EntityScanSource",
})
{
    var found = host.GetType(typeName) != null;
    failed |= !found;
    Console.WriteLine($"{(found ? "PASS" : "FAIL")} core type: {typeName}");
}

foreach (var (typeName, methodName, parameterCount) in new[]
{
    ("GameHelper.RemoteObjects.States.InGameStateObjects.AreaInstance", "ScanEntities", 4),
    ("GameHelper.RemoteObjects.States.InGameStateObjects.AreaInstance", "ScanAwakeEntities", 2),
    ("GameHelper.RemoteObjects.Components.WorldItem", "TryReadItem", 1),
})
{
    var found = host.GetType(typeName)?.GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Any(method => method.Name == methodName && method.GetParameters().Length == parameterCount) == true;
    failed |= !found;
    Console.WriteLine($"{(found ? "PASS" : "FAIL")} core API: {typeName}.{methodName}");
}

Console.WriteLine(failed ? "Plugin load checks failed." : "All 10 plugin discovery/load/API checks passed. Game rendering is not tested.");
return failed ? 1 : 0;
