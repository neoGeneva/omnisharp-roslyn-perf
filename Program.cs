using System.Diagnostics;
using System.Text.Json;

const string TargetSolution = @"C:\Projects\roslyn\Roslyn.sln";
const string TargetFileName = @"C:\Projects\roslyn\src\Compilers\CSharp\Test\Symbol\Symbols\DefaultInterfaceImplementationTests.cs";

var testsRemaining = 5;

var initTimer = new Stopwatch();
initTimer.Start();

var solutionLoadTimer = new Stopwatch();
var hasPrasedSolution = false;

var fileChangeDiagnosticTimer = new Stopwatch();

Console.WriteLine("Starting Omnisharp");

using var proc = Process.Start(new ProcessStartInfo()
{
    WorkingDirectory = Path.GetDirectoryName(TargetSolution),
    FileName = @"C:\Projects\neoGeneva\omnisharp-roslyn\artifacts\publish\OmniSharp.Stdio.Driver\win7-x64\net472\OmniSharp.exe",
    Arguments = "-z "
        + $"-s {TargetSolution} "
        + $"--hostPID {Environment.ProcessId} "
        + "DotNet:enablePackageRestore=false "
        + "--encoding utf-8 "
        + "--loglevel debug "
        + @"--plugin c:\Users\phil\.vscode\extensions\ms-dotnettools.csharp-1.24.0\.razor\OmniSharpPlugin\Microsoft.AspNetCore.Razor.OmniSharpPlugin.dll "
        + "FileOptions:SystemExcludeSearchPatterns:0=**/.git "
        + "FileOptions:SystemExcludeSearchPatterns:1=**/.svn "
        + "FileOptions:SystemExcludeSearchPatterns:2=**/.hg "
        + "FileOptions:SystemExcludeSearchPatterns:3=**/CVS "
        + "FileOptions:SystemExcludeSearchPatterns:4=**/.DS_Store "
        + "FileOptions:SystemExcludeSearchPatterns:5=**/Thumbs.db "
        + "RoslynExtensionsOptions:EnableAnalyzersSupport=true "
        // + "RoslynExtensionsOptions:AnalyzeOpenDocumentsOnly=true "
        + "FormattingOptions:EnableEditorConfigSupport=true "
        + "FormattingOptions:OrganizeImports=true "
        + "formattingOptions:useTabs=false "
        + "formattingOptions:tabSize=4 "
        + "formattingOptions:indentationSize=4",
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true
}) ?? throw new Exception("Could not start OmniSharp");

var cts = new CancellationTokenSource();
var seq = 0;
var fileNames = new HashSet<string>();

var outputThread = Task.Run(async () =>
{
    try
    {
        while (!cts.IsCancellationRequested)
        {
            var line = await proc.StandardOutput
                .ReadLineAsync()
                .WaitAsync(cts.Token);

            if (line is null)
                continue;

            var parsed = JsonSerializer.Deserialize<AllData>(line);

            if (parsed is null)
                continue;

            seq = parsed.Seq ?? seq;
            var ev = parsed.Event;

            if (ev == "MsBuildProjectDiagnostics")
                OnProjectInit(parsed);

            if (ev == "ProjectAdded")
                OnProjectAdded(parsed);

            if (ev == "BackgroundDiagnosticStatus")
                OnDiagnosticStatus(parsed);

            if (ev == "Diagnostic")
                OnDiagnostic(parsed);

            if (ev != "log")
                Debug.WriteLine(ev);
        }
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested)
    { }
});

Console.CancelKeyPress += (sender, e) =>
{
    cts.Cancel();
    e.Cancel = true;
};

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{ }

Console.WriteLine("Stopping...");

cts.Cancel();

await outputThread;

proc.Close();

void OnProjectInit(AllData parsed)
{
    var fileName = parsed.Body.FileName;

    if (fileName == null)
        throw new InvalidOperationException("No files name");

    fileNames.Add(fileName);
}

void OnProjectAdded(AllData parsed)
{
    var fileName = parsed.Body.MsBuildProject.Path;

    if (fileName == null)
        return;

    if (!fileNames.Remove(fileName))
        throw new InvalidOperationException("File not found");

    if (fileNames.Count == 0)
        OnSolutionReady();
}

void OnSolutionReady()
{
    Console.WriteLine("Init Time: " + initTimer.Elapsed);

    solutionLoadTimer.Restart();

    RequestCodeCheck();
}

void OnDiagnosticStatus(AllData parsed)
{
    var numberFilesRemaining = parsed.Body.NumberFilesRemaining;

    if (numberFilesRemaining == null)
        throw new InvalidOperationException("Null files remaining");

    if (numberFilesRemaining == 0 && hasPrasedSolution && solutionLoadTimer.IsRunning)
    {
        solutionLoadTimer.Stop();

        Console.WriteLine("Solution Load Time: " + solutionLoadTimer.Elapsed);

        OpenFile();
        ChangeFile();
    }

    if (numberFilesRemaining > 0)
        hasPrasedSolution = true;
}

void RequestCodeCheck()
{
    Console.WriteLine($"Code check: {TargetFileName}");

    proc.StandardInput.WriteLine(JsonSerializer.Serialize(new
    {
        Type = "request",
        Seq = seq + 1,
        Command = "/codecheck",
        Arguments = new
        {
            Filename = TargetFileName
        }
    }));
}

void ChangeFile()
{
    Console.WriteLine($"Change: {TargetFileName}");

    fileChangeDiagnosticTimer.Restart();

    proc.StandardInput.WriteLine(JsonSerializer.Serialize(new
    {
        Type = "request",
        Seq = seq + 1,
        Command = "/filesChanged",
        Arguments = new[]
        {
            new
            {
                ChangeType = "Change",
                Filename = TargetFileName
            }
        }
    }));
}

void OpenFile()
{
    Console.WriteLine($"Open: {TargetFileName}");

    proc.StandardInput.WriteLine(JsonSerializer.Serialize(new
    {
        Type = "request",
        Seq = seq + 1,
        Command = "/open",
        Arguments = new
        {
            ChangeType = "Change",
            Filename = TargetFileName
        }
    }));
}

void OnDiagnostic(AllData parsed)
{
    if (!fileChangeDiagnosticTimer.IsRunning)
        return;

    var fileNames = parsed.Body.Results.Select(x => x.FileName).ToArray();

    if (fileNames != null)
    {
        foreach (var fileName in fileNames)
        {
            if (fileName == TargetFileName)
            {
                Console.WriteLine("Diagnostic: " + fileChangeDiagnosticTimer.Elapsed);

                fileChangeDiagnosticTimer.Stop();

                if (--testsRemaining == 0)
                    cts.Cancel();
                else
                    ChangeFile();
            }
        }
    }
}

class AllData
{
    public string? Event { get; set; }
    public int? Seq { get; set; }
    public AllDataBody? Body { get; set; }
}

class AllDataBody
{
    public int? NumberFilesRemaining { get; set; }
    public AllDataBodyResults[]? Results { get; set; }
    public MSBuildProject? MsBuildProject { get; set; }
    public string? FileName { get; set; }
}

class AllDataBodyResults
{
    public string? FileName { get; set; }
}

class MSBuildProject
{
    public string? Path { get; set; }
}