<#
   Date: 07/04/2019
   Purpose: Post Build Script of Dynamo
#>
$ErrorActionPreference = "Stop"

try
{
	docker stop build-test
	docker rm build-test
}
catch
{
	Write-Host $error[0]
	throw $LASTEXITCODE
}

