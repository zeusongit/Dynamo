<#
   Date: 07/04/2019
   Purpose: Build Script of Dynamo
#>
Param([string]$Jenkisworkspace = "C:\Jenkins\workspace\Dynamo\Dynamo\DYN-1822")
$ErrorActionPreference = "Stop"

$JOB_NAME = $env:JOB_NAME
$WorkSpace = $env:WORKSPACE

$Jenkisworkspace

$JOB_NAME

$WorkSpace

