<#
   Date: 07/03/2019
   Purpose: Build Dynamo
#>

docker pull artifactory.dev.adskengineer.net/docker-local-v2/dynamo/buildtools2017sdk81

docker run -m 4GB -d -t --mount type=bind,source=C:\\Jenkins\\workspace\\Dynamo\\Dynamo,target=c:\\WorkspaceDynamo --name slow-test dynamo

docker exec slow-test msbuild c:\\WorkspaceDynamo\\DYN-1822\\src\\build.xml

docker rm -f slow-test