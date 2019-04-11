<#
Date: 03/12/2019
Purpose: Invoke testing configuration within the docker container
#>

docker exec localBuildContainer powershell -command "c:\libGCopy\DockerUtilities\runTestsInContainer.ps1"