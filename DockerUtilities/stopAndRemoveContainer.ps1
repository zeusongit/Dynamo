<#
Date: 03/29/2019
Purpose: Stop and remove docker container
#>

# Stop 'localBuildContainer'
docker stop localBuildContainer

# Remove the previous container so that we start with a fresh container
docker rm localBuildContainer