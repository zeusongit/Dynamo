<#
   Date: 07/04/2019
   Purpose: Post Build Script of Dynamo
#>
$ErrorActionPreference = "Stop"

try
{
	docker container stop build-test
	docker container rm build-test
	docker system prune -f
}
catch
{
	Write-Host $error[0]
	throw $LASTEXITCODE
}

