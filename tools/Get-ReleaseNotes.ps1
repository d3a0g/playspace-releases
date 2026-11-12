[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$Notes='',
    [string]$NotesFile=''
)
$ErrorActionPreference='Stop'
if($Notes -and $NotesFile){throw 'Use either Notes or NotesFile, not both.'}
if(-not $Notes){
    if(-not $NotesFile){$NotesFile=Join-Path (Split-Path -Parent $PSScriptRoot) "docs/releases/$Version.md"}
    if(-not(Test-Path -LiteralPath $NotesFile -PathType Leaf)){throw "Release notes required. Create $NotesFile or pass -NotesFile / -Notes."}
    $Notes=[IO.File]::ReadAllText((Resolve-Path -LiteralPath $NotesFile).Path)
}
$Notes=$Notes.Replace("`r`n","`n").Replace("`r","`n").Trim()
if([string]::IsNullOrWhiteSpace($Notes)){throw 'Release notes must describe the changes; empty notes are not allowed.'}
if($Notes.Length -gt 20000){throw 'Release notes exceed the launcher limit of 20000 characters.'}
return $Notes
