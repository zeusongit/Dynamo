<#
Date: 03/21/2019
Purpose: Run the ProtoGeometry tests using the NUnit console runner
#>

# Root directory
$ProjectDir = "C:\\libGCopy"

# Mounted host directory
$HostDir = "C:\\libG"

# LibG release bin directory
$OutDir = "$ProjectDir\\bin\\x64\\Release"

# Set the location of the NUnit test runner
$NUnit = "$ProjectDir\\NUnit\\nunit-console.exe"

# ProtoGeometry tests from the LigG bin directory
$tests = "$OutDir\\ProtoGeometry.Tests.dll"

# Run tests (No test should take over 30 seconds - entire suite generally completes in under 1 minute)
# Reference doc for flags: https://github.com/nunit/docs/wiki/Console-Command-Line
& $NUnit /timeout=30000 /noshadow /labels /exclude:Failure,BackwardIncompatible /framework:"net-4.0" /xml:"$HostDir\\NUnitTestResults.xml" $tests
