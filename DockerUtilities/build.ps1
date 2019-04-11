<#
Date: 03/12/2019
Author: Siddhartha Mangarole
Purpose: To build LibG inside a windows docker container
#>

# Stop 'localBuildContainer'
docker stop localBuildContainer

# Remove the previous container so that we start with a fresh container
docker rm localBuildContainer

# One time pull of the docker image
docker pull artifactory.dev.adskengineer.net/docker-local-v2/dynamo/buildtools2017sdk81

# Copy latest pulled LibG to the container
docker run -m 4GB -d -t -v c:\Jenkins\workspace\Dynamo\Dynamo\DYN-1822:c:\Dynamo --name localBuildContainer artifactory.dev.adskengineer.net/docker-local-v2/dynamo/buildtools2017sdk81

# Build LibG
docker exec localBuildContainer msbuild c:\Dynamo\src\Dynamo.All.sln /property:Configuration=Release
