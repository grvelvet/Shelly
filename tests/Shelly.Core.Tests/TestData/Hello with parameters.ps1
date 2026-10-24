# Example with parameters: greets someone and counts up to a number.

param(
    [Parameter(Mandatory = $true, HelpMessage = 'Who to greet')]
    [string]$Name,

    [int]$Count = 3,

    [switch]$Loud
)

$greeting = "Hello, $Name!"
if ($Loud) { $greeting = $greeting.ToUpper() }
Write-Host $greeting

for ($i = 1; $i -le $Count; $i++) {
    Write-Host "Step $i of $Count"
    Start-Sleep -Milliseconds 300
}
Write-Host 'Done.'
