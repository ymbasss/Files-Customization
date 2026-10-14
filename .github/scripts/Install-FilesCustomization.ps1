# Installs Files Customization from the unzipped release: trusts the package's certificate, then installs the app with
# the dependency packages for this PC. Published in the release zip as Install.ps1.
# Run it from the unzipped folder: right-click Install.ps1 > Run with PowerShell.

$ErrorActionPreference = 'Stop'

try {
	$folder = $PSScriptRoot
	$certificate = Get-ChildItem -LiteralPath $folder -Filter *.cer | Select-Object -First 1
	$bundle = Get-ChildItem -LiteralPath $folder -Filter *.msixbundle | Select-Object -First 1
	if (-not $certificate -or -not $bundle) {
		throw 'The certificate or the package is missing. Unzip the whole download, then run Install.ps1 from that folder.'
	}

	# Windows only installs a self-signed package whose certificate is in Trusted People for the whole PC, which needs
	# administrator rights. Only that step runs as administrator, so the app itself is installed for the current user.
	$thumbprint = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 $certificate.FullName).Thumbprint
	if (-not (Test-Path "Cert:\LocalMachine\TrustedPeople\$thumbprint")) {
		Write-Host 'Trusting the package certificate (Windows asks for administrator permission)...'
		$path = $certificate.FullName.Replace("'", "''")
		$process = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList @(
			'-NoProfile', '-Command',
			"Import-Certificate -FilePath '$path' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null")
		if ($process.ExitCode -ne 0 -or -not (Test-Path "Cert:\LocalMachine\TrustedPeople\$thumbprint")) {
			throw 'The certificate could not be trusted.'
		}
	}

	$architecture = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { 'ARM64' } else { 'x64' }
	$dependencyFolder = Join-Path $folder "Dependencies\$architecture"
	$dependencies = @()
	if (Test-Path -LiteralPath $dependencyFolder) {
		$dependencies = @(Get-ChildItem -LiteralPath $dependencyFolder -File |
			Where-Object { $_.Extension -in '.appx', '.msix' } | ForEach-Object { $_.FullName })
	}

	Write-Host 'Installing Files Customization...'
	if ($dependencies.Count -gt 0) {
		Add-AppxPackage -Path $bundle.FullName -DependencyPath $dependencies -ForceUpdateFromAnyVersion
	}
	else {
		Add-AppxPackage -Path $bundle.FullName -ForceUpdateFromAnyVersion
	}
	Write-Host 'Done. Open "Files - Dev" from the Start menu.' -ForegroundColor Green
}
catch {
	Write-Host "Installation failed: $($_.Exception.Message)" -ForegroundColor Red
}

Read-Host 'Press Enter to close'
