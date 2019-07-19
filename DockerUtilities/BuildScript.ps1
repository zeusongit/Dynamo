<#
   Date: 07/04/2019
   Purpose: Build Script of Dynamo
#>
$ErrorActionPreference = "Stop"

try
{
	docker exec build-test msbuild c:\WorkspaceDynamo\DYN-1822\src\build.xml
}
catch
{
	Write-Host $error[0]
	throw $LASTEXITCODE
}