[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$solution = Join-Path $PSScriptRoot 'TestFinance.sln'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK 未找到。请安装 .NET 8 SDK，并确认 dotnet 位于 PATH。'
}

dotnet --info
dotnet restore $solution
dotnet build $solution --configuration $Configuration --no-restore --warnaserror

if (-not $SkipTests) {
    dotnet test $solution --configuration $Configuration --no-build --logger 'console;verbosity=minimal'
}

if ($Publish) {
    foreach ($project in @('Payments.Api', 'Lending.Api', 'Wealth.Api')) {
        dotnet publish (Join-Path $PSScriptRoot "src/$project/$project.csproj") --configuration $Configuration --no-restore --output (Join-Path $PSScriptRoot "artifacts/$project")
    }
}
