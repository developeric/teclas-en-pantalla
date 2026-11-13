# Abre Teclas compilando Teclas.cs en memoria (Smart App Control bloquea los .exe sin firmar).
# Uso: Abrir-Teclas.ps1          -> se ve en pantalla y en OBS
#      Abrir-Teclas.ps1 -obs     -> solo en OBS, no se ve en tu pantalla
param([switch]$obs)
try {
    $src = Get-Content (Join-Path $PSScriptRoot 'Teclas.cs') -Raw -Encoding UTF8
    Add-Type -TypeDefinition $src -ReferencedAssemblies System.Windows.Forms, System.Drawing -WarningAction SilentlyContinue -ErrorAction Stop
    $t = [AppDomain]::CurrentDomain.GetAssemblies() | ForEach-Object { $_.GetType('Teclas') } | Where-Object { $_ } | Select-Object -Last 1
    $a = [string[]]@(); if ($obs) { $a = [string[]]@('-obs') }
    $t.GetMethod('Main', [Reflection.BindingFlags]'Static,NonPublic,Public').Invoke($null, @(,$a)) | Out-Null
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show("No se pudo abrir Teclas:`n" + $_.Exception.ToString(), 'Teclas') | Out-Null
}
