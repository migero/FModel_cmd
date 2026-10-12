using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Writers.UEFormat.Enums;
using static CUE4Parse.UE4.Versions.EGame;

namespace FModelCli
{
    internal class Entry
    {
        public string Name { get; set; } = "";
        public bool IsDirectory { get; set; }
        public string FullPath { get; set; } = "";
        public long Size { get; set; }
    }

    internal class Program
    {
        private static string? ParseArg(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }

        private static EGame ResolveGame(string? version)
        {
            if (string.IsNullOrEmpty(version)) return GAME_UE4_LATEST;
            version = version.Trim();
            return version switch
            {
                "4.18" => GAME_UE4_18,
                "4.19" => GAME_UE4_19,
                "4.20" => GAME_UE4_20,
                "4.21" => GAME_UE4_21,
                "4.22" => GAME_UE4_22,
                "4.23" => GAME_UE4_23,
                "4.24" => GAME_UE4_24,
                "4.25" => GAME_UE4_25,
                "4.26" => GAME_UE4_26,
                "4.27" => GAME_UE4_27,
                "4.28" => GAME_UE4_28,
                _ => GAME_UE4_LATEST
            };
        }

        private static void Main(string[] args)
        {
            string? folder = ParseArg(args, "--folder");
            string? aesKeyHex = ParseArg(args, "--aes-key");
            string? ueVersion = ParseArg(args, "--ue4-version");
            string? exportMap = ParseArg(args, "--export-map");
            string? meshFormatArg = ParseArg(args, "--mesh-format");
            string? output = ParseArg(args, "--output");
            string? inspectMaterial = ParseArg(args, "--inspect-material");
            string? exportAnim = ParseArg(args, "--export-anim");
            string? animFormat = ParseArg(args, "--anim-format") ?? "psa";
            string? listFilter = ParseArg(args, "--list-assets");
            string? inspectAsset = ParseArg(args, "--inspect-asset");
            string? grepIn = ParseArg(args, "--grep");

            if (string.IsNullOrEmpty(folder))
            {
                Console.Write("Enter folder path to browse: ");
                folder = Console.ReadLine()?.Trim();
            }

            if (string.IsNullOrEmpty(folder))
            {
                Console.WriteLine("No folder specified. Exiting.");
                return;
            }

            var root = Path.GetFullPath(folder);
            if (!Directory.Exists(root) && !File.Exists(root))
            {
                Console.WriteLine($"Directory/Folder not found: {root}");
                return;
            }

            var provider = BuildProvider(root, ueVersion, aesKeyHex);
            if (provider.Files.Count == 0)
            {
                Console.WriteLine("No files found (no .pak/.utoc found, or the AES key is wrong/missing).");
                return;
            }

            if (!string.IsNullOrEmpty(exportMap))
            {
                ExportMap(provider, exportMap, output ?? Path.Combine(Environment.CurrentDirectory, "Exports"),
                    ParseMeshFormat(meshFormatArg));
                return;
            }

            if (!string.IsNullOrEmpty(inspectMaterial))
            {
                InspectMaterial(provider, inspectMaterial);
                return;
            }

            if (!string.IsNullOrEmpty(inspectAsset))
            {
                InspectAnyAsset(provider, inspectAsset, grepIn);
                return;
            }

            if (!string.IsNullOrEmpty(listFilter))
            {
                ListAssets(provider, listFilter);
                return;
            }

            if (!string.IsNullOrEmpty(exportAnim))
            {
                ExportAnim(provider, exportAnim, animFormat, output ?? Path.Combine(Environment.CurrentDirectory, "Exports"));
                return;
            }

            Browse(provider, output ?? Path.Combine(Environment.CurrentDirectory, "Exports"));
        }

