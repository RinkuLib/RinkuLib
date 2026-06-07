using RinkuPowerTools.Compiler;

if (args.Length < 3) {
    Console.Error.WriteLine("Usage: Rinku.XamlCompiler <shell.xaml> <inputDir> <outputDir> [--recursive] [--pattern .template.xaml]");
    return 1;
}

string shellPath = args[0];
string inputPath = args[1];
string outputPath = args[2];

string pattern = ".template.xaml";
bool recursive = false;
try {
    for (int i = 3; i < args.Length; i++) {
        if (args[i] == "--recursive")
            recursive = true;

        if (args[i] == "--pattern" && i + 1 < args.Length) {
            pattern = args[i + 1];
            i++;
        }
    }

    if (!File.Exists(shellPath))
        throw new Exception($"Shell not found: {shellPath}");

    if (!Directory.Exists(inputPath))
        throw new Exception($"Input directory not found: {inputPath}");

    var shell = File.ReadAllText(shellPath);

    var search = recursive
        ? SearchOption.AllDirectories
        : SearchOption.TopDirectoryOnly;

    var files = Directory.GetFiles(inputPath, "*" + pattern, search);

    int count = MergeXAMLEngine.MergeFiles(inputPath, outputPath, pattern, shell, files);

    Console.WriteLine($"RinkuXamlCompiler: completed ({count} file(s)) from \"{inputPath}\"");

    return 0;
}
catch (Exception ex) {
    if (ex is XamlCompilerException x)
        Console.Error.WriteLine(x);
    else
        Console.Error.WriteLine($"error RXC001 : {ex.Message}");
    return 1;
}