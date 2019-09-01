<#
   Date: 07/04/2019
   Purpose: Post Build Script of Dynamo
#>
$ErrorActionPreference = "Stop"

try
{
	docker container stop build-test
	docker container rm build-test
}
catch
{
	Write-Host $error[0]
	throw $LASTEXITCODE
}
finally
{
	docker system prune -f

	$processes = Get-Process "*docker desktop*"
	if ($processes.Count -gt 0)
	{
		$processes[0].Kill()
		$processes[0].WaitForExit()
	}
	Start-Process "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe"
}