        private static DefaultFileProvider BuildProvider(string root, string? ueVersion, string? aesKeyHex)
        {
            var game = ResolveGame(ueVersion);
            var versionContainer = new VersionContainer(game);
            var provider = new DefaultFileProvider(root, SearchOption.AllDirectories, versionContainer, StringComparer.OrdinalIgnoreCase);

            Console.WriteLine("Loading archives...");
            provider.Initialize();

            if (!string.IsNullOrEmpty(aesKeyHex))
            {
                try
                {
                    var aesKey = new FAesKey(aesKeyHex);
                    provider.SubmitKeys(new Dictionary<FGuid, FAesKey> { { new FGuid(0U), aesKey } });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Invalid AES key: {ex.Message}");
                }
            }

            provider.Mount();
            Console.WriteLine($"Game: {provider.Versions.Game} | Mounted archives: {provider.MountedVfs.Count} | Files: {provider.Files.Count}");
            return provider;
        }

        private static void InspectMaterial(DefaultFileProvider provider, string name)
        {
            var key = provider.Files.Keys.FirstOrDefault(k =>
                k.EndsWith("/" + name + ".uasset", StringComparison.OrdinalIgnoreCase) ||
                k.EndsWith("/" + name + ".umap", StringComparison.OrdinalIgnoreCase) ||
                k.Contains("/" + name + ".", StringComparison.OrdinalIgnoreCase));

            if (key is null)
            {
                Console.WriteLine($"Asset '{name}' not found.");
                return;
            }

            var mat = provider.LoadPackageObject<UMaterialInterface>(key[..key.LastIndexOf('.')]);
            DumpMaterial(mat, 0);
        }

        private static void DumpMaterial(UMaterialInterface mat, int depth)
        {
            var indent = new string(' ', depth * 2);
            Console.WriteLine($"{indent}[{mat.GetType().Name}] {mat.Name}");

            if (mat is UMaterial um)
            {
                foreach (var expr in um.Expressions)
                {
                    if (!expr.TryLoad<UMaterialExpressionTextureSampleParameter>(out var tsp) || tsp.Texture is not { } texIdx)
                        continue;
                    var tex = texIdx.Load<UTexture>();
                    Console.WriteLine($"{indent}  param '{tsp.ParameterName.Text}' [{tsp.SamplerType}] -> {tex?.Name}");
                }
                foreach (var rt in um.ReferencedTextures)
                {
                    if (rt is null) continue;
                    var tex = rt.Load<UTexture>();
                    Console.WriteLine($"{indent}  referenced -> {tex?.Name}");
                }
            }
            else if (mat is UMaterialInstance mi)
            {
                if (mi is UMaterialInstanceConstant mic)
                    foreach (var tv in mic.TextureParameterValues)
                        Console.WriteLine($"{indent}  override '{tv.Name}' -> {tv.ParameterValue?.Load<UTexture>()?.Name}");

                if (mi.Parent is { } p && p.TryLoad<UUnrealMaterial>(out var parent) && parent is UMaterialInterface parentMi)
                    DumpMaterial(parentMi, depth + 1);
            }
        }

