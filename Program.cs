using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

const string TargetFileName = @"C:\Projects\Rubber-Monkey\Source\RubberMonkey.Sales.Data\Admin\OrderDal_Cancellation.cs";

var testsRemaining = 5;

var initTimer = new Stopwatch();
initTimer.Start();

var solutionLoadTimer = new Stopwatch();

var fileChangeDiagnosticTimer = new Stopwatch();

using var proc = Process.Start(new ProcessStartInfo()
{
    WorkingDirectory = @"C:\Projects\Rubber-Monkey\Source",
    FileName = @"C:\Projects\neoGeneva\omnisharp-roslyn\artifacts\publish\OmniSharp.Stdio.Driver\win7-x64\net472\OmniSharp.exe",
    Arguments = "-z "
        + @"-s C:\Projects\Rubber-Monkey\Source\RubberMonkey.Sales.sln "
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
        + "RoslynExtensionsOptions:AnalyzeOpenDocumentsOnly=true "
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

            var parsed = JsonConvert.DeserializeObject<JObject>(line);

            if (parsed is null)
                continue;

            seq = parsed.Value<int>("Seq");
            var ev = parsed.Value<string?>("Event");

            if (ev == "MsBuildProjectDiagnostics")
                OnProjectInit(parsed);

            if (ev == "ProjectAdded")
                OnProjectAdded(parsed);

            if (ev == "BackgroundDiagnosticStatus")
                OnDiagnosticStatus(parsed);

            if (ev == "Diagnostic")
                OnDiagnostic(parsed);

            if (ev != "log")
                Debug.WriteLine(line);
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

void OnProjectInit(JObject parsed)
{
    var fileName = parsed.SelectToken("$.Body.FileName")?.Value<string>();

    if (fileName == null)
        throw new InvalidOperationException("No files name");

    fileNames.Add(fileName);
}

void OnProjectAdded(JObject parsed)
{
    if (fileNames.Count == 0)
        throw new InvalidOperationException("No files found");

    var fileName = parsed.SelectToken("$.Body.MsBuildProject.Path")?.Value<string>();

    if (fileName == null)
        throw new InvalidOperationException("No files name");

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

void OnDiagnosticStatus(JObject parsed)
{
    var numberFilesRemaining = parsed.SelectToken("$.Body.NumberFilesRemaining")?.Value<int?>();

    if (numberFilesRemaining == null)
        throw new InvalidOperationException("Null files remaining");

    if (numberFilesRemaining == 0)
    {
        Console.WriteLine("Solution Load Time: " + solutionLoadTimer.Elapsed);

        OpenFile();
        ChangeFile();
    }
}

void RequestCodeCheck()
{
    Console.WriteLine($"Code check: {TargetFileName}");

    proc.StandardInput.WriteLine(JsonConvert.SerializeObject(new
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

    proc.StandardInput.WriteLine(JsonConvert.SerializeObject(new
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

    proc.StandardInput.WriteLine(JsonConvert.SerializeObject(new
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

void OnDiagnostic(JObject parsed)
{
    if (!fileChangeDiagnosticTimer.IsRunning)
        return;

    var fileNames = parsed.SelectTokens("$.Body.Results[*].FileName")?.Select(x => x.Value<string?>());

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