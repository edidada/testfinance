#!/usr/bin/env dotnet-script

using System.Diagnostics;

var configuration = Args.FirstOrDefault(x => x is "Debug" or "Release") ?? "Release";
var skipTests = Args.Contains("--skip-tests", StringComparer.OrdinalIgnoreCase);
var publish = Args.Contains("--publish", StringComparer.OrdinalIgnoreCase);

int RunDotNet(string arguments)
{
    Console.WriteLine($"> dotnet {arguments}");
    using var process = Process.Start(new ProcessStartInfo("dotnet", arguments)
    {
        UseShellExecute = false
    }) ?? throw new InvalidOperationException("无法启动 dotnet。请确认 .NET 8 SDK 已安装且位于 PATH。");
    process.WaitForExit();
    return process.ExitCode;
}

void RequireSuccess(string arguments)
{
    if (RunDotNet(arguments) != 0) Environment.Exit(1);
}

RequireSuccess("restore TestFinance.sln");
RequireSuccess($"build TestFinance.sln --configuration {configuration} --no-restore --warnaserror");

if (!skipTests)
    RequireSuccess($"test TestFinance.sln --configuration {configuration} --no-build --logger \"console;verbosity=minimal\"");

if (publish)
{
    foreach (var project in new[] { "Payments.Api", "Lending.Api", "Wealth.Api" })
        RequireSuccess($"publish src/{project}/{project}.csproj --configuration {configuration} --no-restore --output artifacts/{project}");
}