        /// <summary>
        /// Dumps any asset's JSON properties (enums, structs, data assets). Needed to
        /// read the game's real rules, e.g. the weak-point enum, instead of guessing.
        /// --grep filters the printed lines so large dumps stay readable.
        /// </summary>
        private static void InspectAnyAsset(DefaultFileProvider provider, string path, string? grep)
        {
            var obj = LoadObject<UObject>(provider, path);
            if (obj is null)
            {
                Console.WriteLine($"Asset '{path}' not found or not loadable.");
                return;
            }

            Console.WriteLine($"[{obj.GetType().Name}] {obj.Name}");

            string json;
            try
            {
                json = Newtonsoft.Json.JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Serialize failed: {ex.Message}");
                return;
            }

            if (string.IsNullOrEmpty(grep))
            {
                Console.WriteLine(json);
                return;
            }

            foreach (var line in json.Split('\n'))
                if (line.Contains(grep, StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine(line.TrimEnd());
        }

        /// <summary>
        /// Lists matching asset paths. The interactive browser cannot be driven from
        /// a script, so finding an AnimSequence by eye is not reproducible; this
        /// gives a pipeable way to discover exact paths to feed --export-anim.
        /// </summary>
        private static void ListAssets(DefaultFileProvider provider, string filter)
        {
            string needle = filter.Trim();
            int shown = 0;
            foreach (var key in provider.Files.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                if (!key.Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;
                var size = provider.Files[key].Size;
                Console.WriteLine($"{size,12:N0}  {key}");
                shown++;
            }
            Console.WriteLine($"\n{shown} match(es) for '{needle}'.");
        }

        /// <summary>
        /// Exports one animation asset. The conversion layer already routes
        /// UAnimationAsset to AnimationExporter, which supports ActorX (.psa) and
        /// USD (.usda); only this CLI entry point was missing.
        /// </summary>
        private static void ExportAnim(DefaultFileProvider provider, string path, string format, string outputDir)
        {
            if (!string.Equals(format, "psa", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(format, "usd", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(format, "usda", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Unsupported --anim-format '{format}'. Use 'psa' or 'usd'.");
                return;
            }

            var anim = LoadObject<UAnimationAsset>(provider, path);
            if (anim is null)
            {
                Console.WriteLine($"Could not load an animation asset from '{path}'.");
                return;
            }

            Console.WriteLine($"Loaded {anim.GetType().Name} '{anim.Name}' (skeleton ref: {anim.Skeleton?.Name ?? "<none>"}).");

            // ActorX for .psa, USD for .usda. Mesh/texture options are irrelevant
            // here but ExportOptions is shared, so mirror ExportMap's shape.
            var options = new ExportOptions(
                meshFormat: string.Equals(format, "psa", StringComparison.OrdinalIgnoreCase) ? EMeshFormat.ActorX : EMeshFormat.USD,
                naniteMeshFormat: ENaniteMeshFormat.NoNanite,
                meshQuality: EMeshQuality.Highest,
                texturePlatform: ETexturePlatform.DesktopMobile,
                textureFormat: ETextureFormat.Png,
                textureQuality: 100,
                exportHdrTexturesAsHdr: true,
                exportAllTextureMips: false,
                materialDepth: EMaterialDepth.AllLayersNoRef,
                exportMaterials: false,
                exportMorphTargets: true,
                socketFormat: ESocketFormat.None,
                compressionFormat: EFileCompressionFormat.None);

            var session = new ExportSession();
            session.Add(anim);

            var results = session.RunAsync(outputDir, options).GetAwaiter().GetResult();
            int ok = 0, fail = 0;
            foreach (var r in results)
            {
                if (r.Success)
                {
                    ok++;
                    if (r.DiskFilePaths is not null)
                        foreach (var f in r.DiskFilePaths) Console.WriteLine($"  OK   {f}");
                }
                else
                {
                    fail++;
                    Console.WriteLine($"  FAIL {r.ObjectPath}: {r.Error?.Message}");
                }
            }
            Console.WriteLine($"\nDone: {ok} succeeded, {fail} failed -> {outputDir}");
        }

        /// <summary>
        /// Loads any asset by package path or bare name, without assuming it is a
        /// UWorld (the old --export-map path did, which is why anims were unreachable).
        /// </summary>
        private static T? LoadObject<T>(DefaultFileProvider provider, string path) where T : UObject
        {
            var clean = path.Replace('\\', '/').Trim().TrimStart('/');
            foreach (var suffix in new[] { ".uasset", ".umap", ".uexp" })
                if (clean.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    clean = clean[..clean.LastIndexOf('.')];

            try
            {
                var direct = provider.LoadPackageObject<T>(clean);
                if (direct is not null) return direct;
            }
            catch { /* fall through to name-based lookup */ }

            var name = clean[(clean.LastIndexOf('/') + 1)..];
            var key = provider.Files.Keys.FirstOrDefault(k =>
                k.EndsWith("/" + name + ".uasset", StringComparison.OrdinalIgnoreCase));
            if (key is null) return null;

            try
            {
                var pkg = provider.LoadPackage(key);
                return pkg.GetExports().OfType<T>().FirstOrDefault();
            }
            catch { return null; }
        }

        /// <summary>ActorX (.psk) keeps skin/bone data that the USD path drops, which the
        /// io_scene_psk_psa Blender addon can then import and merge.</summary>
        /// <summary>Reads a raw switch straight off argv for call sites that sit
        /// outside Main (e.g. the interactive browser) and have no local copy.</summary>
        private static string? RawCommandLineArg(string name)
        {
            var all = Environment.GetCommandLineArgs();
            for (int i = 0; i < all.Length - 1; i++)
                if (all[i] == name) return all[i + 1];
            return null;
        }

        private static EMeshFormat ParseMeshFormat(string? raw) =>
            Enum.TryParse<EMeshFormat>(raw, ignoreCase: true, out var fmt) ? fmt : EMeshFormat.USD;

        private static void ExportMap(DefaultFileProvider provider, string path, string outputDir, EMeshFormat meshFormat)
        {
            // Any exportable object, not just UWorld: meshes, skeletons, anims and
            // data assets all go through ExportSession.Add by type.
            var obj = LoadObject<UObject>(provider, path);
            if (obj is null)
            {
                Console.WriteLine($"Could not find or load '{path}' in the archives.");
                return;
            }

            Console.WriteLine($"Exporting {obj.GetType().Name} '{obj.Name}' to {outputDir} ...");

            var session = new ExportSession();
            session.Add(obj);

            var options = new ExportOptions(
                meshFormat: meshFormat,
                naniteMeshFormat: ENaniteMeshFormat.NoNanite,
                meshQuality: EMeshQuality.Highest,
                texturePlatform: ETexturePlatform.DesktopMobile,
                textureFormat: ETextureFormat.Png,
                textureQuality: 100,
                exportHdrTexturesAsHdr: true,
                exportAllTextureMips: false,
                materialDepth: EMaterialDepth.AllLayersNoRef,
                exportMaterials: true,
                exportMorphTargets: true,
                socketFormat: ESocketFormat.None,
                compressionFormat: EFileCompressionFormat.None);

            var results = session.RunAsync(outputDir, options).GetAwaiter().GetResult();

            int ok = 0, fail = 0, texFail = 0;
            foreach (var r in results)
            {
                if (r.Success)
                {
                    ok++;
                    if (r.DiskFilePaths is not null)
                        foreach (var f in r.DiskFilePaths)
                            Console.WriteLine($"  OK   {f}");
                }
                else if (r.ObjectPath.Contains("/Textures/", StringComparison.OrdinalIgnoreCase))
                {
                    // A few textures (crunched, some BC7/ETC) can still fail on Linux; report them separately.
                    texFail++;
                    Console.WriteLine($"  TEXFAIL {r.ObjectPath}: {r.Error?.Message}");
                }
                else
                {
                    fail++;
                    Console.WriteLine($"  FAIL {r.ObjectPath}: {r.Error?.Message}");
                }
            }

            Console.WriteLine($"\nDone: {ok} succeeded, {fail} failed, {texFail} textures failed.");
        }

        private static UWorld? LoadWorld(DefaultFileProvider provider, string path)
        {
            var clean = path.Replace('\\', '/').TrimStart('/');
            clean = clean.EndsWith(".umap", StringComparison.OrdinalIgnoreCase) || clean.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                ? clean[..clean.LastIndexOf('.')]
                : clean;

            try
            {
                return provider.LoadPackageObject<UWorld>(clean);
            }
            catch
            {
                // fall through to name-based lookup
            }

            var name = clean.Substring(clean.LastIndexOf('/') + 1);
            var key = provider.Files.Keys.FirstOrDefault(k =>
                k.EndsWith("/" + name + ".umap", StringComparison.OrdinalIgnoreCase) ||
                k.EndsWith("/" + name + ".uasset", StringComparison.OrdinalIgnoreCase));
            if (key is null) return null;

            try
            {
                var pkg = provider.LoadPackage(key);
                return pkg.GetExports().OfType<UWorld>().FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static void Browse(DefaultFileProvider provider, string outputDir)
        {
            // Build virtual directory tree from provider file paths
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var filesByDir = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (path, gameFile) in provider.Files)
            {
                var normalized = path.Replace('\\', '/');
                var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var current = "";
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    current = current.Length == 0 ? segments[i] : current + "/" + segments[i];
                    dirs.Add(current);
                }

                if (!filesByDir.TryGetValue(current, out var list))
                {
                    list = new List<Entry>();
                    filesByDir[current] = list;
                }

                list.Add(new Entry
                {
                    Name = segments[^1],
                    IsDirectory = false,
                    FullPath = normalized,
                    Size = gameFile.Size
                });
            }

            string currentDir = "";
            var entryCache = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);

            List<Entry> BuildEntries(string dir)
            {
                if (entryCache.TryGetValue(dir, out var cached)) return cached;

                var result = new List<Entry>();

                var prefix = dir.Length == 0 ? "" : dir + "/";
                var childDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in dirs)
                {
                    if (d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var rest = d.Substring(prefix.Length);
                        var idx = rest.IndexOf('/');
                        var child = idx < 0 ? rest : rest.Substring(0, idx);
                        childDirs.Add(dir.Length == 0 ? child : dir + "/" + child);
                    }
                }

                foreach (var childDir in childDirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(new Entry
                    {
                        Name = childDir.Substring(childDir.LastIndexOf('/') + 1),
                        IsDirectory = true,
                        FullPath = childDir,
                        Size = 0
                    });
                }

                if (filesByDir.TryGetValue(dir, out var files))
                    result.AddRange(files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase));

                entryCache[dir] = result;
                return result;
            }

            var entries = BuildEntries(currentDir);
            int selectedIndex = 0;

            try
            {
                while (true)
                {
                    Console.Clear();
                    var title = currentDir.Length == 0 ? "/" : currentDir;
                    Console.WriteLine($"Folder: {title}");
                    Console.WriteLine($"Game: {provider.Versions.Game} | Files: {provider.Files.Count}");
                    Console.WriteLine();

                    for (int i = 0; i < entries.Count; i++)
                    {
                        string marker = i == selectedIndex ? ">>" : "  ";
                        string info = entries[i].IsDirectory ? "/" : $" ({entries[i].Size:N0} bytes)";
                        Console.WriteLine($"{marker} {entries[i].Name}{info}");
                    }

                    Console.WriteLine();
                    Console.WriteLine("[Up/Down] Navigate   [Enter] Open   [Backspace] Up   [E] Export map   [Esc] Exit");

                    if (entries.Count == 0)
                        Console.WriteLine(" (empty directory)");

                    var key = Console.ReadKey(true);
                    switch (key.Key)
                    {
                        case ConsoleKey.UpArrow:
                            if (selectedIndex > 0) selectedIndex--;
                            break;
                        case ConsoleKey.DownArrow:
                            if (selectedIndex < entries.Count - 1) selectedIndex++;
                            break;
                        case ConsoleKey.Enter:
                            if (entries.Count == 0) break;
                            var selected = entries[selectedIndex];
                            if (selected.IsDirectory)
                            {
                                currentDir = selected.FullPath;
                                entries = BuildEntries(currentDir);
                                selectedIndex = 0;
                            }
                            else
                            {
                                Console.WriteLine($"\nFile: {selected.FullPath}");
                                Console.WriteLine($"Size (in pak): {selected.Size:N0} bytes");
                                Console.WriteLine("Press any key to continue...");
                                Console.ReadKey(true);
                            }
                            break;
                        case ConsoleKey.Backspace:
                            if (currentDir.Length > 0)
                            {
                                var idx = currentDir.LastIndexOf('/');
                                currentDir = idx < 0 ? "" : currentDir.Substring(0, idx);
                                entries = BuildEntries(currentDir);
                                selectedIndex = 0;
                            }
                            break;
                        case ConsoleKey.E:
                            if (entries.Count == 0) break;
                            var sel = entries[selectedIndex];
                            if (!sel.IsDirectory && (sel.Name.EndsWith(".umap", StringComparison.OrdinalIgnoreCase) || sel.Name.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)))
                            {
                                Console.Clear();
                                ExportMap(provider, sel.FullPath, outputDir,
                                    ParseMeshFormat(RawCommandLineArg("--mesh-format")));
                                Console.WriteLine("\nPress any key to continue...");
                                Console.ReadKey(true);
                            }
                            else
                            {
                                Console.WriteLine("\nSelect a .umap/.uasset file to export.");
                                Console.ReadKey(true);
                            }
                            break;
                        case ConsoleKey.Escape:
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
            }
        }
    }
}
